# VidCore (4K)

Nguồn online **VidCore** — `https://vidcore.io`, host embed có biến thể 4K. Module động
(`dynamic: true`), resolver **thuần HTTP**, **không cần Playwright/Chromium** nên vẫn chạy
trên Termux khi browser hỏng.

Luồng API lấy công thức từ `SaurabhKaperwan/CSX` (`CineStream` → `invokeVidcore`); chỉ dùng
công thức API, không copy code (CSX GPL-3, kho này MIT).

## Trạng thái

Đã xác minh trên thiết bị (Termux, 2026-08-31): movie `1288445` → `2/5 streams`,
TV `125988/1/1` → `1/5 streams`, stream phát qua `HostStreamProxy` bình thường.
Số server lấy được **bằng đúng** số server sống của `vidcore.io` cho title đó — server nào
bị gắn khiên đỏ trong player gốc thì module cũng không lấy được, đó là nguồn chết chứ không
phải bug. `Premiere 4K` chỉ có ở phim lẻ mới phát hành, nên đừng lấy một title cũ làm phép
thử cho 4K. Module giờ nằm trong cả `--sync` nhẹ lẫn `--sync-all`.

## Bẫy đã gặp (đọc trước khi sửa)

- **`dec-vidcore` phải là JSON body thuần.** `Http.Post(url, string data)` của Lampac luôn bọc
  body bằng `StringContent(..., "application/x-www-form-urlencoded")`; enc-dec thấy vậy là trả
  `400 {"error":"Expected body: text"}`. Thêm header `Content-Type: application/json` **không**
  cứu được (content-type đã bị content đặt trước, `TryAddWithoutValidation` thất bại).
  Cách đúng = `new StringContent(json, Encoding.UTF8, "application/json")` rồi gọi
  `Http.Post(url, content, ...)` (như `IptvOnline`/`GetsTV`). Module làm vậy trong `DecJson()`.
  Dấu hiệu trên thiết bị: `VidCore: servers dec rỗng, resp={"status":400,...,"error":"Expected body: text"}`.

- **GET trang player phải là request trần kiểu browser** (chỉ UA + `Accept: text/html`).
  Mang `X-Requested-With: XMLHttpRequest` hoặc `Accept: application/json` (headers của bước
  enc-dec) sang bước này là vidcore.io không trả HTML chứa `\"en\"` nữa, module báo
  `token not found` dù mở link bằng browser vẫn thấy đúng tập. Header XHR chỉ dùng cho
  `enc-vidcore`/`dec-vidcore`/servers/stream.
- URL TV `{{host}}/tv/{tmdb}/{season}/{episode}` và movie `{{host}}/movie/{tmdb}` **chính là
  trang player** (không phải trang watch có iframe) — token nằm inline trong trang đó.

- **Không set `conf.overridehost`/`conf.overridehosts`.** Ở Lampac đó là cơ chế *chuyển request
  sang Lampac instance khác*: `IsRequestBlocked` → `InvokeOverridehost` → `RedirectResult` rồi
  **dừng**, `Index()`/`Video()` không chạy, không log, Lampa báo "không trả về kết quả". Module
  từng dính đúng cái này vì tưởng nó là "danh sách domain của nguồn".
- Mọi đường chặn trong `BaseOnlineController` đều **im lặng** (403 không log). VidCore giờ in
  `VidCore: blocked (...)` ở `Video()` và `VidCore: index không có dữ liệu (...)` ở `Index()`.
- `Index()` có `[Staticache]`: nếu anh vừa đổi config xong mà vẫn thấy hành vi cũ, xoá cache
  `rm -rf /root/lampac/cache/static` rồi `lampac stop && lampac start` (302 cũ có thể còn nằm trong cache).


## Route

| Route | Chức năng |
|---|---|
| `/lite/vidcore` | Màn hình Lampa (`ViewTmdb`), `method: "call"` |
| `/lite/vidcore/video`, `/lite/vidcore/video.m3u8` | Danh sách server + link phát |

## Luồng resolve

```
GET  {host}/movie/{tmdb}            (tv: {host}/tv/{tmdb}/{s}/{e})
     └─ HTML chứa  \"en\":\"<cipher>\"   (fallback: "en":"<cipher>")
GET  {apihost}/enc-vidcore?text=<cipher>
     └─ {result:{servers,stream,token}}
POST {result.servers}  body {}      (kèm X-CSRF-Token)  → ciphertext
POST {apihost}/dec-vidcore {"text":…} → [{name, data}, …]
mỗi server (song song):
POST {result.stream}/{data} body {} → ciphertext → dec-vidcore → url | stream_url
```

Cache 15 phút theo key `vidcore:{movie|tv}:{tmdb}:{season}:{episode}`; một server chết chỉ
bị loại, không kéo cả dãy (mỗi server một `try/catch`).

## Cấu hình

```jsonc
"VidCore": {
  "enable": true,
  "host": "https://vidcore.io",
  "apihost": "https://enc-dec.app/api",   // dịch vụ giải mã, TỰ HOST được
  "httptimeout": 25,
  "streamproxy": true,
  "displayindex": 1016
}
```

- `apihost` là điểm **phụ thuộc bên thứ ba**. `enc-dec.app` còn sống (kiểm tra 2026-08-31)
  nhưng chính nó đã **gỡ** `/api/enc-mapple` và làm chết nguồn Mapple + provider nuvio.
  Đổi `apihost` sang instance tự chạy là hết phụ thuộc.
- `enable` (mặc định `true` ngay trong `ModInit`, không cần viết section `VidCore`): đây là
  công tắc mà `IsRequestBlockedRchOrDisable` đọc — tắt nó thì mọi request trả 403 **không log**.
  Muốn tắt nguồn: `"VidCore": { "enable": false }`.
- `enabled` (mặc định `true`): khác `enable`. `enabled` chỉ quyết định VidCore có **xuất hiện
  trong danh sách Online** khi `disableEng: true` hay không. Hai cái độc lập, và ModInit của
  mọi nguồn ENG chỉ set `enabled` — VidCore set cả hai, vì `base.conf` để `disableEng: true`
  mà `init.conf` thì không có section cho module mới.
- `streamproxy` bắt buộc bật: stream cần `Referer` mà APK Lampa không gửi.
- Phụ đề (`result.tracks[]`) chưa nối vào `VideoTpl.subtitles`; StremioSub lo phần đó.

## Cài

`bash setup-termux.sh --sync-all` (khối VidCore trong `install_custom_modules()` tạo thư mục
+ `manifest.json` vì module không có trong `lampac-nextgen.zip`). Chủ ý **không** nằm trong
`--sync` nhẹ — xem "Quy trình làm addon" ở README gốc.

## Test

```bash
lampac vidcore 155            # movie (The Dark Knight)
lampac vidcore 2389 1 1       # series: tmdb season episode
```

`lampac vidcore` do `create_launcher()` sinh ra, in: mã HTTP của `/lite/vidcore` (kèm một
*route ma* để so — hai mã bằng nhau nghĩa là module chưa đăng ký route), mã HTTP + 500B đầu
của `/lite/vidcore/video`, rồi phân loại body:

| Body | Nghĩa |
|---|---|
| có `"host"` | ✅ ra link, mở Lampa chọn host được |
| `{"error":"resolve"}` | exception trong `Resolve` — xem `VidCore: ex …` |
| `{"error":"stream"}` | chạy hết nhưng không ra link — xem các dòng `VidCore: …` |
| khác | thường là 500; `base.conf` để `"exceptionHandlerLogTarget": "none"` nên app không ghi lại dấu vết |

Log chi tiết (`VidCore: 5 servers (movie:155)`, `Supreme ok`, `Supreme no url, dec=…`,
`VidCore: token not found in …`, `VidCore: enc-vidcore incomplete …`) in ra terminal đang
chạy `lampac start`. Test thật vẫn là mở phim trong Lampa → danh sách Online → `VidCore (ENG)`.

## Ghi chú từ lần xác minh trên thiết bị (2026-08-31)

- `compilation VidCore` + `loaded module: VidCore`, không `error CS` ⇒ module compile/nạp sạch.
- `POST {result.servers}` **phải gửi body `{}`**; body rỗng trả 0 byte ⇒ `PostCipher` thử `{}`
  trước, body rỗng chỉ là fallback cho build cũ.
- Server thực tế: `Supreme, Prime, Orbit, Premiere 4K, Horizon`.
- `result` của `dec-vidcore` có thể là mảng JSON **hoặc một chuỗi JSON đã escape** — `Unwrap()`
  xử cả hai, và mọi chỗ đọc JSON đi qua `ParseJson/Child/Text/Pick` (không dùng `Get<JObject>`,
  không gọi `Value<T>(key)` trên `JValue`) để không có đường nào ném exception.
