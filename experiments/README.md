# Experiments — plugin thử nghiệm

Thư mục chứa các plugin JS thử nghiệm, **không** được nhét vào root release hay
danh sách sync của `setup-termux.sh`. Nạp trực tiếp qua CDN jsDelivr khi muốn thử.

## sisi-restyle.js

Thay đổi bố cục + poster của danh sách SISI:

- Poster 16:9 bo góc lớn, ảnh phủ kín khung
- Tiêu đề nằm dưới poster như bố cục gốc (không đổi font)
- Badge thời lượng/chất lượng giữ nguyên kiểu mặc định của Lampa
- Lưới tự động: 2 cột (điện thoại dọc) / 3 cột / 4 cột (màn lớn)
- Cài đặt riêng cho người dùng (Cài đặt → Giao diện):
  - **SISI kiểu mới (thử nghiệm)** — bật/tắt toàn bộ
  - **SISI: số cột** — Tự động / 2 / 3 / 4 / 5 cột
  - **SISI: số hàng trên màn hình** — Tự động (16:9) / 2 / 3 / 4 hàng
    (chọn số hàng thì poster co giãn chiều cao để đủ N hàng một màn)
- Chỉ tác động các màn SISI (`sisi_*`), màn khác giữ nguyên

### Cài qua jsDelivr

Trong Lampa: Cài đặt → Tiện ích mở rộng (Plugins) → Thêm plugin, dán:

```text
https://cdn.jsdelivr.net/gh/nguyenquocngu7863-ai/lampac@arena/01a04e63-lampac/experiments/sisi-restyle.js
```

Bản ghim theo commit (không bị cache đổi nội dung):

```text
https://cdn.jsdelivr.net/gh/nguyenquocngu7863-ai/lampac@<commit-sha>/experiments/sisi-restyle.js
```

> jsDelivr cache theo nhánh khoảng 12 giờ. Sau khi push bản mới, muốn nhận ngay
> thì dùng link theo commit SHA, hoặc thêm `?v=<số bất kỳ>` phía sau, hoặc purge:
> `https://purge.jsdelivr.net/gh/nguyenquocngu7863-ai/lampac@arena/01a04e63-lampac/experiments/sisi-restyle.js`
