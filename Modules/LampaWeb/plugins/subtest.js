/*
 * SubTest — plugin test kết hợp SubSense + SubFinder
 * 
 * Mục đích: xác định lỗi là do CODE hay do CORS/network
 * 
 * Test 3 cách download:
 *   1. Direct từ browser (kiểm tra CORS)
 *   2. Qua proxy server /subsense/file
 *   3. Qua proxy server /subfinder/file
 * 
 * Console sẽ log rõ [SubTest] kết quả từng bước
 */
(function() {
  'use strict';

  var SUBSENSE_BASE = 'https://subsense.nepiraw.com/tqljxvjr-xpVxxc7Oy1cTDc80FgUuCNKOLyghmJBy_26-EjECW_sCn_xqWdKYGp9Spe6P42EKjpzWv-aL5FfYhKYffVWt-Haf6sjPsKHmp8Hx4B4AI5dkuBpju2bI3I2vFcl0pWwOTdlxRwF5aUCeb5iVJyH3rPqnobh3WVuRUJN-zgACqSXcVQPnnKJlnyiIo5eIkm8yEjMoYm7wXfWWgwtKOZzK4eR6tbhV2uV5o_SVYAgDLJiTINZpKF6nd3HQ78uteeFCaykR7ydhpWdBYQ';
  var lastMovie = null;

  function log() {
    var args = ['[SubTest]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
  }

  // Detect server origin
  var SERVER = '';
  (function() {
    try {
      var scripts = document.getElementsByTagName('script');
      for (var i = scripts.length - 1; i >= 0; i--) {
        var m = /(https?:\/\/[^\/]+)\/[^\/]*subtest\.js/.exec(scripts[i].src || '');
        if (m) { SERVER = m[1]; return; }
      }
    } catch (e) {}
    if (window.location && /^https?:/.test(window.location.protocol)) {
      SERVER = window.location.origin;
    }
  })();

  log('server detected:', SERVER || '(empty - will test direct only)');

  // === Helpers ===
  function extractImdbId(movie) {
    if (!movie) return null;
    function scan(obj, d) {
      if (!obj || typeof obj !== 'object' || d > 4) return null;
      for (var k in obj) {
        if (!Object.prototype.hasOwnProperty.call(obj, k)) continue;
        var v = obj[k];
        if (/imdb/i.test(k) && typeof v === 'string' && /^tt\d+$/.test(v.trim())) return v.trim();
        if (v && typeof v === 'object') { var f = scan(v, d+1); if (f) return f; }
      }
      return null;
    }
    return scan(movie, 0);
  }

  function srtToVtt(srt) {
    return 'WEBVTT\n\n' + srt.replace(/\r+/g, '').replace(/^\d+\s*$/gm, '').replace(/(\d{2}:\d{2}:\d{2}),(\d{3})/g, '$1.$2');
  }

  // === TEST 1: Fetch direct from browser (CORS test) ===
  function testDirect(url, label, callback) {
    log('[TEST-DIRECT] trying:', label, '→', url.substring(0, 80) + '...');
    var xhr = new XMLHttpRequest();
    xhr.open('GET', url, true);
    xhr.timeout = 15000;
    xhr.onload = function() {
      if (xhr.status >= 200 && xhr.status < 300) {
        log('[TEST-DIRECT] ✅ OK', label, 'status=' + xhr.status, 'size=' + xhr.responseText.length);
        callback(xhr.responseText);
      } else {
        log('[TEST-DIRECT] ❌ FAIL', label, 'HTTP ' + xhr.status);
        callback(null);
      }
    };
    xhr.onerror = function() {
      log('[TEST-DIRECT] ❌ CORS/NETWORK error for', label);
      callback(null);
    };
    xhr.ontimeout = function() {
      log('[TEST-DIRECT] ❌ TIMEOUT for', label);
      callback(null);
    };
    xhr.send();
  }

  // === TEST 2: Fetch through proxy ===
  function testProxy(proxyPath, url, label, callback) {
    if (!SERVER) { log('[TEST-PROXY] skip - no server detected'); callback(null); return; }
    var proxyUrl = SERVER + proxyPath + '?url=' + encodeURIComponent(url);
    log('[TEST-PROXY] trying:', label, 'via', proxyPath);
    var xhr = new XMLHttpRequest();
    xhr.open('GET', proxyUrl, true);
    xhr.timeout = 30000;
    xhr.responseType = 'arraybuffer';
    xhr.onload = function() {
      if (xhr.status >= 200 && xhr.status < 300) {
        var size = xhr.byteLength || xhr.responseText.length;
        log('[TEST-PROXY] ✅ OK', label, 'HTTP ' + xhr.status, 'size=' + size);
        callback(xhr);
      } else {
        log('[TEST-PROXY] ❌ FAIL', label, 'HTTP ' + xhr.status);
        callback(null);
      }
    };
    xhr.onerror = function() {
      log('[TEST-PROXY] ❌ NETWORK error for', label);
      callback(null);
    };
    xhr.ontimeout = function() {
      log('[TEST-PROXY] ❌ TIMEOUT for', label);
      callback(null);
    };
    xhr.send();
  }

  // === Main: search subs from SubSense addon ===
  function searchSubSense(imdbId, season, episode, callback) {
    var id = season && episode ? imdbId + ':' + season + ':' + episode : imdbId;
    var type = season ? 'series' : 'movie';
    var url = SUBSENSE_BASE + '/subtitles/' + type + '/' + encodeURIComponent(id) + '.json';
    log('searching SubSense:', url.substring(0, 100) + '...');

    $.ajax({ url: url, type: 'GET', dataType: 'json',
      success: function(data) {
        var subs = data.subtitles || data.results || data.result || (Array.isArray(data) ? data : []);
        log('SubSense found', subs.length, 'subs');
        callback(subs);
      },
      error: function(xhr) { log('SubSense search error:', xhr.status); callback([]); }
    });
  }

  // === Download & resolve a single sub ===
  function resolveSub(sub, callback) {
    var url = sub.url || sub.file || sub.link || sub.download_url || '';
    if (!url) { callback(null, 'no url'); return; }

    var label = sub.label || sub.release || sub.name || 'unknown';
    var ext = url.split('?')[0].split('.').pop().toLowerCase();

    // If it's already SRT/VTT text, try 3 download methods
    if (ext === 'srt' || ext === 'vtt' || ext === 'ass' || ext === 'ssa') {
      // Method 1: Direct (CORS test)
      testDirect(url, label, function(text) {
        if (text && text.indexOf('-->') !== -1) {
          callback(text);
          return;
        }
        // Method 2: Proxy subsense/file
        testProxy('/subsense/file', url, label, function(xhr) {
          if (xhr) {
            try {
              var t = typeof xhr.response === 'string' ? xhr.response : new TextDecoder().decode(xhr.response);
              if (t.indexOf('-->') !== -1) { callback(t); return; }
            } catch(e) {}
          }
          // Method 3: Proxy subfinder/file
          testProxy('/subfinder/file', url, label, function(xhr2) {
            if (xhr2) {
              try {
                var t2 = typeof xhr2.response === 'string' ? xhr2.response : new TextDecoder().decode(xhr2.response);
                if (t2.indexOf('-->') !== -1) { callback(t2); return; }
              } catch(e) {}
            }
            callback(null, 'all methods failed');
          });
        });
      });
    } else {
      // ZIP or unknown - try proxy only (browser can't handle ZIP easily)
      log('file type:', ext, '- trying proxy for binary');
      testProxy('/subsense/file', url, label + ' [zip]', function(xhr) {
        if (xhr) {
          try {
            var t = typeof xhr.response === 'string' ? xhr.response : new TextDecoder().decode(xhr.response);
            if (t.indexOf('-->') !== -1) { callback(t); return; }
          } catch(e) {}
          log('not text, trying subfinder/file proxy');
        }
        testProxy('/subfinder/file', url, label + ' [zip]', function(xhr2) {
          if (xhr2) {
            try {
              var t2 = typeof xhr2.response === 'string' ? xhr2.response : new TextDecoder().decode(xhr2.response);
              if (t2.indexOf('-->') !== -1) { callback(t2); return; }
            } catch(e) {}
          }
          callback(null, 'binary download failed');
        });
      });
    }
  }

  // === Attach subs to player ===
  function attachSubs(movie, season, episode) {
    var imdbId = extractImdbId(movie);
    if (!imdbId) { log('no imdb_id found, skip'); return; }

    log('=== START TEST for', imdbId, 'S' + (season||0) + 'E' + (episode||0), '===');
    log('server:', SERVER);
    log('movie:', movie.title || movie.name || '?');

    searchSubSense(imdbId, season, episode, function(subs) {
      if (!subs.length) { log('no subs found from SubSense'); return; }

      // Try resolve ALL subs (not just first) to get full diagnostic
      var pending = subs.length;
      var tracks = [];

      subs.forEach(function(sub, i) {
        var subUrl = sub.url || sub.file || sub.link || sub.download_url || '';
        var label = sub.label || sub.release || sub.name || 'Sub ' + (i+1);
        log('--- sub ' + (i+1) + '/' + subs.length + ':', label);
        log('    url:', subUrl.substring(0, 120));

        resolveSub(sub, function(text, err) {
          if (text) {
            var vtt = text.indexOf('WEBVTT') === 0 ? text : srtToVtt(text);
            tracks.push({ url: URL.createObjectURL(new Blob([vtt], {type:'text/vtt'})), label: label, language: 'vi' });
            log('✅ sub', (i+1), 'RESOLVED OK');
          } else {
            log('❌ sub', (i+1), 'FAILED:', err);
          }
          pending--;
          if (pending === 0) {
            log('=== RESULT:', tracks.length, '/', subs.length, 'subs resolved ===');
            if (tracks.length > 0) {
              try { Lampa.Player.subtitles(tracks); } catch(e) { log('Player.subtitles error:', e.message); }
            }
          }
        });
      });
    });
  }

  // === Hook into Lampa ===
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

    log('plugin ready, waiting for playback...');
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
