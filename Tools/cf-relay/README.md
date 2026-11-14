# cf-relay — Worker relay cho module Adult

Cloudflare Worker `xn-relay` cho các site chặn request server-side (TLS fingerprint / CF
challenge). Egress qua IP Cloudflare + forward header/body đầy đủ.

**Worker:** `https://xn-relay.nguyenquocngu7863.workers.dev`

## Cách dùng

```
https://<worker>/<host>/<path>?<query>      →  https://<host>/<path>?<query>
```

- Chỉ host trong `ALLOW` (biến Worker) mới được relay — không thành open relay.
- `GET` được cache ở edge (`cacheTtl: 60s`); `POST` forward **body** + `content-type`.
- Forward: `range` `referer` `origin` `accept` `accept-language` `cookie`
  `authorization` `content-type` `x-requested-with` + `set-cookie` (nhiều header).
- `?__nf=1` = trả 3xx nguyên vẹn (redirect: manual) cho gateway cần biết host đích.

## Host đang bật (ALLOW)

`xnhau.pub,xnhau.pink,jav.guru,sextb.net`

| module | env | ghi chú |
|---|---|---|
| XNhau | `XNHAU_RELAY` | TLS từ IP máy hay reset (SSL EOF) |
| JavGuru | `JAVGURU_RELAY` | |
| VnChich2 | `VNCCHICH2_RELAY` | |
| SexTb | `SEXTB_RELAY` | **bắt buộc UA Chrome** mới qua CF challenge |

Env đặt trong `termux-run-lampac.sh` (repo này). Tắt relay cho 1 module: `SEXTB_RELAY=''`.

## ⚠️ Lưu ý khi relay site có CF challenge

Worker forward UA **của client**. Gửi UA `curl/8.x` → site trả 403 "Checking your browser...".
Module phải gửi UA Chrome (`SexTbTo.ChromeUA`, `XNhauTo`…) thì mới 200.

## Deploy

```bash
python3 Tools/cf-relay/deploy.py            # upload + smoke host dau tien
python3 Tools/cf-relay/deploy.py --test-host sextb.net
```

- Token: `~/.secrets/cf-token` hoặc env `CF_TOKEN` (quyền *Edit Cloudflare Workers*).
- `wrangler` **không chạy được trên Termux** (workerd báo `Unsupported platform: android arm64`)
  → deploy qua REST API như `deploy.py`.
- `ALLOW` nằm trong `deploy.py` (biến `allow`) và `wrangler.toml` (tham khảo) — sửa cả 2.