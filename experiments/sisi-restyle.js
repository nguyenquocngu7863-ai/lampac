/*
 * SISI Restyle (experimental)
 * ---------------------------
 * Plugin thử nghiệm thay đổi bố cục và poster của danh sách SISI:
 *  - Poster 16:9 bo góc lớn, ảnh phủ kín khung (object-fit cover)
 *  - Tiêu đề đè lên đáy poster với dải gradient (không còn chiếm chỗ dưới card)
 *  - Lưới: 2 cột trên điện thoại dọc, 3 cột màn nhỏ ngang, 4 cột màn lớn
 *  - Badge thời lượng/chất lượng nổi trên poster
 *  - Viền focus bám sát bo góc mới
 *
 * Chỉ tác động khi activity hiện tại là component của SISI (sisi_view_*,
 * sisi_main_*, ...) — các màn hình khác của Lampa giữ nguyên.
 *
 * Bật/tắt trong: Cài đặt -> Giao diện -> "SISI kiểu mới (thử nghiệm)".
 */
(function () {
  'use strict';

  if (window.sisi_restyle_plugin) return;
  window.sisi_restyle_plugin = true;

  var settingName = 'sisi_restyle';
  var styleId = 'sisi-restyle-style';
  var bodyClass = 'sisi-restyle';

  function enabled() {
    if (!window.Lampa || !Lampa.Storage) return true;
    var value = Lampa.Storage.get(settingName, 'true');
    return value !== false && value !== 'false';
  }

  function inSisi() {
    try {
      var activity = Lampa.Activity.active();
      return !!(activity && typeof activity.component == 'string' && activity.component.indexOf('sisi') === 0);
    } catch (e) {
      return false;
    }
  }

  function apply() {
    if (!document.body) return;
    document.body.classList.toggle(bodyClass, enabled() && inSisi());
  }

  function installStyle() {
    if (document.getElementById(styleId)) return;

    var style = document.createElement('style');
    style.id = styleId;
    style.textContent = [
      // ── card + poster ──────────────────────────────────────────────
      'body.sisi-restyle .card.card--collection {',
      '  padding-bottom: 1.1em !important;',
      '}',
      'body.sisi-restyle .card.card--collection .card__view {',
      '  padding-bottom: 56.25% !important;', /* 16:9 */
      '  margin-bottom: 0 !important;',
      '  border-radius: 1.1em !important;',
      '  overflow: hidden !important;',
      '  -webkit-transform: translateZ(0);',
      '  transform: translateZ(0);',
      '}',
      'body.sisi-restyle .card.card--collection .card__img {',
      '  border-radius: 1.1em !important;',
      '  width: 100% !important;',
      '  height: 100% !important;',
      '  -o-object-fit: cover !important;',
      '  object-fit: cover !important;',
      '}',
      // ── tiêu đề đè lên đáy poster với gradient ─────────────────────
      'body.sisi-restyle .card.card--collection {',
      '  position: relative !important;',
      '}',
      'body.sisi-restyle .card.card--collection .card__title {',
      '  position: absolute !important;',
      '  left: 0 !important;',
      '  right: 0 !important;',
      '  bottom: 1.1em !important;', /* trùng padding-bottom của card */
      '  margin: 0 !important;',
      '  padding: 1.6em .7em .55em !important;',
      '  font-size: 1.05em !important;',
      '  line-height: 1.25 !important;',
      '  max-height: none !important;',
      '  display: -webkit-box !important;',
      '  -webkit-box-orient: vertical !important;',
      '  -webkit-line-clamp: 2 !important;',
      '  line-clamp: 2 !important;',
      '  overflow: hidden !important;',
      '  color: #fff !important;',
      '  text-shadow: 0 1px 2px rgba(0,0,0,.8) !important;',
      '  background: -webkit-linear-gradient(top, rgba(0,0,0,0) 0%, rgba(0,0,0,.55) 45%, rgba(0,0,0,.88) 100%) !important;',
      '  background: linear-gradient(to bottom, rgba(0,0,0,0) 0%, rgba(0,0,0,.55) 45%, rgba(0,0,0,.88) 100%) !important;',
      '  border-bottom-left-radius: 1.1em !important;',
      '  border-bottom-right-radius: 1.1em !important;',
      '  z-index: 2 !important;',
      '  pointer-events: none !important;',
      '}',
      // Tuổi/mô tả phụ dưới card không còn chỗ -> ẩn
      'body.sisi-restyle .card.card--collection .card__age {',
      '  display: none !important;',
      '}',
      // ── badge thời lượng / chất lượng nổi trên poster ──────────────
      'body.sisi-restyle .card.card--collection .card__quality {',
      '  position: absolute !important;',
      '  top: .55em !important;',
      '  right: .55em !important;',
      '  left: auto !important;',
      '  bottom: auto !important;',
      '  background: rgba(0,0,0,.72) !important;',
      '  color: #fff !important;',
      '  padding: .25em .5em !important;',
      '  border-radius: .55em !important;',
      '  font-size: .78em !important;',
      '  z-index: 3 !important;',
      '}',
      // ── viền focus bám theo bo góc mới ─────────────────────────────
      'body.sisi-restyle .card.card--collection.focus .card__view::after,',
      'body.sisi-restyle .card.card--collection.hover .card__view::after {',
      '  top: -.35em !important;',
      '  left: -.35em !important;',
      '  right: -.35em !important;',
      '  bottom: -.35em !important;',
      '  border-width: .25em !important;',
      '  border-radius: 1.45em !important;',
      '}',
      // Video preview (khi focus) bo góc khớp poster
      'body.sisi-restyle .sisi-video-preview,',
      'body.sisi-restyle .sisi-video-preview video {',
      '  border-radius: 1.1em !important;',
      '}',
      // ── lưới: 2 cột dọc / 3 cột nhỏ ngang / 4 cột lớn ──────────────
      'body.sisi-restyle .card.card--collection { width: 25% !important; }',
      '@media screen and (max-width: 991px) {',
      '  body.sisi-restyle .card.card--collection { width: 33.333% !important; }',
      '}',
      '@media screen and (orientation: portrait) and (max-width: 720px) {',
      '  body.sisi-restyle .card.card--collection { width: 50% !important; }',
      '  body.sisi-restyle .card.card--collection .card__title { font-size: 1em !important; }',
      '}',
      '@media screen and (orientation: portrait) and (max-width: 390px) {',
      '  body.sisi-restyle .card.card--collection { width: 50% !important; }',
      '  body.sisi-restyle .card.card--collection .card__title { font-size: .95em !important; }',
      '}'
    ].join('\n');
    document.head.appendChild(style);
  }

  function installSetting() {
    if (!window.Lampa || !Lampa.SettingsApi || window.sisi_restyle_setting) return;
    window.sisi_restyle_setting = true;

    Lampa.SettingsApi.addParam({
      component: 'interface',
      param: {
        name: settingName,
        type: 'trigger',
        values: '',
        default: true
      },
      field: {
        name: 'SISI kiểu mới (thử nghiệm)',
        description: 'Poster 16:9 bo góc lớn, tiêu đề đè gradient trên poster, lưới 2 cột ở màn dọc.'
      },
      onChange: function () {
        setTimeout(apply, 0);
      }
    });
  }

  function install() {
    if (!window.Lampa || !document.head || !document.body) {
      setTimeout(install, 250);
      return;
    }

    installStyle();
    installSetting();
    apply();

    // Bật/tắt class theo activity: chỉ style khi đang ở màn SISI
    if (Lampa.Listener && Lampa.Listener.follow) {
      Lampa.Listener.follow('activity', function () {
        setTimeout(apply, 0);
      });
    }

    if (Lampa.Storage && Lampa.Storage.listener) {
      Lampa.Storage.listener.follow('change', function (event) {
        if (event.name === settingName) apply();
      });
    }

    // Dự phòng: một số bản Lampa không phát đủ sự kiện activity
    setInterval(apply, 2000);
  }

  install();
})();
