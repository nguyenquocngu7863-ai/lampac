/*
 * SubSense — tự động gắn phụ đề tiếng Việt cho Lampa Player.
 *
 * Nguồn phụ đề: SubSense (Stremio addon). Plugin lấy danh sách phụ đề theo
 * imdb_id (+ season/episode với series), ưu tiên srt/vtt trực tiếp, giải nén
 * zip bằng JSZip khi cần, chuyển sang VTT và gắn toàn bộ vào player để người
 * dùng chọn bản phù hợp.
 *
 * Xử lý bền bỉ hơn bản đầu: chấp nhận mọi dạng response của addon
 * (subtitles/results/result/mảng thuần), chuẩn hoá trường URL của từng bản
 * (url/file/link/download_url/download) và với link không có đuôi file
 * (điển hình là OpenSubtitles) thì thử đọc text trước rồi thử giải nén zip.
 *
 * Đây là plugin gốc của Lampac (LampaWeb), được nạp cùng hệ thống qua
 * /lampainit.js và /on.js khi bật LampaWeb.initPlugins.subsense.
 */
(function() {
  'use strict';

  // SubSense, SubFinder, StremioSub and the optional root auto helper all
  // wrap Lampa.Player.play. Only one automatic subtitle provider may own that
  // hook at a time, including when an old raw URL remains in Lampa storage.
  if (window.__lampacSubtitleAutoOwner) return;
  window.__lampacSubtitleAutoOwner = 'subsense';

  var SUBSENSE_BASE = 'https://subsense.nepiraw.com/bu8e9cfy-ZSnP5mVsCkziQ-DtDwHymaozxOkAUjM0O4fIZCp0Y3wd4behaNV8shVg6U5yv7RhcX7O4Rpi6xee5TQdKCetjN8wmBS-xeADnVc0LVN5jpYQmFyWOyMW7WTVxw04MHCYjyE6XHlc3Jwb1gSm6tLiMsIkGAE85EcfaM_EdeNl472Wr8knESDBNY52CDi0bJKFZ5dOZ5dgN-KhoC2LCSo-mwrKxzUt4GdnbkuzMgeFxntuHVBe2DYJhWIFkNCb4CKsEpOp9TAsifN_Jg';
  var JSZIP_CDN = 'https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js';
  var jszipLoaded = false;
  var lastMovie = null; // cache thông tin phim từ trang chi tiết, để dùng khi player start
  var subtitleCache = {};
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
      // Async subtitle downloads can finish after the user backs out of the
      // player. Do not let Lampa's internal _video/customSubs race crash the UI.
      log('bo qua gan sub vi player khong con san sang:', error.message || error);
    }
  }

  function startSubSense() {
    if (startSubSense.done) return;
    startSubSense.done = true;

    // Bọc lại Lampa.Player.play để tự động gắn sub mỗi khi phát video
    var originalPlay = Lampa.Player.play;
    Lampa.Player.play = function(params) {
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

  function stringValue(value, fallback) {
    if (value === null || typeof value === 'undefined') return fallback || '';
    return String(value);
  }

  function detectFileType(url, declaredFormat) {
    var declared = stringValue(declaredFormat, '').toLowerCase().replace(/^\./, '');
    if (/^(srt|vtt|zip|rar)$/.test(declared)) return declared;

    var clean = stringValue(url, '').split('?')[0].split('#')[0];
    try { clean = decodeURIComponent(clean); } catch (e) {}

    if (/\.srt$/i.test(clean)) return 'srt';
    if (/\.vtt$/i.test(clean)) return 'vtt';
    if (/\.zip$/i.test(clean)) return 'zip';
    if (/\.rar$/i.test(clean)) return 'rar';
    return 'unknown';
  }

  function isHttpUrl(url) {
    return /^https?:\/\//i.test(stringValue(url, '').trim());
  }

  function looksLikeImdb(value) {
    return typeof value === 'string' && /^tt\d+$/.test(value.trim());
  }

  // Chấp nhận nhiều tên trường cho URL vì addon đổi cấu trúc thường xuyên
  function normalizeSubtitle(item, index) {
    var source = item;
    var url = '';

    if (typeof source === 'string') {
      url = source;
      source = {};
    } else if (source && typeof source === 'object') {
      url = source.url || source.file || source.link || source.download_url || source.download || '';
    } else {
      return null;
    }

    url = stringValue(url, '').trim();
    if (url.indexOf('//') === 0) url = 'https:' + url;
    if (!isHttpUrl(url)) return null;

    var language = stringValue(
      source.language || source.lang || source.lang_code || source.iso_639_1 || 'vi',
      'vi'
    ).trim().toLowerCase();

    var label = stringValue(
      source.label || source.name || source.title || source.release || source.source,
      ''
    ).trim();

    if (!label) label = /^(vi|vie)/.test(language) ? 'Tiếng Việt' : 'Sub ' + (index + 1);

    return {
      url: url,
      label: label,
      language: language || 'vi',
      format: detectFileType(url, source.format || source.ext || source.extension || '')
    };
  }

  function subtitlesFromResponse(data) {
    var list = [];

    if (Array.isArray(data)) list = data;
    else if (data && Array.isArray(data.subtitles)) list = data.subtitles;
    else if (data && Array.isArray(data.results)) list = data.results;
    else if (data && Array.isArray(data.result)) list = data.result;

    return list.map(normalizeSubtitle).filter(function(item) {
      return !!item;
    });
  }

  function fetchSubs(imdbId, type, season, episode, onSuccess, onError) {
    var id = buildId(imdbId, type === 'series' ? season : null, type === 'series' ? episode : null);

    if (subtitleCache[id]) {
      onSuccess(subtitleCache[id]);
      return;
    }

    var url = SUBSENSE_BASE + '/subtitles/' + (type || 'movie') + '/' + id + '.json';
    $.ajax({
      url: url,
      type: 'GET',
      dataType: 'json',
      timeout: 15000,
      success: function(data) {
        var subs = subtitlesFromResponse(data);
        subtitleCache[id] = subs;
        onSuccess(subs);
      },
      error: function(xhr, status, error) {
        onError && onError({
          status: xhr && xhr.status ? xhr.status : 0,
          statusText: status || error || 'network/CORS or upstream unavailable'
        });
      }
    });
  }

  // Origin của server Lampac (nơi plugin được chèn vào) để tải file phụ đề
  // qua /subsense/file — tránh bị CORS chặn khi gọi thẳng tới OpenSubtitles...
  var PLUGIN_ORIGIN = '';
  (function detectOrigin() {
    try {
      var scripts = document.getElementsByTagName('script');
      for (var i = scripts.length - 1; i >= 0; i--) {
        var m = /(https?:\/\/[^\/]+)\/[^\/]*subsense\.js/.exec(scripts[i].src || '');
        if (m) { PLUGIN_ORIGIN = m[1]; return; }
      }
    } catch (e) {}
    if (window.location && /^https?:/.test(window.location.protocol)) {
      PLUGIN_ORIGIN = window.location.origin;
    }
  })();

  function proxiedUrl(url) {
    if (!PLUGIN_ORIGIN) return url;
    return PLUGIN_ORIGIN + '/subsense/file?url=' + encodeURIComponent(url);
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

  function unzipFirstSub(buf, callback) {
    ensureJSZip(function() {
      if (!window.JSZip) { callback(null, 'JSZip khong load duoc'); return; }
      window.JSZip.loadAsync(buf).then(function(zip) {
        // ưu tiên .srt/.vtt trong thư mục gốc, sau đó mới quét đệ quy
        var files = [];
        zip.forEach(function(path, entry) {
          if (!entry.dir && /\.(srt|vtt)$/i.test(path)) files.push({ path: path, entry: entry });
        });
        files.sort(function(a, b) {
          var da = a.path.indexOf('/') !== -1 ? 1 : 0;
          var db = b.path.indexOf('/') !== -1 ? 1 : 0;
          return da - db;
        });
        if (!files.length) { callback(null, 'khong co srt/vtt trong zip'); return; }
        files[0].entry.async('string').then(function(text) {
          callback(makeVttBlobUrl(text));
        }).catch(function() { callback(null, 'doc file trong zip that bai'); });
      }).catch(function() { callback(null, 'giai nen zip that bai'); });
    });
  }

  function resolveToVtt(sub, callback) {
    var type = sub.format;

    if (type === 'rar') {
      callback(null, 'dinh dang rar chua ho tro');
      return;
    }

    if (type === 'zip') {
      $.ajax({
        url: proxiedUrl(sub.url),
        type: 'GET',
        xhrFields: { responseType: 'arraybuffer' },
        success: function(buf) { unzipFirstSub(buf, callback); },
        error: function() { callback(null, 'tai zip that bai'); }
      });
      return;
    }

    // srt/vtt hoặc unknown (link OpenSubtitles thường không có đuôi file):
    // thử đọc text trước, nếu không phải phụ đề thì thử giải nén như zip.
    // Tải qua proxy của Lampac để không dính CORS.
    var fetchUrl = proxiedUrl(sub.url);

    function fetchAsZip() {
      $.ajax({
        url: fetchUrl,
        type: 'GET',
        xhrFields: { responseType: 'arraybuffer' },
        success: function(buf) { unzipFirstSub(buf, callback); },
        error: function() { callback(null, 'dinh dang khong xac dinh'); }
      });
    }

    $.ajax({
      url: fetchUrl,
      type: 'GET',
      dataType: 'text',
      success: function(text) {
        if (typeof text === 'string' && text.indexOf('-->') !== -1) {
          callback(makeVttBlobUrl(text));
          return;
        }

        if (window.JSZip) {
          fetchAsZip();
        } else {
          ensureJSZip(function() {
            if (!window.JSZip) { callback(null, 'dinh dang khong xac dinh'); return; }
            fetchAsZip();
          });
        }
      },
      error: function(xhr, s, e) { callback(null, 'tai file that bai (HTTP ' + (xhr ? xhr.status : s) + ')'); }
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

  // tiếng Việt lên đầu, rồi tới định dạng dễ xử lý (nhanh, ít tốn request)
  function sortByEase(subs) {
    function score(s) {
      var langScore = /^vi/.test(s.language || '') ? 0 : 1;
      var fmtScore = { srt: 0, vtt: 1, zip: 2, unknown: 3, rar: 4 }[s.format];
      if (typeof fmtScore === 'undefined') fmtScore = 3;
      return langScore * 10 + fmtScore;
    }
    return subs.slice().sort(function(a, b) {
      var d = score(a) - score(b);
      if (d) return d;
      return String(a.label).localeCompare(String(b.label));
    });
  }

  function extractImdbId(movie) {
    if (!movie) return null;
    if (looksLikeImdb(movie.imdb_id)) return movie.imdb_id.trim();
    if (movie.external_ids && looksLikeImdb(movie.external_ids.imdb_id)) return movie.external_ids.imdb_id.trim();
    if (looksLikeImdb(movie.id)) return movie.id.trim();

    // quét sâu phòng khi imdb nằm ở nơi khác trong object phim
    var seen = [];
    function scan(value, depth) {
      if (!value || typeof value !== 'object' || depth > 4) return null;
      if (seen.indexOf(value) >= 0) return null;
      seen.push(value);
      for (var key in value) {
        if (!Object.prototype.hasOwnProperty.call(value, key)) continue;
        var item = value[key];
        if (/imdb/i.test(key) && looksLikeImdb(item)) return item.trim();
        if (item && typeof item === 'object') {
          var found = scan(item, depth + 1);
          if (found) return found;
        }
      }
      return null;
    }
    return scan(movie, 0);
  }

  function attachAutoSub(movie, season, episode) {
    var serial = playbackSerial;
    var imdbId = extractImdbId(movie);
    if (!imdbId) {
      log('khong co imdb_id, bo qua phim:', movie && movie.title);
      return;
    }
    var type = (movie.number_of_seasons || movie.season || movie.first_air_date) ? 'series' : 'movie';

    fetchSubs(imdbId, type, season, episode, function(subs) {
      if (!subs.length) { log('khong co sub cho', imdbId); return; }
      var sorted = sortByEase(subs);
      log('tim thay', subs.length, 'ban sub, dang gan tat ca...');
      autoResolveAll(sorted, function(tracks) {
        if (!tracks.length) { log('khong gan duoc ban nao'); return; }

        setSubtitlesSafely(tracks, serial);
      });
    }, function(xhr) {
      var code = xhr && xhr.status ? xhr.status : 0;
      var reason = xhr && xhr.statusText ? xhr.statusText : 'network/CORS or upstream unavailable';
      log('loi lay danh sach sub:', code ? 'HTTP ' + code : reason);
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
