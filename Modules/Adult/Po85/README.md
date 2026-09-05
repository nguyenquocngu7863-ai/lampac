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

## Khung code

Theo mẫu `Modules/Adult/Porntrex` (cùng engine KVS): `Controller.cs` (3 routes), `Service.cs` (Uri/Playlist/Menu/StreamLinks), `ModInit.cs` (`SisiSettings` + `headers_stream` Referer), `manifest.json` (`dynamic: true`, tree 3 file). Lampac biên dịch lúc khởi động (`compilation Po85`), không cần build tay — copy file vào `module/Adult/Po85/` rồi restart là test được. Deploy qua `setup-termux.sh --sync` (block Po85 trong cả `sync_latest_modules` và `install_custom_modules`).
