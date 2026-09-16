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
dung lượng (📦/💾), codec video (HEVC/H.264/AV1…), HDR (DV/HDR10+…), audio
(Atmos/TrueHD/DTS… + kênh 5.1/7.1) và số seeder (👤). Nhãn này xuất hiện trong
danh sách card phim và khung "Chọn link" của Lampa. Đặt `false` để quay lại
nhãn ngắn dạng `Nguồn • Chất lượng`.

`enable` tắt toàn bộ cầu nối. `streams` và `subtitles` cho phép tắt riêng
resource tương ứng. Các addon bên trong AIOStreams vẫn được bật/tắt ở chính
trang cấu hình AIOStreams; Lampac không cố sửa ngược cấu hình riêng tư đó.

## API được dùng

- `stream/movie/{id}.json`
- `stream/series/{id}:{season}:{episode}.json`
- `subtitles/movie/{id}.json`
- `subtitles/series/{id}:{season}:{episode}.json`

Module giữ flow series `Season -> Episode -> chọn nguồn -> release -> player`.
Stream HTTP(S) được nhận; `magnet:` và mục chỉ có `externalUrl` không được mở.

### Tập phim Bộ: pop-up chọn nguồn

`lite/aiostreams/episode` nhận thêm cờ `source_pick=1`. Khi có cờ này và chưa có
`stream_source`, route trả JSON `{type:"sources", default, sources:[{name, streams,
quality, url}]}` — mỗi nguồn một `url` đã chốt `stream_source` — để `Online/plugin.js`
bật `Lampa.Select` cho người dùng chọn, rồi mới gọi lại lấy `VideoTpl`. Menu chất
lượng vì thế chỉ chứa link của nguồn vừa chọn thay vì gộp mọi provider.

Không có `source_pick` (plugin cũ, hoặc gọi tay) thì hành vi cũ được giữ nguyên:
một `VideoTpl` với toàn bộ nguồn trong menu chất lượng. Nguồn đã chọn được client
nhớ theo thẻ phim; playlist và menu ngữ cảnh dùng nguồn đã nhớ, không hỏi lại. Các mục có
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
