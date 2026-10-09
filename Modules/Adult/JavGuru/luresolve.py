#!/usr/bin/env python3
# luresolve.py — resolve LuluStream (streamhihi/lulu) TRON trong 1 session.
# Dung: luresolve.py <gateway-hoac-embed-url> <referer> <timeout-giay>
# In 3 dong: variant-url / cao-px / embed-url-cuoi. Loi -> exit != 0.
#
# Ly do ton tai: CDN *.tnmr.org ky TOKEN theo TLS-fingerprint cua ben goi
# (embed/master duoc serve rieng theo client). Module dung curl (.NET/curl
# thuong) thi token "ho curl" — app (Chromium) an 403. Phai mint bang
# Chrome fingerprint (curl_cffi chrome124) thi app moi tai duoc. Gateway
# jav.guru/searcho hay rong (520/timeout) nen retry trong chinh script nay.
import re
import sys
import time

from curl_cffi import requests as rq

UA = ('Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 '
      '(KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36')


def to_base(value, base):
    if value == 0:
        return '0'
    out = ''
    while value > 0:
        d = value % base
        out = chr(ord('0') + d if d < 10 else ord('a') + d - 10) + out
        value //= base
    return out or '0'


def unpack(html):
    m = re.search(r"\}\('([\s\S]*?)',(\d+),(\d+),'([\s\S]*?)'\.split\('\|'\)\)", html)
    if not m:
        return None
    p, a, c, k = m.group(1), int(m.group(2)), int(m.group(3)), m.group(4).split('|')
    for i in range(c - 1, -1, -1):
        if i < len(k) and k[i]:
            p = re.sub(r'\b' + to_base(i, a) + r'\b', k[i], p)
    f = re.search(r'file\s*:\s*["\'](https?://[^"\']+?\.m3u8[^"\']*)["\']', p)
    return f.group(1) if f else None


def main():
    gateway, referer, timeout = sys.argv[1], sys.argv[2], int(sys.argv[3])
    s = rq.Session(impersonate='chrome124', timeout=timeout,
                   headers={'User-Agent': UA})
    emb = None
    for attempt in range(3):
        try:
            emb_resp = s.get(gateway, headers={'Referer': referer})
            emb = emb_resp.url
            master = unpack(emb_resp.text)
            if master:
                break
        except Exception:
            master = None
        if attempt < 2:
            time.sleep(1)
    if not master:
        sys.exit(1)
    body = s.get(master, headers={'Referer': emb}).text
    best, besth = '', -1
    for m in re.finditer(r'#EXT-X-STREAM-INF:([^\r\n]*)\r?\n\s*(\S+)', body):
        h = re.search(r'RESOLUTION=\d+x(\d+)', m.group(1))
        h = int(h.group(1)) if h else 0
        u = m.group(2).strip()
        if not u.startswith('http'):
            u = master.rsplit('/', 1)[0] + '/' + u
        if h >= besth:
            besth, best = h, u
    if not best.startswith('http'):
        sys.exit(2)
    sys.stdout.write(best + '\n' + str(besth) + '\n' + emb + '\n')


if __name__ == '__main__':
    main()