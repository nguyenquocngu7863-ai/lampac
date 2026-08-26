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

  // Category labels come from many independent Adult modules and NextHUB YAML
  // files. Translate reusable terms only inside filter/select/menu controls so
  // movie and video titles are never modified.
  var categoryRules = [
    [/double penetration|двойное проникновение|двойной анал/gi, 'thâm nhập kép'],
    [/ass to mouth|atm/gi, 'từ hậu môn vào miệng'],
    [/big black cock|большой ч[её]рный член/gi, 'dương vật da đen lớn'],
    [/old and young|старые с молодыми|взрослые с молодыми/gi, 'lớn tuổi và trẻ'],
    [/female domination|женское доминирование|женская доминация/gi, 'nữ thống trị'],
    [/group sex|групповой секс|групповое порно|групповуха/gi, 'quan hệ nhóm'],
    [/hidden cam|скрыт(?:ая камера|ые камеры)/gi, 'camera ẩn'],
    [/first time|первый раз|девственность/gi, 'lần đầu'],
    [/role play|ролевые игры/gi, 'nhập vai'],
    [/public sex|на публике|публичное порно/gi, 'nơi công cộng'],
    [/rough sex|грубый секс|жесткий секс|ж[её]сткое/gi, 'quan hệ mạnh'],
    [/big ass|big booty|большие попы|большие жопы|большие задницы/gi, 'mông lớn'],
    [/big tits|big boobs|большие сиськи|большая грудь/gi, 'ngực lớn'],
    [/small tits|маленькие сиськи|маленькая грудь/gi, 'ngực nhỏ'],
    [/hairy pussy|волосатая пизда|волосатые киски|небритые лобки/gi, 'không cạo'],
    [/shaved pussy|бритые письки|гладкие киски/gi, 'đã cạo'],
    [/deep ?throat|глубокий минет|глубоко заглатывают|горловой минет/gi, 'khẩu giao sâu'],
    [/pussy licking|cunnilingus|куни(?:лингус)?|лизать киску/gi, 'khẩu giao nữ'],
    [/foot ?fetish|фут[- ]?фетиш|футфетиш/gi, 'tôn sùng bàn chân'],
    [/footjob|дрочат ножками|дрочка ступнями/gi, 'kích thích bằng chân'],
    [/handjob|дрочит парню|дрочка члена/gi, 'kích thích bằng tay'],
    [/blowjob|минет/gi, 'khẩu giao'],
    [/creampie|кремпай|кончают внутрь|кончает внутрь/gi, 'xuất tinh bên trong'],
    [/cumshot|камшот|выстрелы спермы/gi, 'xuất tinh'],
    [/facial|сперма на лице|кончают на лицо/gi, 'xuất tinh lên mặt'],
    [/swallow(?:ing)?|глотает сперму|глотают сперму/gi, 'nuốt tinh'],
    [/squirting?|сквирт(?:инг)?|сквиртят и текут/gi, 'squirt'],
    [/masturbation|мастурбация|дрочка/gi, 'thủ dâm'],
    [/interracial|межрасов(?:ый|ое|ые)(?: секс| порно)?/gi, 'khác chủng tộc'],
    [/threesome|секс втроем|втроем/gi, 'quan hệ ba người'],
    [/gangbang|г[эе]нгб[эе]нг|ебут толпой/gi, 'quan hệ tập thể'],
    [/lesbians?|лесби(?:янки|янка|сбухи)?/gi, 'đồng tính nữ'],
    [/gay porn|гей порно|геи/gi, 'đồng tính nam'],
    [/transsexuals?|shemales?|транссексуалы|трансвеститы|трансы/gi, 'chuyển giới'],
    [/milfs?|милфы?|мамочки|зрелые мамы/gi, 'phụ nữ trưởng thành'],
    [/mature|зрелые|зрелая|в возрасте/gi, 'trưởng thành'],
    [/teen(?:s| porn)?|тинейджеры|подростки 18\+/gi, 'tuổi 18+'],
    [/amateurs?|любительское(?: порно)?/gi, 'nghiệp dư'],
    [/homemade|домашнее(?: порно)?/gi, 'tự quay'],
    [/webcams?|веб[- ]?камеры?|вебкам(?:ера)?/gi, 'webcam'],
    [/casting|кастинги?/gi, 'thử vai'],
    [/cosplay|косплей/gi, 'hóa trang'],
    [/massage|массаж/gi, 'mát-xa'],
    [/office|офис|секс в офисе/gi, 'văn phòng'],
    [/school ?girl|school|школа|в школе|студентки?/gi, 'trường học'],
    [/teacher|учительница?|училки и студенты|с преподами/gi, 'giáo viên'],
    [/nurses?|медсестры?/gi, 'y tá'],
    [/doctor|доктор|врачи/gi, 'bác sĩ'],
    [/secretary|секретарш[аи]/gi, 'thư ký'],
    [/maid|горничн(?:ая|ые)|служанки/gi, 'người hầu'],
    [/outdoor|на природе|на улице/gi, 'ngoài trời'],
    [/beach|на пляже|пляж/gi, 'bãi biển'],
    [/bathroom|в ванной|в душе|в туалете/gi, 'phòng tắm'],
    [/bedroom|в спальне/gi, 'phòng ngủ'],
    [/kitchen|на кухне|секс на кухне/gi, 'nhà bếp'],
    [/car|в машине|в авто/gi, 'trong xe'],
    [/hotel|в отеле/gi, 'khách sạn'],
    [/gym|спортзал|в спортзале|тренажерный зал/gi, 'phòng tập'],
    [/lingerie|нижнее бель[её]|красивое белье/gi, 'đồ lót'],
    [/stockings|чулки|колготки/gi, 'vớ dài'],
    [/uniforms?|униформа|в униформе/gi, 'đồng phục'],
    [/latex|латекс/gi, 'latex'],
    [/tattooed|tattoos?|татуированные|татуировки/gi, 'hình xăm'],
    [/red ?head|рыжие|рыжеволосые/gi, 'tóc đỏ'],
    [/blondes?|блондинки/gi, 'tóc vàng'],
    [/brunettes?|брюнетки|темноволосые/gi, 'tóc nâu'],
    [/asian|азиатки|азиаты|азиатское/gi, 'châu Á'],
    [/japanese|японки|японское|японцы/gi, 'Nhật Bản'],
    [/korean|корейское|кореянки/gi, 'Hàn Quốc'],
    [/russian|русские|русское(?: порно)?/gi, 'Nga'],
    [/vietnamese|вьетнамское/gi, 'Việt Nam'],
    [/latina|latinas|латинки|латино/gi, 'Mỹ Latin'],
    [/ebony|black(?:ed)?|негритянки|чернокожие/gi, 'da đen'],
    [/bbw|chubby|толстушки|полненькие/gi, 'đầy đặn'],
    [/petite|миниатюрные/gi, 'nhỏ nhắn'],
    [/skinny|худые|худенькие/gi, 'mảnh mai'],
    [/pregnant|беременные/gi, 'mang thai'],
    [/redhead|рыжие/gi, 'tóc đỏ'],
    [/bondage|бондаж|связывание/gi, 'trói buộc'],
    [/bdsm|бдсм/gi, 'BDSM'],
    [/fisting|фистинг/gi, 'fisting'],
    [/fetish|фетиш/gi, 'tôn sùng'],
    [/hentai|хентай/gi, 'hentai'],
    [/anime|аниме/gi, 'anime'],
    [/vintage|винтаж|ретро/gi, 'cổ điển'],
    [/romantic|романтическое/gi, 'lãng mạn'],
    [/funny|приколы|смешные/gi, 'hài hước'],
    [/compilation|подборки|сборник/gi, 'tổng hợp'],
    [/pov|от первого лица/gi, 'góc nhìn thứ nhất'],
    [/solo|соло/gi, 'đơn'],
    [/anal|анальный секс|анал/gi, 'hậu môn'],
    [/oral|оральный секс/gi, 'đường miệng'],
    [/toys?|sex toys?|игрушки|секс-игрушки/gi, 'đồ chơi'],
    [/uncategorized|без категории|общее/gi, 'chưa phân loại'],
    [/all|все|любое/gi, 'tất cả']
  ];

  function isCategoryControl(node) {
    var element = node && (node.nodeType === 1 ? node : node.parentElement);
    for (var depth = 0; element && depth < 7; depth++, element = element.parentElement) {
      var marker = String(element.className || '') + ' ' + String(element.getAttribute && (element.getAttribute('data-name') || '') || '');
      if (/(select|filter|setting|menu|category|catalog|submenu)/i.test(marker)) return true;
    }
    return false;
  }

  function translateCategoryValue(value) {
    var translated = String(value || '').trim();
    if (!translated) return null;
    var original = translated;
    for (var i = 0; i < categoryRules.length; i++) translated = translated.replace(categoryRules[i][0], categoryRules[i][1]);
    return translated !== original ? translated : null;
  }

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
    if (!translated && isCategoryControl(node)) translated = translateCategoryValue(node.nodeValue);
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
