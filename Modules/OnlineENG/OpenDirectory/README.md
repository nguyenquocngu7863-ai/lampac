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
trên URL Lampac. MKV/AVI có thể đi qua `gst.js` khi `gst.enable: true`; HLS và
MP4 giữ route direct bình thường. Episode có nhiều release mở link picker để
chọn từng file.

`streamproxy: true` mặc định khiến Lampac proxy byte stream; module không
transcode. Với file lớn, kiểm tra origin có trả HTTP Range ổn định trước khi
bật dùng thường xuyên.

## Configuration

```json
"OpenDirectory": {
  "enable": true,
  "directoryHost": "https://a.111477.xyz",
  "streamproxy": true,
  "timeoutSeconds": 20,
  "maxFiles": 40,
  "maxDirectoryEntries": 2500
}
```

`maxDirectoryEntries` giới hạn số entry module đọc từ một trang; nó không tải
nguyên thư viện media. Nếu directory đổi host hoặc cần một mirror hợp lệ, chỉ
đổi `directoryHost`; URL nội bộ vẫn bị giới hạn cùng hostname đó.
