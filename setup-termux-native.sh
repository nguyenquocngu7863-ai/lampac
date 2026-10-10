#!/usr/bin/env bash
# Lampac NextGen — Native setup for Termux (Android, no proot at runtime).
#
# Dung khi code native hong ma can dung lai tu zero, hoac dung may moi.
# GitHub chi luu source + module (khong chua binary nang), nen script nay
# tai tao moi binary tu nguon tin cay:
#   dotnet   <- copy /opt/dotnet (proot Ubuntu) + patchelf interp/rpath
#   node     <- nodejs.org tarball v24.16.0 + patchelf interp (md5 khop 100%)
#   chrome   <- copy /opt/google/chrome (proot) + patchelf interp (md5 khop)
#   gst      <- copy /usr/bin/gst-* + scanner (proot) + patchelf interp
#   gio      <- copy libgiognutls.so (proot)
#   shim     <- build tu termux-native/src/*.c bang clang + glibc sysroot
#   packs    <- copy /opt/dotnet/packs (proot)
#   glibc links <- symlink $PREFIX/glibc/lib -> rootfs usr/lib/aarch64-linux-gnu
#   app      <- dotnet publish tu repo (Modules -> module/, giong build.sh)
#
# Usage:
#   bash setup-termux-native.sh                 # full tu zero (proot + native + app)
#   bash setup-termux-native.sh --only-native   # bo qua proot bootstrap
#   bash setup-termux-native.sh --only-app       # chi publish + deploy app
#   NODE_VERSION=24.16.0 bash setup-termux-native.sh   # doi ban node
#
set -euo pipefail

# ─── Config ──────────────────────────────────────────────────────────────────
NATIVE="$HOME/lampac-native"
RUN_DIR="$HOME/lampac-run"
REPO="$HOME/lampac"
SRC="$REPO/termux-native"
R=/data/data/com.termux/files/usr/var/lib/proot-distro/containers/ubuntu/rootfs
GLIBC_LD="$PREFIX/glibc/bin/ld.so"
GLIBC_LIB="$PREFIX/glibc/lib"
SYSROOT_LIBS="$R/usr/lib/aarch64-linux-gnu:$R/lib/aarch64-linux-gnu"
NODE_VERSION="${NODE_VERSION:-24.16.0}"
LISTEN_PORT="${LAMPAC_PORT:-9118}"

MODE="full"
[[ "${1:-}" == "--only-native" ]] && MODE="native"
[[ "${1:-}" == "--only-app" ]] && MODE="app"
[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { MODE="help"; }

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; RESET='\033[0m'
info() { printf "  ${CYAN}→${RESET}  %s\n" "$*"; }
ok()   { printf "  ${GREEN}✓${RESET}  %s\n" "$*"; }
warn() { printf "  ${YELLOW}⚠${RESET}  %s\n" "$*" >&2; }
err()  { printf "  ${RED}✗${RESET}  %s\n" "$*" >&2; }
die()  { err "$*"; exit 1; }

[[ -z "${PREFIX:-}" ]] && die "Chay trong Termux."
[[ "$(uname -m)" == "aarch64" ]] || die "Chi ho tro aarch64."

if [[ "$MODE" == "help" ]]; then
    echo "Usage: bash setup-termux-native.sh [--only-native|--only-app]"
    echo "  (khong doi so = full tu zero)"
    exit 0
fi

# ─── Step 0: Termux packages ─────────────────────────────────────────────────
step_termux_pkgs() {
    info "Termux packages (glibc, clang, proot-distro, patchelf)..."
    pkg update -y -o Dpkg::Options::="--force-confdef" 2>/dev/null || pkg update -y
    pkg install -y glibc-repo 2>/dev/null || true
    pkg install -y glibc glibc-runner clang proot-distro git curl patchelf 2>/dev/null \
        || pkg install -y glibc glibc-runner clang proot-distro git curl
    [[ -x "$GLIBC_LD" ]] || die "Thieu $GLIBC_LD"
    command -v clang >/dev/null || die "Thieu clang"
    command -v patchelf >/dev/null || command -v "$PREFIX/glibc/bin/patchelf" >/dev/null \
        || die "Thieu patchelf"
    ok "Termux packages OK"
}

# ─── Step 1: proot Ubuntu + dotnet SDK + chrome + gst (nguon copy) ────────────
step_proot() {
    info "proot Ubuntu..."
    proot-distro login ubuntu -- ls / >/dev/null 2>&1 || proot-distro install ubuntu
    ok "Ubuntu OK"

    info ".NET 10 SDK trong proot (dung de publish app)..."
    proot-distro login ubuntu -- bash -c '
        set -euo pipefail
        export DEBIAN_FRONTEND=noninteractive
        if [[ ! -x /opt/dotnet/dotnet ]] || ! /opt/dotnet/dotnet --list-sdks 2>/dev/null | grep -q "^10"; then
            apt-get update -qq
            apt-get install -y -qq curl
            curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
            chmod +x /tmp/dotnet-install.sh
            bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet 2>&1 | tail -2
            rm -f /tmp/dotnet-install.sh
            ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet 2>/dev/null || true
        fi
        /opt/dotnet/dotnet --list-sdks
    '
    ok ".NET SDK OK"

    info "GStreamer + Chrome trong proot (nguon copy cho native)..."
    proot-distro login ubuntu -- bash -c '
        set -euo pipefail
        export DEBIAN_FRONTEND=noninteractive
        apt-get update -qq
        apt-get install -y -qq gstreamer1.0-tools gstreamer1.0-plugins-base-apps \
            libgstreamer1.0-0 libgstreamer-plugins-base1.0-0 \
            gstreamer1.0-plugins-base gstreamer1.0-plugins-good \
            gstreamer1.0-plugins-bad gstreamer1.0-plugins-ugly \
            gstreamer1.0-libav ca-certificates curl 2>&1 | tail -1
        if [[ ! -x /opt/google/chrome/chrome && ! -x /usr/bin/google-chrome-stable && ! -x /usr/bin/chromium ]]; then
            arch=$(dpkg --print-architecture)
            case "$arch" in
                arm64) url="https://dl.google.com/linux/direct/google-chrome-stable_current_arm64.deb" ;;
                amd64) url="https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb" ;;
                *) echo "arch khong ho tro: $arch" >&2; exit 2 ;;
            esac
            curl -fL --retry 3 "$url" -o /tmp/google-chrome.deb
            apt-get install -y /tmp/google-chrome.deb 2>&1 | tail -1
            rm -f /tmp/google-chrome.deb
        fi
        google-chrome-stable --version 2>/dev/null || chromium --version 2>/dev/null || true
    '
    ok "GStreamer + Chrome OK"
}

# ─── Step 2: glibc symlinks (989 .so tu rootfs, vo hai voi proot) ─────────────
step_glibc_links() {
    info "Symlink $GLIBC_LIB -> rootfs usr/lib/aarch64-linux-gnu ..."
    n=0
    for f in "$R/usr/lib/aarch64-linux-gnu"/lib*.so*; do
        base="$(basename "$f")"
        if [[ ! -e "$GLIBC_LIB/$base" ]]; then
            ln -sf "$f" "$GLIBC_LIB/$base" && n=$((n + 1))
        fi
    done
    ok "glibc links OK (them $n moi)"
}

# ─── Step 3: seccomp shim + ca-redirect (build tu source) ─────────────────────
step_shim() {
    info "Build libseccomp-shim.so tu $SRC/src ..."
    [[ -f "$SRC/src/libseccomp-shim.c" ]] || die "Thieu $SRC/src/libseccomp-shim.c"
    [[ -f "$SRC/src/ca-redirect.c" ]] || die "Thieu $SRC/src/ca-redirect.c"
    mkdir -p "$NATIVE"
    clang --target=aarch64-linux-gnu --sysroot="$PREFIX/glibc" \
        -shared -fPIC -O2 -nostdlib \
        -o "$NATIVE/libseccomp-shim.so" \
        "$SRC/src/libseccomp-shim.c" "$SRC/src/ca-redirect.c" \
        || die "Build shim that bai"
    ok "shim OK ($(stat -c%s "$NATIVE/libseccomp-shim.so") bytes)"
}

# ─── Step 4: dotnet-host (copy + patchelf + symlink sdk/shared/packs) ─────────
step_dotnet_host() {
    info "Dung dotnet-host..."
    DH="$NATIVE/dotnet-host"
    mkdir -p "$DH/host" "$DH/metadata"
    cp "$R/opt/dotnet/dotnet" "$DH/dotnet"
    chmod +x "$DH/dotnet"
    patchelf --set-interpreter "$GLIBC_LD" \
        --set-rpath "$GLIBC_LIB:$SYSROOT_LIBS" "$DH/dotnet"
    ln -sfn "$R/opt/dotnet/host/fxr" "$DH/host/fxr"
    ln -sfn "$R/opt/dotnet/shared" "$DH/shared"
    ln -sfn "$R/opt/dotnet/sdk" "$DH/sdk"
    if [[ ! -d "$DH/packs/Microsoft.NETCore.App.Ref" ]]; then
        cp -r "$R/opt/dotnet/packs" "$DH/packs"
    fi
    LD_PRELOAD="$NATIVE/libseccomp-shim.so" DOTNET_ROOT="$R/opt/dotnet" \
        "$DH/dotnet" --list-runtimes | head -3
    ok "dotnet-host OK ($("$DH/dotnet" --version 2>/dev/null || echo no-ver))"
}

# ─── Step 5: node glibc (nodejs.org tarball + patchelf) ───────────────────────
step_node() {
    info "Node v$NODE_VERSION (glibc)..."
    NB="$NATIVE/node-bin"
    if [[ -x "$NB/node" ]] && "$NB/node" --version 2>/dev/null | grep -q "$NODE_VERSION"; then
        ok "node $NODE_VERSION da co"
        return 0
    fi
    mkdir -p "$NB"
    T="$(mktemp -d "${TMPDIR:-$HOME}/node-dl-XXXXXX")"
    trap 'rm -rf "$T"' EXIT
    curl -fSL --retry 3 \
        "https://nodejs.org/download/release/v$NODE_VERSION/node-v$NODE_VERSION-linux-arm64.tar.xz" \
        -o "$T/node.tar.xz"
    tar -xf "$T/node.tar.xz" -C "$T"
    cp "$T/node-v$NODE_VERSION-linux-arm64/bin/node" "$NB/node"
    chmod +x "$NB/node"
    patchelf --set-interpreter "$GLIBC_LD" "$NB/node"
    rm -rf "$T"
    trap - EXIT
    "$NB/node" --version
    ok "node OK"
}

# ─── Step 6: chrome (copy + patchelf + symlink assets) ─────────────────────────
step_chrome() {
    info "Chrome native..."
    CH="$NATIVE/chrome"
    mkdir -p "$CH"
    if [[ -x "$R/opt/google/chrome/chrome" ]]; then
        SRC_CHROME="$R/opt/google/chrome"
    else
        die "Khong thay chrome trong proot (/opt/google/chrome)"
    fi
    if [[ ! -f "$CH/chrome" ]] || ! cmp -s <(head -c 1048576 "$SRC_CHROME/chrome") <(head -c 1048576 "$CH/chrome" 2>/dev/null) 2>/dev/null; then
        info "  copy chrome (275MB, cho chut)..."
        cp "$SRC_CHROME/chrome" "$CH/chrome"
        chmod +x "$CH/chrome"
        patchelf --set-interpreter "$GLIBC_LD" "$CH/chrome"
    fi
    n=0
    for f in "$SRC_CHROME"/*; do
        base="$(basename "$f")"
        [[ "$base" == "chrome" ]] && continue
        if [[ ! -e "$CH/$base" ]]; then
            ln -sf "$f" "$CH/$base" && n=$((n + 1))
        fi
    done
    cp -f "$SRC/chrome-run.sh" "$CH/chrome-run.sh"
    chmod +x "$CH/chrome-run.sh"
    ok "chrome OK (them $n symlink)"
}

# ─── Step 7: gst bins + scanner + gio + opencl ─────────────────────────────────
step_gst() {
    info "GStreamer native bins..."
    mkdir -p "$NATIVE/bin" "$NATIVE/gio-modules" "$NATIVE/opencl"
    for b in gst-discoverer-1.0 gst-inspect-1.0 gst-launch-1.0; do
        if [[ ! -x "$NATIVE/bin/$b" ]] || ! cmp -s "$R/usr/bin/$b" "$NATIVE/bin/$b"; then
            cp "$R/usr/bin/$b" "$NATIVE/bin/$b"
            chmod +x "$NATIVE/bin/$b"
            patchelf --set-interpreter "$GLIBC_LD" "$NATIVE/bin/$b"
        fi
    done
    SCANNER="$R/usr/lib/aarch64-linux-gnu/gstreamer1.0/gstreamer-1.0/gst-plugin-scanner"
    if [[ ! -x "$NATIVE/gst-plugin-scanner" ]] || ! cmp -s "$SCANNER" "$NATIVE/gst-plugin-scanner"; then
        cp "$SCANNER" "$NATIVE/gst-plugin-scanner"
        chmod +x "$NATIVE/gst-plugin-scanner"
        patchelf --set-interpreter "$GLIBC_LD" "$NATIVE/gst-plugin-scanner"
    fi
    cp -f "$R/usr/lib/aarch64-linux-gnu/gio/modules/libgiognutls.so" "$NATIVE/gio-modules/" 2>/dev/null \
        || warn "khong copy duoc libgiognutls.so"
    cp -f "$SRC/adreno.icd" "$NATIVE/opencl/adreno.icd"
    ok "gst + gio + opencl OK"
}

# ─── Step 8: scripts (launcher, csc, dotnet wrapper, resolver) ────────────────
step_scripts() {
    info "Scripts..."
    mkdir -p "$NATIVE/csc" "$NATIVE/tmp"
    cp -f "$REPO/termux-run-lampac.sh" "$NATIVE/run-lampac.sh"
    chmod +x "$NATIVE/run-lampac.sh"
    cp -f "$SRC/csc" "$NATIVE/csc/csc"
    chmod +x "$NATIVE/csc/csc"
    cp -f "$SRC/run-resolver.sh" "$NATIVE/run-resolver.sh"
    chmod +x "$NATIVE/run-resolver.sh"
    cp -f "$SRC/dotnet-wrapper.sh" "$PREFIX/bin/dotnet"
    chmod +x "$PREFIX/bin/dotnet"
    cat > "$PREFIX/bin/lampac-native" <<'SHORTCUT'
#!/usr/bin/env bash
case "${1:-}" in
    start)   exec bash "$HOME/lampac-native/run-lampac.sh" ;;
    stop)
        if pkill -TERM -f '[C]ore\.dll' 2>/dev/null; then
            sleep 1; pkill -KILL -f '[C]ore\.dll' 2>/dev/null || true
            echo "Lampac native stopped"
        else echo "Lampac native not running"; fi ;;
    status)  pgrep -f '[C]ore\.dll' >/dev/null 2>&1 && echo "Lampac native is running" || echo "Lampac native not running" ;;
    *) echo "Usage: lampac-native {start|stop|status}"; exit 2 ;;
esac
SHORTCUT
    chmod +x "$PREFIX/bin/lampac-native"
    ok "scripts OK (run-lampac.sh, csc, dotnet, lampac-native)"
}

# ─── Step 9: publish app tu repo -> lampac-run ─────────────────────────────────
step_app() {
    info "Publish app tu repo (Modules -> module/, giong build.sh)..."
    [[ -d "$REPO/Core" ]] || die "Khong thay repo $REPO (clone truoc da)"
    STAGING="$(mktemp -d "${TMPDIR:-$HOME}/lampac-pub-XXXXXX")"
    # shellcheck disable=SC2064
    trap "rm -rf '$STAGING'" EXIT
    proot-distro login ubuntu -- bash -c "
        set -euo pipefail
        cd '$REPO'
        export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GCHeapHardLimit=200000000
        dotnet publish Core/Core.csproj -c Release --self-contained false -o '$STAGING' 2>&1 | tail -3
    "
    [[ -f "$STAGING/Core.dll" ]] || die "Publish that bai (thieu Core.dll)"
    mkdir -p "$RUN_DIR"
    # Bao ve du lieu nguoi dung: init/passwd/data/db/cache/logs + wwwroot custom
    # (vi.js) + .playwright/node + module ngoai repo (mods/).
    if command -v rsync >/dev/null 2>&1; then
        rsync -a --delete \
            --exclude 'init.conf*' --exclude 'passwd' \
            --exclude 'data/' --exclude 'database/' --exclude 'cache/' \
            --exclude 'logs/' --exclude 'tmp/' --exclude '*.log' \
            --exclude 'wwwroot/' --exclude 'mods/' --exclude '.playwright/node/' \
            "$STAGING/" "$RUN_DIR/"
    else
        warn "khong co rsync, dung cp (file cu thua khong duoc xoa)"
        ( shopt -s dotglob
          for f in "$STAGING"/*; do
              b="$(basename "$f")"
              case "$b" in
                  wwwroot) [[ -d "$RUN_DIR/wwwroot" ]] || cp -a "$f" "$RUN_DIR/" ;;
                  .playwright) mkdir -p "$RUN_DIR/.playwright"
                      cp -a "$f/package" "$RUN_DIR/.playwright/" 2>/dev/null || true ;;
                  *) cp -a "$f" "$RUN_DIR/" ;;
              esac
          done )
    fi
    rm -rf "$STAGING"
    trap - EXIT
    ok "app deployed: $(md5sum "$RUN_DIR/Core.dll" | cut -c1-8) Core / $(md5sum "$RUN_DIR/Shared.dll" | cut -c1-8) Shared"

    # init.conf: seed neu chua co, luon ep executablePath native
    if [[ ! -f "$RUN_DIR/init.conf" ]]; then
        cp "$REPO/config/termux-recovery.init.conf" "$RUN_DIR/init.conf"
        info "seed init.conf tu config/termux-recovery.init.conf"
    fi
    python3 - "$RUN_DIR/init.conf" <<'PY'
import json, sys
p = sys.argv[1]
d = json.load(open(p))
d.setdefault("chromium", {}).update({
    "enable": True, "Headless": True,
    "executablePath": __import__("os").path.expanduser("~/lampac-native/chrome/chrome-run.sh"),
})
json.dump(d, open(p, "w"), indent=2, ensure_ascii=False)
print("chromium.executablePath -> native chrome-run.sh")
PY
}

# ─── Step 10: Playwright browsers (headless shell + ffmpeg) ───────────────────
step_browsers() {
    info "Playwright browsers..."
    export PLAYWRIGHT_BROWSERS_PATH="$R/root/.cache/ms-playwright"
    if [[ -d "$PLAYWRIGHT_BROWSERS_PATH/chromium_headless_shell-"* ]]; then
        ok "browsers da co"
        return 0
    fi
    CLI="$RUN_DIR/.playwright/package/cli.js"
    [[ -f "$CLI" ]] || { warn "thieu $CLI, bo qua (browser se tai khi chay)"; return 0; }
    LD_PRELOAD="$NATIVE/libseccomp-shim.so" \
        "$NATIVE/node-bin/node" "$CLI" install chromium-headless-shell ffmpeg 2>&1 | tail -3 \
        || warn "tai browsers that bai, thu lai sau khi start"
    ok "browsers OK"
}

# ─── Main ─────────────────────────────────────────────────────────────────────
info "Lampac native setup (mode=$MODE, node=$NODE_VERSION)"
if [[ "$MODE" == "full" ]]; then
    step_termux_pkgs
    step_proot
    step_glibc_links
    step_shim
    step_dotnet_host
    step_node
    step_chrome
    step_gst
    step_scripts
elif [[ "$MODE" == "native" ]]; then
    step_termux_pkgs
    step_glibc_links
    step_shim
    step_dotnet_host
    step_node
    step_chrome
    step_gst
    step_scripts
fi
step_app
step_browsers
echo ""
ok "XONG. Chay:  lampac-native start   (dung: lampac-native stop)"
