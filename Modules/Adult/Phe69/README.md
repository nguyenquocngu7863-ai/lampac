# Phe69

Module Adult cho phe69.shop (WordPress, phim Việt): list/search/category +
phân trang đầy đủ, resolve ra mp4 trực tiếp, mỗi clip 1 chất lượng 360p.

## Luồng

- List: `article` > `a[href][title]` + `img.video-main-thumb`. Home + category
  `/{cat}/` phân trang `/page/N/` (212 trang), search `?s={kw}`.
- Video: trang post → `iframe play.phe69.shop/data/{id}` → JS packer Dean
  Edwards (JWPlayer setup, `sources[]` mp4 trên `phim.phe69.uk`).
- C# có `UnpackDeanEdwards` (base62) nhưng thực tế server-side unpack luôn
  rỗng (play host chặn TLS .NET / CF) → fallback đoán URL từ slug post:
  `https://phim.phe69.uk/{post-slug}.mp4` (verify 4/4 clip, range 206).
- Referer bắt buộc `https://phe69.shop/` cho cả list lẫn stream.

## Route

- `GET phe69` — list/search (`c`, `pg`).
- `GET phe69/vidosik?uri=` — resolve ra `{label: video.m3u8-url}`.
- `GET phe69/video(.m3u8)?uri=&q=` — 302 qua proxy (giữ đuôi `.m3u8` cho hls.js).

## Bài học

- Thiếu `using System;` là `CS0103 Console` crash vòng startup (watchdog
  dừng server sau 3 lần) — Controller clone từ SexViet100 không có using này.
