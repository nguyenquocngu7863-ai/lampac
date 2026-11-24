# JavBangers

Module Lampac cho `https://www.javbangers.com` (JAV HD tube, engine KVS/Porntrex — theme "javwhores").
Đo & viết 2026-11-24. **Site NẶNG sort nhất từ trước tới giờ** — mỗi context đo riêng (xem bảng bên dưới).

## Route

| route | việc |
|---|---|
| `javbangers` | danh sách mới nhất (`/latest-updates/`) |
| `javbangers?c=<path>&pg=N` | listing / category / model / member |
| `javbangers?sort=<sort_by>` | **SORT** — chỉ sống ở context đã đo |
| `javbangers?search=<kw>` | tìm kiếm (`/search/?q=`) |
| `javbangers/vidosik?uri=<videoUrl>` | trả `MP4` (json) |
| `javbangers/video.mp4?uri=<videoUrl>` | resolve + proxy MP4 |
| `javbangers/genres?uri=<videoUrl>` | popup long-press (Thể loại + Diễn viên) |

`c`: `latest-updates` (mặc định) · `top-rated` · `most-popular` ·
`categories/<slug>` · `models/<slug>` · `members/<id>`.

## Cấu trúc site

- list:    `/latest-updates/[pg]/` (tp 6011) · `/top-rated/` (7539) · `/most-popular/`
- cat:     `/categories/<slug>/[pg]/` (31 category, index `/categories/`)
- model:   `/models/<slug>/[pg]/` (index `/models/`)
- member:  `/members/<id>/[pg]/` (index `/members/` — Community, real user, vd `/members/13/` = "LEGO's Page")
- search:  `/search/?q=<kw>` — **chỉ trang 1** (xem bẫy bên dưới)
- pagination: ajax `from:<N>`; URL dạng `/<path>/<N>/`

Card:

```
<div class="video-item">
  <a class="thumb" href="…/video/<id>/<slug>" title="TÊN"> <img class="cover lazy-load" data-original="…">
  <div class="durations"><i class="fa fa-clock-o"></i> 34:09</div>
  <div class="viewsthumb">12 453 views</div>
```

→ dedupe theo **id** (mỗi card có 2 link cùng video), `time` = duration.

## Player — **trang detail trả 404 cho guest**

`GET /video/<id>/…` → **404** (đo bằng cả curl_cffi và Chrome thật, có cookie) ⇒ **không dùng được detail**.
Lấy stream từ **`GET /embed/<id>`** (200):

```
flashvars: video_id, video_title,
           video_categories: 'Creampie, Censored, Hardcore',
           video_models: '',  video_tags: 'Hardcore, Blowjob, pov',
           video_url: 'https://www.javbangers.com/get_file/4/…/<id>.mp4/?v-acctoken=…&embed=true',
           video_alt_url: '…/video/<id>/<slug>'   ← 720p nhưng là REDIRECT về trang detail 404
```

⇒ chỉ có **MP4 480p** (đo được: h264 854x480, 5675s, 761.645.181 B). Route phải có đuôi `.mp4`.

## SORT — bảng đo (skill `lampac-sort`)

Đo bằng **sequence id + sha1 30 video**, kiểm chứng thêm bằng chính dữ liệu trên card (duration/views/rating):

| context | base | `?sort_by=duration` | `?sort_by=video_viewed` | `?sort_by=rating` | kết luận |
|---|---|---|---|---|---|
| `/latest-updates/` | `f08c292c` | `f08c292c` | `f08c292c` | `f08c292c` | **NO-OP → không có dòng 2** |
| `/members/13/` | `0c741a14` | `0c741a14` | — | — | **NO-OP → không có dòng 2** |
| `/categories/creampie/` | `4b5a2d07` | `73f82883` | đổi (views giảm dần: 566 131 > 293 693 > 247 065 …) | đổi (100% > …) | **SORT thật** |
| `/models/yi-yuna/` | `9fb20b0b` | `2edcf9a6` (29:14 > 22:39 > 19:38 > 19:32) | — | — | **SORT thật** |
| `/search/?q=japan` | `535ab69b` | `8a32933d` | — | — | **SORT thật** |

Bằng chứng "sort thật" = **thứ tự property đúng chiều**: category + duration cho 740:35 > 506:56 > 482:09 >
458:12 (>400 phút), trang 2 tiếp tục 305:34 > 303:58; model + duration 29:14 > 22:39 > 19:38.

Whitelist = `""` (Mới nhất) · `video_viewed` (Xem nhiều) · `rating` (Đánh giá cao) · `duration` (Dài nhất);
`ClampSort` chạy **trước** cache key; sort lạ → Mới nhất (không 503).
`?latest-updates/?sort_by=most_favourited` cũng no-op → không có trong whitelist.

## Menu (tiếng Việt/Anh)

| dòng | nội dung | ghi chú |
|---|---|---|
| 1 | `Tìm kiếm` | root module |
| 2 | `Sắp xếp: <đang mở>` | **chỉ hiện** ở category / model / search; giữ `c`\|`search`, về trang 1 |
| 3 | `Danh sách` (Mới nhất · Top đánh giá · Xem nhiều) | **listing = đổi TẬP phim**, không phải sort |
| 4 | `Thể loại` (31, parse `/categories/`, cache 12h) | taxonomy |

Home / member: **không có dòng 2** (site bỏ qua `sort_by` — đo).
Search: **không có dòng 3** (search lọc theo keyword).

## Popup (long-press)

Lấy từ `/embed/<id>` (vì detail 404), map tên → link qua index `/categories/` và `/models/` (cache 12h):

| nhóm | nguồn | ví dụ |
|---|---|---|
| `Thể loại` | `video_categories` → index `/categories/` | Creampie · Censored · Hardcore |
| `Diễn viên` | `video_models` → index `/models/` | (rỗng với phần lớn video) |

- **Không có nhóm "Người đăng"**: trang detail (nơi có link uploader) **404**; `/embed/` không chứa
  uploader (acctoken `1048|260146|0|…` giống nhau ở mọi video = session, không phải user).
  Member page có thật (`/members/13/`) nhưng phải **biết id** → hỗ trợ qua `?c=members/<id>`.
- **Không có nhóm "Thẻ/tags"**: site **không có trang tag** — `/tags/` trả **502**, `/tags/<slug>/` 404,
  `sitemap.xml?type=other` không có mục tags (chỉ latest-updates, top-rated, most-popular, categories,
  models, channels, playlists, albums). Tag chỉ tồn tại dạng chữ trong `video_tags`.
- **Không có "Liên quan"**: không có trang detail ⇒ không có block related.
- SSRF: `uri` chỉ nhận `/video/<id>` trên `*.javbangers.com`; sai → `{"items":[],"groups":[]}`.

## Bẫy đã dính

1. **Detail 404**: đừng cố fetch `/video/...` — lấy mọi thứ từ `/embed/<id>`.
2. **`&page=N` và `/search/<kw>/2/` đều không có tác dụng** (404 / trả trang 1); ajax
   `?action=get_block&block_id=list_videos_videos_list_search_result&from_videos=N` cũng trả y trang 1
   ⇒ search **chỉ 1 trang** (`total_pages = 1`).
3. `video_alt_url` (720p) là **redirect** về trang detail 404 → không dùng.
4. Mỗi card có **2 link** cùng video → dedupe theo id, nếu không list bị nhân đôi.

## Đo trên app (2026-11-24)

| endpoint | kết quả |
|---|---|
| `/javbangers` | 200 · 30 item · tp 6011 |
| `?pg=2` | 30 item khác trang 1 |
| `?c=categories/creampie` | 24 item · tp 1291 |
| `?c=categories/creampie&sort=duration` | 24 item (740:35 > 506:56 > 482:09) · tp 1295 |
| `?c=categories/creampie&sort=duration&pg=2` | 305:34 > 303:58 (pagination chạy dưới sort) |
| `?c=categories/creampie&sort=video_viewed` / `=rating` | đổi thứ tự |
| `?c=models/yi-yuna` / `&sort=duration` | 24 item · 29:14 > 22:39 |
| `?c=members/13` | 5 item · không có dòng 2 |
| `?c=top-rated` / `?c=most-popular` | 24 item · tp 7539 |
| `?search=japan` / `&sort=duration` | 71 / 73 item · tp 1 |
| `/genres` | Thể loại 3 (Creampie·Censored·Hardcore) |
| `/genres` host lạ | `{"items":[],"groups":[]}` |
| `play.sh` | **PASS** — h264 854x480 · 5675.2s · 206 · 761.645.181 B |
