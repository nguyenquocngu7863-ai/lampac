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
      resolver.timeout = 20000;
      var completed = false;

      function finish(magnet) {
        if (completed) return;
        completed = true;
        try { done(magnet || ''); } catch (e) {}
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

    function rewrite(pending, magnet) {
      if (magnet) {
        pending.payload.link = magnet;
        if (window.console) console.log('Jackett', 'torrent link resolved to magnet');
      } else if (window.console) {
        console.error('Jackett', 'failed to resolve torrent link to magnet');
      }
      return JSON.stringify(pending.payload);
    }

    // 1) jQuery layer — Lampa's main HTTP layer. Return a real Deferred so the
    //    caller can chain .done/.fail/.then/.always like a normal jqXHR.
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

        var deferred = $.Deferred ? $.Deferred() : null;
        var abortable = { abort: function () {} };

        resolve(pending.token, function (magnet) {
          options.data = rewrite(pending, magnet);
          try {
            var xhr = originalAjax.call(context, options);
            if (deferred) {
              if (xhr && xhr.done && xhr.fail) {
                xhr.done(deferred.resolve);
                xhr.fail(deferred.reject);
                abortable.abort = function () { if (xhr.abort) xhr.abort(); };
              } else {
                deferred.resolve();
              }
            }
          } catch (e) {
            if (deferred) deferred.reject();
          }
        });

        if (deferred) {
          var promise = deferred.promise();
          promise.abort = function () { abortable.abort(); };
          return promise;
        }
        return abortable;
      };
    }

    // 2) fetch layer — some Lampa builds / Android TV bridges use fetch().
    if (window.fetch && !window.lampac_jackett_fetch_resolver) {
      window.lampac_jackett_fetch_resolver = true;
      var originalFetch = window.fetch.bind(window);

      window.fetch = function (input, init) {
        init = init || {};
        var url = typeof input === 'string' ? input : ((input && input.url) || '');
        if (String(init.method || 'GET').toUpperCase() !== 'POST' ||
            !/\/torrents(?:\?|$)/i.test(String(url)) ||
            typeof init.body !== 'string') {
          return originalFetch(input, init);
        }

        var pendingFetch = torrentToken(init.body);
        if (!pendingFetch) return originalFetch(input, init);

        return new Promise(function (resolvePromise, rejectPromise) {
          resolve(pendingFetch.token, function (magnet) {
            init.body = rewrite(pendingFetch, magnet);
            originalFetch(input, init).then(resolvePromise, rejectPromise);
          });
        });
      };
    }

    // 3) XMLHttpRequest fallback for builds that POST directly.
    if (XMLHttpRequest.prototype.send && !window.lampac_jackett_xhr_resolver) {
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
        if (target.__lampac_method !== 'POST' ||
            !/\/torrents(?:\?|$)/i.test(target.__lampac_url) ||
            typeof body !== 'string') {
          return originalSend.call(target, body);
        }

        var pendingXhr = torrentToken(body);
        if (!pendingXhr) return originalSend.call(target, body);

        resolve(pendingXhr.token, function (magnet) {
          originalSend.call(target, rewrite(pendingXhr, magnet));
        });
      };
    }
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
