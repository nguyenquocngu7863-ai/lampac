# Sổ tay lỗi thường gặp — lampac trên Termux (Ubuntu proot)

> Cẩm nang bỏ túi cho bản cài Termux + Ubuntu proot. Mỗi lỗi kèm cách nhận biết và cách xử lý.
> Nhánh bản vá: `arena/01a06710-lampac`.

---

## 0. Bản đồ thư mục (nhớ cho khỏi nhầm)

| Vị trí | Là gì |
|---|---|
| `~/lampac` (Termux, `/data/data/com.termux/files/home/lampac`) | Reppo source, chứa `setup-termux.sh` |
| `/root/lampac` (bên trong Ubuntu proot) | Bản chạy thật: `module/`, `mods/`, `init.conf`, `wwwroot/` |
| `/root/lampac/module/...` | Module được nạp |
| `/root/lampac/mods/...` | Override — **thắng** `module/`. Có file ở đây là lampac dùng file này |

- Lệnh chạy ở Termux: `lampac start`, `lampac stop`, `jackett status`.
- Vào proot: `proot-distro login ubuntu`.
- File `.cs` (C#) chỉ biên dịch lại khi **start lại** lampac. File `.js` chỉ cần tải lại, nhưng cứ `lampac start` cho chắc.

---

## 1. Lỗi biên dịch C# (lúc `lampac start`)

### 1.1 `error CS0103: Tên "XYZ" không tồn tại trong ngữ cảnh hiện tại`
- **Nguyên nhân:** file `.cs` dùng kiểu tên ngắn nhưng thiếu `using ...` (vd `DecompressionMethods` cần `using System.Net;`).
- **Xử lý:** sửa code dùng tên đầy đủ (vd `System.Net.DecompressionMethods`) hoặc thêm `using` ở đầu file, rồi deploy lại file `.cs` đó + `lampac start`.
- Lỗi đã gặp & sửa:
  - `CS0103 DecompressionMethods` → ghi đầy đủ `System.Net.DecompressionMethods` (commit b48a4b5).
  - `CS1061 ... AsSpan` → thêm `using System;`.
  - `CS0117 ModInit.uhd không tồn tại` → xóa file controller cũ `UhdmoviesController.cs` còn sót (xem 1.3).

### 1.2 Module không nạp, log "im lặng"
- Thường do `mods/<Module>/manifest.json` cũ còn khai báo controller đã bị xóa → trình nạp mở file không có → module chết âm thầm.
- **Xử lý:** đồng bộ lại cả `module/` lẫn `mods/` (setup tự làm), hoặc xóa thư mục module hỏng ở **cả hai** nơi.

### 1.3 File `.cs` rác còn sót sau khi gỡ module
- Khi một controller bị gỡ khỏi repo nhưng file cũ vẫn nằm trong `/root/lampac/module|mods/...`, dotnet vẫn gom nó vào biên dịch → lỗi.
- **Xử lý:** xóa thủ công, ví dụ:
  ```bash
  proot-distro login ubuntu -- rm -f /root/lampac/module/OnlineENG/MoviesHub/UhdmoviesController.cs \
      /root/lampac/mods/OnlineENG/MoviesHub/UhdmoviesController.cs
  ```

### 1.4 `error compilation /root/lampac/module/<TênModule>` rồi crash cả app
- Một module lỗi biên dịch có thể làm **chết luôn tiến trình** (`proot info: terminated with signal 6`).
- **Xử lý:** đọc dòng `error CS....(dòng,cột)` đầu tiên, sửa đúng file đó. Không sửa mò. Sau khi deploy file mới, `lampac start` lại.

---

## 2. Lỗi đường dẫn / lệnh trong Termux

### 2.1 `curl: (23) ... /tmp/sp.json: No such file or directory`
- **Termux KHÔNG có `/tmp`** (chỉ proot Ubuntu mới có).
- **Xử lý:** ghi file tạm vào home: dùng `~/sp.json` thay vì `/tmp/sp.json`.

### 2.2 Sửa file ở proot nhưng Termux "không thấy"
- Nhầm môi trường. Repo source ở `~/lampac` (Termux); bản chạy ở `/root/lampac` (proot).
- File deploy phải ghi vào `/root/lampac/...` **bên trong** `proot-distro login ubuntu`.

### 2.3 Lệnh dán vào Termux bị đơ / lệch dòng
- Không dán lệnh quá dài một khối. Dùng block ngắn, tách `stop` → chạy script → `start`.
- Không gõ tay tiếng Việt có dấu trong lệnh.

---

## 3. Deploy file bản vá (cache CDN)

### 3.1 Tải về vẫn là nội dung cũ dù đã push
- `raw.githubusercontent.com` bị **CDN cache**.
- **Xử lý:** thêm `?cb=$(date +%s)` vào URL, `rm -f` file cũ trước khi tải, và `grep` xác nhận nội dung mới.
- Nếu raw vẫn cache → dùng **api.github.com** (host khác, không cache), đọc field `content` (base64):
  ```
  https://api.github.com/repos/nguyenquocngu7863-ai/lampac/contents/<đường-dẫn-file>?ref=arena/01a06710-lampac
  ```

### 3.2 Mẫu deploy nhanh 1 file C# (chạy trong Termux)
```bash
lampac stop
proot-distro login ubuntu -- bash -c 'cat > /root/upd.py <<"E"
import urllib.request,json,base64,os
BR="arena/01a06710-lampac"
def get(p):
    u="https://api.github.com/repos/nguyenquocngu7863-ai/lampac/contents/"+p+"?ref="+BR
    j=json.loads(urllib.request.urlopen(urllib.request.Request(u,headers={"User-Agent":"curl"}),timeout=30).read())
    return base64.b64decode(j["content"])
src="Modules/LampaWeb/Controllers/ApiController.cs"   # đổi file cần kéo
data=get(src)
for dst in ["/root/lampac/module/"+src.split("Modules/",1)[1],
            "/root/lampac/mods/"+src.split("Modules/",1)[1]]:
    os.makedirs(os.path.dirname(dst),exist_ok=True)
    open(dst,"wb").write(data); print("ghi",dst)
E
python3 /root/upd.py'
lampac start
```
> `mods/` có thể chưa có thư mục → luôn `mkdir -p` / `os.makedirs(..., exist_ok=True)` trước khi ghi.

### 3.3 Cập nhật toàn bộ bản vá
```bash
cd ~/lampac && curl -fSL "https://api.github.com/repos/nguyenquocngu7863-ai/lampac/contents/setup-termux.sh?ref=arena/01a06710-lampac" -o ~/sp.json && grep -o '"content": *"[^"]*"' ~/sp.json | cut -d'"' -f4 | tr -d '\n' | base64 -d > setup-termux.sh && bash setup-termux.sh --sync-all
lampac start
```

---

## 4. Jackett / torrent không ra HASH

### 4.1 Bấm torrent mà TorrServer báo lỗi / "no HASH"
Luồng đúng: Lampa gửi link → lampac `/jackett/resolve` đổi link `.torrent` thành **magnet** (tự đuổi redirect + giữ cookie + băm SHA1 info) → gửi magnet cho TorrServer.
- **Kiểm tra nhanh:** mở **Bảng điều khiển → tab Request**.
  - Nếu thấy `POST .../torrents : Lỗi 400` → link `.torrent` bị gửi thẳng cho TorrServer công khai (nó không tải được file từ máy bạn). Đảm bảo đã có `plugins/jackett.js` mới (hook `$.ajax`/`fetch`/XHR) và `ApiController.cs` mới.
  - Nếu thấy `Jackett resolve failed ...` kèm chi tiết → đọc lý do:
    - `tracker did not return a .torrent (got ... bytes: <html>)` → tracker trả trang đăng nhập/lỗi → cần **cấu hình login/cookie tracker trong Jackett** (không phải lỗi lampac).
    - `tracker HTTP 4xx/5xx at hop N` → tracker chặn/ chết.
    - `too many redirects` → chuỗi redirect quá dài, báo lại để tăng hop.

### 4.2 `GET /gst/echo : 404 Không tìm thấy trang`
- **Vô hại.** Lampa dò TorrServer nhánh GST; server chuẩn chỉ có `/echo`, trả 404 rồi bỏ qua. Không ảnh hưởng phát phim.

### 4.3 Route Jackett trả 503 "Jackett is disabled or api_key is empty"
- `init.conf` thiếu `api_key`. Vào Admin Panel điền Jackett API key, hoặc sửa `/root/lampac/init.conf`:
  ```json
  "Jackett": { "enable": true, "url": "", "port": 9117, "api_key": "<KEY>", "proxy_downloads": true }
  ```
- Jackett chạy riêng: `jackett status`, `jackett start`.

### 4.4 Tracker riêng (nnmclub, toloka, torrentgalaxy...) hay mất phiên
- Các tracker này bắt đăng nhập. Phải khai báo tài khoản/cookie **trong Jackett indexer config**. lampac chỉ đứng giữa đổi ra magnet; không thay được phần đăng nhập tracker.

---

## 5. Admin Panel (Lampa)

- Giao diện dùng **nền sáng, chữ tối** (build `light-v10`), không dùng `backdrop-filter: blur` (lag Android TV).
- Nếu chữ trắng mất trên nền sáng → bản `adminpanel.js` cũ. Kéo lại file đó (nằm trong gói sync) rồi tải lại app.
- Màn sửa config (textarea) dùng màu cố định vì nằm ngoài gốc theme — nếu hỏng, kiểm tra `.lampac-admin-edit` trong `adminpanel.js`.

---

## 6. Nguồn phim / module

- **VidCore**: để tiếng Anh (ENG); **UhdMovies**: đã ẩn (controller gỡ).
- **Stripchat**, **OneJav**: đã gỡ hoàn toàn, không hồi sinh.
- Nguồn RUS mới đồng bộ: **Gencit, Videoseed, FlixCDN** — `setup-termux.sh --sync-all` tự kéo; nếu thiếu sau khi sync, kiểm tra log `[sync] rus/...`.
- Muốn thêm nguồn kiểu MoviesHub: sửa `manifest.json` (mục tree) trong repo, không cần sửa script.

---

## 7. Quy trình an toàn mỗi khi sửa bản vá

1. Sửa trong repo, push nhánh `arena/01a06710-lampac` (không push `main`, không tạo PR).
2. Termux: `lampac stop`.
3. Kéo file (api.github.com base64) ghi **cả** `module/` và `mods/`, `mkdir -p` trước khi ghi.
4. `grep` xác nhận nội dung mới (từ khóa đặc trưng của bản vá).
5. `lampac start`; nếu lỗi compile → đọc dòng `CS....` đầu tiên, sửa đúng chỗ.
6. Test thực tế trên TV/điện thoại; xem tab **Request** trong Admin Panel khi cần log.
