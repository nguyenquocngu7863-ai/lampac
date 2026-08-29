# Open Directory bridge

Adapter read-only cho một Open Directory công khai được cấu hình trong
`OpenDirectory.directoryHost` (mặc định `https://a.111477.xyz`). Module không
lưu media và không bypass Cloudflare, captcha, DRM hoặc trang download bị khóa.
Chỉ dùng các file mà người dùng có quyền truy cập.

## Cách tìm

- Phim lẻ: thử thư mục chính xác `movies/Title (Year)/` từ `title`,
  `original_title` và `year` của Lampa.
- Phim bộ: thử `tvs/Title/`, sau đó `asiandrama/` và `kdrama/`; season được
  đọc từ các thư mục `Season N`/`Sxx`, tập được đọc từ tên file `SxxExx` hoặc
  `Exx`.
- Module không quét toàn bộ root 1.2 PB và không tìm mơ hồ theo một phần tên.
  Không thấy thư mục chính xác thì trả về rỗng để tránh lấy nhầm phim.

## Phát

Các file `.mkv`, `.mp4`, `.m3u8`, `.webm`, `.avi` và `.m2ts` được giữ đuôi
trên URL. MKV/AVI có thể đi qua `gst.js` khi `gst.enable: true`; HLS và MP4
giữ route direct bình thường. Episode có nhiều release mở link picker để chọn
từng file.

`directLinks: true` (mặc định) đưa **URL gốc của host** thẳng cho Lampa:
playback không chạm vào Lampac, nhẹ CPU/RAM/pin cho máy chạy server. Lampac
chỉ còn fetch trang listing khi tìm phim (bắt buộc, vì nó dùng UA Chrome mới
qua được Cloudflare). Marker chọn file đi trong fragment `#opendirectory_select`
nên origin không bao giờ thấy tham số lạ.

Đặt `directLinks: false` để card/episode link trỏ endpoint
`/lite/opendirectory/*` của Lampac (endpoint 302 sang nguồn) — cần khi bật
`streamproxy: true`: endpoint đó sẽ 302 vào `/proxy/`, Lampac fetch byte bằng
UA Chrome và forward Range header. Dùng chế độ proxy này nếu gặp triệu chứng
"thấy link ngon mà không phát được" (Cloudflare 403 UA của player);
`serverproxy.enable` trong `base.conf` đã được bật mặc định.

Module không transcode. Với file lớn, kiểm tra origin có trả HTTP Range ổn
định trước khi bật dùng thường xuyên.

## Configuration

```json
"OpenDirectory": {
  "enable": true,
  "directoryHost": "https://a.111477.xyz",
  "streamproxy": false,
  "directLinks": true,
  "timeoutSeconds": 20,
  "maxFiles": 40,
  "maxDirectoryEntries": 2500
}
```

`maxDirectoryEntries` giới hạn số entry module đọc từ một trang; nó không tải
nguyên thư viện media. Nếu directory đổi host hoặc cần một mirror hợp lệ, chỉ
đổi `directoryHost`; URL nội bộ vẫn bị giới hạn cùng hostname đó.
