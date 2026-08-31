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

CSX `invokeVidlink`: token `enc-dec.app/api/enc-vidlink`, JSON `stream.qualities` (360/720/1080). HLS khi `type=m3u8` hoặc URL `.m3u8` → `/lite/vidlink/playlist.m3u8?uri=`. Còn lại MP4 → `/lite/vidlink/media.mp4?uri=` với header CDN **`filmboom.top`** (không vidlink.pro). Segment không đi `/proxy/`.

| Route | Việc |
|-------|------|
| `lite/vidlink` | Danh sách phim/tập |
| `lite/vidlink/video` | Stream |
| `lite/vidlink/video.m3u8` | Cùng resolver |
| `lite/vidlink/playlist.m3u8` | Tải HLS, viết lại segment |
| `lite/vidlink/media.mp4` | Stream MP4 (có `uri=`) |
| `lite/vidlink/file.mp4` | Alias MP4 (có `uri=`) |
| `lite/vidlink/media.ts` | Segment HLS |

## Files

`ModInit.cs`, `Controller.cs`.
