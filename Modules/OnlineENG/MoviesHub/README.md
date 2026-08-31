# MoviesHub — MoviesDrive + Movies4U (HubCloud / Google Drive)

Hai nguồn file-host trong **một module**, vì phần khó nhất (file-host → URL chơi được)
là chung: cả hai đều đổ về **HubCloud** (`hubcloud.foo|cx|…`) và **Google Drive**.
Resolver đặt trong `HubController` — `MoviesDriveController`/`Movies4UController` chỉ
loại phần "tìm link trên trang của họ".

- **Không Playwright, không enc-dec.** Regex + redirect thuần HTTP.
- Một assembly để chia sẻ resolver, **hai config section** để tắt/bật và đổi domain độc lập.
- **Chưa chạy trên thiết bị** — đây là vòng test đầu, nên mọi bước đều in log có số đếm
  (`N bài ứng viên`, `N link file-host`, `N/M link giải được`) để một lần chạy là đủ
  kết luận, không phải đoán.

## Luồng

```
MoviesDrive  GET {host}/search.php?q=<imdb>          JSON: hits[].document.permalink
             GET {site}{permalink}                   heading chứa <a href> = 1 quality
             mỗi link  -> HubCloud search-recover -> /drive/<id> -> /drive/download/…
             series:     "Season N" -> trang tập -> "Ep N" -> 1-2 link

Movies4U     GET {apihost}/?s=<imdb | title year>    Cookie: xla=s4t  (thiếu là bị chặn)
             bài khớp imdb.com/title/tt…/
             div.download-links-div a.btn -> trang trung gian -> div.downloads-btns-div a.btn
             series: mỗi div.downloads-btns-div = một mùa (heading phía trước),
                     vào link mùa rồi lấy khối số `episode`
```

HubCloud/Drive resolver (`HubController.ResolveHub`) chấp nhận 4 hình, theo thứ tự:
URL media trần (`.mkv/.mp4/.m3u8`…) → `/drive/download/<id>/<tên>` → Google Drive
(`drive.google.com/file/d/<id>/view` đổi thành `drive.usercontent.google.com/download?id=…`)
→ trang tìm kiếm thì nhảy thêm 1 hop vào file.

## Quy tắc UI (quan trọng, đừng đổi)

Link của nhóm file-host là **file .mkv/.mp4 thô**, không phải HLS. Lampac phải được thấy
chúng như **từng nút nguồn** (`MovieTpl` / `EpisodeTpl`) để việc phát/transcode đi qua
GStreamer. Vì vậy:

- `Collect()` trả `HubEntry` — một link = một nút, nhãn là `4K · 7.1GB · hubcloud.cx`.
- `/video` trả **đúng một** url, **không** có `streamquality`. Nhét mkv vào menu chất lượng
  = Lampa coi là variant HLS và phát trực tiếp = hỏng.
- Cố ý **không** có route `video.m3u8` cho hai nguồn này (route đó ép ngữ cảnh HLS).

## Route

| Route | Tác dụng |
|---|---|
| `GET /lite/moviesdrive?id=&imdb_id=&serial=&s=` | collection: tự tìm bài trên site, mỗi link file-host là một nút (mùa → tập cho series) |
| `GET /lite/<nguồn>/video?src=<base64 link>&label=<base64>&s=&e=` | resolve link đó → một url |
| `…&play=true` | redirect thẳng vào player |

`?checksearch=1` trả `data-json=` (đúng tín hiệu Lampa dùng để hỏi "nguồn có không"), nên
mở phim không bị đốt một lượt search.

## Config (init.conf)

Mỗi nguồn là **một config section riêng** (cùng assembly chỉ để dùng chung resolver):

```jsonc
"MoviesDrive": {
  "enable": true,                        // mặc định true trong ModInit; tắt = 403 im lặng
  "enabled": true,                       // có hiện trong Online khi disableEng:true
  "host": "https://new3.moviesdrive.christmas",   // domain xoay vòng -> đổi ở đây
  "httptimeout": 30, "streamproxy": true, "displayindex": 1017
},
"Movies4U": {
  "enable": true, "enabled": true,
  "host": "https://new5.movies4u.clinic",
  "httptimeout": 30, "streamproxy": true, "displayindex": 1018
}
```

Cả hai xuất hiện trong Admin Panel ở nhóm *Nguồn · HTTP / Stremio (tùy biến)*
(`Modules/AdminPanel/ConfigSectionGroups.cs`).

Referer **không** đặt tĩnh trong `headers_stream`: link cuối thuộc mirror nào của
`hubcloud.*` (hoặc `drive.usercontent.google.com`) thì `HubController.StreamHeaders` gắn
Referer theo đúng origin đó. Đặt một host cố định = 403 cho các mirror còn lại.

## Đọc log

| Dòng | Nghĩa / việc cần làm |
|---|---|
| `moviesdrive: search không trả JSON` | domain đổi → cập nhật `host` |
| `moviesdrive: search 0 kết quả (q=tt…)` | IMDb id không có trong DB (phim mới/hiếm) |
| `moviesdrive: N link file-host` rồi `0/N link giải được` | HubCloud đổi markup → xem `head=` in ra |
| `movies4u: không có download-links-div … (a=123)` | selector sai, nhưng `a=` cho biết trang có bao nhiêu link |
| `movies4u: 0 bài ứng viên` | **WP `?s=` không index href**, nên không bao giờ tìm được bằng IMDb id — module đã chuyển sang tìm tên+năm (qua TMDB); nếu vẫn 0 thì domain đổi hoặc bài không tồn tại |
| `moviesdrive: bài: … a=0` | trang bài viết không có anchor nào → bị chặn/redirect, không phải selector sai |
| `moviesdrive: 0 link file-host (… hits=1)` | hits=1 mà 0 link ⇒ selector h5 đã lỗi thời; bản mới quét MỌI anchor nên không còn case này |
| `movieshub: blocked (enable=False…)` | bị chặn cấu hình (không phải lỗi resolver) |
| `movieshub: tmdb meta fail` | TMDB/cub không trả metadata → không có tên để tìm Movies4U |

## Bẫy đã né sẵn (đều là bài học từ VidCore, đọc trước khi sửa)

- `Http.Post(url, string)` của Lampac **luôn** gửi `application/x-www-form-urlencoded`;
  endpoint nào cần JSON thì phải tự `new StringContent(json, UTF8, "application/json")`.
- GET trang = header trình duyệt trần (`Accept: text/html`), không mang `X-Requested-With`
  hay `Content-Type: application/json` — các host này trả rỗng/403 khi thấy dấu vết API.
- KHÔNG set `conf.overridehost`/`overridehosts` — đó là cơ chế redirect request sang Lampac
  khác (`IsRequestBlocked` → `RedirectResult` rồi dừng, module không chạy, không log).
- File-host trả rỗng khi bị gọi dồn: fan-out để **2** concurrent + retry có backoff
  (`GetPage` mặc định 3 lần), không phát 8 request một lượt.
- `MatchCollection` **không** hỗ trợ `[^1]` — dùng `[Count - 1]`.
