/*
 * Auto Tracks — tự chọn audio + phụ đề theo ngôn ngữ ưa thích
 * -----------------------------------------------------------
 * Khi player nạp xong danh sách track, plugin tự động:
 *  - chọn audio đúng ngôn ngữ cài đặt (mặc định: Tiếng Anh)
 *  - bật phụ đề đúng ngôn ngữ cài đặt (mặc định: Tiếng Việt)
 *
 * Hoạt động với audio track của HLS/DASH/native video và phụ đề nhúng
 * lẫn phụ đề ngoài (StremioSub/SubSense gắn qua Player.subtitles cũng
 * bắn sự kiện 'subs' nên được chọn tự động luôn).
 *
 * Cài đặt -> Trình phát:
 *  - "Tự chọn audio":  Tắt / Anh / Việt / Nhật / Hàn / Trung / Nga
 *  - "Tự bật phụ đề":  Tắt / Việt / Anh
 *
 * Nếu track đang chọn sẵn đã đúng ngôn ngữ thì không đụng vào.
 */
(function () {
  'use strict';

  if (window.lampac_autotracks) return;
  window.lampac_autotracks = true;

  var SET_AUDIO = 'autotracks_audio';
  var SET_SUB = 'autotracks_sub';

  var LANGS = {
    en: ['en', 'eng', 'english', 'tieng anh', 'tiếng anh'],
    vi: ['vi', 'vie', 'viet', 'vietnamese', 'tieng viet', 'tiếng việt', 'việt'],
    ja: ['ja', 'jpn', 'japanese', 'nhat', 'nhật'],
    ko: ['ko', 'kor', 'korean', 'han', 'hàn'],
    zh: ['zh', 'chi', 'zho', 'chinese', 'mandarin', 'trung'],
    ru: ['ru', 'rus', 'russian', 'nga']
  };

  function log() {
    var args = ['[AutoTracks]'].concat(Array.prototype.slice.call(arguments));
    console.log.apply(console, args);
  }

  function storageGet(name, def) {
    var v = Lampa.Storage.get(name, def);
    return v === undefined || v === null || v === '' ? def : v + '';
  }

  function textOf(track) {
    var parts = [];
    if (track.language) parts.push(track.language);
    if (track.lang) parts.push(track.lang); // hls.js audioTracks dùng .lang
    if (track.name) parts.push(track.name);
    if (track.label) parts.push(track.label);
    if (track.title) parts.push(track.title);
    return parts.join(' ').toLowerCase();
  }

  function matchLang(track, code) {
    var hay = textOf(track);
    if (!hay) return false;

    var words = hay.split(/[^a-z\u00C0-\u024F\u1E00-\u1EFF]+/);
    var list = LANGS[code] || [code];

    for (var i = 0; i < list.length; i++) {
      var needle = list[i];
      // Mã ngắn (vi, en, vie...) so theo NGUYÊN TỪ để 'en' không dính
      // trong 'french'; cụm dài so theo chuỗi con.
      if (needle.length <= 3 && needle.indexOf(' ') === -1) {
        if (words.indexOf(needle) >= 0) return true;
      } else if (hay.indexOf(needle) >= 0) {
        return true;
      }
    }
    return false;
  }

  /* ── Audio ── */
  function selectAudio(tracks, code) {
    if (!tracks || tracks.length < 2) return;

    var i;

    // Track đang bật đã đúng ngôn ngữ -> giữ nguyên
    for (i = 0; i < tracks.length; i++) {
      if ((tracks[i].enabled || tracks[i].selected) && matchLang(tracks[i], code)) return;
    }

    var target = null;
    for (i = 0; i < tracks.length; i++) {
      if (matchLang(tracks[i], code)) { target = tracks[i]; break; }
    }
    if (!target) {
      log('khong co audio', code, 'trong', tracks.length, 'track');
      return;
    }

    try {
      for (i = 0; i < tracks.length; i++) {
        tracks[i].enabled = false;
        tracks[i].selected = false;
      }
      target.enabled = true;
      target.selected = true;
      if (typeof target.onSelect === 'function') target.onSelect(target);
      log('da chon audio:', textOf(target) || code);
    } catch (error) {
      log('loi chon audio:', error.message || error);
    }
  }

  /* ── Subtitles ── */
  function selectSub(subs, code) {
    if (!subs || !subs.length) return;

    var i;

    // Sub đang bật đã đúng ngôn ngữ -> giữ nguyên
    for (i = 0; i < subs.length; i++) {
      if (subs[i].selected && subs[i].index !== -1 && matchLang(subs[i], code)) return;
    }

    var target = null;
    for (i = 0; i < subs.length; i++) {
      if (subs[i].index === -1) continue; // mục "Tắt"
      if (matchLang(subs[i], code)) { target = subs[i]; break; }
    }
    if (!target) {
      log('khong co sub', code, 'trong', subs.length, 'ban');
      return;
    }

    try {
      for (i = 0; i < subs.length; i++) {
        subs[i].mode = 'disabled';
        subs[i].selected = false;
      }
      target.mode = 'showing';
      target.selected = true;
      if (typeof target.onSelect === 'function') target.onSelect(target);

      if (Lampa.PlayerVideo && typeof Lampa.PlayerVideo.subsview === 'function')
        Lampa.PlayerVideo.subsview(true);

      log('da bat sub:', textOf(target) || code);
    } catch (error) {
      log('loi bat sub:', error.message || error);
    }
  }

  /* ── Settings ── */
  function installSettings() {
    if (!Lampa.SettingsApi) return;

    Lampa.SettingsApi.addParam({
      component: 'player',
      param: {
        name: SET_AUDIO,
        type: 'select',
        values: {
          off: 'Tắt',
          en: 'Tiếng Anh',
          vi: 'Tiếng Việt',
          ja: 'Tiếng Nhật',
          ko: 'Tiếng Hàn',
          zh: 'Tiếng Trung',
          ru: 'Tiếng Nga'
        },
        default: 'en'
      },
      field: {
        name: 'Tự chọn audio',
        description: 'Tự chuyển sang track audio đúng ngôn ngữ khi phim có nhiều track.'
      }
    });

    Lampa.SettingsApi.addParam({
      component: 'player',
      param: {
        name: SET_SUB,
        type: 'select',
        values: {
          off: 'Tắt',
          vi: 'Tiếng Việt',
          en: 'Tiếng Anh'
        },
        default: 'vi'
      },
      field: {
        name: 'Tự bật phụ đề',
        description: 'Tự bật phụ đề đúng ngôn ngữ (kể cả phụ đề tự tải từ SubDL/SubSource).'
      }
    });
  }

  /* ── Hook player events ── */
  function start() {
    if (start.done) return;
    start.done = true;

    installSettings();

    if (!Lampa.PlayerVideo || !Lampa.PlayerVideo.listener || typeof Lampa.PlayerVideo.listener.follow !== 'function') {
      log('PlayerVideo.listener khong kha dung — bo qua');
      return;
    }

    Lampa.PlayerVideo.listener.follow('tracks', function (e) {
      var code = storageGet(SET_AUDIO, 'en');
      if (code === 'off') return;
      // setTimeout: để Lampa xử lý xong track mặc định rồi mới đổi
      setTimeout(function () { selectAudio(e && e.tracks, code); }, 0);
    });

    Lampa.PlayerVideo.listener.follow('subs', function (e) {
      var code = storageGet(SET_SUB, 'vi');
      if (code === 'off') return;
      setTimeout(function () { selectSub(e && e.subs, code); }, 0);
    });

    log('plugin ready');
  }

  function waitForLampa() {
    if (typeof Lampa === 'undefined' || !Lampa.Player || !Lampa.Listener) {
      setTimeout(waitForLampa, 300);
      return;
    }

    if (window.appready) {
      start();
      return;
    }

    Lampa.Listener.follow('app', function (e) {
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
