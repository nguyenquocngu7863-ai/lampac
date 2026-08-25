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
      '    min-height: 0 !important;',
      '    border-radius: .25em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige + .online-prestige {',
      '    margin-top: .65em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__img {',
      '    width: 5.25em !important;',
      '    min-height: 4.8em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__body {',
      '    min-width: 0 !important;',
      '    padding: .55em .7em !important;',
      '    line-height: 1.15 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__head,',
      '  body.lampac-online-compact .online-prestige__footer {',
      '    min-width: 0 !important;',
      '    gap: .4em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__title {',
      '    min-width: 0 !important;',
      '    font-size: 1.12em !important;',
      '    line-height: 1.15 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__time {',
      '    flex-shrink: 0 !important;',
      '    padding-left: .35em !important;',
      '    font-size: .76em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__timeline {',
      '    margin: .28em 0 !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__timeline:empty {',
      '    display: none !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__info {',
      '    min-width: 0 !important;',
      '    overflow: hidden !important;',
      '    white-space: nowrap !important;',
      '    font-size: .78em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__quality {',
      '    flex-shrink: 0 !important;',
      '    padding-left: .35em !important;',
      '    font-size: .76em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige .online-prestige-split {',
      '    margin: 0 .35em !important;',
      '    font-size: .65em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__viewed {',
      '    top: .35em !important;',
      '    left: .35em !important;',
      '    font-size: .62em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige__episode-number {',
      '    font-size: 1.35em !important;',
      '  }',
      '  body.lampac-online-compact .online-prestige.focus::after {',
      '    top: -.3em !important;',
      '    left: -.3em !important;',
      '    right: -.3em !important;',
      '    bottom: -.3em !important;',
      '    border-width: .18em !important;',
      '  }',
      '}',
      '@media screen and (max-width: 390px) {',
      '  body.lampac-online-compact .online-prestige__img { width: 4.6em !important; }',
      '  body.lampac-online-compact .online-prestige__body { padding: .45em .6em !important; }',
      '  body.lampac-online-compact .online-prestige__title { font-size: 1.02em !important; }',
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
        description: 'Thu nhỏ poster, khoảng cách và metadata của nguồn Online trên màn hình điện thoại.'
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
