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

## Tự đăng nhập — CHẠY ĐƯỢC (đo thật 2026-10-28): Chrome HEADFUL trên X server ảo

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

### Turnstile: bảng đo (3 biến, chỉ 1 tổ hợp qua)

Login cần token Cloudflare Turnstile (`div[data-recaptcha-key=0x4AAAAAADyqkzaFHEUCkKsz]`,
render trong shadow DOM, đổ vào `input[name=cf-turnstile-response]`). Thiếu token → site trả
`{"status":"failure","errors":[{"code":"required","field":"code","block":"logon"…}]}`.

| Tổ hợp | token |
|---|---|
| headless + spoof `navigator.webdriver=false` + UA Windows + plugins/WebGL spoof | 0 |
| headless + CDP thuần (`--remote-debugging-port`, `Input.dispatchMouseEvent`) | 0 |
| headful + Playwright mặc định (`navigator.webdriver=true`) | 0 |
| **headful + `--disable-blink-features=AutomationControlled` + UA THẬT (không override)** | **730 ký tự ✅** |

⇒ **Cả hai điều kiện đều bắt buộc**: có display thật (headful) **và** `webdriver=false`.
Spoof UA Windows/Linux-đè lên platform thật còn làm CF nghi hơn — đừng đổi UA.

### Hạ tầng X: Xvnc :1 + X11 qua TCP

`termux-x11` cần app Android để hiện hình; không cần — dùng **tigervnc** (`pkg install tigervnc`):

```
Xvnc :1 -geometry 1280x900 -depth 24 -rfbport 5901 -SecurityTypes None -ac -AlwaysShared -listen tcp
```

- Chrome là binary glibc của rootfs ⇒ libX11 tìm socket ở `/tmp/.X11-unix` (Android không có)
  → phải đi **TCP**: `DISPLAY=127.0.0.1:1` (X11 cổng 6001).
- Không cần VNC viewer: ai muốn nhìn thì mở VNC `127.0.0.1:5901`, còn script chạy không cần màn hình thật.

### Script auto-login (nguồn: `termux-native/`)

| File | Việc |
|---|---|
| `termux-native/xnhau-login.sh` | bật Xvnc nếu chưa có, set env (LD_LIBRARY_PATH glibc, FONTCONFIG_FILE, DISPLAY), chạy node |
| `termux-native/xnhau-login.js` | Playwright persistent profile `~/lampac-native/xnhau-profile`; **fast path**: profile còn phiên → xuất cookie ngay; hết phiên → mở popup, điền user/pass, bấm widget (4 lần, cách 8 s), chờ token, submit, chờ "Đăng xuất" |

Deploy + dùng:

```bash
cp termux-native/xnhau-login.sh termux-native/xnhau-login.js ~/lampac-native/
chmod +x ~/lampac-native/xnhau-login.sh
~/lampac-native/xnhau-login.sh saunhung 'matkhau'     # in JSON, ghi ~/lampac-run/xnhau.cookie
```

Đo được: **fast path 4 s** (profile còn phiên), **cold path ~90 s** (profile trắng → giải
Turnstile → login). Exit code: 0 ok, 2 thiếu user/pass, 3 Turnstile không token, 4 sai mật khẩu.

### Cookie: cần `PHPSESSID` **và** `kt_member`

`kt_member=1` (HttpOnly, `Max-Age=86400`) mới là cookie quyết định quyền xem video members-only.
Chỉ gửi `PHPSESSID` → trang video trả `.no-player` "Chỉ có thành viên mới có thể xem" dù
`data-member="2"`. Cookie chuẩn: `PHPSESSID=…; kt_member=1; kt_tcookie=1`.

### Module đọc cookie thế nào

- Ưu tiên `~/lampac-run/xnhau.cookie` (script ghi) → `cookieCache` → `init.conf` `XNhau.cookie`.
- File đọc lại **khi mtime đổi** ⇒ refresh cookie **không cần restart app**.
- `/xnhau/login` → chạy `~/lampac-native/xnhau-login.sh` (Process.Start, user/pass truyền qua
  env, không qua argv), xác minh lại bằng trang chủ rồi trả `ok`.
  *Lưu ý*: app chạy bằng dotnet-host glibc nên process con phải **xoá `LD_LIBRARY_PATH`/`LD_PRELOAD`
  và trả `PATH` về Termux**, nếu không `bash` báo `cannot find "libdl.so"`.
- Video members-only mà `links` rỗng → tự gọi login (throttle **1 lần/5 phút**) rồi fetch lại.

### Endpoint tiện dụng

| URL | Việc |
|---|---|
| `/xnhau/check` | `{"cookie":"alive|dead|missing","member":true,"login":"da-cau-hinh","script":"co","cookieFile":"… UTC","chromium":"headless"}` |
| `/xnhau/login` | chạy script headful → `{"ok":true,"msg":"ok (headful script)"}` hoặc message lỗi cụ thể |

### Fallback thủ công (khi script/Chrome hỏng)

1. Firefox (điện thoại) → `xnhau.pub` → đăng nhập, tick "Ghi nhớ đăng nhập".
2. DevTools/Cookie-Editor → copy `PHPSESSID` (HttpOnly, bookmarklet không đọc được).
3. Dán `PHPSESSID=…; kt_member=1; kt_tcookie=1` vào `~/lampac-run/xnhau.cookie`.
4. `curl -s http://127.0.0.1:9118/xnhau/check` → `"cookie":"alive"`.

`PHPSESSID` là session cookie: chết khi logout, xoá cookie, hoặc server dọn session. Mỗi request
tới site server set lại `kt_member=1; Max-Age=86400` nên **dùng app đều đặn thì session tự gia hạn**;
chết thì script login lại (cold path ~90 s, lần đầu sau đó nhanh vì profile nhớ phiên).
