/*
 * SubSense — tự động gắn phụ đề tiếng Việt cho Lampa Player.
 *
 * Nguồn phụ đề: SubSense (Stremio addon). Plugin lấy danh sách phụ đề theo
 * imdb_id (+ season/episode với series), ưu tiên srt/vtt trực tiếp, giải nén
 * zip bằng JSZip khi cần, chuyển sang VTT và gắn toàn bộ vào player để người
 * dùng chọn bản phù hợp.
 *
 * Đây là plugin gốc của Lampac (LampaWeb), được nạp cùng hệ thống qua
 * /lampainit.js và /on.js khi bật LampaWeb.initPlugins.subsense.
 */
(function() {
  'use strict';

  var SUBSENSE_BASE = 'https://subsense.nepiraw.com/lxolz7e9-%7B%22languages%22%3A%5B%22vi%22%5D%7D';
  var JSZIP_CDN = 'https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js';
  var jszipLoaded = false;
  var lastMovie = null; // cache thông tin phim từ trang chi tiết, để dùng khi player start

  function log() {
    var args = ['[SubSense]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
  }

  function startSubSense() {
    if (startSubSense.done) return;
    startSubSense.done = true;

    // Bọc lại Lampa.Player.play để tự động gắn sub mỗi khi phát video
    var originalPlay = Lampa.Player.play;
    Lampa.Player.play = function(params) {
      var result = originalPlay.apply(this, arguments);

      var movie = (params && params.movie) || lastMovie;
      var season = (params && params.season) || (movie && movie.season) || null;
      var episode = (params && params.episode) || (movie && movie.episode) || null;

      if (movie) {
        attachAutoSub(movie, season, episode);
      } else {
        log('khong xac dinh duoc phim dang phat, bo qua tu dong gan sub');
      }

      return result;
    };

    log('plugin da khoi dong, cho phat phim...');
  }

  function ensureJSZip(callback) {
    if (jszipLoaded || window.JSZip) { jszipLoaded = true; callback(); return; }
    var script = document.createElement('script');
    script.src = JSZIP_CDN;
    script.onload = function() { jszipLoaded = true; callback(); };
    script.onerror = function() { log('khong tai duoc JSZip'); callback(); };
    document.head.appendChild(script);
  }

  function buildId(imdbId, season, episode) {
    if (season && episode) return imdbId + ':' + season + ':' + episode;
    return imdbId;
  }

  function fetchSubs(imdbId, type, season, episode, onSuccess, onError) {
    var id = buildId(imdbId, season, episode);
    var url = SUBSENSE_BASE + '/subtitles/' + (type || 'movie') + '/' + id + '.json';
    $.ajax({
      url: url,
      type: 'GET',
      dataType: 'json',
      success: function(data) { onSuccess(data.subtitles || []); },
      error: function(xhr) { onError && onError(xhr); }
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
        success: function(text) {
          if (type === 'vtt') {
            callback(URL.createObjectURL(new Blob([text], { type: 'text/vtt' })));
          } else {
            callback(makeVttBlobUrl(text));
          }
        },
        error: function() { callback(null, 'tai file that bai'); }
      });
      return;
    }

    if (type === 'zip') {
      ensureJSZip(function() {
        if (!window.JSZip) { callback(null, 'JSZip khong load duoc'); return; }
        $.ajax({
          url: sub.url,
          type: 'GET',
          xhrFields: { responseType: 'arraybuffer' },
          success: function(buf) {
            window.JSZip.loadAsync(buf).then(function(zip) {
              var srtFile = null;
              zip.forEach(function(path, entry) {
                if (!srtFile && /\.srt$/i.test(path)) srtFile = entry;
              });
              if (!srtFile) { callback(null, 'khong co .srt trong zip'); return; }
              srtFile.async('string').then(function(text) {
                callback(makeVttBlobUrl(text));
              });
            }).catch(function() { callback(null, 'giai nen zip that bai'); });
          },
          error: function() { callback(null, 'tai zip that bai'); }
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
      success: function(text) {
        if (typeof text === 'string' && text.indexOf('-->') !== -1) {
          callback(makeVttBlobUrl(text));
        } else {
          callback(null, 'dinh dang khong xac dinh');
        }
      },
      error: function() { callback(null, 'tai file that bai'); }
    });
  }

  // resolve toàn bộ các sub trong danh sách để Lampa Player có thể chọn từng bản
  function autoResolveAll(subs, callback) {
    if (!subs.length) { callback([]); return; }

    var tracks = new Array(subs.length);
    var pending = subs.length;

    subs.forEach(function(sub, index) {
      resolveToVtt(sub, function(vttUrl, err) {
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
          callback(tracks.filter(function(track) {
            return !!track;
          }));
        }
      });
    });
  }

  // ưu tiên sub dạng srt/vtt trực tiếp trước (nhanh, đỡ tốn request giải nén)
  function sortByEase(subs) {
    var order = { srt: 0, vtt: 0, zip: 1, unknown: 2, rar: 3 };
    return subs.slice().sort(function(a, b) {
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
    var imdbId = extractImdbId(movie);
    if (!imdbId) {
      log('khong co imdb_id, bo qua phim:', movie && movie.title);
      return;
    }
    var type = (movie.number_of_seasons || movie.season) ? 'series' : 'movie';

    fetchSubs(imdbId, type, season, episode, function(subs) {
      if (!subs.length) { log('khong co sub cho', imdbId); return; }
      var sorted = sortByEase(subs);
      log('tim thay', subs.length, 'ban sub, dang gan tat ca...');
      autoResolveAll(sorted, function(tracks) {
        if (!tracks.length) { log('khong gan duoc ban nao'); return; }

        Lampa.Player.subtitles(tracks);
        log('da gan', tracks.length, 'ban sub');
      });
    }, function(xhr) {
      log('loi lay danh sach sub:', xhr.status);
    });
  }

  function readyLampacSubSense() {
    if (typeof Lampa === 'undefined' || typeof $ === 'undefined' ||
        !Lampa.Player || !Lampa.Listener) {
      setTimeout(readyLampacSubSense, 250);
      return;
    }

    // Bắt movie card khi mở trang chi tiết phim, để có imdb_id/season/episode sẵn
    Lampa.Listener.follow('full', function(e) {
      if (e.type === 'complite' && e.data && e.data.movie) {
        lastMovie = e.data.movie;
      }
    });

    if (window.appready) {
      startSubSense();
      return;
    }

    Lampa.Listener.follow('app', function(e) {
      if (e.type === 'ready') startSubSense();
    });

    // fallback nếu sự kiện app ready đã bắn trước khi plugin đăng ký
    setTimeout(function checkReady() {
      if (!window.appready) {
        setTimeout(checkReady, 500);
        return;
      }
      startSubSense();
    }, 500);
  }

  readyLampacSubSense();

  window.SubSensePlugin = {
    fetch: fetchSubs,
    resolveToVtt: resolveToVtt,
    attachAutoSub: attachAutoSub
  };
})();
