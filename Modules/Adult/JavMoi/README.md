# JavMoi — https://z.javmoi.blog

Module Adult cho site JAVMới (JAV + tube tổng hợp). HTML thuần, không Cloudflare, không CSR.

## Host — có 2 tên miền, CÙNG một site

| Host | Ghi chú |
|---|---|
| `z.javmoi.blog` | host trong `manifest.json`; trang chủ `/` phát link `href` **trỏ về chính nó** |
| `javchat.blog` | mirror; `/danh-sach/phim-moi` và mọi link trong trang chi tiết phát **tuyệt đối** sang host này |

Bằng chứng là mirror (không phải site đã chuyển nhà): `md5sum` trang chi tiết
`/phim/dit-co-ban-thoi-tho-au-trong-mot-chuyen-ve-tham-que` trên hai host **trùng khít**
(`6a02f022ba75d7977d0f6a9676ea9fc2`), kích thước 75377B.

⇒ **Parser không được neo host.** Regex chỉ khớp đường dẫn (`/phim/`), host lấy nguyên từ `href`.
Bản cũ neo `javmoi\.` nên trang `/danh-sach/phim-moi` (link `javchat.blog`) ra **0 item → home 503**.
Đo trên capture: regex cũ 0/24, regex mới 24/24.

## Routes

- `/javmoi` — home = `/danh-sach/phim-moi` (24 item/trang, `total_pages` ≈ 974)
- `/javmoi?pg=N` — trang N
- `/javmoi?c=<path>` — path taxonomy, vd `/the-loai/jav-hd`, `/quoc-gia/nhat-ban`, `/tu-khoa/alice-shaku`
- `/javmoi?search=<kw>` — `/tim-kiem?q=`
- `/javmoi/genres?uri=<uri detail>` — popup long-press
- `/javmoi?rel=<uri detail>` — danh sách "Video liên quan" (`total_pages: 1`, `pg=2` rỗng)
- `/javmoi/vidosik?uri=<uri detail>` — link phát
- `/javmoi/video.m3u8?uri=<uri detail>` — proxy HLS

## Taxonomy

Menu lấy từ nav **trang chủ `/`** (cache 6h), không lấy từ trang danh sách: đo 2026-10-03
trang chủ 24 mục, trang danh sách chỉ 23 (thiếu `/the-loai/khong-che` — khối
`home-v2 movie-list-index` / `more-list-index` chỉ render ở trang chủ).

Site **không có `<select>`/dropdown filter** → nav là toàn bộ taxonomy công khai.

## Sort — SITE KHÔNG CÓ SORT

Đã quét `<select>`, `sap-xep`, `sort`, `orderby` trên cả 3 loại trang (home, danh sách, thể loại):
**không có kết quả nào**. Site chỉ có một thứ tự mặc định = mới nhất.

Menu vì vậy chỉ còn `Sắp xếp › Mới nhất` (`/javmoi?c=moi-nhat`, hiện là alias của home).
**Đừng thêm tùy chọn sắp xếp khác** — sẽ là nút chết.

## Popup long-press

Trang chi tiết in khối `<div id="extras">` (mỗi khối 1 nhãn + danh sách `<a href title>`):

```
Diễn viên:   <i class="fa-user-tag">   → nhóm RỖNG trên mọi trang đã đo → phải bị bỏ
Thể loại:    <i class="fa-bars">       → /the-loai/<slug>   (7–8 mục)
Từ khóa:     <i class="fa-tag">        → /tu-khoa/<slug>    (8–9 mục)
```

- Link là **tuyệt đối sang `javchat.blog`** → chỉ lấy `path`, không lấy host.
- `kind` suy từ path: `/the-loai/` → `cat`, `/tu-khoa/` → `tag` (nhãn chỉ để hiển thị).
- Nhóm **rỗng bị bỏ**, thứ tự cố định: Diễn viên → Thể loại → Thẻ → Liên quan.
- "Liên quan" = `1` mục `plain` trỏ `?rel=`.

"Video liên quan" = khối `ul#more-movie` dưới `<h2>` **"Có thể bạn thích?"** (~10 item),
không chứa chính phim đang xem.

## An toàn

- `DetailUrl()` chặn SSRF: chỉ nhận host `javmoi.blog` / `javchat.blog` (kèm subdomain) **và**
  path bắt đầu `/phim/`; sai → trả shape rỗng, không ném lỗi.
- Cache chip + related trong `Dictionary` tĩnh (TTL 6h, cap 300 entry). Không dùng
  `hybridCache` vì `ValueTuple` không round-trip được.

## Verify 2026-11-23 (app thật, `127.0.0.1:9118`)

Route **không** có tiền tố `/lite` (`/lite/javmoi` → 404).

| Endpoint | Kết quả |
|---|---|
| `/javmoi` | 24 item, `tp=974` |
| `/javmoi?pg=2` | 24 item, khác trang 1 |
| `?c=/the-loai/sex-vung-trom` | 24 item, `tp=82` |
| `?c=/tu-khoa/alice-shaku` | 2 item |
| `?c=moi-nhat` / `/the-loai/jav` / `/quoc-gia/nhat-ban` | 24 / 24 `tp=354` / 24 `tp=836` |
| `genres` (3 phim khác nhau) | `Thể loại` 7–8 · `Thẻ` 8–9 · `Liên quan` 1 |
| `?rel=` | 10 item, `tp=1` |
| `?rel=&pg=2` | rỗng, `tp=1` ✅ |
| `genres?uri=` host lạ / uri rác | `{"items":[],"groups":[]}` HTTP 200 |
| `?rel=` host lạ | `{"total_pages":1,"list":[]}` HTTP 200 |

- `check.sh`: **0 error** (33 warning, toàn `CS8603/CS8625/CS8600` nullable — cùng loại với code cũ)
- Stream: playlist **1426 segment**; segment qua `/proxy/<hash>` là **TS**, `ffprobe` ra
  `h264 1280x720 + aac`, 5.07s.
- `play.sh`: **PASS** — `hls=true`, `cold/warm 0.77/0.63s`, segment 1426/1426 qua Lampac,
  `h264 1280x720 · 7173.8s`, decode 5s đầu ✔ / 5s cuối ✔, không cần proxy.
  *(Lần đo đầu báo FAIL vì `python3` trong PATH trỏ venv thiếu `curl_cffi` — lỗi môi trường,
  không phải stream. Chạy lại với `PATH=/data/data/com.termux/files/usr/bin:$PATH`.)*
  Lưu ý: `ffprobe` **thẳng vào URL playlist** vẫn báo `not in allowed_segment_extensions` do
  segment qua `/proxy/<hash>` không đuôi — đó là giới hạn của ffprobe, không phải của route.
