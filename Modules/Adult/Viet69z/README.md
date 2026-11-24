# Viet69z (viet69z.to)

Module Adult cho nguồn viet69z.to (Việt, WordPress).

## List
- Item: `article.entry-video` > `a[href][title]` + `img[src]` + `h2.entry-title`.
- Home `/`, pagination mọi tuyến `/page/N/` (WordPress).
- Category = tag: `/tag/{slug}/` (sinh-vien, may-bay-ba-gia, teen, check-hang, thu-dam).
- Search `/search/{slug}/` — slugify thường + bỏ dấu Việt (gái xinh → gai-xinh).
- Bookmark bền = full URL video.

## Video
- Trang video có servers `li[data-url]` (base64 của UUID) → `https://emb.cd-vs.com/api/get-video?id={uuid}&counter=0&tried_ids=` → `{"url": "..."}` (blogger m3u8/mp4).
- Trang embed emb.cd-vs.com bị Cloudflare challenge với IP server — gọi API trực tiếp thì 200, không cần qua challenge.

## Pagination
- Mọi tuyến `/page/N/`. Search pg>1 trả rỗng nếu parse 0 item để app dừng (giữ pattern SexViet100).


## Popup (2026-11-24)

| route | nguồn trong HTML |
|---|---|
| `/viet69z/genres?uri=<detail>` | `Thể loại`: class `category-<slug>` trên `<article ... class="... hentry ...">` → permalink `https://viet69z.to/<slug>/` (tên lấy từ anchor text, vd `Sex Việt`); `Thẻ`: `href="/tag/<slug>/"` (15 chip, slug → Title Case); `Liên quan`: 1 item `plain: true` → `?rel=<uri>` |
| `/viet69z?rel=<detail>` | cắt HTML từ heading `Có thể bạn sẽ thích` rồi dùng lại `Playlist()`; bỏ chính bài đang xem → 6 item, `total_pages: 1` |

- Chip dùng lại tham số list sẵn có: `?c=sex-viet` (thể loại) và `?c=tag/ao-dai` (thẻ) — không thêm route list.
- Cache: `viet69z:info:{uri}` 360 phút, `viet69z:rel:{uri}` 20 phút (`InvokeCacheResult`).
- `NormalizePageUrl` chỉ nhận host `viet69z.to` (chống SSRF), uri lạ → `{total_pages:1,list:[]}` hoặc popup rỗng 200.

## Đã đo (2026-11-24)

- home 20 item; `pg=2` 20; `search=sinh vien` 20; `c=sex-viet` 20; `c=tag/ao-dai` 20 (site không trả `pages` → `total_pages: 0`)
- `genres?uri=` → 3 nhóm: **Thể loại 3** (Sex Việt / Sinh Vien / Teen) · **Thẻ 15** · **Liên quan 1**
- `?rel=` **6 item** · `tp=1`; `pg=2` rỗng; `rel=evil.example` rỗng; `genres?uri=evil` rỗng
- `play.sh` → **PASS**: h264 1280x720 · 91s, segment 31/31 (đuôi `.png` ngụy trang), decode 5s đầu **và** cuối OK, cold 0.22s / warm 0.25s

## Cache & Build
- `hybridCache` resolve 20p. Module compile runtime từ `module/Adult/Viet69z/` + restart.
