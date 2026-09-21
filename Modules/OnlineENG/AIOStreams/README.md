# AIOStreams bridge

Module riêng để Lampac đọc một manifest AIOStreams đã cấu hình. AIOStreams
chạy ở phía server của addon; Lampac chỉ gọi API Stremio chuẩn qua HTTP, không
cần chạy Node.js trong tiến trình Lampac hoặc trong Lampa WebView.

## Cấu hình

Manifest URL được tạo từ trang cấu hình AIOStreams và có thể chứa UUID, mật
khẩu, API key hoặc token. Không đưa URL này vào Git, log hoặc chat công khai.
Nhập nó trong AdminPanel hoặc `init.conf`:

```json
"AIOStreams": {
  "enable": true,
  "manifest": "https://your-aiostreams-instance/stremio/.../manifest.json",
  "streams": true,
  "subtitles": true,
  "streamproxy": true,
  "timeoutSeconds": 30,
  "maxStreams": 100,
  "cacheSeconds": 120,
  "detailedLabels": true
}
```

`detailedLabels` (mặc định `true`) làm nhãn stream chi tiết hơn: tên release,
dung lượng, codec video (HEVC/H.264/AV1…), HDR (DV/HDR10+…), audio
(Atmos/TrueHD/DTS… + kênh 5.1/7.1) và số seeder. Nhãn này xuất hiện trong
danh sách card phim và khung "Chọn link" của Lampa. Đặt `false` để quay lại
nhãn ngắn dạng `Nguồn • Chất lượng`.

`enable` tắt toàn bộ cầu nối. `streams` và `subtitles` cho phép tắt riêng
resource tương ứng. Các addon bên trong AIOStreams vẫn được bật/tắt ở chính
trang cấu hình AIOStreams; Lampac không cố sửa ngược cấu hình riêng tư đó.

## Luồng phim bộ: Season → Episode → popup chọn nguồn

1. Mở phim bộ (`serial=1`) liệt kê mùa (`SeasonTpl`), bấm mùa liệt kê tập
   (`EpisodeTpl`). Thẻ tập KHÔNG gắn link phát tắt để mọi lượt bấm đều đi
   qua đúng một đường duy nhất.
2. Bấm tập gọi route episode, server trả single-play JSON với toàn bộ link
   file nằm trong `quality`.
3. Plugin client `aioeppick.js` (cài trong app theo URL
   `http://127.0.0.1:9118/aioeppick.js`) chặn response này và hiện popup
   native `Chọn nguồn` (Lampa.Select): mỗi file một dòng ngắn gọn
   (`2160p • Penguin • 26.8MB`). Bấm dòng nào thì phát link dòng đó.
4. Bấm back (không chọn) thì phát link mặc định đầu tiên. Không bao giờ nuốt
   playback: lỗi ở bất kỳ bước nào cũng rơi về phát mặc định.
5. Link `.mkv` đi qua hook GStreamer của app (`/gst/add`) để transcode audio
   (DTS/TrueHD mà player trong không giải mã được). Link `.mp4`/`.m3u8` phát
   trực tiếp.

## Danh sách route đầy đủ

- `GET lite/aiostreams` — tìm kiếm/liệt kê (phim lẻ, mùa, tập, resolve tập).
  Tham số: `tmdb_id`/`imdb_id`/`stremio_id`, `title`, `original_title`,
  `serial=1` (phim bộ), `s` (mùa), `e` (tập), `stream_source` (lọc theo nguồn),
  `play=true` (chuyển thẳng tới link phát).
- `GET lite/aiostreams/episode` — resolve một tập: `stremio_id`, `title`,
  `original_title`, `s`, `e`, `play`, `stream_source`. Trả single-play JSON
  `{title, method:"play", url, quality, hls_manifest_timeout}`.
- `GET lite/aiostreams/file.mkv` / `file.mp4` / `file.m3u8` / `video` — phát
  một link cụ thể: `u` (link đã mã hóa), `h` (headers đã mã hóa, tùy chọn),
  `play=true`, `aiostreams_select=1`.
- `GET lite/aiostreams/subtitles` — phụ đề cho plugin StremioSub chung:
  `stremio_id` (hoặc `id`/`imdb_id`/`tmdb_id`), `serial`, `s`, `e`, `source`.

## API Stremio được dùng

- `stream/movie/{id}.json`
- `stream/series/{id}:{season}:{episode}.json`
- `subtitles/movie/{id}.json`
- `subtitles/series/{id}:{season}:{episode}.json`

Module giữ flow series `Season -> Episode -> release -> player`. Stream HTTP(S)
được nhận; `magnet:` và mục chỉ có `externalUrl` không được mở. Các mục có
`behaviorHints.notWebReady` hoặc metadata `.mkv` đi route MKV để plugin
GStreamer quyết định; HLS/MP4 rõ ràng giữ route direct.

AIOStreams có thể trả URL resolver hoặc file host phụ thuộc addon bên trong.
Module không bypass DRM, captcha, VIP gate hay anti-bot; lỗi của một URL không
được biến thành quyền truy cập mới.

## Subtitle bridge

`lite/aiostreams/subtitles` trả lại các subtitle record hợp lệ cho plugin
StremioSub chung của LampaWeb. Khi AIOStreams được bật, plugin ưu tiên resource
này; nếu module chưa cấu hình hoặc tắt, nó giữ fallback SubDL + SubSource cũ.

Đây là một bridge generic, không phải bản port Node.js của AIOStreams. Manifest
có thể đổi danh sách addon mà không cần biên dịch lại module.

## Pagination

Nguồn Stremio trả toàn bộ link trong một response JSON nên không có phân trang
kiểu trang 1/2. Danh sách mùa và tập phim lấy từ Cinemeta/TMDB/TVmaze (tùy ID
mở phim), không phụ thuộc số lượng link.

## Cache & Build

- Không cần build DLL: copy `.cs` vào
  `/root/lampac/module/OnlineENG/AIOStreams/` rồi restart là chạy
  (server biên dịch runtime bằng Roslyn lúc khởi động).
- Cache resolve theo `cacheSeconds` (mặc định 120 giây); query tập đã xem
  gần đây có thể trả kết quả cũ — verify code mới bằng tập chưa từng mở.
- Plugin client (`aioeppick.js`, `stremiosub.js`…) serve qua FileCache trong
  RAM: sửa xong phải restart server; app Lampa cache plugin theo URL nên đổi
  nội dung file thì user phải xóa cache plugin trong app (hoặc gỡ + thêm lại
  URL) mới nhận bản mới.
