# OneJAV

Nguồn **JAV (18+)**: duyệt/tìm theo [onejav.com](https://onejav.com), **chọn torrent** rồi phát qua **TorrServer ngoài** (mặc định `http://gren439e.tsarea.tv:8880`) — không qua gst/proxy nội bộ. Tham khảo logic: [nguyenquocngu93/javfast](https://github.com/nguyenquocngu93/javfast).

## Cài

1. Đặt thư mục module vào `module/Adult/OneJav` (gồm `Controller.cs, Service.cs, ModInit.cs, OneJavConf.cs, manifest.json, plugin.js`).
2. Cài plugin Lampa **một lần**: Tiện ích → thêm URL `http://<host-lampac>/onejav.js`.
3. Menu chính của Lampa có **🎌 OneJAV**.

## Sử dụng

- Vào OneJAV → **Mới nhất**, hoặc 🔍 tìm theo mã (vd `SSIS-123`).
- Mở một mã → màn hình **danh sách torrent** (xếp theo seed): OneJAV `.torrent`, **Sukebei** (kèm seed), **ijavtorrent**.
- Chọn torrent → module `add` vào TorrServer ngoài, chọn **file video lớn nhất** rồi trả thẳng URL `…/stream?link=…&index=…&play` cho trình phát.

## Cấu hình (init.conf)

```jsonc
"OneJav": {
  "enable": true,
  "host": "https://onejav.com",
  "tsserver": "http://gren439e.tsarea.tv:8880",  // TorrServer ngoài
  "ts_login": "",      // để trống nếu server không bật auth
  "ts_passwd": "",
  "use_sukebei": true,
  "use_ijav": true
}
```

## API

| Route | Vai trò |
|------|---------|
| `GET /onejav.js` | Plugin Lampa (UI riêng: grid + màn chọn torrent). |
| `GET /onejav/list?page=&q=&path=` | Danh sách card (mới nhất/tìm/tag). |
| `GET /onejav/torrents?id=CODE` | Danh sách torrent cho mã (OneJAV .torrent + Sukebei + ijav, kèm seed). |
| `GET /onejav/play?magnet=` hoặc `?link=` | Add vào TorrServer ngoài → trả `{ ok, url }` stream trực tiếp. |
