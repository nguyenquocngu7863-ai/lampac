#!/data/data/com.termux/files/usr/bin/bash
# popup.sh — verify popup long-press (/<mod>/genres?uri= + ?rel=) qua app Lampac ở 127.0.0.1.
# Xem đầy đủ: popup.sh --help
exec python3 "$(dirname "$(readlink -f "$0")")/popup.py" "$@"
