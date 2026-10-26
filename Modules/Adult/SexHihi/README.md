# SexHihi

Module SISI cho `https://sexhihih.blog` (WordPress, theme `holy`).

| | |
|---|---|
| Site | https://sexhihih.blog |
| Route | `/sexhihi` (`/sexhihi/vidosik`, `/sexhihi/video.m3u8`) |
| Namespace | `SexHihi` — file `Service.cs` = `ShhTo`, `Controller.cs` = `SexHihiController` |
| Kiểu site | HTML server-side (không CSR) → chỉ cần HTTP fetch, **không cần Chrome** |
| Server | 1 server / phim, `data-type="m3u8"` |
| displayindex | 13 |

## Cách làm (quy trình đã dùng)

1. **Soi site 1 lần, offline-first** — `capture_oneshot.py` (skill `lampac-decode-offline`):
   ```bash
   python3 ~/.config/opencode/skills/lampac-decode-offline/tools/capture_oneshot.py \
     --host https://sexhihih.blog \
     --detail https://sexhihih.blog/phim/anh-bac-si-nen-co-y-ta-vao-gio-nghi-trua \
     --out $TMPDIR/opencode/sexhihih_sample
   ```
   Ra `index.json` + 14 file thô (home, page2, 4 taxonomy, search, detail, embed, JS).
2. **Đọc file offline**, không gọi lại site: `grep`/`python` để tìm markup list, phân trang,
   taxonomy, player.
3. **Viết code** theo mẫu `TangThon`/`SexVietDam` (cùng họ theme): `manifest.json` + `ModInit.cs`
   + `Service.cs` + `Controller.cs`. **Không cần `.csproj`** (giống `SupJav`/`JavGuru`) →
   `Core.dll` tự compile lúc start.
4. **Check → deploy → smoke**:
   ```bash
   SHARED_DLL=$HOME/lampac-run/Shared.dll ~/lampac-native/csc/check.sh Modules/Adult/SexHihi
   cp Modules/Adult/SexHihi/{Controller.cs,manifest.json,ModInit.cs,Service.cs} \
      ~/lampac-run/module/Adult/SexHihi/
   pkill -f "Core\.dll"; sleep 3; nohup bash ~/lampac-native/run-lampac.sh > ~/lampac-native/lampac-native.log 2>&1 &
   ```
5. **Dọn**: `rm -rf ~/lampac-clean/Shared/{bin,obj}` (module mới không sinh bin/obj).

## Công thức URL

| Việc | URL (đo 2026-10-26) |
|---|---|
| Trang chủ (dùng list đầy đủ) | `/danh-sach/phim-moi` (+`?page=N`, 24 phim/trang, **515 trang**) |
| Thể loại | `/the-loai/<slug>?page=N` (7 slug) |
| Quốc gia | `/quoc-gia/<slug>?page=N` (1 slug: `trung-quoc`) |
| Tìm kiếm | `/?search=<kw>` (+`&page=N`) — form site: `GET /` với input `name="search"` |
| Detail | `/phim/<slug>` |
| Stream | `data-link` trong `<a class="server">` → `/storage/m3u8/<slug>/index.m3u8` |

`ShhTo.Uri(host, search, c, pg)` map: `search` → `/?search=`; `c` → `/<c>`; mặc định →
`/danh-sach/phim-moi`. `c` nhận dạng `the-loai/<slug>` và `quoc-gia/<slug>`.

## Markup đã dùng

**Card list** (sidebar `list-top-movie-item` không dùng class này → không lọt vào list):
```html
<a class="m-block movie-item" href="https://sexhihih.blog/phim/<slug>" title="Tên phim">
  <div class="lazyload public-film-item-thumb" data-original="/storage/images/...webp"></div>
  <div class="movie-title-1">Tên phim</div>
</a>
```
Poster nằm ở `data-original` (không phải `src`) và **đôi khi là đường dẫn relative** → phải
nối vào `SiteHost` (`ShhTo.Abs`).

**Player**:
```html
<a onclick="chooseStreamingServer(this)" class="server" href="javascript:void(0)"
   data-id="21340" data-link="/storage/m3u8/<slug>/index.m3u8" data-type="m3u8">1</a>
```
`data-link` lúc **relative**, lúc **absolute** → `ShhTo.Abs` xử lý cả hai.
Chỉ nhận `data-type` ∈ {`m3u8`,`m3u`,`mp4`}; `embed` bị bỏ qua.

**Taxonomy** (nav): `<a href="/the-loai/jav-hd">JAV</a>`, `<a href="/quoc-gia/trung-quoc">…</a>`.

## Điểm dễ

- **HTML tĩnh, không JS render** → `curl_cffi`/`Http.Get` là đủ, không cần Playwright.
- **Không obfuscate**: m3u8 nằm thẳng trong `data-link`, không packer/AES/gateway.
- **Phân trang dạng query `?page=N`**, cùng công thức cho list / thể loại / quốc gia / search.
- **1 server duy nhất**, không cần xếp hạng, không cần probe nhiều host.
- **Card HTML có sẵn `title` + `data-original`** → parser 1 regex là ra đủ title/poster/link.
- **Taxonomy lấy động từ nav** (7 thể loại + 1 quốc gia), không cần dò `/the-loai/` index.

## Điểm khó / bẫy đã gặp

1. **Segment có tiền tố PNG (1 số phim)** — CDN `tiktokcdn.com` trả
   `[PNG 1×1 68 byte][MPEG-TS thật]`, `Content-Type: image/png`.
   - Đo sai sẽ tưởng "file rác"/"ảnh". Phải dò TS offset (`0x47` cách nhau 188) → offset **68**.
   - Phim khác lại trả TS thuần (`47 40 00 10`) → **không** được hardcode "luôn có PNG".
   - Xử lý: **không strip ở server** — app đã có patch `PngStripLoader`
     (`Modules/LampaWeb/plugins/lampainit.js`) tự tìm TS offset và cắt.
2. **`/quoc-gia/...` chỉ có trên trang chủ `/`** — trang `/danh-sach/phim-moi` **không có**
   link quốc gia → menu phải fetch **trang chủ** để dựng taxonomy (đã sửa 1 lần).
3. **Một lần fetch bị rớt → 0 phim → 503** (Cloudflare/đứt mạng). Đã thêm:
   - `ShhTo.IsBlocked(html)`: rỗng / có dấu Cloudflare (`just a moment`, `cf-chl`, `attention required`…)
     / **không chứa `sexhihih.blog`** → coi như hỏng.
   - `GetPageAsync` thử **2 vòng**, mỗi vòng race `httpHydra.GetSpan` + `Http.Get`.
   - Có dấu hiệu hỏng → `e.Fail("playlists", refresh_proxy: true)` chứ không trả list rỗng.
4. **List rỗng luôn ra HTTP 503** (`PlaylistResult` → `OnError("playlists")`) — đây là hành vi
   framework, giống `SupJav`; tìm kiếm không ra kết quả sẽ hiện lỗi chứ không phải "no data".
5. **`total_pages` lấy từ pager của chính trang** (`?page=N` max): list 515, thể loại 76,
   quốc gia 33. Trang chủ có pager tới 1029 (khác pager của list) → **đừng lấy max toàn trang chủ**.
6. **Search có phân trang riêng** (`&page=N`), khác `/search/<kw>/` (cái này 404).
7. **Không đoán URL stream** — `data-link` là nguồn duy nhất; không suy ra `/storage/m3u8/<slug>/…`
   từ slug (site có thể đổi đường dẫn).

## Kiểm chứng (2026-10-26, localhost 9118)

```
GET /sexhihi                                   -> 200, 24 item, total_pages=515
GET /sexhihi?c=the-loai/jav-hd                 -> 200, 24 item, total_pages=76
GET /sexhihi?c=quoc-gia/trung-quoc             -> 200, 24 item, total_pages=33
GET /sexhihi?pg=2                              -> 200, 24 item, phim đầu KHÁC trang 1
GET /sexhihi?c=the-loai/jav-hd&pg=2            -> 200, 24 item, phim đầu KHÁC trang 1
GET /sexhihi?search=viet                       -> 200, 24 item
GET /sexhihi/vidosik?uri=<detail>              -> 200 {"1":"…/sexhihi/video.m3u8?…&q=1"}
GET /sexhihi/video.m3u8?uri=<detail>&q=1       -> 200 /proxy/…  #EXTM3U (1588 dòng, 790 segment)
GET <segment qua proxy>                        -> 200 video/mp2t, magic 47 40 00 10 (TS thuần)
GET <poster qua proxyimg>                      -> 200 image/jpeg
```

Menu: `Tìm kiếm` (search_on) / `Danh sách phim mới` / `Thể loại` (7) / `Quốc gia` (1) —
mọi mục gốc đều có `playlist_url` hoặc `submenu` (không có mục chết).
