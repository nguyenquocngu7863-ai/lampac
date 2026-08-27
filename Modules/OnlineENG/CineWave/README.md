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

Resolver chính gọi catalog Stremio-compatible `hdhub.thevolecitor.qzz.io`
bằng IMDb ID và lấy toàn bộ stream 2160p/1080p/720p (HLS, MKV, MP4 và các
CDN trực tiếp), bỏ mục donation/Discord và loại URL trùng. Kết quả được đưa
vào menu chất lượng của Lampa; các file MKV có thể đi qua plugin GStreamer.

Trang `/play/{encId}` cùng Chromium route-sniffing được giữ làm fallback chỉ
khi catalog trực tiếp không trả stream, nên CineWave không còn phụ thuộc
Chromium trong trường hợp bình thường.

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
