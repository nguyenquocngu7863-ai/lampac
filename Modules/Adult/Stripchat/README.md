# Stripchat

Nguồn livecam Stripchat cho SISI/Lampac.

- Route: `/stripchat`
- Config: `Stripchat`
- Danh mục: nữ, cặp đôi, nam, chuyển giới
- Danh sách lấy từ API công khai `/api/front/v2/models`
- Luồng adaptive HLS lấy qua `edge-hls.doppiocdn.net` và proxy bởi Lampac để giữ cùng IP/headers
- Chỉ hiển thị phòng đang live ở trạng thái `public`
