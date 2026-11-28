#!/usr/bin/env python3
"""Deploy Worker qua Cloudflare REST API (wrangler không chạy được trên Termux:
workerd báo "Unsupported platform: android arm64").

Token: ~/.secrets/cf-token (hoặc env CF_TOKEN). Cần quyền "Edit Cloudflare Workers".
Dùng: python3 deploy.py [--add host1,host2] [--test-host host]

Worker chỉ có MỘT biến `ALLOW`; mỗi lần upload Cloudflare THAY cả chuỗi, không merge.
Vì vậy danh sách gốc nằm ở BASE_ALLOW dưới đây, và `--add` (hoặc `--allow` cũ) chỉ
CỘNG THÊM — không bao giờ làm rơi host của module khác.
"""
import json
import mimetypes
import os
import pathlib
import sys
import urllib.request
import urllib.error
import uuid

API = "https://api.cloudflare.com/client/v4"
HERE = pathlib.Path(__file__).parent
NAME = "xn-relay"
SCRIPT = HERE / "src" / "index.js"


def token():
    t = os.environ.get("CF_TOKEN", "").strip()
    if t:
        return t
    p = pathlib.Path.home() / ".secrets" / "cf-token"
    if p.exists():
        return p.read_text().strip()
    sys.exit("thiếu token: ghi token vào ~/.secrets/cf-token hoặc export CF_TOKEN=...")


def call(method, path, body=None, raw=None, ctype=None):
    url = API + path
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Authorization", "Bearer " + token())
    if data is not None:
        req.add_header("Content-Type", ctype or "application/json")
    try:
        with urllib.request.urlopen(req, timeout=90) as r:
            return json.loads(r.read() or b"{}")
    except urllib.error.HTTPError as e:
        txt = e.read().decode("utf-8", "replace")
        sys.exit(f"HTTP {e.code} {path}\n{txt[:800]}")


def multipart(parts):
    """parts: list[(name, value_bytes, ctype, filename_or_None)]"""
    b = "----cf" + uuid.uuid4().hex
    out = b""
    for name, value, ct, fn in parts:
        disp = f'form-data; name="{name}"' + (f'; filename="{fn}"' if fn else "")
        out += f"--{b}\r\nContent-Disposition: {disp}\r\n".encode()
        out += f"Content-Type: {ct}\r\n\r\n".encode() + value + b"\r\n"
    out += f"--{b}--\r\n".encode()
    return out, "multipart/form-data; boundary=" + b


# Host duoc phep relay — NGUON DUY NHAT cua allowlist.
# JavGuru wrap jav.guru (JAVGURU_RELAY cung worker nay); QmhSex/Mbbg wrap embed
# (qooglevideo.shop, xvideo.lat) va CDN segment (bytecdn.cfd).
BASE_ALLOW = (
    "xnhau.plus,xnhau.pub,xnhau.pink,xnhau.moda,"
    "jav.guru,sextb.net,thichsex.org,"
    "lh3-ggcontent.top,uptobox-ggcdn.top,javenglish.cc,javeng.b-cdn.net,"
    "javquick.com,vjav.com,tn.vjav.com,"
    "qooglevideo.shop,xvideo.lat,bytecdn.cfd"
)


def _arg(name):
    if name in sys.argv:
        i = sys.argv.index(name)
        if i + 1 < len(sys.argv):
            return sys.argv[i + 1]
    return None


def _split(s):
    return [h.strip().lower() for h in (s or "").replace(",", " ").split() if h.strip()]


def allowlist():
    """BASE_ALLOW + `--add` (+ `--allow` cũ) — luôn CỘNG THÊM, không thay thế.

    (2026-11-22: `--test-host javquick.com` từng ghi đè còn 1 host;
     2026-11-27: xnhau-domain.sh `--allow` xoá sextb/javquick/... — hai lần cùng
     một nguyên nhân: deploy thay nguyên biến ALLOW.)
    """
    allow = _split(BASE_ALLOW)
    for extra in (_arg("--add"), _arg("--allow")):
        for h in _split(extra):
            if h not in allow:
                allow.append(h)
    return allow


def main():
    allow = allowlist()
    # --test-host CHI doi host smoke, KHONG dung toi ALLOW.
    smoke_override = _arg("--test-host")

    acc = call("GET", "/accounts")
    accounts = acc.get("result") or []
    if not accounts:
        sys.exit("token không thấy account nào (thiếu quyền Account:Read?)")
    account = os.environ.get("CF_ACCOUNT_ID") or accounts[0]["id"]
    print(f"account: {account} ({accounts[0].get('name')})")

    meta = {
        "main_module": "index.js",
        "compatibility_date": "2026-01-01",
        "bindings": [{"type": "plain_text", "name": "ALLOW", "text": ",".join(allow)}],
    }
    body, ct = multipart([
        ("metadata", json.dumps(meta).encode(), "application/json", None),
        ("index.js", SCRIPT.read_bytes(), "application/javascript+module", "index.js"),
    ])
    r = call("PUT", f"/accounts/{account}/workers/scripts/{NAME}", raw=body, ctype=ct)
    print("upload:", "OK" if r.get("success") else r)

    call("POST", f"/accounts/{account}/workers/scripts/{NAME}/subdomain",
         {"enabled": True, "previews_enabled": False})

    sub = call("GET", f"/accounts/{account}/workers/subdomain")
    sd = (sub.get("result") or {}).get("subdomain")
    url = f"https://{NAME}.{sd}.workers.dev"
    print("URL:", url)
    print("allow:", len(allow), "host")

    # Smoke: host chi dinh, khong thi host dau tien. 403 cua SITE (CF challenge)
    # khong phai loi allow — chi quan tam body "host not allowed".
    smoke_host = (smoke_override or allow[0]).strip()
    print("test:", f"{url}/{smoke_host}/")

    try:
        req = urllib.request.Request(f"{url}/{smoke_host}/", headers={
            "User-Agent": "Mozilla/5.0 (Linux; Android 14; SM-A525F) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36"
        })
        with urllib.request.urlopen(req, timeout=45) as resp:
            first = resp.read(200)
            print(f"smoke: HTTP {resp.status} {resp.headers.get('content-type')} | {first[:80]!r}")
    except urllib.error.HTTPError as e:
        body = e.read(120).decode("utf-8", "replace")
        print(f"smoke: HTTP {e.code} | {body!r}" + ("  ← ALLOW THIEU HOST" if "host not allowed" in body else ""))
    except Exception as e:
        print("smoke lỗi:", type(e).__name__, str(e)[:200])

    # Kiem tra allowlist: moi host goc phai qua duoc buoc "host not allowed".
    if "--no-check" in sys.argv:
        return

    from concurrent.futures import ThreadPoolExecutor

    ua = {"User-Agent": "Mozilla/5.0 (Linux; Android 14) Chrome/124.0.0.0 Mobile Safari/537.36"}

    def probe(h):
        try:
            with urllib.request.urlopen(urllib.request.Request(f"{url}/{h}/", headers=ua), timeout=25) as r:
                return h, r.status
        except urllib.error.HTTPError as e:
            return h, e.code, e.read(120).decode("utf-8", "replace")
        except Exception as e:
            return h, 0, type(e).__name__

    missing = []
    with ThreadPoolExecutor(max_workers=6) as ex:
        for row in ex.map(probe, allow):
            if len(row) == 3 and "host not allowed" in row[2]:
                missing.append(row[0])

    print("check:", "OK — moi host trong ALLOW deu duoc relay" if not missing
          else "THIEU host: " + ",".join(missing))
    if missing:
        sys.exit(1)


if __name__ == "__main__":
    main()
