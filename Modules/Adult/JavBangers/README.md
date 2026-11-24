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
| `javbangers/vidosik?uri=<videoUrl>` | trả **danh sách chất lượng**: 1080p · 720p · 480p |
| `javbangers/video.mp4?uri=<videoUrl>&q=<label>` | resolve + proxy MP4 (mặc định **cao nhất**) |
| `javbangers/genres?uri=<videoUrl>` | popup long-press (Người đăng · Thể loại · Diễn viên · Liên quan) |
| `javbangers?rel=<videoUrl>` | danh sách "Liên quan" |

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

## Player — trang detail CHỈ 200 KHI **KHÔNG có `/` cuối**

`/video/117657/japornxxx-…` → **200**; `/video/117657/japornxxx-…/` (thêm `/`) → **404**.
(Lần đầu đo sai vì luôn thêm `/`; sửa 2026-11-24 theo user.)

Trang detail có sẵn mọi thứ trong 1 request:

```
<div class="block-user"><div class="username"><a href="/members/8/"> tarpi </a></div>
flashvars: video_categories: 'Blowjob, Cumshot, Hardcore'
           video_tags: 'Japornxxx, -31, Jun, Sakura, Sex, Cyborg-Blowjob'
           video_models: ''
           video_url: 'https://www.javbangers.com/get_file/5/…/<id>.mp4/?v-acctoken=…'
<div class="related-videos" id="list_videos_related_videos"> … 10 card video-item
```

### Chất lượng — 3 mức từ flashvars (KHÔNG mặc định 480p)

| flashvar | nhãn | file |
|---|---|---|
| `video_url` (+`video_url_text`) | 480p | `…/<id>.mp4` |
| `video_alt_url` (+`_text`) | 720p | `…/<id>_720p.mp4` |
| `video_alt_url2` (+`_text`) | **1080p** | `…/<id>_1080p.mp4` |

- `vidosik` trả **dict nhãn → route** (`1080p`/`720p`/`480p`, sắp cao→thấp); app hiện menu chọn chất lượng.
- `video.mp4?q=<nhãn>`; **không có `q` → chất lượng cao nhất**. Chỉ nhận URL có `/get_file/`
  (bỏ `video_alt_url*` trỏ về trang detail).
- Đo thật: **video 412809 → 1080p h264 1920x1080** (1.843.665.444 B), 720p 936.340.126 B (đúng gấp đôi);
  video 117657 → site khai 1080p nhưng **trả cùng file 720p** (130.023.485 B) — lỗi của site, không phải module
  (vẫn hiện đủ 3 mức).
- `ResolveStreams` cache 120 s theo id; fallback `/embed/<id>` nếu detail fail (embed chỉ có 480p/720p).

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

| nhóm | nguồn trên trang detail | ví dụ (video 117657) |
|---|---|---|
| `Người đăng` | `div.block-user .username a[href=/members/<id>/]` | **tarpi** → `?c=members/8` |
| `Thể loại` | `flashvars.video_categories` → index `/categories/` | Blowjob · Cumshot · Hardcore |
| `Diễn viên` | `flashvars.video_models` → index `/models/` | (rỗng với phần lớn video) |
| `Liên quan` | `div.related-videos` cùng trang (`plain:true` → `?rel=`) | 10 phim |

- **User thật**: `/members/<id>/` mở được **không cần login** (vd `/members/8/` = "tarpi", 5 video/trang);
  hỗ trợ qua `?c=members/<id>`. Member page **phân trang bằng AJAX** (`/members/8/2/` = 404) ⇒ module
  chỉ trả **trang 1** (`total_pages: 1`, `pg>1` ép về 1).
- **Tags**: `video_tags` chỉ là chữ; site **không có trang tag** — `/tags/` = **502**, `/tags/<slug>/` = 404,
  sitemap không có `type=tags` ⇒ **không có nhóm Thẻ**.
- SSRF: `uri` chỉ nhận `/video/<id>` trên `*.javbangers.com`; sai → `{"items":[],"groups":[]}`.

## Bẫy đã dính

1. **Detail 404**: đừng cố fetch `/video/...` — lấy mọi thứ từ `/embed/<id>`.
2. **Detail 404 nếu có `/` cuối** — luôn fetch đúng href của card (không trailing slash).
3. **`&page=N` và `/search/<kw>/2/` đều không có tác dụng** (404 / trả trang 1); ajax
   `?action=get_block&block_id=list_videos_videos_list_search_result&from_videos=N` cũng trả y trang 1
   ⇒ search **chỉ 1 trang** (`total_pages = 1`).
4. `video_alt_url` (720p) là **redirect** về trang detail → không dùng trực tiếp.
5. Mỗi card có **2 link** cùng video → dedupe theo id, nếu không list bị nhân đôi.
6. `total_pages` phải đọc từ link của **chính context** (`/<c>/<N>/`); nếu quét chung `from:` sẽ ra số rác
   (member/8 từng trả `8447` thay vì `150`).

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
| `?c=members/8` (user tarpi) | 5 item · tp 1 · không có dòng 2 (site bỏ qua sort) |
| `/genres` video 117657 | Người đăng **tarpi** · Thể loại 3 · Liên quan |
| `?rel=…` | 10 item · tp 1 · `pg=2` → rỗng |
| `?c=top-rated` / `?c=most-popular` | 24 item · tp 7539 |
| `?search=japan` / `&sort=duration` | 71 / 73 item · tp 1 |
| `/genres` video 412813 | Thể loại 3 (Creampie·Censored·Hardcore) |
| `/genres` host lạ | `{"items":[],"groups":[]}` |
| `play.sh` | **PASS** — h264 854x480 · 5675.2s · 206 · 761.645.181 B |
