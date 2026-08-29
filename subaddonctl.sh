#!/usr/bin/env bash
# subaddonctl.sh — controller for the self-hosted Lampac Subs Stremio addon
# (SubDL + SubSource) that runs alongside Lampac inside the Ubuntu proot.
# Reads SubDL/SubSource API keys from /root/lampac/init.conf (SubFinder block)
# automatically; no separate env var / .env needed.
#
# Usage (from inside Termux OR inside Ubuntu):
#   bash /root/subaddonctl.sh start|stop|restart|status|logs
#   (Termux shortcut: `subaddon start|...` after install creates $PREFIX/bin/subaddon)
set -euo pipefail

DIR="/root/stremio-sub-addon"
PIDFILE="/root/.stremio-sub-addon.pid"
LOGFILE="/root/stremio-sub-addon.log"
PORT="${SUBADDON_PORT:-7000}"
INIT_CONF="${LAMPAC_INIT_CONF:-/root/lampac/init.conf}"

# ---------- 0. proot entrypoint (only runs when OUTSIDE Ubuntu) ----------
# Detect we are NOT in Ubuntu proot: no /etc/os-release listing Ubuntu, or
# we are in Termux (com.termux PREFIX exists).
in_termux_side() {
    [[ -n "${PREFIX:-}" && "${PREFIX:-}" == *com.termux* ]] && return 0
    if ! grep -qi ubuntu /etc/os-release 2>/dev/null; then return 0; fi
    return 1
}

if in_termux_side; then
    if ! proot-distro login ubuntu -- test -x /root/subaddonctl.sh 2>/dev/null; then
        echo "SubAddon chưa được cài. Chạy: bash ~/lampac/setup-termux.sh --sync-all"
        exit 1
    fi
    proot-distro login ubuntu -- /root/subaddonctl.sh "$@"
    exit $?
fi

# ---------- 1. Inside Ubuntu from here down ----------
mkdir -p "$DIR"
export PORT LAMPAC_INIT_CONF="$INIT_CONF" SUBADDON_PORT="$PORT"
cd "$DIR"

write_env_from_init_conf() {
    # Pull subdl_api_key / subsource_api_key out of init.conf without parsing
    # the full JSONC. Tolerates //-comments and trailing commas.
    local subdl="" subsrc=""
    if [[ -f "$INIT_CONF" ]]; then
        subdl=$(grep -oE '"subdl_api_key"[[:space:]]*:[[:space:]]*"[^"]*"' "$INIT_CONF" | head -1 | sed -E 's/.*"([^"]+)"$/\1/' || true)
        subsrc=$(grep -oE '"subsource_api_key"[[:space:]]*:[[:space:]]*"[^"]*"' "$INIT_CONF" | head -1 | sed -E 's/.*"([^"]+)"$/\1/' || true)
    fi
    cat > "$DIR/.env" <<EOF
PORT=$PORT
SUBDL_API_KEY=$subdl
SUBSOURCE_API_KEY=$subsrc
LAMPAC_INIT_CONF=$INIT_CONF
EOF
}

start() {
    if [[ -f "$PIDFILE" ]] && kill -0 "$(cat "$PIDFILE")" 2>/dev/null; then
        echo "stremio-sub-addon already running (pid $(cat "$PIDFILE"))"
        return 0
    fi
    [[ -d node_modules ]] || { echo "Installing node dependencies..."; npm install --omit=dev --no-audit --no-fund >/dev/null 2>&1; }
    write_env_from_init_conf
    : > "$LOGFILE"
    # Launch detached; nohup keeps it after proot login exits.
    nohup node server.js >>"$LOGFILE" 2>&1 &
    echo $! > "$PIDFILE"
    sleep 2
    if kill -0 "$(cat "$PIDFILE")" 2>/dev/null; then
        echo "stremio-sub-addon started on :$PORT (pid $(cat "$PIDFILE"))"
        echo "manifest : http://127.0.0.1:$PORT/manifest.json"
        echo "health   : http://127.0.0.1:$PORT/health"
    else
        echo "FAILED to start. Last 30 lines of log:"
        tail -30 "$LOGFILE"
        rm -f "$PIDFILE"
        return 1
    fi
}

stop() {
    if [[ -f "$PIDFILE" ]]; then
        kill "$(cat "$PIDFILE")" 2>/dev/null || true
        sleep 1
        pkill -f 'node .*stremio-sub-addon/server' 2>/dev/null || true
        rm -f "$PIDFILE"
        echo "stopped"
    else
        pkill -f 'node .*stremio-sub-addon/server' 2>/dev/null || true
        echo "not running"
    fi
}

case "${1:-status}" in
    start)   start ;;
    stop)    stop ;;
    restart) stop || true; sleep 1; start ;;
    status)
        if [[ -f "$PIDFILE" ]] && kill -0 "$(cat "$PIDFILE")" 2>/dev/null; then
            echo "running (pid $(cat "$PIDFILE")) on :$PORT"
            curl -fsS "http://127.0.0.1:$PORT/health" 2>/dev/null && echo
        else
            echo "not running"
            exit 1
        fi
        ;;
    logs|log) tail -n 80 "$LOGFILE" 2>/dev/null || echo "(no log yet)" ;;
    *)
        echo "Usage: subaddonctl.sh {start|stop|restart|status|logs}"; exit 2 ;;
esac
