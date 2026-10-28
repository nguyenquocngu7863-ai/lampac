#!/data/data/com.termux/files/usr/glibc/bin/bash
# Chrome wrapper for Termux native (glibc). Passes all args through.
case "$0" in
  */*) SCRIPT_DIR="${0%/*}" ;;
  *) SCRIPT_DIR="$PWD" ;;
esac
case "$SCRIPT_DIR" in
  /*) ;;
  *) SCRIPT_DIR="$PWD/$SCRIPT_DIR" ;;
esac
NATIVE_DIR="${SCRIPT_DIR%/*}"
unset LD_PRELOAD
export LD_PRELOAD="$NATIVE_DIR/libseccomp-shim.so"
export LD_LIBRARY_PATH="/data/data/com.termux/files/usr/glibc/lib:/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs/usr/lib/aarch64-linux-gnu:/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs/lib/aarch64-linux-gnu"

# ─── Fontconfig ────────────────────────────────────────────────────────────────
# Chrome chạy NATIVE (ngoài proot) nên fonts.conf của Ubuntu trỏ /usr/share/fonts
# không tồn tại → fontconfig trả 0 font → khi gặp chữ có dấu (tiếng Việt) Skia
# chết: "FATAL:.../SkFontMgr_FontConfigInterface.cpp:163] Not implemented."
# ⇒ Playwright báo "Target page, context or browser has been closed".
# Trỏ fontconfig vào font của rootfs Ubuntu + font hệ thống Android.
R="/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs"
FONTCONF="$NATIVE_DIR/fonts.conf"
if [[ ! -s "$FONTCONF" ]]; then
  mkdir -p "$NATIVE_DIR/fontcache"
  cat > "$FONTCONF" <<EOF
<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "fonts.dtd">
<fontconfig>
  <dir>$R/usr/share/fonts</dir>
  <dir>/system/fonts</dir>
  <cachedir>$NATIVE_DIR/fontcache</cachedir>
</fontconfig>
EOF
fi
export FONTCONFIG_FILE="$FONTCONF"

export CHROME_WRAPPER="$SCRIPT_DIR/chrome"
exec "$SCRIPT_DIR/chrome" "$@"
