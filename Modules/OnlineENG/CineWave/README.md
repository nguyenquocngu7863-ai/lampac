# CineWave bridge

Adapter cho `https://watch.cinewave.qzz.io` — trang tổng hợp nhiều server
(VEVC, CHDH, 2EMV, FVC, HEXA, ORA... theo UI trang /play). Module không lưu
media và không bypass DRM; chỉ đọc đúng luồng mà trình duyệt của chính người
dùng tải khi mở trang play.

## Play id

CineWave không dùng TMDB id trần trong URL play mà encode như sau:

- payload phim lẻ: `movie:{tmdbId}`
- payload phim bộ: `tv-{id}-{season}-{episode}`, trong đó mọi ký tự ở vị trí
  tuyệt đối `2, 10, 18...` (bước 8 kể từ index 2) của chuỗi `"{id}-{s}-{e}"`
  bị XOR với `0x17`.
- play id = `base64( XOR(payload, key lặp lại "cinewvve") )`, cắt padding `=`.

Đã kiểm chứng hai chiều với URL thật (movie id 4–7 chữ số, tv id 4–7 chữ số;
mẫu id 7 chữ số còn mask thêm ký tự tập ở index 10). Ví dụ:
`movie:24428` → `DgYYDBJMRFFXW1Y` → `watch.cinewave.qzz.io/play/DgYYDBJMRFFXW1Y`.

## Bật riêng khi ENG đang ẩn

Giữ `"disableEng": true` và chỉ opt-in CineWave:

```json
"CineWave": {
  "enable": true,
  "enabled": true
}
```

Các nguồn ENG khác vẫn không xuất hiện.

## Resolve stream

Backend `api.cinewave.qzz.io` không expose endpoint GET công khai nào, nên
module resolve bằng **chromium headless** (Shared/PlaywrightCore, giống
cơ chế `black_magic` của AutoEmbed): mở trang `/play/{encId}`, chặn media/ads
qua route sniffing, bắt request `.m3u8`/`.mp4` đầu tiên cùng header của nó,
cache trong `cacheSeconds` giây (mặc định 1200). Yêu cầu Playwright/Chromium
đang bật — khi `PlaywrightBrowser.Status == disabled` module tự ẩn khỏi danh
sách nguồn.

Phim lẻ resolve ngay theo TMDB id. Season/tập phim bộ lấy metadata từ TMDB
(`cub.api_key`) và fallback Cinemeta theo `imdb_id`; URL play của tập vẫn
chỉ phụ thuộc `tmdb/s/e` nhờ codec ở trên.

## Phát

`streamproxy: false` mặc định: Lampa nhận URL m3u8/mp4 gốc kèm header đã bắt
được (referer/origin/user-agent của trang play), không qua Lampac — nhẹ
CPU/RAM cho máy chạy server. Bật `streamproxy: true` trong init khi CDN nguồn
chặn UA của player (Lampac sẽ relay `/proxy/` với header của trình duyệt).

Card phim lẻ dùng method `call` → endpoint `lite/cinewave/video` resolve
headless trong lúc Lampa hiển thị loading. `resolveSeconds` (mặc định 20)
giới hạn thời gian chờ xuất hiện m3u8; `timeoutSeconds` (mặc định 30) áp cho
request metadata TMDB/Cinemeta.
