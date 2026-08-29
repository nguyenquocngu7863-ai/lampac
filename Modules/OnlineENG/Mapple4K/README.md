# Mapple 4K

Nguồn ENG trực tiếp cho Mapple, không phụ thuộc `vidsrc.win` và không cần Chromium.

## Trạng thái upstream (kiểm tra 2026-08-28)

Endpoint stream cũ đã bị khai tử. `GET https://mapple.uk/api/stream?...` trả về:

```json
{"success":false,"error":"This playback endpoint has been retired. Refresh the watch page."}
```

Tài liệu chính thức của Mapple Player (`mappletv.uk/docs/getting-started/endpoints`)
chỉ công bố embed iframe, không có API JSON công khai:

- Phim: `https://mapple.rip/watch/movie/{id}`
- TV: `https://mapple.rip/watch/tv/{id}-{season}-{episode}`

## Module làm gì

1. Thử lần lượt các mirror Mapple theo danh sách backup đăng ngay trên `mapple.uk`
   (`mapple.tv`, `mapple.rip`, `mapple.bid`, `mappl.tv`, `mapplee.com`,
   `mapple.cc`, `lightflix.app`).
2. GET trang watch. TV dùng dạng `{id}-{season}-{episode}` đúng tài liệu; dạng
   `{id}/{season}-{episode}` cũ vẫn được thử lại làm fallback.
3. Quét HTML trang watch tìm playlist `.m3u8`/`.mp4` render sẵn ở server. Đây là
   đường chính hiện nay và không cần Chromium.
4. Nếu trang không nhúng playlist, thử protocol cũ: đọc `window.__REQUEST_TOKEN__`,
   tìm client key `mptv_sk_*` trong bundle, `POST /api/playback-init` (kèm giải
   proof-of-work SHA-256 khi server yêu cầu), rồi gọi `/api/stream` cho sáu source
   Mapple, Nexus, Cipher, Pulse, Vertex, Chimp.
5. Phát qua Lampac proxy với Referer đúng.

Khi không lấy được stream, lỗi trả về nêu đúng lý do upstream ghi nhận được — ví
dụ `Mapple: This playback endpoint has been retired. Refresh the watch page.` —
thay vì chỉ báo "Mapple không trả stream".

## Route

- Phim: `/watch/movie/{tmdbId}`
- TV: `/watch/tv/{tmdbId}-{season}-{episode}`

## Bật riêng

```json
"disableEng": true,
"Mapple4K": {
  "enable": true,
  "enabled": true,
  "streamproxy": true
}
```
