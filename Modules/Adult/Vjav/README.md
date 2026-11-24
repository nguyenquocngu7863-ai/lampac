# Vjav (vjav.com)

JAV / Asian tube (~447k video). Site là **SPA Vue** — HTML chỉ có `<div id="app">`,
toàn bộ dữ liệu đi qua JSON API:

| việc | endpoint |
|---|---|
| list / category / search | `/api/json/videos2/{lt}/{gender}/{sort}/{count}/{section}.{obj}.{pg}.{type}.{dur}.{date}.json` (search: `/api/videos2.php?params=...&s=<kw>`) |
| menu thể loại | `/api/json/categories/14400/str.all.en.json` |
| popup chip | `/api/json/video/{lt}/{id/1e6}/{1e3*(id/1e3)}/{id}.json` |
| liên quan | `/api/json/videos_related2/432000/60/{id/1e6}/{1e3*(id/1e3)}/{id}.relevance.1.json` |
| stream | `/api/videofile.php?video_id=<id>&lifetime=864000000` → `video_url` đã mã hoá → `VjavTo.DecodeVideoUrl` → `/get_file/3/<token>/...` |

`lifetime` của `videos_related2` **phải = 432000**, khác đi site trả
`{"error":1,"code":"invalid_lifetime"}`.

## Relay bắt buộc

TLS từ IP máy này tới `vjav.com` bị cắt ngay (`BoringSSL SSL_connect: Connection closed
abruptly`) với cả h1.1 và h2, nên **mọi** URL (list, detail, videofile, ảnh
`tn.vjav.com`, file `/get_file/`) đi qua Cloudflare Worker:

```
export VJAV_RELAY="https://xn-relay.nguyenquocngu7863.workers.dev"   # run-lampac.sh
```

`Tools/cf-relay/deploy.py` đã thêm `vjav.com,tn.vjav.com` vào ALLOW. Tắt relay:
`VJAV_RELAY=''` (module sẽ hỏng lại như trước).

Worker forward UA của client → module luôn gửi UA Chrome (`VjavTo.ChromeUA`) +
`Referer: https://vjav.com/`.

## Routes

| route | ghi chú |
|---|---|
| `/vjav` | `?pg=` `?search=` `?c=cat/<dir>` `?sort=` |
| `/vjav/genres?uri=<detail>` | `groups`: `Thể loại` (→ `?c=cat/<dir>`), `Thẻ` (→ `?search=<title>` vì site không có trang tag riêng), `Liên quan` (`plain: true` → `?rel=<uri>`) |
| `/vjav?rel=<detail>` | 1 trang, `total_pages: 1`, `pg>1` → `{total_pages:1,list:[]}`, uri lạ → 200 rỗng |
| `/vjav/vidosik?uri=<detail>` | trả `{ "720p": "<host>/vjav/video.mp4?uri=...&q=720p" }` |
| `/vjav/video.mp4?uri=&q=` | resolve server-side (token `ti=<epoch>` hết hạn) → redirect qua `HostStreamProxy` (URL đã wrap relay) |

`total_pages` của list lấy từ trường `pages` trong JSON (trước đây là 0 = "vô hạn").

## Đã đo (2026-11-24)

- `vjav` 60 item · `tp=7539`; `pg2` 60 · `search=asian` 60 · `c=cat/amateur` 60
- `genres?uri=` → 3 nhóm (6 thể loại / 11 thẻ / 1 liên quan)
- `?rel=` 60 item · `tp=1`; `pg=2` → rỗng; `rel=https://evil.example/x` → rỗng
- `play.sh` → **PHÁT ĐƯỢC (cảnh báo)**: h264 1280x720 479.8s, cold 0.22s /
  warm 0.13s, decode 5s đầu OK; **decode 5s cuối timeout** vì origin trả Range ở
  offset xa rất chậm qua relay (~8 KB/s ở 20MB, stall ở 30MB+) — giới hạn của
  site/egress Cloudflare, không phải lỗi module.
