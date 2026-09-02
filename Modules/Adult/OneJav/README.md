# OneJAV

Nguồn **JAV (18+)** lấy danh sách/metadata từ [onejav.com](https://onejav.com) và **phát qua TorrServer**.
Tham khảo logic: [nguyenquocngu93/javfast](https://github.com/nguyenquocngu93/javfast).

## Hoạt động

1. Lampa gọi `/onejav/list` (mới nhất), `/onejav/search?q=...` → module parse danh sách từ onejav.com.
2. Mở một mã → `/onejav/card?id=...` → module lấy magnet trên trang; nếu không có, tìm thêm **Sukebei** (nyaa) và **ijavtorrent.com**.
3. Chọn phát → `/onejav/play?hash=...&magnet=...` → module `add` torrent vào TorrServer, hỏi `stat` để chọn **file video lớn nhất** (bỏ sample/trailer), rồi chuyển hướng tới `/ts/stream?link=...&index=...&play` (proxy qua lampac).

## Cấu hình (init.conf)

```jsonc
"OneJav": {
  "enable": true,
  "host": "https://onejav.com",
  "torrserver": "",                 // để trống = dùng TorrServer tích hợp; vd "http://192.168.1.10:8090"
  "use_sukebei": true,              // fallback magnet từ sukebei.nyaa.si
  "use_ijav": true                  // fallback magnet từ ijavtorrent.com
}
```

- **TorrServer tích hợp**: bật module **TorrServer** (mặc định cổng 9085) là phát được, không cần cấu hình gì thêm.
- **TorrServer ngoài**: điền `torrserver` (nhớ kèm `http://`).

## Phân phối

- Backend: `/onejav/list`, `/onejav/search`, `/onejav/card`, `/onejav/play`
- Plugin Lampa: `/onejav.js` (tự gắn nút **🎌 OneJAV** vào menu chính).

Cài plugin vào Lampa: thêm extension `http://<host>:<port>/onejav.js` (hoặc để lampac tự nạp nếu có cấu hình customPlugins).
