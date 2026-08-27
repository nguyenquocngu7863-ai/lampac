# CineWave.su bridge

Adapter cho `https://www.cinewave.su`. Nguồn dùng Chromium để mở trang phim/TV thật và lần lượt chuyển các tab player mà trang đang công bố:

- videasy
- VidFast
- FilmU
- Vares
- VidGod
- VidKing
- VixSrc
- VidLink
- VidZee / VidZee V2
- autoembed
- VidRock
- VidSrc
- 111movies
- SuperEmbed
- 2Embed

Resolver bắt request `.m3u8` và `.mp4`, giữ header của từng player, loại URL trùng và sắp HLS trước file trực tiếp. Nó không lấy catalog file HdHub cũ nữa.

## Bật riêng khi ENG đang ẩn

Giữ `"disableEng": true` và chỉ opt-in CineWave:

```json
"CineWave": {
  "enable": true,
  "enabled": true,
  "siteHost": "https://www.cinewave.su",
  "streamproxy": false,
  "resolveSeconds": 32,
  "cacheSeconds": 1200
}
```

Các nguồn ENG khác vẫn không tự xuất hiện. CineWave cần Chromium; Videasy direct API thì không.

## Phim và TV

- Phim: `/movie/{tmdbId}`.
- TV: `/tv/{tmdbId}`, sau đó resolver chọn season/episode trước khi quét player.
- Danh sách season/tập vẫn lấy từ TMDB và fallback Cinemeta như module cũ.

`resolveSeconds` được chia cho các tab player, với giới hạn khoảng 1.5–2.5 giây mỗi tab. Lần mở đầu chậm hơn; kết quả sau đó được cache theo `cacheSeconds`.
