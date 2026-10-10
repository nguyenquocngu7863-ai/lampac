#!/data/data/com.termux/files/usr/glibc/bin/bash
# Po85 uhd-resolver native launcher (glibc node, no proot).
# Lampac module only HTTP-calls localhost:9196, so run this when you use Po85.
# Usage: run-resolver.sh [--port 9196]
set -euo pipefail

R=/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs
UHD="$HOME/lampac-run/module/Adult/Po85/uhd"
NATIVE="$HOME/lampac-native"

unset LD_PRELOAD
export LD_PRELOAD="$HOME/lampac-native/libseccomp-shim.so"
# NOTE: no LD_LIBRARY_PATH (same reason as run-lampac.sh — node needs none,
# chrome-run.sh sets its own).
export PLAYWRIGHT_BROWSERS_PATH="$R/root/.cache/ms-playwright"
export CHROME_BIN="$NATIVE/chrome/chrome-run.sh"

cd "$UHD"
exec "$NATIVE/node-bin/node" "$UHD/resolver.js" "$@"
