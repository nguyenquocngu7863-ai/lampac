/* Lampac Admin Panel shortcut for Lampa.
 * Opens the protected AdminPanel inside the current Lampa WebView. The root
 * password is requested by /adminpanel/auth and is never handled by this plugin.
 */
(function () {
  'use strict';

  // This file can be delivered by several loaders at once (on.js bundle,
  // lampainit.js plugin sync, the persisted Lampa plugin registry). Every
  // loader creates a separate script evaluation with a fresh closure, so a
  // closure-local guard cannot deduplicate the settings component — use a
  // window flag like gst.js does, otherwise "Mở trang quản trị Lampac"
  // appears once per evaluation.
  if (window.lampac_adminpanel_plugin) return;
  window.lampac_adminpanel_plugin = true;

  function serverOrigin() {
    var scripts = document.getElementsByTagName('script');
    for (var i = scripts.length - 1; i >= 0; i--) {
      var src = scripts[i].src || '';
      var match = /^(https?:\/\/[^/]+)\/adminpanel\.js(?:[?#].*)?$/i.exec(src);
      if (match) return match[1];
    }
    return window.location.protocol + '//' + window.location.host;
  }

  function openAdminPanel() {
    // Deliberately stay in this WebView: the auth page owns the root-password
    // form and same-origin cookie. Back returns the user to Lampa.
    window.location.assign(serverOrigin() + '/adminpanel/auth');
  }

  function install() {
    if (!Lampa.SettingsApi) return;

    Lampa.SettingsApi.addComponent({
      component: 'lampac_adminpanel',
      name: 'Admin Panel',
      icon: '<svg viewBox="0 0 24 24"><path fill="currentColor" d="M12 1a5 5 0 0 0-5 5v2H5a3 3 0 0 0-3 3v9a3 3 0 0 0 3 3h14a3 3 0 0 0 3-3v-9a3 3 0 0 0-3-3h-2V6a5 5 0 0 0-5-5m-3 7V6a3 3 0 1 1 6 0v2H9m3 4a2 2 0 1 1 0 4 2 2 0 0 1 0-4Z"/></svg>'
    });

    Lampa.SettingsApi.addParam({
      component: 'lampac_adminpanel',
      param: { name: 'lampac_adminpanel_open', type: 'button' },
      field: {
        name: 'Mở trang quản trị Lampac',
        description: 'Mở trong WebView hiện tại; cần root password của Lampac.'
      },
      onChange: openAdminPanel
    });
  }

  function wait() {
    if (typeof Lampa === 'undefined' || !Lampa.SettingsApi) {
      setTimeout(wait, 300);
      return;
    }
    install();
  }

  wait();
})();
