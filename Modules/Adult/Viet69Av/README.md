# Viet69Av

Module SISI theme "Viet69" — **domain đang dùng: `clipviet69s.net`** (2026-11-25).

`viet69av.com` vẫn mở được bằng Chrome thật nhưng **TLS bị chặn với curl_cffi/.NET**
(`SSL_connect: Connection closed abruptly`), nên module không fetch được → 503.
Trang đó thực chất phục vụ từ `clipviet69s.net` (mọi asset/`/api/player` đều trỏ sang),
cùng theme `item-movie` / `/trang/N/`. Đổi domain = sửa `SiteHost` (Service.cs),
`ModInit.cs`, `manifest.json`.

## Endpoint

```
/                     trang chủ (mới nhất)
/<the-loai>/          phim-sex-viet-nam, phim-sex-nhat-ban, …
/tag/<slug>/          thẻ
/search/<slug>/       tìm kiếm (slug đã bỏ dấu)
/trang/<N>/           phân trang (Pages() đọc từ khối /trang/<N>/)
```

## Playlist

Card: `<article class="item-movie">` → `<a title="Tên" href="https://…/slug.html">`.
**Bắt buộc có `title=` trên thẻ `<a>`** — banner quảng cáo cũng là `<a href="…/register.html">`
nhưng không có title; trước 2026-11-25 lọt 1 item rác "Chat Telegram" vào list.

## Stream

Detail: `<div id="video" data-video-id="…">` + `<li class="video-server" data-server-id="1">`
→ `POST {origin}/api/player` (`id`, `server`) → `data.sources[].file` (HLS).
Origin lấy từ chính URL detail (site xoay domain), không hardcode.

## Popup nhấn giữ (2026-11-25)

- `GET /viet69av/genres?uri=<detail>` → `{items:[],groups:[…]}`, không bao giờ 500.
- **Thẻ** (11): khối `<div id="extras">` của detail → `<a href="…/tag/<slug>/" title="…">`.
  Mở bằng `?c=tag/<slug>` (bỏ origin vì `Uri()` ghép `host + "/" + c`).
- **Liên quan**: heading `<h1>Phim Sex Liên Quan</h1>` → 16 item, `?rel=<uri>` trả `total_pages: 1`.
- Thể loại (`phim-sex-*`) chỉ nằm ở footer "Chuyên Mục Sex" (giống nhau mọi phim) → không đưa vào chip.
- Cache chip+rel trong RAM 6h theo pageUrl.

## Kiểm chứng (2026-11-25, localhost 9118)

```
GET /viet69av                     -> 28 item, tp=133, 0 item rác
GET /viet69av?pg=2                -> trang 2 khác trang 1
GET /viet69av/genres?uri=<detail> -> Thẻ 11 · Liên quan 1
GET /viet69av?c=tag/phevkl        -> 29 item
GET /viet69av?rel=<detail>        -> 16 item, tp=1
GET /viet69av/vidosik?uri=<detail>-> {"qualitys":{"auto":"/proxy/….m3u8"}}
play.sh                           -> PASS h264 1920x1080 138.9s
```
