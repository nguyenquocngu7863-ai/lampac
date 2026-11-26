# JavSub — https://javsub.blog

Module Adult cho JavSub (Việt + Nhật + FC2). HTML thuần, không Cloudflare.

## Host — 2 tên miền, CÙNG một site

| Host | Ghi chú |
|---|---|
| `javsub.blog` | host trong `manifest.json`; trang chủ `/` và `/ajax/...` gọi từ đây |
| `javsubnet.com` | **mọi `href` trong HTML trỏ tuyệt đối sang host này** (kể cả trang chủ) |

Bằng chứng: `grep -o 'href="https\?://[^/]*' index.html | sort | uniq -c` → **100% `javsubnet.com`**.
Trang chi tiết phục vụ từ 2 host cho **md5 trùng nhau**.

⇒ **Parser không được neo host.** `_itemRe` khớp `class="item"` + `href=.../phim-sex/...` rồi lấy
host nguyên từ `href`. Bản cũ chặn `Host.EndsWith("javsub.blog")` trong `NormalizePageUrl` →
**0 item → `/javsub` 503** (đo 2026-11-23 trên capture: 24 match thô, 0 match qua filter).

Ràng buộc host chỉ đặt ở **URI do client gửi vào** (`SafeDetailUrl`). Từ **2026-11-26** ràng buộc
đó là: giữ path `/phim-sex/`, **bỏ host nguồn**, fetch trên `SiteHost` — vì mirror còn xoay tiếp
(đo được `javsubnet.net`, ngoài danh sách `.blog`/`.com`). Whitelist `MirrorHosts`/`IsSiteHost` đã
bị bỏ: chặn theo danh sách host vừa dễ vỡ vừa không cần, vì request cuối luôn đi tới `SiteHost`.
Bản whitelist cũ làm `/javsub/genres?uri=<uri tuyệt đối>` trả rỗng → **popup trống với người dùng**. 

Verify bằng `~/lampac-native/popup.sh javsub` → PASS (3 nhóm: Thể loại 19 · Thẻ 1 · Liên quan 1,
rel 8 `tp=1`; chip Thể loại → 24 item `tp=81`).

## Route

| Route | Việc |
|---|---|
| `GET /javsub` | home = `/` (24 item/trang, `total_pages` 300) |
| `GET /javsub?pg=N` | trang N |
| `GET /javsub?c=<path>` | `the-loai/<slug>` hoặc `tag/<slug>` (+ `?page=N`) |
| `GET /javsub?search=<kw>` | `/search?k=<kw>` (form site: `action=/search`, `name=k`) |
| `GET /javsub/genres?uri=<uri detail>` | popup long-press |
| `GET /javsub?rel=<uri detail>` | "Video liên quan" (`total_pages: 1`, `pg=2` rỗng) |
| `GET /javsub/vidosik?uri=` | `{"Server N": url}` |
| `GET /javsub/video.m3u8?uri=&srv=` | resolve → 302 `/proxy/<token>` |
| `GET /javsub/video?uri=&srv=` / `.mp4` | giữ tương thích (không đuôi → app không chọn hls.js) |

## Sort — SITE KHÔNG CÓ SORT (tái xác nhận 2026-11-23)

Quét `<select>`, `sap-xep`, `sort`, `orderby` trên home · trang danh sách · trang chi tiết ·
`?view=the-loai` → **0 kết quả**. Site chỉ có một thứ tự mặc định (mới nhất).

`NormalizeSort` trả `null` có chủ ý (không hiện dòng 2 chết — như SupJav).

## Taxonomy

- **Category**: nav + `/?view=the-loai` → 41 mục (`<h3>Ten</h3><small>N phim</small>`), cache 12h.
- **Tag**: site **không có trang liệt kê tag**. `/?view=tag|tags|the-tag` đều rơi về home
  (đo 2026-11-23, nav chỉ có 2 link `/tag/*`: Xvideos, XNXX).
  ⇒ menu Tag chỉ 2 mục; **tag đầy đủ chỉ lấy được từ chip từng phim** (popup, 12–16 tag/phim).
  Đây là giới hạn của site, không phải thiếu sót của module.

## Popup long-press

Chip nằm trong `<div class="video__tags">`, mỗi chip là
`<a href class="label"><svg/><span>Ten</span></a>`:

| Path | kind | Đo 2026-11-23 |
|---|---|---|
| `/the-loai/<slug>` | `cat` → nhóm "Thể loại" | 2 mục |
| `/tag/<slug>` | `tag` → nhóm "Thẻ" | 12–16 mục |

Link là **tuyệt đối sang `javsubnet.com`** → chỉ lấy `path`.

"Video liên quan" **không có trong HTML** — khối đó là Alpine.js:
`x-data="list('/ajax/suggestions/<slug>', true, 8)"`. Phải gọi
`GET /ajax/suggestions/<slug>?limit=8` (JSON `{data:[{title,url,thumbnail_file_url,…}]}`) → 8 item.

Nhóm rỗng bị bỏ; thứ tự cố định: Diễn viên → Thể loại → Thẻ → Liên quan.
Cache chip+rel trong `Dictionary` tĩnh (TTL 6h, cap 300) — không dùng `hybridCache` vì
`ValueTuple` không round-trip được.

## Player: 2 backend lazy-load

Trang chi tiết chỉ có `button.set-player-source[data-source]` (embed host), **không** có link trực tiếp:

1. `GET <embed-host>/videos/<id>/play?...` → HTML embed; có `sources` inline (kiểu vcast) là xong.
2. Không có → `POST <embed-host>/videos/<id>/config?d=` (body rỗng) → `{"sources":[{"file":"…m3u8"}]}`.
3. 404 mọi biến thể → fallback Chrome (Playwright) mở embed, bắt response `/config`.

Cache `javsub:stream:{pageUrl}:{label}` 10 phút.

## Bẫy: URL phải có đuôi `.m3u8`

Lampa chọn player theo **đuôi URL** (`app.min.js`: `if (/\.m3u8/.test(src))`). Link
`/javsub/video?...` không đuôi → Lampa phát m3u8 bằng thẻ `<video>` → **đen hình mọi server**
(site vẫn 200, curl vẫn ra `#EXTM3U`, rất dễ kết luận sai).

⇒ `vidosik` **phải** build `video.m3u8` (nếu cache có `.mp4` thì đổi sang `video.mp4`).

## Bẫy khác

- **`GetPageAsync` không dùng cho JSON.** Hàm đó đòi `page.Length >= 3000` + retry tới 30s;
  JSON `/ajax/suggestions` (~7KB) vẫn bị coi là thất bại và vượt budget `InvokeCacheResult`
  → `?rel=` trả 503. Dùng `FetchJsonAsync` riêng (1 lần `httpHydra.GetSpan`, không length gate).
- **Cache lỗi cũ giả dạng lỗi mới.** Sau khi sửa, `?rel=` vẫn 503 vì `InvokeCacheResult` giữ
  kết quả lỗi theo key. Đo lại bằng **URI khác** trước khi kết luận "chưa sửa được".
- **`global::System.UriPartial`** — `JavSubTo` có hàm tên `Uri`, nên `System.Uri.UriPartial`
  bị compiler hiểu thành method group (CS0117).

## Verify 2026-11-23 (app thật, `127.0.0.1:9118`)

| Endpoint | Kết quả |
|---|---|
| `/javsub` | 24 item, `tp=300` |
| `/javsub?pg=2` | 24 item, khác trang 1 |
| `/javsub?c=the-loai/vietsub` | 24 item, `tp=40` |
| `/javsub?c=tag/vietsub` | 24 item, `tp=3` |
| `genres` | `Thể loại` 2 · `Thẻ` 12 · `Liên quan` 1 |
| `?rel=` (4 phim khác nhau) | 8 item, `tp=1` mỗi phim |
| `?rel=&pg=2` | rỗng, `tp=1` ✔ |
| `genres?uri=` host lạ / uri rác | `{"items":[],"groups":[]}` 200 ✔ |
| `?rel=` host lạ | `{"total_pages":1,"list":[]}` 200 ✔ |
| `vidosik` | 2 server, URL có `.m3u8` ✔ |
| `play.sh` | **PASS** — `hls=true`, master 2 variant, `h264 1280x720 · 4155.5s`, decode 5s đầu ✔ / 5s cuối ✔ |

- `check.sh`: **0 error** (73 warning, toàn `CS8603/CS8600/CS8625/CS8604/CS8618` nullable)
- Menu: `Tìm kiếm` + `Thể loại` (41) + `Tag` (2)

## Đo lịch sử (2026-10-21)

- 8 video liên tiếp: 1–2 server/video, cả 2 backend trả `200 application/vnd.apple.mpegurl`.
- Master → variant → segment: `200`, segment `video/mp2t`, byte đầu `0x47` (TS).
- Playlist của backend 2 là **media playlist** (932–1478 segment), segment URI tương đối →
  Core proxy đã rewrite thành `/proxy/<token>` (OK).

## Cấu hình

- `manifest.json`: module Adult, `dynamic: true` (compile runtime, không cần csproj)
- `ModInit.cs`: `SisiSettings("JavSub", https://javsub.blog)` — đọc `init.conf` nếu có `JavSub.host`