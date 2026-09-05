#!/usr/bin/env bash
#
# Lampac NextGen — Installer for Termux (Android)
# Uses proot-distro (Ubuntu) for .NET runtime compatibility
#
# Usage:
#   bash setup-termux.sh            # install & run
#   bash setup-termux.sh --install  # install only
#   bash setup-termux.sh --run      # run only (skip install)
#   bash setup-termux.sh --sync     # curl only the latest patch files (fast)
#   bash setup-termux.sh --sync-all # repair browser runtime + apply every custom module
#   bash setup-termux.sh --update   # update to latest release
#
set -euo pipefail

# ─── Config ──────────────────────────────────────────────────────────────────

LAMPAC_DIR="$HOME/lampac"
LISTEN_PORT="${LAMPAC_PORT:-9118}"
ROOT_PASSWORD="${LAMPAC_PASSWD:-lampac}"
# Custom modules maintained in this repository. Override when using a private fork.
# Mặc định trỏ nhánh làm việc của bản tuỳ biến (AdminPanel phân nhóm + tìm kiếm,
# module ENG/VN, bản sửa `lampac update`). Đặt LAMPAC_CUSTOM_SOURCE_BASE để đổi
# sang fork/nhánh khác mà không cần sửa file này.
CUSTOM_SOURCE_BASE="${LAMPAC_CUSTOM_SOURCE_BASE:-https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a0673e-lampac}"

MODE=""
[[ "${1:-}" == "--install" ]] && MODE="install"
[[ "${1:-}" == "--run" ]]    && MODE="run"
[[ "${1:-}" == "--sync" ]]   && MODE="sync"
[[ "${1:-}" == "--sync-all" ]] && MODE="sync-all"
[[ "${1:-}" == "--update" ]] && MODE="update"
[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && { MODE="help"; }

# ─── Colors ──────────────────────────────────────────────────────────────────

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
DIM='\033[2m'
RESET='\033[0m'

info()    { printf "  ${CYAN}→${RESET}  %s\n" "$*"; }
ok()      { printf "  ${GREEN}✓${RESET}  %s\n" "$*"; }
warn()    { printf "  ${YELLOW}⚠${RESET}  %s\n" "$*" >&2; }
err()     { printf "  ${RED}✗${RESET}  %s\n" "$*" >&2; }

banner() {
    echo ""
    printf "${CYAN}  ██╗      █████╗ ███╗   ███╗██████╗  █████╗  ██████╗\n"
    printf "  ██║     ██╔══██╗████╗ ████║██╔══██╗██╔══██╗██╔════╝\n"
    printf "  ██║     ███████║██╔████╔██║██████╔╝███████║██║\n"
    printf "  ██║     ██╔══██║██║╚██╔╝██║██╔═══╝ ██╔══██║██║\n"
    printf "  ███████╗██║  ██║██║ ╚═╝ ██║██║     ██║  ██║╚██████╗\n"
    printf "  ╚══════╝╚═╝  ╚═╝╚═╝     ╚═╝╚═╝     ╚═╝  ╚═╝ ╚═════╝\n"
    printf "                                          ${BOLD}NextGen${RESET}${CYAN} for Termux${RESET}\n\n"
}

# ─── Help ────────────────────────────────────────────────────────────────────

show_help() {
    banner
    printf "${BOLD}Usage:${RESET}  bash setup-termux.sh [OPTIONS]\n\n"
    printf "${BOLD}Options:${RESET}\n"
    printf "  ${GREEN}--install${RESET}    Install proot-distro + Ubuntu + .NET + Lampac\n"
    printf "  ${GREEN}--run${RESET}        Run Lampac (skip install)\n"
    printf "  ${GREEN}--sync${RESET}       Curl only the latest patch files (fast, no Chrome/hls.js)\n"
    printf "  ${GREEN}--sync-all${RESET}   Repair browser runtime + apply every custom module\n"
    printf "  ${GREEN}--update${RESET}     Update Lampac to latest release\n"
    printf "  ${GREEN}--help${RESET}       Show this help\n\n"
    printf "${BOLD}Environment:${RESET}\n"
    printf "  ${CYAN}LAMPAC_PORT${RESET}     Listen port (default: 9118)\n"
    printf "  ${CYAN}LAMPAC_PASSWD${RESET}   Root password (default: lampac)\n"
    printf "  ${CYAN}LAMPAC_CUSTOM_SOURCE_BASE${RESET} Raw-git base URL for --sync/--sync-all\n"
    printf "                      (default: .../lampac/arena/01a0673e-lampac;\n"
    printf "                      không dùng main — nhánh đó đang chậm hơn)\n\n"
    printf "${BOLD}How it works:${RESET}\n"
    printf "  This script installs Lampac inside proot-distro Ubuntu.\n"
    printf "  .NET 10 runs natively inside Ubuntu (glibc compatible).\n\n"
    printf "${BOLD}Examples:${RESET}\n"
    printf "  ${DIM}# First time setup${RESET}\n"
    printf "  bash setup-termux.sh\n\n"
    printf "  ${DIM}# Custom port${RESET}\n"
    printf "  LAMPAC_PORT=8080 bash setup-termux.sh\n\n"
    printf "  ${DIM}# Apply only the latest patch files (fast)${RESET}\n"
    printf "  bash setup-termux.sh --sync\n\n"
    printf "  ${DIM}# Re-apply every custom module + browser runtime${RESET}\n"
    printf "  bash setup-termux.sh --sync-all\n\n"

    printf "  ${DIM}# Install optional local services${RESET}\n"
    printf "  aio install\n"
    printf "  jackett install\n"
    printf "  lampac start\n\n"

    printf "  ${DIM}# Update Lampac${RESET}\n"
    printf "  bash setup-termux.sh --update\n\n"
}

# ─── Check Termux ────────────────────────────────────────────────────────────

check_termux() {
    if [[ -z "${PREFIX:-}" ]]; then
        err "This script must run inside Termux."
        err "Download Termux from https://f-droid.org/packages/com.termux/"
        exit 1
    fi
}

# ─── Step 1: Install Termux packages ────────────────────────────────────────

install_termux_deps() {
    info "Updating Termux packages..."
    pkg update -y -o Dpkg::Options::="--force-confdef" 2>/dev/null || pkg update -y

    info "Installing proot-distro..."
    if ! command -v proot-distro &>/dev/null; then
        pkg install -y proot-distro
    fi
    ok "proot-distro ready"

    info "Installing helper packages..."
    pkg install -y git curl wget 2>/dev/null || true
    ok "Termux packages ready"
}

# ─── Step 2: Install Ubuntu via proot-distro ─────────────────────────────────

install_ubuntu() {
    # Check if Ubuntu container already exists (try to login)
    if proot-distro login ubuntu -- ls / &>/dev/null; then
        ok "Ubuntu already installed via proot-distro"
        return 0
    fi

    # Container exists but broken — reset it
    if proot-distro list 2>/dev/null | grep -qi "ubuntu"; then
        warn "Ubuntu container broken — reinstalling..."
        proot-distro reset ubuntu
        ok "Ubuntu reinstalled"
        return 0
    fi

    info "Installing Ubuntu via proot-distro (this may take a few minutes)..."
    proot-distro install ubuntu
    ok "Ubuntu installed"
}

# ─── Step 3: Install .NET + Lampac inside Ubuntu ─────────────────────────────

install_lampac_in_ubuntu() {
    info "Installing .NET 10 runtime inside Ubuntu..."

    proot-distro login ubuntu -- bash -c '
        set -euo pipefail
        export DEBIAN_FRONTEND=noninteractive
        apt-get update -qq
        apt-get install -y -qq curl wget unzip libicu-dev libssl-dev \
            gstreamer1.0-tools gstreamer1.0-plugins-base-apps \
            libgstreamer1.0-0 libgstreamer-plugins-base1.0-0 \
            gstreamer1.0-plugins-base gstreamer1.0-plugins-good \
            gstreamer1.0-plugins-bad gstreamer1.0-plugins-ugly \
            gstreamer1.0-libav ocl-icd-libopencl1 > /dev/null 2>&1

        DOTNET_DIR="/opt/dotnet"
        if [[ ! -x "$DOTNET_DIR/dotnet" ]] || ! "$DOTNET_DIR/dotnet" --list-runtimes 2>/dev/null | grep -q "Microsoft.AspNetCore.App 10"; then
            echo "  Downloading .NET 10 ASP.NET Core runtime..."
            curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
            chmod +x /tmp/dotnet-install.sh
            bash /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir "$DOTNET_DIR" 2>&1 | tail -3
            rm -f /tmp/dotnet-install.sh
            echo "  .NET 10 runtime installed"
        else
            echo "  .NET 10 already installed"
        fi
        ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet 2>/dev/null || true
        echo "OK"
    '
    ok ".NET 10 runtime ready inside Ubuntu"

    info "Downloading Lampac NextGen release..."
    proot-distro login ubuntu -- bash -c '
        set -euo pipefail
        LAMPAC_DIR="/root/lampac"
        mkdir -p "$LAMPAC_DIR"
        cd /tmp
        for i in 1 2 3; do
            if curl -fSL --retry 2 -o lampac.zip "https://github.com/lampac-nextgen/lampac/releases/latest/download/lampac-nextgen.zip"; then
                break
            fi
            echo "  Attempt $i failed, retrying..."
            sleep 2
        done
        [[ ! -s lampac.zip ]] && echo "FAIL" && exit 1

        STAGING="/tmp/lampac-staging"
        rm -rf "$STAGING"
        mkdir -p "$STAGING"
        unzip -oq lampac.zip -d "$STAGING"
        rm -f lampac.zip

        subdirs=$(find "$STAGING" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | wc -l)
        files_root=$(find "$STAGING" -mindepth 1 -maxdepth 1 -type f 2>/dev/null | wc -l)
        if [[ "$subdirs" -eq 1 && "$files_root" -eq 0 ]]; then
            subdir=$(find "$STAGING" -mindepth 1 -maxdepth 1 -type d | head -n1)
            shopt -s dotglob nullglob
            mv "$subdir"/* "$STAGING"/ 2>/dev/null || true
            shopt -u dotglob nullglob
            rmdir "$subdir" 2>/dev/null || true
        fi
        [[ ! -f "$STAGING/Core.dll" ]] && echo "FAIL: Core.dll not found" && exit 1

        OLD_INIT=""
        [[ -f "$LAMPAC_DIR/init.conf" ]] && OLD_INIT="$LAMPAC_DIR/init.conf"
        [[ -f "$LAMPAC_DIR/init.yaml" ]] && OLD_INIT="$LAMPAC_DIR/init.yaml"
        OLD_PASSWD=""
        [[ -f "$LAMPAC_DIR/passwd" ]] && OLD_PASSWD="$LAMPAC_DIR/passwd"

        rm -rf "$LAMPAC_DIR"
        mv "$STAGING" "$LAMPAC_DIR"
        [[ -n "$OLD_INIT" ]] && cp -a "$OLD_INIT" "$LAMPAC_DIR/" 2>/dev/null || true
        [[ -n "$OLD_PASSWD" ]] && cp -a "$OLD_PASSWD" "$LAMPAC_DIR/" 2>/dev/null || true
        echo "OK"
    '
    ok "Lampac downloaded inside Ubuntu"

    # Setup config
    proot-distro login ubuntu -- bash -c "
        set -euo pipefail
        LAMPAC_DIR=\"/root/lampac\"
        cd \"\$LAMPAC_DIR\"
        if [[ ! -f \"init.conf\" && ! -f \"init.yaml\" ]]; then
            cat > init.conf << 'CONF'
{
  \"listen\": {
    \"ip\": \"0.0.0.0\",
    \"port\": ${LISTEN_PORT},
    \"scheme\": \"http\",
    \"version\": true
  },
  \"lowMemoryMode\": true,
  \"BaseModule\": {
    \"SkipModules\": [
      \"Catalog\", \"DLNA\", \"Tracks\", \"Transcoding\", \"WebLog\",
      \"CacheMedia\", \"ProxyLimiter\", \"ForkPlayerXML\", \"MsxNative\",
      \"TelegramAuth\", \"TelegramAuthBot\"
    ],
    \"LoadModules\": [\".*\"]
  },
  \"gst\": {
    \"enable\": true,
    \"hdr_to_sdr\": true,
    \"useGpu\": true,
    \"hardwareAcceleration\": false,
    \"x264Ultrafast\": true,
    \"segment_seconds\": 2,
    \"segment_buffer\": 4
  },
  \"Jackett\": {
    \"enable\": true,
    \"url\": \"\",
    \"port\": 9117,
    \"api_key\": \"\",
    \"proxy_downloads\": true
  },
  \"disableEng\": true,
  \"chromium\": {
    \"enable\": true,
    \"Headless\": true,
    \"executablePath\": \"/usr/bin/google-chrome-stable\",
    \"Args\": [
      \"--no-sandbox\",
      \"--disable-setuid-sandbox\",
      \"--disable-dev-shm-usage\",
      \"--disable-gpu\"
    ],
    \"context\": { \"keepopen\": false, \"min\": 0, \"max\": 1 }
  },
  \"firefox\":  { \"enable\": false },
  \"rch\":      { \"enable\": false },
  \"WAF\":      { \"enable\": false },
  \"accsdb\":   { \"enable\": false },
  \"serverproxy\": { \"enable\": true, \"verifyip\": false },
  \"serilog\":  false,
  \"LampaWeb\": {
    \"widgets\": { \"samsung\": false, \"lg\": false }
  }
}
CONF
            echo \"  init.conf created (port ${LISTEN_PORT})\"
        fi
        [[ ! -f \"passwd\" ]] && echo -n \"${ROOT_PASSWORD}\" > passwd
        echo \"  Config ready\"
    "
    ok "Config ready"
}

# ─── Browser runtime, Termux profile + custom modules ────────────────────────

install_chromium_in_ubuntu() {
    info "Checking Chrome/Chromium for browser-backed sources..."

    if proot-distro login ubuntu -- bash -c 'test -x /usr/bin/google-chrome-stable || test -x /usr/bin/chromium'; then
        ok "Chrome/Chromium executable found"
        return 0
    fi

    if proot-distro login ubuntu -- bash -c '
        set -euo pipefail
        export DEBIAN_FRONTEND=noninteractive
        arch=$(dpkg --print-architecture)
        case "$arch" in
            arm64) url="https://dl.google.com/linux/direct/google-chrome-stable_current_arm64.deb" ;;
            amd64) url="https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb" ;;
            *) echo "Unsupported Debian architecture: $arch" >&2; exit 2 ;;
        esac
        apt-get update -qq
        apt-get install -y -qq ca-certificates curl
        curl -fL --retry 3 "$url" -o /tmp/google-chrome.deb
        apt-get install -y /tmp/google-chrome.deb
        rm -f /tmp/google-chrome.deb
    '; then
        ok "Chrome installed for Mirage/Phantom/Spectre"
    else
        warn "Could not install Chrome automatically; browser-backed sources remain unavailable"
        return 0
    fi
}

ensure_runtime_config() {
    info "Applying Termux runtime configuration (ENG sources disabled)..."

    proot-distro login ubuntu -- bash -c '
        set -euo pipefail
        file=/root/lampac/init.conf
        [ -f "$file" ] || exit 0

        # Repair a common manual-edit typo and keep GStreamer available.
        sed -i "s/^[[:space:]]*}:,/  },/" "$file"
        sed -i "s/\"GStreamer\",[[:space:]]*//g; s/,[[:space:]]*\"GStreamer\"//g" "$file"

        # New installs default to disabled ENG sources, but preserve an
        # explicit user choice on existing installations.
        if ! grep -q "^[[:space:]]*\"disableEng\"[[:space:]]*:" "$file"; then
            if grep -q "^[[:space:]]*\"listen\"[[:space:]]*:" "$file"; then
                sed -i "/^[[:space:]]*\"listen\"[[:space:]]*:/i\  \"disableEng\": true," "$file"
            else
                echo "  warning: could not add disableEng automatically"
            fi
        fi

        # Materialize the local Jackett section in init.conf so AdminPanel can
        # edit it. Catalog-only keys are displayed as "missing" until they
        # exist in either init.conf or current.conf.
        if ! grep -q "^[[:space:]]*\"Jackett\"[[:space:]]*:" "$file"; then
            if grep -q "^[[:space:]]*\"listen\"[[:space:]]*:" "$file"; then
                sed -i "/^[[:space:]]*\"listen\"[[:space:]]*:/i\\  \"Jackett\": { \"enable\": true, \"url\": \"\", \"port\": 9117, \"api_key\": \"\", \"proxy_downloads\": true }," "$file"
            else
                echo "  warning: could not add Jackett section automatically"
            fi
        fi

        # Repair only the exact one-line Chromium setting written by older
        # versions of this Termux script. It accidentally disabled Mirage,
        # Phantom and Spectre together with ENG sources. A normal/detailed
        # user-managed Chromium section is left untouched.
        if [ -x /usr/bin/google-chrome-stable ]; then
            browser=/usr/bin/google-chrome-stable
        elif [ -x /usr/bin/chromium ]; then
            browser=/usr/bin/chromium
        else
            browser=""
        fi

        if grep -q "^[[:space:]]*\"chromium\"[[:space:]]*:[[:space:]]*{[[:space:]]*\"enable\"[[:space:]]*:[[:space:]]*false[[:space:]]*}[[:space:]]*,[[:space:]]*$" "$file"; then
            if [ -n "$browser" ]; then
                sed -i "/^[[:space:]]*\"chromium\"[[:space:]]*:[[:space:]]*{[[:space:]]*\"enable\"[[:space:]]*:[[:space:]]*false[[:space:]]*}[[:space:]]*,[[:space:]]*$/c\\  \"chromium\": {\n    \"enable\": true,\n    \"Headless\": true,\n    \"executablePath\": \"$browser\",\n    \"Args\": [\"--no-sandbox\", \"--disable-setuid-sandbox\", \"--disable-dev-shm-usage\", \"--disable-gpu\"],\n    \"context\": { \"keepopen\": false, \"min\": 0, \"max\": 1 }\n  }," "$file"
                echo "  [chromium] repaired legacy disable; Mirage/Phantom/Spectre enabled"
            else
                echo "  [chromium] warning: legacy disable found but no Chrome/Chromium executable exists"
            fi
        fi

        # Validate only when Chromium is enabled. disableEng is intentionally
        # independent and does not disable browser-backed Russian sources.
        chromium_block=$(sed -n "/^[[:space:]]*\"chromium\"[[:space:]]*:/,/^[[:space:]]*},[[:space:]]*$/p" "$file")
        if [ -n "$browser" ] && printf "%s\n" "$chromium_block" | grep -q "\"enable\"[[:space:]]*:[[:space:]]*true"; then
            if timeout 60 "$browser" --headless=new --no-sandbox --disable-gpu --disable-dev-shm-usage --dump-dom https://example.com 2>/dev/null | grep -q "Example Domain"; then
                echo "  [chromium] self-test OK"
            elif timeout 60 "$browser" --headless=new --no-sandbox --disable-gpu --disable-dev-shm-usage --no-zygote --single-process --dump-dom https://example.com 2>/dev/null | grep -q "Example Domain"; then
                sed -i "/^[[:space:]]*\"chromium\"[[:space:]]*:/,/^[[:space:]]*},[[:space:]]*$/s#\"Args\"[[:space:]]*:[^]]*]#\"Args\": [\"--no-sandbox\", \"--disable-setuid-sandbox\", \"--disable-dev-shm-usage\", \"--disable-gpu\", \"--no-zygote\", \"--single-process\"]#" "$file"
                echo "  [chromium] self-test fixed with proot renderer flags"
            else
                echo "  [chromium] WARNING: executable exists but headless rendering failed"
            fi
        fi

        if [ -f /root/lampac/init.yaml ]; then
            echo "  warning: init.yaml also exists and may override init.conf"
        fi
    '

    ok "Runtime configuration applied; existing ENG/Chromium choices preserved"
}

# Patch original Lampa files with Vietnamese. Keep this OUT of bash -c "..." —
# the language strings contain double quotes and would terminate that script.
# meta.js is the source registry; app.min.js is what Lampa boots (webpack
# inlines meta.languages). Native loadLang then fetches lang/vi.js.
patch_vietnamese_language() {
    info "Registering Vietnamese in original Lampa files (vi.js, meta.js, app.min.js)..."
    proot-distro login ubuntu -- bash -s <<'VI_LANG'
set -euo pipefail
src=/root/lampac/module/LampaWeb/lang/vi.js
root=/root/lampac/wwwroot/lampa-main
langdir="$root/lang"
mkdir -p "$langdir"
if [ -f "$src" ]; then
    cp "$src" "$langdir/vi.js"
fi
entry='vi: { code: "vi", name: "Tiếng Việt", lang_choice_title: "Chào mừng", lang_choice_subtitle: "Chọn ngôn ngữ của bạn" }, '
patch_registry() {
    local file="$1"
    [ -f "$file" ] || return 0
    if grep -qF 'Tiếng Việt' "$file"; then
        return 0
    fi
    awk -v entry="$entry" '
      BEGIN { done = 0 }
      {
        if (!done && match($0, /languages[[:space:]]*:[[:space:]]*\{/)) {
          sub(/languages[[:space:]]*:[[:space:]]*\{/, "& " entry)
          done = 1
        }
        print
      }
    ' "$file" > "$file.tmp"
    mv "$file.tmp" "$file"
}
patch_registry "$langdir/meta.js"
patch_registry "$root/app.min.js"
VI_LANG
    ok "Vietnamese registered in lang/vi.js, lang/meta.js and app.min.js"
}


# --sync pulls only the files from the latest patch. Update this list when
# shipping a small fix so Termux does not re-download Chrome, hls.js, or
# every custom module. Use --sync-all for a full refresh.
sync_latest_modules() {
    info "Syncing latest patch files only (Cub ENG catalog + TMDB English titles/logos)..."

    proot-distro login ubuntu -- bash -c "
        set -euo pipefail
        base=\"${CUSTOM_SOURCE_BASE}\"
        stamp=\$(date +%s)
        pull() {
            src=\"\$1\"
            dest=\"\$2\"
            dir=\$(dirname \"\$dest\")
            if [ ! -d \"\$dir\" ]; then
                echo \"  [sync] skip (missing dir): \$dest\"
                return 0
            fi
            if curl -fsSL --retry 3 \"\$base/\$src?cb=\$stamp\" -o \"\$dest.tmp\"; then
                mv \"\$dest.tmp\" \"\$dest\"
                echo \"  [sync] \$dest\"
            else
                rm -f \"\$dest.tmp\"
                echo \"  [sync] skip (404/missing): \$src\"
            fi
        }
        # Remove modules and site definitions retired from the repository so
        # an existing installation cannot load them after a lightweight sync.
        rm -rf /root/lampac/module/OnlineENG/CineWave \
               /root/lampac/module/OnlineENG/Mapple4K \
               /root/lampac/module/OnlineENG/OpenDirectory \
               /root/lampac/mods/OnlineENG/CineWave \
               /root/lampac/mods/OnlineENG/Mapple4K \
               /root/lampac/mods/OnlineENG/OpenDirectory \
               /root/lampac/module/Adult/OneJav \
               /root/lampac/mods/Adult/OneJav
        rm -f /root/lampac/module/NextHUB/sites/85po.yaml \
              /root/lampac/mods/NextHUB/sites/85po.yaml \
              /root/lampac/module/LampaWeb/plugins/player-landscape.js \
              /root/lampac/mods/LampaWeb/plugins/player-landscape.js

        # Latest patch:
        #  - Cub ENG catalog (viewru off) + TMDB English titles/logos
        #  - lampainit.js: Cyrillic/VI card titles → original_title
        # mods/ overrides module/, so keep both copies in sync when present.
        for lampaweb in /root/lampac/module/LampaWeb /root/lampac/mods/LampaWeb; do
            if [ -d \"\$lampaweb/plugins\" ]; then
                pull Modules/LampaWeb/plugins/online-compact.js \"\$lampaweb/plugins/online-compact.js\"
                pull Modules/LampaWeb/plugins/lampainit.js \"\$lampaweb/plugins/lampainit.js\"
                pull Modules/LampaWeb/plugins/stremiosub.js \"\$lampaweb/plugins/stremiosub.js\"
                pull Modules/LampaWeb/plugins/subfinder.js \"\$lampaweb/plugins/subfinder.js\"
                pull Modules/LampaWeb/plugins/subsense.js \"\$lampaweb/plugins/subsense.js\"
                pull Modules/LampaWeb/plugins/subsense-auto.js \"\$lampaweb/plugins/subsense-auto.js\"
                pull Modules/LampaWeb/plugins/autotracks.js \"\$lampaweb/plugins/autotracks.js\"
                pull Modules/LampaWeb/plugins/vietnamese.js \"\$lampaweb/plugins/vietnamese.js\"
                pull Modules/LampaWeb/plugins/adminpanel.js \"\$lampaweb/plugins/adminpanel.js\"
                # Jackett magnet resolver (client hook .torrent -> magnet + server plugin).
                # Quen file nay thi /torrents POST link .torrent -> TorrServer cong khai 400.
                pull Modules/LampaWeb/plugins/jackett.js \"\$lampaweb/plugins/jackett.js\"
            fi
            if [ -d \"\$lampaweb/Controllers\" ]; then
                pull Modules/LampaWeb/Controllers/ApiController.cs \"\$lampaweb/Controllers/ApiController.cs\"
            fi
            if [ -d \"\$lampaweb/Models\" ]; then
                pull Modules/LampaWeb/Models/InitPlugins.cs \"\$lampaweb/Models/InitPlugins.cs\"
            fi
        done
        for proxymodule in CubProxy TmdbProxy; do
            for proxy in /root/lampac/module/Proxy/\$proxymodule /root/lampac/mods/Proxy/\$proxymodule; do
                if [ -d \"\$proxy\" ]; then
                    pull Modules/Proxy/\$proxymodule/Controller.cs \"\$proxy/Controller.cs\"
                    pull Modules/Proxy/\$proxymodule/ModInit.cs \"\$proxy/ModInit.cs\"
                fi
            done
        done
        for vidlink in /root/lampac/module/OnlineENG/VidLink /root/lampac/mods/OnlineENG/VidLink; do
            if [ -d \"\$vidlink\" ]; then
                pull Modules/OnlineENG/VidLink/Controller.cs \"\$vidlink/Controller.cs\"
                pull Modules/OnlineENG/VidLink/ModInit.cs \"\$vidlink/ModInit.cs\"
            fi
        done
        # VidCore: da xac minh thiet bi 2026-08-31 (movie 1288445 + tv 125988) -> duoc
        # cho vao sync nhe. Khac VidLink/Videasy, thu muc module chua co san trong
        # lampac-nextgen.zip nen phai tu mkdir va lay ca manifest.json; moi file bo qua
        # an toan khi nguon khong co (khoi nay chay duoi set -euo pipefail).
        vidcoretarget=/root/lampac/module/OnlineENG/VidCore
        mkdir -p \"\$vidcoretarget\"
        for vidcorefile in manifest.json Controller.cs ModInit.cs; do
            if curl -fsSL --retry 3 \"\$base/Modules/OnlineENG/VidCore/\$vidcorefile?cb=\$stamp\" -o \"/tmp/vidcore-\$vidcorefile\"; then
                mv \"/tmp/vidcore-\$vidcorefile\" \"\$vidcoretarget/\$vidcorefile\"
                echo \"  [sync] vidcore/\$vidcorefile\"
            else
                rm -f \"/tmp/vidcore-\$vidcorefile\"
                echo \"  [sync] vidcore: bo qua \$vidcorefile (nguon khong co)\"
            fi
        done

        # Po85 (85po.com, KVS): xac minh thiet bi 2026-09-05. Khong co trong
        # lampac-nextgen.zip nen mkdir + lay full file.
        po85synctarget=/root/lampac/module/Adult/Po85
        mkdir -p \"\$po85synctarget\"
        for po85syncfile in manifest.json Controller.cs ModInit.cs Service.cs; do
            if curl -fsSL --retry 3 \"\$base/Modules/Adult/Po85/\$po85syncfile?cb=\$stamp\" -o \"/tmp/po85sync-\$po85syncfile\"; then
                mv \"/tmp/po85sync-\$po85syncfile\" \"\$po85synctarget/\$po85syncfile\"
                echo \"  [sync] po85/\$po85syncfile\"
            else
                rm -f \"/tmp/po85sync-\$po85syncfile\"
                echo \"  [sync] po85: bo qua \$po85syncfile (nguon khong co)\"
            fi
        done

        # VK Video quoc te: tim ca original_title va giu HLS/DASH multi-audio.
        for vkmovie in /root/lampac/module/OnlineRUS/VkMovie /root/lampac/mods/OnlineRUS/VkMovie; do
            if [ -d \"\$vkmovie\" ]; then
                pull Modules/OnlineRUS/VkMovie/Controller.cs \"\$vkmovie/Controller.cs\"
                pull Modules/OnlineRUS/VkMovie/Model.cs \"\$vkmovie/Model.cs\"
                pull Modules/OnlineRUS/VkMovie/ModInit.cs \"\$vkmovie/ModInit.cs\"
                pull Modules/OnlineRUS/VkMovie/manifest.json \"\$vkmovie/manifest.json\"
            fi
        done

        # Nguon RUS vua dong bo tu upstream lampac (ban da chinh): Gencit, Videoseed,
        # FlixCDN. Khong co trong lampac-nextgen.zip nen phai tu tao thu muc va lay ca
        # manifest.json; mods/ override thi chi cap nhat khi thu muc da ton tai.
        for rusmod in Gencit Videoseed FlixCDN; do
            rustarget=/root/lampac/module/OnlineRUS/\$rusmod
            mkdir -p \"\$rustarget\"
            for rusfile in manifest.json Controller.cs Model.cs ModInit.cs Service.cs ModuleConf.cs; do
                if curl -fsSL --retry 3 \"\$base/Modules/OnlineRUS/\$rusmod/\$rusfile?cb=\$stamp\" -o \"/tmp/rus-\$rusmod-\$rusfile\"; then
                    mv \"/tmp/rus-\$rusmod-\$rusfile\" \"\$rustarget/\$rusfile\"
                    echo \"  [sync] rus/\$rusmod/\$rusfile\"
                else
                    rm -f \"/tmp/rus-\$rusmod-\$rusfile\"
                fi
            done
            rusmods=/root/lampac/mods/OnlineRUS/\$rusmod
            if [ -d \"\$rusmods\" ]; then
                for rusfile in manifest.json Controller.cs Model.cs ModInit.cs Service.cs ModuleConf.cs; do
                    [ -f \"\$rustarget/\$rusfile\" ] && cp \"\$rustarget/\$rusfile\" \"\$rusmods/\$rusfile\" 2>/dev/null && echo \"  [sync] rusmods/\$rusmod/\$rusfile\"
                done
            fi
        done

        # AiLiberty: nguon anime moi (ailiberty.top), dung lazy proxy /lite/ailiberty/video.m3u8
        # vi link het han rat nhanh. Khong co trong lampac-nextgen.zip -> tu tao thu muc.
        ailbtarget=/root/lampac/module/OnlineAnime/AiLiberty
        mkdir -p \"\$ailbtarget\"
        for ailbfile in manifest.json Controller.cs Model.cs ModInit.cs; do
            if curl -fsSL --retry 3 \"\$base/Modules/OnlineAnime/AiLiberty/\$ailbfile?cb=\$stamp\" -o \"/tmp/ailb-\$ailbfile\"; then
                mv \"/tmp/ailb-\$ailbfile\" \"\$ailbtarget/\$ailbfile\"
                echo \"  [sync] anime/AiLiberty/\$ailbfile\"
            else
                rm -f \"/tmp/ailb-\$ailbfile\"
            fi
        done
        ailbmods=/root/lampac/mods/OnlineAnime/AiLiberty
        if [ -d \"\$ailbmods\" ]; then
            for ailbfile in manifest.json Controller.cs Model.cs ModInit.cs; do
                [ -f \"\$ailbtarget/\$ailbfile\" ] && cp \"\$ailbtarget/\$ailbfile\" \"\$ailbmods/\$ailbfile\" 2>/dev/null && echo \"  [sync] anime mods/AiLiberty/\$ailbfile\"
            done
        fi


        # MoviesHub cung duoc sync o day: lampac sync la du de cap nhat 2 nguoi nay.
        # Danh sach file .cs KHONG hardcode nua: doc tu "tree" trong manifest.json cua chinh module.
        # Them nguon cung kieu (lop Movies4U/MoviesDrive) => chi can sua manifest.json trong repo,
        # khong phai dong setup-termux.sh. Tai manifest that bai (branch cu, mang che) thi quay ve
        # danh sach da biet: khoi nay chay duoi set -euo pipefail nen mot file phu khong duoc phep
        # dung ca sync.
        movieshubtarget=/root/lampac/module/OnlineENG/MoviesHub
        mkdir -p \"\$movieshubtarget\"
        if curl -fsSL --retry 3 \"\$base/Modules/OnlineENG/MoviesHub/manifest.json?cb=\$stamp\" -o \"\$movieshubtarget/manifest.json\"; then
            echo \"  [sync] movieshub/manifest.json\"
        else
            rm -f \"\$movieshubtarget/manifest.json\"
        fi
        movieshubfiles=\$(grep -o '[A-Za-z0-9_.-]*\.cs' \"\$movieshubtarget/manifest.json\" 2>/dev/null | tr '\n' ' ' || true)
        [ -n \"\$movieshubfiles\" ] || movieshubfiles=\"ModInit.cs HubController.cs MoviesDriveController.cs Movies4UController.cs\"
        for movieshubfile in \$movieshubfiles; do
            if curl -fsSL --retry 3 \"\$base/Modules/OnlineENG/MoviesHub/\$movieshubfile?cb=\$stamp\" -o \"/tmp/movieshub-\$movieshubfile\"; then
                mv \"/tmp/movieshub-\$movieshubfile\" \"\$movieshubtarget/\$movieshubfile\"
                echo \"  [sync] movieshub/\$movieshubfile\"
            else
                rm -f \"/tmp/movieshub-\$movieshubfile\"
                echo \"  [sync] movieshub: bo qua \$movieshubfile (nguon khong co)\"
            fi
        done

        # File .cs da bi rut khoi tree (UhdmoviesController.cs tu 2026-09-01) PHAI bi xoa tren may:
        # nam lai la dotnet build don no vao, no goi ModInit.uhd da go -> CS0117, module khong nap,
        # log im lang. Sync = dong bo ca 2 chieu, khong chi them.
        for oldcs in \"\$movieshubtarget\"/*.cs; do
            [ -e \"\$oldcs\" ] || continue
            orph=\$(basename \"\$oldcs\")
            case \" \$movieshubfiles \" in
                *\" \$orph \"*) ;;
                *) rm -f \"\$oldcs\"; echo \"  [sync] movieshub: xoa \$orph (khong con trong tree)\";;
            esac
        done

        # overlay mods/ THANG module/ khi Lampac nap module, nen no cung phai duoc don + cap nhat:
        # Manifest cu trong mods/ co the van khai bao controller da bi xoa, khien CSharpEval mo
        # file khong ton tai va module khong nap. Sync phai giu hai ban dong nhat.
        if [ -d /root/lampac/mods/OnlineENG/MoviesHub ]; then
            for oldcs in /root/lampac/mods/OnlineENG/MoviesHub/*.cs; do
                [ -e \"\$oldcs\" ] || continue
                orph=\$(basename \"\$oldcs\")
                case \" \$movieshubfiles \" in
                    *\" \$orph \"*) cp \"\$movieshubtarget/\$orph\" /root/lampac/mods/OnlineENG/MoviesHub/\$orph 2>/dev/null;;
                    *) rm -f \"\$oldcs\"; echo \"  [sync] movieshub/mods: xoa \$orph\";;
                esac
            done
            cp \"\$movieshubtarget/manifest.json\" /root/lampac/mods/OnlineENG/MoviesHub/manifest.json 2>/dev/null \
                && echo \"  [sync] movieshub/mods: cap nhat manifest.json\"
        fi

        for sisimod in /root/lampac/module/SISI /root/lampac/mods/SISI; do
            if [ -d \"\$sisimod\" ]; then
                pull SISI/SisiApi.cs \"\$sisimod/SisiApi.cs\"
            fi
            if [ -d \"\$sisimod/plugins\" ]; then
                pull SISI/plugins/sisi-restyle.js \"\$sisimod/plugins/sisi-restyle.js\"
            fi
        done
        for nexthub in /root/lampac/module/NextHUB /root/lampac/mods/NextHUB; do
            if [ -d \"\$nexthub/sites\" ]; then
                pull Modules/NextHUB/sites/xasiat.yaml \"\$nexthub/sites/xasiat.yaml\"
            fi
            if [ -d \"\$nexthub\" ]; then
                pull Modules/NextHUB/CategoryVi.cs \"\$nexthub/CategoryVi.cs\"
            fi
        done
        for admin in /root/lampac/module/AdminPanel /root/lampac/mods/AdminPanel; do
            if [ -d \"\$admin\" ]; then
                pull Modules/AdminPanel/AdminPanelController.cs \"\$admin/AdminPanelController.cs\"
                pull Modules/AdminPanel/ConfigSectionGroups.cs \"\$admin/ConfigSectionGroups.cs\"
                pull Modules/AdminPanel/ModInit.cs \"\$admin/ModInit.cs\"
                pull Modules/AdminPanel/manifest.json \"\$admin/manifest.json\"
                pull Modules/AdminPanel/auth.html \"\$admin/auth.html\"
                pull Modules/AdminPanel/index.html \"\$admin/index.html\"
            fi
        done
    "

    ok "Latest patch files applied"
}

install_custom_modules() {
    info "Installing custom VidCore/KKPhim/K20/VsMov/WebStreamr/Sootio/AIOStreams/GStreamer module files..."

    proot-distro login ubuntu -- bash -c "
        set -euo pipefail

        # Remove retired providers and stale site definitions so the dynamic
        # module loader cannot load them from an earlier installation.
        rm -rf /root/lampac/module/OnlineVN/NguonC \
               /root/lampac/module/OnlineENG/CineWave \
               /root/lampac/module/OnlineENG/Mapple4K \
               /root/lampac/module/OnlineENG/OpenDirectory \
               /root/lampac/mods/OnlineENG/CineWave \
               /root/lampac/mods/OnlineENG/Mapple4K \
               /root/lampac/mods/OnlineENG/OpenDirectory \
               /root/lampac/module/Adult/OneJav \
               /root/lampac/mods/Adult/OneJav
        rm -f /root/lampac/module/NextHUB/sites/85po.yaml \
              /root/lampac/mods/NextHUB/sites/85po.yaml

        # VidCore dat LEN DAU ham install_custom_modules(), khong phai ngau nhien:
        # ca khoi nay chay trong guest script co set -euo pipefail, va nhung khoi o
        # duoi (KKPhim/K20/VsMov/...) dung curl -f tran -> mot file 404 la dung ca
        # ham, nguoi moi cai se khong bao gio co VidCore. Dat o day dam bao 3 lenh
        # --install / --sync-all / --update luon kip chép module truoc khi co thu gi do chet.
        # Module nay khong co trong lampac-nextgen.zip nen phai tu mkdir va lay ca
        # manifest.json; dung [ -d ] nhu module co san thi se bi bo qua mai mai.
        # (used for modules that already exist in the release) would skip it
        # Tu 2026-08-31 module con nam trong sync nhe (sync_latest_modules), da xac minh
        # chay duoc tren thiet bi nen khong con la "thu nghiem" nua.
        # (sync_latest_modules), vi da xac minh chay duoc tren thiet bi.
        #
        # Comment quy tắc trong khoi nay: khong duoc dung backtick cung nhu dau
        # nhay kep. Ca khoi la chuoi double-quote cua bash -c, nen mot cap
        # backtick se bi host chay nhu command substitution va nut tron phan
        # text toi backtick ke tiep; con dau nhay kep chua escape thi dong chuoi
        # som, day phan con lai cua guest script ra chay o host. Kiem tra nhanh:
        # grep -n 'setup-termux.sh$' khong duoc con tro toi script kiem tra nao
        # (kiem tra bang bash -n tren guest script da duoc goi escape).
        #
        # Moi file lay doc lap va loi duoc bo qua: khoi nay chay trong guest
        # script co set -euo pipefail, nen khi nguon khong co module (vi du
        # vua lui LAMPAC_CUSTOM_SOURCE_BASE ve nhanh cu) ma curl -f fail thi
        # cung khong duoc dung ca sync-all.
#  LUU Y: block nay dung mot cache-busting timestamp cua ca ham, nen phai gan
#  syncstamp o ngay trong block; neu khong guest shell voi set -u se dung sync.
#  la dung ca ham cai dat (da phat hien bang cach chay guest script voi curl stub).
        # tai cho; dong duoi gan lai cung khong sao (chi la cache-buster).
        syncstamp=\$(date +%s)
        vidcorebase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/VidCore\"
        vidcoretarget=/root/lampac/module/OnlineENG/VidCore
        for vidcorefile in manifest.json Controller.cs ModInit.cs; do
            if curl -fsSL --retry 3 \"\$vidcorebase/\$vidcorefile?cb=\$syncstamp\" -o \"/tmp/vidcore-\$vidcorefile\"; then
                mkdir -p \"\$vidcoretarget\"
                mv \"/tmp/vidcore-\$vidcorefile\" \"\$vidcoretarget/\$vidcorefile\"
                echo \"  [vidcore] \$vidcorefile\"
            else
                rm -f \"/tmp/vidcore-\$vidcorefile\"
                echo \"  [vidcore] bo qua \$vidcorefile - khong co tren nguon: kiem tra LAMPAC_CUSTOM_SOURCE_BASE\"
            fi
        done

        # Po85 (85po.com, KVS): xac minh thiet bi 2026-09-05 (list 60 items,
        # vidosik ra link, strem proxy 206 video/mp4). Khong co trong
        # lampac-nextgen.zip nen mkdir + lay full file, bo qua tung file neu
        # nguon khong co (an toan duoi set -euo pipefail).
        po85base=\"\${CUSTOM_SOURCE_BASE}/Modules/Adult/Po85\"
        po85target=/root/lampac/module/Adult/Po85
        mkdir -p \"\$po85target\"
        for po85file in manifest.json Controller.cs ModInit.cs Service.cs; do
            if curl -fsSL --retry 3 \"\$po85base/\$po85file?cb=\$syncstamp\" -o \"/tmp/po85-\$po85file\"; then
                mv \"/tmp/po85-\$po85file\" \"\$po85target/\$po85file\"
                echo \"  [po85] \$po85file\"
            else
                rm -f \"/tmp/po85-\$po85file\"
                echo \"  [po85] bo qua \$po85file - khong co tren nguon\"
            fi
        done

        # MoviesHub = MoviesDrive + Movies4U + resolver HubCloud/GDrive dung chung. Danh sach file
        # lay tu "tree" trong manifest.json (giong khoi sync_latest_modules): them nguon cung ho
        # khong can sua file nay; manifest khong co tren nguon thi dung danh sach cu.
        # Patched VK Video source is not part of the upstream release archive yet.
        vkmoviebase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineRUS/VkMovie\"
        mkdir -p /root/lampac/module/OnlineRUS/VkMovie
        for vkmovietarget in /root/lampac/module/OnlineRUS/VkMovie /root/lampac/mods/OnlineRUS/VkMovie; do
            [ -d \"\$vkmovietarget\" ] || continue
            for vkmoviefile in manifest.json Controller.cs Model.cs ModInit.cs; do
                curl -fSL --retry 3 \"\$vkmoviebase/\$vkmoviefile?cb=\$syncstamp\" -o \"\$vkmovietarget/\$vkmoviefile\"
            done
        done

        # Nguon RUS dong bo tu upstream (ban da chinh): Gencit, Videoseed, FlixCDN.
        # Khong nam trong lampac-nextgen.zip nen phai tu tao thu muc; moi file bo qua
        # an toan khi khong co tren nguon (khong duoc phep dung ca cai dat).
        for rusmod in Gencit Videoseed FlixCDN; do
            rusbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineRUS/\$rusmod\"
            rustarget=/root/lampac/module/OnlineRUS/\$rusmod
            mkdir -p \"\$rustarget\"
            for rusfile in manifest.json Controller.cs Model.cs ModInit.cs Service.cs ModuleConf.cs; do
                if curl -fsSL --retry 3 \"\$rusbase/\$rusfile?cb=\$syncstamp\" -o \"/tmp/\$rusmod-\$rusfile\"; then
                    mv \"/tmp/\$rusmod-\$rusfile\" \"\$rustarget/\$rusfile\"
                    echo \"  [\$rusmod] \$rusfile\"
                else
                    rm -f \"/tmp/\$rusmod-\$rusfile\"
                    echo \"  [\$rusmod] bo qua \$rusfile - khong co tren nguon\"
                fi
            done
            rusmods=/root/lampac/mods/OnlineRUS/\$rusmod
            if [ -d \"\$rusmods\" ]; then
                for rusfile in manifest.json Controller.cs Model.cs ModInit.cs Service.cs ModuleConf.cs; do
                    [ -f \"\$rustarget/\$rusfile\" ] && cp \"\$rustarget/\$rusfile\" \"\$rusmods/\$rusfile\" 2>/dev/null
                done
            fi
        done

        # AiLiberty: nguon anime moi (ailiberty.top), lazy proxy link (het han nhanh).
        ailbbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineAnime/AiLiberty\"
        ailbtarget=/root/lampac/module/OnlineAnime/AiLiberty
        mkdir -p \"\$ailbtarget\"
        for ailbfile in manifest.json Controller.cs Model.cs ModInit.cs; do
            if curl -fsSL --retry 3 \"\$ailbbase/\$ailbfile?cb=\$syncstamp\" -o \"/tmp/ailb-\$ailbfile\"; then
                mv \"/tmp/ailb-\$ailbfile\" \"\$ailbtarget/\$ailbfile\"
                echo \"  [AiLiberty] \$ailbfile\"
            else
                rm -f \"/tmp/ailb-\$ailbfile\"
                echo \"  [AiLiberty] bo qua \$ailbfile - khong co tren nguon\"
            fi
        done
        ailbmods=/root/lampac/mods/OnlineAnime/AiLiberty
        if [ -d \"\$ailbmods\" ]; then
            for ailbfile in manifest.json Controller.cs Model.cs ModInit.cs; do
                [ -f \"\$ailbtarget/\$ailbfile\" ] && cp \"\$ailbtarget/\$ailbfile\" \"\$ailbmods/\$ailbfile\" 2>/dev/null
            done
        fi


        movieshubbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/MoviesHub\"
        movieshubtarget=/root/lampac/module/OnlineENG/MoviesHub
        mkdir -p \"\$movieshubtarget\"
        if curl -fsSL --retry 3 \"\$movieshubbase/manifest.json?cb=\$syncstamp\" -o \"\$movieshubtarget/manifest.json\"; then
            echo \"  [movieshub] manifest.json\"
        fi
        movieshubfiles=\$(grep -o '[A-Za-z0-9_.-]*\.cs' \"\$movieshubtarget/manifest.json\" 2>/dev/null | tr '\n' ' ' || true)
        [ -n \"\$movieshubfiles\" ] || movieshubfiles=\"ModInit.cs HubController.cs MoviesDriveController.cs Movies4UController.cs\"
        for movieshubfile in \$movieshubfiles; do
            if curl -fsSL --retry 3 \"\$movieshubbase/\$movieshubfile?cb=\$syncstamp\" -o \"/tmp/movieshub-\$movieshubfile\"; then
                mv \"/tmp/movieshub-\$movieshubfile\" \"\$movieshubtarget/\$movieshubfile\"
                echo \"  [movieshub] \$movieshubfile\"
            else
                rm -f \"/tmp/movieshub-\$movieshubfile\"
                echo \"  [movieshub] bo qua \$movieshubfile - khong co tren nguon: kiem tra LAMPAC_CUSTOM_SOURCE_BASE\"
            fi
        done

        kkbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineVN/KKPhim\"
        kktarget=/root/lampac/module/OnlineVN/KKPhim
        mkdir -p \"\$kktarget\"
        for file in Controller.cs Model.cs ModInit.cs manifest.json; do
            curl -fSL --retry 3 \"\$kkbase/\$file\" -o \"\$kktarget/\$file\"
        done

        k20base=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineVN/K20\"
        k20target=/root/lampac/module/OnlineVN/K20
        mkdir -p \"\$k20target\"
        for file in Controller.cs Model.cs ModInit.cs manifest.json; do
            curl -fSL --retry 3 \"\$k20base/\$file\" -o \"\$k20target/\$file\"
        done

        vsmovbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineVN/VsMov\"
        vsmovtarget=/root/lampac/module/OnlineVN/VsMov
        mkdir -p \"\$vsmovtarget\"
        for file in Controller.cs Model.cs ModInit.cs manifest.json; do
            curl -fSL --retry 3 \"\$vsmovbase/\$file\" -o \"\$vsmovtarget/\$file\"
        done

        webbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/WebStreamr\"
        webtarget=/root/lampac/module/OnlineENG/WebStreamr
        mkdir -p \"\$webtarget\"
        for file in Controller.cs Model.cs ModInit.cs manifest.json; do
            curl -fSL --retry 3 \"\$webbase/\$file\" -o \"\$webtarget/\$file\"
        done

        sootiobase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/Sootio\"
        sootiotarget=/root/lampac/module/OnlineENG/Sootio
        mkdir -p \"\$sootiotarget\"
        for file in Controller.cs Model.cs ModInit.cs manifest.json; do
            curl -fSL --retry 3 \"\$sootiobase/\$file\" -o \"\$sootiotarget/\$file\"
        done

        aiobase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/AIOStreams\"
        aiotarget=/root/lampac/module/OnlineENG/AIOStreams
        mkdir -p \"\$aiotarget\"
        for file in Controller.cs Model.cs ModInit.cs manifest.json; do
            curl -fSL --retry 3 \"\$aiobase/\$file\" -o \"\$aiotarget/\$file\"
        done

        # Custom Online (Lampa client) plugin: wrapped info rows + full titles.
        onlinebase=\"${CUSTOM_SOURCE_BASE}/Online\"
        onlinetarget=/root/lampac/module/Online
        mkdir -p \"\$onlinetarget\"
        curl -fSL --retry 3 \"\$onlinebase/plugin.js\" -o \"\$onlinetarget/plugin.js\"

        curl -fSL --retry 3 \"${CUSTOM_SOURCE_BASE}/aioctl.sh\" -o /root/aioctl.sh
        curl -fSL --retry 3 \"${CUSTOM_SOURCE_BASE}/jackettctl.sh\" -o /root/jackettctl.sh
        chmod +x /root/aioctl.sh /root/jackettctl.sh

        # NextHUB site definitions change more often than release binaries.
        # Keep the known fixed definitions in sync so an update cannot leave
        # the retired sex-studentki.live domain in the runtime tree.
        syncstamp=\$(date +%s)
        nexthubrootbase=\"${CUSTOM_SOURCE_BASE}/Modules/NextHUB\"
        nexthubroottarget=/root/lampac/module/NextHUB
        nexthubtarget=\"\$nexthubroottarget/sites\"
        if [ -d \"\$nexthubtarget\" ]; then
            for file in 24rolika.yaml 24video.yaml 3movs.yaml analdin.yaml batsa.yaml beeg.yaml bigboss.yaml brazzrus.yaml cam4.yaml crocotube.yaml ebasos.yaml ebun.yaml familyporn.yaml fapguru.yaml film-adult.yaml fpo.yaml gayporntube.yaml hellporno.yaml hochutv.yaml huyamba.yaml jopaonline.yaml lenkino.yaml lenporno.yaml noodlemagazine.yaml oxax.yaml perfektdamen.yaml porn4days.yaml porndig.yaml pornhub.yaml pornk.yaml porno365.yaml porno666.yaml pornoakt.yaml pornobolt.yaml pornobriz.yaml pornokaef.yaml pornone.yaml pornve.yaml prostoporno.yaml rusporno.yaml rusvideos.yaml sex-studentki.yaml sexporno.yaml sexxxxhub.yaml sosushka.yaml trahkino.yaml uporno.yaml veporn.yaml vporno.yaml vtrahe.yaml vtrahetv.yaml watchporn.yaml xasiat.yaml xozilla.yaml xxxperevod.yaml yaeby.yaml youjizz.yaml; do
                curl -fSL --retry 3 \"\$nexthubrootbase/sites/\$file?cb=\$syncstamp\" -o \"\$nexthubtarget/\$file.tmp\"
                mv \"\$nexthubtarget/\$file.tmp\" \"\$nexthubtarget/\$file\"
            done
            for file in CategoryVi.cs manifest.json; do
                curl -fSL --retry 3 \"\$nexthubrootbase/\$file\" -o \"\$nexthubroottarget/\$file.tmp\"
                mv \"\$nexthubroottarget/\$file.tmp\" \"\$nexthubroottarget/\$file\"
            done
            curl -fSL --retry 3 \"\$nexthubrootbase/Controllers/ListController.cs\" -o \"\$nexthubroottarget/Controllers/ListController.cs.tmp\"
            mv \"\$nexthubroottarget/Controllers/ListController.cs.tmp\" \"\$nexthubroottarget/Controllers/ListController.cs\"
            curl -fSL --retry 3 \"\$nexthubrootbase/Controllers/ViewController.cs\" -o \"\$nexthubroottarget/Controllers/ViewController.cs.tmp\"
            mv \"\$nexthubroottarget/Controllers/ViewController.cs.tmp\" \"\$nexthubroottarget/Controllers/ViewController.cs\"
        fi

        # Keep Eporner playback behind Lampac's proxy so its CDN receives the
        # web Referer/Origin headers instead of rejecting the Android player.
        epornerbase=\"${CUSTOM_SOURCE_BASE}/Modules/Adult/Eporner\"
        epornertarget=/root/lampac/module/Adult/Eporner
        if [ -d \"\$epornertarget\" ]; then
            for file in Controller.cs ModInit.cs Service.cs; do
                curl -fSL --retry 3 \"\$epornerbase/\$file\" -o \"\$epornertarget/\$file.tmp\"
                mv \"\$epornertarget/\$file.tmp\" \"\$epornertarget/\$file\"
            done
        fi

        # SISI is maintained as translated source instead of a DOM category
        # translator, preventing category rules from touching video titles.
        sisimodtarget=/root/lampac/module/SISI
        if [ -d \"\$sisimodtarget\" ]; then
            curl -fSL --retry 3 \"${CUSTOM_SOURCE_BASE}/SISI/SisiApi.cs\" -o \"\$sisimodtarget/SisiApi.cs.tmp\"
            mv \"\$sisimodtarget/SisiApi.cs.tmp\" \"\$sisimodtarget/SisiApi.cs\"
        fi
        sisitarget=/root/lampac/module/SISI/plugins
        if [ -d \"\$sisitarget\" ]; then
            rm -f \"\$sisitarget/sisi-layout.js\" \"\$sisitarget/sisi-layout.js.tmp\"
            for file in sisi.js startpage.js sisi-restyle.js; do
                curl -fSL --retry 3 \"${CUSTOM_SOURCE_BASE}/SISI/plugins/\$file\" -o \"\$sisitarget/\$file.tmp\"
                mv \"\$sisitarget/\$file.tmp\" \"\$sisitarget/\$file\"
            done
        fi

        for adultmodule in BongaCams Chaturbate Ebalovo Eporner HQporner Po85 PornHub Porntrex Runetki Spankbang Xhamster Xnxx Xvideos XvideosRED; do
            adulttarget=\"/root/lampac/module/Adult/\$adultmodule\"
            if [ -d \"\$adulttarget\" ]; then
                curl -fSL --retry 3 \"${CUSTOM_SOURCE_BASE}/Modules/Adult/\$adultmodule/Service.cs?cb=\$syncstamp\" -o \"\$adulttarget/Service.cs.tmp\"
                mv \"\$adulttarget/Service.cs.tmp\" \"\$adulttarget/Service.cs\"
            fi
        done

        # Keep Chaturbate resolver files synchronized as well as its translated
        # Service.cs. This also restores the stable direct-HLS implementation
        # after experimental resolver changes.
        chaturbatebase=\"${CUSTOM_SOURCE_BASE}/Modules/Adult/Chaturbate\"
        chaturbatetarget=/root/lampac/module/Adult/Chaturbate
        if [ -d \"\$chaturbatetarget\" ]; then
            for file in Controller.cs ModInit.cs; do
                curl -fSL --retry 3 \"\$chaturbatebase/\$file?cb=\$syncstamp\" -o \"\$chaturbatetarget/\$file.tmp\"
                mv \"\$chaturbatetarget/\$file.tmp\" \"\$chaturbatetarget/\$file\"
            done
        fi

        for adultmodinit in BongaCams Runetki Spankbang Ebalovo; do
            adulttarget=\"/root/lampac/module/Adult/\$adultmodinit\"
            if [ -d \"\$adulttarget\" ]; then
                curl -fSL --retry 3 \"${CUSTOM_SOURCE_BASE}/Modules/Adult/\$adultmodinit/ModInit.cs?cb=\$syncstamp\" -o \"\$adulttarget/ModInit.cs.tmp\"
                mv \"\$adulttarget/ModInit.cs.tmp\" \"\$adulttarget/ModInit.cs\"
            fi
        done

        # Videasy is the first ENG resolver under isolated repair. Sync only
        # this provider; the disableEng switch stays globally enabled.
        # (KHONG duoc dung backtick trong comment o day: ca khoi la chuoi
        #  double-quote cua bash -c, nen mot cap backtick se bi host chay nhu
        #  command substitution va nut tron phan text toi backtick ke tiep.
            #  Sau khi sua khoi nay, kiem tra guest script bang bash -n sau khi goi escape.)
        videasybase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/Videasy\"
        videasytarget=/root/lampac/module/OnlineENG/Videasy
        if [ -d \"\$videasytarget\" ]; then
            for file in Controller.cs ModInit.cs; do
                curl -fSL --retry 3 \"\$videasybase/\$file?cb=\$syncstamp\" -o \"\$videasytarget/\$file.tmp\"
                mv \"\$videasytarget/\$file.tmp\" \"\$videasytarget/\$file\"
            done
        fi

        vidsrcbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/VidSrc\"
        vidsrctarget=/root/lampac/module/OnlineENG/VidSrc
        if [ -d \"\$vidsrctarget\" ]; then
            for file in Controller.cs ModInit.cs; do
                curl -fSL --retry 3 \"\$vidsrcbase/\$file?cb=\$syncstamp\" -o \"\$vidsrctarget/\$file.tmp\"
                mv \"\$vidsrctarget/\$file.tmp\" \"\$vidsrctarget/\$file\"
            done
        fi

        vidlinkbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/VidLink\"
        vidlinktarget=/root/lampac/module/OnlineENG/VidLink
        if [ -d \"\$vidlinktarget\" ]; then
            for file in Controller.cs ModInit.cs; do
                curl -fSL --retry 3 \"\$vidlinkbase/\$file?cb=\$syncstamp\" -o \"\$vidlinktarget/\$file.tmp\"
                mv \"\$vidlinktarget/\$file.tmp\" \"\$vidlinktarget/\$file\"
            done
        fi


        for proxymodule in CubProxy TmdbProxy; do
            proxytarget=\"/root/lampac/module/Proxy/\$proxymodule\"
            if [ -d \"\$proxytarget\" ]; then
                for file in Controller.cs ModInit.cs; do
                    curl -fSL --retry 3 \"${CUSTOM_SOURCE_BASE}/Modules/Proxy/\$proxymodule/\$file\" -o \"\$proxytarget/\$file.tmp\"
                    mv \"\$proxytarget/\$file.tmp\" \"\$proxytarget/\$file\"
                done
            fi
        done

        gstbase=\"${CUSTOM_SOURCE_BASE}/Modules/GStreamer\"
        gsttarget=/root/lampac/module/GStreamer
        mkdir -p \"\$gsttarget/Services\" \"\$gsttarget/plugins\"
        for file in Controller.cs ModInit.cs Services/GService.cs Services/GSProbe.cs Services/GStask.cs Services/HdrToneMappingBackend.cs Services/GStask.Pipeline.cs Services/GStask.Producer.cs plugins/gst.js; do
            curl -fSL --retry 3 \"\$gstbase/\$file\" -o \"\$gsttarget/\$file\"
        done

        # LampaWeb is a dynamic module. Sync its controller, plugin model and
        # subtitle assets, otherwise a release can serve an old plugin list or
        # miss the selected built-in subtitle provider.
        webtarget=/root/lampac/module/LampaWeb
        mkdir -p \"\$webtarget/Controllers\" \"\$webtarget/Models\" \"\$webtarget/Services\" \"\$webtarget/plugins\" \"\$webtarget/lang\"
        webbase=\"${CUSTOM_SOURCE_BASE}/Modules/LampaWeb\"
        for file in Controllers/ApiController.cs ModInit.cs Models/InitPlugins.cs Services/LampaCron.cs Services/LampaVietnamese.cs lang/vi.js plugins/lampainit.js plugins/jackett.js plugins/online-compact.js plugins/vietnamese.js plugins/subsense-auto.js plugins/subsense.js plugins/subfinder.js plugins/stremiosub.js plugins/autotracks.js plugins/adminpanel.js; do
            curl -fSL --retry 3 \"\$webbase/\$file\" -o \"\$webtarget/\$file\"
        done

        mkdir -p \"\$webtarget/vendor/hls\"
        for file in hls.js LICENSE; do
            curl -fSL --retry 3 \"\$webbase/vendor/hls/\$file?cb=\$syncstamp\" -o \"\$webtarget/vendor/hls/\$file.tmp\"
            mv \"\$webtarget/vendor/hls/\$file.tmp\" \"\$webtarget/vendor/hls/\$file\"
        done
        if [ -f /root/lampac/wwwroot/lampa-main/app.min.js ]; then
            mkdir -p /root/lampac/wwwroot/lampa-main/vender/hls
            cp \"\$webtarget/vendor/hls/hls.js\" /root/lampac/wwwroot/lampa-main/vender/hls/hls.js.tmp
            mv /root/lampac/wwwroot/lampa-main/vender/hls/hls.js.tmp /root/lampac/wwwroot/lampa-main/vender/hls/hls.js
        fi

        # base.conf is part of the published app, not the dynamic module.
        # Sync it too so default LampaWeb subtitle flags match the controller
        # and do not remain stuck at an older release's defaults.
        curl -fSL --retry 3 \"${CUSTOM_SOURCE_BASE}/config/base.conf\" -o /root/lampac/base.conf

        # Some release archives already contain lampa-main, so LampaCron sees
        # no update to perform and never makes its usual lampainit.js injection.
        # Ensure the page opened at http://PHONE_IP:9118/ always boots Lampac's
        # configured built-in plugins (ts, gst, subtitles, etc.).
        webindex=/root/lampac/wwwroot/lampa-main/index.html
        if [ -f \"\$webindex\" ] && ! grep -q 'src=\"/lampainit.js\"' \"\$webindex\"; then
            sed -i 's#</body>#<script src=\"/lampainit.js\"></script></body>#' \"\$webindex\"
        fi

        # Copy vi.js now; meta.js is patched after this bash -c so Vietnamese
        # strings do not break the surrounding double-quoted script.
        langdir=/root/lampac/wwwroot/lampa-main/lang
        mkdir -p \"\$langdir\"
        cp \"\$webtarget/lang/vi.js\" \"\$langdir/vi.js\"

        # The AdminPanel is protected by the Lampac root password. If it is
        # already installed/enabled, keep its Vietnamese UI in sync too.
        adminbase=\"${CUSTOM_SOURCE_BASE}/Modules/AdminPanel\"
        adminstamp=\$(date +%s)
        for admintarget in /root/lampac/module/AdminPanel /root/lampac/mods/AdminPanel; do
            [ -d \"\$admintarget\" ] || continue
            for file in AdminPanelController.cs ConfigSectionGroups.cs ModInit.cs manifest.json auth.html index.html; do
                curl -fSL --retry 3 \"\$adminbase/\$file?cb=\$adminstamp\" -o \"\$admintarget/\$file.tmp\"
                mv \"\$admintarget/\$file.tmp\" \"\$admintarget/\$file\"
            done
            if ! grep -q 'src-adult-nexthub' \"\$admintarget/ConfigSectionGroups.cs\"; then
                echo \"  [admin] ERROR: downloaded grouping is stale: \$admintarget\" >&2
                exit 1
            fi
            echo \"  [admin] synced and verified: \$admintarget\"
        done
    "

    patch_vietnamese_language

    ok "Custom Online, NextHUB, AIOStreams, GStreamer and LampaWeb files installed; removed retired NguonC"
}

# ─── Step 4: Create launcher scripts (inside Ubuntu!) ────────────────────────

create_launcher() {
    info "Creating launcher scripts..."

    # Write launcher script INSIDE Ubuntu
    proot-distro login ubuntu -- bash -c 'cat > /root/lampac-run.sh << '\''LAUNCHER'\''
#!/usr/bin/env bash
DOTNET_DIR="/opt/dotnet"
LAMPAC_DIR="/root/lampac"

echo ""
echo "  Lampac NextGen"
echo "  ─────────────────────────────────"

PORT=$(grep -o "\"port\": *[0-9]*" "$LAMPAC_DIR/init.conf" 2>/dev/null | head -1 | grep -o "[0-9]*" || echo "9118")
IP=$(hostname -I 2>/dev/null | awk '\''{print $1}'\'' 2>/dev/null || true)
[[ -z "$IP" ]] && IP="<your-ip>"

echo "  Local:    http://localhost:$PORT"
echo "  Network:  http://$IP:$PORT"
echo "  Config:   $LAMPAC_DIR/init.conf"
echo "  Stop:     Ctrl+C"
echo ""

export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

GST_SCANNER=$(find /usr/lib /usr/libexec /usr/local/lib /usr/local/libexec \
    -type f -name gst-plugin-scanner -perm -111 2>/dev/null | head -n 1 || true)
if [[ -n "$GST_SCANNER" ]]; then
    export GST_PLUGIN_SCANNER="$GST_SCANNER"
    export GST_PLUGIN_SCANNER_1_0="$GST_SCANNER"
fi

# AIOStreams remains in the Lampac lifecycle. Jackett is intentionally managed
# separately with `jackett start|stop` so its performance impact is measurable.
if [[ -x /root/aioctl.sh && -f /root/aiostreams/.env ]]; then
    /root/aioctl.sh start || true
fi

cd "$LAMPAC_DIR"
exec dotnet Core.dll
LAUNCHER
chmod +x /root/lampac-run.sh'
    ok "Launcher inside Ubuntu: /root/lampac-run.sh"

    # Termux-side shortcut command
    cat > "$PREFIX/bin/lampac" <<'SHORTCUT'
#!/usr/bin/env bash
case "${1:-}" in
    start)
        echo "Starting Lampac... (press Ctrl+C to stop)"
        proot-distro login ubuntu -- bash /root/lampac-run.sh
        ;;
    stop)
        # The Core process can appear as `dotnet Core.dll` rather than
        # `proot.*Core.dll`; kill both forms so a stale listener cannot block
        # the next start on the configured port.
        if pkill -TERM -f '[C]ore\.dll' 2>/dev/null; then
            sleep 1
            pkill -KILL -f '[C]ore\.dll' 2>/dev/null || true
            echo "Lampac stopped"
        else
            echo "Lampac not running"
        fi
        if proot-distro login ubuntu -- test -d /root/aiostreams 2>/dev/null; then
            proot-distro login ubuntu -- bash /root/aioctl.sh stop 2>/dev/null || true
        fi
        ;;
    status)
        pgrep -f 'proot.*Core.dll' > /dev/null 2>&1 && echo "Lampac is running" || echo "Lampac not running"
        if proot-distro login ubuntu -- test -d /root/aiostreams 2>/dev/null; then
            proot-distro login ubuntu -- bash /root/aioctl.sh status 2>/dev/null || true
        fi
        ;;
    config)
        proot-distro login ubuntu -- bash -c 'nano /root/lampac/init.conf'
        ;;
    sync)
        bash "$(dirname "$0")/setup-termux.sh" --sync
        ;;
    sync-all)
        bash "$(dirname "$0")/setup-termux.sh" --sync-all
        ;;
    vidcore)
        # Probe nhanh nguồn VidCore (4K) — chạy thẳng từ launcher, không cần script ngoài.
        #   lampac vidcore                 -> movie, TMDB 155 (The Dark Knight)
        #   lampac vidcore 680             -> movie khác theo TMDB id
        #   lampac vidcore 2389 1 1        -> series: tmdb=2389 season=1 episode=1
        TMDB_ID="${2:-155}"
        VSEA="${3:--1}"
        VEP="${4:--1}"

        PORT=$(proot-distro login ubuntu -- bash -c "grep -oE '\"port\"[^0-9]+[0-9]+' /root/lampac/init.conf 2>/dev/null | head -1 | grep -oE '[0-9]+$'")
        PORT=${PORT:-9118}

        if ! proot-distro login ubuntu -- test -f /root/lampac/module/OnlineENG/VidCore/Controller.cs; then
            echo "VidCore: module chưa được cài."
            echo "  cài:  bash \"$(dirname \"$0\")/setup-termux.sh\" --sync-all   rồi  lampac stop && lampac start"
            exit 1
        fi

        if ! curl -fsS -m 5 -o /dev/null "http://127.0.0.1:$PORT/" 2>/dev/null; then
            echo "VidCore: Lampac không đáp ứng ở cổng $PORT — bật nó lên trước (lampac start)."
            echo "        Chạy probe ở terminal khác để còn thấy log của module."
            exit 1
        fi

        echo "VidCore probe  →  http://127.0.0.1:$PORT   tmdb=$TMDB_ID season=$VSEA episode=$VEP"

        code=$(curl -s -o /dev/null -w '%{http_code}' -m 30 "http://127.0.0.1:$PORT/lite/vidcore?tmdb_id=$TMDB_ID&rjson=1")
        ghost=$(curl -s -o /dev/null -w '%{http_code}' -m 30 "http://127.0.0.1:$PORT/lite/vidcore_khongcodau_han")
        echo "  /lite/vidcore        HTTP $code     (route ma: $ghost)"
        if [ "$code" = "$ghost" ]; then
            echo "  route chưa đăng ký — tìm 'compilation VidCore' (OK) hoặc 'compilation error: VidCore' (lỗi) trong log khởi động."
        fi

        out=$(curl -s -m 120 -w '\n%{http_code}' "http://127.0.0.1:$PORT/lite/vidcore/video?id=$TMDB_ID&s=$VSEA&e=$VEP")
        vcode=$(printf '%s' "$out" | tail -1)
        body=$(printf '%s' "$out" | sed '$d')
        echo "  .../vidcore/video    HTTP $vcode"
        printf '%s\n' "$body" | head -c 500; echo

        case "$body" in
            *'"host"'*)    echo "  OK: có link stream. Mở Lampa -> Online -> VidCore để chọn host." ;;
            *'resolve'*)   echo "  exception bên trong Resolve. Dòng 'VidCore: ex ...' trong log nói loại lỗi." ;;
            *'stream'*)    echo "  không có stream. Tìm dòng 'VidCore: ...' trong log: token not found / enc-vidcore incomplete / servers POST empty / no servers / <name> no url." ;;
            *)             echo "  không phân loại được. Bật \"exceptionHandlerLogTarget\": \"file\" trong init.conf để 500 được ghi vào /root/lampac/logs/exceptionHandler.log." ;;
        esac
        ;;
    restart)
        pkill -TERM -f '[C]ore\.dll' 2>/dev/null || true
        sleep 1
        pkill -KILL -f '[C]ore\.dll' 2>/dev/null || true
        proot-distro login ubuntu -- bash /root/lampac-run.sh
        ;;
    branch)
        cur="$(cd "$(dirname "$0")" && git branch --show-current 2>/dev/null || echo unknown)"
        base="${LAMPAC_CUSTOM_SOURCE_BASE:-default (arena/01a0673e-lampac)}"
        echo ""
        echo "  Git branch  : $cur"
        echo "  Source base : $base"
        echo ""
        echo "  Nhánh mới nhất (không dùng main):"
        echo "    cd ~/lampac && git fetch origin && git checkout arena/01a0673e-lampac"
        echo "    echo 'export LAMPAC_CUSTOM_SOURCE_BASE=https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a0673e-lampac' >> ~/.bashrc"
        echo "    source ~/.bashrc && lampac sync && lampac stop && lampac start"
        echo ""
        ;;
    info)
        proot-distro login ubuntu -- bash -c '
            PORT=$(grep -o "\"port\": *[0-9]*" /root/lampac/init.conf 2>/dev/null | head -1 | grep -o "[0-9]*" || echo "9118")
            echo ""
            echo "  Lampac NextGen"
            echo "  ─────────────────────────────────"
            echo "  Local:    http://localhost:$PORT"
            echo "  Jackett:  http://localhost:9117/UI/Dashboard"
            echo "  AIO:      http://localhost:3002/stremio/configure"
            echo "  Config:   /root/lampac/init.conf (inside Ubuntu)"
            echo "  Start:    lampac start"
            echo "  Stop:     lampac stop"
            echo ""
        '
        ;;
    update)
        echo "Updating Lampac..."
        # FIX (2026-08-31): khối inline trước đây tham chiếu $KKBase / $VideasyBase /
        # $LampaWebBase / $BaseConfUrl … — 22 biến không hề được định nghĩa trong
        # script, nên mọi URL curl sinh ra đều thiếu host ("curl: (3) URL rejected:
        # No host part") và `set -euo pipefail` làm dừng cả lệnh update. Bản gốc còn
        # trong git show 8ed1ad8:setup-termux.sh (dòng 924-1192).
        # --update ở CLI đã hoán đổi release rồi gọi install_custom_modules() với
        # ${CUSTOM_SOURCE_BASE} được nội suy đúng, nên chỉ cần delegate (giống sync).
        bash "$(dirname "$0")/setup-termux.sh" --update || exit 1
        proot-distro login ubuntu -- bash -s <<'VI_LANG'
set -euo pipefail
src=/root/lampac/module/LampaWeb/lang/vi.js
root=/root/lampac/wwwroot/lampa-main
langdir="$root/lang"
mkdir -p "$langdir"
if [ -f "$src" ]; then
    cp "$src" "$langdir/vi.js"
fi
entry='vi: { code: "vi", name: "Tiếng Việt", lang_choice_title: "Chào mừng", lang_choice_subtitle: "Chọn ngôn ngữ của bạn" }, '
patch_registry() {
    local file="$1"
    [ -f "$file" ] || return 0
    if grep -qF 'Tiếng Việt' "$file"; then
        return 0
    fi
    awk -v entry="$entry" '
      BEGIN { done = 0 }
      {
        if (!done && match($0, /languages[[:space:]]*:[[:space:]]*\{/)) {
          sub(/languages[[:space:]]*:[[:space:]]*\{/, "& " entry)
          done = 1
        }
        print
      }
    ' "$file" > "$file.tmp"
    mv "$file.tmp" "$file"
}
patch_registry "$langdir/meta.js"
patch_registry "$root/app.min.js"
VI_LANG
        ;;
    *)
        echo "Usage: lampac {start|stop|status|config|info|update|vidcore}"
        echo ""
        echo "  start   — Start Lampac server"
        echo "  stop    — Stop Lampac server"
        echo "  status  — Check if running"
        echo "  config  — Edit config (init.conf)"
        echo "  info    — Show URL and port"
        echo "  update  — Update release and restore custom modules"
        echo "  vidcore — Probe nguồn VidCore 4K: lampac vidcore [tmdb] [season] [episode]"
        ;;
esac
SHORTCUT
    chmod +x "$PREFIX/bin/lampac"
    ok "Shortcut 'lampac' command ready"

    cat > "$PREFIX/bin/aio" <<'AIO_SHORTCUT'
#!/usr/bin/env bash
if ! proot-distro login ubuntu -- test -x /root/aioctl.sh 2>/dev/null; then
    echo "AIO controller is not installed yet. Run: bash setup-termux.sh --sync-all"
    exit 1
fi
case "${1:-info}" in
    install|update|start|stop|restart|status|info|config|logs|log|build-log|install-log|diagnose)
        proot-distro login ubuntu -- bash /root/aioctl.sh "$@"
        ;;
    *)
        echo "Usage: aio {install|update|start|stop|restart|status|info|config|logs|build-log|diagnose}"
        exit 2
        ;;
esac
AIO_SHORTCUT
    chmod +x "$PREFIX/bin/aio"
    ok "Shortcut 'aio' command ready"

    cat > "$PREFIX/bin/jackett" <<'JACKETT_SHORTCUT'
#!/usr/bin/env bash
if ! proot-distro login ubuntu -- test -x /root/jackettctl.sh 2>/dev/null; then
    echo "Jackett controller is not installed yet. Run: bash setup-termux.sh --sync-all"
    exit 1
fi
guest() {
    proot-distro login ubuntu -- bash /root/jackettctl.sh "$@"
}

start_detached() {
    # A normal transient proot login kills background children when it exits.
    # Keep a detached host-side proot supervisor alive for standalone Jackett.
    nohup proot-distro login --no-kill-on-exit ubuntu -- \
        bash /root/jackettctl.sh start \
        >"$HOME/.jackett-proot.log" 2>&1 </dev/null &

    for _ in 1 2 3 4 5 6 7 8 9 10 11 12; do
        sleep 1
        if guest status >/dev/null 2>&1; then
            guest status
            return 0
        fi
    done

    echo "Jackett did not stay online; recent logs:"
    guest logs || cat "$HOME/.jackett-proot.log" 2>/dev/null || true
    return 1
}

case "${1:-info}" in
    start)
        start_detached
        ;;
    restart)
        guest stop || true
        start_detached
        ;;
    install|update)
        proot-distro login ubuntu -- env JACKETT_AUTOSTART=0 \
            bash /root/jackettctl.sh "$@"
        start_detached
        ;;
    stop|status|info|logs|log)
        guest "$@"
        ;;
    *)
        echo "Usage: jackett {install|update|start|stop|restart|status|info|logs}"
        exit 2
        ;;
esac
JACKETT_SHORTCUT
    chmod +x "$PREFIX/bin/jackett"
    ok "Shortcut 'jackett' command ready"
}

# ─── Run Lampac ──────────────────────────────────────────────────────────────

run_lampac() {
    if ! proot-distro login ubuntu -- test -f /root/lampac/Core.dll 2>/dev/null; then
        err "Lampac not installed. Run: bash setup-termux.sh --install"
        exit 1
    fi
    info "Starting Lampac... (press Ctrl+C to stop)"
    echo ""
    exec proot-distro login ubuntu -- bash /root/lampac-run.sh
}

# ─── Main ────────────────────────────────────────────────────────────────────

main() {
    banner
    if [[ "$MODE" == "help" ]]; then
        show_help
        exit 0
    fi
    check_termux

    case "$MODE" in
        "run")
            run_lampac
            ;;
        "sync")
            if ! proot-distro login ubuntu -- test -f /root/lampac/Core.dll 2>/dev/null; then
                err "Lampac not installed. Run: bash setup-termux.sh --install"
                exit 1
            fi

            sync_latest_modules
            ok "Latest patch applied. Restart with: lampac stop && lampac start"
            ;;
        "sync-all")
            if ! proot-distro login ubuntu -- test -f /root/lampac/Core.dll 2>/dev/null; then
                err "Lampac not installed. Run: bash setup-termux.sh --install"
                exit 1
            fi

            install_chromium_in_ubuntu
            ensure_runtime_config
            install_custom_modules
            create_launcher
            ok "Custom modules + browser/runtime settings applied"
            ;;
        "update")
            info "Updating Lampac inside Ubuntu..."
            proot-distro login ubuntu -- bash -c '
                set -euo pipefail
                cd /tmp
                curl -fSL --retry 3 -o lampac.zip "https://github.com/lampac-nextgen/lampac/releases/latest/download/lampac-nextgen.zip"
                [[ ! -s lampac.zip ]] && echo "Download failed" && exit 1
                STAGING="/tmp/lampac-staging"
                rm -rf "$STAGING" && mkdir -p "$STAGING"
                unzip -oq lampac.zip -d "$STAGING" && rm -f lampac.zip
                subdirs=$(find "$STAGING" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | wc -l)
                files_root=$(find "$STAGING" -mindepth 1 -maxdepth 1 -type f 2>/dev/null | wc -l)
                if [[ "$subdirs" -eq 1 && "$files_root" -eq 0 ]]; then
                    subdir=$(find "$STAGING" -mindepth 1 -maxdepth 1 -type d | head -n1)
                    shopt -s dotglob nullglob
                    mv "$subdir"/* "$STAGING"/ 2>/dev/null || true
                    shopt -u dotglob nullglob
                    rmdir "$subdir" 2>/dev/null || true
                fi
                [[ ! -f "$STAGING/Core.dll" ]] && echo "Extraction failed" && exit 1
                cp /root/lampac/init.conf /tmp/init.conf.bak 2>/dev/null || true
                cp /root/lampac/passwd /tmp/passwd.bak 2>/dev/null || true
                rm -rf /root/lampac && mv "$STAGING" /root/lampac
                cp /tmp/init.conf.bak /root/lampac/ 2>/dev/null || true
                cp /tmp/passwd.bak /root/lampac/ 2>/dev/null || true
                rm -f /tmp/*.bak
                echo "Update complete!"
            '
            install_chromium_in_ubuntu
            ensure_runtime_config
            install_custom_modules
            create_launcher
            ok "Done!"
            ;;
        *)
            install_termux_deps
            install_ubuntu
            install_lampac_in_ubuntu
            install_chromium_in_ubuntu
            ensure_runtime_config
            install_custom_modules
            create_launcher

            echo ""
            ok "Installation complete!"
            echo ""
            info "Quick commands:"
            info "  lampac start   — Start Lampac"
            info "  lampac stop    — Stop Lampac"
            info "  lampac status  — Check if running"
            info "  lampac config  — Edit config"
            info "  lampac info    — Show URL & port"
            info "  lampac update  — Update to latest"
            info "  aio install      — Install AIOStreams locally (port 3002)"
            info "  jackett install  — Install Jackett locally (port 9117)"
            info "  lampac start     — Start Lampac + AIOStreams"
            info "  jackett start    — Start Jackett separately when needed"
            echo ""

            read -rp "  Start Lampac now? [Y/n]: " answer
            case "${answer:-Y}" in
                [nN]|[nN][oO])
                    info "Run 'lampac start' to start later."
                    ;;
                *)
                    run_lampac
                    ;;
            esac
            ;;
    esac
}

main
