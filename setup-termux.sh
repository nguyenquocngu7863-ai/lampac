#!/usr/bin/env bash
#
# Lampac NextGen — Installer for Termux (Android)
# Uses proot-distro (Ubuntu) for .NET runtime compatibility
#
# Usage:
#   bash setup-termux.sh            # install & run
#   bash setup-termux.sh --install  # install only
#   bash setup-termux.sh --run      # run only (skip install)
#   bash setup-termux.sh --sync     # apply KKPhim/Chromium setup without replacing Lampac
#   bash setup-termux.sh --update   # update to latest release
#
set -euo pipefail

# ─── Config ──────────────────────────────────────────────────────────────────

LAMPAC_DIR="$HOME/lampac"
LISTEN_PORT="${LAMPAC_PORT:-9118}"
ROOT_PASSWORD="${LAMPAC_PASSWD:-lampac}"
# Custom modules maintained in this repository. Override when using a private fork.
CUSTOM_SOURCE_BASE="${LAMPAC_CUSTOM_SOURCE_BASE:-https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a028cf-lampac}"

MODE=""
[[ "${1:-}" == "--install" ]] && MODE="install"
[[ "${1:-}" == "--run" ]]    && MODE="run"
[[ "${1:-}" == "--sync" ]]   && MODE="sync"
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
    printf "  ${GREEN}--sync${RESET}       Apply KKPhim + ARM64 Chromium setup without replacing Lampac\n"
    printf "  ${GREEN}--update${RESET}     Update Lampac to latest release\n"
    printf "  ${GREEN}--help${RESET}       Show this help\n\n"
    printf "${BOLD}Environment:${RESET}\n"
    printf "  ${CYAN}LAMPAC_PORT${RESET}     Listen port (default: 9118)\n"
    printf "  ${CYAN}LAMPAC_PASSWD${RESET}   Root password (default: lampac)\n\n"
    printf "${BOLD}How it works:${RESET}\n"
    printf "  This script installs Lampac inside proot-distro Ubuntu.\n"
    printf "  .NET 10 runs natively inside Ubuntu (glibc compatible).\n\n"
    printf "${BOLD}Examples:${RESET}\n"
    printf "  ${DIM}# First time setup${RESET}\n"
    printf "  bash setup-termux.sh\n\n"
    printf "  ${DIM}# Custom port${RESET}\n"
    printf "  LAMPAC_PORT=8080 bash setup-termux.sh\n\n"
    printf "  ${DIM}# Apply KKPhim + Chromium setup without replacing the current release${RESET}\n"
    printf "  bash setup-termux.sh --sync\n\n"

    printf "  ${DIM}# Update${RESET}\n"
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
            gstreamer1.0-tools libgstreamer1.0-0 libgstreamer-plugins-base1.0-0 \
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
    \"hdr_to_sdr\": false,
    \"useGpu\": false,
    \"hardwareAcceleration\": false
  },
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

# ─── Optional ARM64 Chromium + custom modules ────────────────────────────────

install_chromium_in_ubuntu() {
    info "Checking Chromium/Chrome for Playwright..."

    if proot-distro login ubuntu -- bash -c 'test -x /usr/bin/google-chrome-stable || test -x /usr/bin/chromium'; then
        ok "ARM64-compatible Chromium executable found"
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
        ok "Chrome installed for Playwright"
    else
        warn "Could not install Chrome automatically; English Playwright sources may remain disabled"
        return 0
    fi
}

ensure_runtime_config() {
    info "Applying Termux runtime configuration..."

    proot-distro login ubuntu -- bash -c '
        set -euo pipefail
        file=/root/lampac/init.conf
        [ -f "$file" ] || exit 0

        # Repair the typo produced by some nano edits: `}:,` is not valid JSON.
        sed -i "s/^[[:space:]]*}:,/  },/" "$file"

        # The Termux profile should keep the GStreamer module available.
        sed -i "s/\"GStreamer\",[[:space:]]*//g; s/,[[:space:]]*\"GStreamer\"//g" "$file"

        if [ -x /usr/bin/google-chrome-stable ]; then
            browser=/usr/bin/google-chrome-stable
        elif [ -x /usr/bin/chromium ]; then
            browser=/usr/bin/chromium
        else
            browser=/usr/bin/google-chrome-stable
        fi

        if grep -q "^[[:space:]]*\"chromium\"[[:space:]]*:" "$file"; then
            sed -i "/^[[:space:]]*\"chromium\"[[:space:]]*:/,/^[[:space:]]*},[[:space:]]*$/ s/\"enable\"[[:space:]]*:[[:space:]]*false/\"enable\": true/" "$file"
            if grep -q "^[[:space:]]*\"executablePath\"[[:space:]]*:" "$file"; then
                sed -i "s#^[[:space:]]*\"executablePath\".*#    \"executablePath\": \"$browser\",#" "$file"
            else
                sed -i "/^[[:space:]]*\"Headless\"[[:space:]]*:/a\\    \"executablePath\": \"$browser\"," "$file"
            fi
        fi

        if [ -f /root/lampac/init.yaml ]; then
            echo "  warning: init.yaml also exists and may override init.conf"
        fi
    '

    ok "Runtime configuration applied"
}

install_custom_modules() {
    info "Installing custom KKPhim/K20/GStreamer module files..."

    proot-distro login ubuntu -- bash -c "
        set -euo pipefail

        # VsMov and NguonC are retired: remove leftovers from older installs so
        # the dynamic module loader cannot load the broken providers again.
        rm -rf /root/lampac/module/OnlineVN/VsMov /root/lampac/module/OnlineVN/NguonC

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

        webbase=\"${CUSTOM_SOURCE_BASE}/Modules/OnlineENG/WebStreamr\"
        webtarget=/root/lampac/module/OnlineENG/WebStreamr
        mkdir -p \"\$webtarget\"
        for file in Controller.cs Model.cs ModInit.cs manifest.json; do
            curl -fSL --retry 3 \"\$webbase/\$file\" -o \"\$webtarget/\$file\"
        done

        gstbase=\"${CUSTOM_SOURCE_BASE}/Modules/GStreamer\"
        gsttarget=/root/lampac/module/GStreamer
        mkdir -p \"\$gsttarget/Services\" \"\$gsttarget/plugins\"
        for file in Controller.cs Services/HdrToneMappingBackend.cs Services/GStask.Pipeline.cs Services/GStask.Producer.cs plugins/gst.js; do
            curl -fSL --retry 3 \"\$gstbase/\$file\" -o \"\$gsttarget/\$file\"
        done

        # The AdminPanel is protected by the Lampac root password. If it is
        # already installed/enabled, keep its Vietnamese UI in sync too.
        admintarget=/root/lampac/module/AdminPanel
        if [ -d \"\$admintarget\" ]; then
            adminbase=\"${CUSTOM_SOURCE_BASE}/Modules/AdminPanel\"
            for file in auth.html index.html ConfigSectionGroups.cs; do
                curl -fSL --retry 3 \"\$adminbase/\$file\" -o \"\$admintarget/\$file\"
            done
        fi
    "

    ok "KKPhim, K20, WebStreamr and GStreamer files installed; removed retired VsMov/NguonC"
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

GST_SCANNER=$(find /usr/lib /usr/libexec -type f -name gst-plugin-scanner -perm -111 2>/dev/null | head -n 1 || true)
if [[ -n "$GST_SCANNER" ]]; then
    export GST_PLUGIN_SCANNER="$GST_SCANNER"
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
        pkill -f 'proot.*Core.dll' 2>/dev/null && echo "Lampac stopped" || echo "Lampac not running"
        ;;
    status)
        pgrep -f 'proot.*Core.dll' > /dev/null 2>&1 && echo "Lampac is running" || echo "Lampac not running"
        ;;
    config)
        proot-distro login ubuntu -- bash -c 'nano /root/lampac/init.conf'
        ;;
    info)
        proot-distro login ubuntu -- bash -c '
            PORT=$(grep -o "\"port\": *[0-9]*" /root/lampac/init.conf 2>/dev/null | head -1 | grep -o "[0-9]*" || echo "9118")
            echo ""
            echo "  Lampac NextGen"
            echo "  ─────────────────────────────────"
            echo "  Local:    http://localhost:$PORT"
            echo "  Config:   /root/lampac/init.conf (inside Ubuntu)"
            echo "  Start:    lampac start"
            echo "  Stop:     lampac stop"
            echo ""
        '
        ;;
    update)
        echo "Updating Lampac..."
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

            # Re-apply the custom ARM64/KKPhim/GStreamer setup after replacing the release.
            file=/root/lampac/init.conf
            if [ -f "$file" ]; then
                sed -i "s/^[[:space:]]*}:,/  },/" "$file"
                sed -i "s/\"GStreamer\",[[:space:]]*//g; s/,[[:space:]]*\"GStreamer\"//g" "$file"
                if [ -x /usr/bin/google-chrome-stable ]; then
                    browser=/usr/bin/google-chrome-stable
                elif [ -x /usr/bin/chromium ]; then
                    browser=/usr/bin/chromium
                else
                    browser=/usr/bin/google-chrome-stable
                fi
                if grep -q "^[[:space:]]*\"chromium\"[[:space:]]*:" "$file"; then
                    sed -i "/^[[:space:]]*\"chromium\"[[:space:]]*:/,/^[[:space:]]*},[[:space:]]*$/ s/\"enable\"[[:space:]]*:[[:space:]]*false/\"enable\": true/" "$file"
                    if grep -q "^[[:space:]]*\"executablePath\"[[:space:]]*:" "$file"; then
                        sed -i "s#^[[:space:]]*\"executablePath\".*#    \"executablePath\": \"$browser\",#" "$file"
                    fi
                fi
            fi

            # Do not retain providers removed from the Termux profile.
            rm -rf /root/lampac/module/OnlineVN/VsMov /root/lampac/module/OnlineVN/NguonC

            base="https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a028cf-lampac/Modules/OnlineVN/KKPhim"
            target=/root/lampac/module/OnlineVN/KKPhim
            mkdir -p "$target"
            for file in Controller.cs Model.cs ModInit.cs manifest.json; do
                curl -fSL --retry 3 "$base/$file" -o "$target/$file"
            done

            k20base="https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a028cf-lampac/Modules/OnlineVN/K20"
            k20target=/root/lampac/module/OnlineVN/K20
            mkdir -p "$k20target"
            for file in Controller.cs Model.cs ModInit.cs manifest.json; do
                curl -fSL --retry 3 "$k20base/$file" -o "$k20target/$file"
            done

            webbase="https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a028cf-lampac/Modules/OnlineENG/WebStreamr"
            webtarget=/root/lampac/module/OnlineENG/WebStreamr
            mkdir -p "$webtarget"
            for file in Controller.cs Model.cs ModInit.cs manifest.json; do
                curl -fSL --retry 3 "$webbase/$file" -o "$webtarget/$file"
            done

            gstbase="https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a028cf-lampac/Modules/GStreamer"
            gsttarget=/root/lampac/module/GStreamer
            mkdir -p "$gsttarget/Services" "$gsttarget/plugins"
            for file in Controller.cs Services/HdrToneMappingBackend.cs Services/GStask.Pipeline.cs Services/GStask.Producer.cs plugins/gst.js; do
                curl -fSL --retry 3 "$gstbase/$file" -o "$gsttarget/$file"
            done

            admintarget=/root/lampac/module/AdminPanel
            if [ -d "$admintarget" ]; then
                adminbase="https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a028cf-lampac/Modules/AdminPanel"
                for file in auth.html index.html ConfigSectionGroups.cs; do
                    curl -fSL --retry 3 "$adminbase/$file" -o "$admintarget/$file"
                done
            fi
            echo "Update complete!"
        '
        ;;
    *)
        echo "Usage: lampac {start|stop|status|config|info|update}"
        echo ""
        echo "  start   — Start Lampac server"
        echo "  stop    — Stop Lampac server"
        echo "  status  — Check if running"
        echo "  config  — Edit config (init.conf)"
        echo "  info    — Show URL and port"
        echo "  update  — Update to latest release (also restores KKPhim/Chromium setup)"
        ;;
esac
SHORTCUT
    chmod +x "$PREFIX/bin/lampac"
    ok "Shortcut 'lampac' command ready"
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

            install_chromium_in_ubuntu
            ensure_runtime_config
            install_custom_modules
            create_launcher
            ok "KKPhim + ARM64 Chromium setup applied"
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
