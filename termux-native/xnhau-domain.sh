#!/data/data/com.termux/files/usr/bin/bash
# xnhau-domain.sh — bảo trì XNhau khi site đổi domain: NHẬP DOMAIN, CHẠY, XONG.
#
#   ~/lampac-native/xnhau-domain.sh <domain|url>          # đổi + verify + deploy + restart + smoke
#   ~/lampac-native/xnhau-domain.sh <domain> --dry-run    # chỉ in ra sẽ sửa gì, không ghi
#   ~/lampac-native/xnhau-domain.sh --show                # domain hiện tại / cookie / relay / module
#
# Tại sao chỉ cần domain: class HTML + cơ chế stream của site KHÔNG đổi khi đổi domain.
# Khi đổi domain chỉ có 4 chỗ phải theo: host trong module (ModInit/Service), script login,
# allowlist của Worker relay, và cookie (phải login lại trên host mới).
# Script làm đúng 4 chỗ đó rồi tự kiểm chứng end-to-end.
#
# Thứ tự: verify domain sống → sửa file → check compile → deploy → deploy Worker → restart
#         → login → smoke (home/pg2/search/member/genres/vidosik/strem).
# Fail ở bước verify/compile thì KHÔNG deploy gì (đã backup, restore tự động).
#
# Tùy chọn:
#   --force            vẫn chạy dù fetch domain mới không ra card (site đang chặn tạm)
#   --no-build         bỏ qua bước check.sh
#   --no-relay         không deploy lại Worker
#   --no-login         không refresh cookie
#   --no-restart       không restart app
#   --keep-old=a,b     giữ thêm host trong allowlist relay (mặc định giữ mọi host cũ)
#   --dry-run / --show như trên
set -uo pipefail

SRC="${XNHAU_SRC:-$HOME/lampac-clean}"
NAT="${XNHAU_NATIVE:-$HOME/lampac-native}"
RUN="${XNHAU_RUN:-$HOME/lampac-run}"
MOD_REL="Modules/Adult/XNhau"
MOD_SRC="$SRC/$MOD_REL"
MOD_RUN="$RUN/module/Adult/XNhau"
LOGIN_SRC="$SRC/termux-native"
RELAY_SRC="$SRC/termux-native/cf-relay"
PORT=$(grep -o '"port": *[0-9]*' "$RUN/init.conf" 2>/dev/null | head -1 | grep -o '[0-9]*')
PORT=${PORT:-9118}
BASE="http://127.0.0.1:$PORT"
TS=$(date +%Y%m%d-%H%M%S)
BAK="$NAT/tmp/domain-bak/$TS"
LOG="$NAT/tmp/domain-$TS.log"

die() { echo "[domain] ✗ $*" >&2; exit 1; }
say() { echo "[domain] $*"; }

usage() { sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; }

NEW=""; FORCE=0; DO_BUILD=1; DO_RELAY=1; DO_LOGIN=1; DO_RESTART=1; DRY=0; SHOW=0; KEEP_EXTRA=""
while [ $# -gt 0 ]; do
  case "$1" in
    --force) FORCE=1 ;;
    --no-build) DO_BUILD=0 ;;
    --no-relay) DO_RELAY=0 ;;
    --no-login) DO_LOGIN=0 ;;
    --no-restart) DO_RESTART=0 ;;
    --dry-run) DRY=1 ;;
    --show) SHOW=1 ;;
    --keep-old=*) KEEP_EXTRA="${1#*=}" ;;
    -h|--help) usage; exit 0 ;;
    -*) die "tham số lạ: $1 (--help)" ;;
    *) NEW="$1" ;;
  esac
  shift
done

norm() {
  python3 - "$1" <<'PY'
import re, sys
u = sys.argv[1].strip().lower()
u = re.sub(r'^[a-z][a-z0-9+.-]*://', '', u)
u = re.split(r'[/?#]', u)[0]
u = u.split('@')[-1].split(':')[0].rstrip('.')
if not re.fullmatch(r'(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z]{2,}', u):
    sys.exit("domain không hợp lệ: %r" % u)
print(u)
PY
}

cur_host() {
  grep -o 'SiteHost *= *"https://[^"]*"' "$MOD_SRC/Service.cs" 2>/dev/null \
    | head -1 | sed 's#.*https://##; s#"$##'
}

# ---------- --show ----------
if [ "$SHOW" = 1 ]; then
  echo "domain hiện tại : $(cur_host)"
  echo "module runtime  : $MOD_RUN ($(ls "$MOD_RUN" 2>/dev/null | tr '\n' ' '))"
  echo "module loaded   : $(grep -c 'loaded module: XNhau' "$NAT/lampac-native.log" 2>/dev/null) lần trong log hiện tại"
  echo "relay env       : $(grep -o '^export XNHAU_RELAY=[^ ]*' "$NAT/run-lampac.sh" 2>/dev/null | head -1 | cut -d= -f2-)"
  echo "allowlist mặc định (index.js): $(grep -o 'env.ALLOW || "[^"]*"' "$RELAY_SRC/index.js" 2>/dev/null | cut -d'"' -f2)"
  echo "app             : $(curl -s -m 3 -o /dev/null -w '%{http_code}' "$BASE/" 2>/dev/null) @ $BASE"
  echo -n "cookie          : "; curl -s -m 30 "$BASE/xnhau/check" 2>/dev/null; echo
  exit 0
fi

[ -n "$NEW" ] || { usage; exit 1; }
NEW=$(norm "$NEW") || die "domain không hợp lệ: $NEW"
[ -d "$MOD_SRC" ] || die "không thấy $MOD_SRC"
[ -d "$MOD_RUN" ] || die "không thấy $MOD_RUN"

mkdir -p "$NAT/tmp"
exec > >(tee -a "$LOG") 2>&1

OLD=$(cur_host)
say "domain mới: $NEW   (hiện tại: ${OLD:-?})"
[ "$NEW" = "$OLD" ] && say "(trùng domain hiện tại — chỉ verify + deploy lại)"

# ---------- 1. verify domain sống ----------
dump_path() { awk '{for (i = 1; i <= NF; i++) if ($i == "->") { print $(i + 1); exit }}' <<<"$1"; }
cache_path() { sed -n 's/^\[site\] ĐÃ CÓ: \([^ ]*\) ([0-9]* B).*/\1/p' <<<"$1"; }
site_path() {  # $1 = dòng cuối của site.sh → đường dẫn file dump (rỗng nếu fail)
  local p; p=$(dump_path "$1"); [ -n "$p" ] || p=$(cache_path "$1"); echo "$p"
}

verify_domain() {
  local d="$1" out path code size items
  out=$("$NAT/site.sh" get "https://$d/" --force 2>&1 | tail -1)
  echo "         $out"
  code=$(awk '{print $2}' <<<"$out")
  path=$(site_path "$out")
  case "$code" in 2[0-9][0-9]) ;; *)
    echo "         fetch không phải 2xx (code=${code:-?})"
    return 1 ;;
  esac
  if [ ! -f "$path" ]; then
    echo "         không thấy file dump ($path)"
    return 1
  fi
  size=$(wc -c <"$path")
  items=$(grep -c "video/[0-9]\+/" "$path" 2>/dev/null)
  echo "         body=${size}B  link_video=$items"
  [ "$size" -gt 20000 ] && [ "$items" -ge 5 ]
}

say "1/7 verify https://$NEW/ ..."
if verify_domain "$NEW"; then
  say "    domain sống, có card video"
elif [ "$FORCE" = 1 ]; then
  say "    ⚠ verify fail nhưng có --force → vẫn tiếp tục"
else
  die "domain mới không trả về card video (301/403/chặn?). Không sửa gì cả.
        Đo thêm: ~/lampac-native/x.sh https://$NEW/ 20
        Rồi chạy lại, hoặc dùng --force nếu chắc site chỉ chặn tạm."
fi

# ---------- 2. sửa file ----------
META="$NAT/tmp/domain-$TS.json"
say "2/7 sửa host trong source ..."
python3 - "$NEW" "$SRC" "$NAT" "$RUN" "$BAK" "$DRY" "$TS" >"$META" <<'PY' || die "patch lỗi"
import datetime, json, pathlib, re, shutil, sys

new, src, nat, run, bak, dry, ts = sys.argv[1:8]
dry = dry == "1"
SRC, NAT, RUN, BAK = map(pathlib.Path, (src, nat, run, bak))

FUNC = [
    "Modules/Adult/XNhau/Service.cs",
    "Modules/Adult/XNhau/ModInit.cs",
    "termux-native/xnhau-login.js",
    "termux-native/xnhau-login.sh",
    "termux-native/cf-relay/index.js",
    "termux-run-lampac.sh",
]
README = "Modules/Adult/XNhau/README.md"

def rd(p):
    return p.read_text(encoding="utf-8", errors="surrogateescape")

def wr(p, t):
    p.write_text(t, encoding="utf-8", errors="surrogateescape")

files = [("src", rel, SRC / rel) for rel in FUNC]
files += [("nat", rel, NAT / rel) for rel in ("xnhau-login.js", "xnhau-login.sh")]
files = [(root, rel, p) for root, rel, p in files if p.exists()]

brand = re.compile(r"\bxnhau\.[a-z0-9-]+(?:\.[a-z]{2,})?\b", re.I)
# tên file hay bị regex bắt oan (xnhau.cookie, xnhau.js…)
EXT = {"cookie", "js", "sh", "cs", "conf", "config", "log", "json", "txt", "md",
       "html", "htm", "mp4", "m3u8", "jpg", "jpeg", "png", "gif", "webp", "css",
       "xml", "php", "dll", "csproj", "zip", "bak", "tmp", "pid", "lock", "sqlite"}


def is_host(h):
    h = h.lower()
    return h.rsplit(".", 1)[-1] not in EXT and "." in h


def hosts_in(t):
    found = {m.lower() for m in brand.findall(t)}
    for pat in (pat_cs, pat_js, pat_relay):
        found |= {m.lower() for m in pat.findall(t)}
    return {h for h in found if is_host(h)}

pat_cs = re.compile(
    r'(?:SiteHost\s*=\s*"https://|SisiSettings\("XNhau",\s*"https://|"referer",\s*"https://)([^"/]+)',
    re.I)
pat_js = re.compile(r"(?:cookies|goto)\(\s*['\"]https://([^/'\"\)]+)", re.I)
pat_relay = re.compile(r'env\.ALLOW\s*\|\|\s*"([^"]+)"')

olds, primary = set(), None
for _, rel, p in files:
    t = rd(p)
    olds |= hosts_in(t)
    if rel.endswith("Service.cs") and primary is None:
        m = re.search(r'SiteHost\s*=\s*"https://([^"]+)"', t)
        if m:
            primary = m.group(1).strip("/").lower()

readme_hosts = set()
try:
    readme_hosts = hosts_in(rd(SRC / README))
except FileNotFoundError:
    pass

olds = {h for h in olds if h and h != new}

def bound(h):
    return re.compile(r"(?<![\w.-])" + re.escape(h) + r"(?![\w.-])", re.I)

changed = []
def patch(root, rel, p, transform):
    t0 = rd(p)
    t1 = transform(t0)
    if t1 == t0:
        return False
    if not dry:
        d = BAK / root / rel
        d.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(p, d)
        wr(p, t1)
    changed.append(str(p))
    return True

def rep_hosts(t):
    for h in olds:
        t = bound(h).sub(new, t)
    return t

for root, rel, p in files:
    patch(root, rel, p, rep_hosts)

# README: chỉ sửa tiêu đề + thêm dòng lịch sử (KHÔNG thay hàng loạt để khỏi sai lịch sử)
def rep_readme(t):
    t = re.sub(r"^#\s*XNhau\s*\([^)]*\)", "# XNhau (%s)" % new, t, count=1, flags=re.M)
    if not olds:
        return t
    anchor = re.search(r"^Site \*\*doi domain lien tuc\*\*.*$", t, re.M)
    if not anchor:
        return t
    tail = t[anchor.end():]
    if re.search(r"`%s`" % re.escape(new), tail[:400]):
        return t
    line = ("\nDoi `%s` -> **`%s`** (`%s`) bang "
            "`~/lampac-native/xnhau-domain.sh %s`."
            % (primary or "?", new, datetime.date.today().isoformat(), new))
    return t[:anchor.end()] + line + tail

try:
    patch("src", README, SRC / README, rep_readme)
except FileNotFoundError:
    pass

# init.conf: nếu XNhau pin "host" thì đổi theo
init = RUN / "init.conf"
try:
    t = rd(init)

    def rep_init(t):
        m = re.search(r'("XNhau"\s*:\s*\{)(.*?)(\n\s*\})', t, re.S)
        if not m:
            return t
        blk = m.group(2)
        hm = re.search(r'("host"\s*:\s*"https?://)([^"]+)(")', blk)
        if not hm or hm.group(2).strip("/").lower() not in olds:
            return t
        newblk = blk[:hm.start()] + hm.group(1) + new + hm.group(3) + blk[hm.end():]
        return t[:m.start(2)] + newblk + t[m.end(2):]

    patch("run", "init.conf", init, rep_init)
except FileNotFoundError:
    pass

print(json.dumps({
    "olds": sorted(olds),
    "history": sorted(readme_hosts),
    "primary": primary,
    "changed": changed,
    "dry": dry,
}, ensure_ascii=False))
PY

OLDS=$(python3 -c 'import json,sys;print(" ".join(json.load(open(sys.argv[1]))["olds"]))' "$META")
HIST=$(python3 -c 'import json,sys;print(" ".join(json.load(open(sys.argv[1]))["history"]))' "$META" 2>/dev/null)
[ -n "$OLDS" ] && say "    host cũ thay thế: $OLDS"
say "    file đã sửa: $(python3 -c 'import json,sys;print(len(json.load(open(sys.argv[1]))["changed"]))' "$META")"
python3 -c '
import json,os,sys
m=json.load(open(sys.argv[1]))
for p in m["changed"]:
    for root in (sys.argv[2], sys.argv[3]):
        if p.startswith(root+"/"):
            print("      -", p[len(root)+1:]); break
' "$META" "$SRC" "$NAT"

restore() {
  [ -d "$BAK" ] || return 0
  say "↩ restore từ $BAK"
  [ -d "$BAK/src" ] && cp -a "$BAK/src/." "$SRC/" 2>/dev/null
  [ -d "$BAK/nat" ] && cp -a "$BAK/nat/." "$NAT/" 2>/dev/null
  [ -d "$BAK/run" ] && cp -a "$BAK/run/." "$RUN/" 2>/dev/null
  return 0
}

if [ "$DRY" = 1 ]; then
  say "3/7 --dry-run: bỏ qua compile/deploy/restart. Không ghi file nào (ngoài log)."
  say "    xong (dry-run). Log: $LOG"
  exit 0
fi

# ---------- 3. check compile ----------
if [ "$DO_BUILD" = 1 ]; then
  say "3/7 check compile ..."
  if ! "$NAT/csc/check.sh" "$MOD_SRC"; then
    restore
    die "compile lỗi → đã restore source. Xem lỗi ở trên."
  fi
  say "    compile sạch"
else
  say "3/7 bỏ qua compile (--no-build)"
fi

# ---------- 4. deploy module + login script ----------
say "4/7 deploy module + login script ..."
for f in Controller.cs ModInit.cs Service.cs manifest.json; do
  cp "$MOD_SRC/$f" "$MOD_RUN/$f" || die "copy $f lỗi"
done
[ -f "$LOGIN_SRC/xnhau-login.js" ] && cp "$LOGIN_SRC/xnhau-login.js" "$NAT/xnhau-login.js"
[ -f "$LOGIN_SRC/xnhau-login.sh" ] && { cp "$LOGIN_SRC/xnhau-login.sh" "$NAT/xnhau-login.sh"; chmod +x "$NAT/xnhau-login.sh"; }
[ -f "$LOGIN_SRC/xnhau-domain.sh" ] && { cp "$LOGIN_SRC/xnhau-domain.sh" "$NAT/xnhau-domain.sh"; chmod +x "$NAT/xnhau-domain.sh"; }
say "    → $MOD_RUN"

# ---------- 5. relay allowlist ----------
if [ "$DO_RELAY" = 1 ]; then
  say "5/7 deploy Worker relay allowlist ..."
  ALLOW=$(python3 - "$NEW" "$OLDS" "$HIST" "$KEEP_EXTRA" <<'PY'
import sys
new, olds, hist, extra = sys.argv[1:5]
keep = [new]
for grp in (olds, hist, extra):
    for h in grp.replace(",", " ").split():
        h = h.strip().lower()
        if h and h not in keep:
            keep.append(h)
for h in ("jav.guru",):
    if h not in keep:
        keep.append(h)
print(",".join(keep))
PY
)
  say "    allow = $ALLOW"
  if python3 "$RELAY_SRC/deploy.py" --allow "$ALLOW"; then
    say "    worker OK"
  else
    say "    ⚠ deploy worker fail (kiểm tra ~/.secrets/cf-token). Module vẫn dùng domain mới,"
    say "      nhưng request qua relay sẽ 403 'host not allowed' cho tới khi worker được deploy."
  fi
else
  say "5/7 bỏ qua Worker (--no-relay)"
fi

# ---------- 6. restart ----------
if [ "$DO_RESTART" = 1 ]; then
  say "6/7 restart app ..."
  nrestart >/dev/null 2>&1
  for _ in $(seq 1 45); do
    [ "$(curl -s -m 2 -o /dev/null -w '%{http_code}' "$BASE/" 2>/dev/null)" != "000" ] && break
    sleep 1
  done
  if grep -q "loaded module: XNhau" "$NAT/lampac-native.log" 2>/dev/null; then
    say "    app lên, module XNhau loaded"
  else
    say "    ⚠ chưa thấy 'loaded module: XNhau' trong log — xem: tail -40 $NAT/lampac-native.log"
  fi
else
  say "6/7 bỏ qua restart (--no-restart)"
fi

# ---------- login ----------
if [ "$DO_LOGIN" = 1 ]; then
  say "login lại (Chrome headful trên Xvnc) ..."
  R=$(curl -s -m 300 "$BASE/xnhau/login" 2>&1)
  say "    $R"
  case "$R" in
    *'"ok":true'*) ;;
    *) say "    ⚠ login fail → cookie cũ có thể đã chết. Chạy tay: $NAT/xnhau-login.sh" ;;
  esac
fi

# ---------- 7. smoke ----------
say "7/7 smoke test ..."

# chi tiết 1 video + slug uploader (1 request site.sh, thường đã có trong cache)
DETAIL=$(python3 - "$BASE" <<'PY'
import json, sys, urllib.parse, urllib.request
try:
    d = json.load(urllib.request.urlopen(sys.argv[1] + "/xnhau", timeout=120))
    v = (d.get("list") or [{}])[0].get("video", "")
    print(urllib.parse.parse_qs(urllib.parse.urlsplit(v).query).get("uri", [""])[0])
except Exception:
    print("")
PY
)
SLUG=""
if [ -n "$DETAIL" ]; then
  OUT=$("$NAT/site.sh" get "$DETAIL" 2>&1 | tail -1)
  CAP=$(site_path "$OUT")
  if [ -f "$CAP" ]; then
    SLUG=$(grep -o 'class="avatar" href="https://[^/"]*/\([^/"@]*\)/"' "$CAP" 2>/dev/null \
           | head -1 | sed 's#.*/\([^/"]*\)/"#\1#')
    [ -n "$SLUG" ] && say "    uploader slug: $SLUG"
  else
    say "    không lấy được trang detail để test member feed ($OUT)"
  fi
fi

python3 - "$BASE" "$NEW" "$SLUG" <<'PY' || SMOKE_RC=$?
import json, sys, urllib.error, urllib.parse, urllib.request

base, host, slug = sys.argv[1], sys.argv[2], (sys.argv[3] if len(sys.argv) > 3 else "")
rows = []

def get(url, headers=None, timeout=180):
    req = urllib.request.Request(url)
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, r.read(), dict(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read(), dict(e.headers)
    except Exception as e:
        return 0, str(e).encode(), {}

def check(name, fn):
    try:
        ok, detail = fn()
    except Exception as e:
        ok, detail = False, "%s: %s" % (type(e).__name__, str(e)[:80])
    rows.append((name, ok, detail))
    return ok, detail

def home():
    st, b, _ = get(base + "/xnhau")
    d = json.loads(b)
    l = d.get("list") or []
    return bool(l) and bool(d.get("total_pages")), "%d item, %s trang%s" % (
        len(l), d.get("total_pages"), "" if not l else " | " + l[0]["name"][:32])

def page2():
    st, b, _ = get(base + "/xnhau?pg=2")
    l = json.loads(b).get("list") or []
    return bool(l), "%d item" % len(l)

def search():
    st, b, _ = get(base + "/xnhau?search=gai")
    l = json.loads(b).get("list") or []
    return bool(l), "%d item" % len(l)

check("home", home)
check("pg=2", page2)
check("search", search)

detail = ""
try:
    st, b, _ = get(base + "/xnhau?pg=2")
    d = json.loads(b)
    v = (d.get("list") or [{}])[0].get("video", "")
    detail = urllib.parse.parse_qs(urllib.parse.urlsplit(v).query).get("uri", [""])[0]
except Exception:
    pass

if detail:
    q = urllib.parse.quote(detail, safe="")
    def genres():
        st, b, _ = get(base + "/xnhau/genres?uri=" + q)
        gs = json.loads(b).get("groups") or []
        up = [i["title"] for g in gs if g.get("title") == "Người đăng" for i in g.get("items") or []]
        cats = [g.get("title") for g in gs]
        return bool(up) and len(gs) >= 2, "nhóm=%s | uploader=%s" % (cats, up[:2])
    check("genres", genres)

    def vidosik():
        st, b, _ = get(base + "/xnhau/vidosik?uri=" + q)
        d = json.loads(b)
        return bool(d), " ".join(sorted(d.keys()))
    okv, vdetail = check("vidosik", vidosik)

    def strem():
        st, b, _ = get(base + "/xnhau/vidosik?uri=" + q)
        d = json.loads(b)
        if not d:
            return False, "vidosik rỗng"
        url = d.get("720p") or list(d.values())[0]
        st, b, _ = get(url, {"Range": "bytes=0-2047"})
        return st in (200, 206) and len(b) >= 1024, "HTTP %d, %dB" % (st, len(b))
    check("strem+byte", strem)

    if slug:
        def member():
            st, b, _ = get(base + "/xnhau?search=member%3A" + urllib.parse.quote(slug))
            d = json.loads(b)
            l = d.get("list") or []
            return bool(l) and bool(d.get("total_pages")), "%d item, %s trang" % (
                len(l), d.get("total_pages"))
        check("member:" + slug, member)
else:
    rows.append(("genres", False, "không lấy được uri detail từ home/pg2"))

w = max(len(r[0]) for r in rows) if rows else 4
for name, ok, detail in rows:
    print("    %-*s %s  %s" % (w, name, "OK  " if ok else "FAIL", detail))
sys.exit(0 if all(r[1] for r in rows) else 1)
PY
SMOKE_RC=${SMOKE_RC:-0}

echo
if [ "$SMOKE_RC" = 0 ]; then
  say "✔ XONG — domain $NEW chạy được (log: $LOG)"
else
  say "✗ Smoke có mục FAIL. Gợi ý:"
  say "   ~/lampac-native/site.sh get https://$NEW/        # site còn sống?"
  say "   tail -60 $NAT/lampac-native.log                  # module load/lỗi"
  say "   ~/lampac-native/xnhau-domain.sh $NEW --no-build --no-relay --no-restart --no-login"
  say "   Restore source: cp -a $BAK/src/. $SRC/ && cp -a $BAK/nat/. $NAT/"
fi
say "backup: $BAK"
exit "$SMOKE_RC"
