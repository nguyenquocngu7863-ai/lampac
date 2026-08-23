# NguonC

Nguồn phim Việt Nam dùng API JSON của phim.nguonc.com.

## API sử dụng

- Tìm kiếm: `https://phim.nguonc.com/api/films/search?keyword={keyword}`
- Chi tiết phim và tập: `https://phim.nguonc.com/api/film/{slug}`
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
| `/lite/nguonc` | Tìm kiếm, chi tiết, server, mùa và tập |
| `/lite/nguonc-search` | Global search |
| `/lite/nguonc/video` | Proxy link HLS thành link phát cho Lampa |

## Cấu hình

Module mặc định dùng:

```json
{
  "NguonC": {
    "enable": true,
    "host": "https://phim.nguonc.com",
    "streamproxy": true
  }
}
```

Có thể ghi đè `host`, `apihost`, header hoặc `streamproxy` trong `init.conf` theo cơ chế cấu hình module của Lampac.
