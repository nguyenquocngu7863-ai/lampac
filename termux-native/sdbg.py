#!/usr/bin/env python3
"""sdbg — soi capture ĐÃ LƯU, không bắn request nào.

Vì sao: site.sh/x.sh lo phần lấy dữ liệu (có ngân sách request). Phần còn lại —
"trong đống HTML/JS/JSON đã lưu có gì, đổi gì, player giấu link ở đâu" — hiện phải
gõ tay grep/base64/python mỗi lần. Script này gom 6 việc đó lại, chạy hoàn toàn offline
trên ~/lampac-native/tmp/capture/<host>/.

  sdbg ls [host]              # host nào, file nào, status gì
  sdbg find <regex> [--host H] [--type html|json|js|m3u8] [--ctx N]
  sdbg extract <file...|--host H>   # gom URL media / endpoint API / embed / base64
  sdbg diff <fileA> <fileB>   # so 2 lần capture, đã chuẩn hoá token/hash/số
  sdbg unpack <file>          # P.A.C.K.E.R (Dean Edwards) -> <file>.unpacked.js
  sdbg audit [--host H]       # sức khoẻ capture: 4xx/5xx, 0 byte, CF, JSON hỏng

Không nhận URL. Muốn lấy thêm dữ liệu thì `site.sh get` / `x.sh`, rồi quay lại đây.
"""
import argparse
import base64
import difflib
import json
import os
import pathlib
import re
import sys
from collections import Counter, defaultdict

HOME = pathlib.Path(os.environ.get("HOME", os.path.expanduser("~")))
CAP = pathlib.Path(os.environ.get("SITE_CAPTURE", HOME / "lampac-native/tmp/capture"))

MEDIA = re.compile(
    r"""https?://[^\s"'<>\\]+?\.(?:m3u8|mpd|mp4|webm|ts|key|vtt)(?:\?[^\s"'<>\\]*)?""",
    re.I,
)
QUOTED_KEY = re.compile(
    r"""["'](?:file|src|source|sources|url|hls|dash|video_?url|stream_?url|playlist|link)["']\s*[:=]\s*["']([^"']{8,300})["']""",
    re.I,
)
IFRAME = re.compile(r"""<(?:iframe|embed|source)[^>]+src\s*=\s*["']([^"']{8,400})["']""", re.I)
API_PATH = re.compile(r"""["'](/[A-Za-z0-9_\-/]*(?:api|ajax|playback|ticket|player|stream|media)[A-Za-z0-9_\-/]*)["']""")
TOKEN_PARAM = re.compile(r"""[?&](data-hash|hash|token|ticket|sig|signature|expires|key)=([A-Za-z0-9._~%\-]{6,})""", re.I)
B64_BLOB = re.compile(r"""[A-Za-z0-9+/]{60,}={0,2}""")
PACKER = re.compile(r"""eval\(function\(p,a,c,k,e,[dr]\)""")
CF_MARKS = (b"Just a moment", b"cf-chl", b"Attention Required",
            b"Enable JavaScript and cookies to continue", b"__cf_chl")

TEXT_EXT = {".html", ".htm", ".json", ".js", ".m3u8", ".m3u", ".mpd", ".txt", ".xml", ".php", ""}
TYPE_EXT = {"html": (".html", ".htm"), "json": (".json",), "js": (".js",),
            "m3u8": (".m3u8", ".m3u", ".mpd")}


def say(*a):
    print("[sdbg]", *a, flush=True)


def read_text(p: pathlib.Path, limit: int | None = None) -> str:
    b = p.read_bytes()
    if limit:
        b = b[:limit]
    return b.decode("utf-8", "replace")


def status_of(p: pathlib.Path) -> str:
    h = p.with_suffix(p.suffix + ".headers")
    if not h.exists():
        return "-"
    line = h.read_text(errors="replace").strip()
    return line.split()[0] if line else "-"


def host_dir(name: str) -> pathlib.Path | None:
    d = CAP / name
    if d.is_dir():
        return d
    # cho phép gõ tắt: "7mmtv" -> "7mmtv.sx"
    hits = [p for p in CAP.iterdir() if p.is_dir() and p.name.startswith(name)]
    return hits[0] if len(hits) == 1 else None


def body_files(d: pathlib.Path):
    for f in sorted(d.iterdir()):
        if f.is_file() and not f.name.endswith(".headers") and f.name != "requests.log":
            yield f


def cmd_ls(args):
    if args.host:
        d = host_dir(args.host)
        if not d:
            say(f"không có capture cho {args.host!r} trong {CAP}")
            return 1
        files = list(body_files(d))
        total = sum(f.stat().st_size for f in files)
        say(f"{d.name} — {len(files)} file, {total/1024:.1f} KB")
        for f in files:
            print(f"  {status_of(f):>3}  {f.stat().st_size:>9} B  {f.name}")
        return 0

    if not CAP.is_dir():
        say(f"chưa có capture nào ({CAP})")
        return 1
    rows = []
    for d in sorted(CAP.iterdir()):
        if not d.is_dir():
            continue
        files = list(body_files(d))
        if not files:
            continue
        rows.append((d.name, len(files), sum(f.stat().st_size for f in files)))
    blocked = {}
    bdir = CAP / ".budget"
    if bdir.is_dir():
        for b in bdir.glob("*.blocked"):
            blocked[b.stem] = b.read_text(errors="replace").strip()[:40]
    print(f"{len(rows)} host — {CAP}")
    for name, n, size in rows:
        flag = "  ⛔ chặn" if name in blocked else ""
        print(f"  {name:<34} {n:>4} file {size/1024:>9.1f} KB{flag}")
    return 0


def cmd_find(args):
    try:
        rx = re.compile(args.regex, re.I if args.ignorecase else 0)
    except re.error as e:
        say(f"regex hỏng: {e}")
        return 2

    root = CAP if not args.host else host_dir(args.host)
    if root is None or not root.is_dir():
        say(f"không thấy capture: {args.host or CAP}")
        return 1

    exts = TYPE_EXT.get(args.type) if args.type else None
    hits = 0
    for p in sorted(root.rglob("*")):
        if not p.is_file() or p.name == "requests.log" or p.name.endswith(".headers"):
            continue
        if exts and p.suffix not in exts:
            continue
        try:
            text = read_text(p)
        except Exception:
            continue
        lines = text.splitlines()
        matched = [i for i, ln in enumerate(lines) if rx.search(ln)]
        if not matched:
            continue
        rel = p.relative_to(CAP) if p.is_relative_to(CAP) else p
        for i in matched[:args.max_per_file]:
            lo, hi = max(0, i - args.ctx), min(len(lines), i + args.ctx + 1)
            print(f"── {rel}:{i+1}")
            for j in range(lo, hi):
                mark = ">" if j == i else " "
                print(f" {mark} {lines[j][:400]}")
        hits += len(matched)
        if hits >= args.max:
            say("dừng ở --max", args.max)
            break
    if not hits:
        say("không khớp")
        return 1
    say(f"{hits} dòng khớp")
    return 0


def extract_one(p: pathlib.Path) -> dict:
    text = read_text(p)
    out = defaultdict(set)
    for m in MEDIA.findall(text):
        if re.search(r"\.(?:png|jpe?g|gif|webp|svg|ico)(?:\?|$)", m, re.I):
            continue
        out["media"].add(m)
    for m in QUOTED_KEY.findall(text):
        if m.startswith(("http", "/")) or m.endswith((".m3u8", ".mp4", ".mpd")):
            out["media"].add(m)
        elif len(m) > 20:
            out["field"].add(m)
    for m in IFRAME.findall(text):
        out["embed"].add(m)
    for m in API_PATH.findall(text):
        if len(m) > 3:
            out["api"].add(m)
    for k, v in TOKEN_PARAM.findall(text):
        out["param"].add(f"{k}={v[:40]}")
    for m in B64_BLOB.findall(text):
        try:
            raw = base64.b64decode(m + "=" * (-len(m) % 4), validate=False)
            if raw[:1] in (b"{", b"[", b"h", b"<"):
                out["base64"].add(m[:40] + f"… ({len(raw)}B, bắt đầu {raw[:1]!r})")
        except Exception:
            pass
    if PACKER.search(text):
        out["packer"].add(p.name)
    return out


def cmd_extract(args):
    files: list[pathlib.Path] = []
    for a in args.files:
        p = pathlib.Path(a)
        if not p.exists():
            d = host_dir(a)
            if d:
                files.extend(body_files(d))
            else:
                say(f"không thấy {a}")
        elif p.is_dir():
            files.extend(body_files(p))
        else:
            files.append(p)
    if args.host:
        d = host_dir(args.host)
        if d:
            files.extend(body_files(d))
    files = list(dict.fromkeys(files))
    if not files:
        say("không có file nào để soi")
        return 1

    total = defaultdict(set)
    for f in files:
        r = extract_one(f)
        if not r:
            continue
        print(f"\n## {f}")
        for kind in ("media", "embed", "api", "param", "field", "base64", "packer"):
            if kind in r:
                print(f"  {kind} ({len(r[kind])}):")
                for v in sorted(r[kind]):
                    print(f"    {v}")
                total[kind] |= r[kind]
    if not total:
        say(f"{len(files)} file — không thấy URL/endpoint nào. Thử `sdbg find '<regex>'`.")
        return 1
    say("tổng:", ", ".join(f"{k}={len(v)}" for k, v in sorted(total.items())))
    return 0


def norm(text: str) -> list[str]:
    text = re.sub(r"[0-9a-f]{12,}", "<HEX>", text)
    text = re.sub(r"\b\d{4,}\b", "<N>", text)
    text = re.sub(r"\d{4}-\d{2}-\d{2}[T ][\d:.+Z-]*", "<TS>", text)
    return [ln.rstrip() for ln in text.splitlines()]


def cmd_diff(args):
    a, b = pathlib.Path(args.a), pathlib.Path(args.b)
    for p in (a, b):
        if not p.exists():
            say(f"không thấy {p}")
            return 1
    if a.resolve() == b.resolve():
        say("hai đường dẫn là cùng một file")
        return 2
    la, lb = norm(read_text(a)), norm(read_text(b))
    d = list(difflib.unified_diff(la, lb, a.name, b.name, n=args.ctx, lineterm=""))
    if not d:
        say("giống nhau sau khi chuẩn hoá (token/hash/số/timestamp)")
        return 0
    for ln in d[:args.max]:
        print(ln[:400])
    if len(d) > args.max:
        say(f"… còn {len(d) - args.max} dòng (--max để xem thêm)")
    return 0


def b36(n: int, a: int) -> str:
    digits = "0123456789abcdefghijklmnopqrstuvwxyz"
    if n == 0:
        return "0"
    s = ""
    while n:
        s = digits[n % a] + s
        n //= a
    return s


def cmd_unpack(args):
    p = pathlib.Path(args.file)
    if p.exists():
        text = read_text(p)
        src = p
    else:
        text = args.file
        src = None
    m = re.search(r"""eval\(function\(p,a,c,k,e,[dr]\)\{.*?\}\((.*)\)\)""", text, re.S)
    if not m:
        say("không thấy khối P.A.C.K.E.R eval(function(p,a,c,k,e,d){…})")
        return 1
    merged = m.group(1)
    if args.verbose:
        say(f"payload {len(merged)} ký tự")
    if re.match(r"""\s*["']""", merged):
        # dạng chuẩn: 'BODY',62,COUNT,'a|b|c'.split('|'),0,{}
        strm = re.match(r"""\s*("(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*')""", merged, re.S)
        body = strm.group(1)[1:-1].encode().decode("unicode_escape")
        rest = merged[strm.end():]
        nums = re.match(r"""\s*,\s*(\d+)\s*,\s*(\d+)\s*,""", rest)
        if nums:
            a_, c_ = int(nums.group(1)), int(nums.group(2))
        else:
            a_, c_ = 62, 0
        words_m = re.search(r"""'([^']*)'\s*\.split""", rest)
        words = words_m.group(1).split("|") if words_m else []
        if not words_m:
            words_m = re.search(r"""["']([^"']*)["']\s*\.split""", rest)
            words = words_m.group(1).split("|") if words_m else []
    else:
        # dạng base64 (packer d): "BASE64",62,COUNT,{} — decode rồi tự tách theo base36
        strm = re.match(r"""\s*["']([A-Za-z0-9+/=]+)["']""", merged)
        if not strm:
            say("payload không nhận dạng được (không phải chuỗi body lẫn base64)")
            return 2
        raw = base64.b64decode(strm.group(1) + "=" * (-len(strm.group(1)) % 4))
        parts = raw.decode("utf-8", "replace").split("|")
        a_ = 62
        body = parts[0] if parts else ""
        words = parts[1:]

    for i, w in enumerate(words):
        body = re.sub(r"(?<![0-9a-z])" + re.escape(b36(i, a_)) + r"(?![0-9a-z])", w.replace("\\", "\\\\"), body)
    body = body.replace("\\'", "'").replace('\\"', '"')

    dst = CAP.parent / "unpacked" / ((src.stem if src else "stdin") + ".unpacked.js")
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(body)
    say(f"{len(words)} từ, {len(body)} B -> {dst}")
    return 0


def cmd_audit(args):
    if not CAP.is_dir():
        say(f"chưa có capture ({CAP})")
        return 1
    hosts = [host_dir(args.host)] if args.host else sorted(d for d in CAP.iterdir() if d.is_dir())
    hosts = [d for d in hosts if d]
    bad = 0
    for d in hosts:
        files = list(body_files(d))
        if not files:
            continue
        codes = Counter()
        empty, cf, badjson = 0, 0, 0
        for f in files:
            st = status_of(f)
            if st != "-":
                codes[st] += 1
            try:
                if f.stat().st_size == 0:
                    empty += 1
                    continue
                head = f.read_bytes()[:4000]
            except Exception:
                continue
            if any(c in head for c in CF_MARKS):
                cf += 1
            if f.suffix == ".json":
                try:
                    json.loads(read_text(f))
                except Exception:
                    badjson += 1
        errs = {k: v for k, v in codes.items() if k[0] in "45"}
        if errs or empty or cf or badjson:
            bad += 1
            bits = [f"{len(files)} file"]
            if errs:
                bits.append("status " + " ".join(f"{k}×{v}" for k, v in sorted(errs.items())))
            if cf:
                bits.append(f"CF×{cf}")
            if empty:
                bits.append(f"0B×{empty}")
            if badjson:
                bits.append(f"JSON hỏng×{badjson}")
            print(f"  ⚠ {d.name:<32} " + " · ".join(bits))
        elif args.verbose:
            print(f"  ✓ {d.name:<32} {len(files)} file")
    say(f"{len(hosts)} host — {bad} cần xem lại")
    return 0


def usage():
    print(__doc__.strip())
    return 2


def main(argv):
    ap = argparse.ArgumentParser(prog="sdbg", add_help=True, description="soi capture offline cho Lampac")
    sub = ap.add_subparsers(dest="cmd")

    p = sub.add_parser("ls", help="host/file đã capture")
    p.add_argument("host", nargs="?")
    p.set_defaults(fn=cmd_ls)

    p = sub.add_parser("find", help="grep trong capture")
    p.add_argument("regex")
    p.add_argument("--host")
    p.add_argument("--type", choices=sorted(TYPE_EXT))
    p.add_argument("--ctx", type=int, default=1)
    p.add_argument("--max", type=int, default=60)
    p.add_argument("--max-per-file", type=int, default=6)
    p.add_argument("-i", "--ignorecase", action="store_true", default=True)
    p.set_defaults(fn=cmd_find)

    p = sub.add_parser("extract", help="gom URL media / api / embed / base64 / packer")
    p.add_argument("files", nargs="*")
    p.add_argument("--host")
    p.set_defaults(fn=cmd_extract)

    p = sub.add_parser("diff", help="so 2 capture sau chuẩn hoá")
    p.add_argument("a")
    p.add_argument("b")
    p.add_argument("--ctx", type=int, default=1)
    p.add_argument("--max", type=int, default=200)
    p.set_defaults(fn=cmd_diff)

    p = sub.add_parser("unpack", help="giải P.A.C.K.E.R")
    p.add_argument("file", help="đường dẫn file capture hoặc chuỗi eval(...) trực tiếp")
    p.add_argument("-v", "--verbose", action="store_true")
    p.set_defaults(fn=cmd_unpack)

    p = sub.add_parser("audit", help="sức khoẻ capture")
    p.add_argument("--host")
    p.add_argument("-v", "--verbose", action="store_true")
    p.set_defaults(fn=cmd_audit)

    args = ap.parse_args(argv)
    if not getattr(args, "fn", None):
        return usage()
    return args.fn(args)


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        sys.exit(130)
