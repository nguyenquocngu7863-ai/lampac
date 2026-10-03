#!/data/data/com.termux/files/usr/bin/bash
# Lampac native launcher (Termux glibc, no proot).
#
# SOURCE OF TRUTH — this file is tracked in the repo so the env fixes survive a
# native reinstall. ~/lampac-native/ is NOT a git repo, so if you only edit the
# deployed copy the change is lost the next time native is rebuilt.
#
# Deploy after editing:
#   cp ~/lampac/termux-run-lampac.sh ~/lampac-native/run-lampac.sh
#   chmod +x ~/lampac-native/run-lampac.sh
#
# Usage: run-lampac.sh  (foreground, Ctrl+C to stop)
set -euo pipefail

R=/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs
LAMPAC_DIR="$HOME/lampac-run"
NATIVE="$HOME/lampac-native"

unset LD_PRELOAD
export LD_PRELOAD="$HOME/lampac-native/libseccomp-shim.so"
# NOTE: no LD_LIBRARY_PATH here on purpose — it leaks into bionic children
# (chrome wrapper's bash shebang) and kills them. dotnet finds Ubuntu libs
# (libicu) via RUNPATH baked into dotnet-host/dotnet; chrome-run.sh sets
# LD_LIBRARY_PATH itself after its bionic bash has started.
# LD_LIBRARY_PATH is safe again: chrome-run.sh / run-resolver.sh now use a
# glibc-bash shebang (bionic bash dies on glibc libc in path). Order matters:
# Termux prefix first, then vendor (Adreno OpenCL stack), then Ubuntu sysroot.
export LD_LIBRARY_PATH="$PREFIX/glibc/lib:/vendor/lib64:$R/usr/lib/aarch64-linux-gnu:$R/lib/aarch64-linux-gnu"
# Adreno OpenCL via ocl-icd (kgsl is readable; mesa/DRI is not)
export OCL_ICD_FILENAMES="$NATIVE/opencl/adreno.icd"
export DOTNET_ROOT="$R/opt/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
export PLAYWRIGHT_BROWSERS_PATH="$R/root/.cache/ms-playwright"
# Playwright's driver builds an artifacts dir with mkdtemp(os.tmpdir()+...).
# Node falls back to /tmp when TMPDIR is unset — but Android has no /tmp, so
# launch dies with "ENOENT ... mkdtemp '/tmp/playwright-artifacts-XXXX'" and
# Chromium never starts (every Chrome module 503s). Pin it to a dir we own so
# startup does not depend on who launched the server.
#   Repro: env -u TMPDIR node -e 'require("fs").mkdtempSync("/tmp/z-")'  → ENOENT
export TMPDIR="$NATIVE/tmp"; export TMP="$TMPDIR"; export TEMP="$TMPDIR"
mkdir -p "$TMPDIR"
# GStreamer (Ubuntu plugins via rootfs sysroot, patchelf'd scanner)
export GST_PLUGIN_SYSTEM_PATH="$R/usr/lib/aarch64-linux-gnu/gstreamer-1.0"
export GST_PLUGIN_SCANNER="$NATIVE/gst-plugin-scanner"
export GST_PLUGIN_SCANNER_1_0="$NATIVE/gst-plugin-scanner"
export GST_REGISTRY="$NATIVE/gst-registry.bin"
# GIO TLS backend (libgiognutls) + CA redirect (see ca-redirect.c) for soup https
export GIO_MODULE_DIR="$NATIVE/gio-modules"
# native helpers first (gst-discoverer-1.0, …) — PATH lookup, no rebuild needed
export PATH="$NATIVE/bin:$PATH"
# data dir for modules that log to disk (HeoVl…); same dir, single source
export LAMPAC_DATA="$HOME/lampac-run"
# glibc node for module-spawned JS (Po85 resolver, DevFetch sniff);
# bionic $PREFIX/bin/node reports platform=android and breaks playwright
export LAMPAC_NODE="$NATIVE/node-bin/node"
# cap managed heap so spikes get GC'd instead of growing into swap (tune freely)
export DOTNET_GCHeapHardLimit="${DOTNET_GCHeapHardLimit:-600000000}"

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