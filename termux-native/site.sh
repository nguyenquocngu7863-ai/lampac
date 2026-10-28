#!/data/data/com.termux/files/usr/bin/bash
# site.sh — fetch có NGÂN SÁCH request/host bằng curl_cffi (impersonate), cache ra file, log lại.
# Mục đích: luật offline-first nằm trong code, không nằm trong lời hứa của agent.
#   site.sh get <url> [out] [--force] [--limit N] [--referer URL] [--impersonate NAME]
#   site.sh check
#   site.sh reset [host]
set -uo pipefail
exec python3 "${HOME}/lampac-native/site.py" "$@"
