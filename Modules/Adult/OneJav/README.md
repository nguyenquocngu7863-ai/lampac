# OneJav (SISI)

Nguồn **JAV torrent** từ **`https://onejav.com`** dưới dạng platform **SISI 18+** (route `/ojv`).

## Giai đoạn hiện tại (bước 1)

- Trang chủ (mới nhất) và duyệt theo **danh mục** `/tag/<Tên>` (Uncensored, FC2, Amateur, Creampie…).
- Card: poster + mã phim (vd `SSIS-123`) + dung lượng.
- Menu: Mới nhất / Tìm theo mã (khung) / Danh mục.

## Chưa làm (bước sau)

- **Phát phim**: trang chi tiết chỉ có link `.torrent` (không có magnet sẵn). Cần tải
  `.torrent`, băm SHA1 info dict thành magnet rồi giao cho **TorrServer** (tái dùng cơ chế
  `.torrent -> magnet` đã có cho Jackett). Route `ojv/view` hiện trả danh sách rỗng để
  bấm vào card không lỗi cứng.
- Phân trang tag (onejav nạp "Load more"), tìm theo mã/diễn viên.

## Cấu hình

SISI platform, cấu hình bằng section **`OneJav`** (SisiSettings) trong `init.conf`.

| Trường | Mặc định |
| --- | --- |
| host | `https://onejav.com` |
| displayindex | 40 |
