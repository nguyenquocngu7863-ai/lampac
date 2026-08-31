(function () {
  'use strict';

  // Addon overlay only. Core Lampa UI comes from original files:
  // wwwroot/lampa-main/lang/vi.js, lang/meta.js, and app.min.js.
  // Do not call Lang.addCodes here — it wipes langs.vi after native loadLang.

  if (window.lampac_vietnamese_plugin) return;
  window.lampac_vietnamese_plugin = true;

  var settingName = 'lampac_vietnamese_overlay';
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

    'Главная': 'Trang chủ',
    'Фильмы': 'Phim lẻ',
    'Мультфильмы': 'Hoạt hình',
    'Сериалы': 'Phim bộ',
    'Персоны': 'Nhân vật',
    'Каталог': 'Danh mục',
    'Избранное': 'Dấu trang',
    'Подписки': 'Đang theo dõi',
    'Расписание': 'Lịch phát',
    'Торренты': 'Torrent',
    'Настройки': 'Cài đặt',
    'Информация': 'Thông tin',
    'Консоль': 'Bảng điều khiển',
    'Редактировать': 'Chỉnh sửa',
    'Назад': 'Quay lại',
    'Популярные': 'Phổ biến',
    'Управление': 'Điều khiển',
    'Телевидение': 'Truyền hình',
    'Онлайн Мод': 'Mod trực tuyến',
    'Фильмы и сериалы в онлайн': 'Phim lẻ và phim bộ trực tuyến',
    'Фильмы и сериалы в онлайн.': 'Phim lẻ và phim bộ trực tuyến.',
    'Подборки': 'Tuyển tập',
    'Подборки от онлайн кинотеатров, мультсериалы и мультфильмы.': 'Tuyển tập từ rạp trực tuyến, phim hoạt hình.',

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
    'Качество': 'Chất lượng',
    'Сортировка': 'Sắp xếp',
    'Ориентация': 'Xu hướng',
    'Гетеро': 'Dị tính',
    'Любое': 'Bất kỳ',
    'Мой рейтинг': 'Đánh giá của tôi',
    'Рейтинг': 'Đánh giá',
    'Длительность': 'Thời lượng',
    'Дата добавления': 'Ngày thêm',

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

  function tmdbLangCode() {
    if (!window.Lampa || !Lampa.Storage) return '';
    var language = Lampa.Storage.get('language', 'ru');
    var tmdb = Lampa.Storage.get('tmdb_lang', language);
    return String(tmdb || language || 'en').toLowerCase();
  }

  function isVietnameseTmdbLang(code) {
    return code === 'vi' || code.indexOf('vi-') === 0 || code.indexOf('vi_') === 0;
  }

  function useVietnameseTmdb() {
    return isVietnameseTmdbLang(tmdbLangCode());
  }

  function originalLogoUrl(url) {
    if (typeof url !== 'string') return url;
    if (!/tmdb|themoviedb|\/cub\/|apitmdb|tmapi/i.test(url)) return url;

    // Force English TMDB titles, posters AND logos. CUB still returns vi logos
    // when include_image_language lists vi, even if language=en.
    url = url
      .replace(/([?&]language=)vi(?:[-_][A-Za-z]+)?(?=&|$)/ig, '$1en')
      .replace(/([?&]include_image_language=)[^&]*/ig, '$1en%2Cnull');
    var imagesEndpoint = /\/(?:movie|tv)\/\d+\/images(?:\?|$)/i.test(url) ||
      /append_to_response=[^&]*images/i.test(url);
    if (imagesEndpoint && !/include_image_language=/i.test(url))
      url += (url.indexOf('?') >= 0 ? '&' : '?') + 'include_image_language=en%2Cnull';
    return url;
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

  function sanitizeTmdbLogos(data) {
    if (!data || typeof data !== 'object') return data;
    if (data.movie && data.movie !== data) sanitizeTmdbLogos(data.movie);
    if (data.images && Array.isArray(data.images.logos))
      data.images.logos = filterEnglishLogos(data.images.logos);
    // Logo plugins use GET /images and take logos[0] from this shape.
    if (Array.isArray(data.logos))
      data.logos = filterEnglishLogos(data.logos);
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

    if (window.Lampa && Lampa.Listener && !window.lampac_vietnamese_tmdb_secuses) {
      window.lampac_vietnamese_tmdb_secuses = true;
      Lampa.Listener.follow('request_secuses', function (e) {
        if (e && e.data) sanitizeTmdbLogos(e.data);
      });
      Lampa.Listener.follow('full', function (e) {
        if (e && e.data) sanitizeTmdbLogos(e.data);
      });
    }
  }

  function syncTmdbLanguage() {
    if (!window.Lampa || !Lampa.Storage) return;
    // Keep the Lampa UI in Vietnamese, but default TMDB titles/search to English
    // so movie names match sources. Lampa itself copies language → tmdb_lang.
    if (Lampa.Storage.get('language', 'ru') !== 'vi') return;
    if (!isVietnameseTmdbLang(tmdbLangCode())) return;
    Lampa.Storage.set('tmdb_lang', 'en');
    try {
      if (window.appready && !sessionStorage.getItem('lampac_tmdb_en_reloaded')) {
        sessionStorage.setItem('lampac_tmdb_en_reloaded', '1');
        window.location.reload();
      }
    } catch (e) { }
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

    addLangCatalog();
    installSetting();
    installTmdbLogoPolicy();
    syncTmdbLanguage();
    schedule(document.body);

    if (Lampa.Storage.listener) {
      Lampa.Storage.listener.follow('change', function (event) {
        if (event.name === 'language' || event.name === 'tmdb_lang') syncTmdbLanguage();
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
