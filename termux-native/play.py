#!/usr/bin/env python3
"""play.py — nhập 1 link, in ra CHUỖI GIẢI + HƯỚNG GIẢI để agent tự viết module.

Mục tiêu: agent khác (không có người giải thích) chỉ cần `play.sh <link>` là biết
phải code gì cho phần player — chỗ khó nhất. Output gồm 5 khối:

  1. TRANG NÀY LÀ GÌ  — trang danh sách / detail / player / stream; nếu là list thì
                        báo luôn mẫu phân trang + tham số sort bắt được.
  2. CHUỖI GIẢI       — từng bước HTTP: status, size, header BẮT BUỘC (đo thật),
                        bóc bước kế bằng gì (nhãn + regex C# dùng được ngay).
  3. STREAM           — HLS master/media, variant, AES-128, EXT-X-MAP, segment đầu,
                        đuôi ngụy trang (.png), có token/hết hạn hay không.
  4. HƯỚNG GIẢI       — các bước + regex + sườn C# + danh sách BẪY đã đo.
  5. SMOKE            — ffprobe/ffmpeg decode thật qua route (tự chạy với URL Lampac).
  6. KẾT LUẬN         — PHÁT ĐƯỢC / CHẾT.

Nhận mọi thứ đang có trong tay:
  play.sh 'https://site/clip/abc/123/'      # trang detail
  play.sh 'https://player/embed?id=abc'     # thẳng trang player
  play.sh 'https://cdn/x.m3u8'              # thẳng stream
  play.sh 'https://cdn/x.mp4'
  play.sh 'http://127.0.0.1:9118/lite/mod/video.m3u8?uri=...'   # qua Lampac (tự SMOKE)
  play.sh card.html --base https://site/    # file HTML đã lưu
  play.sh - --json < card.html              # đọc stdin, xuất JSON cho agent
  play.sh detail.html --no-fetch            # CHỈ phân tích file, không gọi mạng

Nguyên tắc đo: mọi request dùng curl_cffi impersonate chrome124; header bắt buộc
được XÁC ĐỊNH bằng cách thử (None -> referer trang trước -> origin), không đoán.
SMOKE: curl 200 KHÔNG có nghĩa app phát được — xem khối "smoke (đo bằng ffmpeg)".
"""
import argparse
import base64
import json
import pathlib
import re
import shutil
import subprocess
import sys
import time
import urllib.parse as up

UA = ("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) "
      "Chrome/124.0.0.0 Mobile Safari/537.36")

STREAM_RE = re.compile(r"https?://[^\s\"'<>\\]+?\.(?:m3u8|mp4)(?:\?[^\s\"'<>\\]*)?", re.I)
CARD_RE = re.compile(r"data-json\s*=\s*(['\"])(.*?)\1", re.S)
# iframe: bắt CẢ data-src (lazy-load) — site để src="about:blank" rồi nhét link thật vào data-src
IFRAME_RE = re.compile(
    r"<iframe[^>]+(?:data-src|data-lazy-src|data-original|src)\s*=\s*[\"']([^\"']+)[\"']", re.I)
JS_FILE_RE = re.compile(
    r"(?:file|src|source|hls|m3u8|video_url|play_url)\s*[:=]\s*[\"']([^\"']+)[\"']", re.I)
B64_RE = re.compile(r"[\"']([A-Za-z0-9+/]{60,}={0,2})[\"']")

# meta player: hai thứ tự thuộc tính đều gặp ngoài thực tế
META_EMBED_RES = [
    re.compile(r"<meta[^>]+itemprop=[\"'](embedURL|contentUrl)[\"'][^>]*content=[\"']([^\"']+)[\"']", re.I),
    re.compile(r"<meta[^>]+content=[\"']([^\"']+)[\"'][^>]*itemprop=[\"'](embedURL|contentUrl)[\"']", re.I),
]

PAGE_HINT_RE = re.compile(
    r"href=[\"']([^\"']*(?:/page/|/paged/|/trang-|page=|paged=)[^\"']*)[\"']", re.I)
SORT_RE = re.compile(r"[?&](orderby|sort|sort_by|sapxep|order)=([A-Za-z0-9_\-]+)", re.I)

# Cache theo (url, referer, range): cùng 1 embed xuất hiện nhiều lần trong trang chỉ tải 1 lần.
_CACHE = {}

# mp4 lớn (>1 GB) không thể GET full body trong 30 s -> probe bằng Range như player thật.
# Chỉ áp cho mp4: m3u8 mà nhận 206/partial thì hỏng playlist.
RANGE_PROBE = "bytes=0-262143"


def looks_mp4(url):
    """URL trông như file/route mp4 (kể cả khi .mp4 nằm trong query/route Lampac)."""
    return bool(re.search(r"\.mp4\b", url or "", re.I))


def url_ext(url):
    """Đuôi của PATH (không tính query): '.m3u8' / '.mp4' / '' nếu không có."""
    p = up.urlparse(url or "").path.lower()
    m = re.search(r"(\.m3u8|\.mp4)$", p)
    return m.group(1) if m else ""


def say(*a):
    print(*a, flush=True)


def hr(title):
    say("")
    say(f"══ {title} ══")


# ---------------------------------------------------------------- fetch

def fetch(url, referer=None, rng=None, timeout=30, imp="chrome124"):
    key = (url, referer, rng)
    if key in _CACHE:
        return _CACHE[key]
    try:
        from curl_cffi import requests as cr
    except Exception:
        say("  ! thiếu curl_cffi: pip install curl_cffi")
        return None
    h = {}
    if referer:
        h["referer"] = referer
    if rng:
        h["range"] = rng
    try:
        r = cr.get(url, impersonate=imp, headers=h, timeout=timeout, allow_redirects=True)
    except Exception as e:
        say(f"  ! fetch lỗi {type(e).__name__}: {str(e)[:110]}")
        r = None
    _CACHE[key] = r
    return r


def origin(url):
    p = up.urlparse(url)
    return f"{p.scheme}://{p.netloc}/" if p.netloc else ""


def is_blocked(r):
    if r is None:
        return True
    if r.status_code >= 400:
        return True
    head = (r.content or b"")[:500].lstrip().lower()
    if head.startswith(b"<!doctype") or head.startswith(b"<html"):
        return any(x in head for x in (b"just a moment", b"cf-chl", b"enable javascript",
                                       b"attention required", b"checking your browser"))
    return False


def body_text(r):
    return (r.content or b"").decode("utf-8", "ignore") if r is not None else ""


def fetch_smart(url, referers, rng=None):
    """Thử lần lượt các Referer (None trước). Trả (resp, referer_đã_dùng, log)."""
    log = []
    last = None
    for ref in referers:
        r = fetch(url, referer=ref, rng=rng)
        last = r
        code = r.status_code if r is not None else 0
        log.append((ref, code))
        if not is_blocked(r):
            return r, ref, log
    return last, (referers[-1] if referers else None), log


def referer_note(log):
    for ref, code in log:
        if 200 <= code < 400:
            return "không cần Referer" if ref is None else f"BẮT BUỘC Referer: {ref}"
    tried = ", ".join("(không)" if r is None else r for r, _ in log)
    return f"KHÔNG mở được với: {tried}"


# ---------------------------------------------------------------- bóc URL

def _b36(n):
    chars = "0123456789abcdefghijklmnopqrstuvwxyz"
    if n == 0:
        return "0"
    out = ""
    while n:
        out = chars[n % 36] + out
        n //= 36
    return out


def _packer_char(c, a):
    if c < a:
        return chr(c)
    r = c % a
    return _packer_char(c // a, a) + (chr(r + 29) if r > 35 else _b36(r))


def unpack_packer(text):
    m = re.search(
        r"eval\(function\(p,a,c,k,e,d\).*?\}\('(.*?)',(\d+),(\d+),'(.*?)'\.split\('\|'\)",
        text, re.S)
    if not m:
        return text
    payload, a, c, words = m.group(1), int(m.group(2)), int(m.group(3)), m.group(4).split("|")

    def repl(mo):
        w = mo.group(0)
        try:
            i = int(w, 36)
        except ValueError:
            return w
        return words[i] if 0 <= i < len(words) and words[i] else w

    return re.sub(r"\b[0-9a-zA-Z]+\b", repl, payload)


def _walk(obj, out):
    if isinstance(obj, dict):
        u = obj.get("url") or obj.get("file") or obj.get("src")
        if isinstance(u, str) and u.startswith("http"):
            out.append({"url": u, "method": obj.get("method"), "from": "json"})
        for v in obj.values():
            _walk(v, out)
    elif isinstance(obj, list):
        for v in obj:
            _walk(v, out)


# ---------------------------------------------------------------- regex gợi ý

def cs_verbatim(pattern):
    """Đổi regex sang verbatim string C# @"..." (nhân đôi dấu ")."""
    return "@\"" + pattern.replace("\"", "\"\"") + "\""


def guess_extractor(text, url):
    """Nhìn quanh chỗ URL xuất hiện để đoán pattern bóc nó -> (nhãn, regex)."""
    i = text.find(url)
    if i < 0:
        i = text.find(url.replace("/", "\\/"))
    ctx = text[max(0, i - 300): i + len(url) + 150] if i >= 0 else ""
    low = ctx.lower()

    if 'itemprop="embedurl"' in low or "itemprop='embedurl'" in low:
        return ("meta itemprop=embedURL",
                r'itemprop="embedURL"[^>]*content="([^"]+)"')
    if 'itemprop="contenturl"' in low or "itemprop='contenturl'" in low:
        return ("meta itemprop=contentUrl",
                r'itemprop="contentUrl"[^>]*content="([^"]+)"')
    if "<iframe" in low or "data-src=" in low:
        return ("iframe src/data-src (lazy)",
                r'<iframe[^>]+(?:data-src|data-lazy-src|src)="([^"]+)"')
    if re.search(r'"file"\s*:', ctx) or "sources" in low:
        ext = "mp4" if ".mp4" in url.lower() else "m3u8"
        return (f"jwplayer sources.file ({ext})",
                r'"file"\s*:\s*"(https?://[^"]+\.' + ext + r'[^"]*)"')
    if "data-hash" in low:
        return ("thuộc tính data-hash", r'data-hash="([^"]+)"')
    if "atob(" in low:
        return ("base64/atob (xem skill lampac-deobfuscate)", None)
    if "eval(function(p,a,c,k,e,d)" in low:
        return ("P.A.C.K.E.R (xem skill lampac-deobfuscate)", None)
    if re.search(r'href="[^"]+\.m3u8', ctx, re.I):
        return ("link m3u8 trực tiếp", r'href="([^"]+\.m3u8[^"]*)"')
    ext = "mp4" if ".mp4" in url.lower() else "m3u8"
    return ("regex URL", r'https?://[^"\']+\.' + ext + r'[^"\']*')


def extract_streams(text, base=""):
    """Bóc mọi URL stream trong 1 khối HTML/JS/JSON, kèm nhãn + regex nguồn."""
    out = []
    seen = set()
    text = text.replace("\\/", "/")

    def add(u, src="regex", evidence=""):
        if not u or u in seen:
            return
        if u.startswith("//"):
            u = "https:" + u
        elif u.startswith("/") and base:
            u = up.urljoin(base, u)
        if not u.startswith("http"):
            return
        seen.add(u)
        label, rx = guess_extractor(text, u.split("?")[0]) if u in text else (f"nguồn {src}", None)
        out.append({"url": u, "from": src, "label": label, "regex": rx,
                    "evidence": evidence.strip()[:120]})

    for _, raw in CARD_RE.findall(text):
        try:
            obj = json.loads(raw.replace("&quot;", '"').replace("&#39;", "'").replace("&amp;", "&"))
        except Exception:
            continue
        cards = []
        _walk(obj, cards)
        for c in cards:
            add(c["url"], "card", raw[:80])

    for u in STREAM_RE.findall(text):
        add(u, "regex")

    for u in JS_FILE_RE.findall(text):
        if ".m3u8" in u or ".mp4" in u:
            add(u, "js")

    if "eval(function(p,a,c,k,e,d)" in text:
        for u in STREAM_RE.findall(unpack_packer(text)):
            add(u, "packer")

    for b in B64_RE.findall(text):
        try:
            dec = base64.b64decode(b + "=" * (-len(b) % 4)).decode("utf-8", "ignore")
        except Exception:
            continue
        if "#EXTM3U" in dec or ".m3u8" in dec or ".mp4" in dec:
            for u in STREAM_RE.findall(dec):
                add(u, "base64")

    out.sort(key=lambda c: (0 if ".m3u8" in c["url"] else 1,))
    return out


# ---------------------------------------------------------------- nhận dạng

def doc_kind(url, ctype, prefix=b""):
    u = url.lower()
    c = (ctype or "").lower()
    p = prefix.lstrip()[:16].upper()
    if ".m3u8" in u or "mpegurl" in c or p.startswith(b"#EXTM3U"):
        return "hls"
    if ".mp4" in u or c.startswith("video/"):
        return "mp4"
    if "json" in c:
        return "json"
    if "html" in c or "xml" in c:
        return "page"
    return "other"


def page_shape(text):
    """Nhìn 1 trang để nói nó là list hay detail, kèm mẫu phân trang/sort."""
    info = {"cards": 0, "pager": None, "sorts": [], "detail": False}
    for cls in ("video-block", "video-item", "post-item", "thumb-item",
                "movie-item", "clip-item", "entry-item"):
        n = len(re.findall(r'class=["\'][^"\']*' + cls + r'[^"\']*["\']', text))
        if n > info["cards"]:
            info["cards"] = n
    info["detail"] = bool(re.search(r'itemprop=["\'](?:embedURL|contentUrl)["\']', text, re.I))
    m = PAGE_HINT_RE.search(text)
    if m:
        href = m.group(1)
        href = re.sub(r"(/page/|/paged/|/trang-)(\d+)", r"\1{pg}", href)
        href = re.sub(r"((?:page|paged)=)(\d+)", r"\1{pg}", href)
        info["pager"] = href
    info["sorts"] = sorted({f"{k}={v}" for k, v in SORT_RE.findall(text)})[:6]
    return info


def find_ts_offset(data):
    """Segment giả ảnh: dò 0x47 cách nhau 188 byte để chứng minh ruột là MPEG-TS."""
    P, N = 188, 5
    if not data or len(data) < P * N:
        return -1
    for o in range(0, min(len(data) - P * N, 64 * 1024)):
        if all(data[o + p * P] == 0x47 for p in range(N)):
            return o
    return -1


# ---------------------------------------------------------------- HLS

def _pick_variant(master_text, base_url):
    lines = [l.strip() for l in master_text.splitlines() if l.strip()]
    best, bw = None, -1
    for i, l in enumerate(lines):
        if l.startswith("#EXT-X-STREAM-INF") and i + 1 < len(lines):
            m = re.search(r"BANDWIDTH=(\d+)", l)
            v = int(m.group(1)) if m else 0
            if v > bw:
                bw, best = v, lines[i + 1]
    return up.urljoin(base_url, best) if best else None


def inspect_hls(url, r):
    body = body_text(r)
    base = str(r.url)
    info = {"valid": body.lstrip().startswith("#EXTM3U"),
            "master": False, "variants": [], "segments": 0, "first_seg": None,
            "seg_ext": "", "disguised": False, "key": None, "map_uri": None,
            "relative": False, "tokens": ""}

    if "#EXT-X-STREAM-INF" in body:
        info["master"] = True
        lines = [l.strip() for l in body.splitlines() if l.strip()]
        for i, l in enumerate(lines):
            if l.startswith("#EXT-X-STREAM-INF") and i + 1 < len(lines):
                bw = re.search(r"BANDWIDTH=(\d+)", l)
                rs = re.search(r"RESOLUTION=([0-9]+x[0-9]+)", l)
                info["variants"].append({"bw": int(bw.group(1)) if bw else 0,
                                         "res": rs.group(1) if rs else "",
                                         "url": up.urljoin(base, lines[i + 1])})

    m = re.search(r'#EXT-X-KEY:METHOD=([^,\s]+)(?:[^#]*?URI="([^"]+)")?', body)
    if m and m.group(1).upper() != "NONE":
        info["key"] = {"method": m.group(1),
                       "uri": up.urljoin(base, m.group(2)) if m.group(2) else None}

    mm = re.search(r'#EXT-X-MAP:[^#]*?URI="([^"]+)"', body)
    if mm:
        info["map_uri"] = up.urljoin(base, mm.group(1))

    segs = [l.strip() for l in body.splitlines() if l.strip() and not l.startswith("#")]
    info["segments"] = len(segs)
    if segs:
        info["relative"] = not segs[0].startswith("http")
        su = up.urljoin(base, segs[0])
        info["first_seg"] = su
        info["seg_ext"] = pathlib.PurePosixPath(up.urlparse(su).path).suffix.lower()
        info["disguised"] = info["seg_ext"] not in (
            ".ts", ".m4s", ".mp4", ".aac", ".m4a", ".cmfv", ".cmfa", "", ".m4v")
        info["tokens"] = up.urlparse(su).query[:100]
    return info


def probe_segment(seg_url, fallback_ref):
    """Tải 2KB đầu: thử KHÔNG Referer trước, nếu chặn thì thử kèm Referer."""
    r = fetch(seg_url, referer=None, rng="bytes=0-2047")
    used = None
    note = ""
    if is_blocked(r) or not (r.content or b""):
        r2 = fetch(seg_url, referer=fallback_ref, rng="bytes=0-2047")
        if not is_blocked(r2) and (r2.content or b""):
            r, used = r2, fallback_ref
            note = f"segment CHỈ chạy khi kèm Referer: {fallback_ref}"
        elif r is None:
            return {"ok": False, "status": 0, "bytes": 0, "ctype": "", "ts_offset": -1, "note": "không tải được"}
    data = r.content or b""
    return {"ok": r.status_code in (200, 206) and len(data) > 0 and not is_blocked(r),
            "status": r.status_code,
            "bytes": len(data),
            "ctype": (r.headers.get("content-type") or "?"),
            "ts_offset": find_ts_offset(data),
            "note": note}


# ---------------------------------------------------------------- crawl

def other_players(text, base, chosen, cap=6):
    """Liệt kê các player/iframe KHÁC trên trang (site nhiều server)."""
    out = []
    for m in IFRAME_RE.finditer(text):
        u = m.group(1).strip()
        if u in ("", "about:blank") or u.lower().startswith("javascript:"):
            continue
        u = up.urljoin(base, u)
        if u != chosen and u not in out:
            out.append(u)
    for rx in META_EMBED_RES:
        for m in rx.finditer(text):
            u = up.urljoin(base, m.group(2) if rx is META_EMBED_RES[0] else m.group(1))
            if u != chosen and u not in out:
                out.append(u)
    return out[:cap]


def crawl(start, start_referers, max_hops=4):
    """Đi từ link user đưa tới stream. Trả (chain, stream_resp)."""
    chain = []
    url = start
    referers = start_referers
    for step in range(1, max_hops + 1):
        rng = RANGE_PROBE if looks_mp4(url) else None
        r, used, log = fetch_smart(url, referers, rng=rng)
        code = r.status_code if r is not None else 0
        text = body_text(r)
        kind = doc_kind(url, (r.headers.get("content-type") if r is not None else ""),
                        (r.content or b"")[:32] if r is not None else b"")
        entry = {"step": step, "kind": kind, "url": url, "status": code,
                 "size": len(r.content or b"") if r is not None else 0,
                 "ctype": (r.headers.get("content-type") or "?") if r is not None else "?",
                 "referer": used, "referer_note": referer_note(log),
                 "extractor": None, "next": None, "note": "", "other_players": []}

        # probe Range: body chỉ là mẩu đầu -> lấy size THẬT từ Content-Range "/<total>"
        if rng and r is not None:
            mcr = re.search(r"/(\d+)\s*$", r.headers.get("content-range") or "")
            if mcr:
                entry["size"] = int(mcr.group(1))
                entry["note"] = f"probe {rng} (size thật từ Content-Range)"

        chain.append(entry)

        if r is None or code >= 400:
            entry["note"] = "tải hỏng"
            return chain, None

        if kind in ("hls", "mp4"):
            return chain, r

        if kind == "page":
            follow_referers = [None, url, origin(url)]
            nxt, label, rx = pick_next(str(r.url), text)
            if nxt:
                entry["extractor"] = {"label": label, "regex": rx}
                entry["next"] = nxt
                entry["other_players"] = other_players(text, str(r.url), nxt)
                url = nxt
                referers = follow_referers
                continue
            entry["other_players"] = other_players(text, str(r.url), None)

        entry["note"] = "không thấy bước kế (embed/iframe/stream)"
        return chain, None
    return chain, None


def pick_next(base_url, text):
    """Ưu tiên: meta embedURL > iframe > stream trong trang."""
    for rx in META_EMBED_RES:
        m = rx.search(text)
        if m:
            u = up.urljoin(base_url, m.group(2) if rx is META_EMBED_RES[0] else m.group(1))
            label = "meta itemprop=embedURL" if "embed" in (m.group(1) or "").lower() \
                else "meta itemprop=contentUrl"
            rxp = (r'itemprop="embedURL"[^>]*content="([^"]+)"' if "embed" in label.lower()
                   else r'itemprop="contentUrl"[^>]*content="([^"]+)"')
            return u, label, rxp

    for m in IFRAME_RE.finditer(text):
        u = m.group(1).strip()
        if u in ("", "about:blank") or u.lower().startswith("javascript:"):
            continue
        return up.urljoin(base_url, u), "iframe src/data-src (lazy)", \
            r'<iframe[^>]+(?:data-src|data-lazy-src|src)="([^"]+)"'

    cands = extract_streams(text, base_url)
    if cands:
        c = cands[0]
        return c["url"], c["label"], c["regex"]
    return None, None, None


# ---------------------------------------------------------------- hướng giải

def build_advice(chain, hls, seg):
    lines = []
    for e in chain:
        kind = {"page": "trang", "hls": "playlist HLS", "mp4": "file MP4",
                "json": "JSON", "other": "khác"}.get(e["kind"], e["kind"])
        note = e["referer_note"]
        hdr = "User-Agent" if note.startswith("không") else f"User-Agent; {note}"
        lines.append(f"Bước {e['step']} — GET {kind}")
        lines.append(f"  url     : {e['url']}")
        lines.append(f"  status  : {e['status']} ({e['size']}B, {e['ctype']})")
        lines.append(f"  header  : {hdr}")
        if e["extractor"]:
            lines.append(f"  bóc tiếp: {e['extractor']['label']}")
            if e["extractor"]["regex"]:
                lines.append(f"  regex   : {e['extractor']['regex']}")
                lines.append(f"  C#      : Regex.Match(html, {cs_verbatim(e['extractor']['regex'])})")
        lines.append("")

    if hls:
        lines.append("Trả stream về Lampac")
        lines.append("  kiểu    : hls = true (giao nguyên playlist cho hls.js, đừng tự chọn variant)")
        lines.append("  proxy   : " + (
            "BẬT (m3u8/segment cần Referer — xem BẪY)" if seg and seg.get("note")
            else "KHÔNG cần (m3u8 + segment tải trực tiếp được)"))
    elif chain and chain[-1]["kind"] == "mp4":
        lines.append("Trả stream về Lampac")
        lines.append("  kiểu    : mp4 trực tiếp (hls = false)")

    return lines


def build_skeleton(chain, hls):
    """Sinh sườn C# ĐÚNG thứ tự: tải trang N -> bóc URL N+1 -> tải N+1 -> ..."""
    out = []
    html_var = None
    for i, e in enumerate(chain):
        if i == 0 and e["kind"] in ("page", "json", "other"):
            html_var = "html1"
            out.append(f'string {html_var} = await http.Get("{e["url"]}");')
        if not (e["extractor"] and e["extractor"]["regex"]):
            continue
        nxt = chain[i + 1] if i + 1 < len(chain) else None
        url_var = f"url{i + 2}"
        out.append(f'string {url_var} = Regex.Match({html_var}, '
                   f'{cs_verbatim(e["extractor"]["regex"])}).Groups[1].Value;')
        out.append(f'if (string.IsNullOrEmpty({url_var})) '
                   f'return e.Error("bước {e["step"]}: không bóc được stream");')
        if nxt is None:
            break
        if nxt["kind"] in ("hls", "mp4"):
            h = "true" if nxt["kind"] == "hls" else "false"
            out.append(f'return e.Stream(new StreamResult({url_var}) {{ hls = {h} }});')
            break
        # tải trang kế (kèm Referer nếu bước kế bắt buộc)
        hdr = ""
        if "BẮT BUỘC Referer" in nxt["referer_note"] and nxt["referer"]:
            hdr = f', new() {{ ["Referer"] = "{nxt["referer"]}" }}'
        html_var = f"html{i + 2}"
        out.append(f'string {html_var} = await http.Get({url_var}{hdr});')
    return out


def one_liner(chain, hls, seg):
    """1 dòng để agent đọc là hiểu phải làm gì."""
    names = {"page": "trang", "hls": "m3u8", "mp4": "mp4", "json": "json", "other": "?"}
    hops = " → ".join(names.get(e["kind"], e["kind"]) for e in chain)
    reqs = [f"bước {e['step']} bắt buộc Referer" for e in chain
            if "BẮT BUỘC Referer" in e["referer_note"]]
    ret = ("trả hls=true" if hls else "trả mp4") if chain else "không ra stream"
    proxy = "CẦN proxy" if (seg and seg.get("note")) else "không cần proxy"
    return (f"{len(chain)} bước: {hops} · "
            + ("; ".join(reqs) if reqs else "không cần header đặc biệt")
            + f" · {ret}, {proxy}")


def build_traps(chain, hls, seg):
    out = []
    for e in chain:
        if e["extractor"] and "BẮT BUỘC Referer" in e["referer_note"]:
            out.append(f"bước {e['step']} ({e['kind']}): thiếu Referer = 403/block. "
                       f"Header đúng: Referer: {e['referer']}")
    if hls:
        if hls.get("disguised"):
            tso = seg.get("ts_offset") if seg else -1
            extra = (f" — dò được MPEG-TS ở offset {tso}" if tso is not None and tso >= 0 else "")
            out.append(f"segment đuôi '{hls['seg_ext']}' nhưng KHÔNG phải ảnh{extra}. "
                       f"Đừng kết luận CDN trả decoy.")
        if hls.get("key"):
            out.append(f"playlist có EXT-X-KEY (AES-{hls['key']['method']}). Giữ nguyên URI key, "
                       f"không tự tải/giải — hls.js lo.")
        if hls.get("relative"):
            out.append("playlist chứa segment dạng tương đối: trả NGUYÊN URL playlist cho hls.js, "
                       "đừng tự ghép/tải segment ở server.")
        if hls.get("tokens"):
            out.append(f"segment có query token ({hls['tokens']}) → có thể hết hạn, cache ngắn.")
        if hls.get("master") and hls.get("variants"):
            out.append(f"là master playlist ({len(hls['variants'])} variant) → trả nguyên master, "
                       f"để hls.js tự ABR.")
        if seg and seg.get("note"):
            out.append(seg["note"])
        if not hls.get("valid"):
            out.append("tải được nhưng KHÔNG bắt đầu bằng #EXTM3U — kiểm tra lại (có thể là trang lỗi).")
    for e in chain:
        if e["extractor"] and e["extractor"]["regex"] is None:
            out.append(f"bước {e['step']}: URL bị mã hoá ({e['extractor']['label']}) "
                       f"→ nạp skill lampac-deobfuscate, giải offline trước khi viết C#.")
    return out


def print_chain(chain):
    hr("CHUỖI GIẢI")
    for e in chain:
        line = f"{e['step']}. [{e['kind']:5s}] {e['status']} {e['url']}"
        say(line)
        say(f"     {e['size']}B  {e['ctype']}  ·  {e['referer_note']}")
        if e["extractor"]:
            say(f"     ↳ bóc bằng: {e['extractor']['label']}")
            if e["extractor"]["regex"]:
                say(f"       regex: {e['extractor']['regex']}")
        if e["note"]:
            say(f"     ! {e['note']}")
        for i, u in enumerate(e.get("other_players") or []):
            say(f"     {'↳ player khác:' if i == 0 else '             '} {u[:130]}")


def print_stream(hls, seg, url):
    hr("STREAM")
    if not hls:
        say(f"{url}")
        return
    say(f"playlist : {url}")
    say(f"loại     : {'master (nhiều variant)' if hls['master'] else 'media (danh sách segment)'}")
    if hls["master"]:
        for v in hls["variants"][:6]:
            say(f"   · {v['res'] or '?':>10s}  {v['bw']//1000 if v['bw'] else '?'}kbps  {v['url'][:100]}")
    say(f"segment  : {hls['segments']} dòng  ·  đuôi '{hls['seg_ext'] or '?'}'"
        f"{'  (NGỤY TRANG)' if hls['disguised'] else ''}")
    if hls["segments"] == 0:
        say("   ! playlist không có segment — có thể chỉ là master rỗng hoặc trang lỗi")
    if hls["key"]:
        say(f"mã hoá   : AES-{hls['key']['method']}  key={hls['key']['uri']}")
    if hls["map_uri"]:
        say(f"init map : {hls['map_uri'][:110]}")
    if seg:
        say(f"đoạn đầu : {seg['status']} {seg['bytes']}B {seg['ctype']}"
            f"{'  · TS offset ' + str(seg['ts_offset']) if seg['ts_offset'] >= 0 else ''}")
        if seg.get("note"):
            say(f"   ! {seg['note']}")


def print_advice(chain, hls, seg):
    hr("HƯỚNG GIẢI")
    for l in build_advice(chain, hls, seg):
        say(l)
    traps = build_traps(chain, hls, seg)
    if traps:
        say("BẪY ĐÃ ĐO")
        for t in traps:
            say(f"  • {t}")
    sk = build_skeleton(chain, hls)
    if sk:
        say("")
        say("Sườn C# (đặt vào ResolveAsync; cấu trúc chuẩn xem Modules/Adult/Javtiful):")
        for l in sk:
            say("  " + l)


# ---------------------------------------------------------------- chế độ chỉ-đọc-file

def analyze_local(text, base, limit):
    hr("TRANG NÀY LÀ GÌ")
    sh = page_shape(text)
    loai = "detail (có player/embed)" if sh["detail"] else \
        (f"danh sách (~{sh['cards']} card)" if sh["cards"] >= 4 else "chưa rõ (ít card)")
    say(f"loại    : {loai}")
    say(f"sort    : {', '.join(sh['sorts']) or '—'}")
    say(f"pager   : {sh['pager'] or 'không thấy (có thể infinite scroll)'}")

    nxt, label, rx = pick_next(base, text)
    if nxt:
        hr("BƯỚC KẾ (player / embed)")
        say(f"url     : {nxt}")
        say(f"bóc bằng: {label}")
        if rx:
            say(f"regex   : {rx}")
            say(f"C#      : Regex.Match(html, {cs_verbatim(rx)})")
        say("→ chạy lại KHÔNG có --no-fetch để đo header + segment thật của bước này")

    cands = extract_streams(text, base)
    hr("ỨNG VIÊN STREAM TRONG FILE")
    if not cands:
        say("không có m3u8/mp4 trong file này" + (" — bình thường, stream nằm ở BƯỚC KẾ." if nxt else "."))
        if not nxt:
            say("→ trang render bằng JS: mở bằng x.sh rồi truyền file dump vào tool.")
        return 0 if nxt else 1
    for c in cands[:limit]:
        say(f"· {c['url'][:150]}")
        say(f"    nguồn : {c['from']} — {c['label']}")
        if c["regex"]:
            say(f"    regex : {c['regex']}")
            say(f"    C#    : Regex.Match(html, {cs_verbatim(c['regex'])})")
    say("")
    say("Chưa kiểm tra phát được (--no-fetch). Bỏ cờ này để đo header + segment thật.")
    return 0


# ---------------------------------------------------------------- smoke (đo bằng ffmpeg)
#
# `curl` 200 KHÔNG có nghĩa vào app là phát được: nó chỉ mở hop đầu, không đi tiếp
# variant/media playlist, không fetch segment qua /proxy/, không đọc EXT-X-KEY/EXT-X-MAP,
# không giải mã. Khối này chạy ĐÚNG chuỗi của player: ffprobe (đọc playlist + codec)
# rồi ffmpeg decode 5s đầu + 5s cuối — qua chính route Lampac cần kiểm tra.

FFMPEG = shutil.which("ffmpeg")
FFPROBE = shutil.which("ffprobe")

# BẪY: segment đuôi NGỤY TRANG (.png) làm ffmpeg 8 từ chối
# ("URL ...png is not in allowed_segment_extensions") -> false negative 100%.
# Tên option đổi theo bản: 7+ `extension_picky`, 5-6 `allowed_segment_extensions`,
# <=4 `allowed_extensions`.
_FMT = None


def _fmt_flags():
    global _FMT
    if _FMT is not None:
        return _FMT
    _FMT = []
    if not FFMPEG:
        return _FMT
    try:
        h = subprocess.run([FFMPEG, "-hide_banner", "-h", "demuxer=hls"],
                           capture_output=True, text=True, timeout=20).stdout
        if "extension_picky" in h:
            _FMT = ["-extension_picky", "0"]
        elif "allowed_segment_extensions" in h:
            _FMT = ["-allowed_segment_extensions", "ALL"]
        elif "allowed_extensions" in h:
            _FMT = ["-allowed_extensions", "ALL"]
    except Exception:
        pass
    return _FMT


def _ff(prog, args, timeout=30, hls=True):
    """Chạy ffmpeg/ffprobe, tự chèn cờ bỏ kiểm duyệt đuôi segment.

    Cờ đó là option RIÊNG của demuxer hls, chỉ hợp lệ khi input là HLS: gặp input mp4
    (demuxer mov) ffmpeg thoát ngay `Option extension_picky not found` -> false negative.
    Vì vậy chỉ chèn khi `hls=True`.
    """
    cmd = [prog, "-hide_banner"] + (_fmt_flags() if hls else []) + args
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        return p.returncode, p.stdout, p.stderr
    except subprocess.TimeoutExpired:
        return 124, "", f"timeout {timeout}s (app cũng sẽ báo manifestLoadTimeout)"
    except Exception as e:
        return 125, "", f"{type(e).__name__}: {str(e)[:120]}"


def _hdr_args(referer):
    """Header cho ffmpeg/ffprobe: UA + Referer (ffmpeg KHÔNG tự gửi referer)."""
    a = ["-user_agent", UA]
    if referer:
        a += ["-headers", f"Referer: {referer}\r\n"]
    return a


def _timed_get(url, referer=None, rng=None, timeout=30):
    """GET KHÔNG qua _CACHE (đo cold/warm thật). Trả (resp, giây, lỗi)."""
    try:
        from curl_cffi import requests as cr
    except Exception:
        return None, 0.0, "thiếu curl_cffi"
    h = {"referer": referer} if referer else {}
    if rng:
        h["range"] = rng
    t0 = time.perf_counter()
    try:
        r = cr.get(url, impersonate="chrome124", headers=h, timeout=timeout,
                   allow_redirects=True)
    except Exception as e:
        return None, time.perf_counter() - t0, f"{type(e).__name__}: {str(e)[:110]}"
    return r, time.perf_counter() - t0, ""


def lampac_route(url):
    return bool(re.search(r"(?:127\.0\.0\.1|localhost):\d+|/lite/", url or ""))


def _first_err(text):
    for l in (text or "").splitlines():
        ls = l.strip()
        if not ls:
            continue
        if re.search(r"\b(403|404|410|451|500|502|503)\b|Error|Invalid|failed|No such", ls, re.I):
            return ls[:180]
    return (text or "").strip().splitlines()[0][:180] if (text or "").strip() else ""


def smoke(url, referer=None):
    """Kiểm tra phát được (~90%): playlist -> segment -> key -> codec -> decode 5s đầu/cuối."""
    res = {"url": url, "ok": False, "level": "FAIL", "fails": [], "warns": [],
           "cold_s": None, "warm_s": None, "body": "", "ext": None, "segments": {},
           "key": None, "codec": "", "duration": None,
           "decode_first": None, "decode_tail": None, "first_error": ""}

    if not FFMPEG or not FFPROBE:
        res["fails"].append("thiếu ffmpeg/ffprobe — không đo được (pkg install ffmpeg)")
        return res

    is_lampac = lampac_route(url)

    # 1) body + thời gian cold/warm (route Lampac: cold = lúc app bấm lần đầu)
    # mp4 > 1 GB: KHÔNG GET full body (timeout 30 s) — probe Range như player.
    rng = RANGE_PROBE if looks_mp4(url) else None
    r1, t1, err = _timed_get(url, referer, rng=rng)
    res["cold_s"] = round(t1, 2)
    if r1 is None:
        res["fails"].append(f"không tải được playlist: {err}")
        return res
    r2, t2, _ = _timed_get(url, referer, rng=rng)
    res["warm_s"] = round(t2, 2)

    if r1.status_code >= 400:
        res["fails"].append(f"HTTP {r1.status_code} khi lấy playlist")
        res["first_error"] = body_text(r1).strip()[:140]
        return res

    body = body_text(r1)
    is_hls = body.lstrip().upper().startswith("#EXTM3U")
    is_mp4 = "ftyp" in body[:64]
    if not (is_hls or is_mp4):
        res["fails"].append("body KHÔNG phải m3u8/mp4 (HTML hoặc JSON lỗi) -> player báo manifestLoadError")
        res["first_error"] = body.lstrip()[:140]
        return res
    res["body"] = "m3u8" if is_hls else "mp4"

    # 1b) ĐUÔI URL quyết định app có phát được hay không.
    # Lampa/SISI chọn player THEO ĐUÔI URL: không có `.m3u8` -> coi là file video
    # thường -> "no supported source". Route Lampac trả HLS mà thiếu đuôi là lỗi
    # thật (SupJav khai báo song song: [Route("supjav/video")] + [Route("supjav/video.m3u8")]).
    ext = url_ext(url)
    res["ext"] = ext
    if is_hls and ext != ".m3u8":
        msg = ("URL route KHÔNG có đuôi .m3u8 (path = "
               f"{up.urlparse(url).path or '/'}) — body là HLS nhưng app chọn player theo đuôi "
               "URL nên coi là file thường -> 'no supported source'. Khai báo thêm route "
               "`<mod>/video.m3u8` (mẫu SupJav: [Route(\"supjav/video\")] + [Route(\"supjav/video.m3u8\")])")
        (res["fails"] if is_lampac else res["warns"]).append(msg)
    elif is_mp4 and ext != ".mp4":
        res["warns"].append("URL không có đuôi .mp4 (body mp4) — Lampa chọn player theo đuôi; "
                            "box Android thường vẫn phát, web/TV có thể 'no supported source'")

    # cờ demuxer chỉ dành cho HLS (xem _ff): input mp4 mà chèn -> "Option not found"
    hls_in = is_hls

    # 2) segment phải đi qua Lampac (/proxy/), không raw CDN (web/TV chết vì CORS)
    if is_hls:
        segs = [l.strip() for l in body.splitlines() if l.strip() and not l.startswith("#")]
        host_route = up.urlparse(url).netloc
        via = raw = 0
        for s in segs:
            u = up.urljoin(url, s)
            if "/proxy" in u or (is_lampac and up.urlparse(u).netloc == host_route):
                via += 1
            else:
                raw += 1
        res["segments"] = {"total": len(segs), "via_lampac": via, "raw": raw}
        if not segs:
            res["fails"].append("playlist rỗng (0 segment)")
        elif raw and is_lampac:
            res["warns"].append(f"{raw}/{len(segs)} segment KHÔNG qua Lampac (raw CDN) — "
                                f"Android có thể phát, web/TV chết vì CORS")

        # 3) AES key: đúng 16 byte
        m = re.search(r'#EXT-X-KEY:METHOD=([^,\s]+)(?:[^#]*?URI="([^"]+)")?', body)
        if m and m.group(1).upper() != "NONE" and m.group(2):
            kurl = up.urljoin(url, m.group(2))
            kr, _, kerr = _timed_get(kurl, referer)
            n = len(kr.content or b"") if kr is not None else 0
            res["key"] = {"method": m.group(1), "status": kr.status_code if kr else 0, "bytes": n}
            if n == 0:
                res["fails"].append(f"AES key tải hỏng (status {res['key']['status']}) {kerr}")
            elif m.group(1).upper().endswith("AES-128") and n != 16:
                res["warns"].append(f"AES-128 key dài {n}B (không phải 16) — player có thể lỗi giải mã")

    # 4) ffprobe: codec + duration (đọc xuyên master -> variant -> media)
    rc, out, err = _ff(FFPROBE, ["-v", "error"] + _hdr_args(referer) +
                       ["-show_entries", "stream=codec_type,codec_name,width,height",
                        "-show_entries", "format=duration", "-of", "json", url], hls=hls_in)
    if rc != 0:
        res["fails"].append(f"ffprobe không mở được stream (rc={rc})")
        res["first_error"] = res["first_error"] or _first_err(err)
    else:
        try:
            j = json.loads(out)
        except Exception:
            j = {}
        vs = [s for s in j.get("streams", []) if s.get("codec_type") == "video"]
        if not vs:
            res["fails"].append("không có stream video (chỉ audio?)")
        else:
            v = vs[0]
            res["codec"] = f"{v.get('codec_name', '?')} {v.get('width', '?')}x{v.get('height', '?')}"
            if v.get("codec_name") in ("av1", "vp9"):
                res["warns"].append(f"codec {v.get('codec_name')} — nhiều box Android không giải mã được")
            elif v.get("codec_name") == "hevc":
                res["warns"].append("codec hevc — box yếu/TV cũ có thể không giải mã")
        d = (j.get("format") or {}).get("duration")
        try:
            res["duration"] = round(float(d), 1)
        except Exception:
            res["duration"] = None

    # 5) decode 5s ĐẦU (bắt chết ngay) — quyết định PASS/FAIL
    rc1, out1, e1 = _ff(FFMPEG, ["-v", "error"] + _hdr_args(referer) +
                        ["-i", url, "-t", "5", "-c", "copy", "-f", "null", "-"], hls=hls_in)
    res["decode_first"] = rc1
    if rc1 != 0:
        res["fails"].append(f"decode 5s đầu FAIL (rc={rc1})")
        res["first_error"] = res["first_error"] or _first_err(e1)

    # 6) decode 5s CUỐI (token/CDN hết hạn giữa stream) — chỉ WARN
    if res["duration"] and res["duration"] > 12:
        rc2, _, e2 = _ff(FFMPEG, ["-v", "error"] + _hdr_args(referer) +
                         ["-sseof", "-5", "-i", url, "-t", "5", "-c", "copy", "-f", "null", "-"], hls=hls_in)
        res["decode_tail"] = rc2
        if rc2 != 0:
            res["warns"].append(f"decode 5s cuối FAIL (rc={rc2}) — đuôi stream lỗi/hết hạn")
            res["first_error"] = res["first_error"] or _first_err(e2)

    # 7) thời gian: cold > 8s nguy cơ app timeout manifest (10s)
    if res["cold_s"] is not None and res["cold_s"] > 8:
        res["warns"].append(f"cold {res['cold_s']}s — sát/timeout manifest của app (10s)")
    if res["warm_s"] is not None and res["warm_s"] > 2:
        res["warns"].append(f"warm {res['warm_s']}s — cache chưa ăn?")

    res["ok"] = not res["fails"]
    res["level"] = "FAIL" if res["fails"] else ("WARN" if res["warns"] else "PASS")
    return res


def print_smoke(sm):
    hr("SMOKE — PHÁT ĐƯỢC TRÊN LAMPAC? (~90%)")
    say(f"route    : {sm['url']}")
    if sm["cold_s"] is not None:
        say(f"cold/warm: {sm['cold_s']}s / {sm['warm_s']}s")
    if sm["body"]:
        seg = sm.get("segments") or {}
        extra = (f" · segment {seg.get('via_lampac', 0)}/{seg.get('total', 0)} qua Lampac"
                 + (f", {seg.get('raw')} raw" if seg.get("raw") else "")) if seg.get("total") else ""
        say(f"body     : {sm['body']}{extra}")
    if sm.get("ext") is not None:
        want = ".m3u8" if sm["body"] == "m3u8" else ".mp4"
        ok_ext = sm["ext"] == want
        say(f"đuôi URL : {(sm['ext'] or '(không có)')} {'✔' if ok_ext else '✗ thiếu ' + want}")
    say(f"key      : " + ("không có" if not sm["key"]
                          else f"{sm['key']['method']} · {sm['key']['status']} {sm['key']['bytes']}B"))
    if sm["codec"]:
        say(f"codec    : {sm['codec']}"
            + (f" · {sm['duration']}s" if sm["duration"] else ""))
    if sm["decode_first"] is not None:
        dt = "—" if sm["decode_tail"] is None else ("✔" if sm["decode_tail"] == 0 else "✗")
        say(f"decode   : 5s đầu {'✔' if sm['decode_first'] == 0 else '✗'} · 5s cuối {dt}")
    for w in sm["warns"]:
        say(f"  ~ {w}")
    for f in sm["fails"]:
        say(f"  ✗ {f}")
    if sm["first_error"]:
        say(f"  lỗi đầu: {sm['first_error']}")
    tag = {"PASS": "PASS (~90%)", "WARN": "PHÁT ĐƯỢC (có cảnh báo)",
           "FAIL": "FAIL — app gần như chắc không phát"}[sm["level"]]
    say(f"→ {tag}")


def smoke_meta(sm):
    return {"level": sm["level"], "ok": sm["ok"], "codec": sm["codec"], "ext": sm.get("ext"),
            "duration": sm["duration"], "cold_s": sm["cold_s"], "warm_s": sm["warm_s"],
            "segments": sm["segments"], "key": sm["key"],
            "decode_first": sm["decode_first"], "decode_tail": sm["decode_tail"],
            "fails": sm["fails"], "warns": sm["warns"], "first_error": sm["first_error"]}


# ---------------------------------------------------------------- main

def main(argv):
    ap = argparse.ArgumentParser(
        add_help=True,
        description="Nhập 1 link -> in chuỗi giải + hướng giải để agent tự viết module",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""\
Output gồm 6 khối:
  TRANG NÀY LÀ GÌ  list hay detail; nếu là list thì kèm mẫu phân trang + sort
  CHUỖI GIẢI       từng bước HTTP: status, header BẮT BUỘC (đo thật), regex bóc bước kế
  STREAM           HLS master/media, variant, AES, EXT-X-KEY, segment đuôi giả ảnh
  SMOKE            ffmpeg decode thật (5s đầu/cuối) + codec + segment qua proxy + AES key
  HƯỚNG GIẢI       các bước + regex + sườn C# + BẪY đã đo
  KẾT LUẬN         PHÁT ĐƯỢC / CHẾT + tóm tắt 1 dòng

Ví dụ:
  play.sh 'https://site/clip/abc/123/'       # trang detail -> đi hết tới m3u8
  play.sh 'https://player/embed?id=abc'      # thẳng trang player
  play.sh 'https://cdn/x.m3u8'               # thẳng stream
  play.sh 'http://127.0.0.1:9118/lite/mod/video.m3u8?uri=...'   # qua Lampac (tự SMOKE)
  play.sh 'https://cdn/x.m3u8' --smoke       # ép SMOKE cả với link ngoài
  play.sh detail.html --base https://site/   # file đã lưu
  play.sh - --json < detail.html             # JSON cho agent
  play.sh detail.html --no-fetch             # chỉ đọc file, không gọi mạng

Lưu ý: 'PHÁT ĐƯỢC' = ffprobe đọc được codec + ffmpeg decode 5s đầu OK (~90% khớp app).
Nó KHÔNG thay cho việc bấm thử trên app: còn 10% là TLS/CORS của app thật, codec box,
token ngắn hạn, bitrate. Segment đuôi ngụy trang (.png) cần cờ bỏ kiểm duyệt đuôi —
tool tự chèn (extension_picky/allowed_segment_extensions), đừng bỏ.
""")
    ap.add_argument("input", help="URL, file HTML/JSON đã lưu, hoặc '-' để đọc stdin")
    ap.add_argument("--referer", default=None, help="Referer khởi đầu (mặc định: tự thử)")
    ap.add_argument("--base", default=None, help="base URL khi input là file (để ghép URL tương đối)")
    ap.add_argument("--limit", type=int, default=4, help="số ứng viên stream tối đa (mặc định 4)")
    ap.add_argument("--json", action="store_true", help="xuất JSON cho agent đọc")
    ap.add_argument("--no-fetch", action="store_true", help="chỉ phân tích file, KHÔNG gọi mạng")
    ap.add_argument("--save", default=None, help="lưu extracted.json vào thư mục này")
    ap.add_argument("--smoke", action="store_true",
                    help="bắt buộc chạy SMOKE (ffprobe/ffmpeg) — mặc định tự chạy với URL Lampac")
    ap.add_argument("--no-smoke", action="store_true", help="tắt khối SMOKE (nhanh hơn)")
    args = ap.parse_args(argv)

    src = args.input
    is_url = bool(re.match(r"^https?://", src))

    # ---- chế độ offline: file/stdin
    if args.no_fetch or not is_url:
        if src == "-":
            text = sys.stdin.read()
            base = args.base or ""
        else:
            p = pathlib.Path(src).expanduser()
            if not p.exists():
                say(f"không thấy file/URL: {src}")
                return 2
            text = p.read_text(errors="ignore")
            base = args.base or ""
            if not base:
                m = re.search(r'<meta[^>]+property=["\']og:url["\'][^>]+content=["\']([^"\']+)', text, re.I) \
                    or re.search(r'<link[^>]+rel=["\']canonical["\'][^>]+href=["\']([^"\']+)', text, re.I)
                base = m.group(1) if m else ""
        if args.save:
            d = pathlib.Path(args.save).expanduser()
            d.mkdir(parents=True, exist_ok=True)
            (d / "extracted.json").write_text(
                json.dumps(extract_streams(text, base), ensure_ascii=False, indent=2))
        return analyze_local(text, base, args.limit)

    # ---- chế độ online: đi hết chuỗi
    start_refs = [None]
    if args.referer:
        start_refs = [args.referer]
    else:
        start_refs = [None, origin(src)]

    chain, stream_resp = crawl(src, start_refs)

    # player đầu không ra stream thì thử các player khác trên cùng trang (site nhiều server)
    dead_note = None
    if stream_resp is None and chain and chain[0].get("other_players"):
        for alt in chain[0]["other_players"][:max(0, args.limit - 1)]:
            c2, r2 = crawl(alt, [None, origin(alt)])
            if r2 is not None:
                dead_note = chain[0]["url"]
                chain, stream_resp = c2, r2
                break

    hls = None
    seg = None
    if stream_resp is not None:
        kind = chain[-1]["kind"]
        if kind == "hls":
            hls = inspect_hls(str(stream_resp.url), stream_resp)
            if hls.get("first_seg"):
                seg = probe_segment(hls["first_seg"], chain[-1]["referer"] or str(stream_resp.url))
        elif kind == "mp4":
            seg = probe_segment(str(stream_resp.url), chain[-1]["referer"])

    playable = bool(stream_resp is not None and (
        (hls and seg and seg["ok"]) or (not hls and seg and seg["ok"])))

    # ---- SMOKE: đo bằng ffprobe/ffmpeg qua CHÍNH route cần kiểm tra.
    # Tự chạy khi đầu vào là route Lampac (curl 200 chưa chắc app phát được).
    sm = None
    if stream_resp is not None and not args.no_smoke and (args.smoke or lampac_route(src)):
        # Lampac route: smoke CHÍNH route (đo cả bước resolve + 302), không phải URL proxy sau redirect
        target = src if lampac_route(src) else str(stream_resp.url)
        sm = smoke(target, chain[-1]["referer"] if chain else args.referer)

    if args.save:
        d = pathlib.Path(args.save).expanduser()
        d.mkdir(parents=True, exist_ok=True)
        (d / "trace.json").write_text(json.dumps(
            {"chain": chain, "hls": hls, "seg": seg, "playable": playable,
             "smoke": smoke_meta(sm) if sm else None},
            ensure_ascii=False, indent=2))

    if args.json:
        say(json.dumps({
            "input": src,
            "reference": args.referer,
            "playable": playable,
            "chain": chain,
            "hls": hls,
            "segment": seg,
            "smoke": smoke_meta(sm) if sm else None,
            "advice": build_advice(chain, hls, seg),
            "traps": build_traps(chain, hls, seg),
            "skeleton": build_skeleton(chain, hls),
            "summary": one_liner(chain, hls, seg),
        }, ensure_ascii=False, indent=2))
        return 0 if (playable and (sm is None or sm["ok"])) else 1

    hr("TRANG NÀY LÀ GÌ")
    if dead_note:
        say(f"! player đầu ({dead_note}) không ra stream → đã thử player khác trên trang.")
    if chain:
        first = chain[0]
        if first["kind"] == "page":
            cached = _CACHE.get((first["url"], first["referer"], None))
            if cached is not None:
                sh = page_shape(body_text(cached))
                if sh["detail"]:
                    say("loại    : detail (có player/embed)")
                else:
                    say(f"loại    : danh sách (~{sh['cards']} card)")
                say(f"sort    : {', '.join(sh['sorts']) or '—'}")
                say(f"pager   : {sh['pager'] or 'không thấy'}")
        elif first["kind"] in ("hls", "mp4"):
            say("link này là stream trực tiếp")
    say(f"đầu vào: {src}")

    print_chain(chain)
    if stream_resp is not None:
        print_stream(hls, seg, str(stream_resp.url))
    if sm:
        print_smoke(sm)
    print_advice(chain, hls, seg)

    hr("KẾT LUẬN")
    if playable and (sm is None or sm["ok"]):
        say(f"PHÁT ĐƯỢC{' (ffmpeg decode OK qua route Lampac)' if sm else ''} — {stream_resp.url}")
    elif stream_resp is not None:
        if sm is not None and not sm["ok"]:
            say(f"KHÔNG PHÁT ĐƯỢC — {stream_resp.url}")
            say("lý do: " + "; ".join(sm["fails"]))
        else:
            say(f"BÓC ĐƯỢC NHƯNG CHƯA CHẮC PHÁT — {stream_resp.url}")
            say("xem BẪY ở trên (Referer / giả ảnh / AES), và test lại trên app.")
    else:
        say("KHÔNG RA STREAM.")
        say("→ trang render bằng JS/Cloudflare: mở bằng x.sh <url> rồi truyền file dump: "
            "play.sh dump.html --base <url>")
        return 1
    say("")
    say("TÓM TẮT CHO AGENT: " + one_liner(chain, hls, seg))
    return 0 if (playable and (sm is None or sm["ok"])) else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
