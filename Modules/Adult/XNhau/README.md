# XNhau (xnhau.cab)

Module Adult cho nguồn xnhau.cab (Việt).

## Cấu hình
- `manifest.json`: module Adult, `enable: true`, `dynamic: true`
- `Controller.cs`: endpoint `/xnhau` (list/search/member feed), `/xnhau/strem` (stream), `/xnhau/uploader` (lấy tên người đăng)
- `Service.cs`: parse HTML, regex `div.item`, stream links từ file:...

## Pagination
- Category (`the-loai/`): dùng `?from=`
- Search (`search/`): dùng `?from_videos+from_albums=` (từ HTML `data-parameters`)
- Member feed (`members/`): dùng `?from=`

## Đã fix
1. Deduplicate playlist items (`DistinctBy` video URI) — tránh card trùng
2. Pagination search: dùng đúng param `from_videos+from_albums`
3. Empty list cho `pg > 1` khi không có thêm nội dung — tránh lặp 24 kết quả

## Cache & Build — ĐỌC TRƯỚC KHI FIX
- Module chạy từ file `.cs` trong container (`/root/lampac/module/Adult/XNhau/`)
- Khi sửa code, phải `build` (`dotnet publish`) rồi `cp` DLL mới vào `/root/lampac/module/Adult/XNhau/`
- Server (`dotnet Core.dll`) load DLL từ thư mục module — KHÔNG tự load `.cs` mới
- **Nếu sửa `.cs` nhưng API vẫn trả kết quả cũ**: server đang dùng DLL cũ trong memory — phải `kill` `dotnet Core.dll`, `cp` DLL mới, rồi `lampac start` lại
- `Controller.cs`: xử lý logic (trả JSON rỗng cho `pg > 1` khi search)
- `Service.cs`: xử lý URL và parse HTML — pagination param phải khớp HTML thực tế (`from_videos+from_albums` cho search, `from=` cho category/member)

## Còn lại
- Plugin người đăng (`online-compact.js`) đang đợi test trên app
- Server cần restart để load DLL mới
