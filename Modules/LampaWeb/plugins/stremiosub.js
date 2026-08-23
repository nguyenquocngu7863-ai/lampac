/*
 * StremioSub — tìm & gắn phụ đề từ SubDL + SubSource Stremio addons
 *
 * Không cần API key, không cần proxy. Tải trực tiếp từ browser vì
 * cả 2 Stremio addon đều có CORS access-control-allow-origin: *
 *
 * Nguồn:
 *   - SubDL: https://subdl.strem.top/...
 *   - SubSource: https://subsource.strem.top/...
 */
(function() {
  'use strict';

  // The plugin may be present in an older client's stored extension list more
  // than once.  Only one hook may wrap Lampa.Player.play.
  if (window.__lampacStremioSubLoaded) return;
  window.__lampacStremioSubLoaded = true;

  var SUBDL_BASE = 'https://subdl.strem.top/OUFldGw4WXpzOTBydHVNSGFIWkJTQ0JXNndPZkVYZ0ovVkkvaGlJbmNsdWRlLw';
  var SUBSOURCE_BASE = 'https://subsource.strem.top/c2tfMjNkNDNkOTUxN2RiY2IwM2IzNDNiNzRjNWU4MzgwYjczMmFlMDBlM2U4MDJhYzlkMWY5NjRlMTE5ZDdhNGVkYS92aWV0bmFtZXNlL2hpSW5jbHVkZS90eXBlOjAv';
  var lastMovie = null;

  function log() {
    var args = ['[StremioSub]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
  }

  /* ── Helpers ── */
  function extractImdbId(movie) {
    if (!movie) return null;
    var seen = [];
    function scan(obj, d) {
      if (!obj || typeof obj !== 'object' || d > 4) return null;
      if (seen.indexOf(obj) >= 0) return null;
      seen.push(obj);
      for (var k in obj) {
        if (!Object.prototype.hasOwnProperty.call(obj, k)) continue;
        var v = obj[k];
        if (/imdb/i.test(k) && typeof v === 'string' && /^tt\d+$/.test(v.trim())) return v.trim();
        if (v && typeof v === 'object') { var f = scan(v, d + 1); if (f) return f; }
      }
      return null;
    }
    return scan(movie, 0);
  }

  function srtToVtt(srt) {
    return 'WEBVTT\n\n' + srt
      .replace(/\r+/g, '')
      .replace(/^\d+\s*$/gm, '')
      .replace(/(\d{2}:\d{2}:\d{2}),(\d{3})/g, '$1.$2');
  }

  function makeVttBlob(text) {
    var vtt = text.indexOf('WEBVTT') === 0 ? text : srtToVtt(text);
    return URL.createObjectURL(new Blob([vtt], { type: 'text/vtt' }));
  }

  /* ── Search from Stremio addon ── */
  function searchAddon(base, imdbId, type, season, episode, callback) {
    var id = season && episode ? imdbId + ':' + season + ':' + episode : imdbId;
    var stremioType = (type === 'series') ? 'series' : 'movie';
    var url = base + '/subtitles/' + stremioType + '/' + encodeURIComponent(id) + '.json';

    $.ajax({
      url: url, type: 'GET', dataType: 'json', timeout: 15000,
      success: function(data) {
        var subs = data.subtitles || [];
        callback(subs);
      },
      error: function() { callback([]); }
    });
  }

  /* ── Download subtitle file directly from browser ── */
  function downloadSub(url, callback) {
    $.ajax({
      url: url, type: 'GET', dataType: 'text', timeout: 20000,
      success: function(text) {
        if (typeof text === 'string' && text.length > 10) {
          callback(text);
        } else {
          callback(null);
        }
      },
      error: function() { callback(null); }
    });
  }

  /* ── Main: search both sources, download all, attach to player ── */
  function attachSubs(movie, season, episode) {
    var imdbId = extractImdbId(movie);
    if (!imdbId) { log('no imdb_id, skip'); return; }

    var type = (movie.number_of_seasons || movie.first_air_date) ? 'series' : 'movie';
    log('searching for', imdbId, type, season ? 'S' + season + 'E' + episode : '');

    var pending = 2;
    var allSubs = [];

    function done() {
      pending--;
      if (pending > 0) return;

      if (!allSubs.length) { log('no subs found'); return; }

      log('found', allSubs.length, 'subs total, downloading...');
      var downloadPending = allSubs.length;
      var tracks = [];

      allSubs.forEach(function(sub) {
        downloadSub(sub.url, function(text) {
          if (text) {
            tracks.push({
              url: makeVttBlob(text),
              label: sub.id || sub.label || 'Sub ' + (tracks.length + 1),
              language: sub.lang || 'vi'
            });
          }
          downloadPending--;
          if (downloadPending === 0 && tracks.length) {
            log('attached', tracks.length, 'subs to player');
            try { Lampa.Player.subtitles(tracks); } catch(e) { log('player error:', e.message); }
          }
        });
      });
    }

    // Search SubDL
    searchAddon(SUBDL_BASE, imdbId, type, season, episode, function(subs) {
      log('SubDL:', subs.length, 'subs');
      allSubs = allSubs.concat(subs);
      done();
    });

    // Search SubSource
    searchAddon(SUBSOURCE_BASE, imdbId, type, season, episode, function(subs) {
      log('SubSource:', subs.length, 'subs');
      allSubs = allSubs.concat(subs);
      done();
    });
  }

  /* ── Hook into Lampa ── */
  function start() {
    if (start.done) return;
    start.done = true;

    var origPlay = Lampa.Player.play;
    Lampa.Player.play = function(params) {
      var result = origPlay.apply(this, arguments);
      var movie = (params && params.movie) || lastMovie;
      var season = (params && params.season) || (movie && movie.season) || null;
      var episode = (params && params.episode) || (movie && movie.episode) || null;
      if (movie) attachSubs(movie, season, episode);
      return result;
    };

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

    if (window.appready) { start(); return; }
    Lampa.Listener.follow('app', function(e) { if (e.type === 'ready') start(); });
    setTimeout(function check() { if (!window.appready) { setTimeout(check, 500); return; } start(); }, 500);
  }

  waitForLampa();
})();
