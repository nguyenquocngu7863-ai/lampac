#!/data/data/com.termux/files/usr/bin/bash
# sdbg.sh — soi capture ĐÃ LƯU (offline, không bắn request): ls / find / extract / diff / unpack / audit.
# Cặp với site.sh + x.sh: hai cái kia lấy dữ liệu, cái này đọc lại. Xem `sdbg.sh --help`.
set -uo pipefail
exec python3 "${HOME}/lampac-native/sdbg.py" "$@"
