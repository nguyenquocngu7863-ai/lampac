/* AIOEpPick — popup chọn nguồn (file) cho tập phim AIOStreams.
 *
 * MÓC Ở `Lampa.Player.play` (KHÔNG phải $.ajax, KHÔNG phải Lampa.Request):
 *   - Online plugin (module/Online/plugin.js → /online.js) lấy JSON tập phim bằng
 *     network['native'](...). Trên APK Android, Request.native rơi vào
 *     Android.httpReq (bridge native) → KHÔNG đi qua jQuery → hook $.ajax chết,
 *     popup không hiện. Hook Lampa.Request cũng không bắt được vì Request gán
 *     method lên TỪNG instance (this.get = …, this["native"] = …).
 *   - Mọi đường phát (web lẫn APK) đều kết thúc ở Lampa.Player.play(element) với
 *     element.quality = { "<nhãn file>": "<endpoint>" } do server dựng từ
 *     json.quality. Hook ở đây phủ cả hai nền tảng, không phụ thuộc request.
 *
 * Luồng: bấm tập → server trả JSON quality (mỗi FILE một dòng) → popup
 * Lampa.Select liệt kê từng file → chọn dòng nào phát link dòng đó; Back phát
 * link đầu tiên. Chỉ đụng tới URL có /lite/aiostreams/ nên module khác không bị ảnh hưởng.
 */
(function() {
  'use strict';

  if (window.__aioEpPickLoaded) return;
  window.__aioEpPickLoaded = true;

  var AIO_PATH = '/lite/aiostreams/';

  function log() {
    var args = ['[AIOEpPick]'].concat(Array.prototype.slice.call(arguments));
    try { console.log.apply(console, args); } catch (e) {}
  }

  function isAioUrl(url) {
    return typeof url === 'string' && url.indexOf(AIO_PATH) !== -1;
  }

  // Trả về [{title, url}] khi data.quality là dict >= 2 file AIOStreams, ngược lại null.
  function qualityItems(data) {
    if (!data || typeof data !== 'object') return null;

    var quality = data.quality;
    if (!quality || typeof quality !== 'object') return null;

    var keys = Object.keys(quality);
    if (keys.length < 2) return null;

    var items = [];

    for (var i = 0; i < keys.length; i++) {
      var title = keys[i];
      var value = quality[title];
      var url = typeof value === 'string'
        ? value
        : (value && typeof value === 'object' ? value.url : '');

      if (!isAioUrl(url)) return null;
      items.push({ title: title, url: url });
    }

    return items.length >= 2 ? items : null;
  }

  // Hiện popup. callbacks: { select(item), back() }
  function showPick(items, callbacks) {
    if (typeof Lampa === 'undefined' || !Lampa.Select || typeof Lampa.Select.show !== 'function')
      return false;

    try {
      if (Lampa.Loading && typeof Lampa.Loading.stop === 'function')
        Lampa.Loading.stop();
    } catch (e) {}

    try {
      Lampa.Select.show({
        title: 'Chọn nguồn',
        items: items,
        onSelect: function(item) {
          try { callbacks.select(item); } catch (e) { log('select error', e && e.message); }
        },
        onBack: function() {
          try { callbacks.back(); } catch (e) { log('back error', e && e.message); }
        }
      });
      return true;
    } catch (e) {
      log('Select.show error', e && e.message);
      return false;
    }
  }

  // Phát với nguồn đã chọn: bỏ quality để Lampa không tự đổi link theo
  // video_quality_default (getUrlQuality sẽ ghi đè lựa chọn của người dùng).
  function applyChoice(data, url) {
    try {
      data.url = url;
      delete data.quality;
    } catch (e) {}
  }

  function hookPlayer() {
    if (typeof Lampa === 'undefined' || !Lampa.Player || typeof Lampa.Player.play !== 'function')
      return false;
    if (Lampa.Player.play.__aioWrapped) return true;

    var orig = Lampa.Player.play;

    Lampa.Player.play = function(data) {
      try {
        if (data && !data.__aioPicked) {
          var items = qualityItems(data);
          if (items) {
            var self = this;
            var args = arguments;
            var playDefault = function() {
              data.__aioPicked = true;
              applyChoice(data, items[0].url);
              orig.apply(self, args);
            };
            var shown = showPick(items, {
              select: function(item) {
                data.__aioPicked = true;
                applyChoice(data, item.url);
                orig.apply(self, args);
              },
              back: playDefault
            });
            if (shown) {
              log('popup', items.length, 'nguồn:', data.title || data.url);
              return;
            }
          }
        }
      } catch (e) {
        log('play hook error', e && e.message);
      }
      return orig.apply(this, arguments);
    };

    Lampa.Player.play.__aioWrapped = true;
    log('plugin ready (Player.play hooked)');
    return true;
  }

  // Tập kế tiếp trong player: playlist item có url là hàm (Online plugin gọi
  // getFileUrl bên trong). Bọc lại để chọn nguồn rồi mới trả url cho player.
  function hookPlaylist() {
    if (typeof Lampa === 'undefined' || !Lampa.Player || typeof Lampa.Player.playlist !== 'function')
      return false;
    if (Lampa.Player.playlist.__aioWrapped) return true;

    var orig = Lampa.Player.playlist;

    Lampa.Player.playlist = function(list) {
      try {
        if (list && typeof list.length === 'number') {
          for (var i = 0; i < list.length; i++) {
            var item = list[i];
            if (!item || typeof item.url !== 'function' || item.url.__aioWrapped) continue;

            (function(entry) {
              var resolve = entry.url;
              entry.url = function(done) {
                var self = this;
                resolve.call(self, function() {
                  try {
                    if (!entry.__aioPicked) {
                      var items = qualityItems(entry);
                      if (items) {
                        entry.__aioPicked = true;
                        var fallback = function() {
                          applyChoice(entry, items[0].url);
                          done();
                        };
                        var shown = showPick(items, {
                          select: function(chosen) {
                            applyChoice(entry, chosen.url);
                            done();
                          },
                          back: fallback
                        });
                        if (shown) return;
                      }
                    }
                  } catch (e) {
                    log('playlist pick error', e && e.message);
                  }
                  done();
                });
              };
              entry.url.__aioWrapped = true;
            })(item);
          }
        }
      } catch (e) {
        log('playlist hook error', e && e.message);
      }
      return orig.apply(this, arguments);
    };

    Lampa.Player.playlist.__aioWrapped = true;
    return true;
  }

  function start() {
    var ok = hookPlayer();
    if (ok) hookPlaylist();
    if (ok && hookPlaylist()) return;
    setTimeout(start, 300);
  }

  start();
})();
