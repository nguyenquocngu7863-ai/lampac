/* JavHDEpPick - popup chon server cho phim JavHD.
 * Online plugin fetch URL vidosik qua Lampa.Reguest (network.native):
 * boc lai de khi JSON tra ve co qualitys thi hien Lampa.Select, bam dong
 * nao thi play link dong do. Back thi choi mac dinh. URL khac cho qua. */
(function() {
  'use strict';

  if (window.__javhdEpPickLoaded) return;
  window.__javhdEpPickLoaded = true;

  function log() {
    var args = ['[JavHDEpPick]'].concat(Array.prototype.slice.call(arguments));
    try { console.log.apply(console, args); } catch (e) {}
  }

  function isVidosikUrl(url) {
    return typeof url === 'string' && url.indexOf('javhd/vidosik') >= 0;
  }

  function pickAndGo(json, success) {
    try {
      var q = json && (json.qualitys || json.quality);
      var keys = q ? Object.keys(q) : [];
      if (keys.length < 2) { success(json); return; }
      if (typeof Lampa === 'undefined' || !Lampa.Select || !Lampa.Select.show) {
        success(json);
        return;
      }
      var items = keys.map(function(k) { return { title: k, url: q[k] }; });
      try {
        if (Lampa.Loading && typeof Lampa.Loading.stop === 'function')
          Lampa.Loading.stop();
      } catch (e) {}
      Lampa.Select.show({
        title: 'Chon server',
        items: items,
        onSelect: function(it) {
          try {
            json.url = it.url;
            if (json.qualitys) {
              json.qualitys = {};
              json.qualitys[it.title] = it.url;
            }
            if (json.quality) {
              json.quality = {};
              json.quality[it.title] = it.url;
            }
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
      if (typeof orig !== 'function' || orig.__javhdWrapped) return;
      inst[name] = function(url, success, error) {
        var args = arguments;
        var self = this;
        if (isVidosikUrl(url) && typeof success === 'function') {
          var wrappedOk = function(json) { pickAndGo(json, success); };
          var a = Array.prototype.slice.call(args);
          a[1] = wrappedOk;
          return orig.apply(self, a);
        }
        return orig.apply(self, args);
      };
      inst[name].__javhdWrapped = true;
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
