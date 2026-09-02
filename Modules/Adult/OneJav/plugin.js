(function () {
    'use strict';

    var API = (function () {
        var s = (document.currentScript && document.currentScript.src) || '';
        try { return new URL(s).origin; } catch (e) { return '{localhost}'; }
    })();

    var TAGS = [
        ['Uncensored', 'uncensored'], ['Big Tits', 'big-tits'], ['Creampie', 'creampie'],
        ['Anal', 'anal'], ['Amateur', 'amateur'], ['Blowjob', 'blowjob'],
        ['Cosplay', 'cosplay'], ['Solowork', 'solowork'], ['Lesbian', 'lesbian'],
        ['Gangbang', 'gangbang'], ['Cowgirl', 'cowgirl'], ['4K', '4k'],
        ['Mature', 'mature-woman'], ['School Girl', 'school-girls'],
        ['Small Tits', 'small-tits'], ['Huge Butt', 'huge-butt'],
        ['Deep Throat', 'deep-throating'], ['Bukkake', 'bukkake']
    ];

    var NET = new Lampa.Reguest();

    function netUrl(u) {
        var url = API + u;
        var sep = url.indexOf('?') === -1 ? '?' : '&';
        var email = Lampa.Storage.get('account_email', '');
        var uid = Lampa.Storage.get('lampac_unic_id', '');
        var token = '{token}';
        if (email) { url += sep + 'account_email=' + encodeURIComponent(email); sep = '&'; }
        if (uid) { url += sep + 'uid=' + encodeURIComponent(uid); sep = '&'; }
        if (token) url += sep + 'token=' + encodeURIComponent(token);
        return url;
    }

    function get(u, cb, err) {
        NET.native(netUrl(u), function (str) {
            try { cb(JSON.parse(str)); } catch (e) { err && err(e); }
        }, function (e) { err && err(e); }, false, false, { dataType: 'text' });
    }

    function esc(s) { return Lampa.Utils.escapeHtml(String(s || '')); }
    function toast(m) { Lampa.Noty ? Lampa.Noty.show(m) : console.log('[OneJAV]', m); }

    // ============================ Screen: lưới phim ============================
    function gridScreen(p) {
        var html, grid, page = 1, loading = false, hasMore = true;

        this.create = function () {
            html = document.createElement('div');
            html.className = 'oj-screen';
            html.innerHTML =
                '<div class="oj-top">' +
                '<div class="oj-btn oj-back selector"><span>‹</span></div>' +
                '<div class="oj-h1">' + esc(p.title || 'OneJAV') + '</div>' +
                '<div class="oj-btn oj-search selector">🔍</div>' +
                '</div><div class="oj-grid"></div>' +
                '<div class="oj-more selector">Hiện thêm</div>';

            grid = html.querySelector('.oj-grid');
            $(html.querySelector('.oj-back')).on('hover:enter click', this.back);
            $(html.querySelector('.oj-search')).on('hover:enter click', openSearch);
            $(html.querySelector('.oj-more')).on('hover:enter click', more);

            load(1);
        };

        function endpoint(pg) {
            if (p.q) return '/onejav/list?q=' + encodeURIComponent(p.q) + '&page=' + pg;
            if (p.tag) return '/onejav/list?path=' + encodeURIComponent(p.tag) + '&page=' + pg;
            return '/onejav/list?page=' + pg;
        }

        function load(pg) {
            if (loading) return;
            loading = true;
            html.querySelector('.oj-more').style.display = 'none';
            get(endpoint(pg), function (json) {
                loading = false;
                hasMore = !!json.hasMore;
                (json.results || []).forEach(function (it) {
                    var el = document.createElement('div');
                    el.className = 'oj-card selector';
                    el.innerHTML =
                        '<div class="oj-poster"><img src="' + esc(it.poster) + '">' +
                        '<div class="oj-badge">' + esc(it.code) + '</div></div>' +
                        '<div class="oj-name">' + esc(it.title) + '</div>';
                    $(el).on('hover:enter click', function () { openCard(it); });
                    grid.appendChild(el);
                });
                finishLoader();
                html.querySelector('.oj-more').style.display = hasMore ? '' : 'none';
                Lampa.Controller.collection();
            }, function () {
                loading = false; finishLoader();
                toast('Không tải được danh sách');
                html.querySelector('.oj-more').style.display = '';
            });
        }
        function finishLoader() { try { Lampa.Activity.active().loader(false); } catch (e) {} }
        function more() { page++; load(page); }

        this.start = function () {
            Lampa.Controller.add('content', {
                toggle: function () {
                    Lampa.Controller.collectionSet(html);
                    Lampa.Controller.collectionFocus(html.querySelector('.selector'), html);
                },
                up: Lampa.Navigator.moveUp, down: Lampa.Navigator.moveDown,
                left: Lampa.Navigator.moveLeft, right: Lampa.Navigator.moveRight,
                back: function () { Lampa.Activity.backward(); }
            });
            Lampa.Controller.enable('content');
        };

        this.render = function () { return html; };
        this.pause = function () {};
        this.stop = function () { NET.clear(); };
        this.destroy = function () {};

        function openCard(it) {
            Lampa.Activity.push({
                title: it.code || it.id,
                component: 'onejav_torrents',
                id: it.id, code: it.code
            });
        }
        function openSearch() {
            Lampa.Keyboard.show({ value: '', placeholder: 'Mã JAV, vd SSIS-123' }, function (val) {
                if (val && val.trim()) Lampa.Activity.push({
                    title: 'Tìm ' + val.trim(),
                    component: 'onejav_grid',
                    q: val.trim()
                });
            }, function () {});
        }
    }

    // ============================ Screen: chọn torrent ============================
    function torrentsScreen(p) {
        var html;

        this.create = function () {
            html = document.createElement('div');
            html.className = 'oj-screen';
            html.innerHTML =
                '<div class="oj-top"><div class="oj-btn oj-back selector"><span>‹</span></div>' +
                '<div class="oj-h1">Torrents · ' + esc(p.code || p.id) + '</div></div>' +
                '<div class="oj-pick"><div class="oj-pick-poster"><div class="oj-spin"></div></div>' +
                '<div class="oj-pick-info"><div class="oj-pick-title">Đang nạp…</div>' +
                '<div class="oj-list"><div class="oj-empty">Đang tìm torrent…</div></div></div></div>';

            $(html.querySelector('.oj-back')).on('hover:enter click', this.back);

            get('/onejav/torrents?id=' + encodeURIComponent(p.id), function (json) {
                var poster = html.querySelector('.oj-pick-poster');
                poster.innerHTML = json.poster ? '<img src="' + esc(json.poster) + '">' : '';
                html.querySelector('.oj-pick-title').textContent =
                    (json.title || p.code || '') + '  [' + (p.code || '').toUpperCase() + ']';

                var box = html.querySelector('.oj-list');
                box.innerHTML = '';
                var list = json.torrents || [];
                if (!list.length) {
                    box.innerHTML = '<div class="oj-empty">Không tìm thấy torrent cho mã này.</div>';
                    return;
                }
                list.forEach(function (t) {
                    var row = document.createElement('div');
                    row.className = 'oj-tor selector';
                    var meta = [];
                    if (t.seed > 0) meta.push('🌱 seed ' + t.seed);
                    if (t.gb) meta.push(t.gb.toFixed(1) + ' GB');
                    row.innerHTML =
                        '<div class="oj-tor-src">' + esc(t.source) + '</div>' +
                        '<div class="oj-tor-name">' + esc((t.title || '').slice(0, 120)) + '</div>' +
                        (meta.length ? '<div class="oj-tor-meta">' + esc(meta.join('  ·  ')) + '</div>' : '');
                    $(row).on('hover:enter click', function () { play(t); });
                    box.appendChild(row);
                });
                Lampa.Controller.collection();
            }, function () {
                html.querySelector('.oj-list').innerHTML = '<div class="oj-empty">Lỗi tải trang chi tiết.</div>';
            });
        };

        function play(t) {
            toast('Đang thêm vào TorrServer…');
            var u = '/onejav/play?';
            if (t.magnet) u += 'magnet=' + encodeURIComponent(t.magnet);
            else u += 'link=' + encodeURIComponent(t.link);
            get(u, function (json) {
                if (!json || !json.ok) { toast('Lỗi: ' + ((json && json.error) || 'TorrServer')); return; }
                Lampa.Player.play({ url: json.url, title: t.title || p.code });
            }, function () { toast('Không gọi được TorrServer'); });
        }

        this.start = function () {
            Lampa.Controller.add('content', {
                toggle: function () {
                    Lampa.Controller.collectionSet(html);
                    Lampa.Controller.collectionFocus(html.querySelector('.selector'), html);
                },
                up: Lampa.Navigator.moveUp, down: Lampa.Navigator.moveDown,
                left: Lampa.Navigator.moveLeft, right: Lampa.Navigator.moveRight,
                back: function () { Lampa.Activity.backward(); }
            });
            Lampa.Controller.enable('content');
        };
        this.render = function () { return html; };
        this.pause = function () {};
        this.stop = function () { NET.clear(); };
        this.destroy = function () {};
    }

    // ============================ Screen: tags ============================
    function tagsScreen() {
        var html;
        this.create = function () {
            html = document.createElement('div');
            html.className = 'oj-screen';
            html.innerHTML =
                '<div class="oj-top"><div class="oj-btn oj-back selector"><span>‹</span></div>' +
                '<div class="oj-h1">OneJAV 🎌</div></div>' +
                '<div class="oj-tags"></div>';
            $(html.querySelector('.oj-back')).on('hover:enter click', this.back);

            var box = html.querySelector('.oj-tags');
            TAGS.forEach(function (tg) {
                var b = document.createElement('div');
                b.className = 'oj-tag selector';
                b.textContent = tg[0];
                $(b).on('hover:enter click', function () {
                    Lampa.Activity.push({ title: tg[0], component: 'onejav_grid', tag: tg[1] });
                });
                box.appendChild(b);
            });
            finish();
        };
        function finish() { try { Lampa.Activity.active().loader(false); } catch (e) {} }
        this.start = function () {
            Lampa.Controller.add('content', {
                toggle: function () {
                    Lampa.Controller.collectionSet(html);
                    Lampa.Controller.collectionFocus(html.querySelector('.selector'), html);
                },
                up: Lampa.Navigator.moveUp, down: Lampa.Navigator.moveDown,
                left: Lampa.Navigator.moveLeft, right: Lampa.Navigator.moveRight,
                back: function () { Lampa.Activity.backward(); }
            });
            Lampa.Controller.enable('content');
        };
        this.render = function () { return html; };
        this.pause = function () {};
        this.stop = function () { NET.clear(); };
        this.destroy = function () {};
    }

    function openHome() {
        Lampa.Activity.push({ url: '', title: 'OneJAV 🎌', component: 'onejav_grid', page: 1 });
    }

    var STYLE =
        '.oj-screen{padding:1em .8em 3em;min-height:100%}' +
        '.oj-top{display:flex;align-items:center;gap:.6em;margin-bottom:.9em}' +
        '.oj-btn{width:2.4em;height:2.4em;display:flex;align-items:center;justify-content:center;border-radius:12px;background:rgba(255,255,255,.08);font-size:1.5em}' +
        '.oj-search{margin-left:auto}' +
        '.oj-h1{font-size:1.6em;font-weight:800;flex:1}' +
        '.oj-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:1em}' +
        '.oj-card{cursor:pointer}' +
        '.oj-poster{position:relative;border-radius:12px;overflow:hidden;aspect-ratio:2/3;background:rgba(255,255,255,.06)}' +
        '.oj-poster img{width:100%;height:100%;object-fit:cover;display:block}' +
        '.oj-badge{position:absolute;left:0;bottom:0;right:0;padding:.3em .5em;font-size:.95em;font-weight:700;background:linear-gradient(transparent,rgba(0,0,0,.85))}' +
        '.oj-name{margin-top:.4em;font-size:1em;line-height:1.3;opacity:.85;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}' +
        '.oj-card.focus .oj-poster,.oj-tag.focus,.oj-tor.focus,.oj-more.focus,.oj-btn.focus{outline:3px solid #ff5e7e;outline-offset:2px}' +
        '.oj-more{margin:1.2em auto;padding:.9em 2em;text-align:center;border-radius:999px;background:rgba(255,94,126,.18);font-weight:700;width:max-content}' +
        '.oj-tags{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:.8em}' +
        '.oj-tag{padding:1em;border-radius:14px;background:rgba(255,255,255,.07);text-align:center;font-weight:700;cursor:pointer}' +
        '.oj-pick{display:flex;gap:1.2em;flex-wrap:wrap}' +
        '.oj-pick-poster{flex:0 0 200px;aspect-ratio:2/3;border-radius:12px;overflow:hidden;background:rgba(255,255,255,.06)}' +
        '.oj-pick-poster img{width:100%;height:100%;object-fit:cover}' +
        '.oj-pick-info{flex:1;min-width:260px}' +
        '.oj-pick-title{font-size:1.3em;font-weight:800;margin-bottom:.8em}' +
        '.oj-list{display:flex;flex-direction:column;gap:.5em}' +
        '.oj-tor{padding:.9em 1.1em;border-radius:12px;background:rgba(255,255,255,.07);cursor:pointer}' +
        '.oj-tor-src{color:#ff8da1;font-weight:700;font-size:1.1em}' +
        '.oj-tor-name{opacity:.85;font-size:.95em;margin-top:.2em}' +
        '.oj-tor-meta{opacity:.6;font-size:.9em;margin-top:.2em}' +
        '.oj-empty{opacity:.6;padding:1em;font-style:italic}' +
        '.oj-spin{width:100%;height:100%;background:linear-gradient(90deg,rgba(255,255,255,.05),rgba(255,255,255,.15),rgba(255,255,255,.05));background-size:200% 100%;animation:ojsh 1.2s infinite}' +
        '@keyframes ojsh{to{background-position:-200% 0}}';

    function startPlugin() {
        var st = document.createElement('style');
        st.textContent = STYLE;
        document.head.appendChild(st);

        Lampa.Component.add('onejav_grid', gridScreen);
        Lampa.Component.add('onejav_tags', tagsScreen);
        Lampa.Component.add('onejav_torrents', torrentsScreen);

        // Nút trong menu chính
        function addButton() {
            if (document.querySelector('.menu__item[data-action="onejav"]')) return;
            var lists = document.querySelectorAll('.menu .menu__list');
            if (!lists.length) return;
            var li = document.createElement('li');
            li.className = 'menu__item selector';
            li.setAttribute('data-action', 'onejav');
            li.innerHTML = '<div class="menu__ico">🎌</div><div class="menu__text">OneJAV</div>';
            $(li).on('hover:enter click', openHome);
            lists[0].appendChild(li);
        }
        if (window.appready) addButton();
        else Lampa.Listener.follow('app', function (e) { if (e.type === 'ready') addButton(); });

        console.log('[OneJAV] ready', API);
    }

    if (window.appready) startPlugin();
    else Lampa.Listener.follow('app', function (e) { if (e.type === 'ready') startPlugin(); });
})();
