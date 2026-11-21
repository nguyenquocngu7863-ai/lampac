# FreePornVideos (module `fpv`)

Site: https://www.freepornvideos.xxx — KVS (Kernel Video Sharing), HTML server-side thuần.
Route Lampac: `/fpv`

## Khảo sát (capture 2026-11-21, 18 request)

| thứ | file | kết quả |
|---|---|---|
| 1 | `/vi/` | 223 KB HTML tĩnh, card `.item` parse được ngay → **SIÊU TỐC, không cần Chrome** |
| 2 | `/vi/categories/` | 361 thể loại (`a.item`, chia hàng theo chữ cái) |
| 3 | `/vi/videos/93823263/...` | trang detail có sẵn `<source>` MP4 2160p/720p/480p |
| 4 | `/vi/latest-updates/2/` | phân trang dạng path `/<list>/<N>/`, 6179 trang |
| 5-9 | `/vi/categories/milf/` + sort + p2, `/vi/search/indian/` + sort | sort + phân trang là path |
| 10-12 | `/vi/most-popular/`, `/vi/top-rated/`, `/vi/models/isiah-maxwell/`, `/vi/networks/tushy-com/` | list path dùng chung công thức |
| 13-18 | `/vi/sites/`, `/vi/models/`, sort variants | taxonomy + sort |

## Công thức

```
list  : {host}/vi/{list}/{pg}/                     list = latest-updates | top-rated | most-popular
                                                          | most-popular/today|week|month
sort  : {host}/vi/{path}/{sort}/{pg}/              sort = latest-updates | top-rated | most-relevant
search: {host}/vi/search/{q}/{sort}/{pg}/          q: khoảng trắng -> "-"
detail: {host}/vi/videos/{id}/{slug}/
stream: <source src='.../get_file/.../<id>_2160m.mp4/' label="2160p">
proxy : cần Referer https://www.freepornvideos.xxx/
```

## Sort — đã đo (không đoán)

Trang 1 category `milf`, so bộ id (2026-11-21):

| sort | URL | id đầu tiên |
|---|---|---|
| mặc định | `/vi/categories/milf/` | `93802926` (video_viewed) |
| Mới nhất | `/vi/categories/milf/latest-updates/` | `93823303` (post_date) |
| Đánh giá cao | `/vi/categories/milf/top-rated/` | `93758936` (rating) |
| Liên quan | `/vi/categories/milf/most-relevant/` | `93361026` (ctr) |

4 bản đều khác thứ tự, cùng tập phim ⇒ **sort thật** (không phải filter). Site còn tự công bố
map này trong `videos_by_category_sort` của HTML.

Search cũng sort được: `/vi/search/indian/latest-updates/` và `/top-rated/` khác bản mặc định;
`most-popular` của search **trùng** thứ tự với `top-rated` nên không đưa vào menu.

Ngữ cảnh `most-popular/today|week|month`, `models/*` không sort được ⇒ **ẩn hẳn dòng 2**
(`SortsFor` trả `null`).

## Phân trang

Path + N/ (`/vi/categories/milf/top-rated/2/`, `/vi/search/indian/2/`). Đã kiểm: trang 1 và
trang 2 không trùng item. `total_pages` lấy từ `li.last` trong khối `.pagination`.

## Card thumbnail (3 trường theo site)

| trường | nguồn |
|---|---|
| `name` | `strong.title` |
| `time` | `span.duration` → cắt `mm:ss` / `h:mm:ss` ("Video đầy đủ 29:53" → `29:53`) |
| `quality` | badge `div.k4` → `4K`; không có badge → `null` (không bịa "HD") |

Poster: `img` (data-src hoặc src), preview: `data-preview` (mp4 cast).

**2 bẫy đã sửa (2026-11-21, đo bằng cách so bộ `id` với HTML site):**

1. **Không cắt block 6000 ký tự.** Card có danh sách diễn viên dài ⇒ block > 11 KB và badge
   `div.k4` nằm **cuối** block (`+6353`), cắt ngắn làm mất `quality` của đúng card đó
   (trang `categories/milf`: site 15 card 4K, module chỉ ra 14). Bỏ cắt ⇒ khớp 15/15.
2. **Badge phải có `<span class="icon">`.** Giữa 2 card thật có card quảng cáo
   (`div.item.ourdreamai-native`, chứa `div.k4` **rỗng**) không có `/vi/videos/` nên block của
   card trước "nuốt" luôn nó — nếu chỉ khớp `class="k4"` thì card không có 4K bị gán nhầm `4K`.
   Regex đúng: `class="k4">\s*<span class="icon"`.

Kiểm chứng sau sửa: `/vi/` 24 card (grid `list_videos_most_recent_videos_items`; 12 card sidebar
`videos_watched_right_now` **không** lấy), `sites/ilovepov` 24/24, `search/indian` 11 4K — khớp
100% với HTML site, 0 card thừa/thiếu.

## Playback

Trang detail chứa sẵn 3 nguồn MP4 (2160p / 720p / 480p) — **không cần ajax/embed**.
`/fpv/vidosik` trả `qualitys` để popup chọn chất lượng, Lampac proxy kèm Referer.

Đo 2026-11-21 bằng `ffprobe` qua `/proxy/`: `h264 852x480 + aac`, duration 2357 s, Range 206
`video/mp4` cho cả 3 mức.

## Menu

1. Tìm kiếm
2. `Sắp xếp: <đang mở>` (theo ngữ cảnh; xem bảng trên)
3. Danh sách: Mới nhất / Đánh giá cao / Phổ biến / tuần / hôm nay / tháng
4. Thể loại #–F, G–L, M–R, S–Z — 361 mục lấy động từ `/vi/categories/`, chia theo chữ cái slug
5. Trang web — 107 mục lấy động từ `/vi/sites/` (6 trang, gom song song)

`/vi/models/` có 887 trang nên **không** đưa vào menu; model vẫn vào được từ popup long-press
của từng phim (`c=models/<slug>`).

## Popup long-press

`/fpv/genres?uri=<detail>` → nhóm `Diễn viên` (btn_model), `Kênh` (btn_sponsor), `Network`
(btn_sponsor_group), `Thể loại` (btn_tag) + mục `Video liên quan`
(`?rel=`, lấy sẵn trong trang detail, không fetch thêm).

## Deploy

```bash
~/lampac-native/csc/check.sh ~/lampac-clean/Modules/Adult/FreePornVideos
cp ~/lampac-clean/Modules/Adult/FreePornVideos/{Controller.cs,ModInit.cs,Service.cs,manifest.json} \
   ~/lampac-run/module/Adult/FreePornVideos/
pkill -f "Core\.dll"; sleep 3
rm -rf ~/lampac-run/cache/fdb ~/lampac-run/cache/static   # menu cache 720 phút
nohup bash ~/lampac-native/run-lampac.sh > ~/lampac-native/lampac-native.log 2>&1 &
```
## Smoke test (2026-11-21, sau khi sửa quality)

| route | HTTP | thời gian | nội dung |
|---|---|---|---|
| `/fpv` | 200 | 0.6 s | 24 item, `total_pages=6179`, menu 8 dòng |
| `/fpv?c=categories/milf` | 200 | 0.6 s | 24 item, `total_pages=906`, 15 card 4K = site |
| `/fpv?c=categories/milf&sort=top-rated&pg=2` | 200 | 0.5 s | 24 item, khác trang 1, `tp=906` |
| `/fpv?search=indian&sort=top-rated` | 200 | 0.5 s | 24 item, `total_pages=7`, 11 card 4K = site |
| `/fpv?c=models/emilia-shot` | 200 | 0.5 s | 3 item, **không** có dòng 2 (Đúng: model không sort) |
| `/fpv?c=sites/ilovepov` | 200 | 0.5 s | 24 item, `tp=10`, có dòng 2 |
| `/fpv?c=most-popular/today` | 200 | 0.5 s | 24 item, **không** có dòng 2 |
| `/fpv?rel=<detail>` | 200 | 0.5 s | 11 item, menu 1 dòng (Tìm kiếm) |
| `/fpv/vidosik?uri=<detail>` | 200 | 0.6 s | `qualitys` 2160p / 720p / 480p (không resolve thêm) |
| `/fpv/genres?uri=<detail>` | 200 | 0.6 s | 5 nhóm: Diễn viên, Kênh, Network, Thể loại, Liên quan |
| `/proxy/<480p>` | 302→206 | — | `ffprobe`: h264 852x480 + aac, duration 607 s |
| preview `cast.../preview/<id>.mp4` | 200 | — | `video/mp4` **không cần Referer** (client phát được) |

`check.sh`: 0 error, 20 warning (không có warning nào là lỗi logic).
