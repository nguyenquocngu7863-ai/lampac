# VNsexTop1 (vnsextop1.com)

Module Adult route `vnsextop1`. Site SSR (Blade) + Alpine cho block "Xem Thêm".

## List
- Card: `<a href="/phim-sex/<slug>" class="item__thumbnail">` + `<img src="/resize/800/..">`.
- Category: `?c=the-loai/<slug>` → `https://vnsextop1.com/the-loai/<slug>?page=N`.
- Search: `?s=<kw>`; home `/?page=N`. Site không trả tổng trang ⇒ `total_pages: 0` (cuộn vô hạn).

## Popup (2026-11-24)

| route | nguồn |
|---|---|
| `/vnsextop1/genres?uri=<detail>` | `Thể loại`: khối `<div class="video__tags">` → `<a href=".../the-loai/<slug>" class="label">` + `<span>TÊN</span>` (12 chip, tên tiếng Việt đúng như site) → `?c=the-loai/<slug>`; `Liên quan`: 1 item `plain: true` → `?rel=<uri>` |
| `/vnsextop1?rel=<detail>` | block "Xem Thêm Phim Sex" render bằng Alpine nên phải gọi `GET /ajax/suggestions/<slug>` (JSON `{"data":[{url,title,thumbnail_file_url}]}`) → 20 item, `total_pages: 1` |

- `slug` lấy từ path detail `/phim-sex/([a-z0-9\-]+)`.
- Cache: `vnsextop1:info:{uri}` 360 phút, `vnsextop1:rel:{slug}` 20 phút.
- `NormalizePageUrl` chỉ nhận host `vnsextop1.com` (chống SSRF); uri lạ → popup rỗng 200 / `{tp:1, list:[]}`.

## Đã đo (2026-11-24)

- home 20 item; `pg=2` 20; `search=jav` 20; `c=the-loai/doggy` 20 (site không có `pages` ⇒ tp 0)
- `genres?uri=` → **Thể loại 12** (Âu - Mỹ / Gái Xinh / Loạn Luân / Vú To / …) + **Liên quan 1**
- `?rel=` **20 item** · `tp=1`; `pg=2` rỗng; `rel=evil.example` rỗng; `genres?uri=evil` rỗng
- `vidosik` → nhiều server (`StreamQQ Plan VIP`, alias HLS…); `play.sh` → **PASS**: h264 640x360 · 661.8s, segment 2/2, decode 5s đầu **và** cuối OK, cold 0.11s / warm 0.09s

## Cache & Build
- Compile runtime từ `module/Adult/VNsexTop1/` + restart, không cần `dotnet build`.
