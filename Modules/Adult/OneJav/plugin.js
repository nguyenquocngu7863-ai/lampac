(function () {
    'use strict';

    // API base tự suy từ URL của chính script này (vd http://host:port/onejav.js)
    var API = (function () {
        var s = (document.currentScript && document.currentScript.src) || '';
        try { return new URL(s).origin; } catch (e) { return '{localhost}'; }
    })();

    var NET = new Lampa.Reguest();

    function netUrl(u) {
        var url = API + u;
        var sep = url.indexOf('?') === -1 ? '?' : '&';
        var email = Lampa.Storage.get('account_email', '');
        var uid = Lampa.Storage.get('lampac_unic_id', '');
        var token = '{token}';
        if (email) url += sep + 'account_email=' + encodeURIComponent(email), sep = '&';
        if (uid) url += sep + 'uid=' + encodeURIComponent(uid), sep = '&';
        if (token) url += sep + 'token=' + encodeURIComponent(token);
        return url;
    }

    function get(u, cb, err) {
        NET.native(netUrl(u), function (str) {
            try { cb(JSON.parse(str)); } catch (e) { err && err(e); }
        }, function (e) { err && err(e); }, false, false, { dataType: 'text' });
    }

    function card(item, onOpen) {
        var el = document.createElement('div');
        el.className = 'onejav-card selector';
        el.setAttribute('tabindex', '0');
        el.innerHTML =
            '<div class="oj-poster"><img loading="lazy" src="' + (item.poster || '') + '">' +
            '<div class="oj-code">' + (item.code || item.id || '') + '</div></div>' +
            '<div class="oj-title">' + (item.title || '') + '</div>';
        $(el).on('hover:enter', function () { onOpen(item); });
        $(el).on('click', function () { onOpen(item); });
        return el;
    }

    function toast(msg) {
        Lampa.Noty ? Lampa.Noty.show(msg) : console.log('[OneJAV]', msg);
    }

    // ============================ Screen: list ============================
    function listScreen(params) {
        var html, body, grid, page = 1, loading = false, hasMore = true;

        this.create = function () {
            html = document.createElement('div');
            html.className = 'onejav-screen';
            html.innerHTML =
                '<div class="oj-top">' +
                '<div class="oj-back selector"><span>‹</span></div>' +
                '<div class="oj-h1">' + Lampa.Utils.escapeHtml(params.title || 'OneJAV') + '</div>' +
                '<div class="oj-search selector">🔍</div>' +
                '</div>' +
                '<div class="oj-grid"></div>' +
                '<div class="oj-more selector">Hiện thêm</div>';
            body = html;
            grid = html.querySelector('.oj-grid');
            this.activity.loader(true);

            $(html.querySelector('.oj-back')).on('hover:enter', this.back).on('click', this.back);
            $(html.querySelector('.oj-search')).on('hover:enter', openSearch).on('click', openSearch);
            $(html.querySelector('.oj-more')).on('hover:enter', loadMore).on('click', loadMore);
        };

        function fetchPage(p) {
            if (loading) return;
            loading = true;
            html.querySelector('.oj-more').style.display = 'none';
            get(params.endpoint(p), function (json) {
                loading = false;
                hasMore = !!json.hasMore;
                (json.results || []).forEach(function (it) {
                    grid.appendChild(card(it, openCard));
                });
                html.querySelector('.oj-more').style.display = hasMore ? '' : 'none';
                done();
                Lampa.Controller.collection();
            }, function () {
                loading = false;
                done();
                toast('Không tải được danh sách (OneJAV có thể bị chặn mạng)');
                html.querySelector('.oj-more').style.display = '';
            });
        }

        function loadMore() { page++; fetchPage(page); }

        function done() {
            try { Lampa.Activity.active().loader(false); } catch (e) {}
        }

        this.start = function () {
            Lampa.Controller.add('content', {
                toggle: function () {
                    Lampa.Controller.collectionSet(html);
                    Lampa.Controller.collectionFocus(html.querySelector('.selector'), html);
                },
                up: Lampa.Navigator.moveUp,
                down: Lampa.Navigator.moveDown,
                left: Lampa.Navigator.moveLeft,
                right: Lampa.Navigator.moveRight,
                back: function () { Lampa.Activity.backward(); }
            });
            Lampa.Controller.enable('content');
            fetchPage(1);
        };

        this.render = function () { return html; };
        this.pause = function () {};
        this.stop = function () { NET.clear(); };
        this.destroy = function () {};

        function openCard(item) {
            Lampa.Activity.push({
                title: item.code || item.id,
                component: 'onejav_card',
                id: item.id,
                code: item.code
            });
        }

        function openSearch() {
            Lampa.Keyboard.show({ value: '', placeholder: 'Mã JAV, vd SSIS-123' }, function (val) {
                if (val && val.trim()) {
                    var q = encodeURIComponent(val.trim());
                    Lampa.Activity.push({
                        title: 'Tìm ' + val.trim(),
                        component: 'onejav_list',
                        onejav: {
                            title: 'Kết quả: ' + val.trim(),
                            endpoint: function (p) { return '/onejav/search?q=' + q + '&page=' + p; }
                        }
                    });
                }
            }, function () {});
        }
    }

    // ============================ Screen: card ============================
    function cardScreen(params) {
        var html, streams = [];

        this.create = function () {
            html = document.createElement('div');
            html.className = 'onejav-screen';
            html.innerHTML =
                '<div class="oj-top">' +
                '<div class="oj-back selector"><span>‹</span></div>' +
                '<div class="oj-h1">Chi tiết</div>' +
                '</div>' +
                '<div class="oj-detail"><div class="oj-detail-poster"><div class="oj-spin"></div></div>' +
                '<div class="oj-info"><div class="oj-info-title">…</div><div class="oj-info-desc"></div>' +
                '<div class="oj-streams"><div class="oj-empty">Đang tìm magnet…</div></div></div></div>';
            this.activity.loader(true);

            $(html.querySelector('.oj-back')).on('hover:enter', this.back).on('click', this.back);

            get('/onejav/card?id=' + encodeURIComponent(params.id), function (json) {
                try { Lampa.Activity.active().loader(false); } catch (e) {}
                streams = json.magnets || [];
                html.querySelector('.oj-detail-poster').innerHTML =
                    '<img src="' + (json.poster || '') + '">';
                html.querySelector('.oj-info-title').textContent =
                    (json.title || params.code || '') + '  [' + (json.original_title || '') + ']';
                html.querySelector('.oj-info-desc').textContent =
                    (json.actresses && json.actresses.length ? 'Diễn viên: ' + json.actresses.join(', ') + '\n' : '') +
                    (json.description || '');

                var box = html.querySelector('.oj-streams');
                box.innerHTML = '';
                if (json.error) { box.innerHTML = '<div class="oj-empty">' + Lampa.Utils.escapeHtml(json.error) + '</div>'; return; }
                if (!streams.length) { box.innerHTML = '<div class="oj-empty">Không tìm thấy magnet cho mã này.</div>'; return; }

                streams.forEach(function (s, i) {
                    var row = document.createElement('div');
                    row.className = 'oj-stream selector';
                    row.innerHTML = '<b>' + Lampa.Utils.escapeHtml(s.source || 'Magnet') + '</b>' +
                        '<span>' + Lampa.Utils.escapeHtml((s.title || '').slice(0, 90)) + '</span>';
                    $(row).on('hover:enter', function () { play(i, s); }).on('click', function () { play(i, s); });
                    box.appendChild(row);
                });
                Lampa.Controller.collection();
            }, function () {
                html.querySelector('.oj-streams').innerHTML =
                    '<div class="oj-empty">Lỗi tải trang chi tiết.</div>';
            });
        };

        function play(i, s) {
            toast('Đang gửi TorrServer… ' + (s.source || ''));
            // Lampa phát trực tiếp URL /onejav/play (server sẽ add torrent rồi redirect tới stream).
            Lampa.Player.play({ url: API + s.url.replace(API, ''), title: s.title || params.code });
        }

        this.start = function () {
            Lampa.Controller.add('content', {
                toggle: function () {
                    Lampa.Controller.collectionSet(html);
                    Lampa.Controller.collectionFocus(html.querySelector('.selector'), html);
                },
                up: Lampa.Navigator.moveUp,
                down: Lampa.Navigator.moveDown,
                left: Lampa.Navigator.moveLeft,
                right: Lampa.Navigator.moveRight,
                back: function () { Lampa.Activity.backward(); }
            });
            Lampa.Controller.enable('content');
        };

        this.render = function () { return html; };
        this.pause = function () {};
        this.stop = function () { NET.clear(); };
        this.destroy = function () {};
    }

    // ============================ Home entry ============================
    function openHome() {
        Lampa.Activity.push({
            url: '',
            title: 'OneJAV 🎌',
            component: 'onejav_list',
            page: 1,
            onejav: {
                title: 'OneJAV 🎌 — Mới nhất',
                endpoint: function (p) { return '/onejav/list?page=' + p; }
            }
        });
    }

    function addMenuButton() {
        if (document.querySelector('.menu__item[data-action="onejav"]')) return;
        var list = document.querySelectorAll('.menu .menu__list');
        if (!list.length) return;
        var li = document.createElement('li');
        li.className = 'menu__item selector';
        li.setAttribute('data-action', 'onejav');
        li.innerHTML =
            '<div class="menu__ico">🎌</div>' +
            '<div class="menu__text">OneJAV</div>';
        $(li).on('click', openHome).on('hover:enter', openHome);
        // chèn sau mục đầu tiên (Home)
        list[0].appendChild(li);
    }

    var STYLE =
        '.onejav-screen{padding:1em .8em 3em;min-height:100%}' +
        '.oj-top{display:flex;align-items:center;gap:.6em;margin-bottom:.8em}' +
        '.oj-back{font-size:2.2em;width:2.2em;height:2.2em;display:flex;align-items:center;justify-content:center;border-radius:12px;background:rgba(255,255,255,.08)}' +
        '.oj-search{margin-left:auto;font-size:1.6em;width:2.4em;height:2.4em;display:flex;align-items:center;justify-content:center;border-radius:12px;background:rgba(255,255,255,.08)}' +
        '.oj-h1{font-size:1.7em;font-weight:800;flex:1}' +
        '.oj-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:1em}' +
        '.onejav-card{cursor:pointer}' +
        '.oj-poster{position:relative;border-radius:12px;overflow:hidden;aspect-ratio:2/3;background:rgba(255,255,255,.06)}' +
        '.oj-poster img{width:100%;height:100%;object-fit:cover;display:block}' +
        '.oj-code{position:absolute;left:0;bottom:0;right:0;padding:.3em .5em;font-size:.95em;font-weight:700;background:linear-gradient(transparent,rgba(0,0,0,.85))}' +
        '.oj-title{margin-top:.4em;font-size:1.05em;line-height:1.3;opacity:.85;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}' +
        '.onejav-card.focus,.onejav-card:hover .oj-poster,.oj-stream.focus,.oj-stream:hover{outline:3px solid #ff5e7e;outline-offset:2px}' +
        '.oj-more{margin:1.2em auto;padding:.9em 2em;text-align:center;border-radius:999px;background:rgba(255,94,126,.18);font-weight:700;width:max-content}' +
        '.oj-detail{display:flex;gap:1.2em;flex-wrap:wrap}' +
        '.oj-detail-poster{flex:0 0 200px;aspect-ratio:2/3;border-radius:12px;overflow:hidden;background:rgba(255,255,255,.06)}' +
        '.oj-detail-poster img{width:100%;height:100%;object-fit:cover}' +
        '.oj-info{flex:1;min-width:260px}' +
        '.oj-info-title{font-size:1.5em;font-weight:800;margin-bottom:.5em}' +
        '.oj-info-desc{white-space:pre-line;opacity:.7;line-height:1.5;margin-bottom:1em}' +
        '.oj-streams{display:flex;flex-direction:column;gap:.5em}' +
        '.oj-stream{display:flex;flex-direction:column;gap:.2em;padding:.9em 1.1em;border-radius:12px;background:rgba(255,255,255,.07);cursor:pointer}' +
        '.oj-stream b{color:#ff8da1;font-size:1.15em}' +
        '.oj-stream span{opacity:.8;font-size:.95em}' +
        '.oj-empty{opacity:.6;padding:1em;font-style:italic}' +
        '.oj-spin{width:100%;height:100%;background:linear-gradient(90deg,rgba(255,255,255,.05),rgba(255,255,255,.15),rgba(255,255,255,.05));background-size:200% 100%;animation:ojsh 1.2s infinite}' +
        '@keyframes ojsh{to{background-position:-200% 0}}';

    function startPlugin() {
        var st = document.createElement('style');
        st.textContent = STYLE;
        document.head.appendChild(st);

        Lampa.Component.add('onejav_list', listScreen);
        Lampa.Component.add('onejav_card', cardScreen);

        // Nút mở ở màn hình Cài đặt → mục khác (đơn giản, tương thích cao)
        if (Lampa.SettingsApi && Lampa.SettingsApi.addParam) {
            Lampa.SettingsApi.addParam({
                component: 'plugin_more',
                param: { name: 'onejav_open', type: 'trigger', default: false },
                field: { name: '🎌 OneJAV (phim torrent)', description: 'Duyệt/tìm JAV, phát qua TorrServer' },
                onChange: function () { setTimeout(openHome, 50); }
            });
        }

        console.log('[OneJAV] plugin ready, API =', API);
    }

    if (window.appready) startPlugin();
    else Lampa.Listener.follow('app', function (e) { if (e.type === 'ready') startPlugin(); });

    // expose để gắn nút home nếu muốn
    window.LampaOneJav = { open: openHome };
})();
