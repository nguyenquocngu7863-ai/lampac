/* Lampac Admin Panel as a native Lampa plugin.
 * Password is entered once, stored on this device, and reused. The UI stays
 * inside Lampa (no /adminpanel WebView navigation).
 */
(function () {
  'use strict';

  if (window.lampac_adminpanel_plugin) return;
  window.lampac_adminpanel_plugin = true;

  var PASS_KEY = 'lampac_admin_passwd';
  var COMPONENT = 'lampac_admin';

  function serverOrigin() {
    var scripts = document.getElementsByTagName('script');
    for (var i = scripts.length - 1; i >= 0; i--) {
      var src = scripts[i].src || '';
      var match = /^(https?:\/\/[^/]+)\/adminpanel\.js(?:[?#].*)?$/i.exec(src);
      if (match) return match[1];
    }
    return window.location.protocol + '//' + window.location.host;
  }

  var ORIGIN = serverOrigin();

  function injectCss() {
    if (document.getElementById('lampac-admin-css')) return;
    var css = document.createElement('style');
    css.id = 'lampac-admin-css';
    css.textContent =
      '.lampac-admin{min-height:100%;--acc:#0d9499;--acc-dk:#0b7d82;--ink:#16202b;--muted:#5c6672;--card:#ffffff;--line:#d9dee7;--bg:#eef2f7' +
        ';background:var(--bg);color:var(--ink)}' +
      // Ép TẤT CẢ chữ trong khung admin sang màu tối — theme tối của Lampa có thể
      // đặt color trắng lên các phần tử con, gây chữ trắng trên nền trắng.
      '.lampac-admin,.lampac-admin *{color:var(--ink)!important;-webkit-tap-highlight-color:rgba(13,148,153,.2)}' +
      '.lampac-admin input,.lampac-admin textarea,.lampac-admin select{background:#fff!important;color:var(--ink)!important}' +
      '.lampac-admin .selector.focus,.lampac-admin .selector:hover{background:#f3fbfc!important}' +
      '.lampac-admin-view{overflow-y:auto;-webkit-overflow-scrolling:touch;overscroll-behavior:contain;padding:.5em .7em 16vh;box-sizing:border-box}' +
      '.lampac-admin__hero{position:relative;margin:.3em .5em .95em;padding:1.2em 1.4em;border-radius:18px;overflow:hidden;' +
        'background:linear-gradient(135deg,#e3f6f7,#d6ecfb);border:1px solid #bfe3e6;' +
        'box-shadow:0 4px 14px rgba(13,120,130,.12)}' +
      '.lampac-admin__head{position:relative;font-size:2em;font-weight:800;line-height:1.2;letter-spacing:.01em;display:flex;align-items:center;gap:.45em;color:#08303a}' +
      '.lampac-admin__head:before{content:"";width:.34em;height:1.05em;border-radius:.2em;background:linear-gradient(180deg,var(--acc),#38bdf8);flex:0 0 auto}' +
      '.lampac-admin__sub{position:relative;margin-top:.45em;font-size:1.15em;line-height:1.45;color:#33525c}' +
      '.lampac-admin .settings-param{display:flex;flex-wrap:wrap;align-items:center;gap:.2em .9em;margin:.55em .3em;padding:1.1em 1.3em;border-radius:16px;' +
        'background:var(--card);border:1px solid var(--line);' +
        'box-shadow:0 2px 6px rgba(20,40,60,.06);' +
        'transition:background .12s,border-color .12s,box-shadow .12s}' +
      '.lampac-admin .settings-param__name{font-size:1.4em;line-height:1.3;font-weight:600;flex:1 1 auto;min-width:55%;color:var(--ink)}' +
      '.lampac-admin .settings-param__value{flex:0 0 auto;margin-left:auto;max-width:48%;text-align:right;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;' +
        'font-size:1.05em;font-weight:700;color:var(--acc);background:rgba(13,148,153,.10);' +
        'border:1px solid rgba(13,148,153,.28);padding:.28em .8em;border-radius:999px}' +
      '.lampac-admin .settings-param__descr{flex:1 1 100%;margin-top:.32em;font-size:1.05em;line-height:1.4;color:var(--muted)}' +
      '.lampac-admin .settings-param.focus,.lampac-admin .settings-param:hover{' +
        'background:#f3fbfc;border-color:var(--acc);' +
        'box-shadow:0 0 0 2px rgba(13,148,153,.25),0 4px 12px rgba(13,120,130,.12)}' +
      '.lampac-admin .settings-param.focus .settings-param__value,.lampac-admin .settings-param:hover .settings-param__value{color:#fff!important;background:var(--acc);border-color:var(--acc)}' +
      '.lampac-admin .simple-button,.lampac-admin .settings-param.focus .settings-param__value{color:#fff!important}' +
      '.lampac-admin-empty{margin:1em .6em;padding:2.2em 1.5em;text-align:center;font-size:1.25em;border-radius:16px;' +
        'border:1px dashed #c4ccd8;background:#fff;color:var(--muted)}' +
      '.lampac-admin-edit__ta{width:100%;min-height:42vh;background:#fff;color:var(--ink);border:1px solid var(--line);border-radius:14px;padding:1em;' +
        'font-family:ui-monospace,Consolas,monospace;font-size:1.3em;line-height:1.5;box-sizing:border-box}' +
      '.lampac-admin-edit__ta:focus{outline:none;border-color:var(--acc);box-shadow:0 0 0 2px rgba(13,148,153,.25)}' +
      '.lampac-admin-edit__actions{display:flex;gap:.8em;margin-top:1em}' +
      '.lampac-admin-edit__actions .simple-button{font-size:1.3em;padding:.85em 1.7em;border-radius:999px;font-weight:700;' +
        'background:var(--acc);border:1px solid var(--acc);color:#fff}' +
      '.lampac-admin-edit__actions .simple-button:hover{background:var(--acc-dk)}';
    document.head.appendChild(css);
  }

  function esc(s) {
    return String(s == null ? '' : s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function compact(s, n) {
    s = String(s == null ? '' : s);
    n = n || 80;
    return s.length > n ? s.slice(0, n - 1) + '…' : s;
  }

  function isTv() {
    try {
      return !!(Lampa.Platform && Lampa.Platform.screen && Lampa.Platform.screen('tv'));
    } catch (e) {
      return false;
    }
  }

  function storedPass() {
    try {
      return String(Lampa.Storage.get(PASS_KEY, '') || '');
    } catch (e) {
      return '';
    }
  }

  function savePass(password) {
    try { Lampa.Storage.set(PASS_KEY, password || ''); } catch (e) { }
  }

  function clearPass() {
    try { Lampa.Storage.set(PASS_KEY, ''); } catch (e) { }
  }

  function api(path, opts) {
    opts = opts || {};
    var headers = opts.headers || {};
    if (opts.body && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
    return fetch(ORIGIN + path, {
      method: opts.method || 'GET',
      headers: headers,
      body: opts.body || undefined,
      credentials: 'same-origin'
    }).then(function (res) {
      return res.text().then(function (text) {
        var json = null;
        if (text) {
          try { json = JSON.parse(text); } catch (e) { json = null; }
        }
        return { ok: res.ok, status: res.status, text: text, json: json };
      });
    });
  }

  function login(password) {
    return api('/adminpanel/api/login', {
      method: 'POST',
      body: JSON.stringify({ password: password, remember: true })
    }).then(function (res) {
      if (res.ok) {
        savePass(password);
        return true;
      }
      return false;
    });
  }

  function sessionOk() {
    return api('/adminpanel/api/session').then(function (res) { return res.ok && res.json && res.json.ok; });
  }

  function ensureAuth() {
    return sessionOk().then(function (ok) {
      if (ok) return true;
      var pass = storedPass();
      if (!pass) return false;
      return login(pass);
    });
  }

  function pretty(value) {
    if (value === undefined) return '';
    try { return JSON.stringify(value, null, 2); } catch (e) { return String(value); }
  }

  function parseJsonOrThrow(raw, emptyValue) {
    var t = String(raw == null ? '' : raw).trim();
    if (t === '') return emptyValue;
    return JSON.parse(t);
  }

  function setDeep(root, path, val) {
    var parts = String(path).split('.');
    var o = root;
    for (var i = 0; i < parts.length - 1; i++) {
      var p = parts[i];
      if (!o[p] || typeof o[p] !== 'object' || Array.isArray(o[p])) o[p] = {};
      o = o[p];
    }
    o[parts[parts.length - 1]] = val;
  }

  function flattenFields(value, prefix, out) {
    out = out || [];
    if (value === null || typeof value !== 'object' || Array.isArray(value)) {
      out.push({ path: prefix, value: value, kind: Array.isArray(value) ? 'json' : (typeof value === 'boolean' ? 'bool' : (typeof value === 'number' ? 'num' : 'str')) });
      return out;
    }
    var keys = Object.keys(value).sort();
    if (!keys.length) {
      out.push({ path: prefix, value: value, kind: 'json' });
      return out;
    }
    for (var i = 0; i < keys.length; i++) {
      var k = keys[i];
      var next = prefix ? prefix + '.' + k : k;
      flattenFields(value[k], next, out);
    }
    return out;
  }

  function editText(title, value, cb) {
    value = value == null ? '' : String(value);
    if (isTv() && Lampa.Input && Lampa.Input.edit) {
      Lampa.Input.edit({
        free: true,
        title: title,
        nosave: true,
        nomic: true,
        value: value
      }, function (next) {
        if (next === null || next === undefined) return;
        cb(String(next));
      });
      return;
    }

    var box = $('<div class="lampac-admin-edit"></div>');
    var ta = $('<textarea class="lampac-admin-edit__ta selector" spellcheck="false"></textarea>');
    ta.val(value);
    var actions = $('<div class="lampac-admin-edit__actions"></div>');
    var save = $('<div class="simple-button selector">Lưu</div>');
    var cancel = $('<div class="simple-button selector">Hủy</div>');
    actions.append(save).append(cancel);
    box.append(ta).append(actions);

    Lampa.Modal.open({
      title: title,
      html: box,
      size: 'large',
      onBack: function () {
        Lampa.Modal.close();
        Lampa.Controller.toggle('content');
      }
    });

    save.on('hover:enter click', function () {
      var next = ta.val();
      Lampa.Modal.close();
      Lampa.Controller.toggle('content');
      cb(next);
    });
    cancel.on('hover:enter click', function () {
      Lampa.Modal.close();
      Lampa.Controller.toggle('content');
    });
  }

  function confirmAct(title, okTitle, cb) {
    Lampa.Select.show({
      title: title,
      items: [
        { title: 'Hủy' },
        { title: okTitle || 'Xác nhận', confirm: true }
      ],
      onSelect: function (item) {
        if (item.confirm) cb();
        else Lampa.Controller.toggle('content');
      },
      onBack: function () {
        Lampa.Controller.toggle('content');
      }
    });
  }

  function noty(text, error) {
    if (Lampa.Noty && Lampa.Noty.show) Lampa.Noty.show(text);
    if (error && window.console) console.warn('Admin Panel', text);
  }

  function copyText(text, label) {
    function done() { noty((label || 'Đã sao chép') + ''); }
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(done).catch(function () {
        noty(compact(text, 180));
      });
      return;
    }
    noty(compact(text, 180));
  }

  function component() {
    var self = this;
    try { console.log('[adminpanel] build light-v8'); } catch (e) { }
    // Native scrolling container instead of Lampa.Scroll (which moves a mask via
    // CSS transform, so browser scrollIntoView/scrollTop can never scroll it).
    // A real overflow:auto element scrolls reliably with remote, wheel & touch.
    var view = $('<div class="lampac-admin-view"></div>');
    var html = $('<div class="lampac-admin"></div>').append(view);

    function sizeView() {
      try {
        var top = view[0].getBoundingClientRect().top || 0;
        var h = Math.max(220, (window.innerHeight || document.documentElement.clientHeight) - top - 16);
        view.css('height', h + 'px');
      } catch (e) { }
    }

    var scroll = {
      render: function () { return view; },
      append: function (n) { view.append(n); },
      clear: function () { view.empty(); },
      reset: function () { try { view[0].scrollTop = 0; } catch (e) { } },
      update: function (el) {
        try {
          var node = el && el[0] ? el[0] : el;
          if (node && node.scrollIntoView) node.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        } catch (e) { }
      },
      wheel: function (dir) { try { view[0].scrollTop += (dir > 0 ? 1 : -1) * Math.round(view[0].clientHeight * 0.7); } catch (e) { } },
      destroy: function () { view.remove(); }
    };
    var stack = [];
    var cache = {
      groups: [],
      catalog: [],
      initObj: {},
      currentObj: {},
      initText: '{}',
      currentText: '{}',
      users: []
    };
    var lastFocus = false;

    function head(title, sub) {
      var wrap = $('<div class="lampac-admin__hero"></div>');
      wrap.append('<div class="lampac-admin__head">' + esc(title) + '</div>');
      if (sub) wrap.append('<div class="lampac-admin__sub">' + esc(sub) + '</div>');
      return wrap;
    }

    // Scroll the focused row into the native overflow:auto container.
    function ensureVisible(el) {
      try { if (el && el.scrollIntoView) el.scrollIntoView({ block: 'center', behavior: 'smooth' }); } catch (e) { }
      try { if (el && el.scrollIntoViewIfNeeded) el.scrollIntoViewIfNeeded(); } catch (e2) { }
    }

    function item(name, value, descr, onEnter) {
      var el = $('<div class="settings-param selector"></div>');
      el.append('<div class="settings-param__name">' + esc(name) + '</div>');
      if (value != null && value !== '')
        el.append('<div class="settings-param__value">' + esc(value) + '</div>');
      if (descr)
        el.append('<div class="settings-param__descr">' + esc(descr) + '</div>');
      // Focus (D-pad navigation / hover) must scroll the focused row into view.
      el.on('hover:focus', function () {
        lastFocus = el[0];
        ensureVisible(el[0]);
      });
      el.on('hover:enter', function () {
        lastFocus = el[0];
        onEnter();
      });
      return el;
    }

    function draw(nodes) {
      scroll.clear();
      lastFocus = false;
      for (var i = 0; i < nodes.length; i++) scroll.append(nodes[i]);
      try { scroll.reset(); } catch (e) { }
      Lampa.Controller.toggle('content');
    }

    function pushView(name, data) {
      stack.push({ name: name, data: data || {} });
      render();
    }

    function replaceView(name, data) {
      if (stack.length) stack[stack.length - 1] = { name: name, data: data || {} };
      else stack.push({ name: name, data: data || {} });
      render();
    }

    function popView() {
      if (stack.length <= 1) {
        Lampa.Activity.backward();
        return;
      }
      stack.pop();
      render();
    }

    function currentView() {
      return stack[stack.length - 1] || { name: 'home', data: {} };
    }

    function loadAll() {
      return Promise.all([
        api('/adminpanel/api/groups'),
        api('/adminpanel/api/groups/catalog'),
        api('/adminpanel/api/init'),
        api('/adminpanel/api/current'),
        api('/adminpanel/api/users-json')
      ]).then(function (parts) {
        for (var i = 0; i < parts.length; i++) {
          if (!parts[i].ok) throw new Error(parts[i].status === 401 ? 'auth' : ('load ' + parts[i].status));
        }
        cache.groups = parts[0].json || [];
        cache.catalog = parts[1].json || [];
        cache.initText = parts[2].text || '{}';
        cache.currentText = parts[3].text || '{}';
        try { cache.initObj = JSON.parse(cache.initText); } catch (e) { cache.initObj = {}; }
        try { cache.currentObj = JSON.parse(cache.currentText); } catch (e) { cache.currentObj = {}; }
        var users = parts[4].json;
        cache.users = Array.isArray(users) ? users : [];
      });
    }

    function boot() {
      self.activity.loader(true);
      ensureAuth().then(function (ok) {
        if (!ok) {
          self.activity.loader(false);
          stack = [{ name: 'login', data: {} }];
          render();
          self.activity.toggle();
          return;
        }
        return loadAll().then(function () {
          self.activity.loader(false);
          stack = [{ name: 'home', data: {} }];
          render();
          self.activity.toggle();
        });
      }).catch(function () {
        self.activity.loader(false);
        stack = [{ name: 'login', data: {} }];
        render();
        self.activity.toggle();
      });
    }

    function askPassword() {
      editText('Mật khẩu root (passwd)', '', function (value) {
        var password = String(value || '').replace(/[\n\r\t ]/g, '');
        if (!password) {
          noty('Cần mật khẩu');
          return;
        }
        self.activity.loader(true);
        login(password).then(function (ok) {
          if (!ok) {
            self.activity.loader(false);
            noty('Sai mật khẩu');
            return;
          }
          loadAll().then(function () {
            self.activity.loader(false);
            stack = [{ name: 'home', data: {} }];
            render();
          }).catch(function (e) {
            self.activity.loader(false);
            noty(e.message || 'Không tải được cấu hình');
          });
        });
      });
    }

    function saveSection(key, value) {
      return api('/adminpanel/api/init/section/' + encodeURIComponent(key), {
        method: 'POST',
        body: JSON.stringify(value)
      }).then(function (res) {
        if (!res.ok) {
          var err = (res.json && (res.json.error + (res.json.detail ? ': ' + res.json.detail : ''))) || ('Lỗi ' + res.status);
          throw new Error(err);
        }
        return loadAll();
      });
    }

    function saveInitRaw(raw) {
      var parsed = parseJsonOrThrow(raw, {});
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed))
        throw new Error('Gốc init.conf phải là object JSON');
      return api('/adminpanel/api/init', {
        method: 'POST',
        body: JSON.stringify(parsed)
      }).then(function (res) {
        if (!res.ok) {
          var err = (res.json && (res.json.error + (res.json.detail ? ': ' + res.json.detail : ''))) || ('Lỗi ' + res.status);
          throw new Error(err);
        }
        return loadAll();
      });
    }

    function saveUsers(list) {
      var cleaned = (list || []).filter(function (u) {
        return u && String(u.id || '').trim();
      }).map(function (u) {
        var row = {
          id: String(u.id).trim(),
          ids: Array.isArray(u.ids) ? u.ids : [],
          IsPasswd: !!u.IsPasswd,
          expires: u.expires || '2099-12-31T23:59:59',
          group: typeof u.group === 'number' ? u.group : parseInt(u.group, 10) || 0,
          ban: !!u.ban,
          ban_msg: u.ban_msg && String(u.ban_msg).trim() ? String(u.ban_msg).trim() : null
        };
        if (u.comment && String(u.comment).trim()) row.comment = String(u.comment).trim();
        if (u.params && typeof u.params === 'object' && !Array.isArray(u.params) && Object.keys(u.params).length)
          row.params = u.params;
        return row;
      });
      return api('/adminpanel/api/users-json', {
        method: 'POST',
        body: JSON.stringify(cleaned)
      }).then(function (res) {
        if (!res.ok) {
          var err = (res.json && (res.json.error + (res.json.detail ? ': ' + res.json.detail : ''))) || ('Lỗi ' + res.status);
          throw new Error(err);
        }
        return loadAll();
      });
    }

    function filterKeys(keys, query) {
      query = String(query || '').trim().toLowerCase();
      if (!query) return keys || [];
      return (keys || []).filter(function (k) { return String(k).toLowerCase().indexOf(query) !== -1; });
    }

    // Gather every config key together with the group title it belongs to.
    function allKeyIndex() {
      var idx = [];
      var seen = {};
      function add(key, groupTitle) {
        if (key && !seen[key]) { seen[key] = 1; idx.push({ key: key, group: groupTitle || '' }); }
      }
      (cache.groups || []).forEach(function (g) {
        (g.keys || []).forEach(function (k) { add(k, g.title || g.id); });
      });
      return idx;
    }

    function viewSearch(data) {
      var q = (data.query || '').trim().toLowerCase();
      var nodes = [
        head('Tìm kiếm cấu hình', 'Gõ tên khóa (vd: PidTor, torrs, proxy, streamproxy). Kết quả mở thẳng trình sửa JSON.'),
        item('Từ khóa: ' + (q || '(chưa nhập)'), '', 'Chọn để gõ/đổi từ khóa', function () {
          editText('Tìm khóa cấu hình', data.query || '', function (v) {
            data.query = v || '';
            render();
          });
        })
      ];

      if (!q) {
        nodes.push($('<div class="lampac-admin-empty">Nhập từ khóa để lọc. Có ' + allKeyIndex().length + ' khóa trong danh mục.</div>'));
        draw(nodes);
        return;
      }

      var matches = allKeyIndex().filter(function (e) { return e.key.toLowerCase().indexOf(q) !== -1; });
      matches.slice(0, 200).forEach(function (e) {
        nodes.push(item(e.key, e.group, '', function () {
          // Open the same editor used inside a group so save/load is identical.
          pushView('key-json', { key: e.key, backView: 'search', backData: data });
        }));
      });

      if (!matches.length)
        nodes.push($('<div class="lampac-admin-empty">Không có khóa nào khớp «' + esc(data.query || '') + '»</div>'));
      else if (matches.length > 200)
        nodes.push($('<div class="lampac-admin-empty">…và ' + (matches.length - 200) + ' kết quả khác, gõ cụ thể hơn.</div>'));

      draw(nodes);
    }

    function viewLogin() {
      draw([
        head('Admin Panel', 'Nhập mật khẩu root (file passwd). Máy này sẽ nhớ, không hỏi lại.'),
        item('Nhập mật khẩu', '', 'Ghi nhớ trên thiết bị này', askPassword)
      ]);
    }

    function viewHome() {
      draw([
        head('Admin Panel', 'Sửa init.conf / users.json trong Lampa. current.conf chỉ xem.'),
        item('🔍 Tìm kiếm cấu hình', '', 'Tra mọi khóa trên tất cả nhóm theo tên', function () {
          pushView('search', { query: '' });
        }),
        item('Nhóm (JSON)', String(cache.groups.length), 'Khóa theo nhóm, lưu từng mục', function () {
          pushView('groups', {});
        }),
        item('Trình chỉnh sửa', String(cache.catalog.length), 'Bật/tắt và sửa trường đơn giản', function () {
          pushView('simple-groups', {});
        }),
        item('init.conf', Object.keys(cache.initObj || {}).length + ' khóa', 'Sửa toàn bộ tệp', function () {
          pushView('init-raw', {});
        }),
        item('current.conf', 'chỉ xem', 'Bản ghép runtime, không ghi đè', function () {
          pushView('current-raw', {});
        }),
        item('users.json', String((cache.users || []).length), 'Tài khoản accsdb', function () {
          pushView('users', {});
        }),
        item('Làm mới', '', 'Tải lại từ máy chủ', function () {
          self.activity.loader(true);
          loadAll().then(function () {
            self.activity.loader(false);
            noty('Đã làm mới');
            render();
          }).catch(function (e) {
            self.activity.loader(false);
            noty(e.message || 'Lỗi tải');
          });
        }),
        item('Đăng xuất', '', 'Xóa mật khẩu đã nhớ trên máy này', function () {
          function afterLogout() {
            clearPass();
            stack = [{ name: 'login', data: {} }];
            render();
          }
          api('/adminpanel/api/logout', { method: 'POST' }).then(afterLogout, afterLogout);
        })
      ]);
    }

    function viewGroups() {
      var nodes = [head('Nhóm (JSON)', 'Chọn nhóm, rồi sửa khóa. Lưu vào init.conf.')];
      (cache.groups || []).forEach(function (g) {
        var n = (g.keys && g.keys.length) || 0;
        nodes.push(item(g.title || g.id, n + '', g.hint || '', function () {
          pushView('group-keys', { group: g, query: '' });
        }));
      });
      if (nodes.length === 1) nodes.push($('<div class="lampac-admin-empty">Không có nhóm</div>'));
      draw(nodes);
    }

    function viewGroupKeys(data) {
      var g = data.group || {};
      var keys = filterKeys(g.keys || [], data.query);
      var nodes = [
        head(g.title || g.id, g.hint || 'Chọn khóa để sửa JSON'),
        item('Tìm khóa', data.query || '', 'Lọc theo tên khóa', function () {
          editText('Tìm khóa', data.query || '', function (q) {
            data.query = q;
            render();
          });
        })
      ];
      keys.forEach(function (key) {
        var inInit = cache.initObj && Object.prototype.hasOwnProperty.call(cache.initObj, key);
        nodes.push(item(key, inInit ? 'trong init' : 'chỉ current', '', function () {
          pushView('key-json', { key: key });
        }));
      });
      if (!keys.length) nodes.push($('<div class="lampac-admin-empty">Không khớp khóa</div>'));
      draw(nodes);
    }

    function viewKeyJson(data) {
      var key = data.key;
      var initVal = cache.initObj ? cache.initObj[key] : undefined;
      var curVal = cache.currentObj ? cache.currentObj[key] : undefined;
      var nodes = [
        head(key, (initVal === undefined ? 'Chưa có trong init.conf. ' : '') + 'current dùng để tham chiếu.'),
        item('Sửa JSON (init)', compact(pretty(initVal), 40), 'Giá trị sẽ ghi vào init.conf', function () {
          editText(key, pretty(initVal === undefined ? (curVal === undefined ? {} : curVal) : initVal), function (raw) {
            var parsed;
            try { parsed = parseJsonOrThrow(raw, {}); } catch (e) { noty('JSON: ' + e.message); return; }
            self.activity.loader(true);
            saveSection(key, parsed).then(function () {
              self.activity.loader(false);
              noty('Đã lưu «' + key + '»');
              render();
            }).catch(function (e) {
              self.activity.loader(false);
              noty(e.message || 'Lỗi lưu');
            });
          });
        }),
        item('current → init', '', 'Sao chép giá trị runtime rồi lưu', function () {
          if (curVal === undefined) { noty('Không có trong current'); return; }
          confirmAct('Ghi «' + key + '» từ current vào init.conf?', 'Lưu', function () {
            self.activity.loader(true);
            saveSection(key, curVal).then(function () {
              self.activity.loader(false);
              noty('Đã copy current → init');
              render();
            }).catch(function (e) {
              self.activity.loader(false);
              noty(e.message || 'Lỗi lưu');
            });
          });
        }),
        item('Xem current', compact(pretty(curVal), 40), 'Chỉ đọc', function () {
          editText(key + ' (current)', pretty(curVal), function () { });
        })
      ];
      draw(nodes);
    }

    function viewSimpleGroups() {
      var nodes = [head('Trình chỉnh sửa', 'Form theo current.conf. Bool bấm để đảo, còn lại nhập giá trị.')];
      (cache.catalog || []).forEach(function (g) {
        var n = (g.keys && g.keys.length) || 0;
        nodes.push(item(g.title || g.id, n + '', g.hint || '', function () {
          pushView('simple-keys', { group: g, query: '' });
        }));
      });
      draw(nodes);
    }

    function viewSimpleKeys(data) {
      var g = data.group || {};
      var keys = filterKeys(g.keys || [], data.query);
      var nodes = [
        head(g.title || g.id, 'Chọn khóa để sửa từng trường'),
        item('Tìm khóa', data.query || '', '', function () {
          editText('Tìm khóa', data.query || '', function (q) {
            data.query = q;
            render();
          });
        })
      ];
      keys.forEach(function (key) {
        var inInit = cache.initObj && Object.prototype.hasOwnProperty.call(cache.initObj, key);
        nodes.push(item(key, inInit ? 'trong init' : 'chỉ current', '', function () {
          pushView('simple-key', { key: key });
        }));
      });
      draw(nodes);
    }

    function viewSimpleKey(data) {
      var key = data.key;
      var initVal = cache.initObj ? cache.initObj[key] : undefined;
      var curVal = cache.currentObj ? cache.currentObj[key] : undefined;
      var base = initVal !== undefined ? initVal : curVal;
      var working = base === undefined ? {} : JSON.parse(JSON.stringify(base));
      var fields = flattenFields(working, '');
      var nodes = [head(key, 'Bấm trường để sửa. «Lưu khóa» ghi cả object vào init.conf.')];

      function refreshWorkingFromCache() {
        var nextInit = cache.initObj ? cache.initObj[key] : undefined;
        var nextCur = cache.currentObj ? cache.currentObj[key] : undefined;
        var nextBase = nextInit !== undefined ? nextInit : nextCur;
        working = nextBase === undefined ? {} : JSON.parse(JSON.stringify(nextBase));
      }

      fields.forEach(function (f) {
        var label = f.path || '(gốc)';
        var shown;
        if (f.kind === 'bool') shown = f.value ? 'Bật' : 'Tắt';
        else if (f.kind === 'json') shown = compact(pretty(f.value), 36);
        else shown = f.value == null ? 'null' : String(f.value);

        nodes.push(item(label, shown, '', function () {
          if (f.kind === 'bool') {
            var next = !f.value;
            if (f.path) setDeep(working, f.path, next);
            else working = next;
            self.activity.loader(true);
            saveSection(key, working).then(function () {
              self.activity.loader(false);
              noty(label + ': ' + (next ? 'Bật' : 'Tắt'));
              render();
            }).catch(function (e) {
              self.activity.loader(false);
              noty(e.message || 'Lỗi lưu');
            });
            return;
          }

          var seed = f.kind === 'json' ? pretty(f.value) : (f.value == null ? '' : String(f.value));
          editText(label, seed, function (raw) {
            var parsed;
            try {
              if (f.kind === 'json') parsed = parseJsonOrThrow(raw, null);
              else if (f.kind === 'num') {
                var t = String(raw).trim();
                if (t === '') parsed = null;
                else {
                  parsed = Number(t);
                  if (isNaN(parsed)) throw new Error('Không phải số');
                }
              } else parsed = raw;
            } catch (e) {
              noty(e.message || 'Giá trị không hợp lệ');
              return;
            }
            refreshWorkingFromCache();
            if (f.path) setDeep(working, f.path, parsed);
            else working = parsed;
            self.activity.loader(true);
            saveSection(key, working).then(function () {
              self.activity.loader(false);
              noty('Đã lưu «' + label + '»');
              render();
            }).catch(function (err) {
              self.activity.loader(false);
              noty(err.message || 'Lỗi lưu');
            });
          });
        }));
      });

      nodes.push(item('Lưu khóa (JSON đầy đủ)', '', 'Mở editor JSON của cả khóa', function () {
        editText(key, pretty(working), function (raw) {
          var parsed;
          try { parsed = parseJsonOrThrow(raw, {}); } catch (e) { noty('JSON: ' + e.message); return; }
          self.activity.loader(true);
          saveSection(key, parsed).then(function () {
            self.activity.loader(false);
            noty('Đã lưu «' + key + '»');
            render();
          }).catch(function (e) {
            self.activity.loader(false);
            noty(e.message || 'Lỗi lưu');
          });
        });
      }));

      draw(nodes);
    }

    function viewInitRaw() {
      draw([
        head('init.conf', 'Ghi đè toàn bộ tệp. Hãy định dạng JSON trước khi lưu.'),
        item('Sửa tệp', compact(cache.initText, 40), Object.keys(cache.initObj || {}).length + ' khóa gốc', function () {
          editText('init.conf', cache.initText, function (raw) {
            confirmAct('Ghi đè toàn bộ init.conf?', 'Lưu', function () {
              self.activity.loader(true);
              try {
                saveInitRaw(raw).then(function () {
                  self.activity.loader(false);
                  noty('init.conf đã lưu');
                  render();
                }).catch(function (e) {
                  self.activity.loader(false);
                  noty(e.message || 'Lỗi lưu');
                });
              } catch (e) {
                self.activity.loader(false);
                noty(e.message || 'JSON lỗi');
              }
            });
          });
        }),
        item('Định dạng', '', 'Pretty-print JSON đang có', function () {
          try {
            cache.initText = JSON.stringify(JSON.parse(cache.initText), null, 2);
            noty('Đã định dạng');
            render();
          } catch (e) {
            noty('JSON: ' + e.message);
          }
        }),
        item('Sao chép', '', '', function () { copyText(cache.initText, 'Đã sao chép init.conf'); })
      ]);
    }

    function viewCurrentRaw() {
      draw([
        head('current.conf', 'Chỉ xem — không lưu được từ admin.'),
        item('Xem tệp', compact(cache.currentText, 40), '', function () {
          editText('current.conf (chỉ xem)', cache.currentText, function () { });
        }),
        item('Sao chép', '', '', function () { copyText(cache.currentText, 'Đã sao chép current.conf'); })
      ]);
    }

    function emptyUser() {
      return {
        id: '',
        ids: [],
        IsPasswd: false,
        expires: '2099-12-31T23:59:59',
        group: 0,
        ban: false,
        ban_msg: '',
        comment: '',
        params: {}
      };
    }

    function viewUsers() {
      var nodes = [
        head('users.json', 'Tài khoản accsdb. Lưu cả danh sách sau khi sửa.'),
        item('+ Người dùng', '', '', function () {
          cache.users.push(emptyUser());
          pushView('user-edit', { index: cache.users.length - 1 });
        }),
        item('Sửa JSON đầy đủ', String(cache.users.length), '', function () {
          editText('users.json', pretty(cache.users), function (raw) {
            var parsed;
            try { parsed = parseJsonOrThrow(raw, []); } catch (e) { noty('JSON: ' + e.message); return; }
            if (!Array.isArray(parsed)) { noty('Gốc phải là mảng'); return; }
            confirmAct('Ghi đè users.json?', 'Lưu', function () {
              self.activity.loader(true);
              saveUsers(parsed).then(function () {
                self.activity.loader(false);
                noty('users.json đã lưu');
                render();
              }).catch(function (e) {
                self.activity.loader(false);
                noty(e.message || 'Lỗi lưu');
              });
            });
          });
        })
      ];
      cache.users.forEach(function (u, index) {
        var title = (u && u.id) ? String(u.id) : '(không có id)';
        var meta = 'group ' + ((u && u.group) || 0) + (u && u.ban ? ' · ban' : '');
        nodes.push(item(title, meta, '', function () {
          pushView('user-edit', { index: index });
        }));
      });
      if (!cache.users.length) nodes.push($('<div class="lampac-admin-empty">Chưa có người dùng</div>'));
      draw(nodes);
    }

    function viewUserEdit(data) {
      var index = data.index;
      if (index < 0 || index >= cache.users.length) {
        popView();
        return;
      }
      var u = cache.users[index] || emptyUser();
      function field(name, value, descr, writer) {
        return item(name, value == null || value === '' ? '—' : String(value), descr, function () {
          writer();
        });
      }
      function persistUsers() {
        self.activity.loader(true);
        saveUsers(cache.users).then(function () {
          self.activity.loader(false);
          noty('Đã lưu users.json');
          render();
        }).catch(function (e) {
          self.activity.loader(false);
          noty(e.message || 'Lỗi lưu');
        });
      }
      draw([
        head(u.id || 'Người dùng mới', 'Sửa xong bấm Lưu danh sách'),
        field('id', u.id, 'UID / tên đăng nhập', function () {
          editText('id', u.id || '', function (v) { u.id = String(v || '').trim(); cache.users[index] = u; render(); });
        }),
        field('ids', (u.ids || []).join(', '), 'UID phụ, cách nhau bằng dấu phẩy', function () {
          editText('ids', (u.ids || []).join('\n'), function (v) {
            u.ids = String(v || '').split(/[\n,;]+/).map(function (s) { return s.trim(); }).filter(Boolean);
            cache.users[index] = u;
            render();
          });
        }),
        field('expires', u.expires, 'ISO 8601', function () {
          editText('expires', u.expires || '', function (v) { u.expires = String(v || '').trim() || '2099-12-31T23:59:59'; cache.users[index] = u; render(); });
        }),
        field('group', u.group, '', function () {
          editText('group', String(u.group == null ? 0 : u.group), function (v) {
            var n = parseInt(v, 10);
            u.group = isNaN(n) ? 0 : n;
            cache.users[index] = u;
            render();
          });
        }),
        item('ban', u.ban ? 'Bật' : 'Tắt', 'Khóa tài khoản', function () {
          u.ban = !u.ban;
          cache.users[index] = u;
          render();
        }),
        field('ban_msg', u.ban_msg, '', function () {
          editText('ban_msg', u.ban_msg || '', function (v) { u.ban_msg = String(v || ''); cache.users[index] = u; render(); });
        }),
        field('comment', u.comment, '', function () {
          editText('comment', u.comment || '', function (v) { u.comment = String(v || ''); cache.users[index] = u; render(); });
        }),
        item('IsPasswd', u.IsPasswd ? 'Bật' : 'Tắt', 'Đăng nhập bằng mật khẩu', function () {
          u.IsPasswd = !u.IsPasswd;
          cache.users[index] = u;
          render();
        }),
        field('params', pretty(u.params || {}), 'object JSON', function () {
          editText('params', pretty(u.params || {}), function (raw) {
            try {
              var parsed = parseJsonOrThrow(raw, {});
              if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('phải là object');
              u.params = parsed;
              cache.users[index] = u;
              render();
            } catch (e) {
              noty('params: ' + e.message);
            }
          });
        }),
        item('Lưu danh sách', '', 'Ghi users.json', persistUsers),
        item('Xóa người dùng', '', '', function () {
          confirmAct('Xóa «' + (u.id || index) + '»?', 'Xóa', function () {
            cache.users.splice(index, 1);
            self.activity.loader(true);
            saveUsers(cache.users).then(function () {
              self.activity.loader(false);
              noty('Đã xóa');
              popView();
            }).catch(function (e) {
              self.activity.loader(false);
              noty(e.message || 'Lỗi lưu');
            });
          });
        })
      ]);
    }

    function render() {
      var view = currentView();
      switch (view.name) {
        case 'login': return viewLogin();
        case 'home': return viewHome();
        case 'search': return viewSearch(view.data);
        case 'groups': return viewGroups();
        case 'group-keys': return viewGroupKeys(view.data);
        case 'key-json': return viewKeyJson(view.data);
        case 'simple-groups': return viewSimpleGroups();
        case 'simple-keys': return viewSimpleKeys(view.data);
        case 'simple-key': return viewSimpleKey(view.data);
        case 'init-raw': return viewInitRaw();
        case 'current-raw': return viewCurrentRaw();
        case 'users': return viewUsers();
        case 'user-edit': return viewUserEdit(view.data);
        default: return viewHome();
      }
    }

    this.create = function () {
      injectCss();
      sizeView();
      if (window.addEventListener) {
        window.addEventListener('resize', sizeView);
        setTimeout(sizeView, 300);
      }
      boot();
    };

    this.start = function () {
      Lampa.Controller.add('content', {
        toggle: function () {
          Lampa.Controller.collectionSet(scroll.render());
          Lampa.Controller.collectionFocus(lastFocus, scroll.render());
        },
        left: function () {
          if (window.Navigator && Navigator.canmove('left')) Navigator.move('left');
          else Lampa.Controller.toggle('menu');
        },
        right: function () {
          if (window.Navigator && Navigator.canmove('right')) Navigator.move('right');
        },
        up: function () {
          if (window.Navigator && Navigator.canmove('up')) Navigator.move('up');
          else Lampa.Controller.toggle('head');
        },
        down: function () {
          if (window.Navigator && Navigator.canmove('down')) Navigator.move('down');
        },
        back: function () {
          self.back();
        }
      });
      Lampa.Controller.toggle('content');
    };

    this.back = function () {
      popView();
    };

    this.pause = function () { };
    this.stop = function () { };
    this.render = function () { return html; };
    this.destroy = function () {
      scroll.destroy();
      html.remove();
    };
  }

  function openAdmin() {
    Lampa.Activity.push({
      url: '',
      title: 'Admin Panel',
      component: COMPONENT,
      page: 1
    });
  }

  function install() {
    if (!Lampa.SettingsApi || !Lampa.Component) return;
    if (!Lampa.Component.get || !Lampa.Component.get(COMPONENT))
      Lampa.Component.add(COMPONENT, component);

    Lampa.SettingsApi.addComponent({
      component: 'lampac_adminpanel',
      name: 'Admin Panel',
      icon: '<svg viewBox="0 0 24 24"><path fill="currentColor" d="M12 1a5 5 0 0 0-5 5v2H5a3 3 0 0 0-3 3v9a3 3 0 0 0 3 3h14a3 3 0 0 0 3-3v-9a3 3 0 0 0-3-3h-2V6a5 5 0 0 0-5-5m-3 7V6a3 3 0 1 1 6 0v2H9m3 4a2 2 0 1 1 0 4 2 2 0 0 1 0-4Z"/></svg>'
    });

    Lampa.SettingsApi.addParam({
      component: 'lampac_adminpanel',
      param: { name: 'lampac_adminpanel_open', type: 'button' },
      field: {
        name: 'Mở Admin Panel',
        description: 'Trong Lampa, không mở WebView. Mật khẩu nhập một lần rồi nhớ trên máy này.'
      },
      onChange: openAdmin
    });
  }

  function wait() {
    if (typeof Lampa === 'undefined' || !Lampa.SettingsApi || !Lampa.Component || !Lampa.Activity) {
      setTimeout(wait, 300);
      return;
    }
    install();
  }

  wait();
})();
