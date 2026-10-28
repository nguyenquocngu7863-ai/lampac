#!/usr/bin/env python3
"""capture_oneshot.py v2 — lần curl ĐẦU TIÊN lấy hết dữ liệu cần cho 1 module Lampac.

v2 thêm so với v1:
  * sitemap: tự dò /sitemap.xml, sitemap_index.xml, wp-sitemap.xml... → lấy taxonomy ẨN
    (tag/studio không có link ở menu), bỏ qua sitemap post/video.
  * tag ẩn: quét <select name=...>, data-taxonomy, data-filter, option value trong
    filter form → ghi vào taxonomy.json["hidden"].
  * JS theme của site (không chỉ JS trong embed) → menu nhiều tầng dựng client-side.
  * endpoint AJAX/API: admin-ajax.php, /api/..., action=..., filter endpoint.
  * taxonomy.json: file duy nhất để sinh sườn module (menu 2 tầng, genre/studio động).
  * --netspy <netspy.json>: merge URL mà Chrome đã bắt được (XHR ẩn không có trong HTML).
  * ngân sách request (--max-requests) để không bao giờ spam site.

Dùng:
  python3 capture_oneshot.py --host https://site.tld --detail https://site.tld/abc.html --out .
Tuỳ chọn:
  --search "co giao"            từ khoá search thử
  --sort-param sort_by --sort-value video_viewed_week
  --cookie "k=v; k2=v2"
  --netspy /path/netspy.json
  --max-cat 4 --max-embed 4 --max-js 4 --max-sitemaps 6 --max-requests 45 --sleep 1.5

Luật: sleep giữa request, retry tối đa 2, dừng ngay khi bị 403/429/challenge.
"""
import argparse
import json
import os
import re
import sys
import time
from urllib.parse import urljoin, urlparse, quote, parse_qs

try:
    from curl_cffi import requests as rq
except ImportError:
    sys.exit("cần: pip install curl_cffi")

UA = ("Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) "
      "Chrome/120.0.0.0 Mobile Safari/537.36")

# /the-loai/, /tag/, /studio/, /dien-vien/ ...
TAXO_RE = re.compile(
    r"/(the-loai|theloai|the-loai-phim|category|categories|genre|genres|tag|tags|"
    r"tu-khoa|keyword|keywords|studio|studios|series|label|labels|"
    r"dao-dien|dien-vien|actor|actors|cast|nhom|nhom-phim|quoc-gia|country|"
    r"nam|year|sort|sap-xep)/", re.I)

TAG_GROUPS = {"tag", "tags", "tu-khoa", "keyword", "keywords"}
STUDIO_GROUPS = {"studio", "studios", "series", "label", "labels",
                 "dao-dien", "dien-vien", "actor", "actors", "cast", "nhom", "nhom-phim"}
OTHER_GROUPS = {"quoc-gia", "country", "nam", "year", "sort", "sap-xep"}

SITEMAP_CANDIDATES = ("/sitemap.xml", "/sitemap_index.xml", "/wp-sitemap.xml")

# sitemap con đáng tải: có taxonomy; bỏ post/video (nặng, vô dụng cho sườn)
SITEMAP_WANT = re.compile(r"(categor|tag|genre|studio|actor|series|label|taxonom|"
                          r"the-loai|dien-vien|dao-dien)", re.I)
SITEMAP_SKIP = re.compile(r"(post|video|movie|film|attachment|image|news|article)", re.I)


def slug(url):
    p = urlparse(url)
    s = (p.netloc + p.path + ("_" + p.query if p.query else "")).strip("/")
    s = re.sub(r"[^A-Za-z0-9]+", "_", s)[:90].strip("_")
    return s or "index"


def taxo_kind(url):
    m = TAXO_RE.search(url)
    if not m:
        return None
    g = m.group(1).lower()
    if g in TAG_GROUPS:
        return "tag"
    if g in STUDIO_GROUPS:
        return "studio"
    if g in OTHER_GROUPS:
        return "other"
    return "category"


def strip_tags(s):
    s = re.sub(r"<[^>]+>", " ", s)
    return re.sub(r"\s+", " ", s).strip()[:80]


class Cap:
    def __init__(self, host, out, sleep, max_embed, cookie=None,
                 impersonate="chrome124", max_requests=45):
        self.host = host.rstrip("/")
        self.netloc = urlparse(self.host).netloc
        self.out = out
        self.sleep = sleep
        self.max_embed = max_embed
        self.idx = []
        self.taxo = {"host": self.host, "categories": [], "tags": [], "studios": [],
                     "other": [], "sort": [], "filters": [], "menu": [],
                     "hidden": [], "sitemaps": [], "js": [], "api": [], "notes": []}
        self._seen_taxo = set()
        self._seen_menu = set()
        self._seen_hidden = set()
        self._seen_api = set()
        self.s = rq.Session(impersonate=impersonate)
        if cookie:
            self.s.headers.update({"Cookie": cookie})
        self.dead = False
        self.budget = max_requests
        self.used = 0

    # ---------- hạ tầng ----------
    def _write(self, name, body, url, status, kind):
        path = os.path.join(self.out, name)
        with open(path, "wb") as f:
            f.write(body)
        self.idx.append({"kind": kind, "url": url, "file": name,
                         "status": status, "size": len(body)})
        print(f"  {kind:12s} {status} {len(body):>8d}  {name}")

    def _check(self, r, url):
        head = r.content[:4000].lower()
        if r.status_code in (403, 429) or b"just a moment" in head or b"cf-chl" in head:
            print(f"  !! {r.status_code} challenge/throttle tại {url} -> DỪNG capture")
            self.dead = True
            return False
        return True

    def _spend(self):
        if self.budget <= 0:
            print("  !! hết ngân sách request -> dừng")
            self.dead = True
            return False
        self.budget -= 1
        self.used += 1
        return True

    def get(self, url, kind, referer=None, name=None):
        if self.dead or not self._spend():
            return None
        for attempt in (1, 2):
            try:
                h = {"User-Agent": UA}
                if referer:
                    h["Referer"] = referer
                r = self.s.get(url, headers=h, timeout=30, allow_redirects=True)
                if not self._check(r, url):
                    return None
                self._write(name or slug(url) + ".html", r.content, str(r.url),
                            r.status_code, kind)
                time.sleep(self.sleep)
                return r.text
            except Exception as e:
                print(f"  retry {attempt} {url[:80]}: {str(e)[:70]}")
                time.sleep(self.sleep * attempt)
        return None

    def probe(self, url, referer=None):
        """GET không ghi file — dùng để dò (sitemap, thử shape) khỏi rác thư mục."""
        if self.dead or not self._spend():
            return None, ""
        try:
            h = {"User-Agent": UA}
            if referer:
                h["Referer"] = referer
            r = self.s.get(url, headers=h, timeout=30, allow_redirects=True)
            if not self._check(r, url):
                return None, ""
            return r, r.text
        except Exception as e:
            print(f"  probe fail {url[:70]}: {str(e)[:60]}")
            return None, ""

    def post(self, url, data, kind, referer, name):
        if self.dead or not self._spend():
            return None
        for attempt in (1, 2):
            try:
                r = self.s.post(url, data=data, timeout=30, headers={
                    "User-Agent": UA, "Referer": referer, "Origin": self.host,
                    "X-Requested-With": "XMLHttpRequest"})
                if not self._check(r, url):
                    return None
                self._write(name, r.content, url, r.status_code, kind)
                time.sleep(self.sleep)
                return r.text
            except Exception as e:
                print(f"  retry {attempt} POST {url[:70]}: {str(e)[:70]}")
                time.sleep(self.sleep * attempt)
        return None

    # ---------- thu thập ----------
    def add_taxo(self, kind, url, name=None, source="html"):
        if kind not in ("category", "tag", "studio", "other"):
            return
        key = (kind, url.split("#")[0])
        if key in self._seen_taxo:
            return
        self._seen_taxo.add(key)
        bucket = {"category": "categories", "tag": "tags",
                  "studio": "studios", "other": "other"}[kind]
        self.taxo[bucket].append({"name": name or "", "url": url, "source": source})

    def add_hidden(self, name, value, source):
        key = (name, value)
        if key in self._seen_hidden or not value:
            return
        self._seen_hidden.add(key)
        self.taxo["hidden"].append({"name": name, "value": value[:120], "source": source})

    def add_api(self, url, source):
        if url in self._seen_api:
            return
        self._seen_api.add(url)
        self.taxo["api"].append({"url": url[:300], "source": source})

    def add_menu(self, group, name, url):
        key = (group, url)
        if key in self._seen_menu:
            return
        self._seen_menu.add(key)
        self.taxo["menu"].append({"group": group, "name": name, "url": url})


def pager(html):
    for m in re.finditer(r'href=["\']([^"\'#]+)["\']', html, re.I):
        u = m.group(1)
        if re.search(r'(?:page|paged|/p/)\s*[=/]?\s*2\b', u):
            return u
    return None


def links(html, base):
    out = []
    for m in re.finditer(r'<a\b[^>]*href=["\']([^"\'#]+)["\'][^>]*>(.*?)</a>',
                         html, re.I | re.S):
        u = urljoin(base, m.group(1))
        out.append((strip_tags(m.group(2)), u))
    return out


def block(html, tag):
    """Nội dung của <tag ...>...</tag> đầu tiên (dùng cho nav/header/footer)."""
    m = re.search(r"<%s\b[^>]*>(.*?)</%s>" % (tag, tag), html, re.I | re.S)
    return m.group(1) if m else ""


def scan_links(c, html, base, source):
    for name, u in links(html, base):
        if urlparse(u).netloc != c.netloc:
            continue
        kind = taxo_kind(u)
        if kind:
            c.add_taxo(kind, u, name, source)
        if re.search(r'(?:[?&](?:s|q|keyword|search|tu-khoa)=)', u):
            c.add_api(u, source + ":search")


def scan_selects(c, html, base, source):
    for m in re.finditer(r"<select\b([^>]*)>(.*?)</select>", html, re.I | re.S):
        attrs, body = m.group(1), m.group(2)
        nm = re.search(r'name=["\']([^"\']+)', attrs, re.I)
        nm = nm.group(1) if nm else "?"
        opts = re.findall(r"<option\b([^>]*)>(.*?)</option>", body, re.I | re.S)
        for oattrs, otext in opts:
            val = re.search(r'value=["\']([^"\']*)', oattrs, re.I)
            val = val.group(1).strip() if val else ""
            if not val or val in ("0", "-1", ""):
                continue
            c.add_hidden(nm, val, source)
            if taxo_kind(val) or "/" in val:
                c.add_taxo(taxo_kind(val) or "other", urljoin(base, val),
                           strip_tags(otext), source + ":select")


def scan_filters(c, html, base, source):
    """<form> filter + endpoint AJAX/API lộ trong HTML."""
    for m in re.finditer(r"<form\b([^>]*)>", html, re.I):
        attrs = m.group(1)
        if not re.search(r"filter|search|loc|sap-xep|sort", attrs, re.I):
            continue
        act = re.search(r'action=["\']([^"\']*)', attrs, re.I)
        meth = re.search(r'method=["\']([^"\']+)', attrs, re.I)
        c.taxo["filters"].append({
            "action": urljoin(base, act.group(1)) if act else base,
            "method": (meth.group(1) if meth else "get").lower(), "source": source})
        if act:
            c.add_api(urljoin(base, act.group(1)), source + ":form")

    for u in re.findall(r'["\'](/[^"\']*(?:admin-ajax\.php|/api/[A-Za-z0-9_\-/]{2,60}))["\']',
                        html):
        c.add_api(urljoin(base, u), source)
    for act in set(re.findall(r'(?:action|ajax_action)["\']?\s*[:=]\s*["\']([A-Za-z0-9_\-]{3,40})',
                              html)):
        c.add_hidden("ajax_action", act, source)


def scan_scripts(c, html, base, limit):
    got = 0
    for m in re.finditer(r'<script\b[^>]*src=["\']([^"\']+\.js[^"\']*)["\']', html, re.I):
        if got >= limit:
            break
        u = urljoin(base, m.group(1))
        if urlparse(u).netloc != c.netloc:
            continue
        if re.search(r'(jquery|bootstrap|fontawesome|analytics|gtag|adsbygoogle)', u, re.I):
            continue
        if c.get(u, "site-js", referer=base):
            c.taxo["js"].append(u)
            got += 1
    return got


def capture_sitemaps(c, max_sitemaps):
    """Sitemap → taxonomy ẩn. Trả về True nếu tìm được sitemap."""
    root = None
    for cand in SITEMAP_CANDIDATES:
        url = c.host + cand
        r, txt = c.probe(url)
        if not r or r.status_code != 200 or "<loc" not in txt:
            continue
        c._write("sitemap_root.xml", r.content, str(r.url), r.status_code, "sitemap")
        locs = re.findall(r"<loc>\s*([^<\s]+)\s*</loc>", txt)
        c.taxo["sitemaps"].append({"url": url, "locs": len(locs)})
        if locs:
            root = locs
            break
    if not root:
        c.taxo["notes"].append("không thấy sitemap (đã thử %d đường dẫn)" % len(SITEMAP_CANDIDATES))
        return False

    # sitemap index -> chọn child theo taxonomy
    childs = [u for u in root if u.endswith(".xml") or ".xml?" in u]
    if childs:
        want = [u for u in childs if SITEMAP_WANT.search(u) and not SITEMAP_SKIP.search(u)]
        pick = want[:max_sitemaps] or [u for u in childs if not SITEMAP_SKIP.search(u)][:max_sitemaps]
        locs = []
        for u in pick:
            r, txt = c.probe(u)
            if r and r.status_code == 200 and "<loc" in txt:
                c._write(slug(u) + ".xml", r.content, u, r.status_code, "sitemap-taxonomy")
                locs += re.findall(r"<loc>\s*([^<\s]+)\s*</loc>", txt)
    else:
        locs = root
    for u in locs[:4000]:
        kind = taxo_kind(u)
        if kind:
            c.add_taxo(kind, u, u.rstrip("/").rsplit("/", 1)[-1], "sitemap")
    c.taxo["notes"].append("sitemap: %d loc, taxonomy nhặt được %d"
                           % (len(locs), len(c._seen_taxo)))
    return True


def merge_netspy(c, path):
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    reqs = d.get("requests") or d.get("hits") or []
    self_netloc = c.netloc
    n = 0
    for r in reqs:
        u = (r.get("url") or "").split("#")[0]
        if not u.startswith("http"):
            continue
        t = (r.get("t") or r.get("kind") or "").lower()
        if urlparse(u).netloc == self_netloc and taxo_kind(u):
            c.add_taxo(taxo_kind(u), u, u.rstrip("/").rsplit("/", 1)[-1], "netspy")
            n += 1
        if t in ("xhr", "fetch") or re.search(r"(admin-ajax|/api/|\.json)(\?|$)", u):
            c.add_api(u, "netspy")
            n += 1
        if re.search(r"\.(m3u8|mpd)(\?|$)", u):
            c.taxo.setdefault("streams", []).append(u[:300])
    c.taxo["notes"].append("netspy: %d URL hữu ích" % n)
    return n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", required=True)
    ap.add_argument("--detail")
    ap.add_argument("--search")
    ap.add_argument("--sort-param")
    ap.add_argument("--sort-value")
    ap.add_argument("--cookie")
    ap.add_argument("--impersonate", default="chrome124")
    ap.add_argument("--netspy")
    ap.add_argument("--max-cat", type=int, default=4)
    ap.add_argument("--max-embed", type=int, default=4)
    ap.add_argument("--max-js", type=int, default=4)
    ap.add_argument("--max-sitemaps", type=int, default=6)
    ap.add_argument("--max-requests", type=int, default=45)
    ap.add_argument("--no-sitemap", action="store_true")
    ap.add_argument("--sleep", type=float, default=1.5)
    ap.add_argument("--out", default=".")
    a = ap.parse_args()

    os.makedirs(a.out, exist_ok=True)
    c = Cap(a.host, a.out, a.sleep, a.max_embed, a.cookie,
            a.impersonate, a.max_requests)
    print(f"capture v2 {a.host} -> {os.path.abspath(a.out)} (ngân sách {a.max_requests} request)")

    home = c.get(a.host + "/", "home")
    if not home:
        print("FAIL home")
        return 1
    base = c.host + "/"

    # menu: nav/header + footer + toàn bộ home
    for grp, blk in (("nav", block(home, "nav") or block(home, "header")),
                     ("footer", block(home, "footer"))):
        for name, u in links(blk, base) if blk else []:
            if urlparse(u).netloc == c.netloc:
                c.add_menu(grp, name, u)
    scan_links(c, home, base, "home")
    scan_selects(c, home, base, "home")
    scan_filters(c, home, base, "home")

    if not a.no_sitemap:
        capture_sitemaps(c, a.max_sitemaps)

    js_n = scan_scripts(c, home, base, a.max_js)
    for u in c.taxo["js"]:
        txt = None
        f = os.path.join(a.out, slug(u) + ".html")
        if os.path.exists(f):
            txt = open(f, encoding="utf-8", errors="replace").read()
        if txt:
            scan_filters(c, txt, u, "site-js")
            for act in set(re.findall(r'action["\']?\s*[:=]\s*["\']([A-Za-z0-9_\-]{3,40})', txt)):
                c.add_hidden("ajax_action", act, "site-js")
    print(f"  site-js: {js_n}")

    if a.netspy:
        try:
            print(f"  netspy: merge {merge_netspy(c, a.netspy)} URL")
        except Exception as e:
            c.taxo["notes"].append("netspy lỗi: %s" % str(e)[:80])

    p2 = pager(home)
    if p2:
        c.get(urljoin(base, p2), "home-page2", referer=base)

    if a.sort_param and a.sort_value:
        c.get(f"{c.host}/?{a.sort_param}={a.sort_value}", "home-sort", referer=base)
        c.taxo["sort"].append({"param": a.sort_param, "value": a.sort_value,
                               "url": f"{c.host}/?{a.sort_param}={a.sort_value}"})

    picked = (c.taxo["categories"][:a.max_cat] + c.taxo["tags"][:1]
              + c.taxo["studios"][:1])
    for item in picked:
        u = item["url"]
        html = c.get(u, "taxonomy", referer=base)
        if html:
            scan_selects(c, html, u, "taxonomy")
            scan_filters(c, html, u, "taxonomy")
            p = pager(html)
            if p:
                c.get(urljoin(u, p), "taxonomy-page2", referer=u)

    if a.search:
        q = quote(a.search)
        c.get(f"{c.host}/search/{q.replace('%20', '-')}/", "search", referer=base)
        c.get(f"{c.host}/?s={q}", "search-alt", referer=base)

    detail_html = c.get(a.detail, "detail", referer=base) if a.detail else None

    embeds, embed_hosts = [], set()
    if detail_html:
        scan_selects(c, detail_html, a.detail, "detail")
        scan_filters(c, detail_html, a.detail, "detail")
        m = re.search(r"(?:player[_-]?server|playerServer|post[_-]?id|postId|ajax_post_id|data-id)"
                      r"['\"]?\s*[:=]\s*['\"]?(\d+)", detail_html)
        if "wp-admin/admin-ajax.php" in detail_html and m:
            pid = m.group(1)
            for sv in (1, 2, 3):
                js = c.post(urljoin(base, "wp-admin/admin-ajax.php"),
                            {"player_server": pid, "server": str(sv),
                             "vast_seen": "1", "is_mobile": "1"},
                            "player", a.detail, f"player_sv{sv}.json")
                if js:
                    for u in re.findall(r'https?:(?:\\?/){2}[^"\'\\\s<>]+?/embed/'
                                        r'[A-Za-z0-9]+(?:\?[^"\'\\\s<>]*)?', js):
                        u = u.replace("\\/", "/").replace("\\u0026", "&")
                        if u not in embeds:
                            embeds.append(u)
        for u in re.findall(r'<iframe[^>]+src=["\']([^"\']+)["\']', detail_html, re.I):
            u = urljoin(a.detail, u)
            if u not in embeds:
                embeds.append(u)

    for u in embeds:
        h = urlparse(u).netloc
        if h in embed_hosts or len(embed_hosts) >= a.max_embed:
            continue
        embed_hosts.add(h)
        html = c.get(u, "embed", referer=a.detail or base)
        if html:
            scan_filters(c, html, u, "embed")
            for js in re.findall(r'<script[^>]+src=["\']([^"\']+\.js[^"\']*)["\']',
                                 html, re.I)[:3]:
                c.get(urljoin(u, js), "player-js", referer=u)

    # ---------- ghi kết quả ----------
    meta = {"host": a.host, "detail": a.detail, "files": c.idx,
            "requests_used": c.used, "stopped_early": c.dead,
            "ts": int(time.time())}
    with open(os.path.join(a.out, "index.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)
    with open(os.path.join(a.out, "taxonomy.json"), "w", encoding="utf-8") as f:
        json.dump(c.taxo, f, ensure_ascii=False, indent=1)

    print(f"\n{len(c.idx)} file / {c.used} request. stopped_early={c.dead}")
    print("  category=%d tag=%d studio=%d other=%d hidden=%d menu=%d api=%d filter=%d js=%d"
          % (len(c.taxo["categories"]), len(c.taxo["tags"]), len(c.taxo["studios"]),
             len(c.taxo["other"]), len(c.taxo["hidden"]), len(c.taxo["menu"]),
             len(c.taxo["api"]), len(c.taxo["filters"]), len(c.taxo["js"])))
    print("Tiếp: đọc taxonomy.json + *.html OFFLINE (không curl lại).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
