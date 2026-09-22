# Javtiful (javtiful.com)

Module Adult route `javtiful`, displayindex 19. Nhóm SISI
chuẩn static-parse (khung Vlxx): `httpHydra` + regex,
**không Playwright, không hook global**.

## Vì sao nhóm chuẩn ăn ngay

- curl thường đã 200 (0.5-1.5s), không CF challenge.
- HTML render sẵn server, tile tĩnh trong
  `<article class="front-video-card">`.
- Stream là MP4 direct trong `<source>`, không unpack.

## URLs (prefix `/vn` = tiêu đề Việt)

- Home: `https://javtiful.com/vn` — **không phân trang**
  (`?page=` bị bỏ qua, trang 2 trùng trang 1) → home mặc
  định dùng feed `/vn/foryou` (23/trang, overlap 0).
- Section listing (có nút Xem thêm, đều `?page=N`):
  `/vn/foryou`, `/vn/censored`, `/vn/uncensored`,
  `/vn/reducing-mosaic`.
- Category: `/vn/category/{slug}?page=N` (+ `sort=popular`,
  `added_today/week/month`, `video_type`).
- Search: `GET /vn/search?q={slug}` — slug lowercase
  (query HOA từng trả 502 một lần).
- Video: `/vn/video/{id}/{slug}`.
- Menu: Tìm kiếm + Mới nhất + Có che / Không che /
  Giảm mosaic + Thể loại (21 genre: affair, amateur,
  big-tits, cosplay...).

## Tile parse

- Trong mỗi article: thumb
  `<a href="/vn/video/..." class="front-video-thumb">`,
  poster `data-front-lazy-src` (relative → prefix host),
  duration `front-duration-tag`, title `front-video-title`.
- Preview mp4 (`data-front-video-preview-src`) map vào
  `PlaylistItem.preview`.
- Bookmark site=`javtiful`.

## Player

- `<source src="https://fast-stream.jav.si/p/...">` =
  **MP4 direct** (HEAD: 200, HTTP/2, hỗ trợ Range).
- 1 quality duy nhất → dict `{MP4: ...}` →
  `/javtiful/video` → `HostStreamProxy` kèm referer javtiful.
- Cover video: `poster` của thẻ `<video>` (relative).

## Cache & Build

- List cache 10p, resolve cache 20p (hybridCache).
- Copy `.cs` vào `/root/lampac/module/Adult/Javtiful/`
  + restart, không cần `dotnet build`.
- Verify: `/javtiful` ~39 items, `javtiful/vidosik?uri=`
  ra MP4, `/javtiful/video` 200 OK, foryou p1/p2 overlap 0.
