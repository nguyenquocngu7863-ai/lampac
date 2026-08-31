# VidLink

Nguồn ENG `https://vidlink.pro`. Resolver mặc định là **HTTP** (token XSalsa20-Poly1305 → `/api/b/movie|tv/...`). Không cần Playwright. Playwright chỉ còn là bước dự phòng.

## Hiện nguồn

- `disableEng: false` — hiện như các nguồn ENG khác.
- `disableEng: true` (mặc định Termux) — vẫn hiện vì `enabled` mặc định `true`. Tắt bằng `"VidLink": { "enabled": false }` hoặc `"enable": false`.

## Cấu hình

```json
"VidLink": {
  "enable": true,
  "enabled": true,
  "streamproxy": true,
  "httptimeout": 20
}
```

## HTTP

API VidLink trả HLS (`stream.playlist`, `type: hls`). Lampac gắn `#.m3u8` để player dùng hls.js — URL `.mp4` thuần sẽ bị HTML5 từ chối (`no supported source`).

| Route | Việc |
|-------|------|
| `lite/vidlink` | Danh sách phim/tập |
| `lite/vidlink/video` | Stream |
| `lite/vidlink/video.m3u8` | Cùng resolver, đuôi HLS |

## Files

`ModInit.cs`, `Controller.cs`.
