# FapClub — https://xxx.fapclub.vip

Module Adult cho site FapClub (tube phương Tây). HTML thuần, không Cloudflare, không CSR.

## Routes
- `/fapclub` — home (30 item/trang)
- `/fapclub?pg=N` — `/latest-updates/N/`
- `/fapclub?c=<slug>` — `/categories/<slug>/[N/]` (50 category, lấy menu từ home)
- `/fapclub?search=<kw>` — `/search/?q=<kw>`, trang `/search/N/?q=<kw>`
- `/fapclub/menu` — menu: "Danh sách" (3 list) + category (2 nhóm × 25)
- `/fapclub?list=<slug>` — `latest-updates` (mặc định) · `top-rated` · `most-popular`
- `/fapclub/vidosik?uri=/videos/<id>/<slug>/` — stream
- `/fapclub/genres?uri=<uri detail>` — popup long-press: chip `Thể loại:` của **chính video**
  (khối `<div class="vcatsp">`, 3–4 category thật) + 1 mục `plain` "Video liên quan" trỏ `?rel=`
- `/fapclub?rel=<uri detail>` — danh sách "Video liên quan" (18 item, `total_pages: 1`)

## Player (khó nhất)
HTML không chứa URL stream. Phải giải mã JS `hplayer1/jwplayer.js` + `jss/KernelTeamp.js`
(obfuscated array-rotation) để lấy công thức:

```
https://{data-n}.vstor.top/whpvid/{ts}/{token}/{folder}/{id}/{id}_{quality}.mp4
folder = floor(id/1000)*1000
```

`data-n`, `data-id`, `data-q` lấy từ `<div id="player">`. `data-q` là danh sách
`quality;hash;label;size;ts;token` phân bằng dấu phẩy — mỗi quality một token riêng.
Sonnet 4.5 bó tay chỗ này (không suy ra được từ HTML); giải bằng deobfuscate offline.

## Card
`name`/`time` có thật từ HTML (`.time` = 34:57). `quality` site không ghi badge → null.
Thumbnail `i.fapclub.vip` có rotation (onmouseover) nhưng dùng `src` tĩnh là đủ.

## Verify 2026-11-22
- check.sh: 0 error (17 warning)
- smoke 5 endpoint: 200, 30 item mỗi route, first-item khác nhau
- vidosik: 4 quality 1080p/720p/480p/240p + proxy
- `play.sh`: **PASS** — h264 1920x1080, 1823.9s, decode 5s đầu/cuối, cold/warm 0.39/0.37s

## Menu & Sort — 2026-11-22
**Menu phải có trong MỌI response playlist** (sisi.js đọc `json.menu`); bản cũ trả `menu:[]`
nên bấm vào module không ra dòng nào. Thêm `MenuAsync` vào `Index` + `RelatedList`.
URL trong `submenu` phải **tuyệt đối** — `sisi.js` push thẳng `playlist_url` vào
`Lampa.Activity`, URL tương đối fetch sai origin.

```
dòng 1  Tìm kiếm            search_on
dòng 2  —                   KHÔNG CÓ (site không sort thật)
dòng 3  Danh sách: <đang mở>  ?list=latest-updates | top-rated | most-popular
dòng 4  Thể loại            ?c=<slug>  (50 category, cache 12h)
```

**Sort: site KHÔNG có** — bằng chứng đo 2026-11-22 trên `/categories/brunette-porn/`:
`?sort_by=post_date_desc|post_date_asc|video_viewed_desc|video_viewed_asc|rating_desc|
duration_desc|video_viewed_week_desc` → **30/30 id y hệt baseline, đúng thứ tự**; trang cũng
không có `<select>`, không có chữ `sort`/`order_by`. Theo luật "site không có sort thì bỏ hẳn dòng 2".
`top-rated` / `most-popular` là **dòng 3 "Danh sách"** (đổi tập phim: 3 tập giao nhau 0/30).

`list` lạ + `c`/`search` → `ClampList` trả `null` (không để lọt vào Uri/cache key).
## Popup (long-press) — 2026-11-22
- Chip thể loại lấy **động từ trang detail** (`div.vcatsp`), KHÔNG dùng list 50 category ở home
  (bản cũ hiện nhầm 50 thể loại của site, không liên quan video).
- "Liên quan" = 1 item `plain` trỏ `?rel=`, không nhồi 20 phim vào popup.
- `?rel=` trả `total_pages: 1` (tránh app nạp lặp vô hạn), `pg>1` → list rỗng.
- 1 fetch detail duy nhất (bản cũ fetch thêm home), cache 360 phút; warm ~5 ms.
- Video không có khối `vcatsp` (vd `/videos/4234/73229/`) → popup chỉ còn nhóm "Liên quan".

## Đã làm
- homepage, category, search, phân trang, menu, stream, popup `/genres` + `?rel=`
