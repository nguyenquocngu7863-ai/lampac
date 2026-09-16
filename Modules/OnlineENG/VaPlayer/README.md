# VaPlayer

Module ENG lấy link trực tiếp từ API JSON của VaPlayer, không cần Playwright,
không giải mã, không browser.

## API

- Phim lẻ: `GET https://streamdata.vaplayer.ru/api.php?imdb={imdb}&type=movie`
- Phim bộ: `GET .../api.php?imdb={imdb}&type=tv&season={s}&episode={e}`
- Header bắt buộc: `Referer: https://nextgencloudfabric.com/`
- Trả về: `data.stream_urls[]` (m3u8 master đa chất lượng 480p/720p/1080p,
  H.264 + AAC — player trong giải mã được, không cần GST), `data.file_name`
  (dùng tách tag `[1080p]` làm nhãn), `default_subs[]` (hiện API trả rỗng
  nhưng code đã parse generic).

Host stream xoay vòng theo từng resolve (vd `onlinevisibilitysystem.site`,
`scalableimpactgroup.site`), path chứa token mã hóa — link có hạn, cache
resolve 15 phút, không lưu link lâu.

## Route

- `GET lite/vaplayer` — tìm kiếm/liệt kê (ViewTmdb, method `call`).
- `GET lite/vaplayer/video` + `video.m3u8` — phát: `imdb_id` (bắt buộc),
  `s`, `e`, `play`. Mỗi URL một dòng quality (`VaPlayer • 1080p #2...`).

## Nguồn gốc

Trích từ repo CSX (CloudStream extensions, hiện hiatus): hàm
`invokeVaPlayer` — đây là nguồn duy nhất trong CSX còn xài được nguyên bản
vì là JSON thẳng. VidFast cùng repo đã chết theo hướng ad-locker (script đa
hình + handshake fingerprint + popup roulette), không port được.
