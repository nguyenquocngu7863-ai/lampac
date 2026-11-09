#!/usr/bin/bash
# Lampac NextGen — setup cho dsh-go (Ubuntu 24.04 glibc native, KHONG proot, KHONG Termux)
#
# Khac setup-termux-native.sh: bo qua toan bo phan Termux (proot, glibc links,
# libseccomp-shim, patchelf dotnet/node/gst) vi dsh-go la Ubuntu that:
#   dotnet  <- apt dotnet-sdk-10.0
#   chrome  <- google-chrome-stable arm64 .deb (native, khong can patchelf)
#   gst     <- apt gstreamer1.0-*
#   node    <- da co san /usr/local/bin/node
#   curl    <- apt curl + shim curl_cffi (bypass Cloudflare)
#
# Usage:
#   bash dshgo/setup.sh              # cai tu dau (apt + publish + deploy)
#   bash dshgo/setup.sh --only-env   # chi apt/pip/shim, khong publish
#   bash dshgo/setup.sh --only-app   # chi publish + deploy
#   bash dshgo/setup.sh --sync       # chi sync module tu GitHub
set -uo pipefail

REPO="${LAMPAC_REPO:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." >/dev/null 2>&1 && pwd)}"
RUN_DIR="${LAMPAC_RUN:-$HOME/lampac/lampac-run}"
NATIVE="${LAMPAC_NATIVE:-$HOME/lampac/lampac-native}"
CHROME_DIR="${LAMPAC_CHROME:-$HOME/lampac-test/chrome}"
LISTEN_PORT="${LAMPAC_PORT:-9119}"
SOURCE_BASE="${LAMPAC_CUSTOM_SOURCE_BASE:-https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a0673e-lampac}"

MODE="full"
case "${1:-}" in
  --only-env) MODE="env" ;;
  --only-app) MODE="app" ;;
  --sync)     MODE="sync" ;;
  -h|--help)  MODE="help" ;;
esac

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; RESET='\033[0m'
info() { printf "  ${CYAN}→${RESET}  %s\n" "$*"; }
ok()   { printf "  ${GREEN}✓${RESET}  %s\n" "$*"; }
warn() { printf "  ${YELLOW}⚠${RESET}  %s\n" "$*" >&2; }
err()  { printf "  ${RED}✗${RESET}  %s\n" "$*" >&2; }
die()  { err "$*"; exit 1; }

if [[ "$MODE" == "help" ]]; then
    cat <<EOF
Usage: bash dshgo/setup.sh [OPTION]

  (khong doi so)   Cai day du: apt + chrome + launcher (KHONG build)
  --only-env       Chi cai moi truong (apt, pip curl_cffi, shim, symlink)
  --only-app       Publish app (chi can khi sua Core/Shared, ton RAM)
  --sync           Chi sync module tu GitHub

Env:
  LAMPAC_PORT              port (default 9119)
  LAMPAC_CUSTOM_SOURCE_BASE  nguon sync module
EOF
    exit 0
fi

[[ "$(uname -m)" == "aarch64" ]] || die "Chi ho tro aarch64."
command -v apt-get >/dev/null || die "Khong phai Ubuntu (thieu apt-get)."

# ─── Step 1: apt packages ────────────────────────────────────────────────────
step_apt() {
    info "apt packages..."
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq
    apt-get install -y --no-install-recommends \
        dotnet-sdk-10.0 ffmpeg curl patchelf rsync unzip \
        libnspr4 libnss3 libatk1.0-0t64 libatk-bridge2.0-0t64 libcups2t64 \
        libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 libxfixes3 libxrandr2 \
        libgbm1 libasound2t64 libpango-1.0-0 libcairo2 libatspi2.0-0t64 \
        libx11-6 libxext6 libxcb1 libdbus-1-3 libexpat1 libfontconfig1 libglib2.0-0t64 \
        gstreamer1.0-tools gstreamer1.0-plugins-base gstreamer1.0-plugins-good \
        gstreamer1.0-plugins-bad gstreamer1.0-plugins-ugly gstreamer1.0-libav \
        gstreamer1.0-plugins-base-apps libgnutls30 2>&1 | tail -2
    ok "apt OK — $(dotnet --version 2>/dev/null || echo 'dotnet chua san')"
}

# ─── Step 2: curl_cffi + shim (bypass Cloudflare) ────────────────────────────
step_curl_cffi() {
    info "curl_cffi (bypass Cloudflare TLS fingerprint)..."
    if python3 -c "import curl_cffi" 2>/dev/null; then
        ok "curl_cffi da co"
    else
        pip install --index-url https://mirrors.aliyun.com/pypi/simple/ curl_cffi 2>&1 | tail -2 \
          || pip install curl_cffi 2>&1 | tail -2
    fi
    mkdir -p "$NATIVE/bin"
    cp -f "$REPO/dshgo/curl-shim" "$NATIVE/bin/curl"
    chmod +x "$NATIVE/bin/curl"
    ok "curl shim -> $NATIVE/bin/curl"
}

# ─── Step 3: node/npm symlink (Po85 ModInit doi hoi /usr/bin/node) ───────────
step_node_links() {
    info "symlink node/npm vao /usr/bin..."
    command -v node >/dev/null || die "Khong thay node"
    ln -sf "$(command -v node)" /usr/bin/node
    command -v npm >/dev/null && ln -sf "$(command -v npm)" /usr/bin/npm
    ok "node $(node --version) / npm $(npm --version 2>/dev/null || echo n/a)"
}

# ─── Step 4: Chrome native tu Google (khong patchelf) ────────────────────────
step_chrome() {
    info "Chrome native (arm64 deb tu Google)..."
    if command -v google-chrome-stable >/dev/null 2>&1; then
        ok "google-chrome-stable da co: $(google-chrome-stable --version 2>/dev/null)"
        CHROME_BIN="$(command -v google-chrome-stable)"
    else
        T="$(mktemp -d)"
        info "  tai google-chrome-stable_current_arm64.deb..."
        curl -fL --retry 3 --max-time 600 \
            "https://dl.google.com/linux/direct/google-chrome-stable_current_arm64.deb" \
            -o "$T/chrome.deb" || die "tai chrome that bai"
        apt-get install -y "$T/chrome.deb" 2>&1 | tail -2
        rm -rf "$T"
        CHROME_BIN="$(command -v google-chrome-stable)"
        ok "chrome: $($CHROME_BIN --version)"
    fi
    # tro CHROME_BIN ve cho launcher dung
    mkdir -p "$CHROME_DIR"
    ln -sfn "$CHROME_BIN" "$CHROME_DIR/chrome"
    ok "chrome -> $CHROME_DIR/chrome"
}

# ─── Step 5: publish app (TUY CHON — thuong KHONG can) ──────────────────────
# Core tu compile module bang Roslyn luc start (xem skill lampac-build §3).
# Chi can publish khi sua Core/ hoac Shared/. Mobile: tranh build vi ton RAM.
step_publish() {
    info "publish app tu repo..."
    [[ -d "$REPO/Core" ]] || die "Khong thay $REPO/Core (clone repo truoc)"
    STAGING="$(mktemp -d)"
    info "  dotnet publish (co the mat vai phut)..."
    ( cd "$REPO" && \
      DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GCHeapHardLimit=200000000 \
      dotnet publish Core/Core.csproj -c Release --self-contained false -o "$STAGING" 2>&1 | tail -3 )
    [[ -f "$STAGING/Core.dll" ]] || die "publish that bai (thieu Core.dll)"

    mkdir -p "$RUN_DIR"
    # Giu nguyen du lieu nguoi dung
    rsync -a --delete \
        --exclude 'init.conf*' --exclude 'passwd' --exclude 'current.conf*' \
        --exclude 'data/' --exclude 'database/' --exclude 'cache/' \
        --exclude 'logs/' --exclude 'tmp/' --exclude '*.log' \
        --exclude 'wwwroot/' --exclude 'mods/' --exclude '.playwright/' \
        "$STAGING/" "$RUN_DIR/"
    rm -rf "$STAGING"
    ok "deploy: $(md5sum "$RUN_DIR/Core.dll" | cut -c1-8) Core / $(md5sum "$RUN_DIR/Shared.dll" | cut -c1-8) Shared"

    # init.conf: seed neu chua co, luon ep executablePath = chrome native
    if [[ ! -f "$RUN_DIR/init.conf" ]]; then
        cp "$REPO/config/termux-recovery.init.conf" "$RUN_DIR/init.conf" 2>/dev/null \
          || warn "khong co config/termux-recovery.init.conf"
    fi
    python3 - "$RUN_DIR/init.conf" "$CHROME_DIR/chrome" "$LISTEN_PORT" <<'PY'
import json, sys, os
p, chrome, port = sys.argv[1], sys.argv[2], int(sys.argv[3])
d = json.load(open(p))
d.setdefault("chromium", {}).update({
    "enable": True, "Headless": True, "executablePath": chrome,
})
d.setdefault("listen", {})["port"] = port
json.dump(d, open(p, "w"), indent=2, ensure_ascii=False)
print("  chromium.executablePath ->", chrome)
print("  listen.port ->", port)
PY
}

# ─── Step 6: launcher + playwright deps ──────────────────────────────────────
step_launcher() {
    info "launcher + playwright deps..."
    mkdir -p "$NATIVE"
    cp -f "$REPO/dshgo/run-lampac.sh" "$NATIVE/run-lampac.sh"
    chmod +x "$NATIVE/run-lampac.sh"

    # Po85 uhd resolver
    local uhd="$RUN_DIR/module/Adult/Po85/uhd"
    if [[ -d "$uhd" ]]; then
        ( cd "$uhd" && npm i playwright-core --no-audit --no-fund 2>&1 | tail -1 )
        ok "Po85 uhd resolver deps OK"
    else
        warn "chua co $uhd (Po85 4K resolver se khong chay)"
    fi
    ok "launcher -> $NATIVE/run-lampac.sh"
}

# ─── Step 7: sync module tu GitHub ───────────────────────────────────────────
step_sync() {
    info "sync module tu $SOURCE_BASE ..."
    [[ -f "$RUN_DIR/Core.dll" ]] || die "Chua cai Lampac. Chay khong co --sync truoc."
    local stamp; stamp="$(date +%s)"

    # Danh sach module can sync (nhung module khong co trong release zip)
    local mods=(
        "Adult/SexDep" "Adult/HeoVl" "Adult/Vlxx" "Adult/Po85" "Adult/MissAV"
        "Adult/Suong3x" "Adult/Jable" "Adult/JavGuru" "Adult/SupJav" "Adult/Spankbang"
        "OnlineRUS/VkMovie" "OnlineRUS/Gencit" "OnlineRUS/Videoseed" "OnlineRUS/FlixCDN"
    )
    local files=("manifest.json" "ModInit.cs" "Controller.cs" "Service.cs" "Model.cs" "ModuleConf.cs" "README.md")

    local n=0
    for mod in "${mods[@]}"; do
        local target="$RUN_DIR/module/$mod"
        [[ -d "$target" ]] || continue
        for f in "${files[@]}"; do
            if curl -fsSL --retry 2 --max-time 30 \
                "$SOURCE_BASE/Modules/$mod/$f?cb=$stamp" \
                -o "$target/$f.tmp" 2>/dev/null; then
                mv "$target/$f.tmp" "$target/$f"
                n=$((n+1))
            else
                rm -f "$target/$f.tmp"
            fi
        done
    done
    ok "sync xong ($n file). Restart: $NATIVE/run-lampac.sh"
}

# ─── Main ────────────────────────────────────────────────────────────────────
info "Lampac dsh-go setup (mode=$MODE)"
case "$MODE" in
    env)
        step_apt; step_curl_cffi; step_node_links; step_chrome
        ;;
    app)
        step_publish; step_launcher
        ;;
    sync)
        step_sync
        ;;
    *)
        # KHONG goi step_publish: Core tu compile module bang Roslyn luc start.
        # Chi publish khi sua Core/Shared: bash dshgo/setup.sh --only-app
        step_apt; step_curl_cffi; step_node_links; step_chrome
        step_launcher
        ;;
esac
echo ""
ok "XONG."
info "Chay: $NATIVE/run-lampac.sh   (dung: pkill -f Core.dll)"
