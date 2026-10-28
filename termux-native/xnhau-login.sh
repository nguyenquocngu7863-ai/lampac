#!/data/data/com.termux/files/usr/bin/bash
# xnhau-login.sh — refresh cookie đăng nhập xnhau.pub (dùng bởi module Adult/XNhau).
#
#   ~/lampac-native/xnhau-login.sh [user] [pass]
#
# Vì sao cần Xvnc: Cloudflare Turnstile không giải được trên Chrome headless,
# nên phải chạy Chrome HEADFUL trên X server ảo. Xvnc được bật tự động ở đây
# (X11 qua TCP 6001 vì libX11 của rootfs tìm /tmp/.X11-unix — Termux không có).
# Cookie ghi ra ~/lampac-run/xnhau.cookie (module đọc lại, không cần restart).
set -uo pipefail

N="${HOME}/lampac-native"
NODE="${N}/node-bin/node"
JS="${N}/xnhau-login.js"
COOKIE_OUT="${HOME}/lampac-run/xnhau.cookie"
R=/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs

USER_ARG="${1:-${XNHAU_USER:-}}"
PASS_ARG="${2:-${XNHAU_PASS:-}}"

xvnc_up() { (exec 3<>/dev/tcp/127.0.0.1/6001) 2>/dev/null; }

if ! xvnc_up; then
  echo "[xnhau-login] bật Xvnc :1" >&2
  mkdir -p "${N}/tmp/probe"
  nohup Xvnc :1 -geometry 1280x900 -depth 24 -rfbport 5901 \
        -SecurityTypes None -ac -AlwaysShared -listen tcp \
        > "${N}/tmp/probe/xvnc.log" 2>&1 &
  for i in $(seq 1 20); do xvnc_up && break; sleep 1; done
  xvnc_up || { echo '{"ok":false,"msg":"không bật được Xvnc :1 (thiếu tigervnc?)"}'; exit 1; }
fi

[ -x "$NODE" ] || { echo '{"ok":false,"msg":"thiếu node glibc (~/lampac-native/node-bin/node)"}'; exit 1; }
[ -f "$JS" ]   || { echo '{"ok":false,"msg":"thiếu ~/lampac-native/xnhau-login.js"}'; exit 1; }

export DISPLAY=127.0.0.1:1
export LD_LIBRARY_PATH="$PREFIX/glibc/lib:$R/usr/lib/aarch64-linux-gnu:$R/lib/aarch64-linux-gnu"
export LD_PRELOAD="${N}/libseccomp-shim.so"
export TMPDIR="${N}/tmp"; export TMP="$TMPDIR"; export TEMP="$TMPDIR"
export FONTCONFIG_FILE="${N}/fonts.conf"
export PLAYWRIGHT_BROWSERS_PATH="$R/root/.cache/ms-playwright"
export NODE_PATH="${HOME}/lampac-run/.playwright/package"
export XNHAU_COOKIE="$COOKIE_OUT"

exec "$NODE" "$JS" "$USER_ARG" "$PASS_ARG" "$COOKIE_OUT"
