# Mapple 4K

Nguồn ENG trực tiếp cho Mapple (`https://mapple.uk`), không phụ thuộc trang tổng hợp `vidsrc.win`.

## Endpoint

- Phim: `/watch/movie/{tmdbId}`
- TV: `/watch/tv/{tmdbId}-{season}-{episode}`

Resolver dùng server-action protocol hiện tại:

1. GET `enc-dec.app/api/enc-mapple` để nhận `sessionId` và `nextAction`.
2. POST JSON với header `Next-Action` tới trang watch.
3. Đọc `data.stream_url` trong response React Server Action.

Nguồn thử theo thứ tự: Europa/Ganymede multi-audio 4K, Callisto/Io 4K, sau đó các alias Mapple/Sakura/Alfa/Oak/Wiggles. URL trùng bị loại; HLS được phát qua Lampac để giữ Referer.

Nếu Server Action không trả stream (action/hash thay đổi), module mở trực tiếp trang Mapple bằng Chromium, kích hoạt player và bắt HLS từ network/Performance API. Browser fallback chỉ chạy một trang Mapple, không quét hàng loạt iframe như CineWave.

## Bật riêng

```json
"disableEng": true,
"Mapple4K": {
  "enable": true,
  "enabled": true,
  "streamproxy": true
}
```
