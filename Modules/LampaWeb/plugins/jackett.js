(function () {
  'use strict';

  var config = {
    url: __JACKETT_URL__,
    apiKey: __JACKETT_API_KEY__
  };

  function installTorrentResolver() {
    if (config.apiKey !== 'lampac-proxy' || window.lampac_jackett_xhr_resolver) return;
    if (!window.XMLHttpRequest || !XMLHttpRequest.prototype) return;

    window.lampac_jackett_xhr_resolver = true;
    var originalOpen = XMLHttpRequest.prototype.open;
    var originalSend = XMLHttpRequest.prototype.send;

    XMLHttpRequest.prototype.open = function (method, url) {
      this.__lampac_method = String(method || '').toUpperCase();
      this.__lampac_url = String(url || '');
      return originalOpen.apply(this, arguments);
    };

    XMLHttpRequest.prototype.send = function (body) {
      var target = this;
      var payload;

      if (target.__lampac_method !== 'POST' || !/\/torrents(?:\?|$)/i.test(target.__lampac_url) || typeof body !== 'string') {
        return originalSend.call(target, body);
      }

      try {
        payload = JSON.parse(body);
      } catch (e) {
        return originalSend.call(target, body);
      }

      if (!payload || payload.action !== 'add' || typeof payload.link !== 'string') {
        return originalSend.call(target, body);
      }

      var match = payload.link.match(/\/jackett\/download\?token=([A-Za-z0-9_-]+)/i);
      if (!match) return originalSend.call(target, body);

      var resolver = new XMLHttpRequest();
      resolver.open('GET', config.url.replace(/\/+$/, '') + '/resolve?token=' + encodeURIComponent(match[1]), true);
      resolver.timeout = 15000;

      var completed = false;
      function continueRequest(magnet) {
        if (completed) return;
        completed = true;
        if (magnet) payload.link = magnet;
        originalSend.call(target, JSON.stringify(payload));
      }

      resolver.onload = function () {
        try {
          var response = JSON.parse(resolver.responseText || '{}');
          continueRequest(response.magnet || '');
        } catch (e) {
          continueRequest('');
        }
      };
      resolver.onerror = resolver.ontimeout = function () {
        continueRequest('');
      };
      resolver.send();
    };
  }

  function apply() {
    if (!window.Lampa || !Lampa.Storage || !config.url || !config.apiKey) return;

    Lampa.Storage.set('parser_use', 'true');
    Lampa.Storage.set('parser_torrent_type', 'jackett');
    Lampa.Storage.set('jackett_url', config.url.replace(/\/+$/, ''));
    Lampa.Storage.set('jackett_key', config.apiKey);
    installTorrentResolver();
  }

  if (window.appready) {
    apply();
  } else if (window.Lampa && Lampa.Listener) {
    Lampa.Listener.follow('app', function (event) {
      if (event.type === 'ready') apply();
    });
  }
})();
