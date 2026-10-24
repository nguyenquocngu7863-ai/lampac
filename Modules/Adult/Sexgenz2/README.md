# Sexgenz2 — x.sexgenz.blog

Module SISI gọn nhất trong repo: **2 route**, không có `video.m3u8` (xem skill `lampac-adult-module` §5b).

## Cấu hình

| | |
|---|---|
| Site | `https://x.sexgenz.blog` |
| Route | `sgz2` (tên route không trùng tên module — không sao) |
| `displayindex` | `10` (đổi được trong `init.conf`: `"Sexgenz2": { "displayindex": 5 }`) |
| `streamproxy` | `true` — bắt buộc, xem "Segment PNG" bên dưới |
| Cache | list 10 phút, view 20 phút (`sexgenz2:v3:*`) |

## URL của site

| Mục | Dạng |
|---|---|
| Home | `/danh-sach/phim-moi` |
| Thể loại | `/the-loai/<slug>` |
| Search | `/?search=<q>` ⚠️ **không phải `?s=`** (WordPress nhưng theme đổi tên tham số) |
| Phân trang | `?page=N` (query, không phải `/page/N/`) |

## Parser

**List** — tách theo `div class="flw-item"`, mỗi card:

```html
<div class="flw-item">
  <div class="film-poster">
    <div class="pick sub">Việt Nam</div>
    <div class="pick">720p</div>
    <img data-original="/storage/images/<slug>/thumb.webp" ...>
    <a href="https://x.sexgenz.blog/phim/<slug>" class="film-poster-ahref">   <!-- KHÔNG có title -->
  <div class="film-detail"><h3 class="film-name"><a href="..." title="Tên phim">
```

- Lấy `title` ở thẻ `a` **của `h3.film-name`** (thẻ `a` poster không có `title`).
- Poster: `data-original` là path tương đối → prepend host.
- Quality: `div class="pick"` (thẻ `pick sub` là quốc gia, đừng nhầm).

**Detail** — mỗi phim chỉ có **1 server**, nằm trong `li.pu-link`:

```html
<li class="pu-link" data-id="29469"
    data-link="/storage/m3u8/<slug>/index.m3u8" data-type="m3u8">
```

- Regex đúng: `data-link="(/storage/m3u8/[^"]*\.m3u8[^"]*)"`.
- ⚠️ **Đừng** tìm `data-type="embed"` — site đã bỏ embed/Blogger, giờ chỉ có `m3u8`.
- Không cần Blogger/googlevideo; bỏ hẳn nhánh đó.

## Stream

- `index.m3u8` tự host, mở công khai (200 `application/vnd.apple.mpegurl`), không cần Referer.
- Segment trỏ sang CDN TikTok: `p16-oec-sg.ibyteimg.com/obj/tos-alisg-i-...`.
- **Segment PNG**: mỗi segment là **MPEG-TS bọc 90 byte header PNG** (`\x89PNG…IEND` rồi mới tới `0x47` sync, sync đúng mỗi 188 byte).
  - `curl`/`file`/`ffprobe` sẽ báo `image/png` — **không phải file rác**, đừng kết luận hỏng (skill §1c).
  - hls.js tự dò sync byte nên vẫn phát; player native Android thì **không** → phải để `streamproxy = true` cho Lampac chuyển tiếp.
- `OnResult(StreamItem)` tự bọc `HostStreamProxy` → client nhận `/proxy/<token>.m3u8`, không cần route video riêng.

## Nhánh dự phòng

- `StreamLinks` thử `data-link`, rồi fallback regex `https?://…\.m3u8` bất kỳ trong HTML.
- `GetCategories()` lấy động từ home (`/the-loai/…`), cache 12 giờ; lỗi mạng → danh sách 15 thể loại hardcode.

## Đã kiểm chứng (2026-10-24)

```bash
~/lampac-native/csc/check.sh ~/lampac/Modules/Adult/Sexgenz2   # 0 error, 8 warning nullable
curl -s http://127.0.0.1:9118/sgz2                             # 24 item
curl -s 'http://127.0.0.1:9118/sgz2/vidosik?uri=…'             # {"qualitys":{"720p":"http://127.0.0.1:9118/proxy/…m3u8"}}
```
