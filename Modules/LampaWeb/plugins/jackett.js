(function () {
  'use strict';

  var config = {
    url: __JACKETT_URL__,
    apiKey: __JACKETT_API_KEY__
  };

  function installTorrentResolver() {
    if (config.apiKey !== 'lampac-proxy' || window.lampac_jackett_resolver) return;
    if (!window.XMLHttpRequest || !XMLHttpRequest.prototype) return;

    window.lampac_jackett_resolver = true;

    function torrentToken(body) {
      if (typeof body !== 'string') return null;
      try {
        var payload = JSON.parse(body);
        if (!payload || payload.action !== 'add' || typeof payload.link !== 'string') return null;
        var match = payload.link.match(/\/jackett\/download\?token=([A-Za-z0-9_-]+)/i);
        return match ? { token: match[1], payload: payload } : null;
      } catch (e) {
        return null;
      }
    }

    function resolve(token, done) {
      var resolver = new XMLHttpRequest();
      resolver.open('GET', config.url.replace(/\/+$/, '') + '/resolve?token=' + encodeURIComponent(token), true);
      resolver.timeout = 15000;
      var completed = false;

      function finish(magnet) {
        if (completed) return;
        completed = true;
        done(magnet || '');
      }

      resolver.onload = function () {
        try {
          var response = JSON.parse(resolver.responseText || '{}');
          finish(response.magnet || '');
        } catch (e) {
          finish('');
        }
      };
      resolver.onerror = resolver.ontimeout = function () { finish(''); };
      resolver.send();
    }

    // Lampa's TorrServer client sends requests through jQuery $.ajax. Patch
    // that layer first because some Android WebViews replace XMLHttpRequest
    // with a native bridge and never reach the prototype hook below.
    if (window.$ && $.ajax && !window.lampac_jackett_ajax_resolver) {
      window.lampac_jackett_ajax_resolver = true;
      var originalAjax = $.ajax;

      $.ajax = function (options) {
        var context = this;
        if (!options || typeof options !== 'object' || String(options.type || 'GET').toUpperCase() !== 'POST' ||
            !/\/torrents(?:\?|$)/i.test(String(options.url || ''))) {
          return originalAjax.apply(context, arguments);
        }

        var pending = torrentToken(options.data);
        if (!pending) return originalAjax.apply(context, arguments);

        resolve(pending.token, function (magnet) {
          if (magnet) {
            pending.payload.link = magnet;
            options.data = JSON.stringify(pending.payload);
            if (window.console) console.log('Jackett', 'torrent link resolved to magnet');
          } else if (window.console) {
            console.error('Jackett', 'failed to resolve torrent link to magnet');
          }
          originalAjax.call(context, options);
        });
        return { abort: function () {} };
      };
    }

    // Fallback for Lampa builds that use XMLHttpRequest directly.
    var originalOpen = XMLHttpRequest.prototype.open;
    var originalSend = XMLHttpRequest.prototype.send;

    XMLHttpRequest.prototype.open = function (method, url) {
      this.__lampac_method = String(method || '').toUpperCase();
      this.__lampac_url = String(url || '');
      return originalOpen.apply(this, arguments);
    };

    XMLHttpRequest.prototype.send = function (body) {
      var target = this;
      if (target.__lampac_method !== 'POST' || !/\/torrents(?:\?|$)/i.test(target.__lampac_url))
        return originalSend.call(target, body);

      var pending = torrentToken(body);
      if (!pending) return originalSend.call(target, body);

      resolve(pending.token, function (magnet) {
        if (magnet) pending.payload.link = magnet;
        originalSend.call(target, JSON.stringify(pending.payload));
      });
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
