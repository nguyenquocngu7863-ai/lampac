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

Nguồn duy nhất: **`hosts.txt`** (1 host/dòng, commit vào repo) — 18 host, gồm
`xnhau.plus,xnhau.pub,xnhau.pink,xnhau.moda,jav.guru,sextb.net,thichsex.org,lh3-ggcontent.top,uptobox-ggcdn.top,javenglish.cc,javeng.b-cdn.net,javquick.com,vjav.com,tn.vjav.com,qooglevideo.shop,xvideo.lat,bytecdn.cfd,javtiful.com`

| module | env | ghi chú |
|---|---|---|
| XNhau | `XNHAU_RELAY` | TLS từ IP máy hay reset (SSL EOF) |
| JavGuru | `JAVGURU_RELAY` | |
| VnChich2 | `VNCCHICH2_RELAY` | |
| SexTb | `SEXTB_RELAY` | **bắt buộc UA Chrome** mới qua CF challenge |
| JavQuick | `JAVQUICK_RELAY` | TLS bị cắt cả khi chào h2 **và** h1.1; ảnh `ie2.javquick.com` 403 trực tiếp |
| JavTiful | `JAVTIFUL_RELAY` | TLS reset từ IP máy (2026-11-28) |

Env đặt trong `termux-run-lampac.sh` (repo này). Tắt relay cho 1 module: `SEXTB_RELAY=''`.

## ⚠️ Lưu ý khi relay site có CF challenge

Worker forward UA **của client**. Gửi UA `curl/8.x` → site trả 403 "Checking your browser...".
Module phải gửi UA Chrome (`SexTbTo.ChromeUA`, `XNhauTo`…) thì mới 200.

## Deploy

```bash
python3 Tools/cf-relay/deploy.py                    # upload + verify + smoke + check ca allowlist
python3 Tools/cf-relay/deploy.py --add xnhau.video  # CỘNG THÊM host (ghi luôn vào hosts.txt)
python3 Tools/cf-relay/deploy.py --remove xnhau.moda  # gỡ host (hiếm khi cần)
python3 Tools/cf-relay/deploy.py --show             # in allowlist hiện tại, KHÔNG deploy
python3 Tools/cf-relay/deploy.py --test-host sextb.net
```

- **Worker chỉ có MỘT biến `ALLOW`; mỗi lần upload Cloudflare THAY cả chuỗi** — không merge.
  Vì vậy `deploy.py` dựng ALLOW = **UNION của 4 nguồn**: `hosts.txt` + `BASE_ALLOW` (seed cũ) +
  **ALLOW đang chạy (đọc từ API `/workers/scripts/<name>/settings`)** + `--add`. Không nguồn nào
  làm rơi host khác; sau upload script **đọc lại binding để verify** (thiếu/thừa → exit 1).
  Đã test self-heal: deploy hỏng còn 2 host (`xnhau.plus,javquick.com`) → chạy `deploy.py`
  không tham số → về đủ 18 host.
- Cuối mỗi lần deploy, script tự probe **từng host** trong ALLOW và fail (exit 1) nếu host nào
  trả `host not allowed`. Bỏ qua bằng `--no-check`.
- **`--test-host <host>` chỉ đổi host smoke**, không đụng ALLOW (sửa 2026-11-22).
- `termux-native/cf-relay` là **symlink** → `Tools/cf-relay`, không còn bản `deploy.py` cũ
  (`allow = --allow or --test-host or "xnhau.pink"`) để deploy nhầm. `wrangler.toml` chỉ tham
  khảo — **đừng `wrangler deploy`** (nó cũng thay nguyên ALLOW).
- `xnhau-domain.sh` bước 5 gọi `deploy.py --add "<domain mới> <olds> <history>"`, không tự dựng
  ALLOW nữa (trước 2026-11-28 nó xoá sextb/javquick/vjav/qooglevideo… mỗi lần đổi domain).
- Token: `~/.secrets/cf-token` hoặc env `CF_TOKEN` (quyền *Edit Cloudflare Workers*).
- `wrangler` **không chạy được trên Termux** (workerd báo `Unsupported platform: android arm64`)
  → deploy qua REST API như `deploy.py`.
- Sửa allowlist: chỉ thêm dòng vào `hosts.txt` (hoặc `--add`). `BASE_ALLOW` trong `deploy.py` và
  `wrangler.toml` chỉ còn là seed/ tham khảo — **đừng `wrangler deploy`** kẻo ghi đè.