(function () {
  'use strict';

  if (window.lampac_online_compact_plugin) return;
  window.lampac_online_compact_plugin = true;

  var settingName = 'lampac_online_compact';
  var styleId = 'lampac-online-compact-style';

  function enabled() {
    if (!window.Lampa || !Lampa.Storage) return true;
    var value = Lampa.Storage.get(settingName, 'true');
    return value !== false && value !== 'false';
  }

  function apply() {
    if (!document.body) return;
    document.body.classList.toggle('lampac-online-compact', enabled());
  }

  function installStyle() {
    if (document.getElementById(styleId)) return;

    var style = document.createElement('style');
    style.id = styleId;
    style.textContent = [
      // PORTRAIT ONLY compact card. Landscape keeps the stock online.js
      // layout untouched.
      //
      // Design (mirrors the landscape card):
      //  - fixed card height -> every card identical, no stretching
      //  - three text lines total: title (1 line, ellipsis) and the
      //    info/description block on exactly TWO lines (clamped)
      //  - square 1:1 poster: width == card height, stretched to the
      //    card's full height and flush with the card's left/top/bottom
      //    edges (no gap, corners clipped by the card's own radius)
      '@media screen and (orientation: portrait) {',
      '  body.lampac-online-compact .online-prestige--full {',
      '    height: 10.4em !important;',
      '    min-height: 10.4em !important;',
      '    max-height: 10.4em !important;',
      '    align-items: stretch !important;',
      '    border-radius: .35em !important;',
      '    overflow: hidden !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full + .online-prestige--full {',
      '    margin-top: 1em !important;',
      '  }',
      // Poster: width equals the card height, height stretches to 100% of
      // the card -> always a perfect square glued to the card edges.
      '  body.lampac-online-compact .online-prestige--full .online-prestige__img {',
      '    width: 10.4em !important;',
      '    min-width: 10.4em !important;',
      '    max-width: 10.4em !important;',
      '    height: auto !important;',
      '    min-height: 0 !important;',
      '    max-height: none !important;',
      '    align-self: stretch !important;',
      '    margin: 0 !important;',
      '    border-radius: 0 !important;',
      '    overflow: hidden !important;',
      '    flex-shrink: 0 !important;',
      '    flex-grow: 0 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__img img {',
      '    position: absolute !important;',
      '    top: 0 !important;',
      '    left: 0 !important;',
      '    width: 100% !important;',
      '    height: 100% !important;',
      '    object-fit: cover !important;',
      '    object-position: center top !important;',
      '    border-radius: 0 !important;',
      '  }',
      // Body: vertically centered column, clips anything that would not fit.
      '  body.lampac-online-compact .online-prestige--full .online-prestige__body {',
      '    min-width: 0 !important;',
      '    padding: .8em 1em !important;',
      '    overflow: hidden !important;',
      '    line-height: 1.3 !important;',
      '    display: flex !important;',
      '    flex-direction: column !important;',
      '    justify-content: center !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__head {',
      '    min-width: 0 !important;',
      '    align-items: baseline !important;',
      '    gap: .6em !important;',
      '  }',
      // Text line 1: title, single line with ellipsis. Never wraps.
      '  body.lampac-online-compact .online-prestige--full .online-prestige__title {',
      '    min-width: 0 !important;',
      '    font-size: 1.4em !important;',
      '    line-height: 1.25 !important;',
      '    white-space: nowrap !important;',
      '    overflow: hidden !important;',
      '    text-overflow: ellipsis !important;',
      '    display: block !important;',
      '    -webkit-line-clamp: 1 !important;',
      '    line-clamp: 1 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__time {',
      '    flex-shrink: 0 !important;',
      '    padding-left: 0 !important;',
      '    font-size: .95em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__timeline {',
      '    margin: .5em 0 !important;',
      '  }',
      // Description block, TWO stacked rows:
      //   row 1 = movie description/tagline (__info), one line, ellipsis
      //   row 2 = file info (__quality: 1080p - size - codec), one line, ellipsis
      // Stacking them stops the long file string from squeezing the
      // description into a tiny column.
      '  body.lampac-online-compact .online-prestige--full .online-prestige__footer {',
      '    display: block !important;',
      '    min-width: 0 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__info {',
      '    display: block !important;',
      '    min-width: 0 !important;',
      '    max-width: 100% !important;',
      '    white-space: nowrap !important;',
      '    overflow: hidden !important;',
      '    text-overflow: ellipsis !important;',
      '    font-size: 1em !important;',
      '    line-height: 1.35 !important;',
      '    max-height: none !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__info > * {',
      '    display: inline !important;',
      '    white-space: nowrap !important;',
      '    overflow: visible !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__quality {',
      '    display: block !important;',
      '    min-width: 0 !important;',
      '    max-width: 100% !important;',
      '    padding-left: 0 !important;',
      '    margin-top: .25em !important;',
      '    text-align: left !important;',
      '    white-space: nowrap !important;',
      '    overflow: hidden !important;',
      '    text-overflow: ellipsis !important;',
      '    font-size: .95em !important;',
      '    opacity: .85 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige-split {',
      '    margin: 0 .38em !important;',
      '    font-size: .8em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full.focus::after {',
      '    top: -.4em !important;',
      '    left: -.4em !important;',
      '    right: -.4em !important;',
      '    bottom: -.4em !important;',
      '    border-width: .22em !important;',
      '  }',
      '}',
      // Phone-sized portrait: smaller fixed card, poster follows card height.
      '@media screen and (orientation: portrait) and (max-width: 720px) {',
      '  body.lampac-online-compact .online-prestige--full {',
      '    height: 8.8em !important;',
      '    min-height: 8.8em !important;',
      '    max-height: 8.8em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__img {',
      '    width: 8.8em !important;',
      '    min-width: 8.8em !important;',
      '    max-width: 8.8em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__title {',
      '    font-size: 1.3em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__info {',
      '    font-size: .95em !important;',
      '    line-height: 1.35 !important;',
      '    max-height: 2.7em !important;',
      '  }',
      '}',
      '@media screen and (orientation: portrait) and (max-width: 390px) {',
      '  body.lampac-online-compact .online-prestige--full {',
      '    height: 8em !important;',
      '    min-height: 8em !important;',
      '    max-height: 8em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__img {',
      '    width: 8em !important;',
      '    min-width: 8em !important;',
      '    max-width: 8em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige--full .online-prestige__body {',
      '    padding: .7em .85em !important;',
      '  }',
      '}'
    ].join('\n');
    document.head.appendChild(style);
  }

  function installSetting() {
    if (!window.Lampa || !Lampa.SettingsApi || window.lampac_online_compact_setting) return;
    window.lampac_online_compact_setting = true;

    Lampa.SettingsApi.addParam({
      component: 'interface',
      param: {
        name: settingName,
        type: 'trigger',
        values: '',
        default: true
      },
      field: {
        name: 'Danh sách Online gọn',
        description: 'Chế độ dọc: card cao cố định, mô tả đúng 2 dòng, poster vuông dính viền như chế độ ngang.'
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

    if (Lampa.Storage.listener) {
      Lampa.Storage.listener.follow('change', function (event) {
        if (event.name === settingName) apply();
      });
    }
  }

  install();
})();
