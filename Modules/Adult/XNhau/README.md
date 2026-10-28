# XNhau (xnhau.limo)

Module Adult cho nguồn xnhau.limo (Việt).

## Cấu hình
- `manifest.json`: module Adult, `enable: true`, `dynamic: true`
- `Controller.cs`: endpoint `/xnhau` (list/search/member feed), `/xnhau/strem` (stream), `/xnhau/uploader` (lấy tên người đăng)
- `Service.cs`: parse HTML, regex `div.item`, stream links từ file:...

## Pagination
- Category (`the-loai/`): dùng `?from=`
- Search (`search/`): dùng `?from_videos+from_albums=` (từ HTML `data-parameters`)
- Member feed (`members/`): dùng `?from=`

## Đã fix
1. Deduplicate playlist items (`DistinctBy` video URI) — tránh card trùng
2. Pagination search: dùng đúng param `from_videos+from_albums`
3. Empty list cho `pg > 1` khi không có thêm nội dung — tránh lặp 24 kết quả

## Cache & Build — ĐỌC TRƯỚC KHI FIX
- Repo: `~/lampac-clean/Modules/Adult/XNhau/` · runtime: `~/lampac-run/module/Adult/XNhau/`
- Runtime **tự compile `*.cs`** lúc start (Roslyn) — KHÔNG cần `dotnet build`, KHÔNG cần DLL.
  Sửa code → `cp 3 file .cs + manifest.json` sang runtime → restart → log có `compilation XNhau` + `loaded module: XNhau`.
- Check compile nhanh (~1–2 s): `~/lampac-native/csc/check.sh Modules/Adult/XNhau`
- Không tự compile lại từ module cũ: nếu API trả kết quả cũ → chưa restart, hoặc `hybridCache` còn (ttl 20–40 s).

## Video "chỉ thành viên mới xem" — cần cookie đăng nhập (2026-10-28)

Một số video do member up bị site khoá: HTML có `<div class="no-player">` +
*"Chỉ có thành viên mới có thể xem"*, **không có** `video_url`. Trước khi fix,
module rơi xuống fallback `<source src>` và bắt trúng **video quảng cáo**
`/static/media/pc-cm88-*.mp4` (giống hệt ở mọi trang, lại là URL tương đối)
⇒ mọi video private trả cùng 1 token `/proxy/` ⇒ proxy **400 rỗng** ⇒ app báo
*"Failed to load because no supported source was found"*.

Đo trên site:
- `PHPSESSID` là **HttpOnly** ⇒ `document.cookie`/bookmarklet KHÔNG đọc được, phải
  DevTools → Application → Cookies (hoặc extension Cookie-Editor trên Firefox Android).
- Có `PHPSESSID` ⇒ trang detail trả `video_url` / `video_alt_url` bình thường.
- **File mp4 không cần cookie**: token `v-acctoken` đã ký sẵn trong URL
  (`.../get_file/3/<md5>/247000/<id>/<id>.mp4/?v-acctoken=...`), tải được cả khi
  không gửi cookie. Vì vậy chỉ cần cookie ở bước lấy trang detail.

Cấu hình (`~/lampac-run/init.conf`, thêm section nếu chưa có):

```json
"XNhau": {
  "enable": true,
  "cookie": "PHPSESSID=xxxxxxxxxxxxxxxxxxxxxxxxxx; kt_tcookie=1"
}
```

- Cookie hết hạn / bị logout ⇒ video private lại 503 `stream_links`. Cách làm mới:
  login `xnhau.pub` trên browser → DevTools → Application → Cookies → copy `PHPSESSID`.
- Login bằng script (curl_cffi) **không được**: form login có Cloudflare Turnstile,
  POST bằng script trả về y hệt nhau cho cả mật khẩu đúng và sai (không set session).
  Muốn tự động hoàn toàn thì phải có Chromium thật (Playwright) để giải Turnstile.

## Đã fix
1. Deduplicate playlist items (`DistinctBy` video URI) — tránh card trùng
2. Pagination search: dùng đúng param `from_videos+from_albums`
3. Empty list cho `pg > 1` khi không có thêm nội dung — tránh lặp 24 kết quả
4. (2026-10-28) Fallback `<iframe>/<source>`: bỏ qua ads `/static/media/*` và `/embed/*`,
   đồng thời tuyệt đối hoá URL tương đối (trước đây `/static/media/...` vào proxy → 400)
5. (2026-10-28) Gửi `init.cookie` khi fetch trang detail + trang member (video private)


## Domain (cap nhat 2026-10-18)

Site **doi domain lien tuc**: `xnhau.cab` -> `xnhau.limo` -> `xnhau.free` -> **`xnhau.pub`**.
`xnhau.free` gio tra **301 Moved Permanently** (body 162 byte, khong co card) va fetch cua module
**khong di theo redirect** -> parse ra 0 item -> app bao 503 `playlists`.

Da sua 5 cho (het `.free` -> `.pub`):
- `ModInit.cs`: `SisiSettings(..., host)` + 2 `referer` + fallback `?? "https://xnhau.pub"`
- `Service.cs`: `SiteHost`

Cach do khi nghi doi domain (khong can sua gi): lay HTML that roi chay regex card cua module —
neu regex khop ma module van 0 item thi loi o **fetch/host**, khong phai parser.
Vi du 2026-10-18: regex khop **40 card** tren HTML `.pub` nhung module 0 item.

## Tự đăng nhập bằng Chrome — ĐO THẬT 2026-10-28 (không khả thi khi headless)

Thiết bị **có** Chrome thật: `~/lampac-native/chrome/chrome-run.sh` (Google Chrome 155),
Lampac bật qua `init.conf` → `chromium.executablePath`. Kiểm tra sống/chết:
`curl -s http://127.0.0.1:9118/api/chromium/ping` → `pong`.

### Bug thật đã fix: Chrome crash vì fontconfig

Trước fix, **mọi** trang có chữ Việt (dấu) làm Chrome chết ngay:

```
FATAL:third_party/skia/src/ports/SkFontMgr_FontConfigInterface.cpp:163] Not implemented.
```

→ Playwright chỉ báo `TargetClosedException: Target page, context or browser has been closed`,
rất dễ đoán sai là "site chặn". Nguyên nhân: Chrome chạy **native (ngoài proot)** nên
`/etc/fonts/fonts.conf` của Ubuntu trỏ `/usr/share/fonts` (không tồn tại) ⇒ fontconfig trả
**0 font** ⇒ Skia chết khi gặp ký tự cần fallback.

Fix nằm trong `termux-native/chrome-run.sh`: tự sinh `~/lampac-native/fonts.conf`
(trỏ font của rootfs Ubuntu + `/system/fonts` của Android, cache `~/lampac-native/fontcache`)
và `export FONTCONFIG_FILE`. Deploy:
`cp termux-native/chrome-run.sh ~/lampac-native/chrome/chrome-run.sh && chmod +x …`

### Vì sao KHÔNG tự đăng nhập được

Luồng login đã chạy tới đúng chỗ: mở popup → điền `saunhung`/mk → **POST `/login/` thật sự
được gửi**. Site trả:

```json
{"status":"failure","errors":[{"code":"required","field":"code","block":"logon",
 "message":"Ô này là bắt buộc"}]}
```

`code` = token **Cloudflare Turnstile** (widget `data-recaptcha-key=0x4AAAAAADyqkzaFHEUCkKsz`,
render trong shadow DOM, `input[name=cf-turnstile-response]`). Token **luôn rỗng** vì headless
bị chặn. Đã thử, tất cả đều thất bại:

| Thử | Kết quả |
|---|---|
| `turnstile.render()` thủ công vào div có sẵn / div mới | `already rendered`, vẫn 0 iframe, 0 token |
| click chuột theo toạ độ checkbox (Playwright `mouse.click`) | checkbox không tick |
| Enter/Space sau khi focus widget | không tick |
| spoof `navigator.webdriver=false`, plugins=5, languages, hardwareConcurrency, `window.chrome` | token vẫn 0 |
| WebGL swiftshader (`--enable-unsafe-swiftshader --use-angle=swiftshader`) | token vẫn 0 |
| **Chrome thường + CDP thuần** (không Playwright, `--remote-debugging-port`, click bằng `Input.dispatchMouseEvent`) | token vẫn 0 |

⇒ Site chỉ cho login khi captcha được giải bởi trình duyệt **headful** (có màn hình, người thật
tick). Không có root, không đọc được cookie Firefox của máy (`/data/data/org.mozilla.firefox`
không truy cập được).

### Endpoint tiện dụng

| URL | Việc |
|---|---|
| `/xnhau/check` | `{"cookie":"alive|dead|missing", "member":true, "chromium":"headless"}` |
| `/xnhau/login` | thử tự login bằng Chrome; fail → trả message rõ ràng (Turnstile chặn headless) |

### Cách làm mới cookie (khi `/xnhau/check` = `dead`)

1. Firefox (điện thoại) → `xnhau.pub` → **đăng nhập, tick "Ghi nhớ đăng nhập"** (session sống lâu hơn).
2. DevTools/extension Cookie-Editor → copy `PHPSESSID` (HttpOnly, bookmarklet không đọc được).
3. Dán vào `~/lampac-run/init.conf` → `"XNhau".cookie` → khởi động lại app.
4. Kiểm tra: `curl -s http://127.0.0.1:9118/xnhau/check` → `"cookie":"alive"`.

`PHPSESSID` là session cookie: chết khi logout, xoá cookie, hoặc server dọn session
(không dùng vài giờ → 1 ngày). Mỗi request tới site server set lại `kt_member=1; Max-Age=86400`
nên **dùng app đều đặn thì session được gia hạn**; bỏ lâu là mất.
