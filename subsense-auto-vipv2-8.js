(function () {
  'use strict';

  var DEFAULT_SUBSENSE_BASE = 'https://subsense.nepiraw.com/lxolz7e9-%7B%22languages%22%3A%5B%22vi%22%5D%7D';
  var JSZIP_CDN = 'https://cdnjs.cloudflare.com/ajax/libs/jszip/3.10.1/jszip.min.js';
  var jszipLoaded = false;
  var lastMovie = null;
  var lastPlayData = null;

  function log() {
    var args = ['[SubSense]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
  }

  // Đọc URL manifest từ Settings (ní cấu hình trong Lampa Settings > SubSense),
  // fallback về giá trị mặc định nếu chưa cấu hình. Tự tách phần base (bỏ
  // /manifest.json ở cuối nếu ní paste nguyên link manifest).
  function getSubsenseBase() {
    var raw = Lampa.Storage.get('subsense_manifest', DEFAULT_SUBSENSE_BASE + '/manifest.json');
    return raw.replace(/\/manifest\.json\/?$/, '');
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
    var url = getSubsenseBase() + '/subtitles/' + (type || 'movie') + '/' + id + '.json';
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

  // Upload tạm nội dung sub lên catbox.moe để có url thật (dùng cho MX Player / external)
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

  // Lấy url thật (http) dùng được cho MX Player / player ngoài, không dùng blob
  function resolveToExternalUrl(sub, callback) {
    var type = detectFileType(sub.url);

    if (type === 'srt' || type === 'vtt') {
      // đã là link thật, MX Player đọc trực tiếp được, khỏi convert
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

  // resolve toàn bộ danh sách song song, trả về mảng track cho player
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
    // quét đệ quy tìm field nào có 'imdb' trong tên, giá trị dạng tt123456
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

  // gọi Lampa.Player.subtitles() an toàn, tự thử lại nếu player chưa sẵn sàng
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

  // gom sub của toàn bộ tập trong 1 season lại thành 1 danh sách
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

  // Đọc season/episode trực tiếp từ URL/path stream (kiểu s01e04, S01E15,
  // 1x04...). Đáng tin hơn field playData.season/episode vì nhiều nguồn trả
  // field đó sai/rác (đã xác nhận qua log thực tế - field đứng yên một giá
  // trị bất kể tập nào đang phát, trong khi URL luôn đúng theo tập thật).
  function parseSeasonEpisodeFromUrl(playData) {
    var candidates = [playData && playData.url, playData && playData.title].filter(Boolean);
    for (var i = 0; i < candidates.length; i++) {
      var str = decodeURIComponent(candidates[i]);
      var m = str.match(/[sS](\d{1,2})[eE](\d{1,3})/) || str.match(/(\d{1,2})x(\d{1,3})/);
      if (m) return { season: parseInt(m[1], 10), episode: parseInt(m[2], 10) };
    }
    return null;
  }

  // Chỉ bắt season (không có tập rõ ràng trong URL) - kích hoạt nhánh tải
  // nguyên season khi không biết chính xác tập nào.
  function parseSeasonOnlyFromUrl(playData) {
    var candidates = [playData && playData.url, playData && playData.title].filter(Boolean);
    for (var i = 0; i < candidates.length; i++) {
      var str = decodeURIComponent(candidates[i]);
      var m = str.match(/[sS]eason[\s._-]?(\d{1,2})/i) || str.match(/[sS](\d{1,2})(?![eE\d])/);
      if (m) return { season: parseInt(m[1], 10), episode: 0 };
    }
    return null;
  }

  function getSeasonEpisode(playData) {
    var fromUrl = parseSeasonEpisodeFromUrl(playData);
    if (fromUrl) return { season: fromUrl.season, episode: fromUrl.episode, confident: true };

    var seasonOnly = parseSeasonOnlyFromUrl(playData);
    if (seasonOnly) return { season: seasonOnly.season, episode: 0, confident: true };

    // fallback về field gốc nếu URL không parse được (phim lẻ, hoặc nguồn
    // không nhét season/ep vào tên file) - field playData.season/episode
    // ĐÃ XÁC NHẬN không đáng tin cho phim bộ (thường = tổng số tập, không
    // phải tập đang xem) - đánh dấu confident:false để chỗ gọi biết mà
    // verify qua TMDB hoặc fallback tải nguyên season thay vì tin mù.
    return { season: playData.season || 0, episode: playData.episode || 0, confident: false };
  }

  // Tra ngược imdb_id qua TMDB id khi data play không có sẵn imdb_id.
  // Lampa.TMDB.external_imdb_id dùng chung API key Lampa đã cấu hình.
  function getTmdbInfo(playData) {
    var card = playData && playData.card;
    if (!card || !card.id) return null;
    var isTv = !!(card.first_air_date !== undefined || card.name || card.number_of_seasons);
    return { id: card.id, type: isTv ? 'tv' : 'movie' };
  }

  // Tra imdb_id từ 1 movie card TMDB (dùng cho nút "Tải phụ đề" ở trang chi
  // tiết phim, trước khi phát) - thử trực tiếp trước, không có thì qua TMDB.
  function getImdbFromMovieCard(movie, callback) {
    var direct = extractImdbId(movie);
    if (direct) { callback(direct); return; }
    if (!movie || !movie.id) { callback(null); return; }
    var isTv = !!(movie.first_air_date !== undefined || movie.name || movie.number_of_seasons);
    Lampa.TMDB.external_imdb_id({ id: movie.id, type: isTv ? 'tv' : 'movie' }, function (id) {
      callback(id || null);
    });
  }

  // Cache sub đã tải trước (theo imdbId:season:episode) - để lúc phát thật
  // gắn ngay lập tức, khỏi phải chờ fetch lại từ đầu.
  var subsCache = {};

  function cacheKey(imdbId, season, episode) {
    return imdbId + ':' + (season || 0) + ':' + (episode || 0);
  }

  function fetchAndResolve(imdbId, season, episode, playData, callback) {
    var key = cacheKey(imdbId, season, episode);
    if (subsCache[key]) { log('dung sub da cache san cho', key); callback(subsCache[key]); return; }

    var isSeries = season > 0 || episode > 0;
    var isUnknownEpisode = season > 0 && !episode; // biết season nhưng không biết tập

    if (isUnknownEpisode) {
      // Không xác định được tập -> tải hết nguyên season, gắn tất cả vào
      // player, ní tự bấm nút chọn sub trong player để chọn đúng tập/bản.
      var episodeCount = getEpisodeCount(playData && playData.card, season) || 30;
      log('khong biet chinh xac tap, dang tai nguyen season', season, '(' + episodeCount + ' tap)...');
      fetchSeasonSubs(imdbId, season, episodeCount, function (allSubs) {
        if (!allSubs.length) { callback([]); return; }
        log('tim thay', allSubs.length, 'ban sub tren ca season, dang resolve het...');
        resolveAll(allSubs, function (tracks) {
          if (tracks.length) subsCache[key] = tracks;
          callback(tracks);
        });
      });
      return;
    }

    fetchSubs(imdbId, isSeries ? 'series' : 'movie', isSeries ? season : null, isSeries ? episode : null, function (subs) {
      if (!subs.length) { callback([]); return; }
      log('tim thay', subs.length, 'ban sub, dang resolve het...');
      resolveAll(subs, function (tracks) {
        if (tracks.length) subsCache[key] = tracks;
        callback(tracks);
      });
    }, function (xhr) {
      log('loi lay danh sach sub:', xhr.status);
      callback([]);
    });
  }

  // Sửa lại số tập thật khi field "episode" nghi ngờ là rác (đã xác nhận
  // qua thực tế: field đó = tổng số tập của season, không phải tập đang
  // xem). Khớp tên tập thật (playData.title) với danh sách tập từ TMDB.
  // QUAN TRỌNG: nếu không khớp được tên tập nào cả -> KHÔNG tin field gốc,
  // trả về 0 (coi như "không biết tập") để kích hoạt nhánh tải nguyên season
  // thay vì lấy nhầm sub của tập khác do tin field rác.
  function resolveEpisodeNumberByTitle(tvId, season, episodeTitle, fallbackEpisode, callback) {
    if (!episodeTitle || !tvId) {
      // Không đủ dữ kiện để xác minh -> an toàn hơn là coi như không biết
      // tập, để rơi vào nhánh tải nguyên season thay vì tin field gốc mù quáng.
      callback(0);
      return;
    }
    Lampa.TMDB.get('tv/' + tvId + '/season/' + season, {}, function (json) {
      var eps = json && json.episodes;
      if (!eps || !eps.length) { callback(0); return; }
      var norm = function (s) { return (s || '').toLowerCase().trim(); };
      var match = eps.filter(function (e) { return norm(e.name) === norm(episodeTitle); })[0];
      if (match) {
        log('khop ten tap "' + episodeTitle + '" -> tap that la', match.episode_number, '(field goc:', fallbackEpisode + ')');
        callback(match.episode_number);
      } else {
        log('khong khop duoc ten tap "' + episodeTitle + '" voi tap nao trong season ' + season + ' -> tai nguyen season');
        callback(0);
      }
    }, function () { callback(0); });
  }

  // Fallback cuối: không còn imdb_id lẫn tmdb_id (không có card gì cả) ->
  // search theo tên tựa phim qua TMDB. LƯU Ý QUAN TRỌNG: TMDB search chỉ
  // khớp theo TÊN SERIES/PHIM, không khớp được tên TẬP LẺ (vd "Spoonful" sẽ
  // ra 0 kết quả vì đó không phải tên series). Fallback này chỉ cứu được
  // trường hợp playData.title đúng là tên phim/series thật (thường đúng với
  // phim lẻ). Trường hợp chỉ có tên tập lẻ, không có gì khác để tra, thì
  // không có cách nào tự động xác định được series - giới hạn kỹ thuật thật,
  // cần web search tổng quát (Google) mà JS trong Lampa không có sẵn.
  function searchImdbByTitle(playData, callback) {
    var title = playData && playData.title;
    if (!title) { callback(null); return; }

    var se = getSeasonEpisode(playData);
    var preferType = se.season > 0 ? 'tv' : null; // ưu tiên tv nếu có dấu hiệu season/episode

    Lampa.TMDB.search({ query: title }, function (items) {
      var movieGroup = items.filter(function (g) { return g.type === 'movie'; })[0];
      var tvGroup = items.filter(function (g) { return g.type === 'tv'; })[0];

      var pick = null;
      var pickType = null;

      if (preferType === 'tv' && tvGroup && tvGroup.results && tvGroup.results.length) {
        pick = tvGroup.results[0]; pickType = 'tv';
      } else if (movieGroup && movieGroup.results && movieGroup.results.length) {
        pick = movieGroup.results[0]; pickType = 'movie';
      } else if (tvGroup && tvGroup.results && tvGroup.results.length) {
        pick = tvGroup.results[0]; pickType = 'tv';
      }

      if (!pick) { callback(null); return; }

      log('search TMDB theo ten "' + title + '" -> khop:', pick.title || pick.name, '(' + pickType + ', tmdb_id=' + pick.id + ')');
      Lampa.TMDB.external_imdb_id({ id: pick.id, type: pickType }, function (foundImdbId) {
        callback(foundImdbId || null, pick.id, pickType);
      });
    });
  }

  // Gộp bước cuối: có imdb_id rồi, nếu có kèm tvId (tmdb) thì sửa lại số tập
  // cho đúng (dựa vào tên tập) trước khi fetch sub, vì field episode gốc
  // không đáng tin (xem ghi chú resolveEpisodeNumberByTitle).
  function resolveAndFetch(imdbId, tvId, playData) {
    var se = getSeasonEpisode(playData);

    function finish(season, episode) {
      fetchAndResolve(imdbId, season, episode, playData, function (tracks) {
        if (!tracks.length) { log('khong co sub / khong resolve duoc ban nao'); return; }
        safeSetSubtitles(tracks);
      });
    }

    if (se.confident) {
      // season/episode lấy trực tiếp từ URL stream - đáng tin, dùng thẳng
      finish(se.season, se.episode);
      return;
    }

    if (tvId && se.season > 0 && playData.title) {
      // field gốc không đáng tin nhưng có card để verify qua TMDB
      resolveEpisodeNumberByTitle(tvId, se.season, playData.title, se.episode, function (realEpisode) {
        finish(se.season, realEpisode);
      });
      return;
    }

    if (se.season > 0) {
      // biết season nhưng field episode không đáng tin và không verify được
      // -> coi như không biết tập, để fetchAndResolve tự tải nguyên season
      log('field episode khong dang tin va khong verify duoc, tai nguyen season thay vi doan bua');
      finish(se.season, 0);
      return;
    }

    // phim lẻ (season=0) hoặc không xác định được gì thêm - dùng nguyên field
    finish(se.season, se.episode);
  }

  function attachAutoSub(playData) {
    log('DEBUG play data:', playData);

    var imdbId = extractImdbId(playData);
    var tmdbInfo = getTmdbInfo(playData);

    if (imdbId) {
      resolveAndFetch(imdbId, tmdbInfo && tmdbInfo.type === 'tv' ? tmdbInfo.id : null, playData);
      return;
    }

    // Không có imdb_id sẵn -> thử tra ngược qua TMDB id trước khi bỏ cuộc
    if (tmdbInfo) {
      log('khong co imdb_id truc tiep, dang tra qua TMDB id', tmdbInfo.id, '(' + tmdbInfo.type + ')...');
      Lampa.TMDB.external_imdb_id({ id: tmdbInfo.id, type: tmdbInfo.type }, function (foundImdbId) {
        if (foundImdbId) {
          log('tra duoc imdb_id qua TMDB id:', foundImdbId);
          resolveAndFetch(foundImdbId, tmdbInfo.type === 'tv' ? tmdbInfo.id : null, playData);
        } else {
          trySearchByTitle();
        }
      });
      return;
    }

    trySearchByTitle();

    function trySearchByTitle() {
      log('khong co card/imdb/tmdb id, thu search theo ten:', playData && playData.title);
      searchImdbByTitle(playData, function (foundImdbId, foundTmdbId, foundType) {
        if (!foundImdbId) {
          log('search theo ten cung khong ra, bo qua phim:', playData && playData.title);
          return;
        }
        log('tra duoc imdb_id qua search ten:', foundImdbId);
        resolveAndFetch(foundImdbId, foundType === 'tv' ? foundTmdbId : null, playData);
      });
    }
  }

  // Fix chuẩn cho player NỘI BỘ: dùng đúng event nội bộ Lampa bắn ra khi video
  // engine sẵn sàng (sau preroll quảng cáo nếu có).
  Lampa.Player.listener.follow('ready', function (data) {
    if (data) {
      lastPlayData = data; // luu lai de nut "Tai phu de" o menu dung lai duoc
      attachAutoSub(data);
    } else {
      log('event ready khong co data, bo qua tu dong gan sub');
    }
  });

  // resolve toàn bộ danh sách thành link http thật (dùng cho MX Player/external)
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

  // Base64url không padding - đúng format Android Base64.URL_SAFE|NO_PADDING|NO_WRAP
  // để khớp với app bridge (torrshelf-player) đọc subtitleMeta.
  function base64UrlEncode(str) {
    var utf8 = unescape(encodeURIComponent(str));
    var b64 = btoa(utf8);
    return b64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }

  // Nối engine=mx + subtitleMeta (1 param/sub) vào URL stream gốc.
  function buildMxLink(originalLink, tracks) {
    var parts = ['engine=mx'];
    tracks.forEach(function (t) {
      var json = JSON.stringify({ url: t.url, label: t.label, language: t.language });
      parts.push('subtitleMeta=' + base64UrlEncode(json));
    });
    var sep = originalLink.indexOf('?') === -1 ? '?' : '&';
    return originalLink + sep + parts.join('&');
  }

  // Fix cho MX Player / external: chặn Lampa.Android.openPlayer(), CHỜ lấy +
  // resolve xong sub thành link thật rồi mới mở MX Player (delay vài giây,
  // đổi lại chắc chắn có sub thay vì mở ngay không kịp gắn).
  // Timeout an toàn: nếu quá 8s vẫn chưa xong thì mở MX Player luôn (không sub)
  // để tránh treo app nếu addon SubSense chậm/lỗi.
  // LƯU Ý: cần app bridge Android đã patch handleIntent() đọc engine/subtitleMeta
  // trên URL http thường (không chỉ scheme torrshelf-player://) mới ăn.
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

      function openNow(finalLink) {
        if (opened) return;
        opened = true;
        clearTimeout(timeoutId);
        if (finalLink) {
          originalOpenPlayer.call(self, finalLink, data);
        } else {
          originalOpenPlayer.apply(self, args);
        }
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
          var mxLink = buildMxLink(link, tracks);
          log('MX Player: da ghep', tracks.length, 'sub vao link, mo MX Player');
          log('DEBUG link gui qua MX:', mxLink);
          openNow(mxLink);
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

  // Debug thủ công: lấy link http thật cho 1 sub (dùng cho MX Player)
  // Cách test tay trong console:
  //   SubSensePlugin.getExternalLink(someSubObject, function(url, err){ console.log(url, err); })
  function getExternalLink(sub, callback) {
    resolveToExternalUrl(sub, function (url, err) {
      if (url) log('link ngoai:', url);
      else log('loi tao link ngoai:', err);
      callback(url, err);
    });
  }

  // Cố gắng bắt sự kiện mở player ngoài (MX Player...) để gắn link sub thật vào đó.
  // LƯU Ý: tên event 'external'/'player_external' là ĐOÁN theo quy ước Lampa thường dùng,
  // chưa xác nhận chạy thật. Nếu không thấy log nào bắt đầu bằng
  // "[SubSense] bat duoc su kien external" khi ní bấm "mo bang MX Player",
  // gửi tui xem trong console có event nào tên khác không (tìm trong Network/Console
  // lúc bấm nút mở external, hoặc gửi tui đoạn code Lampa xử lý nút đó).
  try {
    Lampa.Listener.follow('player', function (e) {
      if (e.type === 'external' || e.type === 'open_external') {
        log('bat duoc su kien external:', e);
      }
    });
  } catch (err) {
    log('khong gan duoc listener external:', err);
  }

  // === Settings + Menu: bọc trong hàm riêng, tự thử lại nếu Lampa app chưa
  // sẵn sàng (Lampa.Menu.addButton cần html noi bo da duoc khoi tao, goi qua
  // som se loi am tham va lam dung ca doan code phia sau trong cung script).
  var uiRegistered = false;

  function registerUI(attempt) {
    if (uiRegistered) return;
    attempt = attempt || 0;
    try {
      Lampa.SettingsApi.addComponent({
        component: 'subsense',
        name: 'SubSense',
        icon: '<svg width="20" height="20" viewBox="0 0 20 20"><path d="M2 4h16v12H2z" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="M5 8h4M5 11h7" stroke="currentColor" stroke-width="1.5"/></svg>'
      });

      Lampa.SettingsApi.addParam({
        component: 'subsense',
        param: {
          name: 'subsense_manifest',
          type: 'input',
          values: '', // bắt buộc phải có, thiếu sẽ crash Settings (đã xác nhận qua source)
          "default": DEFAULT_SUBSENSE_BASE + '/manifest.json'
        },
        field: {
          name: 'Manifest URL',
          description: 'Link manifest.json cua addon SubSense (Stremio)'
        }
      });

      log('da dang ky Settings component thanh cong');

      // Menu trái: nút "Tai phu de" - chạy lại quy trình tự động cho phim
      // đang phát hiện tại. Dùng lại data đã lưu từ event 'ready' gần nhất.
      Lampa.Menu.addButton(
        '<svg width="20" height="20" viewBox="0 0 20 20"><path d="M2 4h16v12H2z" fill="none" stroke="currentColor" stroke-width="1.5"/><path d="M5 8h4M5 11h7" stroke="currentColor" stroke-width="1.5"/></svg>',
        'Tai phu de',
        function () {
          if (!lastPlayData) {
            Lampa.Noty.show('Chua co phim nao dang phat, mo phim len truoc');
            return;
          }
          log('Tai lai sub thu cong cho phim dang phat...');
          attachAutoSub(lastPlayData);
        }
      );

      log('da them nut menu thanh cong');
      uiRegistered = true;
    } catch (err) {
      log('loi dang ky Settings/Menu (lan ' + attempt + '):', err.message);
      if (attempt < 10) {
        setTimeout(function () { registerUI(attempt + 1); }, 1000);
      } else {
        log('bo cuoc dang ky Settings/Menu sau 10 lan thu');
      }
    }
  }

  // Ưu tiên đợi đúng lúc app Lampa báo sẵn sàng, kèm fallback retry nếu vì
  // lý do gì đó event 'app'/'ready' không bắn ra hoặc đã bắn qua trước khi
  // plugin kịp gắn listener (trường hợp plugin load muộn hơn app).
  try {
    Lampa.Listener.follow('app', function (e) {
      if (e.type === 'ready') registerUI();
    });
  } catch (err) {
    log('khong gan duoc listener app/ready:', err.message);
  }
  registerUI();

  // === Xử lý "Lampa không cho lưu file": 2 hướng, chọn tuỳ nhu cầu ===
  // Hướng 1 (đang dùng, mặc định): upload tạm lên catbox.moe (đã có sẵn ở
  // resolveToExternalUrl/uploadToCatbox phía trên) - không cần tự host gì.
  //
  // Hướng 2 (tự host qua chính Termux của ní, không phụ thuộc bên thứ 3):
  // Vì Lampac/TorrServer ní đã chạy sẵn trên Termux, có thể viết thêm 1 endpoint
  // nhỏ (Node.js hoặc thêm route vào chính server sẵn có) kiểu:
  //   POST /subsense/upload  { text: "...", filename: "sub.srt" }
  //   -> lưu file tạm vào thư mục, trả về { url: "http://<ip-lan>:<port>/subsense/<id>.srt" }
  // rồi phía JS gọi endpoint đó thay vì catbox. Ưu điểm: không phụ thuộc
  // dịch vụ ngoài, sub không rời khỏi mạng nhà. Nhược điểm: cần tự viết +
  // chạy thêm 1 service, và chỉ dùng được khi Lampa/điện thoại cùng mạng LAN
  // với Termux (không phải lúc nào cũng đúng nếu Lampa chạy qua Cloudflare
  // Tunnel/domain public). Báo tui nếu muốn tui viết endpoint đó, tui sẽ cần
  // biết ní đang chạy Lampac bằng framework gì (Node/Python/khác) để nối vào
  // đúng chỗ thay vì tạo 1 service tách rời.

  window.SubSensePlugin = {
    fetch: fetchSubs,
    resolveToVtt: resolveToVtt,
    resolveAll: resolveAll,
    resolveToExternalUrl: resolveToExternalUrl,
    getExternalLink: getExternalLink,
    attachAutoSub: attachAutoSub
  };
})();
