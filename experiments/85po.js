/*
 * 85PO (experimental)
 * -------------------
 * Plugin thử nghiệm lấy phim từ https://www.85po.com cho Lampa.
 * Nếu chạy ổn sẽ chuyển thành module SISI chính thức phía server.
 *
 * Cách hoạt động:
 *  - Thêm nút "85PO" vào menu trái của Lampa
 *  - Danh mục: Mới nhất / Phổ biến / Đánh giá cao / Thể loại / Tìm kiếm
 *  - Trang HTML của 85po được tải qua proxy CORS (mặc định cors.eu.org,
 *    đổi được trong Cài đặt -> 85PO)
 *  - Link video mp4 (get_file) được bóc trực tiếp từ trang xem;
 *    có thể khai báo "Stream proxy prefix" để phát qua proxy của server
 *    Lampac (ví dụ: http://IP:9118/media/stream/<token>/)
 *
 * Cài đặt -> 85PO:
 *  - Proxy CORS      : prefix gắn trước URL trang web khi tải HTML
 *  - Stream proxy    : prefix gắn trước URL mp4 khi phát (rỗng = phát thẳng)
 */
(function () {
  'use strict';

  if (window.plugin_85po_ready) return;
  window.plugin_85po_ready = true;

  var HOST = 'https://www.85po.com';
  var COMPONENT = '85po';
  var SET_COMPONENT = '85po';
  var SET_PROXY = '85po_cors_proxy';
  var SET_STREAM = '85po_stream_proxy';
  var DEFAULT_PROXY = 'https://cors.eu.org/';

  // ───────────────────────── helpers ─────────────────────────

  function storage(name, def) {
    var v = Lampa.Storage.get(name, def);
    return v === undefined || v === null ? def : v;
  }

  function corsProxy() {
    var p = (storage(SET_PROXY, DEFAULT_PROXY) + '').trim();
    return p;
  }

  function streamProxy() {
    return (storage(SET_STREAM, '') + '').trim();
  }

  function pageUrl(path, page) {
    // path chứa "{page}"; trang 1 bỏ hẳn "{page}/" như cấu trúc của 85po
    if (page <= 1) return HOST + '/' + path.replace('{page}/', '').replace('{page}', '');
    return HOST + '/' + path.replace('{page}', page + '');
  }

  function fetchHtml(url, ok, fail) {
    var network = new Lampa.Reguest();
    network.timeout(20000);
    network.silent(corsProxy() + url, function (str) {
      ok(str + '');
    }, function () {
      fail();
    }, false, { dataType: 'text' });
    return network;
  }

  function absUrl(u) {
    if (!u) return '';
    if (u.indexOf('http') === 0) return u;
    if (u.indexOf('//') === 0) return 'https:' + u;
    if (u.charAt(0) === '/') return HOST + u;
    return HOST + '/' + u;
  }

  function parseList(html) {
    var results = [];
    try {
      var doc = new DOMParser().parseFromString(html, 'text/html');
      var anchors = doc.querySelectorAll("a[href*='/v/']");
      var seen = {};

      for (var i = 0; i < anchors.length; i++) {
        var a = anchors[i];
        var href = a.getAttribute('href') || '';
        if (!href || seen[href]) continue;

        var img = a.querySelector('img');
        if (!img) continue; // link chữ (breadcrumb, related text...) bỏ qua

        seen[href] = true;

        var poster = img.getAttribute('data-original') || img.getAttribute('data-src') || img.getAttribute('src') || '';
        var title = a.getAttribute('title') || img.getAttribute('alt') || (a.textContent || '').trim();

        var duration = '';
        var dnode = a.querySelector("[class*='duration']") || (a.parentElement ? a.parentElement.querySelector("[class*='duration']") : null);
        if (dnode) duration = (dnode.textContent || '').trim();

        results.push({
          title: title || '85PO',
          url: absUrl(href),
          img: absUrl(poster),
          time: duration
        });
      }
    } catch (e) {}
    return results;
  }

  function extractVideo(html) {
    var m;
    var qualities = ['_2160p', '_1440p', '_1080p', '_720p', '_480p', '_360p'];

    for (var i = 0; i < qualities.length; i++) {
      m = html.match(new RegExp("(https?://[^/]+/get_file/[^'\"\\s]+" + qualities[i] + "\\.mp4[^'\"\\s]*)", 'i'));
      if (m && m[1]) return clean(m[1]);
    }

    var alts = ['video_alt_url5', 'video_alt_url4', 'video_alt_url3', 'video_alt_url2', 'video_alt_url', 'video_url'];
    for (var j = 0; j < alts.length; j++) {
      m = html.match(new RegExp(alts[j] + ":\\s*'([^']+)'", 'i'));
      if (m && m[1] && m[1].indexOf('/get_file/') !== -1) return clean(m[1]);
    }

    m = html.match(/(https?:\/\/[^/]+\/get_file\/[^'"\s]+\.mp4[^'"\s]*)/i);
    if (m && m[1]) return clean(m[1]);

    return null;

    function clean(link) {
      return link
        .replace(/&amp;/g, '&')
        .replace('function/0/', '')
        .replace(/([?&])download_filename=[^&]*/g, '$1')
        .replace(/([?&])download=true/g, '$1')
        .replace(/[?&]+$/, '')
        .replace(/([?&])&+/g, '$1');
    }
  }

  function play(element) {
    Lampa.Loading.start(function () {
      Lampa.Loading.stop();
    });

    fetchHtml(element.url, function (html) {
      Lampa.Loading.stop();
      var link = extractVideo(html);

      if (!link) {
        Lampa.Noty.show('85PO: không tìm thấy link video');
        return;
      }

      var sp = streamProxy();
      if (sp) link = sp + encodeURIComponent(link);

      Lampa.Player.play({
        url: link,
        title: element.title,
        tv: false
      });

      Lampa.Player.playlist([]);
    }, function () {
      Lampa.Loading.stop();
      Lampa.Noty.show('85PO: không tải được trang video (kiểm tra proxy CORS)');
    });
  }

  // ───────────────────────── component ─────────────────────────

  function component(object) {
    var comp = this;
    var scroll = new Lampa.Scroll({ mask: true, over: true, step: 250 });
    var html = $('<div class="po85"></div>');
    var body = $('<div class="category-full"></div>');
    var page = object.page || 1;
    var waiting = false;
    var last;
    var network = null;

    this.create = function () {
      this.activity.loader(true);
      load(true);
      return this.render();
    };

    function load(first) {
      if (waiting) return;
      waiting = true;

      network = fetchHtml(pageUrl(object.search_path || object.path, page), function (str) {
        waiting = false;
        var items = parseList(str);
        comp.build(items, first);
      }, function () {
        waiting = false;
        if (first) comp.empty('Không tải được danh sách. Kiểm tra "Proxy CORS" trong Cài đặt -> 85PO.');
      });
    }

    this.empty = function (msg) {
      var empty = new Lampa.Empty({ descr: msg });
      html.append(empty.render());
      this.start = empty.start;
      this.activity.loader(false);
      this.activity.toggle();
    };

    this.build = function (items, first) {
      if (first && !items.length) return this.empty('Danh sách trống hoặc site đổi cấu trúc.');

      items.forEach(function (element) {
        var card = Lampa.Template.get('card', {
          title: element.title,
          release_year: ''
        });

        card.addClass('card--collection po85-card');
        card.find('.card__img').attr('src', element.img).on('error', function () {
          $(this).attr('src', './img/img_broken.svg');
        });

        if (element.time) {
          card.find('.card__view').append('<div class="po85-time">' + element.time + '</div>');
        }

        card.on('hover:focus', function () {
          last = card[0];
          scroll.update(card, true);
        });

        card.on('hover:enter', function () {
          play(element);
        });

        body.append(card);
      });

      if (first) {
        scroll.render().addClass('layer--wheight');
        scroll.append(body);
        html.append(scroll.render());
      } else {
        Lampa.Controller.enable('content');
      }

      this.activity.loader(false);
      this.activity.toggle();
    };

    this.start = function () {
      Lampa.Controller.add('content', {
        link: this,
        toggle: function () {
          Lampa.Controller.collectionSet(scroll.render());
          Lampa.Controller.collectionFocus(last || false, scroll.render());
        },
        left: function () {
          if (Navigator.canmove('left')) Navigator.move('left');
          else Lampa.Controller.toggle('menu');
        },
        right: function () {
          Navigator.move('right');
        },
        up: function () {
          if (Navigator.canmove('up')) Navigator.move('up');
          else Lampa.Controller.toggle('head');
        },
        down: function () {
          if (Navigator.canmove('down')) Navigator.move('down');
          else {
            page++;
            load(false);
          }
        },
        back: function () {
          Lampa.Activity.backward();
        }
      });

      Lampa.Controller.toggle('content');
    };

    this.pause = function () {};
    this.stop = function () {};

    this.render = function () {
      return html;
    };

    this.destroy = function () {
      if (network) network.clear();
      scroll.destroy();
      html.remove();
      body.remove();
    };
  }

  // ───────────────────────── menu ─────────────────────────

  var CATEGORIES = [
    ['Tự quay', 'zi-pai'],
    ['Đài Loan', 'tai-wan'],
    ['Nhật Bản', 'ri-ben'],
    ['Cặp đôi', 'qing-lv'],
    ['Ngực lớn', 'ju-ru'],
    ['Mông đẹp', 'mei-tun'],
    ['Thủ dâm', 'zi-wei'],
    ['Squirt', 'pen-shui'],
    ['Quan hệ', 'zuo-ai'],
    ['Khỏa thân', 'quan-luo'],
    ['Trên giường', 'chuang-shang'],
    ['Gợi cảm', 'sao']
  ];

  function openList(title, path) {
    Lampa.Activity.push({
      url: '',
      title: '85PO - ' + title,
      component: COMPONENT,
      path: path,
      page: 1
    });
  }

  function openSearch() {
    Lampa.Input.edit({
      title: 'Tìm kiếm 85PO',
      value: '',
      free: true,
      nosave: true
    }, function (query) {
      if (query) openList('Tìm: ' + query, 'search/' + encodeURIComponent(query) + '/{page}/');
    });
  }

  function openMenu() {
    var items = [
      { title: 'Mới nhất', path: 'latest-updates/{page}/' },
      { title: 'Phổ biến', path: 'most-popular/{page}/' },
      { title: 'Đánh giá cao', path: 'top-rated/{page}/' },
      { title: 'Tìm kiếm', search: true },
      { title: '── Thể loại ──', separator: true }
    ];

    CATEGORIES.forEach(function (c) {
      items.push({ title: c[0], path: 'tags/' + c[1] + '/{page}/' });
    });

    Lampa.Select.show({
      title: '85PO',
      items: items,
      onSelect: function (item) {
        if (item.separator) return openMenu();
        if (item.search) return openSearch();
        openList(item.title, item.path);
      },
      onBack: function () {
        Lampa.Controller.toggle('menu');
      }
    });
  }

  // ───────────────────────── install ─────────────────────────

  function installStyle() {
    if (document.getElementById('po85-style')) return;
    var style = document.createElement('style');
    style.id = 'po85-style';
    style.textContent = [
      '.po85-card .card__view { padding-bottom: 56.25% !important; border-radius: 1.1em; }',
      '.po85-card .card__img { object-fit: cover; width: 100%; height: 100%; border-radius: 1.1em; }',
      '.po85-time {',
      '  position: absolute; right: .5em; bottom: .5em;',
      '  background: rgba(0,0,0,.72); color: #fff;',
      '  padding: .2em .45em; border-radius: .45em; font-size: .8em;',
      '}',
      '.po85-card { width: 25%; }',
      '@media screen and (max-width: 991px) { .po85-card { width: 33.333%; } }',
      '@media screen and (orientation: portrait) and (max-width: 720px) { .po85-card { width: 50%; } }'
    ].join('\n');
    document.head.appendChild(style);
  }

  function installSettings() {
    if (!Lampa.SettingsApi) return;

    Lampa.SettingsApi.addComponent({
      component: SET_COMPONENT,
      name: '85PO',
      icon: '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><rect x="2" y="4" width="20" height="16" rx="3" stroke="currentColor" stroke-width="2"/><path d="M10 9l5 3-5 3V9z" fill="currentColor"/></svg>'
    });

    Lampa.SettingsApi.addParam({
      component: SET_COMPONENT,
      param: { name: SET_PROXY, type: 'input', values: '', default: DEFAULT_PROXY },
      field: {
        name: 'Proxy CORS',
        description: 'Prefix gắn trước URL khi tải trang 85po. Mặc định: ' + DEFAULT_PROXY
      }
    });

    Lampa.SettingsApi.addParam({
      component: SET_COMPONENT,
      param: { name: SET_STREAM, type: 'input', values: '', default: '' },
      field: {
        name: 'Stream proxy prefix',
        description: 'Phát video qua proxy server Lampac, ví dụ: http://IP:9118/media/stream/TOKEN/ — để trống thì phát thẳng link mp4.'
      }
    });
  }

  function installMenu() {
    var ico = '<svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg"><rect x="2" y="4" width="20" height="16" rx="3" stroke="currentColor" stroke-width="2"/><path d="M10 9l5 3-5 3V9z" fill="currentColor"/></svg>';
    var button = $([
      '<li class="menu__item selector" data-action="po85">',
      '  <div class="menu__ico">' + ico + '</div>',
      '  <div class="menu__text">85PO</div>',
      '</li>'
    ].join(''));

    button.on('hover:enter', function () {
      if (Lampa.ParentalControl && Lampa.ParentalControl.query) {
        Lampa.ParentalControl.query(openMenu, function () {});
      } else openMenu();
    });

    $('.menu .menu__list').eq(0).append(button);
  }

  function init() {
    Lampa.Component.add(COMPONENT, component);
    installStyle();
    installSettings();
    installMenu();
  }

  if (window.appready) init();
  else {
    Lampa.Listener.follow('app', function (e) {
      if (e.type === 'ready') init();
    });
  }
})();
