# LampaWeb

Phân phối **web-client Lampa** từ **`wwwroot`**, build **`lampainit.js`** và các script phụ trợ, danh sách extension, tích hợp **accsdb**, cập nhật repo nền (background) (**`LampaCron`**).

## Mục đích

- Gốc site **`/`**: khi cần sẽ chèn thẻ **`<base>`** cho thư mục lấy từ **`conf.index`** (ví dụ `lampa-main/index.html`), nếu không thì redirect sang file tĩnh.
- Cấu hình module khai báo repo Git (**`git`**, **`tree`**) để tự động kéo source và chu kỳ **`intervalupdate`** (phút), cờ **`autoupdate`**.

## Các route chính (`Controllers/ApiController.cs`)

| Route | Chức năng |
|---------|------------|
| `/`, `/personal.lampa`, các biến thể lồng nhau | Trang chính của Lampa hoặc trang placeholder. |
| `/reqinfo` | JSON chứa thông tin request hiện tại (**`requestInfo`**). |
| `/extensions` | Trả **`extensions.json`** của module (có cache) với thay thế `{localhost}`. |
| `/testaccsdb` | Kiểm tra quyền truy cập accsdb (GET/POST); xem thêm **`EventListener.Accsdb`** trong `ModInit`. |
| `/app.min.js`, `/{type}/app.min.js` | Build app đã minify. |
| `/css/app.css` | Stylesheet. |
| `msx/start.json`, `samsung.wgt`, `lg.ipk` | Gói riêng cho TV (MSX, Samsung Tizen, LG webOS). |
| `/lampainit.js` | Khởi tạo client (chèn danh sách plugin, **deny.js**, token). |
| `/on.js`, `/on/js/{token}`, `/on/h/{token}`, `/on/{token}` | Chế độ online-plugin. |
| `/dorama.js`, `/dorama/js/{token}` | Lampa plugin riêng cho mục **«Doramas»** và nguồn `lampac_dorama`. |
| `/subsense.js` | Lampa plugin **SubSense** — tự động gắn phụ đề tiếng Việt cho player (nguồn SubSense, chuyển đổi srt/zip → VTT). |
| `/privateinit.js` | Khởi tạo bổ sung. |
| `/telegram_auth_gate.js` | Plugin kịch bản xác thực Telegram (xem module Community). |

Sự kiện **`accsdb`** trong `ModInit`: với đường dẫn **`/testaccsdb`**, nếu UID trùng **`shared_passwd`** thì gán **`IsAnonymousRequest`** (đi qua cho mật khẩu dùng chung).

## Cấu hình

Section trong `init.conf`: **`LampaWeb`**.

Các field quan trọng với giá trị mặc định trong code:

- **`index`** — đường dẫn dưới `wwwroot` tới HTML vào;
- **`basetag`** — chèn `<base>` cho SPA;
- **`git`**, **`tree`** — nguồn cập nhật;
- **`intervalupdate`** — chu kỳ cron (phút);
- **`initPlugins.dorama`** — nối Lampa plugin riêng **`/dorama.js`** vào `/lampainit.js` và `/on.js`;
- **`initPlugins.subsense`** — nối Lampa plugin **`/subsense.js`** vào `/lampainit.js` và `/on.js`; mặc định bật (`true`), tắt bằng cách đặt `false` trong `init.conf`;
- **`limit_map`** — WAF cho **`^/(extensions|testaccsdb|msx/)`**.

## Doramas

Plugin **`plugins/dorama.js`** thêm mục **«Doramas»** vào menu chính của Lampa ngay sau **«Serial»** và đăng ký nguồn riêng **`lampac_dorama`**. Không phụ thuộc SISI, chỉ được nối qua **`LampaWeb.initPlugins.dorama`**.

Nguồn dựng các section thông qua TMDB Discover TV cho phim truyền hình Hàn Quốc: **`with_original_language=ko`**, **`with_genres=18`**, **`include_adult=false`**. Request đi qua TMDB proxy chuẩn của Lampac **`/tmdb/api/3/...`** khi `{localhost}` được thay thế, ngược lại dùng URL-builder TMDB gốc của client. Plugin cũng chặn các link Dorama `category_full` để nút **«Xem thêm»** không rơi vào CUB/TMDB source đang hoạt động.

## SubSense

Plugin **`plugins/subsense.js`** tự động gắn phụ đề tiếng Việt cho Lampa Player. Mỗi lần bắt đầu phát, nó lấy `imdb_id` (và mùa/tập với series) từ card phim, gọi Stremio addon SubSense để lấy danh sách phụ đề, sắp xếp ưu tiên (srt/vtt trực tiếp → zip → khác), convert sang VTT (zip giải nén qua JSZip từ CDN) rồi đưa toàn bộ track vào `Lampa.Player.subtitles()`.

Chỉ được nối qua **`LampaWeb.initPlugins.subsense`** (mặc định bật). Kiểm thử: mở bất kỳ phim nào qua `/lampainit.js` hoặc `/on.js`, bấm phát và xác nhận trong player xuất hiện các track phụ đề «SubSense» / «Tiếng Việt».

Kiểm thử sau khi sửa đổi:

- bật **`LampaWeb.initPlugins.dorama`**;
- mở Lampa qua **`/lampainit.js`** và xác nhận **«Doramas»** đứng ngay sau **«Serial»**;
- mở Lampa qua **`/on.js`** và xác nhận mục **«Doramas»** xuất hiện mà không phụ thuộc SISI;
- mở **«Doramas»** và kiểm tra màn hình section có tải được các hàng phim;
- mở **«Xem thêm»** trong một section bất kỳ và kiểm tra trang 2 tải từ **`lampac_dorama`**, không phải từ CUB;
- khởi động lại app và xác nhận menu vẫn chỉ có đúng một mục **«Doramas»**.

## Phụ thuộc

- Thư mục **`wwwroot/`** ở gốc ứng dụng với **`lampa-main/`** và file tĩnh.
- Plugin của module nằm trong **`plugins/`** (extensions, deny v.v.).

## Thành phần

| Thành phần | Vai trò |
|-----------|------|
| `LampaCron` | Cập nhật từ Git nền theo cấu hình. |
| `ErrorDocController` | Các route lỗi bổ sung (`/e/acb`). |
