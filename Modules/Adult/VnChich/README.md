# VnChich — module Adult (tách khỏi NextHUB)

Site: `https://vn.vnchich.vip` (PeerTube/JVideos API), route: `/vnchich`.

Trước đây VnChich là site YAML của NextHUB (`Modules/NextHUB/sites/vnchich.yaml`).
Bây giờ là module C# độc lập: có sort đầy đủ, popup long-press (thẻ/kênh/tác giả +
video liên quan), nhiều nguồn phát (mp4 theo resolution + HLS master).

## API (đo 2026-11-04)

| Việc | Endpoint |
|------|----------|
| Danh sách | `GET /api/v1/videos?start={n}&count=12&sort={sort}` |
| Tìm kiếm | `...&search={từ khoá}` |
| Lọc thẻ | `...&tagsOneOf={thẻ}` (đúng 1 thẻ, phân cách bằng `,` không work) |
| Lọc thể loại PeerTube | `...&categoryOneOf={id}` |
| Chi tiết | `GET /api/v1/videos/{uuid}` |

`channelOneOf` / `accountOneOf` **bị API bỏ qua** (total trả về luôn bằng total toàn site)
→ không dùng để lọc.

Response list: `{"total": 19413, "data": [ ... ]}` — item **không** có field `tags`.

## Sort (đều trả 200 ở home / danh mục / search)

| Nhãn | Query |
|------|-------|
| Mới nhất | `-publishedAt` |
| Xem nhiều nhất | `-views` |
| Thích nhiều nhất | `-likes` |
| Dài nhất | `-duration` |
| Thịnh hành | `-trending` |
| Hot | `-hot` |
| Nhiều bình luận | `-comments` |

`-relevance` → HTTP 400 (không dùng).

## Danh mục (menu)

Site không có API taxonomy (`/api/v1/search/video-tags` → 400, `/api/v1/videos/tags` → 400).
Danh mục trong menu là danh sách thẻ phổ biến, lọc bằng `tagsOneOf`; nếu thẻ không tồn tại
module tự lùi về `search` (Controller: `asSearch: true`).

Thẻ lấy động cho popup đến từ `tags` của API chi tiết.

## Nguồn phát

- Video **local**: `files[]` = mp4 theo resolution (`fileUrl`), bỏ `Audio only` → nhiều nguồn
  (vd `406p`).
- Video **remote**: `files[]` chỉ có audio, `streamingPlaylists[]` = HLS master → 1 nguồn `HLS`.

`/vnchich/vidosik` trả map `{label: url}` (app đọc label để chọn chất lượng),
`/vnchich/video?uri=&q=` redirect qua `HostStreamProxy` (giữ Referer/Origin cho CDN).

## Popup long-press

`GET /vnchich/genres?uri={uuid}` → `{items:[], groups:[...]}`:

- `Thẻ` — tags của video → `?c=tag/<thẻ>`
- `Liên quan` — 1 item `plain` → `?rel=<uuid>` (cùng thẻ: ưu tiên thẻ thuộc danh mục,
  không có thì thẻ đầu tiên)

Cache detail 30 phút, cache liên quan 180 phút (RAM).
