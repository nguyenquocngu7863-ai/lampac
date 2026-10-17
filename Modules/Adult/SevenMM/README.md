# 7MMTV (SevenMM)

Module Lampac cho `https://7mmtv.sx` (JAV online).
Công thức giải mã player: skill **`lampac-deobfuscate` F19** (không chép lại ở đây).

## Route

| route | việc |
|---|---|
| `sevenmm?c=<path>&pg=N` | danh sách (mặc định `censored_list/all`) |
| `sevenmm?search=<kw>` | tìm kiếm (POST `searchform_search`) |
| `sevenmm/vidosik?uri=<detailUrl>` | liệt kê server (mặc định `json=true`) |
| `sevenmm/video.m3u8?uri=<detailUrl>` | resolve + proxy stream |

`c` là path tương đối dưới `/en/`, bỏ `/<pg>.html`:
`censored_list/all`, `uncensored_makersr/37/FC2`, `censored_category/1/Variety` …

## Cấu trúc site

- list:   `/en/{prefix}_list/all/{pg}.html`
- genre:  `/en/{prefix}_category/{id}/{name}/{pg}.html`
- maker:  `/en/{prefix}_makersr/{id}/{name}/{pg}.html`
- detail: `/en/{prefix}_content/{id}/{CODE}.html`

Card: `div.col-item` → `h3.video-title > a` (title), `img.lazyload[data-src]` (thumb),
`video[data-src]` (preview mp4), `span.small.text-muted` (ngày).
Trang list 20 phim/trang; phân trang bằng `?pg=N` (site **không** trả `total_pages`).

## Danh mục — 3 BỘ GENRE KHÁC NHAU (đo 2026-10-17)

Tên nhóm lấy theo **chính nav của site**:

| nhóm (nav site) | nguồn | mục | URL genre | item trả về |
|---|---|---|---|---|
| **Censored Genre (244)** | `censored_categorylist/list/index.html` | 206 → 202 tên | `censored_category/{id}/{name}` | `censored_content` |
| **Amateur Genre (117)** | `amateurjav_categorylist/list/index.html` | 117 → 116 tên | `amateurjav_category/{id}/{name}` | `amateurjav_content` |
| **Uncensored** | ⚠️ site **không có** trang index | 218 (harvest) | `uncensored_category/{id}/{name}` | `uncensored_content` |

⚠️ **Bẫy đã dính (2026-10-17):** từng gắn nhãn "Thể loại Uncen" cho `amateurjav_*` → menu "Uncen"
toàn phim **có mosaic**. `amateurjav` = **Amateur** (amateur Nhật vẫn mosaic), **không phải uncensored**.
Ba bộ dùng **id khác nhau** (censored `Chubby`=133 vs uncensored `Chubby`=480) → không dùng chung id,
không dedup chéo.

- `uncensored_categorylist/list/index.html` → **200 nhưng 0 genre** (16.792 B = template chỉ có nav);
  `amateur`, `reducing-mosaic`, `clive`, `klive` cũng y hệt. Không có sitemap, không dropdown genre
  trên trang list.
- Trang detail của **một phần** phim uncensored có khối Genre **rỗng** (vd phim FC2), nhưng phim thuộc
  studio (10musume, HEYZO…) thì **có** pill genre → phải lấy mẫu nhiều phim, đừng kết luận từ 1 phim.
- `/en/uncensored_category/{slug}/1.html` **thiếu `{id}`** → trang fallback (slug rác nào cũng ra y hệt,
  title `completely uncensored - Genre`). **Phải có `{id}`.**

### Harvest genre uncensored (`UncenSetAsync`)

1. Pool = href detail gom từ `/en/uncensored_list/all/{1..12}.html` (240 link, cache 6 h).
2. Đọc từng detail → `UncenGenres()` lấy cặp `/en/uncensored_category/{id}/{name}/1.html`.
3. **Ngân sách mỗi lần build: ≤25 s, 3 luồng, tối đa 200 genre**; kết quả **cộng dồn** qua các lần
   (cache 7 ngày) → vài lần build là đủ, không dội site một lần.
4. Log: `SevenMM: uncen pool=<n> genres=<n>`.

Đo thật: pool 240 → **218 genre trong 20 s**.

`Danh mục` = 7 mục thể loại thật: Reducing Mosaic, Censored, Amateur, Uncensored, Chinese AV,
Korean Live, Chinese Live (`Chinese Live` site báo `(0)`).

Kiểm chứng link Uncen **qua module**: `#amateur` #0 → 20 item, `Idol` #109 → 20 item,
`＃Squirting` #217 → 3 item — **tất cả `uncensored_content`, 0 censored**.

## Server + player

Detail **không** nhúng iframe trực tiếp — link player nằm rỗng trong `mvarr`:

| key | server | prefix |
|---|---|---|
| `37_1` | SW (StreamHide) | `https://mmsi02.com/e/` |
| `38_1` | VH (VidHide) | `https://mmvh02.com/v/` |
| `42_1` | SP (SV3) | `//7mmtv.sx/assets/js/play/play.php?id=` ← protocol-relative, phải thêm `https:` |
| — | DD | `playmogo.com/e/<id>` (JSON-LD `contentUrl`) — DoodStream, công thức **F5** |

Biến inline trên trang detail (lấy **match cuối**): `hcdeedg252` (key), `hadeedg252` (xor),
`argdeqweqweqwe` (aesKey), `hdddedg252` (aesIv). `PREFIX + DecryptEnc(ENC)` = URL embed.

Referer khi lấy stream: **`https://7mmtv.sx/`** cho cả SW / VH / SP (thiếu → 403).

Thứ tự ưu tiên đang đặt: **SP → SW → VH → DD**. VH để cuối không phải vì hỏng:
nút thắt là hls.js (xem skill F19 Bẫy 8), đã fix ở client
(`Modules/LampaWeb/plugins/lampainit.js`).

## Số đo (2026-10-17, 4 trang detail đã lưu: d2, d4, jyma, list)

| thứ | giá trị |
|---|---|
| `hcdeedg252` (key decr_sun) | **17** (4/4 trang), `sep = 'r'` |
| `hadeedg252` (xor) | **17** (4/4 trang) |
| `argdeqweqweqwe` (aesKey) | **đổi theo trang**: `687b3731437aa483`, `09346e0ca1ba3eab`, `9b757b12e3cd131d`, `cb5e9d8b22ee29dd` |
| `hdddedg252` (aesIv) | đi kèm: `22fcd69f8ae5c786`, `f151c2f5e480935e`, `084455b34f710a38`, `29f92f2a2e14176c` |
| vector verify (d2.html) | key=xor=17, ak `687b3731437aa483`, iv `22fcd69f8ae5c786` → `37_1`=`bk5s6zqsx8ul`, `38_1`=`0dc7tr80trym`, `42_1`=`h0v5fLAKg3sfavCV27zdD2vx9FS7Cd43wInWJ5J9-4ZShTLae0ISyQOI2Xqj44HE` |
| SP | playlist **2587 segment**, `#EXT-X-KEY:METHOD=AES-128`, segment 533–681 KB @ **~1 MB/s** |
| SW | segment MPEG-TS **947 KB / 1.4 s** |
| VH | **30–145 KB/s** (chậm thật, đã xử ở client) |
| DD | CDN `el1110vo.cloudatacdn.com` (162.19.233.91) timeout từ IP VN |

## Đã biết là lỗi phía site (không phải lỗi module)

- `Thể loại Uncen` mục cuối `新登場メーカー` (`amateurjav_category/246`): site **tự 404** mọi dạng URL (escaped / raw / chỉ id) dù vẫn hiện checkbox. Giữ nguyên trong menu.
- `Chinese Live`: site trả `(0)`.
- `total_pages` không có → `pg` là tham số duy nhất; page 2+ vẫn chạy.

## Vận hành

- CF bật/tắt thất thường (bật lại 2026-10-17): IP Termux hay bị SSL-abort/403 khi fetch dồn dập.
  Nếu bị: bật proxy cho module trong `init.conf`
  (`"SevenMM": { "usperproxy": true, "globalnameproxy": "geo" }`) rồi restart.
- Tìm kiếm dùng **POST** (`searchform_search/all/index.html`, field `search_keyword`)
  — chưa verify được response khi bị CF chặn.
- **Không spam `curl`** vào site (xem luật cứng trong skill): dùng `curl_cffi impersonate="chrome124"`,
  retry 3–8 lần; recon nặng thì đi jina.
