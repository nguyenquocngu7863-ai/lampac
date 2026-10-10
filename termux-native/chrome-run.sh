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
export CHROME_WRAPPER="$SCRIPT_DIR/chrome"
exec "$SCRIPT_DIR/chrome" "$@"
