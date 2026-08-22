(function () {
'use strict';

var SUBSENSE_BASE = 'https://subsense.nepiraw.com/lxolz7e9-%7B%22languages%22%3A%5B%22vi%22%5D%7D';
var JSZIP_CDN = 'https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js';
var jszipLoaded = false;
var lastMovie = null;

function log() {
    var args = ['[SubSense]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
}

function ensureJSZip(callback) {
    if (jszipLoaded || window.JSZip) { jszipLoaded = true; callback(); return; }
    var script = document.createElement('script');
    script.src = JSZIP_CDN;
    script.onload = function () { jszipLoaded = true; callback(); };
    script.onerror = function () { log('khong tai duoc JSZip'); callback(); };
    document.head.appendChild(script);
}

// === MOI: bo ma hoa base64url + them query (dung cho MX Sub Bridge) ===
function base64UrlEncode(str) {
    try {
        var utf8 = unescape(encodeURIComponent(str));
        var b64 = btoa(utf8);
        return b64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    } catch (e) {
        log('base64UrlEncode loi:', e.message);
        return '';
    }
}

function appendQuery(url, key, value) {
    if (!url || typeof url !== 'string') return url;
    var sep = url.indexOf('?') === -1 ? '?' : '&';
    return url + sep + encodeURIComponent(key) + '=' + encodeURIComponent(value);
}
// === het phan moi ===

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
      success: function (data) { onSuccess(data.subtitles || []); },
      error: function (xhr) { onError && onError(xhr); }
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

function uploadToCatbox(text, filename, callback) {
    var blob = new Blob([text], { type: 'text/plain' });
    var form = new FormData();
    form.append('reqtype', 'fileupload');
    form.append('fileToUpload', blob, filename || 'sub.srt');

    $.ajax({
      url: 'https://catbox.moe/user/api.php',
      type: 'POST',
      data: form,
      processData: false,
      contentType: false,
      success: function (url) {
        if (typeof url === 'string' && url.indexOf('http') === 0) {
          callback(url.trim());
        } else {
          callback(null, 'catbox tra ve khong hop le: ' + url);
        }
      },
      error: function (xhr) {
        callback(null, 'upload catbox that bai: ' + xhr.status);
      }
    });
}

function resolveToExternalUrl(sub, callback) {
    var type = detectFileType(sub.url);

    if (type === 'srt' || type === 'vtt') {
      callback(sub.url);
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
                uploadToCatbox(text, 'sub.srt', callback);
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

    callback(null, 'dinh dang khong xac dinh, khong the tao link ngoai');
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
        error: function () { callback(null, 'tai file that bai'); }
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

function resolveAll(subs, callback) {
    if (!subs.length) { callback([]); return; }
    var tracks = [];
    var pending = subs.length;
    subs.forEach(function (sub) {
      resolveToVtt(sub, function (vttUrl, err) {
        if (vttUrl) {
          tracks.push({ url: vttUrl, label: sub.label, language: 'vi' });
        } else {
          log('bo qua sub', sub.label, '-', err);
        }
        pending--;
        if (pending === 0) callback(tracks);
      });
    });
}

function extractImdbId(movie) {
    if (!movie) return null;
    function scan(obj, depth) {
      if (!obj || typeof obj !== 'object' || depth > 3) return null;
      for (var key in obj) {
        if (!obj.hasOwnProperty(key)) continue;
        var val = obj[key];
        if (/imdb/i.test(key) && typeof val === 'string' && /^tt\d+/.test(val)) {
          return val;
        }
        if (typeof val === 'object') {
          var found = scan(val, depth + 1);
          if (found) return found;
        }
      }
      return null;
    }
    return scan(movie, 0);
}

function safeSetSubtitles(tracks, attempt) {
    attempt = attempt || 0;
    try {
      Lampa.Player.subtitles(tracks);
      log('da gan', tracks.length, 'ban sub thanh cong');
    } catch (err) {
      log('gan sub loi (lan ' + attempt + '):', err.message);
      if (attempt < 5) {
        setTimeout(function () { safeSetSubtitles(tracks, attempt + 1); }, 800);
      } else {
        log('bo cuoc sau 5 lan thu, khong gan duoc sub vao player');
      }
    }
}

function getEpisodeCount(movie, season) {
    if (movie && Array.isArray(movie.seasons)) {
      var found = movie.seasons.filter(function (s) {
        return String(s.season_number) === String(season);
      })[0];
      if (found && found.episode_count) return found.episode_count;
    }
    return null;
}

function fetchSeasonSubs(imdbId, season, episodeCount, callback) {
    var allSubs = [];
    var pending = episodeCount;
    if (!pending) { callback(allSubs); return; }

    for (var ep = 1; ep <= episodeCount; ep++) {
      (function (epNum) {
    fetchSubs(imdbId, 'series', season, epNum, function (subs) {
          subs.forEach(function (s) {
            s.label = 'Tập ' + epNum + ' - ' + s.label;
          });
          allSubs = allSubs.concat(subs);
          pending--;
          if (pending === 0) callback(allSubs);
        }, function () {
          pending--;
          if (pending === 0) callback(allSubs);
        });
      })(ep);
    }
}

function attachAutoSub(playData) {
    log('DEBUG play data:', playData);

    var imdbId = extractImdbId(playData);
    if (!imdbId) {
      log('khong tim thay imdb_id trong data play, bo qua phim:', playData && playData.title);
      return;
    }

    var season = playData.season || 0;
    var episode = playData.episode || 0;
    var isSeries = season > 0 || episode > 0;

    log('DEBUG imdbId =', imdbId, 'season =', season, 'episode =', episode, 'isSeries =', isSeries);

    if (!isSeries) {
      fetchSubs(imdbId, 'movie', null, null, function (subs) {
        if (!subs.length) { log('khong co sub cho', imdbId); return; }
        log('tim thay', subs.length, 'ban sub, dang tai het...');
        resolveAll(subs, function (tracks) {
          if (!tracks.length) { log('khong resolve duoc ban nao'); return; }
          safeSetSubtitles(tracks);
        });
      }, function (xhr) {
        log('loi lay danh sach sub:', xhr.status);
      });
      return;
    }

    fetchSubs(imdbId, 'series', season, episode, function (subs) {
      if (!subs.length) { log('khong co sub cho S' + season + 'E' + episode); return; }
      log('tim thay', subs.length, 'ban sub cho S' + season + 'E' + episode + ', dang tai het...');
      resolveAll(subs, function (tracks) {
        if (!tracks.length) { log('khong resolve duoc ban nao'); return; }
        safeSetSubtitles(tracks);
      });
    }, function (xhr) {
      log('loi lay danh sach sub:', xhr.status);
    });
}

// Fix chuẩn cho player NỘI BỘ
Lampa.Player.listener.follow('ready', function (data) {
    if (data) {
      attachAutoSub(data);
    } else {
      log('event ready khong co data, bo qua tu dong gan sub');
    }
});

// resolve thành link http thật (dùng cho MX Player/external)
function resolveAllExternal(subs, callback) {
    if (!subs.length) { callback([]); return; }
    var tracks = [];
    var pending = subs.length;
    subs.forEach(function (sub) {
      resolveToExternalUrl(sub, function (url, err) {
        if (url) {
          tracks.push({ url: url, label: sub.label, language: 'vi' });
        } else {
          log('bo qua sub (MX)', sub.label, '-', err);
        }
        pending--;
        if (pending === 0) callback(tracks);
      });
    });
}

// Fix cho MX Player: chặn Lampa.Android.openPlayer(), đợi sub rồi mới mở
if (Lampa.Android && typeof Lampa.Android.openPlayer === 'function') {
    var originalOpenPlayer = Lampa.Android.openPlayer;
    Lampa.Android.openPlayer = function (link, data) {
      var self = this;
      var args = arguments;

      if (!data) return originalOpenPlayer.apply(self, args);

      var imdbId = extractImdbId(data);
      if (!imdbId) {
        log('MX Player: khong co imdb_id, mo ngay khong sub');
        return originalOpenPlayer.apply(self, args);
      }

      var season = data.season || 0;
      var episode = data.episode || 0;
      var type = (season > 0 || episode > 0) ? 'series' : 'movie';
      var opened = false;

      function openNow() {
        if (opened) return;
        opened = true;
        clearTimeout(timeoutId);
        originalOpenPlayer.apply(self, args);
      }

      var timeoutId = setTimeout(function () {
        log('MX Player: qua 8s cho sub, mo luon khong sub');
        openNow();
      }, 8000);

      log('MX Player: dang cho lay sub cho', imdbId, '(toi da 8s)...');

      fetchSubs(imdbId, type, season, episode, function (subs) {
        if (!subs.length) {
          log('MX Player: khong co sub, mo luon');
          openNow();
          return;
        }
        resolveAllExternal(subs, function (tracks) {
          if (!tracks.length) {
            log('MX Player: khong resolve duoc ban nao, mo luon khong sub');
            openNow();
            return;
          }
          data.subtitles = tracks;
          data.subs = tracks.map(function (t) {
            return { url: t.url, name: t.label, lang: t.language };
          });

          // === MOI: nhét subtitleMeta (base64url JSON) vào LINK để MX Sub Bridge APK đọc ra ===
          try {
            var meta = tracks.map(function (t) {
              return { url: t.url, label: t.label, language: t.language };
            });
            var encoded = base64UrlEncode(JSON.stringify(meta));
            if (encoded) {
              args[0] = appendQuery(args[0], 'subtitleMeta', encoded);
              log('MX Player: da gan subtitleMeta vao link (' + tracks.length + ' sub)');
            }
          } catch (e) {
            log('MX Player: khong gan duoc subtitleMeta:', e.message);
          }
          // === het phan moi ===

          log('MX Player: da nhet', tracks.length, 'sub vao data, mo MX Player');
          log('DEBUG data gui qua MX:', data);
          openNow();
        });
      }, function (xhr) {
        log('MX Player: loi lay danh sach sub (' + xhr.status + '), mo luon');
        openNow();
      });
    };
    log('da gan hook (che do CHO) cho Lampa.Android.openPlayer');
} else {
    log('khong tim thay Lampa.Android.openPlayer, co the ban dang khong chay tren app Android');
}

log('plugin da load, cho phat phim...');

function getExternalLink(sub, callback) {
    resolveToExternalUrl(sub, function (url, err) {
      if (url) log('link ngoai:', url);
      else log('loi tao link ngoai:', err);
      callback(url, err);
    });
}

try {
    Lampa.Listener.follow('player', function (e) {
      if (e.type === 'external' || e.type === 'open_external') {
        log('bat duoc su kien external:', e);
      }
    });
} catch (err) {
    log('khong gan duoc listener external:', err);
}

window.SubSensePlugin = {
    fetch: fetchSubs,
    resolveToVtt: resolveToVtt,
    resolveAll: resolveAll,
    resolveToExternalUrl: resolveToExternalUrl,
    getExternalLink: getExternalLink,
    attachAutoSub: attachAutoSub
};
})();
