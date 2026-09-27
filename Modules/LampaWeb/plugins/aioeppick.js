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

  /* JavGuru: /javguru/vidosik tra ve luon dict server, muc 0 la STREAM TV
   * (turbo) / VO / LU / DD / JK. Module KHONG resolve truoc — chi tra danh
   * sach, server nao bam moi resolve dung server do. Nho vay popup nay ra
   * ngay (khong timeout 20-30s) va bam server nao thi chi tra ve server do. */
  function isJavGuruUrl(url) {
    return typeof url === 'string' && url.indexOf('javguru/vidosik') >= 0;
  }

  function mapOf(json) {
    if (!json || typeof json !== 'object') return null;

    // AIOStreams: link nam trong json.quality
    if (json.quality && typeof json.quality === 'object') {
      var qk = Object.keys(json.quality);
      if (qk.length) {
        return qk.map(function (k) { return { title: k, url: json.quality[k] }; });
      }
    }

    // JavGuru: dict phang o cap goc, bo cac key bao loi/flags
    var skip = { error: 1, msg: 1, denymsg: 1, accsdb: 1, rch: 1, quality: 1, qualitys: 1, url: 1, title: 1, card: 1 };
    var out = [];
    for (var k in json) {
      if (!Object.prototype.hasOwnProperty.call(json, k)) continue;
      if (skip[k]) continue;
      if (typeof json[k] !== 'string' || !/^https?:/i.test(json[k])) continue;
      out.push({ title: k, url: json[k] });
    }
    return out.length ? out : null;
  }

  /* isJg = chon nguon do chinh module JavGuru tra ve.
   * Khi do SISI doc ket qua Api.qualitys (sisi.js):
   *   var qualitys = data.qualitys || data;
   *   for (var i in qualitys) qualitys[i] = Api.account(qualitys[i], true);
   * nen key phai la "qualitys" va moi gia tri phai la URL string. Dat
   * "quality" (object) se lam Api.account goi u.replace tren object ->
   * TypeError. "url" cung khong can: video.url lay tu qualityDefault. */
  function pickAndGo(json, success, isJg) {
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
            if (isJg) {
              // SISI: chi giu 1 nguon, ten dung "qualitys"
              json.qualitys = {};
              json.qualitys[it.title] = it.url;
              delete json.quality;
              delete json.url;
            } else {
              // giu nguyen hinh dang Lampa doc: dat url va quality khoa lai
              json.url = it.url;
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
      if (typeof orig !== 'function' || orig.__aioWrapped) return;
      inst[name] = function(url, success, error) {
        var args = arguments;
        var self = this;
        if ((isEpisodeUrl(url) || isJavGuruUrl(url)) && typeof success === 'function') {
          var isJg = isJavGuruUrl(url);
          var wrappedOk = function(json) { pickAndGo(json, success, isJg); };
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
