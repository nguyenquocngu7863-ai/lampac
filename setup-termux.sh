#!/usr/bin/env bash
#
# Lampac NextGen — Installer for Termux (Android)
#
# Usage:
#   bash setup-termux.sh            # install & run
#   bash setup-termux.sh --install  # install only
#   bash setup-termux.sh --run      # run only (skip install)
#   bash setup-termux.sh --update   # update to latest release
#
set -euo pipefail

# ─── Config ──────────────────────────────────────────────────────────────────

LAMPAC_DIR="$HOME/lampac"
DOTNET_DIR="$PREFIX/share/dotnet"
DOTNET_CHANNEL="10.0"
DOTNET_BIN="$DOTNET_BIN" # resolved after install
LISTEN_PORT="${LAMPAC_PORT:-9118}"
ROOT_PASSWORD="${LAMPAC_PASSWD:-lampac}"

MODE=""
[[ "${1:-}" == "--install" ]] && MODE="install"
[[ "${1:-}" == "--run" ]]    && MODE="run"
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
    printf "  ${GREEN}--install${RESET}    Install dependencies, .NET runtime, and Lampac\n"
    printf "  ${GREEN}--run${RESET}        Run Lampac (skip install)\n"
    printf "  ${GREEN}--update${RESET}     Update Lampac to latest release\n"
    printf "  ${GREEN}--help${RESET}       Show this help\n\n"
    printf "${BOLD}Environment:${RESET}\n"
    printf "  ${CYAN}LAMPAC_PORT${RESET}     Listen port (default: 9118)\n"
    printf "  ${CYAN}LAMPAC_PASSWD${RESET}   Root password (default: lampac)\n\n"
    printf "${BOLD}Examples:${RESET}\n"
    printf "  ${DIM}# First time setup & run${RESET}\n"
    printf "  bash setup-termux.sh\n\n"
    printf "  ${DIM}# Custom port and password${RESET}\n"
    printf "  LAMPAC_PORT=8080 LAMPAC_PASSWD=mypassword bash setup-termux.sh\n\n"
    printf "  ${DIM}# Update to latest version${RESET}\n"
    printf "  bash setup-termux.sh --update\n\n"
    printf "  ${DIM}# Run without reinstalling${RESET}\n"
    printf "  bash setup-termux.sh --run\n\n"
}

# ─── Detect arch ─────────────────────────────────────────────────────────────

detect_arch() {
    case "$(uname -m)" in
        aarch64|arm64) echo "linux-arm64" ;;
        armv7l)        echo "linux-arm" ;;
        x86_64)        echo "linux-x64" ;;
        i686)          echo "linux-x64" ;;
        *)
            err "Unsupported architecture: $(uname -m)"
            exit 1
            ;;
    esac
}

# ─── Check Termux ────────────────────────────────────────────────────────────

check_termux() {
    if [[ -z "${PREFIX:-}" ]]; then
        err "This script must run inside Termux."
        err "Download Termux from https://f-droid.org/packages/com.termux/"
        exit 1
    fi
}

# ─── Step 1: Install system packages ────────────────────────────────────────

install_system_deps() {
    info "Updating package lists..."
    pkg update -y -o Dpkg::Options::="--force-confdef" 2>/dev/null || pkg update -y

    local packages=(
        curl wget unzip
        git
        jq
        libxml2
        libxslt
        openssl
    )

    info "Installing system packages..."
    for pkg in "${packages[@]}"; do
        if pkg list-installed "$pkg" 2>/dev/null | grep -q "$pkg"; then
            ok "$pkg already installed"
        else
            pkg install -y "$pkg" 2>/dev/null || warn "Failed to install $pkg (non-critical)"
            ok "$pkg installed"
        fi
    done
}

# ─── Step 2: Install .NET runtime ───────────────────────────────────────────

install_dotnet() {
    if [[ -x "$DOTNET_DIR/dotnet" ]]; then
        if "$DOTNET_DIR/dotnet" --list-runtimes 2>/dev/null | grep -q 'Microsoft.AspNetCore.App 10\.'; then
            ok "ASP.NET Core 10 runtime already installed"
            DOTNET_BIN="$DOTNET_DIR/dotnet"
            return 0
        fi
    fi

    info "Installing .NET $DOTNET_CHANNEL ASP.NET Core runtime..."

    local installer="/tmp/dotnet-install-$$"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
    chmod +x "$installer"

    mkdir -p "$DOTNET_DIR"

    # Install ASP.NET Core runtime only (smaller than full SDK)
    bash "$installer" \
        --channel "$DOTNET_CHANNEL" \
        --runtime aspnetcore \
        --install-dir "$DOTNET_DIR" \
        2>&1 | tail -3

    rm -f "$installer"

    # Create symlink
    ln -sf "$DOTNET_DIR/dotnet" "$PREFIX/bin/dotnet" 2>/dev/null || true

    if ! command -v dotnet &>/dev/null; then
        err "dotnet not found after install. Check $DOTNET_DIR"
        exit 1
    fi

    DOTNET_BIN="$(command -v dotnet)"
    ok ".NET $DOTNET_CHANNEL runtime installed"
}

# ─── Step 3: Download Lampac ────────────────────────────────────────────────

download_lampac() {
    info "Downloading Lampac NextGen release..."

    mkdir -p "$LAMPAC_DIR"
    cd "$LAMPAC_DIR"

    local zip_file="/tmp/lampac-nextgen-$$"

    # Download latest release zip
    if ! curl -fSL --retry 3 -o "$zip_file" \
        "https://github.com/lampac-nextgen/lampac/releases/latest/download/lampac-nextgen.zip"; then
        err "Download failed. Check your internet connection."
        rm -f "$zip_file"
        exit 1
    fi

    info "Extracting..."
    unzip -oq "$zip_file" -d "$LAMPAC_DIR"
    rm -f "$zip_file"

    # Handle subdirectory inside zip
    local subdirs
    subdirs=$(find "$LAMPAC_DIR" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | wc -l)
    local files_in_root
    files_in_root=$(find "$LAMPAC_DIR" -mindepth 1 -maxdepth 1 -type f 2>/dev/null | wc -l)

    if [[ "$subdirs" -eq 1 && "$files_in_root" -eq 0 ]]; then
        local only_subdir
        only_subdir=$(find "$LAMPAC_DIR" -mindepth 1 -maxdepth 1 -type d | head -n1)
        shopt -s dotglob nullglob
        mv "$only_subdir"/* "$LAMPAC_DIR"/ 2>/dev/null || true
        shopt -u dotglob nullglob
        rmdir "$only_subdir" 2>/dev/null || true
    fi

    if [[ ! -f "$LAMPAC_DIR/Core.dll" ]]; then
        err "Core.dll not found after extraction."
        exit 1
    fi

    ok "Lampac downloaded to $LAMPAC_DIR"
}

# ─── Step 4: Create config ──────────────────────────────────────────────────

setup_config() {
    cd "$LAMPAC_DIR"

    # Create init.conf if missing
    if [[ ! -f "init.conf" && ! -f "init.yaml" ]]; then
        info "Creating init.conf..."
        cat > init.conf <<'CONF'
{
  "listen": {
    "ip": "0.0.0.0",
    "port": 9118,
    "scheme": "http",
    "version": true
  },
  "lowMemoryMode": true,
  "BaseModule": {
    "SkipModules": [
      "Catalog", "DLNA", "Tracks", "Transcoding", "WebLog",
      "CacheMedia", "ProxyLimiter", "ForkPlayerXML", "MsxNative",
      "TelegramAuth", "TelegramAuthBot"
    ],
    "LoadModules": [".*"]
  },
  "chromium": { "enable": false },
  "firefox":  { "enable": false },
  "rch":      { "enable": false },
  "WAF":      { "enable": false },
  "accsdb":   { "enable": false },
  "serilog":  false,
  "LampaWeb": {
    "widgets": {
      "samsung": false,
      "lg": false
    }
  }
}
CONF
        # Replace default port with custom port
        if [[ "$LISTEN_PORT" != "9118" ]]; then
            sed -i "s/\"port\": 9118/\"port\": $LISTEN_PORT/" init.conf
        fi
        ok "init.conf created (port $LISTEN_PORT)"
    else
        ok "Config already exists"
    fi

    # Create passwd file if missing
    if [[ ! -f "passwd" ]]; then
        echo -n "$ROOT_PASSWORD" > passwd
        ok "passwd created (root password: $ROOT_PASSWORD)"
    fi
}

# ─── Step 5: Create helper scripts ──────────────────────────────────────────

create_helper_scripts() {
    cd "$LAMPAC_DIR"

    # Start script
    cat > start.sh <<'SCRIPT'
#!/usr/bin/env bash
cd "$(dirname "$0")"
export DOTNET_ROOT="${DOTNET_ROOT:-$PREFIX/share/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
exec dotnet Core.dll "$@"
SCRIPT
    chmod +x start.sh

    # Quick info script
    cat > info.sh <<'SCRIPT'
#!/usr/bin/env bash
cd "$(dirname "$0")"
PORT=$(grep -o '"port": *[0-9]*' init.conf 2>/dev/null | head -1 | grep -o '[0-9]*' || echo "9118")
IP=$(hostname -I 2>/dev/null | awk '{print $1}')
[[ -z "$IP" ]] && IP="<your-ip>"
echo ""
echo "  Lampac NextGen"
echo "  ─────────────────────────────────"
echo "  Local:    http://localhost:$PORT"
echo "  Network:  http://$IP:$PORT"
echo "  Config:   ./init.conf"
echo "  Logs:     see terminal output"
echo "  Stop:     Ctrl+C"
echo ""
SCRIPT
    chmod +x info.sh

    ok "Helper scripts created (start.sh, info.sh)"
}

# ─── Step 6: Create session save/restore helpers ─────────────────────────────

create_session_helpers() {
    # Save lampac path to bashrc for convenience
    local bashrc="$HOME/.bashrc"
    local marker="# lampac-nextgen"

    if ! grep -q "$marker" "$bashrc" 2>/dev/null; then
        cat >> "$bashrc" <<EOF

$marker
lampac() {
    case "\${1:-}" in
        start)  cd ~/lampac && bash start.sh ;;
        stop)   pkill -f 'dotnet Core.dll' && echo "Lampac stopped" ;;
        status) pgrep -f 'dotnet Core.dll' > /dev/null && echo "Lampac is running" || echo "Lampac is not running" ;;
        logs)   cd ~/lampac && tail -f /dev/null 2>&1 ;;
        config) nano ~/lampac/init.conf ;;
        info)   cd ~/lampac && bash info.sh ;;
        *)      echo "Usage: lampac {start|stop|status|config|info}" ;;
    esac
}
$marker
EOF
        ok "Added 'lampac' shortcut to ~/.bashrc"
        info "Run: source ~/.bashrc  (or restart Termux)"
    fi
}

# ─── Run Lampac ──────────────────────────────────────────────────────────────

run_lampac() {
    cd "$LAMPAC_DIR"

    if ! [[ -f "Core.dll" ]]; then
        err "Core.dll not found in $LAMPAC_DIR"
        err "Run: bash setup-termux.sh --install"
        exit 1
    fi

    bash info.sh

    export DOTNET_ROOT="${DOTNET_ROOT:-$DOTNET_DIR}"
    export DOTNET_CLI_TELEMETRY_OPTOUT=1

    info "Starting Lampac on port $LISTEN_PORT..."
    info "Press Ctrl+C to stop"
    echo ""

    exec dotnet Core.dll
}

# ─── Update ──────────────────────────────────────────────────────────────────

update_lampac() {
    if [[ ! -d "$LAMPAC_DIR" ]]; then
        err "Lampac not installed yet. Run: bash setup-termux.sh --install"
        exit 1
    fi

    info "Backing up config files..."
    local backup="/tmp/lampac-backup-$$"
    mkdir -p "$backup"
    cp -a "$LAMPAC_DIR/init.conf" "$backup/" 2>/dev/null || true
    cp -a "$LAMPAC_DIR/init.yaml" "$backup/" 2>/dev/null || true
    cp -a "$LAMPAC_DIR/passwd" "$backup/" 2>/dev/null || true
    cp -a "$LAMPAC_DIR/mods" "$backup/" 2>/dev/null || true

    download_lampac

    info "Restoring config files..."
    cp -a "$backup"/* "$LAMPAC_DIR"/ 2>/dev/null || true
    rm -rf "$backup"

    ok "Update complete"
    bash info.sh
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
        "update")
            update_lampac
            ;;
        *)
            # Full install + run
            install_system_deps
            install_dotnet
            download_lampac
            setup_config
            create_helper_scripts
            create_session_helpers

            echo ""
            ok "Installation complete!"
            echo ""
            info "Quick commands (after sourcing ~/.bashrc):"
            info "  lampac start   — Start Lampac"
            info "  lampac stop    — Stop Lampac"
            info "  lampac status  — Check if running"
            info "  lampac config  — Edit config"
            info "  lampac info    — Show URL & port"
            echo ""
            info "Or run directly:"
            info "  cd ~/lampac && bash start.sh"
            echo ""

            # Auto-run after install
            read -rp "  Start Lampac now? [Y/n]: " answer
            case "${answer:-Y}" in
                [nN]|[nN][oO])
                    info "Skipping. Run 'cd ~/lampac && bash start.sh' to start later."
                    ;;
                *)
                    run_lampac
                    ;;
            esac
            ;;
    esac
}

main
