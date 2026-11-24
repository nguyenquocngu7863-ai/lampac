# XszAv (xsz-av.com)

JAV tổng hợp (Qwik SSR). List + detail render server-side, HTML khá nặng (~265 KB/trang
detail) nhưng **fetch trực tiếp được** — không cần relay.

## Routes

| route | ghi chú |
|---|---|
| `/xszav` | `?search=` `?c=videos/<cat>` `?c=tags/<slug>` `?c=actresses/<tên>` `?sort=bw\|mv\|tf\|lg` `?pg=` |
| `/xszav/genres?uri=<detail>` | `groups`: `Diễn viên` (`/actresses/<tên>` → `?c=actresses/<tên>`), `Thẻ` (`/search/videos/<kw>` → `?search=<kw>`), `Liên quan` (`plain: true` → `?rel=<uri>`) |
| `/xszav?rel=<detail>` | 26 card "cùng diễn viên / cùng thể loại" nằm sau khối `class="thumbnail"` (loại bài chính), `total_pages: 1`, `pg>1` → `{tp:1,list:[]}`, uri lạ/rỗng → 200 rỗng |
| `/xszav/vidosik?uri=<detail>` | `{ "HD": "<host>/xszav/video.m3u8?uri=&q=HD" }` |
| `/xszav/video.m3u8?uri=&q=` | lấy `<video src="....m3u8">` → redirect qua `HostStreamProxy` |

## Popup: nguồn chip

Trang detail có khối thông tin nằm **giữa `<h1>` và card đầu tiên (`class="thumbnail"`)** —
chỉ trong khoảng này mới có link của chính video (12 `/search/videos/<kw>` + 1 `/actresses/<tên>`);
`/tags/` và `/videos/` xuất hiện nhiều nhưng đa số là **mega-menu**, không phải chip.

- Chip actress trả slug **raw** từ `XszAvTo.Chips`, Controller `HttpUtility.UrlEncode` **một lần**
  khi ghép URL (encode 2 lần → site 404).
- Keyword href có `%20` ở đầu → phải `Trim()` trước khi tạo `?search=`.

Cache: `xszav:info:{uri}` 360 phút, `xszav:rel:{uri}` 20 phút; `?rel=` không thêm request
nào cho `vidosik` (route stream vẫn đọc `xszav:servers:{uri}` như cũ).

## Đã đo (2026-11-24)

- home 11 item · `tp=4289`; `pg2` 11; `search=teen` 11 · `tp=4`; `c=videos/uncensored` 11 · `tp=767`
- `genres?uri=` → 3 nhóm (1 diễn viên / 12 thẻ / 1 liên quan)
- chip diễn viên → 11 item · `tp=6`; chip thẻ `?search=女教師` → 11 item · `tp=834`
- `?rel=` 26 item · `tp=1`; `pg=2` rỗng; `rel=https://evil.example/x` rỗng
- `play.sh` → **PASS**: h264 1280x720 · 7188s, segment 2/2, decode 5s đầu **và** cuối OK,
  cold 0.17s / warm 0.18s
