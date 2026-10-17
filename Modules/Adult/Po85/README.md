# Po85 — nguồn 85po.com (KVS)

Module Adult xem 85po.com trực tiếp trong Lampa/SISI. Route gốc: `/po85`.

## Routes

| Route | Chức năng |
|---|---|
| `/po85?search=&sort=&c=&t=&pg=` | Menu + playlist |
| `/po85/vidosik?uri=<url trang video>` | Dict link theo chất lượng → `/po85/strem?link=` |
| `/po85/strem?link=<get_file>` | Resolve redirect, fallback proxy trực tiếp kèm Referer |

Menu gồm: Tìm kiếm, Sắp xếp (Mới nhất, 4K, Đánh giá cao, Xem nhiều nhất), Thể loại (26 tag phổ biến cào từ trang `/tags/`).

## Cách làm

### 1. Trinh sát trang thật

85po chặn fingerprint lạ ở tầng Cloudflare: curl trần 403, Chrome headless trong proot-distro dính trang "Attention Required!", proxy công cộng (allorigins, codetabs, jina) cũng chết. Nhưng HTTP client của Lampac với header trình duyệt đầy đủ qua được, và Chrome thật trên điện thoại qua được. Kết luận: fetch server-side được, không cần Playwright.

Lấy HTML trang video (`/v/34567/...`) về phân tích, phát hiện engine **KVS** (`kt_player.js` v7.11.4):

- Link video nằm trong `flashvars.video_url`: `'function/0/https://www.85po.com/get_file/3/<hash>/34000/34567/34567.mp4/?br=446'`.
- Tiền tố `function/0/` là marker của KVS player (giải bằng JS timing trong `dJ/dH/dI`), nhưng thực tế strip đi vẫn dùng được.
- Token **ổn định theo video** (không đổi mỗi lần load), **không bind cookie** (mở tab ẩn danh được), nhưng **hết hạn theo thời gian**.
- List dùng selector `.thumb a[href*="/v/"]`, ảnh `data-original`, chất lượng ở class `qualtiy` (site viết sai chính tả), thời lượng `.time`.

### 2. Tìm cơ chế quality

Nhãn trên card (2K, 4K) là site tự gắn, không phản ánh file thật. Dropdown download trong trang video mới là nguồn quality thật: mỗi mức (480p/720p/1080p) một hash riêng, tên file có hậu tố `_720p`/`_1080p`. Module parse **tất cả** anchor download, lấy text ("MP4 1080p, 5.65 Mb") làm nhãn.

### 3. Chốt đường phát

- `get_file` của flashvars có thể 404 (hash cũ) → ưu tiên link download.
- `get_file` trả file trực tiếp (206), không redirect → `Strem` fallback `HostStreamProxy(link)` kèm `Referer: https://www.85po.com/` khi `GetLocation` rỗng.
- Đã verify end-to-end: proxy trả `206 video/mp4`.
- Redesign: `/strem` BAT BUOC giu buoc theo-redirect (GetLocation + RCH neu bat).
  Ban viet lai tung cat mat (proxy thang URL route) → curl van 200 nhung app bao
  "no supported source". Bai hoc: dung bao "xong" khi chua test phat that tren app.

### 4. UHD resolver (4K, node + Chrome thật, port 9196)

- File 2160p chỉ nhả cho phiên trình duyệt có `/vi/` + TLS Chrome: server curl 403 ngay cả kèm cookie.
- `uhd/resolver.js` (playwright-core + system chrome `/usr/bin/google-chrome`) mở trang `/vi/`, đặt `<video src=file+rnd>` rồi bắt response 302 để lấy signed `remote_control.php` (valid ~15-25p). Health check: `GET 127.0.0.1:9196/health` → `ok`.
- Playwright riêng ở `/root/lampac/.playwright` (không dùng node host Termux — android bị từ chối). ModInit tự `npm i` lần đầu + spawn resolver kèm supervisor loop.

## Khung code

Theo mẫu `Modules/Adult/Porntrex` (cùng engine KVS): `Controller.cs` (3 routes), `Service.cs` (Uri/Playlist/Menu/StreamLinks), `ModInit.cs` (`SisiSettings` + `headers_stream` Referer), `manifest.json` (`dynamic: true`, tree 3 file). Lampac biên dịch lúc khởi động (`compilation Po85`), không cần build tay — copy file vào `module/Adult/Po85/` rồi restart là test được. Deploy qua `setup-termux.sh --sync` (block Po85 trong cả `sync_latest_modules` và `install_custom_modules`).

## Redesign 2026-09 (web đổi giao diện, giữ engine KVS)

- List: `<div class="item` + link `/video/{id}/{slug}/`, poster `data-original`,
  quality `is-4k/is-2k/is-hd`, duration `<div class="duration">`.
- Trang `/video/` chỉ có template; player + flashvars chuyển sang `/embed/{id}/`
  (`EmbedPage()` dò sang). Bỏ dropdown download.
- `video_alt_url2/3` có thể là URL trang locale (không phải file) — `AltPages()`
  mở tiếp trang đó lấy flashvars đầy đủ (1080p/2160p).
- Sort `/4k/` chết (404) — bỏ khỏi menu. Category mới theo vùng.
- Phân trang KVS: AJAX `{page}?mode=async&function=get_block&block_id={id}&...&from=N`.

## Redesign 2026-10-17 (locale /en/ + link mới + bỏ flashvars)

Đo lại toàn bộ — site đổi **6 chỗ**:

| chỗ | cũ (2026-09) | mới (2026-10-17) |
|---|---|---|
| locale | không prefix | **`/en/`** (path cũ vẫn 200, nhưng canonical có `/en/`) |
| link video | `https://www.85po.com/video/{id}/{slug}/` | **`https://www.85po.com/en/v/{id}.html`** |
| phân trang | `?from=N` | **path `/{pg}/`** — vd `/en/latest-updates/2/` |
| tag | `/tags/{slug}/` | `/en/tags/{slug}/` |
| category | `/categories/{slug}/` | `/en/categories/{slug}/` |
| player | `flashvars` ở `/embed/{id}/` | **`var <tên-ngẫu-nhiên> = { video_url: '…', video_alt_url: '…' }`** truyền vào `kt_player()` — KVS **v15.8.3**, `flashvars` = **0** |

- `video_url` giờ có **`?v-acctoken=<base64>`** — dùng nguyên xi URL đó (đo: 206, `ftyp`,
  `video/mp4`, chỉ cần `Referer: https://www.85po.com/`).
- Config nằm **cả trên trang video lẫn `/en/embed/{id}`** → `EmbedPage()` dò id bằng
  `/(?:video|v)/(\d+)(?:/|\.html)`.
- Phân trang path ăn cho **tag / category / latest-updates / top-rated / most-popular**
  (đo: trùng 6/26 với trang 1 — 6 id đầu là **block ghim lặp** trên mọi trang, phần còn lại khác).
- `?sort_by=X` trên home **vẫn sống**, và **`&from=N` vẫn ăn** (đo: trùng 17/41) → nhánh sort_by giữ nguyên.
- Search `/en/search/{kw}/` OK; trang 2+ vẫn dùng AJAX `get_block` (đo: 24 card).
- `/en/4k/` vẫn 404 → vẫn rơi về `latest-updates/`.
- `/tags/` có 2765 slug; category slug theo vùng (ri-ben, han-guo, tai-wan, hong-king…).
- **Danh sách quality có nhiều nhãn cho cùng độ phân giải** (`720p`, `720p 2`,
  `MP4 720p, 16.04 Mb`) — đây là **link xem** (`video_url`/`video_alt_url`) và **link download**
  là hai nguồn khác nhau, **không phải trùng lặp** → để nguyên.

**Verify 2026-10-17 (qua module):** `/po85` 36 item; pg1/pg2/pg3 = 36/24/24
(trùng 1·2 = 1, 2·3 = 0); sort newest/4k/top-rated 24, sort_by 36; tag 24;
category ri-ben 24 / han-guo 1; `vidosik` 6 nhãn (xem + download); `strem` → **206 `ftyp` `video/mp4`**.
