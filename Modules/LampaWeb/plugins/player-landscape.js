(function () {
  'use strict';

  if (window.lampac_player_landscape_plugin) return;
  window.lampac_player_landscape_plugin = true;

  var settingName = 'lampac_player_landscape';
  var cssClass = 'lampac-player-landscape-css';
  var styleId = 'lampac-player-landscape-style';
  var overlayId = 'lampac-player-landscape-touch';
  var locked = false;
  var cssOn = false;
  var retryTimers = [];
  var modalWatcher = null;

  function enabled() {
    if (!window.Lampa || !Lampa.Storage) return true;
    var value = Lampa.Storage.get(settingName, 'true');
    return value !== false && value !== 'false';
  }

  function isTv() {
    try {
      if (Lampa.Platform && typeof Lampa.Platform.screen === 'function' && Lampa.Platform.screen('tv'))
        return true;
      if (Lampa.Platform && typeof Lampa.Platform.tv === 'function' && Lampa.Platform.tv())
        return true;
    } catch (e) { }
    return false;
  }

  function isPhone() {
    if (isTv()) return false;
    try {
      if (Lampa.Platform && typeof Lampa.Platform.screen === 'function' && Lampa.Platform.screen('mobile'))
        return true;
    } catch (e) { }
    var coarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
    var narrow = Math.min(window.innerWidth || 0, window.innerHeight || 0) <= 820;
    return coarse || narrow || /Android|iPhone|iPod|Mobile/i.test(navigator.userAgent || '');
  }

  function isPortrait() {
    try {
      if (screen.orientation && screen.orientation.type)
        return String(screen.orientation.type).indexOf('portrait') === 0;
    } catch (e) { }
    if (typeof window.orientation === 'number')
      return Math.abs(window.orientation) !== 90;
    return (window.innerHeight || 0) > (window.innerWidth || 0);
  }

  function lockNative() {
    try {
      if (typeof AndroidJS !== 'undefined') {
        if (typeof AndroidJS.setOrientation === 'function') AndroidJS.setOrientation('landscape');
        else if (typeof AndroidJS.changeOrientation === 'function') AndroidJS.changeOrientation(1);
      }
    } catch (e) { }

    try {
      var orientation = window.screen && window.screen.orientation;
      if (orientation && typeof orientation.lock === 'function') {
        var result = orientation.lock('landscape');
        if (result && typeof result.catch === 'function')
          result.catch(function () { });
        return true;
      }
      var screenObject = window.screen;
      var legacy = screenObject && (
        screenObject.lockOrientation ||
        screenObject.mozLockOrientation ||
        screenObject.msLockOrientation ||
        screenObject.webkitLockOrientation
      );
      if (typeof legacy === 'function') {
        legacy.call(screenObject, 'landscape');
        return true;
      }
    } catch (e) { }
    return false;
  }

  function unlockNative() {
    try {
      if (typeof AndroidJS !== 'undefined') {
        if (typeof AndroidJS.setOrientation === 'function') AndroidJS.setOrientation('portrait');
        else if (typeof AndroidJS.changeOrientation === 'function') AndroidJS.changeOrientation(0);
      }
    } catch (e) { }

    try {
      var orientation = window.screen && window.screen.orientation;
      if (orientation && typeof orientation.unlock === 'function')
        orientation.unlock();
    } catch (e) { }
  }

  function keepInlineVideo() {
    var video = document.querySelector('.player video');
    if (!video) return;
    try {
      video.setAttribute('playsinline', '');
      video.setAttribute('webkit-playsinline', '');
      video.playsInline = true;
    } catch (e) { }
  }

  function playerEl() {
    return document.querySelector('.player');
  }

  function modalOpen() {
    var nodes = document.querySelectorAll('.selectbox, .modal, .settings-container');
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (!el || el.classList.contains('hide')) continue;
      var style = window.getComputedStyle(el);
      if (style.display === 'none' || style.visibility === 'hidden') continue;
      if ((el.offsetWidth || 0) < 2 && (el.offsetHeight || 0) < 2) continue;
      return true;
    }
    return false;
  }

  function localPoint(clientX, clientY) {
    var vw = window.innerWidth || 0;
    return { x: clientY, y: vw - clientX };
  }

  function localRect(el, root) {
    var left = 0;
    var top = 0;
    var node = el;
    while (node && node !== root) {
      left += node.offsetLeft || 0;
      top += node.offsetTop || 0;
      node = node.offsetParent;
      if (node === document.body || node === document.documentElement)
        break;
    }
    return {
      left: left,
      top: top,
      width: el.offsetWidth || 0,
      height: el.offsetHeight || 0,
      right: left + (el.offsetWidth || 0),
      bottom: top + (el.offsetHeight || 0)
    };
  }

  function hitFromRects(x, y) {
    var root = playerEl();
    if (!root) return null;
    var best = null;
    var bestArea = Infinity;
    var nodes = root.querySelectorAll('.selector, button, .player-panel__time-touch-zone, .player-panel__timeline, .player-video');
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      var r = el.getBoundingClientRect();
      if (r.width < 8 || r.height < 8) continue;
      if (x < r.left || x > r.right || y < r.top || y > r.bottom) continue;
      var area = r.width * r.height;
      if (area < bestArea) {
        bestArea = area;
        best = el;
      }
    }
    return best;
  }

  function hitFromLocal(clientX, clientY) {
    var root = playerEl();
    if (!root) return null;
    var pt = localPoint(clientX, clientY);
    var best = null;
    var bestArea = Infinity;
    var nodes = root.querySelectorAll('.selector, button, .player-panel__time-touch-zone, .player-panel__timeline, .player-video');
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      var r = localRect(el, root);
      if (r.width < 8 || r.height < 8) continue;
      if (pt.x < r.left || pt.x > r.right || pt.y < r.top || pt.y > r.bottom) continue;
      var area = r.width * r.height;
      if (area < bestArea) {
        bestArea = area;
        best = el;
      }
    }
    return best;
  }

  function hit(clientX, clientY) {
    return hitFromRects(clientX, clientY) || hitFromLocal(clientX, clientY);
  }

  function fireEnter(el) {
    if (!el) return;
    var target = el;
    if (window.$) {
      var $el = window.$(el);
      var $sel = $el.closest('.selector');
      if ($sel && $sel.length) $el = $sel;
      $el.trigger('hover:enter');
      $el.trigger('click');
    } else {
      if (target.closest) {
        var sel = target.closest('.selector');
        if (sel) target = sel;
      }
      try {
        target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
      } catch (e) {
        if (typeof target.click === 'function') target.click();
      }
    }
    setTimeout(syncOverlay, 30);
  }

  function seekAt(clientX, clientY, el) {
    var timeline = el;
    if (!timeline.classList.contains('player-panel__timeline') && !timeline.classList.contains('player-panel__time-touch-zone')) {
      timeline = document.querySelector('.player-panel__timeline');
    }
    if (!timeline) return false;

    var percent = 0;
    var mapped = false;
    var box = timeline.getBoundingClientRect();
    if (box.width >= 8 && box.height >= 8 &&
        clientX >= box.left - 24 && clientX <= box.right + 24 &&
        clientY >= box.top - 24 && clientY <= box.bottom + 24) {
      if (box.width >= box.height)
        percent = (clientX - box.left) / box.width;
      else
        percent = (clientY - box.top) / box.height;
      mapped = true;
    }

    if (!mapped) {
      var root = playerEl();
      var pt = localPoint(clientX, clientY);
      var r = localRect(timeline, root || timeline);
      if (r.width >= 8)
        percent = (pt.x - r.left) / r.width;
    }

    percent = Math.max(0, Math.min(1, percent));
    try {
      var video = document.querySelector('.player video');
      if (video && video.duration)
        video.currentTime = video.duration * percent;
      return true;
    } catch (e) { }
    return false;
  }

  function tapVideo(clientX, clientY) {
    var videoHtml = document.querySelector('.player-video');
    if (!videoHtml) {
      fireEnter(document.querySelector('.player-panel__playpause'));
      return;
    }
    try {
      var ev = new MouseEvent('click', {
        bubbles: true,
        cancelable: true,
        view: window,
        clientX: clientX,
        clientY: clientY
      });
      videoHtml.dispatchEvent(ev);
    } catch (e) {
      fireEnter(document.querySelector('.player-panel__playpause'));
    }
  }

  function tap(clientX, clientY) {
    if (modalOpen()) return;
    var el = hit(clientX, clientY);
    if (!el) {
      tapVideo(clientX, clientY);
      return;
    }
    if (el.classList.contains('player-panel__time-touch-zone') ||
        el.classList.contains('player-panel__timeline') ||
        (el.className && String(el.className).indexOf('timeline') >= 0)) {
      seekAt(clientX, clientY, el);
      return;
    }
    if (el.classList.contains('player-video') || (el.tagName && el.tagName.toLowerCase() === 'video')) {
      tapVideo(clientX, clientY);
      return;
    }
    fireEnter(el);
  }

  function bindOverlay(el) {
    var start = null;

    el.addEventListener('touchstart', function (e) {
      if (!cssOn || modalOpen()) return;
      var t = e.changedTouches && e.changedTouches[0];
      if (!t) return;
      start = { x: t.clientX, y: t.clientY, target: hit(t.clientX, t.clientY) };
      e.preventDefault();
    }, { passive: false });

    el.addEventListener('touchmove', function (e) {
      if (!cssOn || !start) return;
      var t = e.changedTouches && e.changedTouches[0];
      if (!t) return;
      var target = start.target;
      if (target && (target.classList.contains('player-panel__time-touch-zone') ||
          target.classList.contains('player-panel__timeline'))) {
        seekAt(t.clientX, t.clientY, target);
        e.preventDefault();
      }
    }, { passive: false });

    el.addEventListener('touchend', function (e) {
      if (!cssOn || !start) return;
      var t = e.changedTouches && e.changedTouches[0];
      if (!t) {
        start = null;
        return;
      }
      var dx = t.clientX - start.x;
      var dy = t.clientY - start.y;
      if (dx * dx + dy * dy < 900)
        tap(t.clientX, t.clientY);
      start = null;
      e.preventDefault();
    }, { passive: false });
  }

  function ensureOverlay() {
    var el = document.getElementById(overlayId);
    if (el) return el;
    el = document.createElement('div');
    el.id = overlayId;
    el.setAttribute('aria-hidden', 'true');
    document.body.appendChild(el);
    bindOverlay(el);
    return el;
  }

  function syncOverlay() {
    var el = document.getElementById(overlayId);
    if (!cssOn) {
      if (el) el.style.display = 'none';
      return;
    }
    el = ensureOverlay();
    el.style.display = modalOpen() ? 'none' : 'block';
  }

  function applyCss(on) {
    if (!document.body) return;
    cssOn = !!on;
    document.body.classList.toggle(cssClass, cssOn);
    syncOverlay();
  }

  function pingResize() {
    try { window.dispatchEvent(new Event('resize')); } catch (e) { }
  }

  function clearRetries() {
    retryTimers.forEach(function (id) { clearTimeout(id); });
    retryTimers = [];
  }

  function lock() {
    if (!enabled() || !isPhone()) return;
    locked = true;
    keepInlineVideo();
    lockNative();
    applyCss(false);

    [180, 500, 1200].forEach(function (ms) {
      retryTimers.push(setTimeout(function () {
        if (!locked) return;
        keepInlineVideo();
        lockNative();
        applyCss(isPortrait());
      }, ms));
    });
  }

  function unlock() {
    locked = false;
    clearRetries();
    applyCss(false);
    unlockNative();
    pingResize();
  }

  function installStyle() {
    if (document.getElementById(styleId)) return;
    var style = document.createElement('style');
    style.id = styleId;
    style.textContent = [
      '#' + overlayId + ' {',
      '  position: fixed;',
      '  inset: 0;',
      '  z-index: 110;',
      '  display: none;',
      '  touch-action: none;',
      '  background: transparent;',
      '}',
      'body.' + cssClass + ' .player {',
      '  position: fixed !important;',
      '  top: 0 !important;',
      '  left: 100vw !important;',
      '  width: 100vh !important;',
      '  height: 100vw !important;',
      '  transform: rotate(90deg);',
      '  transform-origin: top left;',
      '  z-index: 100 !important;',
      '}',
      'body.' + cssClass + ' .player video {',
      '  width: 100% !important;',
      '  height: 100% !important;',
      '  object-fit: contain !important;',
      '}'
    ].join('\n');
    document.head.appendChild(style);
  }

  function installSetting() {
    if (!window.Lampa || !Lampa.SettingsApi || window.lampac_player_landscape_setting) return;
    window.lampac_player_landscape_setting = true;

    Lampa.SettingsApi.addParam({
      component: 'interface',
      param: {
        name: settingName,
        type: 'trigger',
        values: '',
        default: true
      },
      field: {
        name: 'Player xoay ngang',
        description: 'Khi phát video trên điện thoại, khóa màn hình ngang. Tắt player thì trả lại dọc.'
      }
    });
  }

  function bindPlayer() {
    if (!window.Lampa || !Lampa.Player || !Lampa.Player.listener) {
      setTimeout(bindPlayer, 250);
      return;
    }

    Lampa.Player.listener.follow('create', lock);
    Lampa.Player.listener.follow('start', lock);
    Lampa.Player.listener.follow('destroy', unlock);

    if (Lampa.PlayerVideo && Lampa.PlayerVideo.listener)
      Lampa.PlayerVideo.listener.follow('play', lock);
  }

  function install() {
    if (!window.Lampa || !document.head || !document.body) {
      setTimeout(install, 250);
      return;
    }

    installStyle();
    installSetting();
    bindPlayer();
  }

  install();
})();
