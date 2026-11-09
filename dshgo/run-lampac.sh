#!/usr/bin/bash
# Lampac launcher — dsh-go proot (Ubuntu 24.04 arm64), no Termux paths.
# Patched 2026-11-10: dotnet/chrome/gst all run on the host glibc 2.39.
set -euo pipefail

# MissAVTaxCache writes to $HOME/lampac-run/cache/missav-tax.json
export HOME=/root/lampac

LAMPAC_DIR=/root/lampac/lampac-run
NATIVE=/root/lampac/lampac-native
CHROME_DIR=/root/lampac-test/chrome

export DOTNET_ROOT="$NATIVE/dotnet-host"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
export DOTNET_GCHeapHardLimit="${DOTNET_GCHeapHardLimit:-600000000}"

# No LD_PRELOAD / LD_LIBRARY_PATH: host glibc already provides everything.
unset LD_PRELOAD LD_LIBRARY_PATH || true

# Temp dir (Android/proot has no usable /tmp)
export TMPDIR="$NATIVE/tmp"; export TMP="$TMPDIR"; export TEMP="$TMPDIR"
mkdir -p "$TMPDIR"

# GStreamer from apt Ubuntu 1.24.2 (Termux gst binaries are version-mismatched)
export GST_PLUGIN_SYSTEM_PATH=/usr/lib/aarch64-linux-gnu/gstreamer-1.0
export GST_PLUGIN_SCANNER=/usr/lib/aarch64-linux-gnu/gstreamer1.0/gst-plugin-scanner
export GST_PLUGIN_SCANNER_1_0="$GST_PLUGIN_SCANNER"
export GST_REGISTRY="$NATIVE/gst-registry.bin"
export GIO_MODULE_DIR="$NATIVE/gio-modules"
# gst tools come from apt Ubuntu 1.24.2; Termux $NATIVE/bin binaries are ABI-mismatched
# curl shim (curl_cffi impersonate) MUST come first for MissAV surrit.com
export PATH="$NATIVE/bin:/usr/bin:/bin:$PATH"
export LAMPAC_DATA="$LAMPAC_DIR"

# Chrome: patched ELF, run directly (no Termux chrome-run.sh wrapper)
export CHROME_BIN="$CHROME_DIR/chrome"
export PLAYWRIGHT_BROWSERS_PATH="$CHROME_DIR"

# Node for module-spawned JS (use host node, not Termux node-bin)
export LAMPAC_NODE="$(command -v node)"
export PLAYWRIGHT_NODEJS_PATH="$LAMPAC_NODE"

export XNHAU_RELAY="${XNHAU_RELAY-https://xn-relay.nguyenquocngu7863.workers.dev}"
export JAVGURU_RELAY="${JAVGURU_RELAY-https://xn-relay.nguyenquocngu7863.workers.dev}"
export VNCCHICH2_RELAY="${VNCCHICH2_RELAY-https://xn-relay.nguyenquocngu7863.workers.dev}"

PORT=$(grep -o '"port": *[0-9]*' "$LAMPAC_DIR/init.conf" 2>/dev/null | head -1 | grep -o '[0-9]*' || echo 9118)
echo ""
echo "  Lampac NextGen (native, no proot)"
echo "  ─────────────────────────────────"
echo "  Local:  http://localhost:$PORT"
echo "  App:    $LAMPAC_DIR"
echo "  Stop:   Ctrl+C (rollback: lampac start)"
echo ""

cd "$LAMPAC_DIR"
exec "$NATIVE/dotnet-host/dotnet" "$LAMPAC_DIR/Core.dll" "$@"