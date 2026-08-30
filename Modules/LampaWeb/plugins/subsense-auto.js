(function () {
  'use strict';

  // All subtitle helpers hook the same Lampa.Player.play method. A stale
  // client can keep more than one URL in its plugin registry, so use one
  // process-wide owner flag shared with subsense.js, subfinder.js and
  // stremiosub.js. The server also selects only one provider, but this guard
  // protects manual/raw-URL installs too.
  if (window.__lampacSubtitleAutoOwner) return;
  window.__lampacSubtitleAutoOwner = 'subsense-auto';

  var SUBSENSE_BASE = 'https://subsense.nepiraw.com/bu8e9cfy-ZSnP5mVsCkziQ-DtDwHymaozxOkAUjM0O4fIZCp0Y3wd4behaNV8shVg6U5yv7RhcX7O4Rpi6xee5TQdKCetjN8wmBS-xeADnVc0LVN5jpYQmFyWOyMW7WTVxw04MHCYjyE6XHlc3Jwb1gSm6tLiMsIkGAE85EcfaM_EdeNl472Wr8knESDBNY52CDi0bJKFZ5dOZ5dgN-KhoC2LCSo-mwrKxzUt4GdnbkuzMgeFxntuHVBe2DYJhWIFkNCb4CKsEpOp9TAsifN_Jg';
  var JSZIP_CDN = 'https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js';
  var jszipLoaded = false;
  var lastMovie = null; // cache thông tin phim từ trang chi tiết, để dùng khi player start
  var playbackSerial = 0; // tăng mỗi lần Player.play — chặn sub tải trễ gắn vào phiên phát mới

  function log() {
    var args = ['[SubSense]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
  }

  /* ── Guard chống sub phim cũ ──
   * lastMovie chỉ là fallback khi player không truyền params.movie
   * (torrent, online.js). Player SISI/adult cũng không truyền movie, nên
   * không có guard này thì sub của phim TRƯỚC sẽ bị gắn vào clip đang xem. */
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
    if (!play) return true; // không có tiêu đề để so — giữ hành vi cũ

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
      log('phat noi dung SISI/adult — khong dung lastMovie, bo qua sub');
      return null;
    }
    if (params && !titleMatchesMovie(lastMovie, params.title)) {
      log('tieu de dang phat khong khop phim truoc — bo qua sub:', params.title);
      return null;
    }
    return lastMovie;
  }

  function setSubtitlesSafely(tracks, serial) {
    try {
      if (serial !== undefined && serial !== playbackSerial) {
        log('sub tai xong sau khi da chuyen phim khac, bo qua');
        return;
      }
      if (!Lampa.Player || typeof Lampa.Player.subtitles !== 'function') return;
      if (typeof Lampa.Player.opened === 'function' && !Lampa.Player.opened()) {
        log('player da dong truoc khi sub tai xong, bo qua');
        return;
      }
      Lampa.Player.subtitles(tracks);
      log('da gan', tracks.length, 'ban sub');
    } catch (error) {
      // The subtitle request is asynchronous; the user may close the player
      // before it completes and Lampa then has no internal _video object.
      log('bo qua gan sub vi player khong con san sang:', error.message || error);
    }
  }

  function ensureJSZip(callback) {
    if (jszipLoaded || window.JSZip) { jszipLoaded = true; callback(); return; }
    var script = document.createElement('script');
    script.src = JSZIP_CDN;
    script.onload = function () { jszipLoaded = true; callback(); };
    script.onerror = function () { log('khong tai duoc JSZip'); callback(); };
    document.head.appendChild(script);
  }

  function buildId(imdbId, season, episode) {
    if (season && episode) return imdbId + ':' + season + ':' + episode;
    return imdbId;
  }

  function fetchSubs(imdbId, type, season, episode, onSuccess, onError) {
    var id = buildId(imdbId, season, episode);
    var url = SUBSENSE_BASE + '/subtitles/' + (type || 'movie') + '/' + encodeURIComponent(id) + '.json';
    $.ajax({
      url: url,
      type: 'GET',
      dataType: 'json',
      timeout: 15000,
      success: function (data) { onSuccess(data && data.subtitles || []); },
      error: function (xhr, status, error) {
        onError && onError({
          status: xhr && xhr.status ? xhr.status : 0,
          statusText: status || error || 'network/CORS or upstream unavailable'
        });
      }
    });
  }

  function detectFileType(url) {
    var clean = decodeURIComponent(url.split('?')[0]);
    if (/\.zip$/i.test(clean)) return 'zip';
    if (/\.rar$/i.test(clean)) return 'rar';
    if (/\.srt$/i.test(clean)) return 'srt';
    if (/\.vtt$/i.test(clean)) return 'vtt';
    return 'unknown';
  }

  function srtToVtt(srtText) {
    return 'WEBVTT\n\n' + srtText
      .replace(/\r+/g, '')
      .replace(/^\d+\s*$/gm, '')
      .replace(/(\d{2}:\d{2}:\d{2}),(\d{3})/g, '$1.$2');
  }

  function makeVttBlobUrl(srtText) {
    var blob = new Blob([srtToVtt(srtText)], { type: 'text/vtt' });
    return URL.createObjectURL(blob);
  }

  function resolveToVtt(sub, callback) {
    var type = detectFileType(sub.url);

    if (type === 'srt' || type === 'vtt') {
      $.ajax({
        url: sub.url,
        type: 'GET',
        success: function (text) {
          if (type === 'vtt') {
            callback(URL.createObjectURL(new Blob([text], { type: 'text/vtt' })));
          } else {
            callback(makeVttBlobUrl(text));
          }
        },
        error: function (xhr, s) { callback(null, 'tai file that bai (HTTP ' + (xhr ? xhr.status : s) + ')'); }
      });
      return;
    }

    if (type === 'zip') {
      ensureJSZip(function () {
        if (!window.JSZip) { callback(null, 'JSZip khong load duoc'); return; }
        $.ajax({
          url: sub.url,
          type: 'GET',
          xhrFields: { responseType: 'arraybuffer' },
          success: function (buf) {
            window.JSZip.loadAsync(buf).then(function (zip) {
              var srtFile = null;
              zip.forEach(function (path, entry) {
                if (!srtFile && /\.srt$/i.test(path)) srtFile = entry;
              });
              if (!srtFile) { callback(null, 'khong co .srt trong zip'); return; }
              srtFile.async('string').then(function (text) {
                callback(makeVttBlobUrl(text));
              });
            }).catch(function () { callback(null, 'giai nen zip that bai'); });
          },
          error: function () { callback(null, 'tai zip that bai'); }
        });
      });
      return;
    }

    if (type === 'rar') {
      callback(null, 'dinh dang rar chua ho tro');
      return;
    }

    $.ajax({
      url: sub.url,
      type: 'GET',
      success: function (text) {
        if (typeof text === 'string' && text.indexOf('-->') !== -1) {
          callback(makeVttBlobUrl(text));
        } else {
          callback(null, 'dinh dang khong xac dinh');
        }
      },
      error: function () { callback(null, 'tai file that bai'); }
    });
  }

  // resolve toàn bộ các sub trong danh sách để Lampa Player có thể chọn từng bản
  function autoResolveAll(subs, callback) {
    if (!subs.length) { callback([]); return; }

    var tracks = new Array(subs.length);
    var pending = subs.length;

    subs.forEach(function (sub, index) {
      resolveToVtt(sub, function (vttUrl, err) {
        if (vttUrl) {
          tracks[index] = {
            url: vttUrl,
            label: sub.label,
            language: sub.language || 'vi'
          };
        } else {
          log('bo qua sub', sub.label, '-', err);
        }

        pending--;
        if (pending === 0) {
          callback(tracks.filter(function (track) {
            return !!track;
          }));
        }
      });
    });
  }

  // ưu tiên sub dạng srt/vtt trực tiếp trước (nhanh, đỡ tốn request giải nén)
  function sortByEase(subs) {
    var order = { srt: 0, vtt: 0, zip: 1, unknown: 2, rar: 3 };
    return subs.slice().sort(function (a, b) {
      return order[detectFileType(a.url)] - order[detectFileType(b.url)];
    });
  }

  function extractImdbId(movie) {
    if (!movie) return null;
    if (movie.imdb_id) return movie.imdb_id;
    if (movie.external_ids && movie.external_ids.imdb_id) return movie.external_ids.imdb_id;
    if (typeof movie.id === 'string' && /^tt\d+/.test(movie.id)) return movie.id;
    return null;
  }

  function attachAutoSub(movie, season, episode) {
    var serial = playbackSerial;
    var imdbId = extractImdbId(movie);
    if (!imdbId) {
      log('khong co imdb_id, bo qua phim:', movie && movie.title);
      return;
    }
    var type = (movie.number_of_seasons || movie.season) ? 'series' : 'movie';

    fetchSubs(imdbId, type, season, episode, function (subs) {
      if (!subs.length) { log('khong co sub cho', imdbId); return; }
      var sorted = sortByEase(subs);
      log('tim thay', subs.length, 'ban sub, dang gan tat ca...');
      autoResolveAll(sorted, function (tracks) {
        if (!tracks.length) { log('khong gan duoc ban nao'); return; }

        setSubtitlesSafely(tracks, serial);
      });
    }, function (xhr) {
      var code = xhr && xhr.status ? xhr.status : 0;
      var reason = xhr && xhr.statusText ? xhr.statusText : 'network/CORS or upstream unavailable';
      log('loi lay danh sach sub:', code ? 'HTTP ' + code : reason);
    });
  }

  // Bắt movie card khi mở trang chi tiết phim, để có imdb_id/season/episode sẵn
  Lampa.Listener.follow('full', function (e) {
    if (e.type === 'complite' && e.data && e.data.movie) {
      lastMovie = e.data.movie;
    }
  });

  // Bọc lại Lampa.Player.play để tự động gắn sub mỗi khi phát video
  var originalPlay = Lampa.Player.play;
  Lampa.Player.play = function (params) {
    playbackSerial++; // phiên phát mới — vô hiệu mọi request sub còn treo
    var result = originalPlay.apply(this, arguments);

    var movie = resolvePlaybackMovie(params);
    var season = (params && params.season) || (movie && movie.season) || null;
    var episode = (params && params.episode) || (movie && movie.episode) || null;

    if (movie) {
      attachAutoSub(movie, season, episode);
    } else {
      log('khong xac dinh duoc phim dang phat, bo qua tu dong gan sub');
    }

    return result;
  };

  log('plugin da load (v20260830 stale-sub-fix), cho phat phim...');

  window.SubSensePlugin = {
    fetch: fetchSubs,
    resolveToVtt: resolveToVtt,
    attachAutoSub: attachAutoSub
  };
})();
