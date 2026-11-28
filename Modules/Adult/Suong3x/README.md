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

### Sửa 2026-11-24 (2 lỗi làm app KHÔNG phát)

1. **Route thiếu đuôi** — app chọn player theo **đuôi URL**; `vidosik` trả
   `/suong3x/video?...` (không đuôi) nên bị coi là file thường → `no supported source`.
   Nay có `[Route("suong3x/video.m3u8")]` / `[Route("suong3x/video.mp4")]`
   (giữ route cũ `suong3x/video` cho client đã cache).
2. **Embed trả MP4, không chỉ HLS** — một số video dùng CDN xvideos:
   `"file":"https://mp4-cdn77.xvideos-cdn.com/.../video_240p.mp4?secure=..."`.
   `EndsWith(".mp4")` trên URL còn query → **false** ⇒ `stream_links`.
   Nay `StreamFromEmbed` cắt query trước khi kiểm đuôi, và `Vidosik` trả nhãn
   `HLS`/`MP4` + route tương ứng theo đuôi thật của file.

## Đã đo (2026-11-24)

- home 40 item `tp=382` · `pg=2` 40 · `sort=title_asc` 20 (tp 764 — cùng tổng 15280 item)
- `search=em` 40 `tp=146` · `c=cat/clip-sex-telegram` 40 `tp=84` · `c=tag/chongtoico` 40 `tp=23`
- `genres?uri=` → **Danh mục 3** + **Thẻ 16** + **Liên quan 1**; `?rel=` 10 item `tp=1`
- 12/12 video (home + pg2 + pg3) resolve được: 8 **HLS** + 4 **MP4**
- `play.sh`: HLS **PASS** (h264 720x1280 · 339.6s, segment 41/41 đuôi `.png` ngụy trang,
  decode 5s đầu + cuối) · MP4 **PASS** (h264 444x250 · 979.4s và 3959.3s, decode OK)

## Popup long-press

`GET /suong3x/genres?uri={url detail}` → `{items:[], groups:[...]}`:

- `Danh mục` — chip `.btn.btn-black` → `?c=cat/<slug>`
- `Thẻ` — chip `.btn.btn-grey` (link `/tag/<slug>/`) → `?c=tag/<slug>`
- `Liên quan` — 1 item `plain` → `?rel=<url detail>` (mở danh sách "Phim Liên Quan")

Block "Phim Liên Quan" trên trang detail: `<div class="related-videos">` → 10 card.
Chip + liên quan lấy cùng **1 request detail** (cache RAM 6h); cache player 30 phút.

## Lọc "Liên quan" chết (2026-11-28)

Bẫy: bài **cũ** trên site trỏ `itemprop="embedURL"` sang
`https://xcdnx.cdn-xvideos-xnxx.xyz/embed.php?url=https://www.xvideos.com/embedframe/<id>`.
Trang embed đó trả `sources:[{"file":""}]` (rỗng) vì video gốc trên xvideos đã bị xoá (embedframe 404)
→ bấm "Tiếp theo" chỉ ra `stream_links` (503), player đứng im = "item ảo".
Bài **mới** dùng `play2.cdn-xvideos-xnxx.xyz/embed4.php?id1=...` (chạy tốt); dạng
`embed.php?url=<id số>` vẫn trả `"file":"https://mp4-gcore...mp4?secure=.."`.

Cách chữa: `RecomendsAsync` → `PlayableAsync` kiểm từng item qua `M3u8Async` (cache 30 phút),
4 item/lượt, kết quả (kể cả false) cache RAM `_aliveCache` 6 giờ; item chết bị bỏ khỏi
`recomends` và `?rel=`. Đo: 10 → **5 item**, 5/5 trả `qualitys`, `?rel=` 5 `tp=1`,
cold 2.6s → warm 0.002s, `play.sh` MP4 **PASS** (h264 444x250 · 979.4s).
