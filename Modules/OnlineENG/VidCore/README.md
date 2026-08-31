# VidCore (4K)

Nguồn online **VidCore** — `https://vidcore.io`, host embed có biến thể 4K. Module động
(`dynamic: true`), resolver **thuần HTTP**, **không cần Playwright/Chromium** nên vẫn chạy
trên Termux khi browser hỏng.

Luồng API lấy công thức từ `SaurabhKaperwan/CSX` (`CineStream` → `invokeVidcore`); chỉ dùng
công thức API, không copy code (CSX GPL-3, kho này MIT).

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
- `enabled` (trong init.conf, khác `enable` của manifest): `true` để VidCore **vẫn hiện khi
  `disableEng: true`** — giống VidLink.
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
