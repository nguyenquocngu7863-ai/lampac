/*
 * SubFinder — tìm và gắn phụ đề tiếng Việt cho Lampa Player.
 *
 * Nguồn: SubDL API (cần API key miễn phí) và SubSource.
 * Plugin gọi server proxy của Lampac để search + download sub,
 * tránh CORS và giữ API key an toàn server-side.
 *
 * Config trong init.conf:
 *   "SubFinder": { "enabled": true, "subdl_api_key": "YOUR_KEY" }
 *
 * Đăng ký API key miễn phí tại: https://subdl.com/developers
 */
(function () {
  'use strict';

  var lastMovie = null;
  var subtitleCache = {};
  var JSZIP_CDN = 'https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js';
  var jszipLoaded = false;

  function log() {
    var args = ['[SubFinder]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
  }

  /* ── Detect server origin ── */
  var SERVER_ORIGIN = '';
  (function detectOrigin() {
    try {
      var scripts = document.getElementsByTagName('script');
      for (var i = scripts.length - 1; i >= 0; i--) {
        var m = /(https?:\/\/[^\/]+)\/[^\/]*subfinder\.js/.exec(scripts[i].src || '');
        if (m) { SERVER_ORIGIN = m[1]; return; }
      }
    } catch (e) {}
    if (window.location && /^https?:/.test(window.location.protocol)) {
      SERVER_ORIGIN = window.location.origin;
    }
  })();

  /* ── Helpers ── */
  function looksLikeImdb(v) {
    return typeof v === 'string' && /^tt\d+$/i.test(v.trim());
  }

  function extractImdbId(movie) {
    if (!movie) return null;
    if (looksLikeImdb(movie.imdb_id)) return movie.imdb_id.trim();
    if (movie.external_ids && looksLikeImdb(movie.external_ids.imdb_id))
      return movie.external_ids.imdb_id.trim();
    if (looksLikeImdb(movie.id)) return movie.id.trim();
    // deep scan
    var seen = [];
    function scan(obj, depth) {
      if (!obj || typeof obj !== 'object' || depth > 4) return null;
      if (seen.indexOf(obj) >= 0) return null;
      seen.push(obj);
      for (var k in obj) {
        if (!Object.prototype.hasOwnProperty.call(obj, k)) continue;
        if (/imdb/i.test(k) && looksLikeImdb(obj[k])) return obj[k].trim();
        if (obj[k] && typeof obj[k] === 'object') {
          var f = scan(obj[k], depth + 1);
          if (f) return f;
        }
      }
      return null;
    }
    return scan(movie, 0);
  }

  function isSeries(movie) {
    if (!movie) return false;
    return !!(
      movie.first_air_date || movie.number_of_seasons ||
      movie.number_of_episodes || movie.media_type === 'tv' ||
      movie.type === 'series' || (movie.name && movie.original_name)
    );
  }

  function ensureJSZip(cb) {
    if (jszipLoaded || window.JSZip) { jszipLoaded = true; cb(); return; }
    var s = document.createElement('script');
    s.src = JSZIP_CDN;
    s.onload = function () { jszipLoaded = true; cb(); };
    s.onerror = function () { log('khong tai duoc JSZip'); cb(); };
    document.head.appendChild(s);
  }

  function srtToVtt(srt) {
    return 'WEBVTT\n\n' + srt
      .replace(/\r+/g, '')
      .replace(/^\d+\s*$/gm, '')
      .replace(/(\d{2}:\d{2}:\d{2}),(\d{3})/g, '$1.$2');
  }

  function makeVttBlob(text) {
    return URL.createObjectURL(new Blob([srtToVtt(text)], { type: 'text/vtt' }));
  }

  function detectFormat(url) {
    var clean = decodeURIComponent((url || '').split('?')[0]);
    if (/\.srt$/i.test(clean)) return 'srt';
    if (/\.vtt$/i.test(clean)) return 'vtt';
    if (/\.zip$/i.test(clean)) return 'zip';
    if (/\.ass$/i.test(clean) || /\.ssa$/i.test(clean)) return 'ass';
    return 'unknown';
  }

  /* ── Download + convert subtitle file ── */
  function resolveToVtt(sub, cb) {
    // Fix SubDL download domain: subdl.com → dl.subdl.com
    var downloadUrl = sub.url || '';
    if (downloadUrl.indexOf('subdl.com/subtitle/') !== -1 && downloadUrl.indexOf('dl.subdl.com') === -1) {
      downloadUrl = downloadUrl.replace('https://subdl.com/', 'https://dl.subdl.com/');
    }
    var fetchUrl = downloadUrl;
    var fmt = detectFormat(downloadUrl) || sub.format || 'unknown';

    if (fmt === 'ass' || fmt === 'ssa') {
      // ASS/SSA: download text, convert basic tags to VTT
      $.ajax({
        url: fetchUrl, type: 'GET', dataType: 'text',
        success: function (text) {
          if (typeof text === 'string' && text.indexOf('Dialogue') !== -1) {
            // very basic ASS→VTT: extract dialogue lines
            var lines = text.split('\n');
            var cues = [];
            lines.forEach(function (l) {
              var m = l.match(/^Dialogue:\s*\d+,(\d+:\d+:\d+\.\d+),(\d+:\d+:\d+\.\d+),[^,]*,[^,]*,[^,]*,[^,]*,[^,]*,[^,]*,[^,]*,(.*)/);
              if (m) {
                var start = m[1].replace('.', ',');
                var end = m[2].replace('.', ',');
                var txt = m[3].replace(/\{[^}]*\}/g, '').replace(/\\N/g, '\n').replace(/\\n/g, '\n');
                cues.push(start + ' --> ' + end + '\n' + txt);
              }
            });
            if (cues.length) {
              cb(URL.createObjectURL(new Blob(['WEBVTT\n\n' + cues.join('\n\n')], { type: 'text/vtt' })));
              return;
            }
          }
          cb(null, 'ASS convert that bai');
        },
        error: function (xhr, s) { cb(null, 'tai ASS that bai (HTTP ' + (xhr ? xhr.status : s) + ')'); }
      });
      return;
    }

    if (fmt === 'zip') {
      $.ajax({
        url: fetchUrl, type: 'GET', xhrFields: { responseType: 'arraybuffer' },
        success: function (buf) {
          ensureJSZip(function () {
            if (!window.JSZip) { cb(null, 'JSZip khong load'); return; }
            window.JSZip.loadAsync(buf).then(function (zip) {
              var files = [];
              zip.forEach(function (p, e) {
                if (!e.dir && /\.(srt|vtt)$/i.test(p)) files.push({ p: p, e: e });
              });
              if (!files.length) { cb(null, 'khong co srt/vtt trong zip'); return; }
              files[0].e.async('string').then(function (t) { cb(makeVttBlob(t)); });
            }).catch(function () { cb(null, 'giai nen zip that bai'); });
          });
        },
        error: function (xhr, s) { cb(null, 'tai zip that bai (HTTP ' + (xhr ? xhr.status : s) + ')'); }
      });
      return;
    }

    // srt/vtt/unknown: try text first, then zip fallback
    $.ajax({
      url: fetchUrl, type: 'GET', dataType: 'text',
      success: function (text) {
        if (typeof text === 'string' && (text.indexOf('-->') !== -1 || text.trim().length > 20)) {
          cb(makeVttBlob(text));
        } else {
          // try as zip
          $.ajax({
            url: fetchUrl, type: 'GET', xhrFields: { responseType: 'arraybuffer' },
            success: function (buf) {
              ensureJSZip(function () {
                if (!window.JSZip) { cb(null, 'khong phai sub va khong zip'); return; }
                window.JSZip.loadAsync(buf).then(function (zip) {
                  var f = null;
                  zip.forEach(function (p, e) { if (!f && /\.(srt|vtt)$/i.test(p)) f = e; });
                  if (!f) { cb(null, 'khong co srt/vtt trong zip'); return; }
                  f.async('string').then(function (t) { cb(makeVttBlob(t)); });
                }).catch(function () { cb(null, 'khong phai sub hop le'); });
              });
            },
            error: function () { cb(null, 'tai that bai'); }
          });
        }
      },
      error: function (xhr, s) { cb(null, 'tai file that bai (HTTP ' + (xhr ? xhr.status : s) + ')'); }
    });
  }

  /* ── Search SubDL via server proxy ── */
  function searchSubDL(imdbId, query, type, season, episode, cb) {
    if (!SERVER_ORIGIN) { cb([]); return; }
    // SubDL q param accepts both IMDB ID and name
    var params = 'q=' + encodeURIComponent(imdbId || query || '');
    if (type === 'tv' && season && episode) {
      params += '&type=tv&season=' + season + '&episode=' + episode;
    } else {
      params += '&type=movie';
    }
    params += '&languages=vi';

    $.ajax({
      url: SERVER_ORIGIN + '/subfinder/search?' + params,
      type: 'GET', dataType: 'json', timeout: 15000,
      success: function (data) {
        var subs = [];
        if (data && Array.isArray(data.subtitles)) {
          subs = data.subtitles.map(function (s) {
            return {
              url: s.url || s.download_url || '',
              label: s.release_name || s.label || s.filename || 'SubDL',
              language: (s.lang || s.language || 'vi').toLowerCase(),
              format: detectFormat(s.url || ''),
              source: 'SubDL'
            };
          }).filter(function (s) { return !!s.url; });
        }
        cb(subs);
      },
      error: function (xhr, s) {
        log('SubDL search loi:', xhr ? xhr.status : s);
        cb([]);
      }
    });
  }

  /* ── Search SubSource via server proxy ── */
  function searchSubSource(imdbId, query, type, season, episode, cb) {
    if (!SERVER_ORIGIN) { cb([]); return; }
    var params = '';
    if (imdbId) {
      params = 'imdb=' + encodeURIComponent(imdbId);
    } else if (query) {
      params = 'query=' + encodeURIComponent(query);
    } else {
      cb([]); return;
    }
    if (type === 'tv' && season && episode) {
      params += '&type=tv&season=' + season + '&episode=' + episode;
    } else {
      params += '&type=movie';
    }
    params += '&languages=vi';

    $.ajax({
      url: SERVER_ORIGIN + '/subfinder/search-source?' + params,
      type: 'GET', dataType: 'json', timeout: 15000,
      success: function (data) {
        var subs = [];
        if (data && Array.isArray(data.subtitles)) {
          subs = data.subtitles.map(function (s) {
            return {
              url: s.url || s.download_url || '',
              label: s.release_name || s.label || 'SubSource',
              language: (s.lang || s.language || 'vi').toLowerCase(),
              format: detectFormat(s.url || ''),
              source: 'SubSource'
            };
          }).filter(function (s) { return !!s.url; });
        }
        cb(subs);
      },
      error: function (xhr, s) {
        log('SubSource search loi:', xhr ? xhr.status : s);
        cb([]);
      }
    });
  }

  /* ── Main: search all sources + resolve + attach ── */
  function attachSubs(movie, season, episode) {
    var imdbId = extractImdbId(movie);
    if (!imdbId) {
      log('khong co imdb_id, bo qua:', movie && (movie.title || movie.name));
      return;
    }

    var type = isSeries(movie) ? 'tv' : 'movie';
    var cacheKey = imdbId + ':' + type + ':' + (season || '') + ':' + (episode || '');

    if (subtitleCache[cacheKey]) {
      log('da co cache cho', cacheKey);
      return;
    }
    subtitleCache[cacheKey] = true;

    log('tim sub cho', imdbId, type, season ? 'S' + season + 'E' + episode : '');

    // Movie name for fallback search when no IMDB ID
    var movieName = (movie && (movie.title || movie.name || movie.original_title || '')) || '';

    // Search both sources in parallel
    var allSubs = [];
    var pending = 2;

    function done() {
      pending--;
      if (pending > 0) return;

      // De-duplicate by URL
      var seen = {};
      allSubs = allSubs.filter(function (s) {
        if (seen[s.url]) return false;
        seen[s.url] = true;
        return true;
      });

      if (!allSubs.length) {
        log('khong tim thay sub nao cho', imdbId || movieName);
        return;
      }

      // Sort: Vietnamese first, then by source preference
      allSubs.sort(function (a, b) {
        var la = /^vi/.test(a.language) ? 0 : 1;
        var lb = /^vi/.test(b.language) ? 0 : 1;
        return la - lb;
      });

      log('tim thay', allSubs.length, 'ban sub, dang gan...');

      // Resolve all to VTT
      var tracks = [];
      var resolvePending = allSubs.length;

      allSubs.forEach(function (sub, idx) {
        resolveToVtt(sub, function (vttUrl, err) {
          if (vttUrl) {
            tracks.push({
              url: vttUrl,
              label: sub.source + ' · ' + sub.label,
              language: sub.language || 'vi'
            });
          } else {
            log('bo qua:', sub.label, '-', err);
          }
          resolvePending--;
          if (resolvePending === 0 && tracks.length) {
            Lampa.Player.subtitles(tracks);
            log('da gan', tracks.length, 'ban sub');
          }
        });
      });
    }

    // Pass imdbId || movieName so SubDL can search by name if no ID
    var searchQuery = imdbId || movieName;

    searchSubDL(imdbId, movieName, type, season, episode, function (subs) {
      log('SubDL:', subs.length, 'ket qua');
      allSubs = allSubs.concat(subs);
      done();
    });

    searchSubSource(imdbId, movieName, type, season, episode, function (subs) {
      log('SubSource:', subs.length, 'ket qua');
      allSubs = allSubs.concat(subs);
      done();
    });
  }

  /* ── Hook into Lampa ── */
  function startSubFinder() {
    if (startSubFinder.done) return;
    startSubFinder.done = true;

    var originalPlay = Lampa.Player.play;
    Lampa.Player.play = function (params) {
      var result = originalPlay.apply(this, arguments);
      var movie = (params && params.movie) || lastMovie;
      var season = (params && params.season) || (movie && movie.season) || null;
      var episode = (params && params.episode) || (movie && movie.episode) || null;
      if (movie) attachSubs(movie, season, episode);
      return result;
    };

    log('plugin da khoi dong, cho phat phim...');
  }

  // Wait for Lampa to be ready
  if (typeof Lampa !== 'undefined') {
    if (Lampa.Manifest && Lampa.Manifest.app && Lampa.Manifest.app_ready) {
      startSubFinder();
    } else {
      Lampa.Listener.follow('app', function (e) {
        if (e.type === 'ready') startSubFinder();
      });
    }
  }

  // Cache movie info from detail page
  if (typeof Lampa !== 'undefined') {
    Lampa.Listener.follow('full', function (e) {
      if (e.type === 'complite' && e.data && e.data.movie) {
        lastMovie = e.data.movie;
      }
    });
  }
})();
