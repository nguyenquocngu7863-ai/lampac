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
