# SexTb (sextb.net)

Module Adult cho sextb.net (JAV streaming, HTML server-side).

## CF relay (bắt buộc từ 2026-11-13)

Site bật **Cloudflare challenge với mọi request server-side**:

| đường | kết quả |
|---|---|
| `curl_cffi` (chrome124) trực tiếp | `SSL_connect: Connection closed abruptly` |
| `httpHydra` / `Http.Get` (in-app) | 403 / body rỗng |
| Chrome thật (`x.sh`, Xvnc) | 200 |
| Qua Worker `xn-relay` + **UA Chrome** | 200 (245 KB) |
| Qua Worker với UA `curl/8.x` | 403 "Checking your browser..." |

⇒ Mọi fetch HTML đi qua Worker `xn-relay` (egress Cloudflare):

- `wrangler.toml` / `deploy.py`: `ALLOW` phải có `sextb.net`
- Worker forward thêm `content-type` / `authorization` / `x-requested-with` / body POST
  và **set-cookie** (session của `/ajax/player`), `cacheTtl` 60 s
- Module: env **`SEXTB_RELAY`**, đặt trong `termux-run-lampac.sh`
  (`SEXTB_RELAY='' ` để tắt, về direct)
- `SexTbTo.Relay()` bọc `GetPageAsync()` (mọi trang HTML) và `/ajax/relatedAjax`

Không relay media: CDN (`*.growcdnssedge.com`, `tsvideo.sacdnssedge.com`, Dood…)
phát trực tiếp bình thường.

## Kết cấu site (đo 2026-11-13, KHÔNG đổi)

- List: `<div class="tray-item ...">` + `tray-item-thumbnail` / `tray-item-title`
  / `tray-item-code` / `tray-item-runtime` / `tray-item-quality`
- Detail: `id="infomation"` (chip), `filmId = <số>`, `window.__pt/__pk`,
  `name="_token"` + `name="_socket"` (Authorization cho `/ajax/player`)
- Related: `POST /ajax/relatedAjax` form `pg=1&filmId=<id>` → JSON array

## Verify

home 72 item · category/search · `genres` 6 nhóm (Diễn viên/Hãng/Nhãn/Đạo diễn/
Thể loại/Liên quan) · `?rel=` 12 item · stream **F4 (HLS)** và **DD (mp4)**
`play.sh` → PHÁT ĐƯỢC.