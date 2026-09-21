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

## Redesign 2026-09 (web doi giao dien, giu engine KVS)

- List: `<div class="item` + link `/video/{id}/{slug}/`, poster `data-original`,
  quality `is-4k/is-2k/is-hd`, duration `<div class="duration">`.
- Trang `/video/` chi co template; player + flashvars chuyen sang `/embed/{id}/`
  (`EmbedPage()` doi sang). Bo dropdown download.
- `video_alt_url2/3` co the la URL trang locale (khong phai file) — `AltPages()`
  mo tiep trang do lay flashvars day du (1080p/2160p). Noi dung KHAC nhau theo
  locale (`/en/` vs `/ja/`) — thieu quality thi quet ca 2.
- File `*_2160p.mp4` co san trong flashvars thi parse thang, khong can resolver.
- Sort `/4k/` chet (404) — bo khoi menu. Category moi theo vung
  (ri-ben/zhong-guo/tai-wan/ma-lai-xi-ya/xin-jia-po/xiang-gang).
- Phan trang KVS moi: AJAX `{page}?mode=async&function=get_block&block_id={id}&...&from=N`
  (`?from=` tra 404, `?page=` bi lo). Xem chi tiet trong skill `devfetch-proxy`.
- `/strem` GIU buoc theo-redirect cu (GetLocation + RCH neu bat): link get_file la
  route trung gian, proxy thang route thi curl van 200 nhung app hong.
- Recon qua module tam `DevFetch` (`/devfetch?url=&re=`) khi CF chan IP ngoai.
