# SexTb (sextb.xyz)

Module Adult cho sextb.xyz (JAV streaming, HTML server-side). Domain cũ
`sextb.net` đã chết (TLS đóng với mọi client) — 2026-11-28 chuyển sang `.xyz`
(markup `tray-item` + API `/ajax/player` giữ nguyên). Request server-side đi
thẳng bằng `curl_cffi` chrome124 (retry 3 lần); worker relay chỉ dùng cho
domain `.net` cũ nên bị bỏ qua với `.xyz`.

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
## DISABLED (2026-11-28)

User chốt tắt module: site chặn gắt, chỉ là **embed** (F4/FL/SW/DD) chứ không có server riêng
như MissAV/JavTiful ⇒ bảo trì không đáng. Tắt bằng `~/lampac-run/init.conf`:

```json
"SexTb": { "enable": false }
```

`manifest.json` giữ `enable: true` (đúng quy ước như `DuJav`, `SexHihi` — override nằm ở init.conf).
Đo sau restart: `GET /sextb` → **403 `{"accsdb":true,"error":true,"msg":"disable"}`**,
`GET /sisi` **132 channel, không còn SexTb**. Bật lại: xoá/đổi key trong init.conf rồi restart.

Đo trước khi tắt (để tham chiếu): 12/12 item `recomends` trả `qualitys`; 16/16 item home OK;
F4/FL/SW/DD đều ra stream (`206`/`200`). Điểm giòn đã thấy: resolve fail (site siết IP) thì
`EmbedsAsync` cache `"{}"` **1 phút** ⇒ bấm server nào cũng `stream_links` trong 1 phút đó.
