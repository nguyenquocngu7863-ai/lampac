# Sootio Stremio bridge

Module riêng cho Sootio, chạy song song với WebStreamr và K20.
Module gọi Stremio chuẩn:

```text
stream/movie/{id}.json
stream/series/{id}:{season}:{episode}.json
```

Manifest mặc định là cấu hình `httpstreaming` do người dùng cung cấp. Có thể
thay bằng manifest được tạo lại ở `https://sooti.click/` trong `init.conf`:

```json
"Sootio": {
  "enable": true,
  "manifest": "https://sooti.click/.../manifest.json",
  "streamproxy": true,
  "timeoutSeconds": 30,
  "maxStreams": 100
}
```

## Behavior

- Sootio chỉ được gọi bằng IMDb id chuẩn `tt...`. Nếu Lampa chỉ có TMDB id,
  bridge dùng TMDB `external_ids` để đổi sang IMDb chính xác trước khi gọi
  Sootio; không gửi raw `tmdb:...` để upstream tự đoán phim.
- Movie hiển thị từng file như một card riêng, có nhóm theo provider (4KHDHub,
  111477, UHDMovies, MkvBase, Vadapav...).
- Series xây season/episode trước. Sau khi chọn tập, Sootio mới được gọi và
  popup cho phép chọn từng release/độ phân giải.
- Entry quảng cáo chỉ có `externalUrl` bị bỏ qua. Chỉ nhận `stream.url` là
  HTTP(S); `magnet:`, `externalUrl` và giá trị không phải HTTP không được mở.
- Sootio thường trả URL resolver không có đuôi. Metadata `fileName`, `quality`,
  `resolution`, `size` được dùng để giữ đúng route media. Link video opaque
  được đưa qua `/file.mkv` để `gst.js` có thể xử lý khi `gst.enable: true`.
  HLS/MP4 rõ ràng vẫn đi route direct.
- `streamproxy: true` chỉ proxy byte stream và header hợp lệ; module không
  bypass DRM, captcha, VIP gate hoặc anti-bot. Chỉ dùng add-on và stream mà
  người dùng có quyền truy cập.

Do Sootio tìm các stream cached/debrid từ nhiều catalog, độ ổn định và quyền
truy cập phụ thuộc cấu hình/nhà cung cấp của người dùng. Đây là source riêng,
không thay thế WebStreamr hoặc K20.
