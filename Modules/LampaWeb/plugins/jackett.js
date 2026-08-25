(function () {
  'use strict';

  var config = {
    url: __JACKETT_URL__,
    apiKey: __JACKETT_API_KEY__
  };

  function apply() {
    if (!window.Lampa || !Lampa.Storage || !config.url || !config.apiKey) return;

    Lampa.Storage.set('parser_use', 'true');
    Lampa.Storage.set('parser_torrent_type', 'jackett');
    Lampa.Storage.set('jackett_url', config.url.replace(/\/+$/, ''));
    Lampa.Storage.set('jackett_key', config.apiKey);
  }

  if (window.appready) {
    apply();
  } else if (window.Lampa && Lampa.Listener) {
    Lampa.Listener.follow('app', function (event) {
      if (event.type === 'ready') apply();
    });
  }
})();
