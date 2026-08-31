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

## Kiểm tra nhanh

```bash
# route đã được nạp chưa (200/302 = OK, 404 = module chưa vào)
curl -s -o /dev/null -w "%{http_code}\n" "http://127.0.0.1:9118/lite/vidcore?tmdb_id=155&rjson=1"

# log khi resolve
lampac start 2>&1 | grep "VidCore:"
#  VidCore: 3/5 streams (movie:155)  ← có link
#  VidCore: token not found in …      ← site đổi cách nhét token, phải sửa regex
#  VidCore: enc-vidcore incomplete     ← apihost hỏng / bị gỡ endpoint
```
