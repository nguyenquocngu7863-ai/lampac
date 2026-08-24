#!/usr/bin/env bash
#
# AIOStreams local controller for Lampac's Termux + Ubuntu proot setup.
# This script only downloads/builds/runs the official AIOStreams repository;
# it does not contain a private manifest, API key or generated configuration.
#
set -euo pipefail

# proot-distro inherits Termux's PATH. Put Ubuntu's Linux binaries first;
# otherwise /data/data/com.termux/.../node (Android) is used inside Ubuntu.
# That Android Node build adds -llog to native addons and breaks bcrypt on
# glibc/aarch64. AIOStreams needs the Linux Node installed below.
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:$PATH"

AIO_DIR="${AIO_DIR:-/root/aiostreams}"
AIO_VERSION="${AIO_VERSION:-v2.33.2}"
AIO_DEFAULT_PORT="${AIO_DEFAULT_PORT:-3002}"
ENV_FILE="$AIO_DIR/.env"
PID_FILE="$AIO_DIR/.aio.pid"
LOG_FILE="$AIO_DIR/aio.log"
INSTALL_LOG_FILE="$AIO_DIR/aio-install.log"
NODE_RUNTIME_CHANGED=0

info() { printf '  → %s\n' "$*"; }
ok() { printf '  ✓ %s\n' "$*"; }
warn() { printf '  ⚠ %s\n' "$*" >&2; }
err() { printf '  ✗ %s\n' "$*" >&2; }

require_root_dir() {
    if [[ ! -d "$(dirname "$AIO_DIR")" ]]; then
        mkdir -p "$(dirname "$AIO_DIR")"
    fi
}

env_value() {
    local key="$1"
    [[ -f "$ENV_FILE" ]] || return 0
    grep -E "^${key}=" "$ENV_FILE" | tail -n 1 | cut -d '=' -f 2- || true
}

set_env_value() {
    local key="$1"
    local value="$2"
    local escaped
    escaped=$(printf '%s' "$value" | sed 's/[\\&|]/\\&/g')

    if grep -qE "^${key}=" "$ENV_FILE" 2>/dev/null; then
        sed -i "s|^${key}=.*|${key}=${escaped}|" "$ENV_FILE"
    else
        printf '%s=%s\n' "$key" "$value" >> "$ENV_FILE"
    fi
}

ensure_env() {
    require_root_dir
    mkdir -p "$AIO_DIR"

    if [[ ! -f "$ENV_FILE" ]]; then
        if [[ -f "$AIO_DIR/.env.sample" ]]; then
            cp "$AIO_DIR/.env.sample" "$ENV_FILE"
        else
            : > "$ENV_FILE"
        fi
    fi

    local port
    port="$(env_value PORT)"
    [[ "$port" =~ ^[0-9]+$ ]] || port="$AIO_DEFAULT_PORT"

    local base
    base="$(env_value BASE_URL)"
    [[ -n "$base" ]] || set_env_value BASE_URL "http://127.0.0.1:${port}"
    [[ -n "$(env_value PORT)" ]] || set_env_value PORT "$port"
    [[ -n "$(env_value DATABASE_URI)" ]] || set_env_value DATABASE_URI 'sqlite://./data/db.sqlite'

    if [[ -z "$(env_value SECRET_KEY)" ]]; then
        set_env_value SECRET_KEY "$(openssl rand -hex 32)"
        chmod 600 "$ENV_FILE" 2>/dev/null || true
        ok "Generated a private AIOStreams SECRET_KEY"
    fi
}

node_is_new_enough() {
    command -v node >/dev/null 2>&1 || return 1
    node -e '
        const major = Number(process.versions.node.split(".")[0]);
        const linux = process.platform === "linux";
        const termux = process.execPath.startsWith("/data/data/com.termux/");
        process.exit(linux && !termux && major >= 24 ? 0 : 1);
    ' >/dev/null 2>&1
}

pnpm_is_new_enough() {
    command -v pnpm >/dev/null 2>&1 || return 1
    local pnpm_path
    pnpm_path="$(readlink -f "$(command -v pnpm)" 2>/dev/null || command -v pnpm)"
    [[ "$pnpm_path" != /data/data/com.termux/* ]] || return 1
    pnpm --version 2>/dev/null | awk -F. '{ exit !($1 >= 11) }'
}

install_dependencies() {
    if ! node_is_new_enough; then
        info "Installing Node.js 24 from the official NodeSource repository..."
        apt-get update
        apt-get install -y ca-certificates curl gnupg
        curl -fsSL https://deb.nodesource.com/setup_24.x | bash -
        apt-get install -y nodejs
        NODE_RUNTIME_CHANGED=1
    fi

    # A previous attempt may have compiled native packages with Termux's
    # Android Node. Rebuild from a clean project node_modules once Linux Node
    # is installed; keep pnpm's global store so the download is still cheap.
    if [[ "$NODE_RUNTIME_CHANGED" -eq 1 && -d "$AIO_DIR/node_modules" ]]; then
        info "Removing native modules built by Android Node..."
        rm -rf "$AIO_DIR/node_modules"
    fi

    apt-get update
    apt-get install -y git openssl python3 make g++

    if ! pnpm_is_new_enough; then
        info "Installing pnpm 11..."
        npm install --global pnpm@11.0.8
    fi

    ok "Node $(node --version), pnpm $(pnpm --version)"
}

checkout_source() {
    require_root_dir

    if [[ ! -d "$AIO_DIR/.git" ]]; then
        info "Cloning official AIOStreams ${AIO_VERSION}..."
        rm -rf "$AIO_DIR"
        git clone --depth 1 --branch "$AIO_VERSION" \
            https://github.com/Viren070/AIOStreams.git "$AIO_DIR"
    else
        info "Updating AIOStreams source tags..."
        git -C "$AIO_DIR" fetch --tags --force
        git -C "$AIO_DIR" checkout "$AIO_VERSION"
    fi
}

run_logged() {
    local label="$1"
    shift
    info "$label"

    if ! "$@" 2>&1 | tee -a "$INSTALL_LOG_FILE"; then
        err "$label failed"
        echo ""
        echo "Last lines from $INSTALL_LOG_FILE:"
        tail -n 100 "$INSTALL_LOG_FILE" 2>/dev/null || true
        return 1
    fi
}

build_source() {
    ensure_env
    cd "$AIO_DIR"
    : > "$INSTALL_LOG_FILE"
    run_logged "Installing AIOStreams dependencies" pnpm install --frozen-lockfile --reporter=append-only
    run_logged "Building AIOStreams" pnpm run build
    run_logged "Generating AIOStreams metadata" pnpm run metadata --channel=stable
    mkdir -p "$AIO_DIR/data"
    ok "AIOStreams build complete"
}

read_port() {
    local port
    port="$(env_value PORT)"
    [[ "$port" =~ ^[0-9]+$ ]] && printf '%s' "$port" || printf '%s' "$AIO_DEFAULT_PORT"
}

is_running() {
    [[ -f "$PID_FILE" ]] || return 1
    local pid
    pid="$(cat "$PID_FILE" 2>/dev/null || true)"
    [[ "$pid" =~ ^[0-9]+$ ]] || return 1
    kill -0 "$pid" 2>/dev/null
}

start_aio() {
    if [[ ! -d "$AIO_DIR/packages/server" ]]; then
        err "AIOStreams is not installed. Run: aio install"
        return 1
    fi

    ensure_env

    if is_running; then
        ok "AIOStreams is already running (PID $(cat "$PID_FILE"))"
        return 0
    fi

    rm -f "$PID_FILE"
    cd "$AIO_DIR"
    info "Starting local AIOStreams on port $(read_port)..."
    nohup bash -c 'cd "$1" && exec pnpm run start' _ "$AIO_DIR" \
        >> "$LOG_FILE" 2>&1 < /dev/null &
    local pid=$!
    printf '%s\n' "$pid" > "$PID_FILE"

    sleep 2
    if ! kill -0 "$pid" 2>/dev/null; then
        err "AIOStreams stopped during startup"
        tail -n 40 "$LOG_FILE" 2>/dev/null || true
        rm -f "$PID_FILE"
        return 1
    fi

    ok "AIOStreams started"
    info "Dashboard: http://127.0.0.1:$(read_port)/stremio/configure"
}

stop_aio() {
    local stopped=0
    if [[ -f "$PID_FILE" ]]; then
        local pid
        pid="$(cat "$PID_FILE" 2>/dev/null || true)"
        if [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null; then
            kill "$pid" 2>/dev/null || true
            stopped=1
            for _ in 1 2 3 4 5; do
                kill -0 "$pid" 2>/dev/null || break
                sleep 1
            done
        fi
    fi

    # pnpm may have handed the server to a child process. Match only this
    # installation path, never every Node process on the phone.
    pkill -f "$AIO_DIR/packages/server/dist/server" 2>/dev/null || true
    rm -f "$PID_FILE"

    if [[ "$stopped" -eq 1 ]]; then
        ok "AIOStreams stopped"
    else
        info "AIOStreams is not running"
    fi
}

status_aio() {
    local port
    port="$(read_port)"
    if is_running; then
        echo "AIOStreams is running (PID $(cat "$PID_FILE"), port $port)"
        return 0
    fi

    if pgrep -f "$AIO_DIR/packages/server/dist/server" >/dev/null 2>&1; then
        echo "AIOStreams is running (server process found, port $port)"
    else
        echo "AIOStreams is not running"
        return 1
    fi
}

info_aio() {
    local port base
    port="$(read_port)"
    base="$(env_value BASE_URL)"
    [[ -n "$base" ]] || base="http://127.0.0.1:${port}"

    echo ""
    echo "  AIOStreams local"
    echo "  ─────────────────────────────────"
    echo "  Dashboard: $base/stremio/configure"
    echo "  Port:      $port"
    echo "  Source:    $AIO_DIR"
    echo "  Data:      $AIO_DIR/data"
    echo "  Manifest:  copy the generated URL from the dashboard into Lampac AdminPanel"
    echo ""
}

install_aio() {
    install_dependencies
    checkout_source
    ensure_env
    build_source
    start_aio
    info_aio
}

update_aio() {
    if [[ ! -d "$AIO_DIR/.git" ]]; then
        err "AIOStreams is not installed. Run: aio install"
        return 1
    fi

    install_dependencies
    git -C "$AIO_DIR" fetch --tags --force
    local latest
    latest="$(git -C "$AIO_DIR" tag --list 'v*' --sort=-version:refname | head -n 1)"
    [[ -n "$latest" ]] || latest="$AIO_VERSION"
    AIO_VERSION="$latest" checkout_source
    build_source
    stop_aio
    start_aio
    info "Updated to $latest"
}

config_aio() {
    if [[ ! -f "$ENV_FILE" ]]; then
        err "AIOStreams is not installed. Run: aio install"
        return 1
    fi
    echo "Edit only bootstrap settings here. Runtime settings belong in the AIOStreams dashboard."
    echo "SECRET_KEY must not be changed after the first run."
    nano "$ENV_FILE"
}

logs_aio() {
    if [[ ! -f "$LOG_FILE" ]]; then
        info "No AIOStreams runtime log yet"
        return 0
    fi
    tail -n "${AIO_LOG_LINES:-80}" "$LOG_FILE"
}

build_log_aio() {
    if [[ ! -f "$INSTALL_LOG_FILE" ]]; then
        info "No AIOStreams install/build log yet"
        return 0
    fi
    tail -n "${AIO_LOG_LINES:-120}" "$INSTALL_LOG_FILE"
}

diagnose_aio() {
    echo "AIOStreams diagnostics"
    echo "  architecture: $(uname -m 2>/dev/null || echo unknown)"
    echo "  node:         $(node --version 2>/dev/null || echo missing)"
    echo "  node path:    $(command -v node 2>/dev/null || echo missing)"
    echo "  node platform:$(node -p 'process.platform' 2>/dev/null || echo unknown)"
    echo "  pnpm:         $(pnpm --version 2>/dev/null || echo missing)"
    echo "  pnpm path:    $(command -v pnpm 2>/dev/null || echo missing)"
    echo "  source:       $AIO_DIR"
    echo "  env file:     $ENV_FILE"
    echo ""
    build_log_aio
}

case "${1:-info}" in
    install)
        install_aio
        ;;
    update)
        update_aio
        ;;
    start)
        start_aio
        ;;
    stop)
        stop_aio
        ;;
    restart)
        stop_aio
        start_aio
        ;;
    status)
        status_aio
        ;;
    info)
        info_aio
        ;;
    config)
        config_aio
        ;;
    logs|log)
        logs_aio
        ;;
    build-log|install-log|diagnose)
        if [[ "$1" == "diagnose" ]]; then
            diagnose_aio
        else
            build_log_aio
        fi
        ;;
    *)
        echo "Usage: aio {install|update|start|stop|restart|status|info|config|logs|build-log|diagnose}"
        exit 2
        ;;
esac
