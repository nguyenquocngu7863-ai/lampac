/*
 * SISI layout addon
 *
 * The addon deliberately scopes both its CSS and the category pagination hint
 * to the SISI activities. Normal Lampa catalog pages continue to use the
 * stock category layout and the stock seven-column loading behaviour.
 */
(function () {
  'use strict';

  // lampainit.js can see the same plugin more than once when a device has an
  // older persisted URL. Keep the settings component and listeners singleton.
  if (window.lampac_sisi_layout_plugin) return;
  window.lampac_sisi_layout_plugin = true;

  var STORAGE = {
    columns: 'sisi_layout_columns',
    rows: 'sisi_layout_rows',
    poster: 'sisi_layout_poster'
  };
  var SCOPE_CLASS = 'lampac-sisi-layout';
  var STYLE_ID = 'lampac-sisi-layout-style';
  var styleNode;
  var DEFAULTS = {
    columns: 5,
    rows: 3,
    poster: 'large'
  };
  var posterRatios = {
    small: '135%',
    normal: '150%',
    large: '165%'
  };

  function numberSetting(name, fallback, min, max) {
    var value = parseInt(Lampa.Storage.get(name, fallback), 10);
    if (isNaN(value)) value = fallback;
    return Math.max(min, Math.min(max, value));
  }

  function settings() {
    var poster = String(Lampa.Storage.get(STORAGE.poster, DEFAULTS.poster) || DEFAULTS.poster);
    if (!posterRatios[poster]) poster = DEFAULTS.poster;

    return {
      columns: numberSetting(STORAGE.columns, DEFAULTS.columns, 3, 8),
      rows: numberSetting(STORAGE.rows, DEFAULTS.rows, 1, 6),
      poster: poster
    };
  }

  function renderStyle(value) {
    if (!styleNode) return;

    // Use concrete values instead of relying only on CSS custom properties:
    // some older TV WebViews do not support var() in a width declaration.
    var width = (100 / value.columns).toFixed(6) + '%';
    styleNode.textContent = [
      /* Category pages are the SISI grid. Do not touch .card globally. */
      'body.' + SCOPE_CLASS + ' .category-full .card--category {',
      '  width: ' + width + ' !important;',
      '}',
      'body.' + SCOPE_CLASS + ' .category-full .card--category .card__view {',
      '  padding-bottom: ' + posterRatios[value.poster] + ' !important;',
      '}',
      /* Keep titles readable when a larger poster is selected. */
      'body.' + SCOPE_CLASS + ' .category-full .card--category .card__title {',
      '  max-height: 3.6em;',
      '}'
      // Collection cards used by SISI home/preview lines intentionally
      // retain their landscape ratio and stock line width.
    ].join('\n');
  }

  function installStyle() {
    styleNode = document.getElementById(STYLE_ID);

    if (!styleNode) {
      styleNode = document.createElement('style');
      styleNode.id = STYLE_ID;
      styleNode.type = 'text/css';
      document.head.appendChild(styleNode);
    }

    renderStyle(DEFAULTS);
  }

  function isSisiComponent(component) {
    return /^sisi(?:_view)?_[^\s]+$/i.test(String(component || ''));
  }

  function eventComponent(event) {
    if (!event) return '';
    if (event.component) return event.component;
    if (event.object) {
      if (event.object.component) return event.object.component;
      if (event.object.activity && event.object.activity.component) return event.object.activity.component;
    }
    return '';
  }

  function applyScope(active) {
    if (!document.body) return;

    var body = document.body;
    body.classList.toggle(SCOPE_CLASS, !!active);

    if (active) {
      var value = settings();
      renderStyle(value);
      // The shared Category component reads this value only while the class
      // above is present. It is not a global catalog setting.
      window.lampac_sisi_layout_columns = value.columns;
      window.lampac_sisi_layout_rows = value.rows;
    }
  }

  function activeSisiActivity() {
    // The class is the authoritative state because it works on old Lampa
    // builds that do not expose Activity.active().
    if (document.body && document.body.classList.contains(SCOPE_CLASS)) return true;

    try {
      if (Lampa.Activity && typeof Lampa.Activity.active === 'function') {
        var activity = Lampa.Activity.active();
        return !!(activity && isSisiComponent(activity.component));
      }
    } catch (e) {}
    return false;
  }

  function applyCurrentSettings() {
    var active = activeSisiActivity();
    if (active) applyScope(true);
  }

  function installSettings() {
    if (!Lampa.SettingsApi || window.lampac_sisi_layout_settings) return;
    window.lampac_sisi_layout_settings = true;

    Lampa.SettingsApi.addComponent({
      component: 'sisi_layout',
      name: '18+ / SISI — giao diện',
      icon: '<svg viewBox="0 0 24 24"><path fill="currentColor" d="M4 3h6v6H4V3m10 0h6v6h-6V3M4 13h6v6H4v-6m10 0h6v6h-6v-6Z"/></svg>'
    });

    Lampa.SettingsApi.addParam({
      component: 'sisi_layout',
      param: {
        name: STORAGE.columns,
        type: 'select',
        values: {
          '3': '3 cột — poster rất lớn',
          '4': '4 cột — poster lớn',
          '5': '5 cột — khuyến nghị',
          '6': '6 cột — poster vừa',
          '7': '7 cột — mặc định Lampa',
          '8': '8 cột — poster nhỏ'
        },
        default: String(DEFAULTS.columns)
      },
      field: {
        name: 'Số cột poster',
        description: 'Chỉ áp dụng cho lưới danh sách 18+ / SISI.'
      },
      onChange: applyCurrentSettings
    });

    Lampa.SettingsApi.addParam({
      component: 'sisi_layout',
      param: {
        name: STORAGE.rows,
        type: 'select',
        values: {
          '1': '1 hàng',
          '2': '2 hàng',
          '3': '3 hàng — khuyến nghị',
          '4': '4 hàng',
          '5': '5 hàng',
          '6': '6 hàng'
        },
        default: String(DEFAULTS.rows)
      },
      field: {
        name: 'Số hàng / lần tải',
        description: 'SISI tải thêm sau số hàng này; số card tương ứng bằng số cột nhân số hàng.'
      },
      onChange: applyCurrentSettings
    });

    Lampa.SettingsApi.addParam({
      component: 'sisi_layout',
      param: {
        name: STORAGE.poster,
        type: 'select',
        values: {
          small: 'Thấp — 135%',
          normal: 'Chuẩn — 150%',
          large: 'Lớn — 165% (khuyến nghị)'
        },
        default: DEFAULTS.poster
      },
      field: {
        name: 'Chiều cao poster',
        description: 'Tăng chiều cao poster dọc mà không làm thay đổi các line preview của SISI.'
      },
      onChange: applyCurrentSettings
    });
  }

  function installActivityScope() {
    if (!Lampa.Listener || window.lampac_sisi_layout_activity) return;
    window.lampac_sisi_layout_activity = true;

    Lampa.Listener.follow('activity', function (event) {
      var component = eventComponent(event);

      if (event.type === 'start') {
        applyScope(isSisiComponent(component));
      } else if ((event.type === 'stop' || event.type === 'destroy') && isSisiComponent(component)) {
        applyScope(false);
      }
    });
  }

  function install() {
    if (typeof Lampa === 'undefined' || !Lampa.Storage || !document.body || !document.head) {
      setTimeout(install, 250);
      return;
    }

    installStyle();
    installSettings();
    installActivityScope();
    applyScope(false);
  }

  install();
})();
