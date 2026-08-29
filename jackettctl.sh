#!/usr/bin/env bash
# Jackett controller for Lampac's Termux + Ubuntu proot setup.
set -euo pipefail

JACKETT_DIR="${JACKETT_DIR:-/root/jackett}"
JACKETT_DATA_DIR="${JACKETT_DATA_DIR:-/root/.config/Jackett}"
JACKETT_PORT="${JACKETT_PORT:-9117}"
# CoreCLR can attempt an enormous virtual GC reservation on Android/proot ARM64
# and fail with 0x8007000E despite available physical memory. The value is
# hexadecimal bytes; 0x40000000 caps Jackett's managed heap at 1 GiB.
JACKETT_GC_HEAP_HARD_LIMIT="${JACKETT_GC_HEAP_HARD_LIMIT:-40000000}"
PID_FILE="$JACKETT_DATA_DIR/jackett.pid"
LOG_FILE="$JACKETT_DATA_DIR/jackett.log"

info() { printf '  → %s\n' "$*"; }
ok() { printf '  ✓ %s\n' "$*"; }
warn() { printf '  ⚠ %s\n' "$*" >&2; }
err() { printf '  ✗ %s\n' "$*" >&2; }

archive_name() {
    local arch
    arch="$(dpkg --print-architecture 2>/dev/null || uname -m)"
    case "$arch" in
        arm64|aarch64) printf 'Jackett.Binaries.LinuxARM64.tar.gz' ;;
        amd64|x86_64) printf 'Jackett.Binaries.LinuxAMDx64.tar.gz' ;;
        armhf|armv7l) printf 'Jackett.Binaries.LinuxARM32.tar.gz' ;;
        *) err "Unsupported Jackett architecture: $arch"; return 2 ;;
    esac
}

jackett_binary() {
    if [[ -x "$JACKETT_DIR/jackett" ]]; then
        printf '%s' "$JACKETT_DIR/jackett"
    elif [[ -x "$JACKETT_DIR/Jackett/jackett" ]]; then
        printf '%s' "$JACKETT_DIR/Jackett/jackett"
    else
        return 1
    fi
}

saved_pid() {
    [[ -f "$PID_FILE" ]] || return 1
    local pid
    pid="$(cat "$PID_FILE" 2>/dev/null || true)"
    [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null || return 1
    printf '%s' "$pid"
}

dashboard_ready() {
    curl -fsS --max-time 2 \
        "http://127.0.0.1:$JACKETT_PORT/UI/Dashboard" >/dev/null 2>&1
}

is_running() {
    saved_pid >/dev/null 2>&1 || dashboard_ready
}

install_jackett() {
    local archive url staging extracted
    archive="$(archive_name)"
    url="https://github.com/Jackett/Jackett/releases/latest/download/$archive"
    staging="$(mktemp -d /tmp/jackett-install.XXXXXX)"

    info "Downloading official Jackett package for $(dpkg --print-architecture 2>/dev/null || uname -m)..."
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl tar >/dev/null
    curl -fL --retry 3 --retry-all-errors "$url" -o "$staging/jackett.tar.gz"
    tar -xzf "$staging/jackett.tar.gz" -C "$staging"

    extracted="$staging/Jackett"
    [[ -x "$extracted/jackett" ]] || { err "Downloaded archive does not contain Jackett/jackett"; return 1; }

    stop_jackett >/dev/null 2>&1 || true
    rm -rf "$JACKETT_DIR.new"
    mkdir -p "$JACKETT_DIR.new"
    cp -a "$extracted"/. "$JACKETT_DIR.new"/
    rm -rf "$JACKETT_DIR"
    mv "$JACKETT_DIR.new" "$JACKETT_DIR"
    mkdir -p "$JACKETT_DATA_DIR"
    chmod +x "$JACKETT_DIR/jackett"
    rm -rf "$staging"
    ok "Jackett installed in $JACKETT_DIR"
    if [[ "${JACKETT_AUTOSTART:-1}" != "0" ]]; then
        start_jackett
        info_jackett
    fi
}

start_jackett() {
    local binary
    binary="$(jackett_binary 2>/dev/null || true)"
    if [[ -z "$binary" ]]; then
        err "Jackett is not installed. Run: jackett install"
        return 1
    fi

    mkdir -p "$JACKETT_DATA_DIR"
    if is_running; then
        local current_pid
        current_pid="$(saved_pid 2>/dev/null || true)"
        if [[ -n "$current_pid" ]]; then
            ok "Jackett is already running (PID $current_pid)"
        else
            ok "Jackett is already running on port $JACKETT_PORT"
        fi
        return 0
    fi

    rm -f "$PID_FILE"
    info "Starting Jackett on port $JACKETT_PORT (GC limit 0x$JACKETT_GC_HEAP_HARD_LIMIT)..."
    DOTNET_GCHeapHardLimit="$JACKETT_GC_HEAP_HARD_LIMIT" \
    DOTNET_GCConserveMemory=9 \
    DOTNET_gcServer=0 \
    COMPlus_gcServer=0 \
    nohup "$binary" --NoRestart --ListenPublic --Port "$JACKETT_PORT" \
        --DataFolder "$JACKETT_DATA_DIR" >>"$LOG_FILE" 2>&1 </dev/null &
    local pid=$!
    printf '%s\n' "$pid" > "$PID_FILE"

    for _ in 1 2 3 4 5 6 7 8 9 10; do
        if dashboard_ready; then
            # Jackett can replace its bootstrap process. Require the dashboard
            # to remain available before reporting a successful start.
            sleep 2
            if dashboard_ready; then
                ok "Jackett started"
                return 0
            fi
        fi

        if ! kill -0 "$pid" 2>/dev/null; then
            err "Jackett stopped during startup"
            tail -n 50 "$LOG_FILE" 2>/dev/null || true
            if tail -n 50 "$LOG_FILE" 2>/dev/null | grep -q 'GC heap initialization failed'; then
                warn "CoreCLR GC reservation failed; retry with a lower hexadecimal limit, for example:"
                warn "JACKETT_GC_HEAP_HARD_LIMIT=20000000 jackett start"
            fi
            rm -f "$PID_FILE"
            return 1
        fi
        sleep 1
    done

    warn "Jackett process is running but the dashboard is not ready yet; check: jackett logs"
}

stop_jackett() {
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
    # The self-contained launcher may replace its bootstrap PID. Limit the
    # fallback match to this installation instead of killing arbitrary .NET apps.
    if dashboard_ready; then
        pkill -f "^${JACKETT_DIR}/(Jackett/)?jackett( |$)" 2>/dev/null || true
        stopped=1
    fi

    rm -f "$PID_FILE"
    if [[ "$stopped" -eq 1 ]]; then ok "Jackett stopped"; else info "Jackett is not running"; fi
}

status_jackett() {
    if is_running; then
        local pid
        pid="$(saved_pid 2>/dev/null || true)"
        if [[ -n "$pid" ]]; then
            echo "Jackett is running (PID $pid, port $JACKETT_PORT)"
        else
            echo "Jackett is running (dashboard responds on port $JACKETT_PORT; bootstrap PID changed)"
        fi
    else
        echo "Jackett is not running"
        return 1
    fi
}

info_jackett() {
    echo ""
    echo "  Jackett local"
    echo "  ─────────────────────────────────"
    echo "  Dashboard: http://127.0.0.1:$JACKETT_PORT/UI/Dashboard"
    echo "  Port:      $JACKETT_PORT"
    echo "  Program:   $JACKETT_DIR"
    echo "  Data:      $JACKETT_DATA_DIR"
    echo "  API key:   available in the Jackett dashboard"
    echo ""
}

logs_jackett() {
    [[ -f "$LOG_FILE" ]] || { info "No Jackett log yet"; return 0; }
    tail -n "${JACKETT_LOG_LINES:-100}" "$LOG_FILE"
}

case "${1:-info}" in
    install|update) install_jackett ;;
    start) start_jackett ;;
    stop) stop_jackett ;;
    restart) stop_jackett; start_jackett ;;
    status) status_jackett ;;
    info) info_jackett ;;
    logs|log) logs_jackett ;;
    *)
        echo "Usage: jackett {install|update|start|stop|restart|status|info|logs}"
        exit 2
        ;;
esac
