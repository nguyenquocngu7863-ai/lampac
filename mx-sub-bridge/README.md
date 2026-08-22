# SubSense Termux Bridge

APK cầu nối **Lampa → SubSense → Termux:API**. APK này không phát video và không phụ thuộc Lampa Player. Nó nhận một URL `mxsub://download...` từ plugin, sau đó gọi Termux `RUN_COMMAND` để tải phụ đề vào thư mục **Android/Downloads**.

## Cách hoạt động

```text
Lampa – nút Tải phụ đề
  ↓ chọn tập và bản sub
SubSense download plugin
  ↓ mxsub://download?url=...&filename=...
SubSense Termux Bridge APK
  ↓ com.termux.RUN_COMMAND
Termux:API – termux-download
  ↓
Android/Downloads/<tên phụ đề>.srt
```

- SRT: dùng `termux-download`, có thông báo tải xuống của Android.
- VTT/ZIP: chạy `curl` trong Termux và chuẩn hóa thành SRT. Với ZIP, APK lấy file SRT/VTT đầu tiên trong archive.
- RAR không được tự giải nén; plugin sẽ không cho chọn RAR.

## Chuẩn bị Termux

Cài **Termux** và **Termux:API** từ cùng một nguồn (F-Droid hoặc GitHub; không trộn chữ ký APK), sau đó chạy trong Termux:

```bash
termux-setup-storage
pkg update
pkg install termux-api curl unzip
```

Cho phép ứng dụng Termux:API truy cập các quyền mà Android yêu cầu. Mở `~/.termux/termux.properties` và thêm:

```properties
allow-external-apps=true
```

Áp dụng cấu hình:

```bash
termux-reload-settings
```

## Build APK ngay trên Termux

Yêu cầu: `wget`, `unzip`, `openjdk-17`, `aapt2`. Script sẽ tải Android command-line tools, SDK và Gradle nếu máy chưa có.

```bash
cd ~/lampac/mx-sub-bridge
bash build-termux.sh
```

APK sau khi build:

```text
$HOME/subsense-termux-bridge.apk
```

Cài bằng:

```bash
termux-open "$HOME/subsense-termux-bridge.apk"
```

Nếu source nằm ở thư mục khác, chạy script ngay trong thư mục `mx-sub-bridge`; APK vẫn được xuất ra `$HOME/subsense-termux-bridge.apk`.

## Cấp quyền cho bridge

Sau khi cài APK:

1. Mở **App info → Permissions** của **SubSense Termux Bridge**.
2. Bật quyền **Run commands in Termux environment** (`com.termux.permission.RUN_COMMAND`).
3. Nếu Android chặn chạy nền, cho phép Termux hiển thị trên ứng dụng khác hoặc mở Termux một lần trước khi thử.

Quyền này là bắt buộc vì Android không cho một ứng dụng bất kỳ tự chạy lệnh Termux.

## Cài plugin Lampa

Cài file `subsense-download.js` trong thư mục gốc của repository bằng URL raw tương ứng với branch đang dùng, ví dụ:

```text
https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a028cf-lampac/subsense-download.js
```

Plugin hiện tại `subsense-auto.js` vẫn giữ nguyên chức năng gắn sub cho Lampa Player. Hai plugin là hai chế độ riêng; chỉ dùng plugin download nếu muốn lưu sub để mở bằng player khác.

Trong Lampa:

1. Mở trang chi tiết phim.
2. Chọn **Tải phụ đề**.
3. Với series, chọn mùa/tập.
4. Chọn bản sub tiếng Việt/SRT/ZIP.
5. Chờ thông báo của Termux:API hoặc Android Download Manager.

File ổn định theo tên phim, tập và label bản sub, ví dụ:

```text
Downloads/Avatar S01E02 - Vietnamese.srt
```

## Xử lý lỗi

- **Không có nút Tải phụ đề:** kiểm tra phim có IMDB ID và plugin đã reload.
- **Chưa tìm thấy bridge:** cài APK này; trên thiết bị Android plugin phải chạy trong Lampa app có `Android.openBrowser`.
- **Permission denied / RUN_COMMAND:** cấp quyền Run commands cho APK và đặt `allow-external-apps=true` trong Termux.
- **Không tải được ZIP:** kiểm tra `curl` và `unzip` đã cài; một số archive chỉ có RAR/PGS nên không thể chuyển thành SRT.
- **Không có thông báo hoàn tất:** SRT trực tiếp dùng Download Manager; VTT/ZIP chạy nền bằng Termux, hãy xem notification hoặc kiểm tra thư mục Downloads.
