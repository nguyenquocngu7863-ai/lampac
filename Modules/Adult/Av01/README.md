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
   - `q=360p|720p|1080p` → trả playlist variant (segment tuyệt đối, player tải trực tiếp).

## Đã đo (2026-11-27)
- `/av01` 48 item, tổng 209 trang; `?sort=hottest`, `?c=tag:..`, `?c=actress:..`, `?pg=2` OK.
- `/av01/genres?uri=` → 3 nhóm (Diễn viên / Thể loại / Nhà sản xuất) + mục "Liên quan".
- `/av01/vidosik` → `{qualitys: 360p/720p/1080p, recomends: 20}`.
- `play.sh` 1080p: h264 1920x1080, 906 segment, decode 5s đầu + 5s cuối → **PASS (~90%)**.

## Bẫy
- **Search API không dùng được cho khách**: `POST /api/v1/videos/search` và `/complex_search` trả `400 {"error":"Invalid video ID format"}` (gateway route nhầm sang `/videos/{id}`) → module **không có ô tìm kiếm**.
- Segment là URL tuyệt đối trên `customers.iw01.xyz` → app Android phát trực tiếp OK; web/TV có thể vướng CORS (cần proxy segment nếu gặp).
- `access_token` gắn với IP + hết hạn 60 phút; giữ cache playlist ngắn (20 phút).
- `IP` là IPv6 (Viettel), phải truyền nguyên (không URL-encode dấu `:`) như trình duyệt.
