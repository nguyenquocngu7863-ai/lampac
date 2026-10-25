# KawaiJAV (Adult)

Module SISI cho site [Kawai JAV](https://kawai-jav.com) — JAV online (censored + uncensored leaked).

## Tính năng
- **Trang chủ / Bảng xếp hạng**: mới nhất, phổ biến (theo lượt xem)
- **Thể loại**: click vào studio (Prestige, S1, Moodyz, ...) để xem phim
- **Thẻ (Tags)**: đám mây thẻ ở cuối trang, click vào tag để lọc phim
- **Tìm kiếm**: theo từ khóa
- **Xem phim**: stream trực tiếp HD/SD qua stream proxy

## Cấu trúc
| File | Mô tả |
|------|-------|
| `ModInit.cs` | Đăng ký module SISI, host `https://kawai-jav.com` |
| `Controller.cs` | Route `/kawaijav` (danh sách), `/kawaijav/vidosik` (stream links), `/kawaijav/video` (phát trực tiếp) |
| `Service.cs` | Parse danh sách phim, thể loại, thẻ, videoData từ trang chi tiết |
| `manifest.json` | `{"enable": true, "dynamic": true}` |

## Smoke test (localhost:9118)
```
GET /kawaijav                        -> danh sách phim (count, total_pages, menu)
GET /kawaijav?c=categories/15.prestige -> xem theo thể loại
GET /kawaijav?c=tag/Tokyohot         -> xem theo thẻ
GET /kawaijav?search=ipx             -> tìm kiếm
GET /kawaijav/vidosik?uri=<watch-url>-> stream links {HD, SD}
```

## Lưu ý
- Site dữ liệu khá thưa: một số thể loại/thẻ có rất ít phim (hoặc trống).
- Yêu cầu `streamproxy = true`, `httpversion = 1`, timeout 25s.
