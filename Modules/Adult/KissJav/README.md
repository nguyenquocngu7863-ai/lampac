# KissJAV

Module Lampac cho `https://kissjav.li` (Korean / JAV tube).
Đo & viết 2026-11-24 trên site thật (chỉ 2 request: home + 1 detail).

## Route

| route | việc |
|---|---|
| `kissjav` | danh sách mới nhất (`/latest-updates/`) |
| `kissjav?c=<path>&pg=N` | danh mục / listing (`c` = path dưới host) |
| `kissjav?sort=<sort_by>` | sắp xếp |
| `kissjav?search=<kw>` | tìm kiếm (`/search/?q=`) |
| `kissjav/vidosik?uri=<detailUrl>` | trả `MP4` (json) |
| `kissjav/video.mp4?uri=<detailUrl>` | resolve + proxy MP4 |
| `kissjav/genres?uri=<detailUrl>` | popup long-press (Thể loại riêng của phim + Liên quan) |
| `kissjav?rel=<detailUrl>` | danh sách "Liên quan" |

`c`: `latest-updates`, `most-popular`, `categories/<slug>` (vd `categories/korean-porn`).
`sort`: rỗng (mới nhất) · `video_viewed` · `rating` · `duration` · `most_commented` · `most_favourited`.
`pg` trong URL site là **segment**: `/latest-updates/2/`, `/categories/jav/3/`.

## Cấu trúc site

- list:   `/latest-updates/[pg]/`, `/most-popular/[pg]/?sort_by=…`
- cat:    `/categories/<slug>/[pg]/`
- search: `/search/?q=<kw>` (form `#search_form`, field `q`)
- detail: `/video/<id>/<slug>/`
- stream: `flashvars.video_url` = **base64**(`https://kissjav.li/get_file/11/…/<id>.mp4/?br=1014`)
  - `video_url_hd` = `MQ==` (= `"1"`) → **sentinel**, không phải URL → bỏ qua.
  - `postfix: '.mp4'` → MP4 progressive (35 MB / 273 s phim mẫu), không phải HLS.

Card (list **và** related dùng chung markup):

```
<div class="thumb thumb_rel item">
  <a href="…/video/<id>/<slug>/" title="TÊN">
    <img class="lazy-load" data-original="//assets6.cdnhop.com/…/320x180/1.jpg">
    <div class="item-bottom"><div class="time">0:23</div></div>
```

→ `title` (tên), `data-original` (thumb), `div.time` (thời lượng), `href` (detail).
Trang list **30 phim/trang**; `total_pages` đo từ link `/<listing>/<N>/` trong pagination (home = 9).

## Popup (long-press)

- **Thể loại**: chỉ lấy thẻ **riêng của phim** — `<a class="btn gold button" href="/categories/<slug>/">`.
  KHÔNG dùng link `/categories/…` ở nav header (nav có 8 danh mục chung → chip sai phim).
- **Liên quan**: khối `#list_videos_related_videos_items` trên chính trang detail (6 phim mẫu).
- Site **không có** tag/model link (flashvars `video_tags` thường rỗng) → không có nhóm Diễn viên/Thẻ.
- SSRF: `uri` chỉ nhận `/video/<id>/<slug>` (bắt buộc host `kissjav.li`), sai → `{"items":[],"groups":[]}`.
- `rel` với `pg>1` → list rỗng (giữ popup sống, không 503).

## Menu (tiếng Việt/Anh)

`Tìm kiếm` · `Danh mục` (parse nav site, fallback 10 danh mục) · `Sắp xếp` (6 kiểu).

## Đo (2026-11-24)

| mục | kết quả |
|---|---|
| `/kissjav` | 200, 30 item, total_pages 9 |
| `?pg=2` | 30 item, tên khác trang 1 |
| `?c=categories/jav` | 30 item |
| `?sort=video_viewed` | thứ tự khác hẳn mặc định |
| `?search=korean` | 20 item |
| `/genres` | Thể loại 2 (KOREAN PORN, vip), Liên quan 6 |
| `/genres` host lạ | `{"items":[],"groups":[]}` |
| stream | `206`, `video/mp4`, `ftypisom`, 35.453.233 B, `Accept-Ranges: bytes` |
| `play.sh` | **PASS ~90%** — h264 720x1252, 273.2 s, decode 5 s đầu + 5 s cuối OK |

Lưu ý: route video phải **có đuôi `.mp4`** (app chọn player theo đuôi URL).

## Ghi chú

- Site có Cloudflare nhưng cho qua `curl_cffi` chrome124; `HttpClient` trong app cũng qua được.
- Referer khi tải stream: `https://kissjav.li/` (đặt trong `headers_stream` của ModInit).
- Nguồn gốc: **module tự làm** (không phải SISI, không phải NextHUB) — xem `Modules/Adult/MODULE-ORIGIN.md`.
