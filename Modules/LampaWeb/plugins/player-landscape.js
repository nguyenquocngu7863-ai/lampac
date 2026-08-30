(function () {
  'use strict';

  if (window.lampac_player_landscape_plugin) return;
  window.lampac_player_landscape_plugin = true;

  var settingName = 'lampac_player_landscape';
  var cssClass = 'lampac-player-landscape-css';
  var styleId = 'lampac-player-landscape-style';
  var locked = false;
  var weRequestedFullscreen = false;
  var retryTimers = [];

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
      var orientation = window.screen && window.screen.orientation;
      if (orientation && typeof orientation.unlock === 'function')
        orientation.unlock();
    } catch (e) { }
  }

  function requestFullscreen() {
    try {
      if (document.fullscreenElement || document.webkitFullscreenElement || document.mozFullScreenElement)
        return;
      var el = document.documentElement;
      var req = el.requestFullscreen || el.webkitRequestFullscreen || el.mozRequestFullScreen || el.msRequestFullscreen;
      if (!req) return;
      var result = req.call(el);
      weRequestedFullscreen = true;
      if (result && typeof result.catch === 'function') {
        result.catch(function () {
          weRequestedFullscreen = false;
        });
      }
    } catch (e) {
      weRequestedFullscreen = false;
    }
  }

  function exitFullscreen() {
    if (!weRequestedFullscreen) return;
    weRequestedFullscreen = false;
    try {
      var exit = document.exitFullscreen || document.webkitExitFullscreen || document.mozCancelFullScreen || document.msExitFullscreen;
      if (!exit) return;
      var result = exit.call(document);
      if (result && typeof result.catch === 'function')
        result.catch(function () { });
    } catch (e) { }
  }

  function applyCss(on) {
    if (!document.body) return;
    document.body.classList.toggle(cssClass, !!on);
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
    requestFullscreen();
    lockNative();
    applyCss(isPortrait());
    pingResize();

    [200, 600, 1400].forEach(function (ms) {
      retryTimers.push(setTimeout(function () {
        if (!locked) return;
        lockNative();
        applyCss(isPortrait());
        pingResize();
      }, ms));
    });
  }

  function unlock() {
    locked = false;
    clearRetries();
    applyCss(false);
    unlockNative();
    exitFullscreen();
    pingResize();
  }

  function installStyle() {
    if (document.getElementById(styleId)) return;
    var style = document.createElement('style');
    style.id = styleId;
    style.textContent = [
      '@media screen and (orientation: portrait) {',
      '  body.' + cssClass + ' .player {',
      '    position: fixed !important;',
      '    top: 0 !important;',
      '    left: 100vw !important;',
      '    width: 100vh !important;',
      '    height: 100vw !important;',
      '    transform: rotate(90deg);',
      '    transform-origin: top left;',
      '    z-index: 100 !important;',
      '  }',
      '  body.' + cssClass + ' .player video {',
      '    width: 100% !important;',
      '    height: 100% !important;',
      '    object-fit: contain !important;',
      '  }',
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
