# Hướng dẫn test nguồn mới trên Termux

Tài liệu này hướng dẫn cách **test nhanh một nguồn (module Online) mới** sau khi ông tự build, trên Termux. Khớp với cơ chế sẵn có trong `setup-termux.sh` (launcher `lampac`).

## 1. Nguyên tắc chung

Mọi nguồn trong Lampac NextGen đều là một module nằm trong `Modules/Online*` (ENG / RUS / VN / Anime...). Khi build module mới, ông cần đảm bảo 3 thứ hoạt động:

1. **Module được biên dịch** (Roslyn) — không có lỗi compile khi server start.
2. **Route được đăng ký** — Lampac nhận diện được module (gọi được endpoint).
3. **Trả về link stream thật** — resolve ra được host/video để Lampa phát.

## 2. Đồng bộ module vào server

Module nguồn mới (file `.cs`, `ModInit.cs`, `manifest.json`) cần được kéo vào máy chạy Lampac (trong Ubuntu proot-distro). Dùng launcher `lampac`:

```bash
# Đồng bộ module từ nhánh arena về server
lampac sync
lampac stop
lampac start
```

> `lampac sync` lấy các file module từ `LAMPAC_CUSTOM_SOURCE_BASE` (mặc định nhánh `arena/01a06ac7-lampac`). Nếu module mới chưa push lên remote, hãy push trước rồi mới sync.

## 3. Kiểm tra module đã được biên dịch

Xem log khởi động để biết module có compile OK không:

```bash
# Chạy lampac ở chế độ xem log (hoặc mở log file)
lampac start

# Tìm dòng biên dịch của module mới
#  "compilation VidCore" (OK)
#  "compilation error: VidCore" (lỗi compile — sửa code trước)
```

Nếu lỗi compile, log sẽ báo tên file + lỗi. Sửa xong → `lampac stop && lampac start` lại.

## 4. Test nhanh một nguồn (VidCore làm ví dụ)

Launcher `lampac` có sẵn lệnh probe nguồn `vidcore` — test trực tiếp bằng HTTP, không cần mở Lampa:

```bash
# Movie mặc định (The Dark Knight, TMDB 155)
lampac vidcore

# Movie theo TMDB id
lampac vidcore 680

# Series: tmdb=2389 season=1 episode=1
lampac vidcore 2389 1 1
```

Lệnh này gọi:
- `GET /lite/vidcore?tmdb_id=...&rjson=1` → kiểm tra route được đăng ký
- `GET /lite/vidcore/video?id=...&s=...&e=...` → kiểm tra resolve stream

### Đọc kết quả probe

| Body trả về | Nghĩa | Việc cần làm |
| --- | --- | --- |
| `"host"` | **OK** — có link stream | Mở Lampa → Online → VidCore để chọn host |
| `"resolve"` | Exception bên trong Resolve | Tìm dòng `VidCore: ex ...` trong log |
| `"stream"` | Không có stream | Tìm dòng `VidCore: ...`: `token not found` / `enc-vidcore incomplete` / `servers POST empty` / `no servers` / `<name> no url` |
| `route ma` = HTTP code | Route chưa đăng ký | Tìm `compilation VidCore` (OK) hoặc `compilation error: VidCore` trong log |

## 5. Test nguồn khác tương tự

Mỗi nguồn ông tự build sẽ có một route riêng dạng `/lite/<tennguon>`. Cách test thủ công bằng curl khi Lampac đang chạy ở cổng `$PORT` (mặc định `9118`):

```bash
# Gọi trực tiếp route của module (thay <nguon> bằng tên module)
curl -s "http://127.0.0.1:9118/lite/<nguon>?tmdb_id=155&rjson=1"

# Xem body resolve stream
curl -s "http://127.0.0.1:9118/lite/<nguon>/video?id=155"
```

Kết quả phân loại giống như VidCore ở mục 4.

## 6. Bật log exception đầy đủ khi 500

Nếu route trả về HTTP 500 mà không rõ lỗi, bật ghi log exception vào file:

```json
// /root/lampac/init.conf (trong Ubuntu)
{
  "exceptionHandlerLogTarget": "file"
}
```

Sau đó HTTP 500 sẽ được ghi vào `/root/lampac/logs/exceptionHandler.log`. Sửa xong nhớ tắt lại nếu không cần.

## 7. Kiểm tra thật trên Lampa

Khi probe HTTP đã cho `"host"`, mở **Lampa** trên điện thoại/TV box:

1. Vào **Online** → chọn module ông build (VidCore / KKPhim / VsMov / K20...).
2. Chọn 1 phim/series → xem có danh sách nguồn (host) hiện ra không.
3. Phát thử 1 host → kiểm tra stream chạy.

## Checklist nhanh khi nguồn lỗi

- [ ] Module đã sync vào server chưa? (`lampac sync`)
- [ ] Compile OK? (tìm `compilation <ten>` trong log)
- [ ] Route có đăng ký không? (gọi `curl /lite/<ten>?rjson=1`)
- [ ] Resolve có link không? (gọi `curl /lite/<ten>/video?id=...`)
- [ ] Nếu 500 → bật `exceptionHandlerLogTarget: file` xem log cụ thể.
