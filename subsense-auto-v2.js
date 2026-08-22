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

  // Tra ngược imdb_id qua TMDB id khi data play không có sẵn imdb_id.
  // Lampa.TMDB.external_imdb_id dùng chung API key Lampa đã cấu hình.
  function getTmdbInfo(playData) {
    var card = playData && playData.card;
    if (!card || !card.id) return null;
    var isTv = !!(card.first_air_date !== undefined || card.name || card.number_of_seasons);
    return { id: card.id, type: isTv ? 'tv' : 'movie' };
  }

  function proceedWithImdb(imdbId, playData) {
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

    // phim bộ: season/episode đã có sẵn trong data play, khỏi hỏi tay nữa
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

  function attachAutoSub(playData) {
    log('DEBUG play data:', playData);

    var imdbId = extractImdbId(playData);
    if (imdbId) {
      proceedWithImdb(imdbId, playData);
      return;
    }

    // Không có imdb_id sẵn -> thử tra ngược qua TMDB id trước khi bỏ cuộc
    var tmdbInfo = getTmdbInfo(playData);
    if (!tmdbInfo) {
      log('khong co imdb_id lan tmdb_id, bo qua phim:', playData && playData.title);
      return;
    }

    log('khong co imdb_id truc tiep, dang tra qua TMDB id', tmdbInfo.id, '(' + tmdbInfo.type + ')...');
    Lampa.TMDB.external_imdb_id({ id: tmdbInfo.id, type: tmdbInfo.type }, function (foundImdbId) {
      if (!foundImdbId) {
        log('TMDB khong tra ra imdb_id, bo qua phim:', playData && playData.title);
        return;
      }
      log('tra duoc imdb_id qua TMDB:', foundImdbId);
      proceedWithImdb(foundImdbId, playData);
    });
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
