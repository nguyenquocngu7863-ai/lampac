# Experiments — plugin thử nghiệm

Thư mục chứa các plugin JS thử nghiệm, **không** được nhét vào root release hay
danh sách sync của `setup-termux.sh`. Nạp trực tiếp qua CDN jsDelivr khi muốn thử.

## 85po.js

Plugin thử nghiệm lấy phim từ `85po.com` — nếu chạy ổn sẽ chuyển thành module
SISI chính thức phía server:

- Nút **85PO** trong menu trái của Lampa
- Danh mục: Mới nhất / Phổ biến / Đánh giá cao / 12 thể loại / Tìm kiếm
- Lưới video 16:9 có badge thời lượng, cuộn xuống tự tải trang tiếp
- HTML tải qua proxy CORS (mặc định `https://cors.eu.org/`, đổi được)
- Tự bóc link mp4 `get_file` chất lượng cao nhất (2160p → 360p)
- Cài đặt → **85PO**:
  - **Proxy CORS** — prefix tải trang web
  - **Stream proxy prefix** — phát video qua proxy server Lampac
    (ví dụ `http://IP:9118/media/stream/TOKEN/`), để trống thì phát thẳng

### Cài qua jsDelivr

```text
https://cdn.jsdelivr.net/gh/nguyenquocngu7863-ai/lampac@arena/01a04e63-lampac/experiments/85po.js
```

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
