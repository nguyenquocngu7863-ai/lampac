(function () {
  'use strict';
  // Ẩn control player nhanh: mặc định Lampa chờ 5s (mobile) / 3s (TV).
  // Chỉnh số giây trong Cài đặt > Ẩn control nhanh (không cần sửa file).
  // Cài plugin: mở app Lampa > Cài đặt > Plugins > thêm URL file này.
  //
  // GHI CHÚ: bản mod rootu chặn chạm ở tầng native nên plugin KHÔNG thấy
  // sự kiện touch/click trên video — chạm ẩn/hiện kiểu VLC không làm được
  // bằng plugin. Plugin này chỉ làm tự ẩn nhanh + chỉnh ms trong Cài đặt.

  function getDelay() {
    try {
      return Math.max(300, parseInt(Lampa.Storage.get('fasthide_ms', '1200')) || 1200);
    } catch (e) {
      return 1200;
    }
  }

  var timer = null;

  function disarm() {
    if (timer) {
      clearTimeout(timer);
      timer = null;
    }
  }

  function fire() {
    timer = null;
    try {
      if (!Lampa.PlayerPanel.visibleStatus()) return;
      var v = Lampa.PlayerVideo && Lampa.PlayerVideo.video ? Lampa.PlayerVideo.video() : null;
      if (!v || v.paused) return; // đang pause thì để yên cho bấm nút
      if (Lampa.PlayerPanel.render().hasClass('panel--footer-open')) {
        arm();
        return; // đang mở menu footer thì hẹn lại, không ẩn
      }
      Lampa.PlayerPanel.hide();
    } catch (e) {}
  }

  function arm() {
    disarm();
    timer = setTimeout(fire, getDelay());
  }

  function addSettings() {
    try {
      if (!Lampa.SettingsApi) return;
      Lampa.SettingsApi.addComponent({
        component: 'fasthide',
        name: 'Ẩn control nhanh',
        icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 3"/></svg>'
      });
      Lampa.SettingsApi.addParam({
        component: 'fasthide',
        param: { name: 'hide_ms', type: 'input', values: '', default: '1200' },
        field: { name: 'Thời gian chờ ẩn (ms)', description: 'Mặc định Lampa: 5000 (điện thoại), 3000 (TV). Nhập ví dụ 1200 = 1,2 giây, tối thiểu 300.' },
        onChange: function (value) {
          var ms = Math.max(300, parseInt(value) || 1200);
          Lampa.Storage.set('fasthide_ms', String(ms));
        }
      });
    } catch (e) {}
  }

  function start() {
    if (!window.Lampa || !Lampa.PlayerPanel || !Lampa.PlayerPanel.listener) {
      setTimeout(start, 500);
      return;
    }
    addSettings();
    Lampa.PlayerPanel.listener.follow('visible', function (e) {
      if (e.status) arm();
      else disarm();
    });
  }

  start();
})();
