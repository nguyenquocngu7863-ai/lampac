# JavQuick (module `javquick`)

Site: https://javquick.com — SPA nhẹ kiểu Tailwind (server trả HTML đủ card), route `/javquick`.

## Công thức

```
list  : {host}/                     (trang 1: 24 card, các trang sau: 12 card)
        {host}/?page=N
        {host}/release | /upcoming
        {host}/genres/{id}/{slug}   -> c=%2fgenre%2f{id}%2f{slug}
        {host}/maker/{id}/{slug}    -> c=%2fmaker%2f{id}%2f{slug}
search: {host}/search?q=<q>&page=N
detail: {host}/movie/{id}/{slug}
player: {host}/watch?token={data-id}   (data-id lấy từ id="video-player")
```

Card: `<article>` → `a[href="/movie/..."]`, tên ở `<h2 title="...">`, poster `data-srcset`
(fallback `img.src`). **Site không in `duration`/`quality` trên card ⇒ module không set
`time`/`quality`** (đúng luật 3 trường: không bịa).

Phân trang chỉ là `?page=N`, site không công bố tổng số trang ⇒ `total_pages=0`
(`sisi.js` tự mặc định 30 khi thiếu/0).

## Player (đã có từ trước, giữ nguyên)

`/watch?token=` trả 1 trong 2 dạng:

- `https://...mp4` → Redirect `HostStreamProxy` (Lampac proxy kèm Referer)
- `<ticket>|<iv>|<url1>|...` → các part mã hoá **AES-128-CTR** (key `7ExuG3hz8CGdVZ5s` lấy
  từ `decryptor.wasm`, counter 128-bit big-endian reset mỗi 6 MB), module tự giải rồi
  stream theo Range — xem `Controller.StreamAsync`.

## Sự cố 2026-11-21: home 503 "playlists" (đã sửa)

Triệu chứng: `/javquick` trả `503 {"msg":"playlists"}` sau **~50 s** (2 lần thử × timeout).

Đo:

| lệnh | kết quả |
|---|---|
| `curl --http1.1 -A <Chrome UA> https://javquick.com/` | **200**, 2/2 lần đầu |
| `curl` mặc định (chào ALPN **h2**) | `TLS connect error: unexpected eof while reading`, 15 s |
| `curl --http2` | y hệt h2 |
| `curl_cffi impersonate=chrome124` | `SSL_connect: Connection closed abruptly` |
| sau ~6 request | cả h1.1 cũng bị cắt (IP bị siết) |
| `jina` (egress khác) | 200 — site còn sống |

⇒ Cloudflare của site **cắt TLS khi client chào h2** và siết IP sau vài request; ảnh
(`ie2.javquick.com`) trả 403 trực tiếp. Sửa 2 lớp:

1. `ModInit`: `httpversion = 1` (giống JavMoi: h2 của .NET/curl bị cắt).
2. **Relay qua Cloudflare Worker** (`Tools/cf-relay`, env `JAVQUICK_RELAY`):
   - `JavQuickTo.Relay(url)` wrap mọi URL `javquick.com` / `ie2.javquick.com`
   - dùng ở `FetchHtmlAsync` (list, detail, `/watch`, `/genres`, `/makers`) và ở `picture`/`bookmark.image`
   - worker `xn-relay` phải **allow `javquick.com`** (deploy.py) — nhớ allow cả host ảnh vì
     `allowed()` so khớp cả subdomain.

Kết quả sau sửa: home 200 trong **0.02–1.1 s**, 24 item + 8 dòng menu; genre/maker/search/p2
200; `vidosik` → `video.mp4` 302 → 206 `video/mp4` (`ffprobe`: h264 1920x1080 + aac, 4055 s);
poster qua `/proxyimg` 200 `image/jpeg`.

## Menu

1. Tìm kiếm
2. Danh sách: Mới nhất (`c=release`) / Sắp ra mắt (`c=upcoming`) / Trang chủ
3. Thể loại (1/2, 2/2) — 361 mục động từ `/genres`
4. Hãng (1/4…4/4) — 947 mục động từ `/makers`

Site **không có sort** ⇒ không có dòng "Sắp xếp" (đúng luật, không làm dòng 2 giả).

## Popup long-press

`/javquick/genres?uri=<detail>` → nhóm `Thể loại` / `Hãng` / `Nhãn` + `Liên quan`
(`?rel=` lấy sẵn trong trang detail).

## Deploy

```bash
~/lampac-native/csc/check.sh ~/lampac-clean/Modules/Adult/JavQuick
cp ~/lampac-clean/Modules/Adult/JavQuick/{Controller.cs,ModInit.cs,Service.cs,manifest.json} \
   ~/lampac-run/module/Adult/JavQuick/
python3 ~/lampac-clean/Tools/cf-relay/deploy.py     # nếu đổi ALLOW (KHÔNG dùng --test-host để đổi allow)
lampac nstop; rm -rf ~/lampac-run/cache/fdb ~/lampac-run/cache/static; lampac nstart
```

Env `JAVQUICK_RELAY` đã pin trong `termux-run-lampac.sh` / `~/lampac-native/run-lampac.sh`.
Tắt relay: `JAVQUICK_RELAY=''`.