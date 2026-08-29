# KKPhim

Nguồn phim Việt Nam dùng API JSON của KKPhim/PhimAPI. Đây là **module mới được copy và viết lại từ luồng HDVB**; module `Modules/OnlineRUS/HDVB` gốc không bị thay đổi.

## API sử dụng

- Tìm kiếm: `https://phimapi.com/v1/api/tim-kiem?keyword=...`
- Chi tiết phim và tập: `https://phimapi.com/phim/{slug}`
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
| `/lite/kkphim` | Tìm kiếm, chi tiết, server, mùa và tập |
| `/lite/kkphim-search` | Global search |
| `/lite/kkphim/video` | Proxy link HLS thành link phát cho Lampa |

## Cấu hình

Module mặc định dùng:

```json
{
  "KKPhim": {
    "enable": true,
    "host": "https://phimapi.com",
    "streamproxy": true
  }
}
```

Có thể ghi đè `host`, `apihost`, header hoặc `streamproxy` trong `init.conf` theo cơ chế cấu hình module của Lampac.
