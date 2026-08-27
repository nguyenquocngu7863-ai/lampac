# Mapple 4K

Nguồn ENG trực tiếp cho Mapple, không phụ thuộc `vidsrc.win` và không cần Chromium.

Protocol hiện tại được đối chiếu với plugin StreamPlay build ngày 2026-08-27:

1. Thử các mirror Mapple đang hoạt động.
2. GET trang watch và đọc `window.__REQUEST_TOKEN__`.
3. Tìm client key công khai trong bundle JavaScript lúc chạy; không hardcode key vào repository.
4. POST `/api/playback-init`.
5. Nếu server yêu cầu proof-of-work, giải SHA-256 challenge và POST lại.
6. Dùng playback token gọi `/api/stream` cho sáu source: Mapple, Nexus, Cipher, Pulse, Vertex, Chimp.
7. Thu thập `data.stream_url`, loại URL trùng và phát qua Lampac proxy với Referer đúng.

Route:

- Phim: `/watch/movie/{tmdbId}`
- TV: `/watch/tv/{tmdbId}/{season}-{episode}`

## Bật riêng

```json
"disableEng": true,
"Mapple4K": {
  "enable": true,
  "enabled": true,
  "streamproxy": true
}
```
