/* Lampac Admin Panel — embed web-based /adminpanel inside Lampa Activity.
 * Iframe wrapper registered with the same Lampa APIs as the old native
 * plugin (Lampa.Component + Lampa.SettingsApi, installed after Lampa is ready).
 */
(function () {
  'use strict';

  if (window.lampac_adminpanel_plugin) return;
  window.lampac_adminpanel_plugin = true;

  var COMPONENT = 'lampac_admin';

  function serverOrigin() {
    var scripts = document.getElementsByTagName('script');
    for (var i = scripts.length - 1; i >= 0; i--) {
      var src = scripts[i].src || '';
      var match = /^(https?:\/\/[^/]+)\/adminpanel\.js(?:\?|#|$)/i.exec(src);
      if (match) return match[1];
    }
    return window.location.protocol + '//' + window.location.host;
  }

  var ORIGIN = serverOrigin();

  function injectCss() {
    if (document.getElementById('lampac-adminpanel-iframe-css')) return;
    var css = document.createElement('style');
    css.id = 'lampac-adminpanel-iframe-css';
    css.textContent =
      '.lampac-adminpanel-iframe-wrap{display:flex;flex-direction:column;height:100%;width:100%;background:#0c0e12;padding-bottom:calc(76px + env(safe-area-inset-bottom, 0px));box-sizing:border-box}' +
      '.lampac-adminpanel-iframe{flex:1;width:100%;min-height:60vh;border:none;background:#0c0e12}' +
      '.lampac-adminpanel-toolbar{display:flex;align-items:center;gap:8px;padding:8px 12px;background:#13161d;border-bottom:1px solid #2a3040;flex-shrink:0}' +
      '.lampac-adminpanel-toolbar .btn{font-size:12px;padding:6px 12px;border-radius:6px;border:1px solid #3ec9d1;background:rgba(62,201,209,.12);color:#3ec9d1;cursor:pointer}' +
      '.lampac-adminpanel-toolbar .btn:hover{background:rgba(62,201,209,.22)}' +
      '.lampac-adminpanel-toolbar .spacer{flex:1}';
    document.head.appendChild(css);
  }

  function component() {
    injectCss();

    var wrap = $('<div class="lampac-adminpanel-iframe-wrap"></div>');

    var toolbar = $('<div class="lampac-adminpanel-toolbar"></div>');
    var backBtn = $('<div class="btn selector">Quay lai</div>');
    backBtn.on('hover:enter click', function () {
      Lampa.Activity.backward();
    });
    toolbar.append(backBtn);
    toolbar.append('<div class="spacer"></div>');
    wrap.append(toolbar);

    var iframe = $('<iframe class="lampac-adminpanel-iframe" allow="clipboard-read; clipboard-write"></iframe>');
    iframe.attr('src', ORIGIN + '/adminpanel');
    wrap.append(iframe);

    this.create = function () {
      if (this.activity) this.activity.loader(false);
      return this.render();
    };
    this.start = function () {
      Lampa.Controller.add('content', {
        toggle: function () {
          Lampa.Controller.collectionSet(wrap[0]);
          Lampa.Controller.collectionFocus(backBtn[0], wrap[0]);
        },
        back: function () { Lampa.Activity.backward(); }
      });
      Lampa.Controller.toggle('content');
    };
    this.render = function () { return wrap; };
    this.destroy = function () { wrap.remove(); };
    this.pause = function () { };
    this.stop = function () { };
    this.back = function () { Lampa.Activity.backward(); };
  }

  function openAdmin() {
    Lampa.Activity.push({
      url: '',
      title: 'Admin Panel',
      component: COMPONENT,
      page: 1
    });
  }

  function install() {
    if (!Lampa.SettingsApi || !Lampa.Component) return;
    if (!Lampa.Component.get || !Lampa.Component.get(COMPONENT))
      Lampa.Component.add(COMPONENT, component);

    Lampa.SettingsApi.addComponent({
      component: 'lampac_adminpanel',
      name: 'Admin Panel',
      icon: '<svg viewBox="0 0 24 24"><path fill="currentColor" d="M12 1a5 5 0 0 0-5 5v2H5a3 3 0 0 0-3 3v9a3 3 0 0 0 3 3h14a3 3 0 0 0 3-3v-9a3 3 0 0 0-3-3h-2V6a5 5 0 0 0-5-5m-3 7V6a3 3 0 1 1 6 0v2H9m3 4a2 2 0 1 1 0 4 2 2 0 0 1 0-4Z"/></svg>'
    });

    Lampa.SettingsApi.addParam({
      component: 'lampac_adminpanel',
      param: { name: 'lampac_adminpanel_open', type: 'button' },
      field: {
        name: 'Mo Admin Panel',
        description: 'Web /adminpanel ngay trong Lampa.'
      },
      onChange: openAdmin
    });
  }

  function wait() {
    if (typeof Lampa === 'undefined' || !Lampa.SettingsApi || !Lampa.Component || !Lampa.Activity) {
      setTimeout(wait, 300);
      return;
    }
    install();
  }

  wait();
})();
