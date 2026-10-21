/* AIOEpPick — popup chọn nguồn cho tập phim AIOStreams.
 *
 * MÓC Ở `$.ajax` (không phải Lampa.Request):
 *   Lampa.Request gán method lên TỪNG instance (`this.get = function…`, `this["native"] = …`)
 *   — không có method nào trên prototype. Component "Trực tuyến" lại tạo `new Request()`
 *   bằng biến module-scoped, nên thay `Lampa.Request` bằng Proxy KHÔNG bắt được request
 *   của Lampa (popup im lặng biến mất).
 *   Mọi request của Lampa đều đi qua jQuery: `Request.quiet/silent/get/native` → `go()` →
 *   `request()` → `$.ajax(data)` (app.min.js ~33500). Hook `$.ajax` là điểm duy nhất phủ hết.
 *
 * Luồng: bấm tập → Lampa gọi `/lite/aiostreams/episode?...&s=&e=` → JSON có `quality`
 * (mỗi FILE một dòng) → hiện Lampa.Select. Bấm dòng nào thì phát link dòng đó
 * (đặt `json.url` + `json.quality`), Back thì chọn mặc định. URL khác cho qua.
 */
(function() {
  'use strict';

  if (window.__aioEpPickLoaded) return;
  window.__aioEpPickLoaded = true;

  function log() {
    var args = ['[AIOEpPick]'].concat(Array.prototype.slice.call(arguments));
    try { console.log.apply(console, args); } catch (e) {}
  }

  function isEpisodeUrl(url) {
    return typeof url === 'string'
      && url.indexOf('lite/aiostreams') >= 0
      && /[?&]s=\d+/.test(url)
      && /[?&]e=\d+/.test(url);
  }

  function mapOf(json) {
    if (!json || typeof json !== 'object') return null;

    // AIOStreams: link nằm trong json.quality
    if (json.quality && typeof json.quality === 'object') {
      var qk = Object.keys(json.quality);
      if (qk.length) {
        return qk.map(function (k) {
          return { title: k, url: json.quality[k] };
        });
      }
    }

    return null;
  }

  function pickAndGo(json, success) {
    try {
      if (!json || typeof json !== 'object') { success(json); return; }

      // Một response chỉ hỏi một lần (tránh 2 popup nếu nhiều hook cùng thấy).
      if (json.__aioPicked) { success(json); return; }

      var items = mapOf(json);
      if (!items || items.length < 2) { success(json); return; }
      if (typeof Lampa === 'undefined' || !Lampa.Select || !Lampa.Select.show) {
        success(json);
        return;
      }
      try {
        if (Lampa.Loading && typeof Lampa.Loading.stop === 'function') Lampa.Loading.stop();
      } catch (e) {}
      Lampa.Select.show({
        title: 'Chọn nguồn',
        items: items,
        onSelect: function(it) {
          try {
            // giữ nguyên hình dạng Lampa đọc: đặt url và quality khoá lại
            json.url = it.url;
            json.quality = {};
            json.quality[it.title] = it.url;
          } catch (e) {}
          json.__aioPicked = true;
          success(json);
        },
        onBack: function() {
          json.__aioPicked = true;
          success(json);
        }
      });
    } catch (e) {
      success(json);
    }
  }

  // Hook jQuery: mọi request của Lampa đi qua đây.
  function hookAjax() {
    var $ = window.jQuery || window.$;
    if (!$ || typeof $.ajax !== 'function') return false;
    if ($.ajax.__aioWrapped) return true;

    var orig = $.ajax;

    function wrap(opts) {
      var ok = opts.success;
      opts.success = function(json) {
        try {
          pickAndGo(json, function(final) {
            if (typeof ok === 'function') ok(final);
          });
        } catch (e) {
          if (typeof ok === 'function') ok(json);
        }
      };
      return orig.call($, opts);
    }

    $.ajax = function(first, second) {
      try {
        var opts = (typeof first === 'string')
          ? $.extend({ url: first }, second || {})
          : first;

        if (opts && isEpisodeUrl(opts.url) && typeof opts.success === 'function')
          return wrap(opts);
      } catch (e) {}
      return orig.apply($, arguments);
    };

    $.ajax.__aioWrapped = true;
    log('plugin ready ($.ajax hooked)');
    return true;
  }

  function start() {
    if (hookAjax()) return;
    setTimeout(start, 300);
  }

  start();
})();
