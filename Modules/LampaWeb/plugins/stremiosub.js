/*
 * StremioSub — tìm & gắn phụ đề từ SubDL + SubSource Stremio addons
 *
 * StremioSub dùng API subtitle chuẩn của hai addon. Danh sách và file phụ đề
 * được cache trong vòng đời trang để một lần phát bị GStreamer đóng/mở lại
 * không bắn lại nhiều request giống hệt nhau (dễ dính rate limit).
 *
 * Nguồn:
 *   - SubDL: https://subdl.strem.top/...
 *   - SubSource: https://subsource.strem.top/...
 */
(function() {
  'use strict';

  // All automatic subtitle providers use the same Player.play hook. Keep one
  // owner even if an older client still has duplicate/raw plugin URLs.
  if (window.__lampacSubtitleAutoOwner) return;
  window.__lampacSubtitleAutoOwner = 'stremiosub';
  window.__lampacStremioSubLoaded = true;

  var SUBDL_BASE = 'https://subdl.strem.top/OUFldGw4WXpzOTBydHVNSGFIWkJTQ0JXNndPZkVYZ0ovVkkvaGlJbmNsdWRlLw';
  var SUBSOURCE_BASE = 'https://subsource.strem.top/c2tfMjNkNDNkOTUxN2RiY2IwM2IzNDNiNzRjNWU4MzgwYjczMmFlMDBlM2U4MDJhYzlkMWY5NjRlMTE5ZDdhNGVkYS92aWV0bmFtZXNlL2hpSW5jbHVkZS90eXBlOjAv';

  var SEARCH_CACHE_TTL = 10 * 60 * 1000;
  var EMPTY_SEARCH_CACHE_TTL = 45 * 1000;
  var RATE_LIMIT_CACHE_TTL = 90 * 1000;
  var TRACK_CACHE_TTL = 30 * 60 * 1000;
  var EMPTY_TRACK_CACHE_TTL = 45 * 1000;
  var ATTACH_RETRY_DELAY = 300;
  var ATTACH_MAX_RETRIES = 30;

  var lastMovie = null;
  var serverOrigin = detectServerOrigin();
  var searchCache = Object.create(null);
  var trackCache = Object.create(null);
  var activePlayback = null;
  var playbackSerial = 0;

  function log() {
    var args = ['[StremioSub]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
  }

  function detectServerOrigin() {
    try {
      if (document.currentScript && document.currentScript.src) {
        var current = originFromUrl(document.currentScript.src);
        if (current) return current;
      }

      var scripts = document.getElementsByTagName('script');
      for (var i = scripts.length - 1; i >= 0; i--) {
        var src = scripts[i].src || '';
        if (/\/stremiosub\.js(?:[?#]|$)/i.test(src)) {
          var origin = originFromUrl(src);
          if (origin) return origin;
        }
      }
    } catch (error) { }

    try {
      if (window.location && /^https?:$/i.test(window.location.protocol))
        return window.location.origin || (window.location.protocol + '//' + window.location.host);
    } catch (error) { }

    return '';
  }

  function originFromUrl(value) {
    try {
      var anchor = document.createElement('a');
      anchor.href = value;
      if (anchor.protocol && anchor.host)
        return anchor.protocol + '//' + anchor.host;
    } catch (error) { }
    return '';
  }

  function isHttpUrl(value) {
    return typeof value === 'string' && /^https?:\/\//i.test(value.trim());
  }

  function compactId(value) {
    return String(value || '').trim().toLowerCase();
  }

  /* ── Guards against stale-movie subtitles ──
   * lastMovie is only a fallback for players that do not pass params.movie
   * (torrents, online.js). SISI/adult players also omit movie, so without
   * these checks the previous film's subtitles get attached to them. */
  function inSisiContext() {
    try {
      var activity = Lampa.Activity && Lampa.Activity.active && Lampa.Activity.active();
      return !!(activity && typeof activity.component === 'string' && activity.component.indexOf('sisi') === 0);
    } catch (error) {
      return false;
    }
  }

  function normalizeTitle(value) {
    return String(value || '')
      .toLowerCase()
      .replace(/[\.\-_:;,!?'"“”‘’\/\\\[\]\(\)\{\}\+]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
  }

  function titleMatchesMovie(movie, playTitle) {
    var play = normalizeTitle(playTitle);
    if (!play) return true; // no title to compare — keep legacy behaviour

    var candidates = [movie.title, movie.name, movie.original_title, movie.original_name];
    for (var i = 0; i < candidates.length; i++) {
      var candidate = normalizeTitle(candidates[i]);
      if (candidate && candidate.length >= 2 && (play.indexOf(candidate) >= 0 || candidate.indexOf(play) >= 0))
        return true;
    }
    return false;
  }

  function resolvePlaybackMovie(params) {
    if (params && params.movie) return params.movie;
    if (!lastMovie) return null;
    if (inSisiContext()) {
      log('adult/SISI playback — skipping lastMovie fallback');
      return null;
    }
    if (params && !titleMatchesMovie(lastMovie, params.title)) {
      log('play title does not match lastMovie — skipping subtitles for', params.title);
      return null;
    }
    return lastMovie;
  }

  function positiveNumber(value) {
    var number = parseInt(value, 10);
    return isFinite(number) && number > 0 ? number : null;
  }

  function extractImdbId(movie) {
    if (!movie) return null;
    var seen = [];

    function scan(obj, depth) {
      if (!obj || typeof obj !== 'object' || depth > 4) return null;
      if (seen.indexOf(obj) >= 0) return null;
      seen.push(obj);

      for (var key in obj) {
        if (!Object.prototype.hasOwnProperty.call(obj, key)) continue;
        var value = obj[key];
        if (/imdb/i.test(key) && typeof value === 'string' && /^tt\d+$/.test(value.trim()))
          return value.trim();
        if (value && typeof value === 'object') {
          var found = scan(value, depth + 1);
          if (found) return found;
        }
      }
      return null;
    }

    return scan(movie, 0);
  }

  function mediaType(movie) {
    return movie && (
      movie.number_of_seasons ||
      movie.first_air_date ||
      movie.season ||
      movie.original_name
    ) ? 'series' : 'movie';
  }

  function playbackInfo(movie, season, episode) {
    var imdbId = extractImdbId(movie);
    if (!imdbId) return null;

    var type = mediaType(movie);
    var normalizedSeason = positiveNumber(
      season !== null && season !== undefined ? season : movie.season
    );
    var normalizedEpisode = positiveNumber(
      episode !== null && episode !== undefined ? episode : movie.episode
    );

    return {
      movie: movie,
      imdbId: imdbId,
      type: type,
      season: normalizedSeason,
      episode: normalizedEpisode,
      key: [
        compactId(imdbId),
        type,
        normalizedSeason || 0,
        normalizedEpisode || 0
      ].join('|')
    };
  }

  function formatEpisode(info) {
    return info.season && info.episode
      ? 'S' + info.season + 'E' + info.episode
      : '';
  }

  function stremioId(info) {
    return info.season && info.episode
      ? info.imdbId + ':' + info.season + ':' + info.episode
      : info.imdbId;
  }

  function cloneTracks(tracks) {
    return (tracks || []).map(function(track) {
      return {
        url: track.url,
        label: track.label,
        language: track.language
      };
    });
  }

  function subtitleText(text) {
    if (typeof text !== 'string' || text.length < 10) return false;
    // SubDL/SubSource return SRT or WebVTT. Reject HTML/error pages, which
    // otherwise would be converted into a bogus subtitle Blob.
    if (!/-->/.test(text) && !/^\s*WEBVTT\b/i.test(text)) return false;
    return true;
  }

  function srtToVtt(srt) {
    return 'WEBVTT\n\n' + String(srt || '')
      .replace(/^\uFEFF/, '')
      .replace(/\r+/g, '')
      .replace(/^\d+\s*$/gm, '')
      .replace(/(\d{2}:\d{2}:\d{2}),(\d{3})/g, '$1.$2');
  }

  function makeVttBlob(text) {
    var clean = String(text || '').replace(/^\uFEFF/, '');
    var vtt = /^\s*WEBVTT\b/i.test(clean)
      ? clean.replace(/^\s+/, '')
      : srtToVtt(clean);
    return URL.createObjectURL(new Blob([vtt], { type: 'text/vtt' }));
  }

  function errorInfo(xhr, status, error) {
    var code = xhr && Number(xhr.status) ? Number(xhr.status) : 0;
    var reason = code
      ? 'HTTP ' + code
      : (status || error || 'network/CORS');
    return {
      status: code,
      reason: String(reason)
    };
  }

  /* ── Search one Stremio addon ── */
  function searchAddon(sourceName, base, info, callback) {
    var id = stremioId(info);
    var url = base + '/subtitles/' + info.type + '/' + encodeURIComponent(id) + '.json';

    $.ajax({
      url: url,
      type: 'GET',
      dataType: 'json',
      timeout: 15000,
      success: function(data) {
        var subs = data && data.subtitles;
        if (!Array.isArray(subs) && Array.isArray(data)) subs = data;
        if (!Array.isArray(subs)) subs = [];

        var addonError = data && data.error ? String(data.error) : '';
        callback(subs, addonError ? { status: 0, reason: addonError } : null);
      },
      error: function(xhr, status, error) {
        callback([], errorInfo(xhr, status, error));
      }
    });
  }

  function uniqueSubtitles(items) {
    var result = [];
    var seen = Object.create(null);

    (items || []).forEach(function(sub) {
      if (!sub || typeof sub !== 'object') return;
      var url = sub.url || sub.file || sub.link || sub.download_url || '';
      if (!isHttpUrl(url)) return;

      var id = String(sub.id || sub.label || url);
      var key = url + '|' + id;
      if (seen[key]) return;
      seen[key] = true;

      result.push({
        id: sub.id || sub.label || '',
        url: url,
        lang: sub.lang || sub.language || 'vi'
      });
    });

    return result;
  }

  /* ── Search AIOStreams' standard subtitle resource ── */
  function searchAio(info, callback) {
    if (!serverOrigin) {
      callback(false, [], null);
      return;
    }

    var query =
      '?stremio_id=' + encodeURIComponent(stremioId(info)) +
      '&serial=' + (info.type === 'series' ? '1' : '0');

    if (info.season) query += '&s=' + info.season;
    if (info.episode) query += '&e=' + info.episode;

    $.ajax({
      url: serverOrigin + '/lite/aiostreams/subtitles' + query,
      type: 'GET',
      dataType: 'json',
      timeout: 20000,
      success: function(data) {
        var subs = data && data.subtitles;
        if (!Array.isArray(subs)) {
          callback(false, [], { status: 0, reason: 'invalid AIOStreams subtitle response' });
          return;
        }
        callback(true, subs, null);
      },
      error: function(xhr, status, error) {
        // A disabled/unconfigured AIOStreams module falls back to the two
        // legacy providers. A successful empty JSON response is different: it
        // means AIO is enabled and configured but found no subtitle.
        callback(false, [], errorInfo(xhr, status, error));
      }
    });
  }

  /*
   * One search per title/episode at a time. GStreamer may call Player.play
   * once for the aborted native player and again for the real HLS player; both
   * calls join this entry instead of producing four identical API requests.
   * When AIOStreams is configured, it is the single subtitle source. The
   * direct SubDL + SubSource path remains a backwards-compatible fallback.
   */
  function searchBoth(info, callback) {
    var now = Date.now();
    var entry = searchCache[info.key];

    if (entry && entry.state === 'ready' && now < entry.expiresAt) {
      callback(entry.subs.slice(), entry.errors || []);
      return;
    }

    if (entry && entry.state === 'empty' && now < entry.expiresAt) {
      callback([], entry.errors || []);
      return;
    }

    if (entry && entry.state === 'loading') {
      entry.waiters.push(callback);
      return;
    }

    entry = {
      state: 'loading',
      startedAt: now,
      waiters: [callback],
      subs: [],
      errors: []
    };
    searchCache[info.key] = entry;

    function publish(allSubs, errors) {
      var merged = uniqueSubtitles(allSubs);
      var hadRateLimit = (errors || []).some(function(item) {
        return item.info && (item.info.status === 429 || item.info.status === 403);
      });

      entry.subs = merged;
      entry.errors = errors || [];
      entry.state = merged.length ? 'ready' : 'empty';
      entry.expiresAt = now + (
        merged.length
          ? SEARCH_CACHE_TTL
          : hadRateLimit ? RATE_LIMIT_CACHE_TTL : EMPTY_SEARCH_CACHE_TTL
      );

      var waiters = entry.waiters.slice();
      entry.waiters.length = 0;
      waiters.forEach(function(waiter) {
        waiter(merged.slice(), entry.errors);
      });

      if (!merged.length)
        log('no subs found for', info.imdbId, formatEpisode(info));
    }

    function searchLegacy() {
      var pending = 2;
      var allSubs = [];
      var errors = [];

      function finish(sourceName, subs, error) {
        if (error) errors.push({ source: sourceName, info: error });
        log(sourceName + ':', subs.length, 'subs', error ? '(' + error.reason + ')' : '');
        allSubs = allSubs.concat(subs || []);
        pending--;
        if (pending === 0) publish(allSubs, errors);
      }

      searchAddon('SubDL', SUBDL_BASE, info, finish.bind(null, 'SubDL'));
      searchAddon('SubSource', SUBSOURCE_BASE, info, finish.bind(null, 'SubSource'));
    }

    searchAio(info, function(used, subs, error) {
      if (used) {
        log('AIOStreams:', subs.length, 'subs');
        publish(subs, error ? [{ source: 'AIOStreams', info: error }] : []);
        return;
      }

      searchLegacy();
    });
  }

  function fetchSubtitleText(url, requestUrl, callback) {
    $.ajax({
      url: requestUrl,
      type: 'GET',
      dataType: 'text',
      timeout: 20000,
      success: function(text) {
        if (subtitleText(text)) {
          callback(text, null);
        } else {
          callback(null, { status: 0, reason: 'invalid subtitle response' });
        }
      },
      error: function(xhr, status, error) {
        callback(null, errorInfo(xhr, status, error));
      }
    });
  }

  /* ── Download subtitle file; use Lampac proxy only as a CORS fallback ── */
  function downloadSub(url, callback) {
    fetchSubtitleText(url, url, function(text, directError) {
      if (text) {
        callback(text, null);
        return;
      }

      if (!serverOrigin) {
        callback(null, directError);
        return;
      }

      var proxyUrl = serverOrigin + '/subfinder/file?url=' + encodeURIComponent(url);
      log('direct subtitle download failed; trying Lampac proxy', directError && directError.reason);
      fetchSubtitleText(url, proxyUrl, function(proxyText, proxyError) {
        callback(proxyText, proxyText ? null : (proxyError || directError));
      });
    });
  }

  function resolveTracks(info, subs, callback) {
    var now = Date.now();
    var entry = trackCache[info.key];

    if (entry && entry.state === 'ready' && now < entry.expiresAt) {
      callback(cloneTracks(entry.tracks));
      return;
    }

    if (entry && entry.state === 'empty' && now < entry.expiresAt) {
      callback([]);
      return;
    }

    if (entry && entry.state === 'loading') {
      entry.waiters.push(callback);
      return;
    }

    entry = {
      state: 'loading',
      startedAt: now,
      waiters: [callback],
      tracks: []
    };
    trackCache[info.key] = entry;

    if (!subs || !subs.length) {
      complete([]);
      return;
    }

    var pending = subs.length;
    var tracks = new Array(subs.length);

    subs.forEach(function(sub, index) {
      downloadSub(sub.url, function(text, error) {
        if (text) {
          tracks[index] = {
            url: makeVttBlob(text),
            label: sub.id || 'Sub ' + (index + 1),
            language: sub.lang || 'vi'
          };
        } else if (error) {
          log('subtitle file failed:', error.reason);
        }

        pending--;
        if (pending === 0) complete(tracks.filter(function(track) { return !!track; }));
      });
    });

    function complete(resolved) {
      entry.tracks = resolved || [];
      entry.state = entry.tracks.length ? 'ready' : 'empty';
      entry.expiresAt = Date.now() + (
        entry.tracks.length ? TRACK_CACHE_TTL : EMPTY_TRACK_CACHE_TTL
      );

      log('resolved', entry.tracks.length, '/', subs.length, 'subtitle files');
      var waiters = entry.waiters.slice();
      entry.waiters.length = 0;
      waiters.forEach(function(waiter) {
        waiter(cloneTracks(entry.tracks));
      });
    }
  }

  function isActive(state) {
    return !!state && activePlayback === state && !state.destroyed;
  }

  function scheduleAttach(state) {
    if (!isActive(state) || state.attachTimer) return;
    if (state.attachRetries >= ATTACH_MAX_RETRIES) {
      log('player is not ready; subtitle attach cancelled');
      return;
    }

    state.attachTimer = setTimeout(function() {
      state.attachTimer = null;
      state.attachRetries++;
      tryAttach(state);
    }, ATTACH_RETRY_DELAY);
  }

  function tryAttach(state) {
    if (!isActive(state) || state.attached || !state.tracks || !state.tracks.length) return;
    if (!Lampa.Player || typeof Lampa.Player.subtitles !== 'function') return;

    var opened = typeof Lampa.Player.opened !== 'function' || Lampa.Player.opened();
    if (!opened) {
      // This is often the short gap between GStreamer aborting the original
      // player and Lampa opening the real HLS player. Do not throw the tracks
      // away; wait for ready/reopen instead.
      scheduleAttach(state);
      return;
    }

    try {
      Lampa.Player.subtitles(cloneTracks(state.tracks));
      state.attached = true;
      log('attached', state.tracks.length, 'subs to player');
    } catch (error) {
      if (state.attachRetries < ATTACH_MAX_RETRIES) {
        scheduleAttach(state);
      } else {
        log('bo qua gan sub vi player khong con san sang:', error.message || error);
      }
    }
  }

  function loadForPlayback(state) {
    if (!state || !state.info) return;

    searchBoth(state.info, function(subs) {
      if (!subs.length) return;

      resolveTracks(state.info, subs, function(tracks) {
        if (!tracks.length) return;
        state.tracks = tracks;
        tryAttach(state);
      });
    });
  }

  function beginPlayback(info, data) {
    var state = {
      id: ++playbackSerial,
      info: info,
      data: data,
      tracks: null,
      attached: false,
      attachRetries: 0,
      attachTimer: null,
      destroyed: false
    };

    if (activePlayback && activePlayback.attachTimer)
      clearTimeout(activePlayback.attachTimer);
    if (activePlayback) activePlayback.destroyed = true;
    activePlayback = state;
    return state;
  }

  /* ── Hook into Lampa ── */
  function start() {
    if (start.done) return;
    start.done = true;

    var origPlay = Lampa.Player.play;
    Lampa.Player.play = function(params) {
      var movie = resolvePlaybackMovie(params);
      var season = params && params.season !== undefined
        ? params.season
        : movie && movie.season;
      var episode = params && params.episode !== undefined
        ? params.episode
        : movie && movie.episode;
      var info = movie ? playbackInfo(movie, season, episode) : null;
      var state;

      if (info) {
        state = beginPlayback(info, params);
      } else {
        // New playback with no identifiable movie: drop the previous state so
        // the 'ready' listener cannot resurrect the last film's subtitles.
        state = null;
        if (activePlayback) {
          if (activePlayback.attachTimer) clearTimeout(activePlayback.attachTimer);
          activePlayback.destroyed = true;
          activePlayback = null;
        }
      }

      var result;
      try {
        result = origPlay.apply(this, arguments);
      } finally {
        if (state) loadForPlayback(state);
      }
      return result;
    };

    if (Lampa.Player.listener && typeof Lampa.Player.listener.follow === 'function') {
      Lampa.Player.listener.follow('ready', function(e) {
        var data = e && e.data;
        var movie = data && data.movie;
        var info = playbackInfo(
          movie || (activePlayback && activePlayback.info.movie),
          data && data.season !== undefined
            ? data.season
            : activePlayback && activePlayback.info.season,
          data && data.episode !== undefined
            ? data.episode
            : activePlayback && activePlayback.info.episode
        );

        if (!info) return;

        if (!activePlayback || activePlayback.info.key !== info.key)
          beginPlayback(info, data);

        activePlayback.data = data || activePlayback.data;
        activePlayback.ready = true;
        tryAttach(activePlayback);
      });

      Lampa.Player.listener.follow('destroy', function() {
        if (!activePlayback) return;
        activePlayback.destroyed = true;
        if (activePlayback.attachTimer) clearTimeout(activePlayback.attachTimer);
        activePlayback.attachTimer = null;
        activePlayback = null;
      });
    }

    log('plugin ready');
  }

  function waitForLampa() {
    if (typeof Lampa === 'undefined' || typeof $ === 'undefined' || !Lampa.Player || !Lampa.Listener) {
      setTimeout(waitForLampa, 300);
      return;
    }

    Lampa.Listener.follow('full', function(e) {
      if (e.type === 'complite' && e.data && e.data.movie) lastMovie = e.data.movie;
    });

    if (window.appready) {
      start();
      return;
    }

    Lampa.Listener.follow('app', function(e) {
      if (e.type === 'ready') start();
    });
    setTimeout(function check() {
      if (!window.appready) {
        setTimeout(check, 500);
        return;
      }
      start();
    }, 500);
  }

  waitForLampa();
})();
