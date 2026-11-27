# Av01 (av01.tv)

Module Adult route `av01`, displayindex 82. Site: **av01.tv** (SPA Vite + REST API `/api/v1`, Cloudflare).

## Điểm khó
- Trang là **SPA** (1 bundle `assets/index-*.js`), HTML không có dữ liệu → phải dịch ngược bundle để lấy API.
- Player cần **token 2 lớp**:
  - `token_v2` + `expires` + `ip` từ `https://files.iw01.xyz/edge/geo.js?json` (sống 30 phút).
  - `access_token` (JWT, IP-bound, 60 phút) ký sẵn trong từng variant của `/playlist`.
- Segment nằm ở CDN `customers.iw01.xyz`, **403 nếu thiếu `access_token`**; gọi qua route `av01/video.m3u8` mới có token đúng.

## API (same-origin)
| Chức năng | Endpoint |
|---|---|
| Mới nhất | `GET /api/v1/videos/types/latest?page=N&limit=48` |
| Xem nhiều | `GET /api/v1/videos/types/hottest?page=N&limit=48` |
| Theo thể loại | `GET /api/v1/videos/tag/{id}?page=N&limit=48` |
| Theo diễn viên | `GET /api/v1/videos/actress/{id}?page=N&limit=48` |
| Theo NSX | `GET /api/v1/videos/maker/{id}?page=N&limit=48` |
| Chi tiết | `GET /api/v1/videos/{id}` |
| Liên quan | `GET /api/v1/videos/{id}/similars?page=N&limit=48` |
| Thể loại (menu) | `GET /api/v1/tags/by-video-count?page=1&limit=80` |
| Diễn viên (menu) | `GET /api/v1/actresses/by-video-count?page=1&limit=60` |
| NSX (menu) | `GET /api/v1/makers/by-video-count?page=1&limit=30` |

`pagination.totalPages` = tổng trang.

## Stream
1. `geo.js?json` → `{token_v2, expires, ip}` (cache 20 phút).
2. `GET /api/v1/videos/{id}/playlist?expires=&ip=&token_v2=` → `{src: "data:application/x-mpegurl;base64,..."}` (master đã ký `access_token` cho từng variant).
3. Variant: `GET /api/v1/videos/{id}/manifest/{file}?access_token=` → m3u8 với segment **tuyệt đối** trên `customers.iw01.xyz` (kèm token).
4. Route `av01/video.m3u8?uri=&q=`:
   - `q` rỗng → trả master, variant trỏ về chính route này.
   - `q=360p|720p|1080p` → **302** sang `/proxy/...` của Lampac (`force_streamproxy`); `ProxyM3u8`
     rewrite cả segment lẫn `EXT-X-MAP` về cùng origin nên web/TV không bị CORS chặn.

## Ảnh bìa
- Host tĩnh `img1.iw01.xyz/media/videos/tmb/{id}/1.jpg` đã trả **403** (không còn dùng được).
- Ảnh thật nằm trên R2: `https://files.iw01.xyz/covers/{id}/800.webp?token_v2=&expires=&ip=`
  (200 kể cả khi không gửi Referer) — cần token từ `geo.js`.
- Route `av01/poster?uri=<detail>`: lấy geo (cache 20 phút) rồi **302** sang URL R2 còn hạn.
  `picture` trong list trỏ về route này nên app luôn lấy được ảnh mới.

## Đã đo (2026-11-27)
- `/av01` 48 item, tổng 209 trang; `?sort=hottest`, `?c=tag:..`, `?c=actress:..`, `?pg=2` OK.
- `/av01/genres?uri=` → 3 nhóm (Diễn viên / Thể loại / Nhà sản xuất) + mục "Liên quan".
- `/av01/vidosik` → `{qualitys: 360p/720p/1080p, recomends: 20}`.
- `play.sh` 1080p: h264 1920x1080, 906 segment, decode 5s đầu + 5s cuối → **PASS (~90%)**.
- Fix ảnh (2026-11-27): 20/20 `picture` trong list trả 200 JPEG qua `av01/poster` (trước đó 403).
- Fix stream refactor geo (2026-11-27): `play.sh` 720p vẫn **PHÁT ĐƯỢC**, ffmpeg decode 5s đầu + cuối OK.

## Bẫy
- **Search API không dùng được cho khách**: `POST /api/v1/videos/search` và `/complex_search` trả `400 {"error":"Invalid video ID format"}` (gateway route nhầm sang `/videos/{id}`) → module **không có ô tìm kiếm**.
- Segment gốc ở `customers.iw01.xyz` → trả thẳng thì web/TV chết vì CORS; đã chuyển qua `/proxy/` (đo 146/146 segment qua Lampac, 900/993 với 1080p).
- `access_token` gắn với IP + hết hạn 60 phút; giữ cache playlist ngắn (20 phút).
- `IP` là IPv6 (Viettel), phải truyền nguyên (không URL-encode dấu `:`) như trình duyệt.
