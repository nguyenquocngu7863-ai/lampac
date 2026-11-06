# VietSexBlog — https://vietsexhd.blog

Module SISI phim Việt Nam. Route `vietsexblog`, displayindex 10.

## Luồng resolve

- List: `/danh-sach/phim-moi`, `/the-loai/{slug}`, `/quoc-gia/{slug}`,
  search `/?search={kw}`. Item `a.m-block.movie-item` (href + title),
  poster lấy từ `src` của `<img>` (site bỏ `data-original` từ 2026-11;
  code thử lần lượt `data-original` → `data-src` → `src`), nhãn `div.label`.
- Trang video `/phim/{slug}`: server `data-link` + `data-type`.
  - Đa số: `data-type="m3u8"`, link `/storage/m3u8/{slug}/index.m3u8` →
    phát qua route `video.m3u8` (302 sang `/proxy/`) để app dùng hls.js.
  - Một số phim (vd blogger): `data-type="embed"`, link
    `blogger.com/video.g?token=...` — không phải file phát được.
    `ResolveBloggerAsync` GET trang embed (HTML đã chứa URL `googlevideo`
    escape u003d / u0026), `ExtractMp4s` bóc các `itag` (360p/720p/MP4),
    fallback Playwright nếu GET không ra. Phát qua route `video.mp4`.
- Bookmark lưu full href `/phim/{slug}` cho bền.

## Menu

Không hardcode danh mục: `Taxonomies()` parse `div.main-menu > nav.menu`
từ chính HTML trang list (không tốn thêm request) rồi tách nhóm theo tiền tố
path — `/the-loai/*` → "Thể loại" (21), `/quoc-gia/*` → "Quốc gia" (4).
Slug giữ nguyên tiền tố (`the-loai/jav`) vì `c=` nhận cả path.
Chỉ khi parse hỏng mới rơi về `FallbackGroups` (cũng dùng slug thật).

## Pagination

Tất cả tuyến (home, thể loại, quốc gia, search) đều `?page=N`,
mỗi trang 24 item chính (`m-block movie-item`), overlap 0.
10 item trùng giữa các trang là sidebar top-film-week, bỏ qua.

## Segment PNG bọc TS

Segment (tiktokcdn/nidplay) trả PNG 512x512 1-bit, bên trong từ offset
~214 là TS thật (PAT `47 40 11 10`, sync `0x47` mỗi 188 byte, có NAL H264).
`ProxyApiOverride` trong ModInit lột tới sync-byte đầu rồi trả
`video/mp2t` (lọc theo host tiktokcdn.com/nidplay.blog). Giống SexViet100.

## Lưu ý

- Clip đầu trang chủ đã hỏng (CDN 403 `domain forbidden` cả playlist lẫn
  segment) — verify bằng clip thứ 2 trở đi, đừng kết luận cả nguồn chết
  từ 1 clip.
- Playlist tải 200 không có nghĩa segment sống — check segment riêng.

## Cache & Build

- Không cần build DLL: copy 4 file (.cs + manifest) vào
  `/root/lampac/module/Adult/VietSexBlog/` rồi restart là chạy.
- Sửa code xong verify: list (ảnh) → menu → vidosik (m3u8 + blogger→mp4) →
  `video.m3u8`/`video.mp4` (302) → `/proxy/` trả `200 video/mp4`.
