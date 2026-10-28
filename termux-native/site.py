#!/usr/bin/env python3
"""site.py — fetch 1 URL bằng curl_cffi, CÓ NGÂN SÁCH request/host, cache ra file, log lại.

Vì sao: luật "offline-first" chỉ đáng tin khi ngân sách request nằm trong code, không nằm
trong lời hứa của agent. Script này:
  - đếm request theo host (mặc định 45/host), quá hạn là từ chối;
  - URL đã tải rồi thì KHÔNG tải lại (trừ --force) → chống "quên nội dung nên fetch lại";
  - 403/429/503 hoặc dấu hiệu Cloudflare → đánh dấu host đang chặn, nhắc dùng x.sh (Chrome);
  - lưu body + header ra ~/lampac-native/tmp/capture/<host>/ để phân tích offline.

Dùng qua site.sh (xem `site.sh -h`).
"""
import hashlib
import os
import pathlib
import re
import sys
from datetime import datetime
from urllib.parse import unquote, urlparse

HOME = pathlib.Path(os.environ.get("HOME", os.path.expanduser("~")))
CAP = pathlib.Path(os.environ.get("SITE_CAPTURE", HOME / "lampac-native/tmp/capture"))
BUD = CAP / ".budget"
LIMIT_DEFAULT = int(os.environ.get("SITE_MAX", "45"))
CHALLENGE = (
    b"Just a moment",
    b"cf-chl",
    b"Attention Required",
    b"Enable JavaScript and cookies to continue",
    b"__cf_chl",
)


def host_of(url: str) -> str:
    h = urlparse(url).hostname or "unknown"
    return re.sub(r"[^A-Za-z0-9._-]", "_", h)


def file_for(url: str) -> str:
    p = urlparse(url)
    base = os.path.basename(unquote(p.path).rstrip("/")) or "index"
    base = re.sub(r"[^A-Za-z0-9._-]", "_", base)[:60] or "index"
    if "." not in base:
        base += ".html"
    if p.query:
        base += "." + hashlib.sha1(p.query.encode()).hexdigest()[:8]
    return base


def read_int(path: pathlib.Path) -> int:
    try:
        return int(path.read_text().strip() or 0)
    except Exception:
        return 0


def bump(host: str, by: int = 1) -> int:
    f = BUD / f"{host}.count"
    n = read_int(f) + by
    f.write_text(str(n))
    return n


def say(*a):
    print("[site]", *a, flush=True)


def cmd_get(url, out=None, force=False, limit=None, imp="chrome124", referer=None):
    from curl_cffi import requests as cr  # import trễ để `check` chạy được cả khi thiếu lib

    host = host_of(url)
    limit = limit or LIMIT_DEFAULT
    BUD.mkdir(parents=True, exist_ok=True)

    blocked = BUD / f"{host}.blocked"
    if blocked.exists():
        say(f"TỪ CHỐI: {host} đang bị đánh dấu chặn ({blocked.read_text().strip()[:80]})")
        say("→ dùng `x.sh <url>` (Chrome headful trên Xvnc) rồi phân tích file đã dump;")
        say("  hết chặn thì `site.sh reset %s`." % host)
        return 4

    used = read_int(BUD / f"{host}.count")
    if used >= limit:
        say(f"TỪ CHỐI: {host} đã dùng {used}/{limit} request — đủ rồi, phân tích offline đi.")
        say(f"  (muốn thêm: `site.sh get ... --limit N`; xem `site.sh check`)")
        return 3

    dst = pathlib.Path(out) if out else CAP / host / file_for(url)
    dst.parent.mkdir(parents=True, exist_ok=True)
    if dst.exists() and not force:
        say(f"ĐÃ CÓ: {dst} ({dst.stat().st_size} B) — không fetch lại. Muốn đè: --force")
        return 0

    headers = {"referer": referer or f"https://{hostparse(url)}/"}
    try:
        r = cr.get(url, impersonate=imp, timeout=40, headers=headers)
    except Exception as e:
        n = bump(host)
        say(f"LỖI {type(e).__name__}: {str(e)[:90]}")
        say(f"  ({n}/{limit} {host}) — nếu là TLS/SSL_connect thì dùng `x.sh` (Chrome thật).")
        return 5

    n = bump(host)
    body = r.content or b""
    ctype = (r.headers.get("content-type") or "").split(";")[0].strip()
    if ctype == "application/json" and dst.suffix == ".html":
        dst = dst.with_suffix(".json")
    elif "mpegurl" in ctype and dst.suffix == ".html":
        dst = dst.with_suffix(".m3u8")

    dst.write_bytes(body)
    hdr = " ".join(f"{k}: {v}" for k, v in r.headers.items() if k.lower() in
                   ("content-type", "content-length", "location", "server", "cf-mitigated", "content-range"))
    (dst.with_suffix(dst.suffix + ".headers")).write_text(f"{r.status_code} {hdr}\n")

    with (CAP / host / "requests.log").open("a") as log:
        log.write(f"{datetime.now().isoformat(timespec='seconds')} {r.status_code} {len(body)}B {url} -> {dst.name}\n")

    say(f"{r.status_code} {len(body)}B {ctype or '?'} -> {dst}  ({n}/{limit} {host})")

    if r.status_code in (403, 429, 503) or (body[:4000] and any(c in body[:4000] for c in CHALLENGE)):
        blocked.write_text(f"{datetime.now().isoformat(timespec='seconds')} status={r.status_code} ctype={ctype}")
        say(f"CẢNH BÁO: {host} trả {r.status_code}/challenge → đã đánh dấu chặn. Dùng `x.sh` (Chrome).")
        return 4

    return 0 if r.status_code < 400 else 6


def hostparse(url: str) -> str:
    return urlparse(url).netloc or "unknown"


def cmd_check():
    if not BUD.exists():
        say("chưa có request nào")
        return 0
    rows = sorted(p for p in BUD.iterdir() if p.suffix == ".count")
    if not rows:
        say("chưa có request nào")
        return 0
    say(f"ngân sách mặc định: {LIMIT_DEFAULT}/host — {CAP}")
    for p in rows:
        host = p.stem
        n = read_int(p)
        b = BUD / f"{host}.blocked"
        flag = f"  ⛔ BLOCKED: {b.read_text().strip()[:40]}" if b.exists() else ""
        size = sum(f.stat().st_size for f in (CAP / host).glob("*")) if (CAP / host).is_dir() else 0
        print(f"  {host:<34} {n:>3}/{LIMIT_DEFAULT}   {size/1024:8.1f} KB{flag}")
    return 0


def cmd_reset(host=None, all_=False):
    if not BUD.exists():
        return 0
    if not host and not all_:
        say("TỪ CHỐI: reset không kèm host sẽ xoá bộ đếm của MỌI phiên đang chạy.")
        say("  → `site.sh reset <host>` để xoá 1 host, hoặc `site.sh reset --all` nếu chắc chắn.")
        return 2
    for suffix in (".count", ".blocked"):
        for p in BUD.glob(f"{host or '*'}{suffix}"):
            p.unlink()
            say(f"xoá {p.name}")
    return 0


def usage():
    print("""site.sh — fetch có ngân sách (curl_cffi) + cache file + log

  site.sh get <url> [out] [--force] [--limit N] [--referer URL] [--impersonate NAME]
  site.sh check                 # xem đã dùng bao nhiêu request / host
  site.sh reset <host>          # xoá bộ đếm + cờ blocked của 1 host
  site.sh reset --all           # xoá TẤT CẢ (cẩn thận: dùng chung giữa các phiên)

Mặc định: 45 request/host (env SITE_MAX), lưu ở ~/lampac-native/tmp/capture/<host>/
URL đã tải thì không tải lại (trừ --force). 403/429/503 → đánh dấu chặn, nhắc dùng x.sh.""")
    return 2


def main(argv):
    if not argv or argv[0] in ("-h", "--help", "help"):
        usage()
        return 0
    cmd, rest = argv[0], argv[1:]
    if cmd == "check":
        return cmd_check()
    if cmd == "reset":
        only = [a for a in rest if not a.startswith("--")]
        return cmd_reset(only[0] if only else None, all_="--all" in rest)
    if cmd != "get" or not rest:
        return usage()

    url, out = rest[0], None
    force = False
    limit = None
    imp = "chrome124"
    referer = None
    i = 1
    while i < len(rest):
        a = rest[i]
        if a == "--force":
            force = True
        elif a == "--limit":
            i += 1
            limit = int(rest[i])
        elif a == "--referer":
            i += 1
            referer = rest[i]
        elif a == "--impersonate":
            i += 1
            imp = rest[i]
        elif out is None:
            out = a
        else:
            return usage()
        i += 1

    if not re.match(r"^https?://", url):
        say("URL phải bắt đầu bằng http:// hoặc https://")
        return 2
    return cmd_get(url, out=out, force=force, limit=limit, imp=imp, referer=referer)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
