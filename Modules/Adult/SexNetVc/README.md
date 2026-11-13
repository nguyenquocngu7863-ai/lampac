# SexNetVc

Module Adult cho `https://sexnetvc.com` (engine vlxx, SSR thuần, không cần Chrome).

## Route

| route | việc |
|---|---|
| `/sexnetvc` | danh sách: `?search=`, `?c=the-loai/<slug>`, `?c=tag/<slug>`, `?pg=N`, `?rel=<uri>` |
| `/sexnetvc/p/...` | đường dẫn thay query: `p/2`, `p/the-loai/khong-che/2`, `p/tag/xnxx`, `p/search/jav%20hd` |
| `/sexnetvc/genres?uri=` | popup long-press: chip `Thể loại` / `Thẻ` + mục `Liên quan` |
| `/sexnetvc/vidosik?uri=` | danh sách nguồn phát |
| `/sexnetvc/video.m3u8?uri=&q=` | proxy stream (HLS) |

## Nguồn dữ liệu

| dữ liệu | nguồn |
|---|---|
| danh sách | HTML server-side, 24 card/trang |
| phân trang | `?page=N` (home 417 trang, `khong-che` 80, `tag/xnxx` 299) |
| tìm kiếm | `/tim-kiem/<q>?page=N` — form `GET /tim-kiem?k=` **chỉ trả trang 1**, phải dùng path |
| thể loại | `/?view=the-loai` → 48 thẻ loại (kèm số phim) |
| từ khoá | nav trang chủ (`xnxx`, `xvideos`) |
| chip popup | khối `Thể loại:` / `Tags:` của trang detail |
| phim liên quan | `GET /ajax/suggestions/<slug>` → `{"data":[...]}` |
| player | 2 nút `set-player-source`: `e.streamforester.name` (StreamQQ) + `vcast.name` (Alias) |

## Sort — KHÔNG có dòng 2 (đo 2026-11-08)

Site không in UI sắp xếp ở bất kỳ trang nào. Tham số ẩn `?sort=` **có** tác dụng nhưng
chỉ là **sắp xếp tăng** của cùng một tập:

| thử | kết quả đo |
|---|---|
| `?page=1` vs trang gốc | trùng khít (baseline = mới nhất) |
| `?sort=views` | tổng trang vẫn 80, nhưng trang 1 toàn phim 2024 (tăng dần) |
| `?sort=likes` / `?sort=created_at` / `?sort=id` | giống nhau từng item |
| `?sort=views&order=desc`, `?sort=-views` | site nuốt im lặng, vẫn tập cũ |

⇒ không có "xem nhiều / mới nhất" thật ⇒ theo `lampac-sort`: **bỏ hẳn dòng 2**, không dựng
dòng chết.

## Thumbnail — ưu tiên thời lượng, thiếu thì 1 nhãn

Thứ tự lấy `time` (3 slot tối đa, không bịa):

1. **duration** nếu site in (`video-item__duration` / `mm:ss`) — site này: **0/24 card**
2. không có duration → **một nhãn ribbon đầu tiên** (`Không Che` / `Trung Quốc` / `Âu - Mỹ`…)
3. không có cả hai → `null`

`quality` = `null` (site không in nhãn chất lượng trên card).

**Chỉ 1 nhãn, không nối.** Site in ribbon xếp chồng (0–2 cái/card); nối 2 nhãn
(`"Không Che · Uncensored Leak"`) thì badge dài tràn hết poster, còn nối bằng ký tự
xuống dòng thì **vô dụng**: `loader.js` set `h.innerText = m` và `.card__quality`
không có `white-space: pre*` → xuống dòng bị gộp thành dấu cách. Đã test bằng Chrome
thật: badge ra `KHÔNG CHE UNCENSORED LEAK` trên 1 dòng, tràn khung.

⇒ Một nhãn = một dòng badge gọn. Nhãn thứ hai (nếu có) đã có ở chip + popup `/genres`.

## Ghi chú vận hành

- `/actresses/<slug>` cũng là listing (24/trang) nên `p/actresses/<slug>` chạy được, nhưng
  **không** đưa vào menu: thư mục diễn viên 81 trang (~1900 người), nhét 24 vào submenu là
  danh sách nửa vời.
- `HttpUtility.UrlEncode` trả `+` cho khoảng trắng; tìm kiếm dùng `Uri.EscapeDataString`
  (`%20`) vì đã đo `/tim-kiem/jav%20hd` trả đúng "Tìm kiếm jav hd".
- `master.m3u8` của wogplayer có `?e=` hết hạn ~1h → cache link 5 phút.
- Referer khi proxy stream phải tách: `vcast.name` → `https://vcast.name/`,
  còn lại → site.
