# VsMov

Nguồn phim Việt Nam dùng API JSON miễn phí của vsmov.com. Kho phim lớn với CDN tốc độ cao.

## API sử dụng

- Tìm kiếm: `https://vsmov.com/api/tim-kiem?keyword=...&limit=...`
- Chi tiết phim và tập: `https://vsmov.com/api/phim/{slug}`
- Phim mới: `https://vsmov.com/api/danh-sach/phim-moi-cap-nhat?page=1`
- Link phát ưu tiên: `link_m3u8`
- Fallback: lấy `url` trong `link_embed` nếu đó là link HLS

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
| `/lite/vsmov/video` | Proxy link HLS thành link phát cho Lampa |

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
