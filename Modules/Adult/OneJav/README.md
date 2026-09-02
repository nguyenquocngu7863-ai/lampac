# OneJAV (SISI)

Nguồn **JAV (18+)** lấy danh sách/metadata từ [onejav.com](https://onejav.com) và **phát qua TorrServer**, tích hợp thẳng vào trình duyệt online của lampac (SISI) — không cần cài plugin Lampa riêng. Tham khảo logic: [nguyenquocngu93/javfast](https://github.com/nguyenquocngu93/javfast).

## Hiển thị trong Lampa

Module đăng ký `IModuleSisi` → tự xuất hiện cùng các nguồn 18+ khác (Eporner, PornHub…). Người dùng chọn nguồn **OneJAV 🎌**, có tìm kiếm theo mã, vài tag phổ biến (Uncensored, Big Tits, Creampie…).

## Luồng hoạt động

| Route | Vai trò |
|------|---------|
| `GET /oj` | Danh sách (`search` = tìm, `c` = tag, `pg` = trang). Parse card onejav.com. |
| `GET /oj/view?uri=CODE` | Trang mã: lấy magnet từ trang onejav; nếu trống thì tìm thêm **Sukebei** (nyaa) và **ijavtorrent.com**. Trả về `qualitys` — mỗi nguồn là một "chất lượng". |
| `GET /oj/play?hash=…&magnet=…` | `add` torrent vào TorrServer, hỏi `stat` để chọn **file video lớn nhất** (bỏ sample/trailer), rồi redirect sang proxy `/ts/stream` của lampac. |

## TorrServer

- Mặc định dùng **TorrServer tích hợp** (module TorrServer, cổng 9085) — không cần cấu hình.
- Lưu ý: cần bật module **TorrServer** để có endpoint `/ts/...`; nếu chưa có TorrServer, route `/oj/play` trả 503.

## Cấu hình (init.conf)

```jsonc
"OneJav": {
  "enable": true,
  "host": "https://onejav.com",
  "useproxy": false        // bật nếu onejav bị chặn theo mạng của server
}
```
