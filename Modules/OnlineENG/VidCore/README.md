# VidCore (4K)

Nguồn online **VidCore** — `https://vidcore.io`, host embed có biến thể 4K. Module động
(`dynamic: true`), resolver **thuần HTTP**, **không cần Playwright/Chromium** nên chạy được
trên Termux kể cả khi browser hỏng.

Cổng này lấy công thức từ `SaurabhKaperwan/CSX` (`CineStream` → `invokeVidcore`) — chỉ dùng
**luồng API** (facts), không copy code (CSX GPL-3, kho này MIT).

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
     └─ {result:{servers, stream, token}}
POST {result.servers}               (header X-CSRF-Token: <token>)
     └─ ciphertext → POST {apihost}/dec-vidcore {"text":…} → [{name, data}, …]
mỗi server:
POST {result.stream}/{data}  → ciphertext → dec-vidcore → {result:{url, tracks[]}}
```

Tất cả server được gọi **song song** (`Task.WhenAll`), server chết bị loại, không kéo cả dãy.
Kết quả cache 15 phút theo key `vidcore:{movie|tv}:{tmdb}:{s}:{e}`.

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

- `apihost` là điểm **phụ thuộc bên thứ ba**. `enc-dec.app` hiện còn sống (`/api/enc-vidcore`
  trả `200` + `{servers, stream, token}`, ngày 2026-08-31) nhưng chính nó đã **gỡ**
  `/api/enc-mapple` và làm chết nguồn Mapple + provider nuvio. Đổi `apihost` sang instance
  tự chạy là hết phụ thuộc.
- `enabled` (bảng điều khiển, khác `enable` của manifest): `true` để VidCore **vẫn hiện khi
  `disableEng: true`** — giống VidLink. Đặt `false` nếu muốn nó biến mất cùng nhóm ENG.
- `streamproxy` bắt buộc bật: `stream_url` của VidCore cần `Referer` mà APK Lampa không gửi.
- Phụ đề của track VidCore (`result.tracks[]`) **chưa** nối vào `VideoTpl.subtitles`; phụ đề
  đang do StremioSub (`/stremiosub.js`) đảm nhiệm, bật một chỗ là đủ.

## Cài / cập nhật

Module **không có trong `lampac-nextgen.zip`**, nên phải được tạo mới trong Ubuntu:

```bash
bash setup-termux.sh --sync-all     # hoặc --install / --update
lampac stop && lampac start
```

`install_custom_modules()` tạo `/root/lampac/module/OnlineENG/VidCore` + chép
`manifest.json`, `Controller.cs`, `ModInit.cs`. Chủ ý **không** nằm trong `--sync`
(danh sách vá nhẹ) cho tới khi ổn định — xem "Quy trình làm addon" ở README gốc.

## Bài học compile/nạp (đã xác minh trên thiết bị)

`bash vidcore.sh log` cho thấy `compilation VidCore` + `loaded module: VidCore` và **không** có
`error CS` nào — module compile và nạp sạch. Vì vậy khi route trả body **rỗng** mà log không có
dòng `VidCore:` nào, thủ phạm là **exception chưa được bắt**: `config/base.conf` đặt
`"exceptionHandlerLogTarget": "none"`, nên Lampac không ghi lại dấu vết 500 ở đâu cả.
Module giờ tự bắt exception ở mọi bước và tự in log, không phụ thuộc exception handler nữa.
Bật trace toàn cục (tuỳ chọn, ích cho mọi module): thêm `"exceptionHandlerLogTarget": "file"`
vào `init.conf` → xem `/root/lampac/logs/exceptionHandler.log`.

## Đã xác minh trên thiết bị (2026-08-31, mode `chain`)

- `vidcore.io/movie/155` còn sống, cipher bắt được bằng mẫu escaped.
- `enc-vidcore` + `dec-vidcore` trên `enc-dec.app` trả đúng shape.
- **`POST {result.servers}` phải gửi body `{}`** — body rỗng trả về 0 byte. `PostCipher`
  vì vậy thử `{}` trước; body rỗng chỉ còn là fallback cho build cũ.
- Server thực tế trả về: `Supreme, Prime, Orbit, Premiere 4K, Horizon`.
- `result` của `dec-vidcore` có thể là mảng JSON **hoặc một chuỗi JSON đã escape** —
  `Unwrap()` xử lý cả hai (và tránh `JToken.Value<T>(key)` nổ trên `JValue`).

## Kiểm tra nhanh

### Cách 1 — thẳng trong Lampac (đây là cách nên dùng hằng ngày)

`lampac vidcore` do `create_launcher()` sinh ra, có sẵn sau `bash setup-termux.sh --sync-all`:

```bash
lampac vidcore                 # movie 155 (The Dark Knight)
lampac vidcore 680             # movie khác theo TMDB id
lampac vidcore 2389 1 1        # series: tmdb / season / episode
```

Nó in: mã HTTP của `/lite/vidcore` (kèm **route ma** để so — 2 mã bằng nhau nghĩa là module
chưa đăng ký route), mã HTTP + 500B đầu của `/lite/vidcore/video`, rồi phân loại:

| Body | Nghĩa |
|---|---|
| có `"host"` | ✅ ra link, mở Lampa chọn host được |
| `{"error":"resolve"}` | exception bên trong `Resolve` — xem `VidCore: ex …` |
| `{"error":"stream"}` | chạy hết nhưng không ra link — xem các dòng `VidCore: …` |
| khác | thường là 500; bật `"exceptionHandlerLogTarget": "file"` trong `init.conf` để thấy `[GlobalError]` |

Log chi tiết (`VidCore: 5 servers (movie:155)`, `Supreme ok`, `no url, dec=…`) in ra terminal
đang chạy `lampac start`, nên tốt nhất: terminal 1 `lampac restart`, terminal 2 `lampac vidcore 155`.

### Cách 2 — script rời, dò cả 5 hop API bằng curl (không đụng Lampac)

`termux-test-vidcore.sh` ở gốc repo, chạy trong Termux (không cần clone):

```bash
curl -fsSL "https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a05799-lampac/termux-test-vidcore.sh?cb=$(date +%s)" -o vidcore.sh && bash vidcore.sh
```

| Lệnh | Làm gì |
|---|---|
| `bash vidcore.sh chain` | Dò **5 hop** API bằng curl **trong Ubuntu**, không đụng file, không restart. In ra đúng mắt xích hỏng: trang → cipher → `enc-vidcore` → `POST servers` → `dec-vidcore` → `stream/<data>` → url |
| `bash vidcore.sh install` | Backup rồi chép `manifest.json`, `Controller.cs`, `ModInit.cs` vào `/root/lampac/module/OnlineENG/VidCore` |
| `bash vidcore.sh serve` | Bật Lampac tách tiến trình (log `~/.vidcore-test.log`), gọi `/lite/vidcore` + `/lite/vidcore/video?id=…`, in `error CS` nếu module không compile |
| `bash vidcore.sh rollback` | Trả lại bản backup (hoặc xoá module nếu chưa có backup) |

Mặc định test `TMDB=155` (The Dark Knight). Phim bộ: `TMDB=<id> SEASON=1 EPISODE=1 bash vidcore.sh chain`.
`chain` dùng `enc-dec.app`; đổi bằng `VIDCORE_API=https://… bash vidcore.sh chain`.

Cách thủ công:

```bash
# route đã được nạp chưa (200/302 = OK, 404 = module chưa vào)
curl -s -o /dev/null -w "%{http_code}\n" "http://127.0.0.1:9118/lite/vidcore?tmdb_id=155&rjson=1"

# log khi resolve
lampac start 2>&1 | grep "VidCore:"
#  VidCore: 3/5 streams (movie:155)  ← có link
#  VidCore: token not found in …      ← site đổi cách nhét token, phải sửa regex
#  VidCore: enc-vidcore incomplete     ← apihost hỏng / bị gỡ endpoint
```
