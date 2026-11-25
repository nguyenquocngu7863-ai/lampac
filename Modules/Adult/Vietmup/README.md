# Vietmup Module

**Site**: https://vietmup.lol  
**Route**: `/vietmup`  
**Status**: ✅ Hoạt động (cập nhật 2026-11-25)

## Cấu trúc

- `manifest.json` — module dynamic
- `ModInit.cs` — IModuleLoaded + IModuleSisi, SisiSettings
- `Service.cs` — `Uri()`, `Playlist()`, `Servers()`, `DramaManifest()`, `DramaEpisodes()`, `Categories()`, `Tags()`, `Chips()`, `PlaylistRelated()`, `SortSet()`, `Menu()`
- `Controller.cs` — `Index`, `Episodes`, `Vidosik`, `Video`, `Abyss`, `Genres` + `?rel=`
- `Abyss.cs` — resolve + giải mã 64 KB đầu của Abyss mp4

## Ngữ cảnh list (`?c=`)

| c | Trang site | Phân trang | Sort |
|---|---|---|---|
| (trống) | `/` | `?page=N` | 5 |
| `cat/<slug>` | `/category/<slug>` | `?page=N` | 4 |
| `tag/<slug>` | `/tag/<slug>` | `?page=N` | 4 |
| `films` | `/films` | `?page=N` (109 trang) | 5 |
| `drama` | `/drama` | **không** (trang tổng hợp theo tab) | 3 (`tab=`) |
| `drama/<slug>` | `/drama/<slug>` | — (trang series) | — |
| `search=<kw>` | `/search?q=` | **không** | — |

`drama/<slug>` → `Episodes()`: cắt các link `/drama/<slug>/ep-N`, trả mỗi tập 1 item phát được (`total_pages: 1`).

## Sort (dòng 2)

Đo 2026-11-25 — mỗi ngữ cảnh có bộ sort riêng; **sort lạ bị ClampSort về mặc định**:

- **home**: Nổi bật (`null`) · Mới nhất (`latest`) · Thịnh hành (`view_by_week`) · Xem nhiều (`view_count`) · Yêu thích (`like_count`)
- **category/tag**: Nổi bật (`score`) · Thịnh hành · Xem nhiều · Yêu thích
- **films**: Mới nhất · Thịnh hành · Xem nhiều · Đánh giá cao (`rating`) · Yêu thích
- **drama**: Mới cập nhật (`tab=latest`) · Đang hot (`tab=hot`) · Trọn bộ (`tab=finished`)
- **search / drama series**: site không có sort → **bỏ hẳn dòng 2**

Đã đo sequence khác nhau cho mọi sort hợp lệ (md5 danh sách tên), ví dụ home: def `0966624d` → latest `08ec1c19` → week `f33fa911` → view `61736853` → like `bcb094e0`.

## Menu

- Dòng 1: Tìm kiếm
- Dòng 2: `Sắp xếp: <đang mở>` (submenu theo ngữ cảnh; ẩn ở search/series)
- `Phim` → `?c=films`, `Phim ngắn` → `?c=drama`
- `Thể loại`: 59 mục lấy từ `/category` (home chỉ có 12), cache `vietmup:categories:v2` 360 phút
- `Thẻ`: 115 tag từ `/tag` (site có ~10.097 tag trong sitemap — không đưa hết vào menu), bỏ dấu nháy/`#` khi hiển thị, cache `vietmup:tags:v2` 720 phút

## Popup nhấn giữ (`/vietmup/genres?uri=`)

`{items:[], groups:[{title,items:[{title,url}]}]}`:

- `Thể loại` / `Thẻ`: chip `<a href="/category|/tag/...">` trong trang chi tiết (cắt trước "Có thể bạn cũng thích")
- `Liên quan`: 1 item `plain: true` → `?rel=<uri>`; `?rel=` trả 12 item, `total_pages: 1`, `pg>1` rỗng
- watch dùng khối "Có thể bạn cũng thích", films dùng khối "Phim liên quan"; drama ep không có khối related
- Cache RAM `_infoCache` 6 h; SSRF: chỉ nhận URL chứa `vietmup.lol`

## Player servers

Trang watch/films: `<button data-server-index data-server-type data-server-url>`. Module map:

| type | xử lý |
|---|---|
| `m3u8`, `hls` | HLS `/api/stream?t=…` → `video.m3u8` qua proxy (Referer site) |
| `direct` | mp4 → `video.mp4` |
| `abyss` | `player.abyssplayer.com/<slug>` → `/vietmup/abyss.mp4` (Abyss.cs) |
| `embed` | iframe bên thứ ba → bỏ |

Trang tập phim ngắn: `data-manifest-url` (HLS) / `data-direct-url` (mp4) trên `div[data-drama-player]`.

`vidosik` trả URL có đuôi đúng loại: `.m3u8` để sisi.js bật hls.js, `.mp4` cho Abyss/direct.

### Nguồn chết phía site (đo 2026-11-25)

- Server `hls` của watch trỏ `server1.streamcap.store` (segment `.html`) → **Cloudflare 403 cho mọi client**, kể cả Chrome thật (`Attention Required!`); playlist vẫn 200.
- Server `Google` của films trỏ `lh3.googleusercontent.com` → segment trả `image/png`, không phải video (nguồn rác của site).

Vì vậy khi test nên chọn `Vip` (m3u8), `Abyss`, hoặc server phim `VIP`.

## Smoke (2026-11-25)

- home 25 `tp=0` · pg2 25 khác trang 1 · cat/tag 25 · tag pg2 khác · films 30 `tp=0` · films pg2 30 khác · drama 33 / hot 42 / finished 17 `tp=1` · series 13 tập `tp=1`, pg2 rỗng · search 25 `tp=1`, pg2 lặp trang 1 (site không phân trang), kw rác → `count=0`
- menu 6 dòng; Thể loại 59; Thẻ 115; quét CJK/Cyrillic/Thai/Hàn → 0
- popup: watch (1+1+Liên quan 12 `tp=1`) · films (10+15+Liên quan 12 `tp=1`)
- `play.sh`: **PASS** Vip HLS (h264 720x1280, 614.8 s, decode 5 s đầu/cuối) · **PASS** Abyss mp4 (h264 720x1280, 614.8 s) · **PASS** drama ep-1 HLS (h264 720x1280, 280.4 s)

## Bài học test

Response SISI trả `"count"` + `"list"`, KHÔNG phải `"items"`:

```python
d = r.json()
count, items = d.get("count", 0), d.get("list", [])
```

Đọc `items` → luôn 0, kết luận oan "module lỗi".
