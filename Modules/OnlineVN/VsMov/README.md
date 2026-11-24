# VsMov

Nguồn phim Việt Nam dùng API JSON miễn phí của vsmov.com. Kho phim lớn với CDN tốc độ cao.

## API sử dụng

- Tìm kiếm: `https://vsmov.com/api/tim-kiem?keyword=...&limit=...`
- Chi tiết phim và tập: `https://vsmov.com/api/phim/{slug}`
- Phim mới: `https://vsmov.com/api/danh-sach/phim-moi-cap-nhat?page=1`
- Link phát ưu tiên: `link_m3u8`
- Fallback: lấy `url` trong `link_embed` nếu đó là link HLS

## Stream (đã đo 2026-11-24)

- Embed `https://v{N}.streamvsmov.com/video/{hash}` → `https://v{N}.streamvsmov.com/stream/{hash}/master.m3u8`.
  **Host đổi theo từng tập** (v1/v8/v9/v15…): phải lấy `Uri.Host` của `link_embed`, KHÔNG hardcode
  (bản cũ hardcode `v1` → Cloudflare trả 403 → playlist 503).
- Playlist là media playlist thuần (`#EXTINF` + URL `.png`), 348–597 segment.
- Segment là **polyglot**: header PNG ở đầu (321–500 byte) rồi mới tới gói MPEG-TS
  (`0x47` sync, kiểm 4 gói cách nhau 188 byte). Route `segment.ts` tải bytes và cắt phần
  header, trả `video/mp2t` (offset thay đổi từng segment nên phải dò runtime, không cắt cứng).
- URL play trả kèm đuôi `.m3u8` (`/lite/vsmov/video.m3u8`) để app chọn đúng player HLS.
- Smoke `play.sh`: PASS (~90%) — h264 1920x1080, decode 5s đầu/cuối, 348/348 segment qua Lampac.

## Tính năng

- Tìm kiếm theo tên Việt hoặc tên gốc.
- Phim lẻ và phim bộ.
- Chọn server/phiên bản (Vietsub, Thuyết Minh...).
- Chọn mùa và tập khi API cung cấp thông tin mùa.
- Chuyển link HLS qua `HostStreamProxy` của Lampac.
- Có endpoint search riêng cho global search của Lampa.

## Routes

| Route | Mục đích |
| --- | --- |
| `/lite/vsmov` | Tìm kiếm, chi tiết, server, mùa và tập |
| `/lite/vsmov-search` | Global search |
| `/lite/vsmov/video.m3u8` | Link phát (JSON `play` cho Lampa, `play=true` → redirect playlist) |
| `/lite/vsmov/playlist.m3u8` | Playlist nội bộ, rewrite mọi segment sang `/lite/vsmov/segment.ts` |
| `/lite/vsmov/segment.ts` | Proxy segment, cắt header PNG của TS polyglot |

## Cấu hình

Module mặc định dùng:

```json
{
  "VsMov": {
    "enable": true,
    "host": "https://vsmov.com",
    "streamproxy": true
  }
}
```

Có thể ghi đè `host`, `apihost`, header hoặc `streamproxy` trong `init.conf` theo cơ chế cấu hình module của Lampac.
