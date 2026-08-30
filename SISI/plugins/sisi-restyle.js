/*
 * SISI Restyle
 * ------------
 * Plugin tích hợp sẵn (serve tại /sisi-restyle.js, tự đăng ký khi
 * initPlugins.sisi bật) thay đổi bố cục và poster của danh sách SISI:
 *  - Poster 16:9 bo góc lớn, ảnh phủ kín khung (object-fit cover)
 *  - Tiêu đề nằm DƯỚI poster như bố cục gốc (giữ nguyên size font gốc)
 *  - Badge thời lượng/chất lượng giữ nguyên kiểu mặc định của Lampa
 *  - Lưới mặc định: 2 cột (điện thoại dọc) / 3 cột / 4 cột (màn lớn)
 *  - Người dùng tự chọn số CỘT và số HÀNG hiển thị trong Cài đặt
 *
 * Chỉ tác động khi activity hiện tại là component của SISI (sisi_view_*,
 * sisi_main_*, ...) — các màn hình khác của Lampa giữ nguyên.
 *
 * Cài đặt -> Giao diện:
 *  - "SISI kiểu mới (thử nghiệm)": bật/tắt toàn bộ
 *  - "SISI: số cột": Tự động / 2..5 cột
 *  - "SISI: số hàng trên màn hình": Tự động (16:9) / 2..4 hàng
 *    (chọn số hàng thì poster co giãn chiều cao để đủ N hàng trong một màn)
 */
(function () {
  'use strict';

  if (window.sisi_restyle_plugin) return;
  window.sisi_restyle_plugin = true;

  var settingName = 'sisi_restyle';
  var settingCols = 'sisi_restyle_cols';
  var settingRows = 'sisi_restyle_rows';
  var styleId = 'sisi-restyle-style';
  var bodyClass = 'sisi-restyle';

  function storageGet(name, def) {
    if (!window.Lampa || !Lampa.Storage) return def;
    var v = Lampa.Storage.get(name, def);
    return v === undefined || v === null || v === '' ? def : v + '';
  }

  function enabled() {
    var value = storageGet(settingName, 'true');
    return value !== 'false';
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

    var active = enabled() && inSisi();
    var cols = storageGet(settingCols, 'auto');
    var rows = storageGet(settingRows, 'auto');

    document.body.classList.toggle(bodyClass, active);
    document.body.classList.toggle('sisi-cols-fixed', active && cols !== 'auto');
    document.body.classList.toggle('sisi-rows-fixed', active && rows !== 'auto');

    if (active && cols !== 'auto') document.body.style.setProperty('--sisi-cols', cols);
    else document.body.style.removeProperty('--sisi-cols');

    if (active && rows !== 'auto') document.body.style.setProperty('--sisi-rows', rows);
    else document.body.style.removeProperty('--sisi-rows');
  }

  function installStyle() {
    if (document.getElementById(styleId)) return;

    var style = document.createElement('style');
    style.id = styleId;
    style.textContent = [
      // ── poster 16:9 bo góc lớn ─────────────────────────────────────
      'body.sisi-restyle .card.card--collection {',
      '  padding-bottom: 1.1em !important;',
      '}',
      'body.sisi-restyle .card.card--collection .card__view {',
      '  padding-bottom: 56.25% !important;', /* 16:9 */
      '  margin-bottom: .7em !important;',
      '  border-radius: 1.1em !important;',
      '}',
      'body.sisi-restyle .card.card--collection .card__img {',
      '  border-radius: 1.1em !important;',
      '  width: 100% !important;',
      '  height: 100% !important;',
      '  -o-object-fit: cover !important;',
      '  object-fit: cover !important;',
      '}',
      // Tiêu đề: nằm dưới poster như gốc, KHÔNG đổi font — không override.
      // Badge thời lượng/chất lượng: giữ NGUYÊN kiểu mặc định của Lampa
      // (nền vàng, nhô ra mép trái poster) — không override, và không dùng
      // overflow hidden trên card__view để badge không bị cắt.
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
      // ── lưới TỰ ĐỘNG: 2 cột dọc / 3 cột nhỏ ngang / 4 cột lớn ─────
      'body.sisi-restyle .card.card--collection { width: 25% !important; }',
      '@media screen and (max-width: 991px) {',
      '  body.sisi-restyle .card.card--collection { width: 33.333% !important; }',
      '}',
      '@media screen and (orientation: portrait) and (max-width: 720px) {',
      '  body.sisi-restyle .card.card--collection { width: 50% !important; }',
      '}',
      // ── số CỘT do người dùng chọn (đè lên lưới tự động) ────────────
      'body.sisi-restyle.sisi-cols-fixed .card.card--collection {',
      '  width: calc(100% / var(--sisi-cols, 3)) !important;',
      '}',
      // ── số HÀNG do người dùng chọn: poster co chiều cao để đủ ──────
      //    N hàng (poster + tiêu đề) trong một màn hình
      'body.sisi-restyle.sisi-rows-fixed .card.card--collection .card__view {',
      '  padding-bottom: 0 !important;',
      '  height: calc((100vh - 9em) / var(--sisi-rows, 3) - 4.6em) !important;',
      '  min-height: 6em !important;',
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
        description: 'Poster 16:9 bo góc lớn, tiêu đề và badge giữ kiểu mặc định.'
      },
      onChange: function () {
        setTimeout(apply, 0);
      }
    });

    Lampa.SettingsApi.addParam({
      component: 'interface',
      param: {
        name: settingCols,
        type: 'select',
        values: {
          auto: 'Tự động',
          2: '2 cột',
          3: '3 cột',
          4: '4 cột',
          5: '5 cột'
        },
        default: 'auto'
      },
      field: {
        name: 'SISI: số cột',
        description: 'Số cột của lưới video SISI. Tự động: 2 cột màn dọc, 3-4 cột màn ngang.'
      },
      onChange: function () {
        setTimeout(apply, 0);
      }
    });

    Lampa.SettingsApi.addParam({
      component: 'interface',
      param: {
        name: settingRows,
        type: 'select',
        values: {
          auto: 'Tự động (16:9)',
          2: '2 hàng',
          3: '3 hàng',
          4: '4 hàng'
        },
        default: 'auto'
      },
      field: {
        name: 'SISI: số hàng trên màn hình',
        description: 'Ép đủ N hàng card trong một màn hình (poster co giãn chiều cao). Tự động: giữ tỉ lệ 16:9.'
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
        if (event.name === settingName || event.name === settingCols || event.name === settingRows) apply();
      });
    }

    // Dự phòng: một số bản Lampa không phát đủ sự kiện activity
    setInterval(apply, 2000);
  }

  install();
})();
