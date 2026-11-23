# Phe69 — https://phe69.shop

Module SISI cho Phe69 (phim sex Việt Nam). WordPress + Cloudflare nhẹ.

## Route

| Route | Việc |
|---|---|
| `GET /phe69` | danh sách (20 item/trang, `tp=112`) |
| `GET /phe69?pg=N` | trang N |
| `GET /phe69?c=<slug>` | category (`sex-nhat-ban`, `gai-dam`, …) hoặc `tag/<slug>` |
| `GET /phe69?search=<kw>` | `/?s=<kw>` (form site: `name="s"`) |
| `GET /phe69/genres?uri=<detail>` | popup long-press |
| `GET /phe69?rel=<detail>` | "Video liên quan" (`total_pages: 1`, `pg=2` rỗng) |
| `GET /phe69/vidosik?uri=` | `{"Auto": url}` |
| `GET /phe69/video.m3u8?uri=&q=Auto` | 302 → `/proxy/<token>.m3u8` |

## Home dùng category thay vì root (sửa 2026-11-23)

`https://phe69.shop/` và `/page/N/` trả về challenge cho **http client của app** → `e.Fail`
→ home/pg2 503, trong khi `curl_cffi` (site.sh) lấy được 200/30 article.

Category `/phim-sex-moi/` và `/phim-sex-moi/page/N/` chạy bình thường trong app
(đã so sánh: cùng parser, 20 item/trang). Vì vậy `Uri()` mặc định (không `search`, không `c`)
phát `/phim-sex-moi/`, phân trang nối `page/N/`.

**Cold-start**: request **đầu tiên ngay sau restart** đôi khi vẫn trả danh sách rỗng rồi tự hết
(đã thử retry 800ms trong lambda nhưng không hẳn hết). Gọi lại lần 2 là có 20 item. Không phải lỗi site.

## Sort — SITE KHÔNG CÓ

Quét `<select>` / `sort=` / `orderby` / `sap-xep` trên home, category, trang 2 và chi tiết →
**0 kết quả**. Trang chi tiết có 1 `<select>` nhưng là chọn player, không phải sort danh sách.
Không hiện dòng "Sắp xếp".

## Popup long-press

- **Thể loại** — lấy từ **breadcrumb** `<div class="breadcrumbs">`, bỏ link "Trang chủ".
  Không lấy từ nav (nav chứa toàn bộ category).
- **Thẻ** — 15 link `<a href=".../tag/<slug>/">`, khớp với 15 `<meta property="article:tag">`.
- **Diễn viên** — site không có nhóm riêng (tên diễn viên nằm trong tag, ví dụ `Rikako Katayama`)
  ⇒ nhóm bị bỏ.
- **Liên quan** — cắt từ `<div class="under-video-block"><h2>Video liên quan</h2>` tới
  `div.show-more-related`. Cắt từ đầu trang sẽ lẫn article của nav/category (đã dính:
  item đầu trả "Phim Sex Việt Nam").

Cache chip+rel trong `Dictionary` tĩnh (TTL 6h, cap 300) — `hybridCache` không round-trip
được `ValueTuple`.

## Player — site ĐỔI CƠ CHẾ (2026-11-23)

Cũ: iframe `https://play.phe69.shop/data/<token>` chứa packer Dean Edwards `a=62` →
giải mã ra `.mp4`.

Mới: trang `/data/<token>` gắn thẳng
```
const hlsSource = "https://hlshd.phe69.shop/hls_videos/<slug>/index.m3u8";
```
(đã đối chiếu 2 phim). `PlayerSources()` đọc `hlsSource` trước, fallback packer cũ sau.

Thêm vào đó `httpHydra` đôi khi nhận bản HTML chi tiết **không có iframe** (khác curl_cffi)
→ `GetIframe()` null. Fallback: suy URL HLS từ slug (`hlshd.../hls_videos/<slug>/index.m3u8`).
Fallback mp4 cũ `phim.phe69.uk/{slug}.mp4` giờ chỉ để tham khảo, không còn dùng.

**`vidosik` phải trả URL có đuôi `.m3u8`** — route `/phe69/video` trả body HLS nhưng Lampa chọn
player theo đuôi URL → "no supported source". Module đã có sẵn `[Route("phe69/video.m3u8")]`,
chỉ cần `vidosik` trỏ tới đó.

SSRF: `SafeDetailUrl()` chỉ nhận host `phe69.shop` + path không phải root/`page/`; popup trả
shape rỗng HTTP 200 thay vì lỗi.

## Verify 2026-11-23 (app thật, `127.0.0.1:9118`)

| Endpoint | Kết quả |
|---|---|
| `/phe69` (lần 2+) | 20 item, `tp=112` |
| `?pg=2` / `?pg=3` | 20 item, khác trang 1 |
| `?c=gai-dam` | 20 item, `tp=2` |
| `?c=sex-nhat-ban` | 20 item, `tp=3` |
| `?c=tag/bj` / `tag/doggy` | 20 item, `tp=60` / `tp=7` |
| `?search=sex` | 20 item, `tp=58` |
| `genres` | Thể loại 1 · Thẻ 15 · Liên quan 1 |
| `?rel=` | 8 item, `tp=1` |
| `?rel=&pg=2` | rỗng, `tp=1` ✔ |
| SSRF (host lạ / uri rác) | shape rỗng HTTP 200 ✔ |
| `play.sh` | **PASS** — `hls=true`, h264 1280x720, 855.9s, decode 5s đầu/cuối ✔ |

- `check.sh`: **0 error** (29 warning, nullable)
- `Pages()` đọc `/page/N/` → `tp` đúng (trước đây `PlaylistResult` không truyền total_pages)

## Bài học cũ (vẫn còn giá trị)

- Thiếu `using System;` là `CS0103 Console` crash vòng startup — Controller clone từ
  SexViet100 không có using này.
- Referer bắt buộc `https://phe69.shop/` cho list lẫn stream.
