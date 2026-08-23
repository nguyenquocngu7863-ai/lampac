# Lampac NextGen cho Termux (Android)

Bản hướng dẫn này dành cho cách chạy Lampac trên **Android qua Termux**. Script [`setup-termux.sh`](setup-termux.sh) tạo một Ubuntu bằng `proot-distro`, cài .NET 10 và chạy Lampac bên trong Ubuntu đó.

> Đây là cách chạy phù hợp để tự dùng trên điện thoại/TV box Android. Android có thể dừng tiến trình nền để tiết kiệm pin; không nên xem đây là máy chủ luôn hoạt động 24/7.

## Yêu cầu

- Thiết bị Android 64-bit (`arm64` là phổ biến; script cũng hỗ trợ `amd64`).
- Cài **Termux từ F-Droid**: <https://f-droid.org/packages/com.termux/>. Không dùng bản Termux cũ trên Google Play.
- Kết nối Internet ổn định, còn vài GB bộ nhớ trống và pin/sạc đủ trong lần cài đầu.
- Nên tắt tối ưu pin cho Termux nếu muốn server chạy lâu hơn.

## Cài đặt nhanh

Mở Termux, tải script rồi chạy:

```bash
pkg update -y && pkg install -y curl
curl -fLO https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a028cf-lampac/setup-termux.sh
bash setup-termux.sh
```

Sau khi cài xong, script hỏi có khởi động Lampac ngay không. Chọn `Y` hoặc chỉ nhấn Enter để chạy ngay.

### Cài đặt nhưng chưa chạy

```bash
bash setup-termux.sh --install
```

### Đổi port hoặc mật khẩu root ngay từ đầu

```bash
LAMPAC_PORT=8080 LAMPAC_PASSWD='mat-khau-cua-ban' bash setup-termux.sh --install
```

- Port mặc định: `9118`.
- Mật khẩu mặc định: `lampac`. Hãy đổi bằng `LAMPAC_PASSWD` khi cài mới, hoặc sửa file cấu hình/mật khẩu sau khi cài.

## Script làm gì?

Khi chạy lần đầu, `setup-termux.sh` thực hiện tuần tự các bước sau:

1. Cập nhật package của Termux, cài `proot-distro`, `git`, `curl`, `wget`.
2. Cài Ubuntu trong `proot-distro` (hoặc sửa/cài lại Ubuntu nếu môi trường đang hỏng).
3. Trong Ubuntu, cài các thư viện cần thiết, GStreamer và **ASP.NET Core Runtime .NET 10** tại `/opt/dotnet`.
4. Tải bản phát hành Lampac NextGen mới nhất, giải nén vào `/root/lampac` trong Ubuntu.
5. Tạo `init.conf` tối ưu cho Termux: `lowMemoryMode`, GStreamer, Chromium headless và các module nặng được tắt bớt.
6. Cài Chrome/Chromium tương thích `arm64` hoặc `amd64` để các nguồn dùng Playwright hoạt động.
7. Xoá nguồn đã ngừng dùng/lỗi **NguonC**, rồi đồng bộ module tuỳ biến: **KKPhim, K20, VsMov, WebStreamr, GStreamer**; AdminPanel cũng được cập nhật giao diện tiếng Việt nếu module đã có sẵn.
8. Tạo lệnh `lampac` để quản lý server từ Termux.

Lần cài đầu có thể mất vài phút vì phải tải Ubuntu, runtime .NET, Chrome và bản phát hành Lampac. Không đóng Termux khi đang chạy script.

## Quản lý Lampac sau khi cài

```bash
lampac start     # Khởi động; Ctrl+C để dừng khi chạy ở terminal hiện tại
lampac stop      # Dừng tiến trình Lampac
lampac status    # Kiểm tra trạng thái
lampac info      # Hiện URL, port và vị trí config
lampac config    # Mở init.conf bằng nano trong Ubuntu
lampac update    # Cập nhật Lampac và đồng bộ lại thiết lập tuỳ biến
```

Lệnh `lampac start` in ra địa chỉ local, địa chỉ mạng LAN và cổng đang dùng. Thông thường bạn truy cập từ thiết bị khác cùng Wi-Fi qua:

```text
http://IP_CUA_ANDROID:9118
```

Nếu không thấy địa chỉ IP, chạy trong Termux:

```bash
ip addr show wlan0
```

## Cập nhật và đồng bộ module

### Cập nhật đầy đủ

```bash
lampac update
# hoặc
bash setup-termux.sh --update
```

Cập nhật sẽ tải release mới, giữ lại `init.conf` và `passwd`, rồi cài lại Chrome/Chromium, cấu hình Termux và các module tuỳ biến. Thư mục nguồn lỗi cũ `NguonC` cũng bị xoá; `VsMov` được đồng bộ lại.

### Chỉ đồng bộ KKPhim, K20, VsMov, Chromium và GStreamer

Dùng khi release Lampac vẫn giữ nguyên nhưng bạn muốn lấy lại các thay đổi tuỳ biến:

```bash
bash setup-termux.sh --sync
```

### Chỉ chạy server

```bash
bash setup-termux.sh --run
# hoặc
lampac start
```

## Cấu hình Termux

File cấu hình nằm **bên trong Ubuntu proot**:

```text
/root/lampac/init.conf
```

Cách đơn giản nhất để sửa:

```bash
lampac config
```

Cấu hình mặc định của script đã bao gồm:

```jsonc
{
  "listen": {
    "ip": "0.0.0.0",
    "port": 9118,
    "scheme": "http"
  },
  "lowMemoryMode": true,
  "gst": {
    "enable": true,
    "useGpu": false,
    "hardwareAcceleration": false
  },
  "chromium": {
    "enable": true,
    "Headless": true,
    "context": { "keepopen": false, "min": 0, "max": 1 }
  }
}
```

Thiết lập này ưu tiên ổn định và tiết kiệm RAM. Nếu máy yếu, không nên bật cùng lúc nhiều module nặng, nhiều Chromium context hoặc transcoding.

## SubSense tự động chèn phụ đề Việt

Trong mã nguồn Lampac, plugin gốc `/subsense-auto.js` được bật bằng:

```json
{
  "LampaWeb": {
    "initPlugins": {
      "subsenseAuto": true
    }
  }
}
```

Plugin chạy lúc Lampa khởi động, tìm phụ đề tiếng Việt trên SubSense và gắn vào player khi có IMDb ID. Đặt `subsenseAuto` thành `false` nếu không muốn dùng.

> Bản cài Termux tải Lampac từ release trước rồi ghi đè các module tuỳ biến. Khi tự build hoặc dùng release đã chứa thay đổi này, plugin sẽ có sẵn theo cấu hình trên.

## Xử lý lỗi thường gặp

### Script báo không chạy trong Termux

Hãy dùng Termux từ F-Droid và chạy lại trong ứng dụng Termux; không chạy script từ shell Android khác.

### `proot-distro` hoặc Ubuntu bị lỗi

```bash
pkg update -y
pkg install -y proot-distro
proot-distro reset ubuntu
bash setup-termux.sh --install
```

Lệnh `reset` xoá Ubuntu proot hiện tại, vì vậy cần cài lại Lampac sau đó.

### Không truy cập được từ thiết bị khác trong Wi-Fi

- Kiểm tra `lampac status`.
- Dùng đúng IP Wi-Fi của Android, không dùng `localhost` từ thiết bị khác.
- Đảm bảo cả hai thiết bị cùng mạng Wi-Fi và router không chặn client-to-client.
- Kiểm tra port trong `lampac info` hoặc `init.conf`.

### Nguồn Playwright không hoạt động

Chạy đồng bộ lại để cài Chrome/Chromium và cập nhật đường dẫn browser:

```bash
bash setup-termux.sh --sync
```

### Android tự dừng server

Giữ Termux ở foreground khi cần độ ổn định cao, tắt battery optimization cho Termux và tránh để hệ thống đóng ứng dụng khi thiếu RAM.

## Lưu ý an toàn

- Đổi mật khẩu mặc định `lampac`.
- Không mở port Lampac trực tiếp ra Internet nếu không có reverse proxy, firewall và xác thực phù hợp.
- Không chia sẻ `init.conf`, `passwd`, cookie, token hoặc tài khoản cá nhân.
- Chỉ sử dụng nội dung mà bạn có quyền truy cập theo luật pháp và điều khoản của từng nguồn.

## Tài liệu mã nguồn

- [Script cài Termux](setup-termux.sh)
- [Cấu hình mẫu](config/example.init.conf)
- [Module LampaWeb](Modules/LampaWeb/README.md)
- [Danh sách module](Modules/)
- [Giấy phép MIT](LICENSE)
