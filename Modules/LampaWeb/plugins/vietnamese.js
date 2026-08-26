(function () {
  'use strict';

  if (window.lampac_vietnamese_plugin) return;
  window.lampac_vietnamese_plugin = true;

  var settingName = 'lampac_vietnamese_overlay';
  var scriptSource = document.currentScript && document.currentScript.src || '';
  var scheduled = false;
  var pendingRoots = [];

  var exact = {
    'Online': 'Trực tuyến',
    'Source': 'Nguồn',
    'Filter': 'Bộ lọc',
    'Video': 'Video',
    'No browsing history': 'Chưa có lịch sử xem',
    'Watch online': 'Xem trực tuyến',
    'Change balancer': 'Đổi nguồn',
    'Search': 'Tìm kiếm',
    'Home': 'Trang chủ',
    'Back': 'Quay lại',
    'Settings': 'Cài đặt',
    'Interface': 'Giao diện',
    'Player': 'Trình phát',
    'Parser': 'Trình phân tích torrent',
    'Plugins': 'Tiện ích',
    'Extensions': 'Tiện ích mở rộng',
    'More': 'Khác',
    'Update': 'Cập nhật',
    'Information': 'Thông tin',
    'Error': 'Lỗi',
    "It's empty here": 'Chưa có nội dung',
    'The list is currently empty.': 'Danh sách hiện đang trống.',
    'Failed to get HASH, try reloading TorrServer': 'Không lấy được HASH, hãy thử khởi động lại TorrServer',
    'Released': 'Đã phát hành',
    'Dubbing': 'Lồng tiếng',
    'Default': 'Mặc định',
    'Unknown': 'Không xác định',
    'Sort': 'Sắp xếp',
    'Categories': 'Danh mục',
    'Category': 'Danh mục',
    'All': 'Tất cả',
    'Sites': 'Trang nguồn',
    'Preview': 'Xem trước',
    'History': 'Lịch sử',
    'Similar': 'Tương tự',
    'Model': 'Người mẫu',
    'Translation': 'Bản dịch',
    'Subtitles': 'Phụ đề',
    'Original': 'Nguyên bản',
    'New': 'Mới',
    'Most viewed': 'Xem nhiều nhất',
    'Top rated': 'Đánh giá cao',
    'Longest': 'Dài nhất',
    'Shortest': 'Ngắn nhất',

    'Клубничка': 'Nội dung 18+',
    'Доступ ограничен': 'Quyền truy cập bị hạn chế',
    'В закладки': 'Thêm vào dấu trang',
    'Удалить из закладок': 'Xóa khỏi dấu trang',
    'Удалить из истории': 'Xóa khỏi lịch sử',
    'Похожие': 'Nội dung tương tự',
    'Плеер Lampa': 'Trình phát Lampa',
    'Меню': 'Trình đơn',
    'Успешно': 'Thành công',
    'Любой': 'Tất cả',
    'Найти': 'Tìm',
    'Поиск': 'Tìm kiếm',
    'Фильтр': 'Bộ lọc',
    'Предпросмотр': 'Xem trước',
    'Показывать предпросмотр при наведение на карточку': 'Hiện bản xem trước khi chọn thẻ nội dung',
    'История': 'Lịch sử',
    'Сохранять историю просмотров': 'Lưu lịch sử xem',
    'Все': 'Tất cả',
    'Сайты': 'Trang nguồn',
    'Добавьте идентификатор устройства в init.conf': 'Hãy thêm mã thiết bị vào init.conf',
    'Удерживайте ОК на видео для добавления в закладки.': 'Giữ OK trên video để thêm vào dấu trang.',

    'Смотреть онлайн': 'Xem trực tuyến',
    'Видео': 'Video',
    'Нет истории просмотра': 'Chưa có lịch sử xem',
    'Не удалось извлечь ссылку': 'Không lấy được liên kết',
    'Источник': 'Nguồn',
    'Онлайн': 'Trực tuyến',
    'Подписаться на перевод': 'Theo dõi bản dịch',
    'Вы успешно подписались': 'Đã theo dõi thành công',
    'Возникла ошибка': 'Đã xảy ra lỗi',
    'Очистить все метки': 'Xóa tất cả đánh dấu',
    'Очистить все тайм-коды': 'Xóa tất cả mốc thời gian',
    'Изменить балансер': 'Đổi nguồn',
    'По умолчанию': 'Mặc định',
    'Неизвестно': 'Không xác định',
    'Дубляж': 'Lồng tiếng',
    'Многоголосый': 'Đa giọng',
    'Двухголосый': 'Hai giọng',
    'Любительский': 'Nghiệp dư',
    'Субтитры': 'Phụ đề',
    'Оригинал': 'Nguyên bản',
    'Новинки': 'Mới nhất',
    'Топ просмотра': 'Xem nhiều nhất',
    'Топ рейтинга': 'Đánh giá cao',
    'Длинные ролики': 'Video dài',
    'Короткие ролики': 'Video ngắn',
    'Категория': 'Danh mục',

    'Сейчас смотрят': 'Đang được xem',
    'Новые серии': 'Tập mới',
    'Онгоинги': 'Đang phát hành',
    'Популярное': 'Phổ biến',
    'Последнее добавление': 'Mới thêm gần đây',
    'Новинки этого года': 'Phim mới năm nay',
    'С высоким рейтингом': 'Đánh giá cao',
    'Комедийные дорамы': 'Phim hài',
    'Криминальные': 'Tội phạm',
    'Детективы': 'Trinh thám',
    'Боевики': 'Hành động',
    'Фэнтези': 'Kỳ ảo',
    'Семейные': 'Gia đình',
    'Мини-сериалы': 'Phim bộ ngắn',
    'Дорамы': 'Phim châu Á'
  };

  var prefixes = [
    [/^Поиск\s*-\s*/i, 'Tìm kiếm - '],
    [/^Модель\s*-\s*/i, 'Người mẫu - '],
    [/^Похожие\s*-\s*/i, 'Tương tự - '],
    [/^Сортировка:\s*/i, 'Sắp xếp: '],
    [/^Категории?:\s*/i, 'Danh mục: '],
    [/^Поиск на \(([^)]+)\) не дал результатов$/i, 'Nguồn ($1) không trả về kết quả'],
    [/^Источник будет переключен автоматически через\s*/i, 'Nguồn sẽ tự động chuyển sau '],
    [/^Search\s*-\s*/i, 'Tìm kiếm - '],
    [/^Sorting:\s*/i, 'Sắp xếp: '],
    [/^Categor(?:y|ies):\s*/i, 'Danh mục: '],
    [/^Model\s*-\s*/i, 'Người mẫu - '],
    [/^Similar\s*-\s*/i, 'Tương tự - ']
  ];

  function enabled() {
    if (!window.Lampa || !Lampa.Storage) return true;
    var value = Lampa.Storage.get(settingName, 'true');
    return value !== false && value !== 'false';
  }

  function translateValue(value) {
    var trimmed = String(value || '').trim();
    if (!trimmed) return null;
    if (exact[trimmed]) return exact[trimmed];

    for (var i = 0; i < prefixes.length; i++) {
      if (prefixes[i][0].test(trimmed)) return trimmed.replace(prefixes[i][0], prefixes[i][1]);
    }
    return null;
  }

  function translateTextNode(node) {
    if (!node || node.nodeType !== 3 || !node.parentNode) return;
    var tag = (node.parentNode.tagName || '').toLowerCase();
    if (tag === 'script' || tag === 'style' || tag === 'textarea' || tag === 'code') return;

    var translated = translateValue(node.nodeValue);
    if (!translated) return;

    var leading = (node.nodeValue.match(/^\s*/) || [''])[0];
    var trailing = (node.nodeValue.match(/\s*$/) || [''])[0];
    node.nodeValue = leading + translated + trailing;
  }

  function translateAttributes(element) {
    if (!element || element.nodeType !== 1) return;
    ['title', 'placeholder', 'aria-label'].forEach(function (name) {
      if (!element.hasAttribute(name)) return;
      var translated = translateValue(element.getAttribute(name));
      if (translated) element.setAttribute(name, translated);
    });
  }

  function translateTree(root) {
    if (!enabled() || !root) return;
    if (root.nodeType === 3) {
      translateTextNode(root);
      return;
    }
    if (root.nodeType !== 1 && root.nodeType !== 9 && root.nodeType !== 11) return;

    if (root.nodeType === 1) translateAttributes(root);
    var walker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, null, false);
    var node;
    while ((node = walker.nextNode())) {
      if (node.nodeType === 3) translateTextNode(node);
      else translateAttributes(node);
    }
  }

  function flush() {
    scheduled = false;
    var roots = pendingRoots.splice(0, pendingRoots.length);
    for (var i = 0; i < roots.length; i++) translateTree(roots[i]);
  }

  function schedule(root) {
    pendingRoots.push(root);
    if (scheduled) return;
    scheduled = true;
    if (window.requestAnimationFrame) window.requestAnimationFrame(flush);
    else window.setTimeout(flush, 0);
  }

  function languageUrl() {
    var match = /^(https?:\/\/[^/]+)/i.exec(scriptSource);
    var origin = match ? match[1] : window.location.protocol + '//' + window.location.host;
    return origin + '/lampa-main/lang/vi.js?v=' + Date.now();
  }

  function installCoreLanguage() {
    if (!window.Lampa || !Lampa.Lang || !Lampa.Lang.addCodes || !Lampa.Lang.AddTranslation) return;

    // meta.js is bundled into app.min.js, so patching the static meta file is
    // not enough. Register the language through Lampa's public runtime API.
    Lampa.Lang.addCodes({ vi: 'Tiếng Việt' });

    import(languageUrl()).then(function (module) {
      if (module && module.default) {
        Lampa.Lang.AddTranslation('vi', module.default);
        schedule(document.body);
      }
    }).catch(function (error) {
      if (window.console) console.error('Vietnamese', 'Không tải được vi.js', error);
    });
  }

  function addLangCatalog() {
    if (!window.Lampa || !Lampa.Lang || !Lampa.Lang.add) return;

    Lampa.Lang.add({
      lampac_watch: { vi: 'Xem trực tuyến' },
      lampac_video: { vi: 'Video' },
      lampac_no_watch_history: { vi: 'Chưa có lịch sử xem' },
      lampac_nolink: { vi: 'Không lấy được liên kết' },
      lampac_balanser: { vi: 'Nguồn' },
      helper_online_file: { vi: 'Giữ phím OK để mở menu ngữ cảnh' },
      title_online: { vi: 'Trực tuyến' },
      lampac_voice_subscribe: { vi: 'Theo dõi bản dịch' },
      lampac_voice_success: { vi: 'Đã theo dõi thành công' },
      lampac_voice_error: { vi: 'Đã xảy ra lỗi' },
      lampac_clear_all_marks: { vi: 'Xóa tất cả đánh dấu' },
      lampac_clear_all_timecodes: { vi: 'Xóa tất cả mốc thời gian' },
      lampac_change_balanser: { vi: 'Đổi nguồn' },
      lampac_balanser_dont_work: { vi: 'Nguồn ({balanser}) không trả về kết quả' },
      lampac_balanser_timeout: { vi: 'Nguồn sẽ tự đổi sau <span class="timeout">10</span> giây.' },
      lampac_does_not_answer_text: { vi: 'Nguồn ({balanser}) không trả về kết quả' },
      lampac_sisiname: { vi: 'Nội dung 18+' },
      pirate_store: { vi: 'Kho tiện ích' }
    });
  }

  function useVietnameseTmdb() {
    if (!window.Lampa || !Lampa.Storage) return false;
    return Lampa.Storage.get('language', 'ru') === 'vi' || Lampa.Storage.get('tmdb_lang', 'ru') === 'vi';
  }

  function originalLogoUrl(url) {
    if (!useVietnameseTmdb() || typeof url !== 'string' || url.indexOf('include_image_language=') < 0) return url;

    // Keep metadata requests in Vietnamese, but request only English and
    // language-neutral images so Lampa never selects a missing/broken vi logo.
    return url.replace(/([?&]include_image_language=)[^&]*/i, '$1en%2Cnull');
  }

  function sanitizeTmdbLogos(data) {
    if (!useVietnameseTmdb() || !data || typeof data !== 'object' || !data.images || !Array.isArray(data.images.logos)) return data;

    // Cached CUB/TMDB responses may still contain vi logos even after the URL
    // policy changes. Remove them at the response boundary as well.
    data.images.logos = data.images.logos.filter(function (logo) {
      var code = String(logo && logo.iso_639_1 || '').toLowerCase().split(/[-_]/)[0];
      return !code || code === 'en';
    });
    return data;
  }

  function installTmdbLogoPolicy() {
    if (window.lampac_vietnamese_tmdb_policy) return;
    window.lampac_vietnamese_tmdb_policy = true;

    if (window.$ && $.ajax) {
      var originalAjax = $.ajax;
      $.ajax = function (options) {
        if (options && typeof options === 'object') {
          if (options.url) options.url = originalLogoUrl(String(options.url));

          var originalSuccess = options.success;
          if (typeof originalSuccess === 'function') {
            options.success = function (data) {
              arguments[0] = sanitizeTmdbLogos(data);
              return originalSuccess.apply(this, arguments);
            };
          }
        }
        else if (typeof options === 'string') arguments[0] = originalLogoUrl(options);

        return originalAjax.apply(this, arguments);
      };
    }

    if (window.XMLHttpRequest && XMLHttpRequest.prototype) {
      var originalOpen = XMLHttpRequest.prototype.open;
      XMLHttpRequest.prototype.open = function (method, url) {
        arguments[1] = originalLogoUrl(String(url || ''));
        return originalOpen.apply(this, arguments);
      };
    }
  }

  function syncTmdbLanguage() {
    if (!window.Lampa || !Lampa.Storage) return;
    if (Lampa.Storage.get('language', 'ru') === 'vi' && Lampa.Storage.get('tmdb_lang', 'ru') !== 'vi')
      Lampa.Storage.set('tmdb_lang', 'vi');
  }

  function installSetting() {
    if (!window.Lampa || !Lampa.SettingsApi || window.lampac_vietnamese_setting) return;
    window.lampac_vietnamese_setting = true;
    Lampa.SettingsApi.addParam({
      component: 'interface',
      param: { name: settingName, type: 'trigger', values: '', default: true },
      field: {
        name: 'Lớp Việt hóa addon',
        description: 'Dịch các chuỗi Anh/Nga hardcode của Online, SISI và addon sau mỗi lần render.'
      },
      onChange: function () { schedule(document.body); }
    });
  }

  function install() {
    if (!window.Lampa || !document.body) {
      setTimeout(install, 250);
      return;
    }

    installCoreLanguage();
    addLangCatalog();
    installSetting();
    installTmdbLogoPolicy();
    syncTmdbLanguage();
    schedule(document.body);

    if (Lampa.Storage.listener) {
      Lampa.Storage.listener.follow('change', function (event) {
        if (event.name === 'language') syncTmdbLanguage();
      });
    }

    var observer = new MutationObserver(function (mutations) {
      if (!enabled()) return;
      for (var i = 0; i < mutations.length; i++) {
        for (var j = 0; j < mutations[i].addedNodes.length; j++) schedule(mutations[i].addedNodes[j]);
      }
    });
    observer.observe(document.body, { childList: true, subtree: true });
  }

  install();
})();
