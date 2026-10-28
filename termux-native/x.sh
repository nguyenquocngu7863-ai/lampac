#!/data/data/com.termux/files/usr/bin/bash
# x.sh <url> [giây] — mở bằng Chrome THẬT (headful trên Xvnc :1), dump HTML + log request.
# Dùng cho: trang render bằng JS, Cloudflare challenge, chưa rõ endpoint, curl_cffi bị TLS-reset.
# Wrapper của netspy.sh: tự đặt thư mục capture + luôn dump HTML để khỏi phải fetch lần 2.
set -uo pipefail
N="${HOME}/lampac-native"
URL="${1:-}"
WAIT="${2:-10}"
if [ -z "$URL" ]; then
  echo "dùng: x.sh <url> [giây]   (mặc định 10s)" >&2
  exit 2
fi
HOST=$(printf '%s' "$URL" | sed -E 's#^[A-Za-z]+://##; s#[/:?&#].*##')
[ -n "$HOST" ] || HOST=unknown
OUTDIR="${N}/tmp/capture/${HOST}"
mkdir -p "$OUTDIR"
export NETSPY_DUMP_HTML=1
export NETSPY_OUT="${OUTDIR}/netspy.json"   # HTML ra "${NETSPY_OUT}.html"
echo "[x] chrome headful: $URL  (${WAIT}s)" >&2
echo "[x] ra: $OUTDIR/netspy.json + netspy.json.html" >&2
exec "${N}/netspy.sh" "$URL" "$WAIT"
