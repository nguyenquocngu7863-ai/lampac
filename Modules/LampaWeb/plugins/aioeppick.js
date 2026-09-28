/* AIOEpPick — popup chon nguon cho tap phim AIOStreams.
 * Online plugin fetch URL tap phim qua Lampa.Reguest (network.native):
 * boc lai de khi JSON tra ve co quality thi hien Lampa.Select, bam dong
 * nao thi play link dong do. Back thi choi mac dinh. URL khac cho qua. */
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

    // AIOStreams: link nam trong json.quality
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
      var items = mapOf(json);
      if (!items || items.length < 2) { success(json); return; }
      if (typeof Lampa === 'undefined' || !Lampa.Select || !Lampa.Select.show) {
        success(json);
        return;
      }
      try {
        if (Lampa.Loading && typeof Lampa.Loading.stop === 'function')
          Lampa.Loading.stop();
      } catch (e) {}
      Lampa.Select.show({
        title: 'Chọn nguồn',
        items: items,
        onSelect: function(it) {
          try {
            // giu nguyen hinh dang Lampa doc: dat url va quality
            // khoa lai
            json.url = it.url;
            json.quality = {};
            json.quality[it.title] = it.url;
          } catch (e) {}
          success(json);
        },
        onBack: function() { success(json); }
      });
    } catch (e) {
      success(json);
    }
  }

  function wrapMethod(inst, name) {
    try {
      var orig = inst[name];
      if (typeof orig !== 'function' || orig.__aioWrapped) return;
      inst[name] = function(url, success, error) {
        var args = arguments;
        var self = this;
        if (isEpisodeUrl(url) && typeof success === 'function') {
          var wrappedOk = function(json) {
            pickAndGo(json, success);
          };
          var a = Array.prototype.slice.call(args);
          a[1] = wrappedOk;
          return orig.apply(self, a);
        }
        return orig.apply(self, args);
      };
      inst[name].__aioWrapped = true;
    } catch (e) {}
  }

  function start() {
    try {
      if (typeof Lampa === 'undefined' || typeof Lampa.Reguest !== 'function') {
        setTimeout(start, 500);
        return;
      }
    } catch (e) {
      setTimeout(start, 500);
      return;
    }

    if (start.done) return;
    start.done = true;

    try {
      var Orig = Lampa.Reguest;
      var Proxy = function() {
        var inst = new Orig();
        wrapMethod(inst, 'native');
        wrapMethod(inst, 'get');
        wrapMethod(inst, 'silent');
        wrapMethod(inst, 'quiet');
        return inst;
      };
      Proxy.prototype = Orig.prototype;
      for (var k in Orig) {
        try { Proxy[k] = Orig[k]; } catch (e) {}
      }
      Lampa.Reguest = Proxy;
      log('plugin ready (Reguest hooked)');
    } catch (e) {
      log('hook failed');
    }
  }

  start();
})();
