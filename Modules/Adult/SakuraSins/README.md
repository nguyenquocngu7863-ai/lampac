# SakuraSins

Module Lampac cho `https://sakurasins.com` (JAV/Asian uncensored).

## Route

| route | việc |
|---|---|
| `/{module}` | list (home / category / search / studio / performer / collection) |
| `/{module}/vidosik?uri=<detail>` | liệt kê link phát (HLS) |
| `/{module}/video?uri=<detail>&q=HLS` | resolve + proxy m3u8 qua Lampac |
| `/{module}/video.m3u8?uri=<detail>&q=HLS` | như trên — **link vidosik trả về luôn có đuôi `.m3u8`** để app nhận là HLS |
| `/{module}/genres?uri=<detail>` | popup long-press (Danh mục / Studio / Liên quan) |

## Nguồn dữ liệu (đo 2026-11-06)

- **Feed** (home/category/search) infinite scroll:
  `GET /api/videos.php?sort=&cat=&q=&page=N` → JSON `{ok, html, count, has_more, next_page}`.
  `html` là fragment `<article class="vcard">` giống trang server-side.
- **Studio/Performer**: trang server-side `/{kind}/<slug>?page=N`, tổng trang đọc từ
  `pager__cur">Page X of N`.
- **Collection**: 1 trang, không phân trang.
- **Danh mục**: `/categories` → 24 mục (`<a class="catcard" href="/?cat=slug">`).
- **Studio**: `/studios` (~498, cap 300) · **Diễn viên**: `/performers` (~630, cap 300)
  · **Bộ sưu tập**: `/collections` (3).
- **Sort**: `recent` (mặc định), `views`, `rating`, `longest` — đo tại home và category
  (`?sort=views` đổi thứ tự). Search/studio/performer không xác nhận sort → ẩn dòng 2.

## Player

1. Trang detail có `data-mid="<id>"` trên `#player-wrap`.
2. `GET /ph/extract.php?v=<id>&retries=5` (Referer = trang detail) → `{session, validto, formats}`.
3. Master `GET /ph/stream/<session>/playlist.m3u8` → 3 variant `quality/240|480|720.m3u8`
   → segment tương đối `../seg-N.ts?q=<height>`.
4. Trả nguyên master cho `HostStreamProxy` (hls.js tự ABR). Session sống ~1h,
   cache resolve 20 phút.

Đã verify bằng `play.sh`: PASS (~90%) — ffmpeg decode 5s đầu + 5s cuối, segment qua `/proxy/`.
