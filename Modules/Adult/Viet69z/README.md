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

## Cache & Build
- `hybridCache` resolve 20p. Module compile runtime từ `module/Adult/Viet69z/` + restart.
