#!/usr/bin/env python3
"""Deploy Worker qua Cloudflare REST API (wrangler không chạy được trên Termux:
workerd báo "Unsupported platform: android arm64").

Token: ~/.secrets/cf-token (hoặc env CF_TOKEN). Cần quyền "Edit Cloudflare Workers".
Dùng: python3 deploy.py [--test-host xnhau.pub]
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


def main():
    allow = "xnhau.pub"
    if "--test-host" in sys.argv:
        allow = sys.argv[sys.argv.index("--test-host") + 1]

    acc = call("GET", "/accounts")
    accounts = acc.get("result") or []
    if not accounts:
        sys.exit("token không thấy account nào (thiếu quyền Account:Read?)")
    account = os.environ.get("CF_ACCOUNT_ID") or accounts[0]["id"]
    print(f"account: {account} ({accounts[0].get('name')})")

    meta = {
        "main_module": "index.js",
        "compatibility_date": "2026-01-01",
        "bindings": [{"type": "plain_text", "name": "ALLOW", "text": allow}],
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
    print("test:", f"{url}/{allow}/")

    # smoke test qua worker (1 request)
    try:
        with urllib.request.urlopen(f"{url}/{allow}/", timeout=45) as resp:
            first = resp.read(200)
            print(f"smoke: HTTP {resp.status} {resp.headers.get('content-type')} | {first[:80]!r}")
    except Exception as e:
        print("smoke lỗi:", type(e).__name__, str(e)[:200])


if __name__ == "__main__":
    main()
