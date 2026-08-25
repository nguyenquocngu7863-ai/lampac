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
      '@media screen and (max-width: 720px) {',
      '  body.lampac-online-compact .online-prestige {',
      '    min-height: 7.2em !important;',
      '    border-radius: .35em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige + .online-prestige {',
      '    margin-top: 1em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__img {',
      '    width: 6em !important;',
      '    min-height: 7.2em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__body {',
      '    min-width: 0 !important;',
      '    padding: .8em .85em !important;',
      '    line-height: 1.3 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__head {',
      '    min-width: 0 !important;',
      '    align-items: flex-start !important;',
      '    gap: .6em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__title {',
      '    min-width: 0 !important;',
      '    font-size: 1.3em !important;',
      '    line-height: 1.25 !important;',
      '    -webkit-line-clamp: 2 !important;',
      '    line-clamp: 2 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__time {',
      '    flex-shrink: 0 !important;',
      '    padding-left: 0 !important;',
      '    font-size: .86em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__timeline {',
      '    margin: .45em 0 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__timeline:empty {',
      '    display: none !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__footer {',
      '    display: block !important;',
      '    min-width: 0 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__info {',
      '    display: block !important;',
      '    min-width: 0 !important;',
      '    overflow: hidden !important;',
      '    white-space: normal !important;',
      '    font-size: .9em !important;',
      '    line-height: 1.45 !important;',
      '    max-height: 2.9em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__info > * {',
      '    display: inline !important;',
      '    overflow: visible !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__quality {',
      '    display: inline-block !important;',
      '    padding-left: 0 !important;',
      '    margin-top: .3em !important;',
      '    font-size: .86em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige .online-prestige-split {',
      '    margin: 0 .38em !important;',
      '    font-size: .7em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige.focus::after {',
      '    top: -.4em !important;',
      '    left: -.4em !important;',
      '    right: -.4em !important;',
      '    bottom: -.4em !important;',
      '    border-width: .22em !important;',
      '  }',
      '}',
      '@media screen and (max-width: 390px) {',
      '  body.lampac-online-compact .online-prestige__img { width: 5.4em !important; }',
      '  body.lampac-online-compact .online-prestige__body { padding: .7em !important; }',
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
        name: 'Danh sách Online thoáng',
        description: 'Cho tiêu đề và metadata xuống dòng để danh sách Online dễ đọc hơn trên điện thoại.'
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
