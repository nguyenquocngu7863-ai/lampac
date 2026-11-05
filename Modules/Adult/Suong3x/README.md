# Suong3x — module Adult

Site: `https://phimsexsuong3x.net` (WordPress theme "vtube", sau Cloudflare), route: `/suong3x`.

Module độc lập: list phân trang, tìm kiếm, danh mục (category), thẻ (tag), sắp xếp
(Mới nhất / Mới sửa / A→Z / Z→A), popup long-press (danh mục + thẻ + video liên quan).

## API (đo 2026-11-05)

| Việc | Endpoint |
|------|----------|
| Danh sách | `GET /` (40 card/trang), phân trang `/page/{n}/` (tối đa 380 trang) |
| Tìm kiếm | `GET /?s={từ khoá}` |
| Danh mục | `GET /{slug}/` |
| Thẻ | `GET /tag/{slug}/` |
| Taxonomy (menu) | `GET /wp-json/wp/v2/categories`, `/wp-json/wp/v2/tags?orderby=count&order=desc` |
| Sort thật | `GET /wp-json/wp/v2/posts?orderby=modified` hoặc `orderby=title&order=asc|desc` |

**Site KHÔNG có sort phía server**: `/?orderby=views` và `/?orderby=rand` trả y hệt trang
gốc (thứ tự không đổi) → sort đi qua **WP REST mở** (`orderby=modified|title`).
REST `orderby=rand` và `orderby=meta_value_num` (views) → **HTTP 400**, không dùng.

REST `per_page` tối đa 100; module dùng `per_page=20`. REST **không trả duration** →
item từ REST có `time = null` (item từ HTML có `02:00`).

Menu danh mục/thẻ lấy động từ WP REST (cache 12h), bỏ category `uncategorized`.
Thẻ lấy **top 100** theo `count`.

## Nguồn phát

Trang detail có `<meta itemprop="embedURL" content="https://play2.cdn-xvideos-xnxx.xyz/embed4.php?id1=...">`.
Trang embed (JWPlayer) **bắt buộc có `Referer` = site** (không có → Cloudflare **403**),
trong đó:

```
sources: [{"file":"https://sv1.cdn-78-vid.top/<id>/<tok>.m3u8","label":"0","type":"hls"}]
```

Segment của CDN có **prefix PNG** (kiểu `IMG_xxx_0000.png`) → hls.js tự backtrack về
điểm đồng bộ nên vẫn phát (đã verify bằng `play.sh`).

`/suong3x/vidosik` trả map `{label: url}` (app đọc label để chọn chất lượng),
`/suong3x/video?uri=&q=` redirect qua `HostStreamProxy` (giữ Referer/Origin cho CDN).

## Popup long-press

`GET /suong3x/genres?uri={url detail}` → `{items:[], groups:[...]}`:

- `Danh mục` — chip `.btn.btn-black` → `?c=cat/<slug>`
- `Thẻ` — chip `.btn.btn-grey` (link `/tag/<slug>/`) → `?c=tag/<slug>`
- `Liên quan` — 1 item `plain` → `?rel=<url detail>` (mở danh sách "Phim Liên Quan")

Block "Phim Liên Quan" trên trang detail: `<div class="related-videos">` → 10 card.
Chip + liên quan lấy cùng **1 request detail** (cache RAM 6h); cache player 30 phút.
