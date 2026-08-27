# Mapple 4K

Nguồn ENG cho Mapple (`https://mapple.uk`), không phụ thuộc `vidsrc.win`.

## Endpoint

- Phim: `/watch/movie/{tmdbId}`
- TV: `/watch/tv/{tmdbId}-{season}-{episode}`

Mapple hiện dùng protocol động gồm `window.__REQUEST_TOKEN__`, `/api/playback-init`, proof-of-work tùy phiên, rồi `/api/stream`. Module để chính trang Mapple thực hiện handshake/PoW trong Chromium và chặn response `/api/stream` để đọc `data.stream_url`; không lưu API key hay hash action trong source.

HLS cũng được bắt trực tiếp từ network và Performance API làm fallback. Chỉ một trang Mapple được mở, không quét nhiều iframe như CineWave.

## Bật riêng

```json
"disableEng": true,
"Mapple4K": {
  "enable": true,
  "enabled": true,
  "streamproxy": true
}
```
