# Experiments — plugin thử nghiệm

Thư mục chứa các plugin JS thử nghiệm, **không** được nhét vào root release hay
danh sách sync của `setup-termux.sh`. Nạp trực tiếp qua CDN jsDelivr khi muốn thử.

## 85po.js (ĐÃ GỠ — thử nghiệm thất bại)

Plugin lấy phim từ `85po.com` đã bị gỡ sau 2 vòng thử nghiệm. Lý do kỹ thuật
(ghi lại để không lặp lại):

- Link video `get_file` của 85po **khóa theo IP người xin link** — link sinh
  cho IP nào thì chỉ IP đó phát được.
- Vòng 1: tải HTML qua proxy công cộng (cors.eu.org) rồi phát thẳng →
  "no supported source" vì link thuộc IP của proxy.
- Vòng 2: tải HTML qua `/corseu` + phát qua `/media` của chính server Lampac
  (cùng một IP) → server nhận đúng request nhưng vẫn không phát được trên
  thiết bị thật.
- Kết luận: muốn hỗ trợ 85po phải viết module SISI C# phía server (bóc link
  và stream trong cùng một tiến trình, kèm ProxyLink). Không làm bằng JS
  thuần phía client.

## sisi-restyle.js (ĐÃ TỐT NGHIỆP → tích hợp chính thức)

Đã chuyển thành plugin tích hợp sẵn tại `SISI/plugins/sisi-restyle.js`:

- Server serve tại `/sisi-restyle.js` (route trong `SISI/SisiApi.cs`)
- Tự đăng ký vào Lampa khi `LampaWeb.initPlugins.sisi` bật
  (`Modules/LampaWeb/Controllers/ApiController.cs`)
- `setup-termux.sh --sync` / `--sync-all` tự cập nhật file lên máy

Ai đã cài bản jsDelivr thủ công thì nên gỡ link jsDelivr trong
Cài đặt → Tiện ích mở rộng để tránh chạy hai bản trùng nhau.
