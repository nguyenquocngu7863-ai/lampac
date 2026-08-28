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
| `/subsense-auto.js` | Plugin **SubSense Auto** (opt-in), được đăng ký giống `ts.js` và tự gắn phụ đề từ addon SubSense. |
| `/subsense.js` | Plugin **SubSense** (legacy opt-in) — tự động gắn phụ đề tiếng Việt cho player (nguồn SubSense, chuyển đổi srt/zip → VTT). |
| `/stremiosub.js` | Plugin **StremioSub** — ưu tiên subtitle resource của AIOStreams, fallback về SubDL và SubSource. |
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
- **`initPlugins.stremiosub`** — nối plugin **`/stremiosub.js`** vào `/lampainit.js` và `/on.js`; ưu tiên AIOStreams nếu module AIO đã bật, nếu không dùng Stremio SubDL + SubSource, bật mặc định.
- **`initPlugins.subsenseAuto`** — nối plugin gốc **`/subsense-auto.js`** vào cùng danh sách với **`ts.js`**; mặc định tắt. Bật nó **thay cho** `stremiosub` nếu muốn dùng addon SubSense.
- **`initPlugins.subsense`** — plugin **`/subsense.js`** legacy, mặc định tắt; chỉ bật thay cho `stremiosub`.
- **`initPlugins.subfinder`** — plugin **`/subfinder.js`**, mặc định tắt; chỉ bật thay cho `stremiosub`.
- Chỉ chọn **một** trong `subsenseAuto`, `subsense`, `subfinder`, `stremiosub`; server sẽ ưu tiên theo thứ tự đó nếu lỡ bật nhiều cờ. Các plugin cũng có khóa dùng chung để raw URL cũ không bọc `Lampa.Player.play` lần nữa.
- **`limit_map`** — WAF cho **`^/(extensions|testaccsdb|msx/)`**.

## Doramas

Plugin **`plugins/dorama.js`** thêm mục **«Doramas»** vào menu chính của Lampa ngay sau **«Serial»** và đăng ký nguồn riêng **`lampac_dorama`**. Không phụ thuộc SISI, chỉ được nối qua **`LampaWeb.initPlugins.dorama`**.

Nguồn dựng các section thông qua TMDB Discover TV cho phim truyền hình Hàn Quốc: **`with_original_language=ko`**, **`with_genres=18`**, **`include_adult=false`**. Request đi qua TMDB proxy chuẩn của Lampac **`/tmdb/api/3/...`** khi `{localhost}` được thay thế, ngược lại dùng URL-builder TMDB gốc của client. Plugin cũng chặn các link Dorama `category_full` để nút **«Xem thêm»** không rơi vào CUB/TMDB source đang hoạt động.

## SubSense

Plugin **`plugins/subsense.js`** tự động gắn phụ đề tiếng Việt cho Lampa Player. Mỗi lần bắt đầu phát, nó lấy `imdb_id` (và mùa/tập với series) từ card phim, gọi Stremio addon SubSense để lấy danh sách phụ đề, sắp xếp ưu tiên (srt/vtt trực tiếp → zip → khác), convert sang VTT (zip giải nén qua JSZip từ CDN) rồi đưa toàn bộ track vào `Lampa.Player.subtitles()`.

Plugin này là provider legacy và mặc định tắt. Nếu bật **`LampaWeb.initPlugins.subsense`**, hãy tắt `subsenseAuto`, `subfinder` và `stremiosub`; server chỉ đăng ký một provider tự động cho mỗi client. Nếu endpoint SubSense đang lỗi 502 thì nên dùng `stremiosub` thay thế, không phải lỗi JSON của Lampac.

Muốn dùng **addon trong root `subsense-auto.js`** ngang hàng với `ts.js`, cấu hình như sau:

```json
"LampaWeb": {
  "initPlugins": {
    "torrserver": true,
    "subsenseAuto": true,
    "subsense": false,
    "subfinder": false,
    "stremiosub": false
  }
}
```

Không đồng thời thêm cùng URL vào `customPlugins`; URL **`/subsense-auto.js`** đã được server đăng ký và phục vụ qua LampaWeb. Nếu muốn dùng provider đang ổn định hơn trong lúc SubSense host chết, để `subsenseAuto: false` và `stremiosub: true`.

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
| `LampaCron` | Cập nhật từ Git nền theo cấu hình; cài `lang/vi.js` và vá `meta.js` + `app.min.js`. |
| `LampaVietnamese` | Chèn `vi` vào registry ngôn ngữ gốc của Lampa (file `meta.js`/`app.min.js`). |
| `ErrorDocController` | Các route lỗi bổ sung (`/e/acb`). |
