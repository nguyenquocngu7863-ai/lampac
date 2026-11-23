# KawaiJAV — https://kawai-jav.com

Module SISI cho Kawai JAV (censored + uncensored leaked). HTML thuần, không Cloudflare.

## Tính năng

- **Bảng xếp hạng**: phổ biến (mặc định) · đánh giá cao — chọn bằng **path**, không phải query
- **Thể loại**: studio (Prestige, S1, Moodyz, SOD Stars, …) 48 mục từ menu
- **Thẻ (Tags)**: từ trang chi tiết + tag cloud cuối trang
- **Tìm kiếm**: `?keyword=`
- **Popup long-press**: kênh · thể loại · thẻ · video liên quan
- **Xem phim**: HD/SD qua stream proxy

## Sort — SITE KHÔNG CÓ `?sort=` (đo 2026-11-23)

Quét `<select>`, `sort=`, `orderby` trên home · detail · category · most-view · top-rate:
**0 kết quả**. Sắp xếp là **bản thân đường dẫn**:

| Sort | Path | Item |
|---|---|---|
| `views` (mặc định) | `/en/most-view.html` | 38 |
| `top-rated` | `/en/top-rate.html` | 38 |

- `/en/premium-hd.html` **không phải danh sách** — 0 link `/en/watch/`, là trang nâng cấp
  (login/signup) ⇒ **không** đưa vào menu sort.
- `/en/video.html` → **404**. Trang chủ `/` là trang tổng hợp **không có pagination**
  ⇒ không có bảng "mới nhất" phân trang; mặc định dùng `most-view`.

Code cũ nối `&sort=views` vào URL → site bỏ qua, mọi lựa chọn sort trả về cùng một trang.

## Search — `?keyword=`, KHÔNG phải `?q=`

Form site: `<form action="/en/search.html" method="get">` + `<input name="keyword">`.
`?q=IPX` bị site **bỏ qua** và trả về nguyên trang chủ (375 KB, 230 link).
Code cũ dùng `?q=` ⇒ tìm kiếm luôn ra danh sách mặc định.

## Route

| Route | Việc |
|---|---|
| `GET /kawaijav` | danh sách (38 item/trang, `total_pages` 5) |
| `GET /kawaijav?pg=N` | trang N |
| `GET /kawaijav?sort=views\|top-rated` | chọn bảng xếp hạng |
| `GET /kawaijav?c=categories/<id>.<slug>.html` | theo studio |
| `GET /kawaijav?c=tag/<keyword>` | theo thẻ → `/en/search.html?keyword=` |
| `GET /kawaijav?search=<kw>` | tìm kiếm → `/en/search.html?keyword=` |
| `GET /kawaijav/genres?uri=<watch url>` | popup long-press |
| `GET /kawaijav?rel=<watch url>` | "Related Videos" (`total_pages: 1`, `pg=2` rỗng) |
| `GET /kawaijav/vidosik?uri=` | `{"HD": url, "SD": url}` |
| `GET /kawaijav/video?uri=&q=hd\|sd` | 302 → `/proxy/<token>.mp4` |

## Popup long-press

Trang chi tiết **không có** khối diễn viên riêng. Trong tab thông tin, giữa `Channel:` và
`Added on:` có 3 nhóm:

| Nhãn | Dạng link | kind |
|---|---|---|
| `Channel:` | `/en/channel/<id>/<slug>` | `channel` → "Kênh" |
| `Tags:` | `/en/search.html?keyword=<kw>` | `tag` → "Thẻ" |
| `Categories:` | `/en/categories/<id>.<slug>.html` | `cat` → "Thể loại" |

Phải **cắt đúng đoạn** `Channel:` → `Added on:`: nav mega-menu cũng chứa 48 link
`/en/categories/` và 24 link `/en/pornstars/`, nếu parse cả trang sẽ ra toàn bộ danh mục
thay vì của phim.

"Related Videos" nằm sau `<h2>Related Videos</h2>`. **Trước heading có 2 link `/en/watch/`**
(share/self) ⇒ phải cắt từ heading, nếu không phim đang xem sẽ lẫn vào danh sách.

Nhóm rỗng bị bỏ; thứ tự cố định: Diễn viên → Kênh → Thể loại → Thẻ → Liên quan.

## Bẫy khác

- **Tag có tiền tố số** (`118_Minami Aozora`, `26_japanese av`) là artifact của site; một số
  không có kết quả khi search. Tag không tiền tố (`uncensored japanese` → 24 item,
  `SOD Stars` → 12 item) vẫn bình thường ⇒ giữ nguyên text của site, không tự cắt.
- `SafeDetailUrl()` chặn SSRF: chỉ host `kawai-jav.com` + path `/en/watch/`; popup luôn trả
  shape rỗng HTTP 200 chứ không 500.
- Cache chip+rel trong `Dictionary` tĩnh (TTL 6h, cap 300) — `ValueTuple` không round-trip
  qua `hybridCache`.

## Verify 2026-11-23 (app thật, `127.0.0.1:9118`)

| Endpoint | Kết quả |
|---|---|
| `/kawaijav` | 38 item, `tp=5` |
| `?pg=2` | 38 item, khác trang 1 |
| `?sort=views` | 38 item (IPX-922 …) |
| `?sort=top-rated` | 38 item (Caribbeancom …) — khác mặc định ✔ |
| `?search=IPX` | 13 item |
| `?c=tag/IPX` | 13 item |
| `?c=categories/17.sod-stars.html` | 17 item |
| `?c=/en/channel/37/japanese-av` | 36 item, `tp=5` |
| `genres` | Kênh 1 · Thể loại 1 · Thẻ 2 · Liên quan 1 |
| `?rel=` | 8 item, `tp=1` |
| `?rel=&pg=2` | rỗng, `tp=1` ✔ |
| SSRF (host lạ / uri rác / path sai) | shape rỗng HTTP 200 ✔ |
| `play.sh` | **PASS** — mp4 358 MB `video/mp4`, ffmpeg decode OK |

- `check.sh`: **0 error** (50 warning, toàn `CS8603/CS8625/CS8604/CS8600/CS8619/CS8618` nullable)
- HD (1.59 GB, h264 1280x720, 7363s) tải được qua `/proxy/`; `play.sh` đứt ở lần đầu do file quá
  lớn (cold start), chạy lại với SD thì PASS.

## Ghi chú vận hành

- Yêu cầu `streamproxy = true`, `httpversion = 1`, timeout 25s.
- Dữ liệu site thưa: nhiều studio/thẻ chỉ có vài phim.