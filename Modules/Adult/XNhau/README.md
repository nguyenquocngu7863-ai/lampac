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
1. Deduplicate playlist items (DistinctBy video URI)
2. Pagination search: sửa từ `?from=` thành `?from_videos+from_albums=`
3. Empty list trả về đúng JSON cho trang >1 (tránh duplicate)

## Còn lại
- Plugin người đăng (`online-compact.js`) đang đợi test trên app
- Server cần restart để load DLL mới
