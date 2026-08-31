#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Test nguồn VidCore (4K) trên Lampac Termux — KHÔNG cần clone repo.
#
#   curl -fsSL "https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a05799-lampac/termux-test-vidcore.sh?cb=$(date +%s)" -o vidcore.sh && bash vidcore.sh
#
# Mode (mặc định = all):
#   chain      chỉ dò 5 bước API (không đụng file, không restart Lampac)
#   install    chép Controller.cs + ModInit.cs + manifest.json vào module, có backup
#   serve      bật Lampac tách tiến trình + gọi thẳng route video + in log
#   log        in nguyên khối "compilation error" + mọi dòng VidCore + tình trạng file module
#   rollback   khôi phục bản backup gần nhất
#   all        chain → install → serve
#
# Biến môi trường:
#   TMDB=155 SEASON=-1 EPISODE=-1     155 = The Dark Knight (movie)
#   LAMPAC_SOURCE_REF=arena/01a05799-lampac
#   VIDCORE_API=https://enc-dec.app/api
# ─────────────────────────────────────────────────────────────────────────────
set -u

MODE="${1:-all}"
LAMPAC_SOURCE_REF="${LAMPAC_SOURCE_REF:-arena/01a05799-lampac}"
SOURCE_BASE="https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/${LAMPAC_SOURCE_REF}"
MODULE=VidCore
MODULE_ROUTE=vidcore
TMDB="${TMDB:-155}"
SEASON="${SEASON:-1}"
EPISODE="${EPISODE:-1}"
GUEST_DIR=/root/lampac/module/OnlineENG/$MODULE
BAK_DIR=/root/lampac/.vidcore-bak          # ngoài cây module/ để Lampac không quét nhằm
LOG="$HOME/.vidcore-test.log"

say() { echo "[vidcore] $1"; }
ok()  { echo "[vidcore] ✓ $1"; }
no()  { echo "[vidcore] ✗ $1"; }

if ! command -v proot-distro >/dev/null 2>&1; then
    no "không tìm thấy proot-distro — script này chạy trong Termux (host), không chạy trong Ubuntu."
    exit 1
fi

guest() { proot-distro login ubuntu -- bash -s -- "$@"; }

# ── 1) Dò chuỗi API: page → enc-vidcore → POST servers → dec-vidcore ─────────
mode_chain() {
    say "Dò 5 bước của VidCore (tmdb=$TMDB season=$SEASON episode=$EPISODE)"
    guest "$TMDB" "$SEASON" "$EPISODE" "${VIDCORE_API:-https://enc-dec.app/api}" <<'CHAIN'
set -u
tmdb=${1:-155}; season=${2:-1}; episode=${3:-1}
host=https://vidcore.io
api=${4:-https://enc-dec.app/api}
UA='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36'
if [ "$season" -gt 0 ] 2>/dev/null; then page="$host/tv/$tmdb/$season/$episode"; kind=tv; else page="$host/movie/$tmdb"; kind=movie; fi
step() { echo "  ── $1"; }
die() { echo "  ✗ $1"; echo; echo "KẾT LUẬN: chuỗi VidCore hỏng ở bước này — báo lại dòng trên, đừng sửa module vội."; exit 0; }

step "1/5  GET $page"
html=$(curl -fsSL -m 40 -A "$UA" "$page" 2>/dev/null) || die "không tải được trang embed (mạng/Cloudflare chặn IP thiết bị?)"
[ -n "$html" ] || die "trang trả về rỗng"
# VidCore nhét cipher vào chuỗi JS đã escape:  \"en\":\"<cipher>\"  (fallback "en":"<cipher>")
cipher=$(printf '%s' "$html" | grep -oE '\\"(en|token)\\"[[:space:]]*:[[:space:]]*\\"[A-Za-z0-9+/=_-]+' | head -1 | sed -E 's/.*\\"//')
if [ -z "$cipher" ]; then
    cipher=$(printf '%s' "$html" | grep -oE '"(en|token)"[[:space:]]*:[[:space:]]*"[A-Za-z0-9+/=_-]+' | head -1 | sed -E 's/.*:[[:space:]]*"//')
    [ -n "$cipher" ] && echo "    (bắt được ở dạng KHÔNG escape — bản site này khác, module C# vẫn còn thiếu dung sai này)"
fi
[ -n "$cipher" ] || die "không thấy \"en\"/\"token\" trong HTML — site đã đổi cách nhét token (module cũng sẽ fail; cần sửa regex)"
echo "    ✓ cipher ${#cipher} ký tự: ${cipher%%?????}…"

step "2/5  GET $api/enc-vidcore?text=…"
enc=$(curl -fsSL -m 40 --get --data-urlencode "text=$cipher" "$api/enc-vidcore" 2>/dev/null) \
    || die "enc-vidcore chết/bị chặn — endpoint đã bị gỡ (yêu cầu enc-dec.app) hoặc sai API"
for k in servers stream token; do
    printf '%s' "$enc" | grep -q "\"$k\"" || die "enc-vidcore trả JSON không có \"$k\": $(printf '%s' "$enc" | head -c 240)"
done
servers=$(printf '%s' "$enc" | sed -n 's/.*"servers"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1 | sed 's#\\/#/#g')
stream=$(printf '%s' "$enc"  | sed -n 's/.*"stream"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'  | head -1 | sed 's#\\/#/#g')
token=$(printf '%s' "$enc"   | sed -n 's/.*"token"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'   | head -1)
echo "    ✓ servers = $servers"
echo "    ✓ stream  = $stream"

step "3/5  POST $servers ({} — đã xác minh bằng chain)
H1=(-H "X-CSRF-Token: $token" -H 'X-Requested-With: XMLHttpRequest' -H 'Content-Type: application/json' -H "Referer: $host/" -H "Origin: $host" -A "$UA")
p1=$(curl -fsSL -m 40 -X POST "${H1[@]}" "$servers" 2>/dev/null)
how="body rỗng"
if [ -z "$p1" ]; then
    p1=$(curl -fsSL -m 40 -X POST "${H1[@]}" -d '{}' "$servers" 2>/dev/null); how="body {}"
fi
[ -n "$p1" ] || die "POST servers không trả gì với cả 2 cách body — module cũng sẽ fail ở đây (cs2: thử đổi headers)"
echo "    ✓ $how, ${#p1} ký tự"

step "4/5  POST $api/dec-vidcore"
# payload có thể là chuỗi trần hoặc JSON; nhét trần vào {"text":…} sẽ hỏng nếu là chuỗi
case "$p1" in
    \{*|\[*|\"*|null*|true*|false*|[-0-9]*) ;;
    *) p1="\"$p1\"" ;;
esac
dec=$(curl -fsSL -m 40 -X POST -H 'Content-Type: application/json' -d "{\"text\":$p1}" "$api/dec-vidcore" 2>/dev/null) \
    || die "dec-vidcore lỗi"
printf '%s' "$dec" | grep -qE '"(name|data)"' || die "dec-vidcore không trả [{name,data}]: $(printf '%s' "$dec" | head -c 240)"
echo "    ✓ danh sách server: $(printf '%s' "$dec" | tr -d '\\' | grep -oE '"name"[[:space:]]*:[[:space:]]*"[^"]*"' | sed -E 's/.*"([^"]*)"$/\1/' | paste -sd, - | cut -c1-200)"

step "5/5  hop cuối: mỗi server → url (đây là phần module C# làm)"
# `result` có thể là mảng JSON hoặc một CHUỖI JSON đã escape; gỡ backslash để grep chạy được cả hai
flat=$(printf '%s' "$dec" | tr -d '\\')
first=$(printf '%s' "$flat" | grep -oE '"data"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed -E 's/.*"([^"]*)"$/\1/')
[ -n "$first" ] || die "dec-vidcore không có \"data\" cho từng server — module sẽ return null"
svname=$(printf '%s' "$flat" | grep -oE '"name"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed -E 's/.*"([^"]*)"$/\1/')
echo "    ── test server \"$svname\" → POST $stream/<data>"
p2=$(curl -fsSL -m 40 -X POST "${H1[@]}" -d '{}' "$stream/$first" 2>/dev/null)
[ -n "$p2" ] || die "POST $stream/<data> không trả gì (thiếu header? sai stream base?)"
case "$p2" in
    \{*|\[*|\"*|null*|true*|false*|[-0-9]*) ;;
    *) p2="\"$p2\"" ;;
esac
dec2=$(curl -fsSL -m 40 -X POST -H 'Content-Type: application/json' -d "{\"text\":$p2}" "$api/dec-vidcore" 2>/dev/null) \
    || die "dec-vidcore lần 2 lỗi"
flat2=$(printf '%s' "$dec2" | tr -d '\\')
m3u8=$(printf '%s' "$flat2" | grep -oE '"(url|stream_url)"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed -E 's/.*"([^"]*)"$/\1/' | sed 's#\\/#/#g')
if [ -z "$m3u8" ]; then
    echo "    ✗ giải mã được nhưng không thấy url/stream_url. JSON giải mã (300B đầu):"
    printf '%s' "$dec2" | head -c 300; echo
    echo
    echo "KẾT LUẬN: 4 bước đầu OK, hop cuối khác shape — dán khối trên cho tôi để sửa module."
    exit 0
fi
echo "    ✓ $svname → $m3u8"
case "$m3u8" in
    *m3u8*) echo "    ✓ là HLS — streamproxy của Lampac sẽ phát được" ;;
    *)      echo "    (không phải .m3u8 — có thể mp4/ts trực tiếp; module vẫn nhận vì chỉ cần http(s))" ;;
esac
echo
echo "KẾT LUẬN: cả 5 hop còn sống, công thức port sang C# là đúng. Chạy 'bash termux-test-vidcore.sh' (install + serve) để test module."
CHAIN
}

# ── 2) Chép module vào Ubuntu ─────────────────────────────────────────────────
mode_install() {
    say "Chép module từ $SOURCE_BASE"
    guest <<INSTALL || return 1
set -eu
base="$SOURCE_BASE"
dir="$GUEST_DIR"
bak="$BAK_DIR"
stamp=\$(date +%s)
mkdir -p "\$dir"
if [ -f "\$dir/Controller.cs" ]; then
    mkdir -p "\$bak"
    cp -f "\$dir"/*.cs "\$dir/manifest.json" "\$bak/" 2>/dev/null || true
    echo "  [bak] \$bak"
fi
for f in manifest.json Controller.cs ModInit.cs README.md; do
    curl -fsSL --retry 3 "\$base/Modules/OnlineENG/$MODULE/\$f?cb=\$stamp" -o "\$dir/\$f.tmp"
    mv "\$dir/\$f.tmp" "\$dir/\$f"
    echo "  [OK] \$f (\$(wc -c < "\$dir/\$f") B)"
done
grep -q 'class VidCoreController' "\$dir/Controller.cs" || { echo "  [ERR] Controller.cs không nguyên vẹn"; exit 1; }
grep -q 'IModuleOnline' "\$dir/ModInit.cs" || { echo "  [ERR] ModInit.cs không nguyên vẹn"; exit 1; }
echo "  [OK] module: \$dir"
INSTALL
    ok "Đã chép. (manifest dynamic:true → Lampac tự compile lúc khởi động)"
}

mode_rollback() {
    say "Khôi phục bản backup"
    guest <<'ROLLBACK' || return 1
set -eu
dir=/root/lampac/module/OnlineENG/VidCore
bak=/root/lampac/.vidcore-bak
if [ -d "$bak" ] && [ -f "$bak/Controller.cs" ]; then
    cp -f "$bak"/*.cs "$bak/manifest.json" "$dir/"
    echo "  [OK] đã trả lại: $dir"
else
    rm -rf "$dir"
    echo "  [OK] xoá module (không có backup, nên gỡ hẳn)"
fi
ROLLBACK
    say "Khởi động lại:  lampac restart"
}

# ── 3) Bật server, gọi route, in log ─────────────────────────────────────────
mode_serve() {
    say "Đọc port từ init.conf"
    PORT=$(guest <<'PORTSH' 2>/dev/null | tr -dc '0-9'
grep -oE '"port"[[:space:]]*:[[:space:]]*[0-9]+' /root/lampac/init.conf 2>/dev/null | head -1 | grep -oE '[0-9]+' || echo 9118
PORTSH
)
    PORT=${PORT:-9118}
    say "Port = $PORT"

    if pgrep -f '[C]ore\.dll' >/dev/null 2>&1; then
        no "Lampac đang chạy — module compile lúc khởi động, nên cần restart để nạp VidCore."
        say "Dừng tiến trình cũ rồi tự bật bản tạm có log..."
        pkill -TERM -f '[C]ore\.dll' 2>/dev/null || true
        sleep 2
    fi

    rm -f "$LOG"
    if proot-distro login ubuntu -- test -f /root/lampac-run.sh >/dev/null 2>&1; then
        nohup proot-distro login --no-kill-on-exit ubuntu -- bash /root/lampac-run.sh >"$LOG" 2>&1 </dev/null &
    else
        no "thiếu /root/lampac-run.sh → chạy thẳng dotnet Core.dll"
        nohup proot-distro login --no-kill-on-exit ubuntu -- \
            bash -c 'cd /root/lampac && exec dotnet Core.dll' >"$LOG" 2>&1 </dev/null &
    fi
    say "Chờ server lên (tối đa 90s, lần đầu còn compile module)..."
    up=0
    for _ in $(seq 1 90); do
        sleep 1
        if [ "$(curl -s -m 3 -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT/" 2>/dev/null)" != "000" ]; then up=1; break; fi
    done
    [ "$up" = 1 ] || { no "server không lên — log:"; tail -n 40 "$LOG"; return 1; }
    ok "Server đã lên."

    if ! proot-distro login ubuntu -- test -f "$GUEST_DIR/Controller.cs" >/dev/null 2>&1; then
        no "module chưa được cài → chạy: bash termux-test-vidcore.sh install"
    fi

    echo
    say "Route:  /lite/$MODULE_ROUTE"
    code=$(curl -s -o /dev/null -w '%{http_code}' -m 30 "http://127.0.0.1:$PORT/lite/$MODULE_ROUTE?tmdb_id=$TMDB&rjson=1")
    ghost=$(curl -s -o /dev/null -w '%{http_code}' -m 30 "http://127.0.0.1:$PORT/lite/$MODULE_ROUTE_khongcoayroute?tmdb_id=$TMDB")
    echo "    HTTP $code   (route ma trả $ghost — hai mã GIỐNG NHAU nghĩa là route chưa đăng ký, không phải module chạy OK)"
    if [ "$code" = "$ghost" ]; then
        no "route /lite/$MODULE_ROUTE không tồn tại → module chưa được nạp. Xem khối 'compilation error' bên dưới."
    fi

    echo
    say "Resolve: /lite/$MODULE_ROUTE/video?id=$TMDB"
    body=$(curl -s -m 90 "http://127.0.0.1:$PORT/lite/$MODULE_ROUTE/video?id=$TMDB&s=$SEASON&e=$EPISODE")
    printf '%s\n' "$body" | head -c 700; echo
    if printf '%s' "$body" | grep -q '"host"'; then
        ok "CÓ link stream — mở AdminPanel/Log để chọn host, hoặc bấm play trong Lampa."
    else
        no "chưa ra link — xem 2 khối log bên dưới."
    fi

    echo
    say "Lỗi compile module (nếu có):"
    # CSharpEval in "compilation error: <module>" rồi mới tới từng dòng `error CS…`,
    # nên phải lấy cả khối — grep cùng dòng sẽ không bao giờ khớp tên module.
    if grep -q "compilation error" "$LOG" 2>/dev/null; then
        grep -n -A 24 "compilation error" "$LOG" | head -80
    else
        echo "    (không có 'compilation error' trong log)"
    fi
    echo
    say "Log của module:"
    grep -n "VidCore:" "$LOG" | tail -15 || echo "    (không có dòng 'VidCore:' — route chưa được gọi hoặc module chưa nạp)"
    echo
    say "Log đầy đủ: $LOG   ·  Dừng server tạm: pkill -f '[C]ore.dll'   ·  Chạy lại bình thường: lampac start"
    say "Tắt hẳn VidCore: đặt \"VidCore\": { \"enable\": false } trong init.conf (lệnh: lampac config)"
}

mode_log() {
    say "Khối 'compilation error' (CSharpEval.cs in module name ở dòng riêng):"
    grep -n -A 24 "compilation error" "$LOG" 2>/dev/null | head -120 || echo "    (không có)"
    echo
    say "Mọi dòng nhắc VidCore:"
    grep -in "vidcore" "$LOG" 2>/dev/null | tail -25 || echo "    (không có)"
    echo
    say "File module trong Ubuntu:"
    proot-distro login ubuntu -- bash -s <<'LSLOG'
dir=/root/lampac/module/OnlineENG/VidCore
if [ -d "$dir" ]; then ls -la "$dir"; else echo "  ✗ không có $dir"; fi
[ -f "$dir/manifest.json" ] && { echo "  manifest:"; sed 's/^/    /' "$dir/manifest.json"; }
echo "  cache dll đã compile:"
ls -la /root/lampac/module/*.dll /root/lampac/cache 2>/dev/null | head -6 || echo "    (không có cache dll)"
LSLOG
    echo
    say "40 dòng cuối của log:"
    tail -n 40 "$LOG" 2>/dev/null || echo "    (không đọc được $LOG)"
}

case "$MODE" in
    chain)    mode_chain ;;
    log)      mode_log ;;
    install)  mode_install ;;
    rollback) mode_rollback ;;
    serve)    mode_serve ;;
    all)      mode_chain; echo; mode_install && { echo; mode_serve; } ;;
    *)        sed -n '3,24p' "$0"; exit 2 ;;
esac
