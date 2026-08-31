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
| `moviesdrive: 0 link file-host \| hosts=hubcloud.foo:12 t.me:3 …` | bộ lọc đúng nhưng site đặt link ở host lạ → gửi em nguyên dòng `hosts=` |
| `…: <url> bị Cloudflare chặn (js challenge)` | host đó cần bypass (bật Playwright trong Lampac) — không phải bug; module bỏ qua, không retry 3 lần |
| `…: 302 của downloadfile -> …mkv` | cửa số 4 ăn: link thô đã thành link chơi được |
| `…: dựng link từ <title>: …` | cửa số 5 ăn: HubCloud không trả 302 cho `downloadfile=true` |
| `moviesdrive: 0 link file-host (… hits=1)` | tìm được bài nhưng không có anchor nào ra file-host ⇒ xem `hosts=` ở dòng ngay dưới |
| `movies4u: movie: N link từ M gate (bỏ gdflix/gdlink vì Cloudflare)` | đã duyệt hết mọi khối chất lượng trong bài (không còn dừng ở gate đầu) |
| `movies4u: 0 link từ 0 gate của bài …` | bài không có `download-links-div` nào dùng được → xem `hosts=` ngay sau |
| `…: trang trung gian <mdrive…> -> <hubcloud…>` | nhánh (m) ăn: đã lấy được link thật từ trang rút gọn |
| `…: có N anchor nhưng không có link HubCloud/GDFlix \| hosts=…` | MoviesDrive đổi chỗ chứa link → gửi em dòng `hosts=` |
| `movieshub: blocked (enable=False…)` | bị chặn cấu hình (không phải lỗi resolver) |
| `movieshub: tmdb meta fail` | TMDB/cub không trả metadata → không có tên để tìm Movies4U |

## Bước extractor (học CSX: class HubCloud / class GDFlix trong Extractors.kt)

Vòng test Mutiny 2026 cho thấy 8 nút nguồn là **đúng**, nhưng từ nút đó không trích ra link nào:
trang chia sẻ của HubCloud **không chứa link chơi được** (log: `len=20070`, chỉ có
`<title>(Movies4u.Foo).Mutiny.2026.480p...mkv</title>`). CSX phải đi thêm một tầng, và module
giờ làm y hệt (`HubExtract` / `GdExtract` trong `HubController.cs`):

```
HubCloud:  GET {share}    -> <script> var url = '/xxx'        (vcloud: var url = atob(atob('..')))
           GET {root+url} -> div.card-header (tên), i#size (dung lượng),
                             <h2><a class="btn" href="…">FSL Server / Download File / Mega Server</a></h2>  ← file thô
GDFlix:    GET {share}    -> <li>Name : …</li> <li>Size : …</li>
                             <div class="text-center"><a href="…">FSL V2 / DIRECT DL / CLOUD DOWNLOAD [R2]</a></div>
           + GD Index   → GET {link}?type=1 và 2, lấy a.btn-success
           + FAST CLOUD → GET {link}, lấy a trong div.card-body
           + Instant DL → đọc header Location, cắt sau "url="
```

Các nút khác: `pixeldrain` → `{root}/api/file/{id}?download`; `10Gbps` → theo 302 rồi cắt sau
`link=`; `Buzz Server` → theo `.download-btn`; `GoFile` bỏ qua (CineStream đệ quy vào extractor
riêng, Lampac không cần vì nó không phải file .mkv).

Host của HubCloud/GDFlix/vcloud **đổi TLD liên tục** — CSX đọc
`https://raw.githubusercontent.com/SaurabhKaperwan/Utils/refs/heads/main/urls.json` để lấy mirror
mới nhất. Module dùng cùng nguồn, cache 180 phút vào hybridCache, và chỉ như fallback: json fail
hoặc mirror mới cũng lỗi thì giữ nguyên host trong link (log nói rõ), không bao giờ chết cả nguồn.

Quy ước khi sửa vùng này: **không viết verbatim string chứa `[""]`** — `HubExtract` vòng trước
hỏng vì `class=[""']...` lệch một dấu nháy, C# nuốt hết code phía sau thành chuỗi. Muốn dò class/id
trong HTML thì dùng `Block(html, tag, fragment)` / `Links(block, fragment)` (so `Contains` trên thẻ
mở, nên miễn nhiễm nháy đơn/kép), còn biến JS thì dùng `JsVar(html, "url", out int atobCount)`
(xử lý luôn atob và atob(atob())).

## Trang file HubCloud/GDFlix: vì sao phải thử nhiều cửa

`https://hubcloud.cx/drive/<id>` là **app JS** — HTML không chứa link tải. Bằng chứng từ log thiết bị:
`len=20070`, `head=<title>(Movies4u.Foo).Mutiny.2026.480p.WEB-DL.English.ESub.x264.mkv</title>`
→ có **tên file**, không có **url**. Nên resolver thử lần lượt:

1. url đã là file (`.mkv/.mp4/…`) → dùng thẳng.
2. `…/drive/download/<id>/<tên>.mkv` xuất hiện trần trong HTML (kể cả trong chuỗi JS, không cần `href`).
3. Google Drive id (trong url hoặc trang) → `drive.usercontent.google.com/download?id=…`.
4. (chỉ khi extractor ở trên fail) `<url trang file>?downloadfile=true` + `Http.GetLocation`: engine GDFlix trả **302** vào file thô;
   helper đó dùng `HttpCompletionOption.ResponseHeadersRead` nên **không tải body**, và vẫn đi qua
   proxy cấu hình trong Lampac (không tự `new HttpClient`).
5. Không ra 302 thì dựng `{root}/{drive|dr|file}/{id}/{tên lấy từ <title>}.mkv` — engine map theo
   `id`, phần tên chỉ để player đoán định dạng (HubCloud canonical cũng đúng dạng này).
6. Hết cách mới nhảy sang trang `/drive|/dr|/file/<id>` khác tìm thấy trong trang. Id phải ≥13 ký tự
   vì `{6,}` từng bắt nhầm `https://hubcloud.club/drive/assets` (file tĩnh) → 3 hop lãng phí.

Về **tìm link trên trang nguồn**: không được giả định cấu trúc (bài học từ vòng test đầu — giả định
`<h5><a>` làm MoviesDrive trả 0 link). Mọi regex ở `HubController` dùng `AnchorPattern` /
`HrefPattern` / `DivOpenPattern`, chấp nhận `href="x"`, `href='x'` và `href=x`; `.NET` đọc
giá trị qua `HrefValue(m)` (ba nhóm `d`/`s`/`n`, không dùng trùng tên nhóm).

## Bẫy riêng của họ (đã có trong code, đừng bỏ)

- **TLD `.mov` không phải file video.** Regex media từng chỉ đòi chuỗi kết thúc bằng `.mkv|.mov|…`
  nên `https://moviesdrives.mov` (domain chính của MoviesDrive) bị nhận là link chơi được — đúng
  cái log `moviesdrive: play https://moviesdrives.mov`. Mọi pattern media phải có `/` trước tên
  file, `IsMediaPath` siết `^https?://[^/]+/.+\.ext$`, và `Anchors` bỏ link trơ về homepage.
- **`mdrive.lol` là trang rút gọn, không phải file-host** (bước `extractMdrive()` của CSX): nút
  chất lượng trong bài MoviesDrive trỏ sang đó, bên trong mới có link HubCloud/GDFlix. `ResolveHub`
  có nhánh (m): thử 302 bằng `Http.GetLocation` (rẻ) rồi đọc trang, lọc `Links()` lấy
  HubCloud/GDFlix/file, đệ quy `ResolveHub(depth + 1)` tối đa 3 link.
- **Dung lượng không nằm trong text của nút.** `WidenLabel()` lấy text anchor, nếu chưa có
  `\d+(\.\d+)?\s*(KB|MB|GB|TB)` thì mò 200 ký tự trước / 600 sau anchor — size thường đứng ở
  `<span>` cạnh nút. Dùng cho `Anchors()`, `Links()`, `DivBlocks()`, không tốn thêm request nào.

## Luật viết code của vùng này (toàn bộ từng làm hỏng build trên thiết bị)

- `var x = [.. …]` **không compile được** (CS9176 — C# không suy kiểu cho collection expression khi
  đích là `var`). Phải ghi tường minh: `List<string> x = [.. …]`.
- Verbatim string chứa `[""]` rất dễ lệch một dấu nháy ⇒ C# nuốt hết code phía sau thành chuỗi,
  lỗi báo ở dòng cuối file choàng hoàng. Dò HTML attribute thì dùng `Block()` / `Links()`.
- `Math.Max(season, 1)` với `season` là `short` ⇒ CS0121 (hằng int convert ngược xuống short được,
  nên cả `Max(short,short)` lẫn `Max(int,int)` đều hợp lệ). Viết `Math.Max((int)season, 1)`.
- `MatchCollection` không chỉ mục từ cuối: dùng `[Count - 1]`, không có `[^1]`.

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
