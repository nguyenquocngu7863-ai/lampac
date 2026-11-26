#!/data/data/com.termux/files/usr/bin/python3
"""popup.py — verify popup long-press của module Lampac (route /<mod>/genres?uri= + ?rel=).

Khác với play.sh (đo stream phát được thật), script này đo HỢP ĐỒNG POPUP:
  1. home trả list + lấy được uri từ item đầu
  2. /<mod>/genres?uri=<uri> → 200 JSON, có groups, mỗi chip có title+url
  3. menu không lẫn chữ Trung/Nhật/Hàn/Nga/Thái (LUẬT CỨNG AGENTS.md)
  4. /<mod>?rel=<uri> → có list, total_pages, item related nên có plain:true
  5. rel pg=2 → rỗng, total_pages 1
  6. SSRF: /genres?uri=https://example.com/x → 200 rỗng, không 500

Chỉ gọi 127.0.0.1 (app Lampac đang chạy). Không gọi site thật trực tiếp.
Code: PASS = 0 lỗi; WARN = đủ popup nhưng thiếu chuẩn; FAIL = thiếu/hỏng popup.
"""
import argparse
import concurrent.futures as cf
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("LAMPAC", "http://127.0.0.1:9118").rstrip("/")
RUN_MOD = os.path.expanduser("~/lampac-run/module/Adult")
TIMEOUT = float(os.environ.get("POPUP_TIMEOUT", "60"))

LANG_BAD = [
    ("CJK", re.compile(r"[\u3400-\u9FFF\u3040-\u30FF\uF900-\uFAFF]")),
    ("Hàn", re.compile(r"[\uAC00-\uD7AF\u1100-\u11FF]")),
    ("Nga", re.compile(r"[\u0400-\u04FF]")),
    ("Thái", re.compile(r"[\u0E00-\u0E7F]")),
]


def get(path, timeout=None):
    url = BASE + path
    req = urllib.request.Request(url, headers={"User-Agent": "popup-check/1.0"})
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=timeout or TIMEOUT) as r:
            body = r.read()
            return r.status, body, time.time() - t0, None
    except urllib.error.HTTPError as e:
        return e.code, e.read()[:400], time.time() - t0, None
    except Exception as e:  # noqa: BLE001 - timeout/reset đều là dữ liệu đo
        return None, b"", time.time() - t0, f"{type(e).__name__}: {e}"


def get_json(path):
    st, body, dt, err = get(path)
    if err:
        return None, st, dt, f"request lỗi: {err}"
    if st != 200:
        return None, st, dt, f"HTTP {st}: {body[:120].decode('utf-8', 'replace')}"
    try:
        return json.loads(body), st, dt, None
    except Exception:  # noqa: BLE001
        return None, st, dt, f"không phải JSON: {body[:120].decode('utf-8', 'replace')}"


def langs(text):
    return [name for name, rx in LANG_BAD if text and rx.search(text)]


def route_alias(mod):
    """Alias route = giá trị [Route("...")] đầu tiên trong Controller của module."""
    d = os.path.join(RUN_MOD, mod)
    if not os.path.isdir(d):
        return None
    for fn in sorted(os.listdir(d)):
        if not fn.startswith("Controller") or not fn.endswith(".cs"):
            continue
        try:
            src = open(os.path.join(d, fn), encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        for m in re.finditer(r'Route\("([^"]+)"\)', src):
            alias = m.group(1).strip("/")
            if alias and "/" not in alias and "?" not in alias:
                return alias
    return None


def resolve(name, index):
    """Nhận alias route, tên module, hoặc tên không phân biệt hoa/thường."""
    if name in index:
        return name, index[name]
    low = name.lower()
    for alias, mod in index.items():
        if alias.lower() == low or mod.lower() == low:
            return alias, mod
    alias = route_alias(name)
    return (alias, name) if alias else (name, name)


def all_modules():
    out = []
    if not os.path.isdir(RUN_MOD):
        return out
    for mod in sorted(os.listdir(RUN_MOD)):
        mf = os.path.join(RUN_MOD, mod, "manifest.json")
        try:
            if json.load(open(mf, encoding="utf-8")).get("enable", True) is False:
                continue
        except Exception:  # noqa: BLE001
            pass
        alias = route_alias(mod)
        if alias:
            out.append((mod, alias))
    return out


def check(mod, alias, uri_arg=None):
    fails, warns, notes = [], [], []
    t0 = time.time()
    mod = mod or alias

    # 1. home → uri của item đầu
    home, st, dt, err = get_json(f"/{alias}")
    if err:
        return mod, "FAIL", [], [f"home không dùng được ({err})"], round(time.time() - t0, 1)
    items = home.get("list") or []
    notes.append(f"home {len(items)} item · tp={home.get('total_pages')}")
    uri = uri_arg
    if not uri:
        if not items:
            return mod, "FAIL", [], notes + ["home 0 item → không có uri để test popup"], round(time.time() - t0, 1)
        video = items[0].get("video") or ""
        q = urllib.parse.parse_qs(urllib.parse.urlparse(video).query)
        uri = (q.get("uri") or [None])[0]
        if not uri:
            return mod, "FAIL", [], notes + [
                f"item đầu không có ?uri= (video={video[:70]}) → không có trang detail để mở popup"
            ], round(time.time() - t0, 1)
    enc = urllib.parse.quote(uri, safe="")

    # 2. chips
    info, st, dt, err = get_json(f"/{alias}/genres?uri={enc}")
    if err:
        return mod, "FAIL", [], notes + [f"/genres lỗi ({err})"], round(time.time() - t0, 1)
    groups = info.get("groups") or []
    chips = [(g.get("title") or "", g.get("items") or []) for g in groups]
    n_chip = sum(len(it) for _, it in chips)
    if not chips or n_chip == 0:
        # chẩn đoán: uri tuyệt đối trỏ host mirror khác SiteHost → thử lại bằng path-only
        path_only = urllib.parse.urlparse(uri).path or ""
        if path_only and path_only != uri:
            info2, _, _, err2 = get_json(f"/{alias}/genres?uri=" + urllib.parse.quote(path_only, safe=""))
            if not err2 and (info2.get("groups") or []):
                return mod, "FAIL", [], notes + [
                    f"chips rỗng với uri tuyệt đối ({urllib.parse.urlparse(uri).netloc}) nhưng OK với path "
                    f"«{path_only}» → module phải normalize host mirror trước khi fetch detail"
                ], round(time.time() - t0, 1)
        return mod, "FAIL", [], notes + ["/genres trả 200 nhưng KHÔNG có groups/chip"], round(time.time() - t0, 1)
    notes.append(f"genres {len(chips)} nhóm / {n_chip} chip · {dt:.1f}s")

    # 3. chip hợp lệ + ngôn ngữ menu + chip "Liên quan" phải có plain:true
    bad_lang = []
    rel_chip = None
    for title, its in chips:
        for bad in langs(title):
            bad_lang.append(f"{bad}: nhóm «{title}»")
        for it in its:
            t = it.get("title") or ""
            if not t.strip():
                fails.append("chip rỗng title")
            if not (it.get("url") or "").strip():
                fails.append(f"chip «{t}» thiếu url")
            if "rel=" in (it.get("url") or ""):
                rel_chip = it
            for bad in langs(t):
                bad_lang.append(f"{bad}: chip «{t}»")
    if bad_lang:
        fails.append("menu sai ngôn ngữ (phải Việt/Anh): " + " · ".join(bad_lang[:4]))

    # 4. related
    rel, st, dt, err = get_json(f"/{alias}?rel={enc}&pg=1")
    if err:
        fails.append(f"?rel= lỗi ({err})")
    else:
        rl = rel.get("list") or []
        notes.append(f"rel {len(rl)} item · tp={rel.get('total_pages')} · {dt:.1f}s")
        if not rl:
            fails.append("?rel= trả list rỗng")
        else:
            no_url = sum(1 for it in rl if not (it.get("video") or it.get("url")))
            if no_url:
                fails.append(f"{no_url} item related không có url")
        if rel_chip is None:
            warns.append("?rel= chạy nhưng popup KHÔNG có chip mở related (thiếu chip plain:true url=?rel=)")
        elif not rel_chip.get("plain"):
            fails.append(f"chip «{rel_chip.get('title')}» mở ?rel= nhưng thiếu plain:true (app sẽ không gọi popup liên quan)")
        if (rel.get("total_pages") or 0) > 1:
            rl2, _, _, err2 = get_json(f"/{alias}?rel={enc}&pg=2")
            if err2:
                warns.append(f"?rel=&pg=2 lỗi ({err2})")
            elif (rl2.get("list") or []):
                warns.append("?rel=&pg=2 còn item (nên rỗng, total_pages 1)")

    # 5. SSRF / uri rác
    junk, st, dt, err = get_json(f"/{alias}/genres?uri=" + urllib.parse.quote("https://example.com/x"))
    if err:
        fails.append(f"SSRF uri rác gây lỗi ({err}) — phải trả 200 rỗng")
    elif (junk.get("groups") or []):
        warns.append("uri tuyệt đối lạ vẫn trả groups (nên rỗng)")

    verdict = "FAIL" if fails else ("WARN" if warns else "PASS")
    return mod, verdict, warns, notes + fails, round(time.time() - t0, 1)


def main():
    ap = argparse.ArgumentParser(description="Verify popup module Lampac (/genres?uri= + ?rel=)")
    ap.add_argument("modules", nargs="*", help="tên module hoặc alias route (vd tizam, xnx)")
    ap.add_argument("--all", action="store_true", help="quét mọi module đang enable trong runtime")
    ap.add_argument("--uri", help="dùng uri detail cố định thay vì lấy từ home")
    ap.add_argument("--jobs", type=int, default=3, help="số module chạy song song khi --all (default 3)")
    ap.add_argument("--quiet", action="store_true", help="chỉ in dòng kết luận")
    args = ap.parse_args()

    if not args.modules and not args.all:
        ap.error("cần tên module, hoặc --all")

    if args.all:
        pairs = all_modules()
        if args.jobs > 1:
            print(f"# --all: {len(pairs)} module, jobs={args.jobs} (mỗi module gọi site thật qua Lampac)", file=sys.stderr)
    else:
        index = {a: m for m, a in all_modules()}
        pairs = []
        for n in args.modules:
            alias, mod = resolve(n, index)
            pairs.append((mod, alias))

    results = []
    if args.all and args.jobs > 1 and len(pairs) > 1:
        with cf.ThreadPoolExecutor(max_workers=args.jobs) as ex:
            futs = [ex.submit(check, m, a, args.uri) for m, a in pairs]
            for f in cf.as_completed(futs):
                results.append(f.result())
    else:
        for m, a in pairs:
            results.append(check(m, a, args.uri))
    order = {m: i for i, (m, _) in enumerate(pairs)}
    results.sort(key=lambda r: order.get(r[0], 999))

    n_fail = 0
    for mod, verdict, warns, lines, dt in results:
        if verdict == "FAIL":
            n_fail += 1
        if not args.quiet:
            print(f"[{verdict}] {mod:<16} {dt}s")
            for w in warns:
                print(f"         ~ {w}")
            for ln in lines:
                print(f"         · {ln}")
    print(f"\n{len(results)} module: PASS={sum(1 for r in results if r[1] == 'PASS')} "
          f"WARN={sum(1 for r in results if r[1] == 'WARN')} FAIL={n_fail}")
    return 1 if n_fail else 0


if __name__ == "__main__":
    sys.exit(main())
