(function() {
  'use strict';

  window.lampac_version = { major: 0, minor: 0 };

  //localStorage.setItem('cub_mirrors', '["mirror-kurwa.men"]');
  
  window.lampa_settings = window.lampa_settings || {};
  window.lampa_settings.torrents_use = true;    // Показывать кнопку торрентов
  window.lampa_settings.demo = false;           // demo off
  window.lampa_settings.read_only = false;      // Режим только для чтения, без кнопок онлайн и расширений
  window.lampa_settings.socket_use = true;      // cub - Использовать сокеты для синхронизации данных
  window.lampa_settings.socket_url = undefined; // cub - Адрес сокета, по умолчанию лампа берет адреса из манифеста
  window.lampa_settings.socket_methods = true;  // cub - Обрабатывать сообщения сокетов
  window.lampa_settings.account_use = true;   // cub - Использовать аккаунты
  window.lampa_settings.account_sync = true;  // cub - Синхронизировать закладки, таймкоды и прочее
  window.lampa_settings.plugins_store = true; // cub магазин расширений
  window.lampa_settings.feed = true;          // cub лента
  window.lampa_settings.iptv = false;         // Является ли приложение IPTV
  window.lampa_settings.white_use = false;    // Белая и пушистая лампа, для одобрения модерации
  window.lampa_settings.push_state = true;    // адрес в url /?card=1241982&media=movie 
  window.lampa_settings.lang_use = true;      // Подключить другие языки интерфейса, по умолчанию только русский и английский
  window.lampa_settings.plugins_use = true;   // Разрешить установку плагинов и расширений
  window.lampa_settings.dcma = false;         // Добавить список блокировки карточек, пример: [{"id":3566556,"cat":"movie"},...]
  window.lampa_settings.services = true;      // Различные сервисы cub в приложении
  window.lampa_settings.youtube = true;       // Подключить YouTube API
  window.lampa_settings.geo = true;           // Определять гео по IP, иначе будет RU
  window.lampa_settings.mirrors = true;       // Использовать поиск зеркал

  window.lampa_settings.disable_features = window.lampa_settings.disable_features || {};
  window.lampa_settings.disable_features.dmca = true;           // шлет нахер правообладателей - on
  window.lampa_settings.disable_features.ads = true;            // Вспомогательные сервисы на подписку према
  window.lampa_settings.disable_features.reactions = false;     // cub реакции
  window.lampa_settings.disable_features.discuss = false;       // cub комментарии
  window.lampa_settings.disable_features.ai = false;            // cub AI-поиск
  window.lampa_settings.disable_features.install_proxy = false; // cub tmdb proxy
  window.lampa_settings.disable_features.subscribe = false;     // cub подписки
  window.lampa_settings.disable_features.blacklist = false;     // Черный список плагинов
  window.lampa_settings.disable_features.persons = false;       // Подписка на актеров
  window.lampa_settings.disable_features.trailers = false;      // Трейлеры
  window.lampa_settings.disable_features.lgbt = true;           // Разрешить ЛГБТ контент

  window.lampa_settings.developer = window.lampa_settings.developer || {};
  
  
  {lampainit-invc}

  // Sync the native plugin list ({initiale}) on EVERY launch.
  // Previously this ran only once per client (guarded by lampac_initiale),
  // so plugins added on the server later never reached already-connected
  // devices. The url-dedup check below makes repeated syncing harmless.
  function syncPlugins() {
    var plugins = Lampa.Plugins.get() || [];

    // One controlled migration after the duplicate-plugin incident: retain a
    // backup of every client entry, remove only old Lampac-hosted add-ons, then
    // add the current server list below in its declared order. Third-party
    // plugin URLs (for example jsDelivr) are never touched.
    var resetKey = 'lampac_plugin_reset_20260906_v6';
    if (Lampa.Storage.get(resetKey, 'false') !== 'true') {
      Lampa.Storage.set('lampac_plugins_backup_20260823', plugins);
      var lampacPluginPath = /\/(?:dlna|tracks|transcoding|tmdbproxy|cubproxy|online|online-compact|vietnamese|jackett|watchtogether|catalog|dorama|subsense-auto|subsense|subfinder|stremiosub|adminpanel|gst|autotracks|sisi|sisi-layout|sisi-restyle|startpage|sync|timecode|bookmark|ts|backup)\.js(?:[?#]|$)/i;
      var subtitlePluginPath = /\/(?:subsense-auto|subsense|subfinder|stremiosub)\.js(?:[?#]|$)/i;
      plugins.forEach(function (plugin) {
        var url = plugin && plugin.url || '';
        // Also remove old raw/GitHub subtitle URLs. They are legacy copies
        // that otherwise run before the newly selected built-in provider.
        if ((url.indexOf(window.location.origin + '/') === 0 && lampacPluginPath.test(url)) || subtitlePluginPath.test(url)) {
          Lampa.Plugins.remove(plugin);
        }
      });
      Lampa.Storage.set(resetKey, 'true');
      plugins = Lampa.Plugins.get() || [];
    }

    // Collapse duplicate URLs through Lampa's own registry API.
    // Plugins.get() only returns an array copy; splice() does not remove entries
    // from Lampa's live _loaded registry on Android.
    var knownUrls = {};
    var remove = [];
    plugins.forEach(function (plugin) {
      var url = plugin && plugin.url || '';
      if (!url || knownUrls[url]) remove.push(plugin);
      else knownUrls[url] = true;
    });
    remove.forEach(function (plugin) {
      Lampa.Plugins.remove(plugin);
    });

    // Re-read after remove() because it updates Lampa's internal registry.
    plugins = Lampa.Plugins.get() || [];
    knownUrls = {};
    plugins.forEach(function (plugin) {
      if (plugin && plugin.url) knownUrls[plugin.url] = true;
    });

    var plugins_add = {initiale};
    var plugins_push = [];
    plugins_add.forEach(function(plugin) {
      if (plugin && plugin.url && !knownUrls[plugin.url]) {
        Lampa.Plugins.add(plugin);
        knownUrls[plugin.url] = true;
        plugins_push.push(plugin.url);
      }
    });

    if (plugins_push.length) Lampa.Utils.putScript(plugins_push, function() {}, function() {}, function() {}, true);
    return plugins_push.length > 0;
  }

  var timer = setInterval(function() {
    if (typeof Lampa !== 'undefined') {
      clearInterval(timer);

      // Language selection is user-controlled in Settings → Interface.
      // Do not force `vi`: Lampa must load the complete standalone vi.js first.
	  
      // UI stays Vietnamese (`language=vi`). TMDB titles/posters must be
      // English or search/source matching breaks. Set this before appready
      // so the first catalog request is already `language=en`.
      forceEnglishTmdbStorage();
      installEnglishTmdbApi();

      if (lampainit_invc)
        lampainit_invc.appload();

      if ({btn_priority_forced})
        Lampa.Storage.set('full_btn_priority', '{full_btn_priority_hash}');

      var unic_id = Lampa.Storage.get('lampac_unic_id', '');
      if (!unic_id) {
        unic_id = Lampa.Utils.uid(8).toLowerCase();
        Lampa.Storage.set('lampac_unic_id', unic_id);
      }

      Lampa.Utils.putScriptAsync(["{localhost}/privateinit.js?account_email=" + encodeURIComponent(Lampa.Storage.get('account_email', '')) + "&uid=" + encodeURIComponent(Lampa.Storage.get('lampac_unic_id', ''))], function() {});

      if (window.appready) {
        start();
      }
      else {
        Lampa.Listener.follow('app', function(e) {
          if (e.type == 'ready') {
            start();
          }
        });
      }

	  {pirate_store}
    }
  }, 200);


  function rewriteTmdbLangUrl(url) {
    if (typeof url !== 'string') return url;
    if (!/tmdb|themoviedb|apitmdb|tmapi|\/cub\//i.test(url)) return url;
    url = url
      .replace(/([?&]language=)vi(?:[-_][A-Za-z]+)?(?=&|$)/ig, '$1en')
      .replace(/([?&]include_image_language=)[^&]*/ig, '$1en%2Cnull');
    // Logo plugins call /movie|tv/{id}/images?language=vi. TMDB treats
    // `language` as a FILTER, so no-vi movies return logos:[]. Always
    // ask for English title treatments.
    var imagesEndpoint = /\/(?:movie|tv)\/\d+\/images(?:\?|$)/i.test(url) ||
      /append_to_response=[^&]*images/i.test(url);
    if (imagesEndpoint && !/include_image_language=/i.test(url))
      url += (url.indexOf('?') >= 0 ? '&' : '?') + 'include_image_language=en%2Cnull';
    return url;
  }

  function uiIsVietnamese() {
    return window.Lampa && Lampa.Storage && Lampa.Storage.get('language', 'ru') === 'vi';
  }

  function hasVietnameseText(value) {
    return /[àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ]/i.test(String(value || ''));
  }

  function hasCyrillicText(value) {
    return /[\u0400-\u04FF]/.test(String(value || ''));
  }

  function needsEnglishTitle(value) {
    return hasVietnameseText(value) || hasCyrillicText(value);
  }

  function filterEnglishLogos(logos) {
    if (!logos || !logos.length) return logos;
    var kept = logos.filter(function (logo) {
      var code = String(logo && logo.iso_639_1 || '').toLowerCase().split(/[-_]/)[0];
      return !code || code === 'en';
    });
    return kept.length ? kept : logos.filter(function (logo) {
      var code = String(logo && logo.iso_639_1 || '').toLowerCase().split(/[-_]/)[0];
      return code !== 'vi';
    });
  }

  function sanitizeTmdbPayload(data) {
    if (!uiIsVietnamese() || !data || typeof data !== 'object') return data;
    if (data.movie && data.movie !== data) sanitizeTmdbPayload(data.movie);
    // Cub home/browse lists are results[]; card details are a single object.
    if (Array.isArray(data.results)) {
      for (var i = 0; i < data.results.length; i++) sanitizeTmdbPayload(data.results[i]);
    }
    if (needsEnglishTitle(data.title)) {
      var enTitle = data.original_title || data.original_name;
      if (enTitle && !needsEnglishTitle(enTitle)) data.title = enTitle;
    }
    if (needsEnglishTitle(data.name)) {
      var enName = data.original_name || data.original_title;
      if (enName && !needsEnglishTitle(enName)) data.name = enName;
    }
    if (data.images && Array.isArray(data.images.logos))
      data.images.logos = filterEnglishLogos(data.images.logos);
    // Logo plugins consume /images JSON as `{logos:[...]}` and take logos[0].
    if (Array.isArray(data.logos))
      data.logos = filterEnglishLogos(data.logos);
    return data;
  }

  function applyEnglishCardTitle(e) {
    if (!uiIsVietnamese() || !e) return;
    var movie = e.data && (e.data.movie || e.data);
    var root = e.body;
    if (!root || !root.find) {
      if (!window.$) return;
      root = $(document);
    }
    var titleEl = root.find('.full-start-new__title, .full-start__title').first();
    if (!titleEl.length) return;
    // Logo plugin owns this node. Do not insert or resize <img>.
    if (titleEl.find('img').length) return;

    var text = (titleEl.text() || '').trim();
    if (needsEnglishTitle(text)) {
      var en = (movie && (movie.original_title || movie.original_name || movie.title)) || text;
      if (en && en !== text && !needsEnglishTitle(en)) titleEl.text(en);
    }
  }

  function forceEnglishTmdbStorage() {
    if (!window.Lampa || !Lampa.Storage) return;
    if (Lampa.Storage.get('language', 'ru') !== 'vi') return;
    var tmdb = String(Lampa.Storage.get('tmdb_lang', 'vi') || 'vi').toLowerCase();
    if (tmdb === 'vi' || tmdb.indexOf('vi-') === 0 || tmdb.indexOf('vi_') === 0)
      Lampa.Storage.set('tmdb_lang', 'en');
  }

  function installEnglishTmdbApi() {
    if (!window.Lampa) return;
    forceEnglishTmdbStorage();

    if (Lampa.TMDB && typeof Lampa.TMDB.api === 'function' && !window.lampac_english_tmdb_api) {
      window.lampac_english_tmdb_api = true;
      var origApi = Lampa.TMDB.api;
      Lampa.TMDB.api = function () {
        return rewriteTmdbLangUrl(origApi.apply(this, arguments));
      };
    }

    if (Lampa.Listener && !window.lampac_english_tmdb_request) {
      window.lampac_english_tmdb_request = true;
      Lampa.Listener.follow('request_before', function (e) {
        if (e && e.params && e.params.url)
          e.params.url = rewriteTmdbLangUrl(e.params.url);
      });
      Lampa.Listener.follow('request_secuses', function (e) {
        if (e && e.data) sanitizeTmdbPayload(e.data);
      });
      Lampa.Listener.follow('full', function (e) {
        if (!e) return;
        if (e.data) sanitizeTmdbPayload(e.data);
        if (e.type === 'complite') {
          applyEnglishCardTitle(e);
          setTimeout(function () { applyEnglishCardTitle(e); }, 200);
          setTimeout(function () { applyEnglishCardTitle(e); }, 800);
        }
      });
    }
  }

  function start() {
    {deny}

    forceEnglishTmdbStorage();
    installEnglishTmdbApi();
	
    // Always sync plugins first, even on already-initialized clients
    syncPlugins();

    if (Lampa.Storage.get('lampac_initiale', 'false')) {
      if (lampainit_invc) lampainit_invc.first_initiale();
      return;
    }

    Lampa.Storage.set('lampac_initiale', 'true');
    Lampa.Storage.set('source', 'cub');
    Lampa.Storage.set('video_quality_default', '2160');
    Lampa.Storage.set('full_btn_priority', '{full_btn_priority_hash}');
    Lampa.Storage.set('proxy_tmdb', '{country}' == 'RU');
    Lampa.Storage.set('poster_size', 'w300');

    Lampa.Storage.set('parser_use', 'true');
    Lampa.Storage.set('jackett_url', '{jachost}');
    Lampa.Storage.set('jackett_key', '1');
    Lampa.Storage.set('parser_torrent_type', 'jackett');

    if (lampainit_invc)
      lampainit_invc.first_initiale();

  }
})();
