/*
 * 85PO (experimental)
 * -------------------
 * Plugin thử nghiệm lấy phim từ https://www.85po.com cho Lampa.
 * Nếu chạy ổn sẽ chuyển thành module SISI chính thức phía server.
 *
 * Link video (get_file) của 85po BỊ KHÓA THEO IP: link sinh ra cho IP nào
 * thì chỉ IP đó phát được. Vì vậy plugin dùng chính server Lampac làm proxy
 * cho CẢ HAI bước — tải trang HTML (qua /corseu) và phát video (qua /media)
 * — để hai bước cùng một IP server.
 *
 * Yêu cầu server (làm MỘT lần trong Termux):
 *   proot-distro login ubuntu -- bash -c '
 *     cd /root/lampac
 *     grep -q "CorsMedia" init.conf || sed -i "0,/{/s//{\n  \"CorsMedia\": { \"tokens\": [\"lampac\"] },\n  \"Corseu\": { \"tokens\": [\"lampac\"] },/" init.conf
 *   '
 *   lampac stop && lampac start
 *
 * Plugin tự tìm địa chỉ server Lampac từ danh sách plugin đã cài (online.js,
 * lampainit.js...). Token mặc định: "lampac". Cả hai đổi được trong
 * Cài đặt -> 85PO.
 */
(function () {
  'use strict';

  if (window.plugin_85po_ready) return;
  window.plugin_85po_ready = true;

  var HOST = 'https://www.85po.com';
  var COMPONENT = '85po';
  var SET_COMPONENT = '85po';
  var SET_SERVER = '85po_server';
  var SET_TOKEN = '85po_token';
  var SET_PROXY = '85po_cors_proxy';
  var DEFAULT_TOKEN = 'lampac';
  var DEFAULT_PROXY = 'https://cors.eu.org/';

  // ───────────────────────── helpers ─────────────────────────

  function storage(name, def) {
    var v = Lampa.Storage.get(name, def);
    return v === undefined || v === null ? def : v;
  }

  function getToken() {
    return (storage(SET_TOKEN, DEFAULT_TOKEN) + '').trim();
  }

  function fallbackProxy() {
    return (storage(SET_PROXY, DEFAULT_PROXY) + '').trim();
  }

  // Tìm địa chỉ server Lampac:
  //  1. Người dùng tự điền trong Cài đặt -> 85PO
  //  2. Quét danh sách plugin đã cài, lấy origin của online.js/lampainit.js...
  //  3. Origin của trang hiện tại (khi Lampa mở từ http://IP:9118)
  function detectServer() {
    var manual = (storage(SET_SERVER, '') + '').trim().replace(/\/+$/, '');
    if (manual) return manual;

    try {
      var plugins = (Lampa.Plugins && Lampa.Plugins.get()) || [];
      for (var i = 0; i < plugins.length; i++) {
        var u = ((plugins[i] && plugins[i].url) || '') + '';
        if (/^https?:\/\//i.test(u) && /\/(lampainit|online|online-compact|sisi|vietnamese|gst|ts)\.js/i.test(u)) {
          return u.replace(/^(https?:\/\/[^\/]+).*$/i, '$1');
        }
      }
    } catch (e) {}

    try {
      if (/^https?:$/i.test(window.location.protocol)) return window.location.origin;
    } catch (e) {}

    return '';
  }

  function pageUrl(path, page) {
    // path chứa "{page}"; trang 1 bỏ hẳn "{page}/" như cấu trúc của 85po
    if (page <= 1) return HOST + '/' + path.replace('{page}/', '').replace('{page}', '');
    return HOST + '/' + path.replace('{page}', page + '');
  }

  // Tải HTML: ưu tiên server Lampac (/corseu), lỗi thì thử proxy công cộng
  function fetchHtml(url, ok, fail) {
    var network = new Lampa.Reguest();
    network.timeout(20000);

    var server = detectServer();
    var token = getToken();

    function viaPublic() {
      var p = fallbackProxy();
      if (!p) return fail();
      network.silent(p + url, function (str) { ok(str + '', false); }, function () { fail(); }, false, { dataType: 'text' });
    }

    if (server && token) {
      var target = server + '/corseu?auth_token=' + encodeURIComponent(token) + '&url=' + encodeURIComponent(url);
      network.silent(target, function (str) {
        ok(str + '', true);
      }, function () {
        viaPublic();
      }, false, { dataType: 'text' });
    } else {
      viaPublic();
    }

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

    fetchHtml(element.url, function (html, viaServer) {
      Lampa.Loading.stop();
      var link = extractVideo(html);

      if (!link) {
        Lampa.Noty.show('85PO: không tìm thấy link video');
        return;
      }

      var server = detectServer();
      var token = getToken();

      if (viaServer && server && token) {
        // Phát qua proxy server Lampac: cùng IP với bước tải HTML,
        // kèm Referer để qua chặn hotlink.
        var headers = JSON.stringify({ referer: HOST + '/' });
        link = server + '/media?auth_token=' + encodeURIComponent(token) +
          '&type=stream&url=' + encodeURIComponent(link) +
          '&headers=' + encodeURIComponent(headers);
      } else {
        Lampa.Noty.show('85PO: chưa nối được server Lampac — phát thẳng, có thể lỗi do link khóa IP');
      }

      Lampa.Player.play({
        url: link,
        title: element.title,
        tv: false
      });

      Lampa.Player.playlist([]);
    }, function () {
      Lampa.Loading.stop();
      Lampa.Noty.show('85PO: không tải được trang video (kiểm tra Cài đặt -> 85PO)');
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
        if (first) comp.empty('Không tải được danh sách. Kiểm tra Cài đặt -> 85PO (server/token hoặc proxy dự phòng).');
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
      param: { name: SET_SERVER, type: 'input', values: '', default: '' },
      field: {
        name: 'Server Lampac',
        description: 'Để trống = tự tìm. Chỉ điền khi tự động không được, ví dụ: http://192.168.1.5:9118'
      }
    });

    Lampa.SettingsApi.addParam({
      component: SET_COMPONENT,
      param: { name: SET_TOKEN, type: 'input', values: '', default: DEFAULT_TOKEN },
      field: {
        name: 'Token proxy',
        description: 'Token của CorsMedia/Corseu trong init.conf. Mặc định: lampac'
      }
    });

    Lampa.SettingsApi.addParam({
      component: SET_COMPONENT,
      param: { name: SET_PROXY, type: 'input', values: '', default: DEFAULT_PROXY },
      field: {
        name: 'Proxy CORS dự phòng',
        description: 'Chỉ dùng khi không nối được server (xem danh sách vẫn được, phát video có thể lỗi vì link khóa IP).'
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
