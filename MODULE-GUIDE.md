# MODULE-GUIDE.md — Quy trình làm module Adult (BẮT BUỘC đọc trước khi viết code)

> File này dành cho **mọi AI agent** (Kiro, Opencode, Devin, ...).
> Viết xong mà không build + deploy + smoke test = CHƯA XONG.

Repo: Lampac (ASP.NET Core, `net10.0`).
Runtime: `/lampac-run` (Termux, app đang chạy). Repo source: thư mục này.
`~/lampac-run` và repo là 2 bản khác nhau — sửa repo KHÔNG tự hiện lên app.

---

## 1. Repo chuẩn là BÊN TRÁI (~/lampac, nhánh arena), KHÔNG phải repo này

- Bên phải (`lampac-clean`) là bản để AI vọc. Khi tạo module mới ở đây, đồng bộ
  ngược về `~/lampac` (nhánh `arena/...`) rồi mới tính chuyện deploy lên app.
- Đừng mở PR từ repo này khi chưa đối chiếu với `~/lampac`.

## 2. Cấu trúc module (copy SupJav, đừng sáng tạo)

Mẫu chuẩn: `Modules/Adult/SupJav/` — 5 file:

```
Modules/Adult/<Name>/
  <Name>.csproj      ← BẮT BUỘC, xem §3
  manifest.json      ← name, site, enable:true, dynamic:true
  ModInit.cs         ← IModuleLoaded + IModuleSisi, SisiSettings
  Service.cs         ← Uri(), Playlist(), Pages(), Menu() — hàm thuần
  Controller.cs      ← kế thừa BaseSisiController
```

Namespace = `<Name>`, route = tên module lowercase (`hornyjav`).

### `.csproj` — CHỈ 16/43 module mới có (đo trong repo: 2026-10)

`Modules/Adult/` có 43 module nhưng **chỉ 16 cái có `.csproj`**. Module không
có `.csproj` vẫn chạy: `Core.dll` biên dịch mọi file `.cs` trong
`/lampac-run/module/` lúc runtime.

Kiểm tra trước khi copy mẫu:

```bash
ls Modules/Adult/<Name>/*.csproj   # có thì theo §3, §4
                                    # không thì copy 4 file từ module
                                    # không có csproj, BỎ QUA §3 và §4
```

Module KHÔNG có `.csproj` (mẫu để copy): `SupJav`, `JavGuru`, `JavMoi`,
`Jable`, `VNsexTop1`. Mẫu chuẩn để copy 4 file nên là `SupJav`.

### Model SISI — dùng SAI FIELD là lỗi build (đã dính 2 lần)

```csharp
using Shared.Models.SISI.Base;

// PlaylistItem (lowercase, KHÔNG phải Title/Url/Poster):
new PlaylistItem() { name = title, video = detailUrl, picture = poster, time = duration };

// MenuItem (lowercase, KHÔNG có items/url):
new MenuItem() { title = "Censored", playlist_url = root + "/category/censored" };
// submenu con:
submenu = new List<MenuItem> { new MenuItem() { title = ..., playlist_url = ... } }
```

File định nghĩa: `Shared/Models/SISI/Base/PlaylistItem.cs`,
`Shared/Models/SISI/Base/MenuItem.cs`. **Đọc 2 file này trước khi gán field.**

## 3. Khai báo vào NextGen.slnx — CHỈ khi module CÓ `.csproj`

Thêm dòng vào `<Folder Name="/Modules/Adult/">`, giữ thứ tự ABC:

```xml
<Project Path="Modules/Adult/<Name>/<Name>.csproj" />
```

**Không có `.csproj` thì BỎ QUA mục này.** Thêm line trỏ tới file không tồn
tại làm hỏng **cả solution** (đã dính: 2 line thừa — `HornyJav` không tồn
tại, `VNsexTop1` không có `.csproj` — khiến build toàn repo fail với
`MSB3202: Project file does not exist`, kéo theo mấy module khác fail theo).

## 4. Build — BẮT BUỘC khi có `.csproj`, không được rào "không có môi trường"

```bash
dotnet build Modules/Adult/<Name>/<Name>.csproj --nologo 2>&1 | tail -5
```

- `0 Error(s)` mới được tiếp. Còn error → đọc từng dòng `file(line,col): error CSxxxx`
  và sửa, build lại.
- Lỗi hay gặp nhất: sai tên field model (§2), thiếu using, sai namespace.
- **Không có `.csproj`** → không có bước build riêng. Kiểm chứng bằng cách copy
  4 file sang runtime (§6), restart, rồi xem log có `loaded module: <Name>` và
  route trả 200. Thấy `compilation <Name>` trong log là đúng.

## 5. Đo site TRƯỚC khi viết parser (đừng đoán path)

1. `curl` từng path module sẽ gọi, ghi lại status + số item thật:
   trang chủ, `/page/2/`, `/category/<slug>/`, `/?s=<kw>`, trang detail 1 video.
2. Path trả 404 = parser sai từ gốc, sửa path trước, đừng sửa regex.
3. Trang chủ nhiều site **đã là 1 category mặc định** (ví dụ home = censored).
   Menu trỏ về home thì 2 mục trùng nhau — phải trỏ path category thật.

## 6. Deploy lên app đang chạy (không deploy = SISI không thấy)

Module mới phải có mặt trong `/lampac-run/module/Adult/<Name>/` với **đúng 4 file source**:

```
Controller.cs  manifest.json  ModInit.cs  Service.cs
```

**KHÔNG copy** `bin/`, `obj/`, `*.csproj` sang runtime.

```bash
mkdir -p ~/lampac-run/module/Adult/<Name>/
cp Modules/Adult/<Name>/{Controller.cs,manifest.json,ModInit.cs,Service.cs} \
   ~/lampac-run/module/Adult/<Name>/
```

Rồi restart app (xem `termux-run-lampac.sh` / lệnh `lampac start|stop` của máy):

```bash
pkill -TERM -f '[C]ore\.dll'; sleep 3
# đợi STOPPED rồi start lại, kiểm tra log có "loaded module: <Name>"
```

Sửa code sau này: copy lại file `.cs` đã sửa sang runtime + restart (app compile lúc chạy).

## 7. Smoke test — BẮT BUỘC sau deploy

```bash
B=http://127.0.0.1:9118
curl -s -m 60 "$B/<route>" | python3 -c "import json,sys; d=json.load(sys.stdin); print(len(d.get('list',[])), 'items')"
# menu: các mục category phải ra first-item KHÁC nhau (trùng = bug §5.3)
# search:  ?search=<kw>  phải ra item chứa kw
# page 2:  ?pg=2  first-item phải khác trang 1
# vidosik: /<route>/vidosik?uri=<proxy-url>  phải ra link stream, KHÔNG được 503 stream_links
```

- `503 stream_links` = `ExtractStreamUrls()` chưa lấy được URL phát → soi trang detail
  thật, xem player loại gì (m3u8 trực tiếp / iframe embed / JS). Embed mã hoá thì
  cần giải mã thêm, không phải lỗi mạng.
- Test dồn dập nhiều curl vào site ngoài dễ bị rate-limit (`000`) → mỗi endpoint
  đo tối đa 3 lần, cách nhau vài giây.

## 8. Checklist bàn giao (thiếu 1 dòng = chưa xong)

- [ ] `.csproj` tồn tại + đã khai báo trong `NextGen.slnx`
- [ ] `dotnet build` → `0 Error(s)` (dán output)
- [ ] Đã đo site thật: status + số item từng path (dán bảng)
- [ ] Đã deploy 4 file `.cs`/`.json` sang `~/lampac-run/module/Adult/<Name>/`
- [ ] App restart, log có `loaded module: <Name>`
- [ ] Smoke test: home / category / search / pg2 / vidosik đều 200 (dán kết quả)
- [ ] `git status` + `git diff --stat` sạch sẽ, đúng file mình sửa
