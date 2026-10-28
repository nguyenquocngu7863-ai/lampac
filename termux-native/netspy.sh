#!/data/data/com.termux/files/usr/bin/bash
# netspy.sh <url> [giây] — mở bằng Chrome headful, ghi mọi request ra JSON
set -uo pipefail
N="${HOME}/lampac-native"
R=/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs
up() { (exec 3<>/dev/tcp/127.0.0.1/6001) 2>/dev/null; }
if ! up; then
  nohup Xvnc :1 -geometry 1280x900 -depth 24 -rfbport 5901 -SecurityTypes None -ac \
        -AlwaysShared -listen tcp > "${N}/tmp/probe/xvnc.log" 2>&1 &
  for i in $(seq 1 20); do up && break; sleep 1; done
fi
export DISPLAY=127.0.0.1:1
export LD_LIBRARY_PATH="$PREFIX/glibc/lib:$R/usr/lib/aarch64-linux-gnu:$R/lib/aarch64-linux-gnu"
export LD_PRELOAD="${N}/libseccomp-shim.so"
export TMPDIR="${N}/tmp"; export TMP="$TMPDIR"; export TEMP="$TMPDIR"
export FONTCONFIG_FILE="${N}/fonts.conf"
export PLAYWRIGHT_BROWSERS_PATH="$R/root/.cache/ms-playwright"
export NODE_PATH="${HOME}/lampac-run/.playwright/package"
exec "${N}/node-bin/node" "${N}/netspy.js" "$@"
