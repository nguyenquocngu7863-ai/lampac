# VietSexBlog — https://x.vietsex.blog

Module SISI phim Việt Nam. Route `vietsexblog`, displayindex 10.

## Luồng resolve

- List: `/danh-sach/phim-moi`, `/the-loai/{slug}`, `/quoc-gia/{slug}`,
  search `/?search={kw}`. Item `a.m-block.movie-item` (href + title),
  poster `data-original` (relative), nhãn `div.label`.
- Trang video `/phim/{slug}`: server `data-link` + `data-type`
  (vd `/storage/m3u8/{slug}/index.m3u8`). Một phim thường chỉ 1 server.
- Phát qua route `video.m3u8` của module (302 sang `/proxy/`) để app
  dùng hls.js. Bookmark lưu full href `/phim/{slug}` cho bền.

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
- Sửa code xong verify 4 chặng: list → vidosik → video.m3u8 (302) →
  `/proxy/` playlist → segment đầu `47 40...` + `video/mp2t`.
