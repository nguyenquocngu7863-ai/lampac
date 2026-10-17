# Javtiful (javtiful.com)

Module Adult route `javtiful`, displayindex 19. Nhóm SISI chuẩn static-parse (khuôn Vlxx):
`httpHydra` + regex, **không Playwright, không hook global**.
Công thức player: skill `lampac-deobfuscate` (MP4 direct, không cần giải mã).

## Route

| route | việc |
|---|---|
| `javtiful?c=<path>&pg=N&sort=X` | danh sách + menu (mặc định feed `/vn/foryou`) |
| `javtiful?search=<kw>` | tìm kiếm (`GET /vn/search?q=<slug lowercase>`) |
| `javtiful/vidosik?uri=<detailUrl>` | liệt kê server (`json=true` → `{MP4: url}`) |
| `javtiful/video?uri=<detailUrl>&q=MP4` | proxy stream (thiếu `q` → 503 sạch, không 500) |

## Cấu trúc site

- Home `/vn` **không phân trang** (`?page=` bị bỏ qua) → module dùng feed `/vn/foryou`.
- Section (đều `?page=N`): `/vn/foryou`, `/vn/censored`, `/vn/uncensored`, `/vn/reducing-mosaic`.
- Category: `/vn/category/{slug}?page=N` (+ `sort=popular|popular_today|popular_week|popular_month|most_viewed|most_liked`).
- Kênh: `/vn/channel/{slug}?page=N`; danh sách kênh `/vn/channels?page=N` (14 trang).
- Search: `/vn/search?q={slug}` — **slug lowercase** (query HOA từng trả 502).
- Video: `/vn/video/{id}/{slug}`.

### ⚠️ Đổi cơ chế 2026-10-17 — site thay design system, class cũ = 0

| cũ (hỏng) | mới |
|---|---|
| `<article class="front-video-card">` | `<article class="video-card" data-recommendation-id=…>` |
| `a.front-video-thumb` | `a.video-card__thumbnail` |
| `a.front-video-title` | `a.video-card__title` (title vẫn là **text trong thẻ**, không phải attr) |
| `a.front-collection-card-link` + `strong.front-collection-title` | `a.directory-card__link` + `strong.directory-card__name` |

- `data-front-lazy-src` (poster) và `data-front-video-preview-src` (preview mp4) **giữ nguyên**.
- **Mới: thẻ quảng cáo** `<article class="video-card video-card--partner">` — `href` ra ngoài
  (`t.fluxtrck.site`) → tách article theo `<article class="` rồi lọc bằng href `/vn/video/`.
- Trang list còn có container `data-endpoint="/vn/foryou/impressions"` + `data-token` (feed gợi ý
  phía client) — **không cần**, server vẫn render + phân trang bình thường.

## Danh mục

- `Thể loại`: **hardcode 21 genre** (`?c=category/{slug}`) — không phụ thuộc parse, không hỏng khi site đổi class.
- `Kênh`: lấy động từ `/vn/channels` (314 kênh / 14 trang), cắt trang nếu > 20.
- `Kiểm duyệt`: Có che / Không che / Giảm mosaic; `Danh sách khác`: Mới nhất / JAVMost.

## Server + player

- `<source src="https://fast-stream.jav.si/p/…">` = **MP4 direct** (HEAD: 200, hỗ trợ `Range`).
- 1 quality duy nhất → `{MP4: url}` → `/javtiful/video` → `HostStreamProxy` kèm
  `Referer: https://javtiful.com/` + `Origin`.
- Poster: `poster` của thẻ `<video>` (relative → prefix host).

## Số đo (2026-10-17, sau khi site đổi design)

| thứ | giá trị |
|---|---|
| item/trang | **23 phim + 1 thẻ QC** (mọi nguồn: foryou, censored, uncensored, category, channel, search) |
| `/vn/channels` | 24 kênh/trang, **14 trang**, tổng 314 |
| foryou p1/p2 (site raw) | overlap **0** |
| foryou p1/p2 (qua module) | overlap **~1** — p1 đã nằm cache 10p còn p2 vừa fetch, feed gợi ý **trôi** giữa 2 thời điểm. Không phải lỗi parse (cache key có `pg`) |
| player | `GET /javtiful/video?…&q=MP4` → 200, magic `ftypisom`, `video/mp4` |

## Đã biết là lỗi site / lỗi vận hành

- Gọi dồn dập → site trả **503 `playlists`** ở module (raw vẫn 200) hoặc **SSL abort** ở curl.
  Chờ vài phút là hết; không phải lỗi parse.
- Thiếu `q` ở `/javtiful/video` trước đây ném exception → **500**; đã guard thành 503.

## Vận hành

- List cache 10p, resolve cache 20p (`hybridCache`), cache key `javtiful:{search}:{c}:{sort}:{pg}`.
- Copy `.cs` vào `~/lampac-run/module/Adult/Javtiful/` + restart, **không cần `dotnet build`**.
- Verify: `/javtiful` 23 item, `/javtiful?c=censored` 23, `vidosik` ra `MP4`, `/video?q=MP4` 200 `ftyp`.
