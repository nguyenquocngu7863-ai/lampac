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

## Phát qua GStreamer — và hai cái giá của việc làm sai

`Modules/GStreamer/plugins/gst.js` chỉ bật GStreamer khi **PATH** của url mà player nhận kết thúc
bằng `.mkv` (nó cắt query trước: `url.split('#')[0].split('?')[0]` rồi `/\.mkv$/`). Không có `.mkv`
trong path thì Lampa phát bằng player thường = mất E-AC3/DDP 5.1 ("mất tiếng"). Query dán thoải mái.

1. **`method:"call"` bắt buộc trả JSON.** Đặt `play=true` mặc định cho route mà Lampa gọi bằng
   `call` ⇒ nó trả 302 ⇒ Lampa theo redirect, nhận nguyên xác file mkv rồi cố parse JSON ⇒ chết mọi
   link. Bài học: hoặc là JSON, hoặc là đừng dùng `call`.
2. **Mọi url đưa cho player phải qua `accsArgs()`.** Đây là lý do thật khiến link đã resolve đúng mà
   vẫn "Không play được": url player tự fetch là `/lite/...` của Lampac, và nếu thiếu token access thì
   `AccsDbInvk` chặn. Bản `v14` bọc `accsArgs` cho `stream:` nhưng quên chỗ player cần nhất.
   Bằng chứng là chính Sootio: `BuildVideoEndpoint()` trả `accsArgs(endpoint + "&play=true")` và
   `BuildMovieTemplate()` gắn `method:"play"` (Sootio/Controller.cs:858 và :857) — Sootio **cũng**
   đi qua `/lite/sootio/file.mkv` (Controller.cs:116-158), không phải em bày ra.

**Hợp đồng hiện tại (v15)** — một nút = một url, không JSON, không proxy:

```
MovieTpl.Append(nhãn, accsArgs("{host}/lite/{plugin}/file.mkv?src=…&label=…&play=true"), "play",
                details: host);                       // EpisodeTpl likewise
VideoCore:  ResolveHub(...) -> link trần verbatim -> RedirectToPlay(link)   // 302, không /proxy
```

Tức là: path phải giữ `.mkv` (cho gst.js) và phải có token (cho player) — còn byte thì Lampac không
chạm vào, 302 thẳng sang link mà extractor vừa trả. `RouteFor(label,url)` chọn `file.mp4` khi tên
file là mp4 (mp4 thì player thường lo được, khỏi remux). `PlayUrl` chỉ proxy khi bật
`"streamproxy": true` cho đúng section trong `init.conf`.

`Clean()`/`NormalizeUrl()`: link presigned của HubCloud nằm trong `href` của HTML nên có khi còn
nguyên `&amp;`; để nguyên thì R2 nhận tham số `amp;X-Amz-Credential` → 400/403 → "Không play được".
`Clean()` chữa `&amp;`/nháy bọc ngoài, `NormalizeUrl()` thêm `%20` cho khoảng trắng (ClearStreamUri
cắt url ở space). **Không** round-trip qua `Uri.AbsoluteUri`: query presigned (X-Amz-Signature) lệch
một ký tự là chết.

Kiểm chứng trên máy: `"gst": { "enable": true }` trong `init.conf`, plugin `http://<host>:9118/gst.js`
đã đăng ký trong Lampa, và `curl -s http://127.0.0.1:9118/gst/status` phải thấy task khi đang phát.

## Series: nhóm release của một mùa nằm ở **Bộ lọc → thuyết minh** (`?g=`)

Một mùa trên Movies4U không có "danh sách tập", nó có **N nhóm**, mỗi nhóm là một heading + một nút
`DOWNLOAD LINKS` dẫn sang trang riêng:

```
Season 4 [Hindi ORG. + English]        480p [250MB/E]  -> DOWNLOAD LINKS
Season 4 [Hindi ORG. + Multi Audio]    720p [600MB/E]  -> DOWNLOAD LINKS
Season 4 [Hindi ORG. + Multi Audio]    1080p [900MB/E] -> DOWNLOAD LINKS
Season 4 [Hindi ORG. + Multi Audio]    1080p [4GB/E]   -> DOWNLOAD LINKS
Season 4 [Hindi ORG. + Multi Audio]    2160p 4K [6GB/E]-> DOWNLOAD LINKS
```

Luồng ba bước, giống cách PidTor làm với "Mùa: 1 сезон":

1. `GET /lite/movies4u?...&serial=1&s=4` → `EpisodeTpl` của nhóm **đầu tiên**, kèm `VoiceTpl` liệt
   kê cả 5 nhóm. Lampa hiển thị `VoiceTpl` ở **Bộ lọc → thuyết minh** (anh chụp PidTor: "Thuyết
   minh / LostFilm"), nên đổi nhóm được ngay trong danh sách tập, không phải quay lại màn hình chọn.
   `GroupsForSeason()` đọc `download-links-div` có heading nhắc đúng mùa, dự phòng là mọi nút có chữ
   "download links". **Bất biến phải nhớ: `SeasonTpl` = danh sách MÙA.** Bản `v16` trả nhóm bằng
   `SeasonTpl` ⇒ Lampa nhét hết "Download Links 650MB", "BATCH/ZIP [1.5GB]" vào bộ lọc "Mùa" và
   không còn phân biệt được gì (ảnh anh chụp) — nhóm thì dùng `VoiceTpl`, mùa mới dùng `SeasonTpl`.
2. `…&g=2` → chỉ trang nhóm 2 được tải; mỗi tập một nút, **mọi** host của cùng tập nằm trong
   `streamquality` của nút đó (`Ep 3 · hubcloud · 3 host`). Biến thể ở TẬP là chuẩn của Lampa; cái bị
   cấm là nhét link phim lẻ vào menu chất lượng (`## Quy tắc UI`). Mỗi tập mang `voice_name` =
   nhãn nhóm đã cắt "Season N" (`GroupShort`) để Lampa in dòng phụ, nhìn là biết đang xem
   `Hindi ORG. + Multi Audio | 1080p [900MB/E]`. Nhóm đang chọn được đánh dấu `active`
   (`vtpl.Append(name, i + 1 == cur, link)`) — đúng kỹ thuật của Mirage
   (`Modules/OnlineRUS/Mirage/Controller.cs:190-215` + `etpl.Append(vtpl)` ở :368).
3. Bấm tập → `PlayLink()` → `/lite/movies4u/file.mkv?…&play=true` (accsArgs) → 302 vào link trần.

`ReleaseGroup <= 0` và có đúng 1 nhóm thì **không** hiện màn hình chọn (khỏi thừa một bước). Ở bước 1
mở mùa chỉ tốn ĐÚNG một lần dịch trang nhóm (nhóm mặc định), không phải 5 — xem `CollectEpisodes`.
Vì lý do đó `CollectCached` nhét `ReleaseGroup` vào cache key: g=0 chỉ chứa phiếu nhóm, dùng chung
key với g=N thì menu tập sẽ rỗng.

MoviesDrive không làm theo cách này (user yêu cầu: nguồn đó không thiên về series) ⇒ `HubEntry.Group`
luôn null bên đó, nhánh chọn nhóm không bao giờ chạy.


## Route

| Route | Tác dụng |
|---|---|
| `GET /lite/moviesdrive?id=&imdb_id=&serial=&s=` | collection: tự tìm bài trên site, mỗi link file-host là một nút (mùa → tập cho series) |
| `GET /lite/movies4u?...&serial=1&s=4` | series: tập của nhóm đầu + `VoiceTpl` (Bộ lọc → thuyết minh) liệt kê mọi nhóm của mùa đó |
| `GET /lite/movies4u?...&serial=1&s=4&g=2` | nhóm release thứ 2 của mùa 4 → `EpisodeTpl` (mỗi tập một nút, các host là biến thể) |
| `GET /lite/<nguồn>/video?src=<base64 link>&label=<base64>&s=&e=` | resolve link đó → **JSON** một url (đường `method:"call"`) |
| `GET /lite/<nguồn>/file.mkv` / `file.mp4` | CÙNG action, alias để path có đuôi file cho gst.js |
| `…&play=true` | 302 thẳng vào link trần (VLC/DLNA, và bước cuối của nước đi ở trên) |

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
| `… build=<marker>` | marker bản build (`Build` trong HubController). Lampac compile module bằng Roslyn trong bộ nhớ nên không có dll trên đĩa để so — thấy marker trong log **khác** với bản vừa kéo là máy đang compile bản CŨ: `lampac stop`, xoá cache build của module (`rm -rf /root/lampac/module/OnlineENG/MoviesHub/obj /root/lampac/module/OnlineENG/MoviesHub/bin /root/lampac/module/OnlineENG/MoviesHub/*.dll` — không có cũng không sao), `lampac start` rồi mở lại phim |
| `moviesdrive: play … [direct]` / `[proxy]` | `[direct]` = phát thẳng link extractor (mặc định). `[proxy]` = link đang đi qua `/proxy/{token}` vì init.conf bật `streamproxy` cho section đó — path sẽ mất `.mkv` nên GStreamer không bật |
| `movies4u: mùa 4 có 5 nhóm release — đưa màn hình chọn (g=1..5)` | tầng chọn nhóm hoạt động (đó là cái anh hỏi) |
| `movies4u: mùa 4 nhóm 3/5 'Season 4 […] 1080p [900MB/E]': 10 khối, 30 link` | đã dịch đúng một trang nhóm; `0 link` thì xem `classes=`/`hosts=` ngay dòng dưới |
| `movieshub: bỏ N nút gdflix/gdlink/go2link khỏi menu` | nguồn vẫn trả link chết, CollectionCore chặn ở cổ chai cuối |
| `movies4u: nhãn thiếu chất lượng: 'hubcloud.cx' (heading='' nút='…')` | heading khối không bắt được ⇒ selector `download-links-div`/`downloads-btns-div` đã đổi; xem `classes=` ở dòng ngay trên để biết site dùng class gì |
| `movieshub: link có ký tự rác (&amp; / nháy / space) — đã sửa…` | HTML lọt vào link, module tự chữa; vẫn 403 thì gửi em đúng dòng này |
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
- **Patch C# bằng python: mọi chuỗi phải là raw string.** Hôm trước ghi `@"(?i)\.mp4\b"` qua chuỗi
  thường của Python ⇒ `\b` bị dịch thành ký tự backspace (0x08) nằm TRONG FILE. C# vẫn compile vì
  0x08 hợp lệ trong string literal, regex thì không bao giờ khớp ⇒ `RouteFor` luôn trả `file.mkv`,
  mp4 cũng bị ép đi remux, không có lỗi nào báo. Bắt buộc quét sau khi ghi file:

  ```bash
  python3 -c 'F="<file.cs>"; s=open(F).read(); print("control chars ở dòng:", [i+1 for i,l in enumerate(s.split(chr(10))) if any(ord(c)<9 for c in l)])'
  ```
- **Mỗi commit sửa MoviesHub phải bump `Build` trong HubController.cs** (hiện là `v14-json-direct`)
  và viết marker mới vào message commit. Đây là cách duy nhất để log tự chứng minh máy anh đang chạy
  bản nào, vì module không để dll lại trên đĩa.

- Không đổi `method:"call"` sang trả 302, và ngược lại: nút phát thì dùng `method:"play"` chứ không
  `call`. Không bọc link đã resolve vào `HostStreamProxy` rồi đưa cho player. **Mọi** url mà player
  tự fetch phải đi qua `accsArgs(...)` (xem `## Phát qua GStreamer`).
- Static của BCL **phải có lớp đứng trước**: viết `Uri.TryCreate(x, UriKind.Absolute, out Uri u)`,
  không phải `TryCreate(x, out Uri u)`. Lỗi này (CS0103 ở `HubController.cs:201`) làm module không
  compile, mà module không compile là **Lampac không boot được** (`Core.Startup.ConfigureServices`
  ném exception, process chết với signal 6) — người dùng mất server, không chỉ mất nguồn. Quét sau
  khi ghi file (một phút, cứu một buổi):

  ```bash
  python3 - <<'PY'
  import glob, re
  for f in glob.glob("Modules/OnlineENG/MoviesHub/*.cs"):
      for i, l in enumerate(open(f).read().split(chr(10)), 1):
          if re.search(r"(?<![.\w])(TryCreate|IsNullOrWhiteSpace|IsNullOrEmpty|EscapeDataString"
                       r"|UnescapeDataString|ToHexString|HashData|ReadAllBytes)\(", l):
              print(f"{f}:{i}: {l.strip()}")
  print("xong")
  PY
  ```

  Ngoài ra vẫn chạy đều: kiểm `{}/()/[]` cân bằng (bỏ qua comment + chuỗi), và quét control char —
  `python3 -c 's=open(F).read(); print([i+1 for i,l in enumerate(s.split(chr(10))) if any(ord(c)<9 for c in l)])'`.
- Trước khi đưa link cho player, luôn `Clean()`/`NormalizeUrl()`: `&amp;` trong `href` của HTML làm
  R2 trả 400/403 (tham số thành `amp;X-Amz-Credential`), và khoảng trắng làm `ClearStreamUri` cắt
  link. Nhưng **không** được chuẩn hóa bằng `Uri.AbsoluteUri` — query presigned lệch 1 ký tự là chết.
- `HubEntry` có hai vai: `Episode > 0` = tập, `Episode == 0 && Group != null` = **phiếu nhóm**.
  Thêm bất kỳ loại nào khác mà quên cập nhật `CollectionCore` là menu rỗng không lý do.
- Nhãn của một khối nút **không phải lúc nào cũng là heading**: Movies4U ghi
  "Season 4 [Hindi ORG. + Multi Audio] 1080p [900MB/E]" bằng thẻ thường (không `h1..h6`), nên
  `NearestHeadingBefore` trả rỗng và nhãn rớt về đúng chữ trên nút -> bộ lọc toàn
  "Download Links 900MB", nhìn không ra nhóm nào (ảnh thiết bị 31/8). Vì vậy mọi chỗ cần nhãn khối
  phải đi qua `NearestLabelBefore()` (heading trước, không thì lấy đoạn text ngắn nhất có
  season/quality/dung lượng ngay trước khối). Và **chính nhãn đó dùng để tách mùa**: bản cũ cho qua
  mọi nhóm khi heading rỗng => cả 4 mùa hiện cùng một danh sách. Còn `BATCH/ZIP [x.xGB]` là pack cả
  mùa trong một file, bị loại khỏi danh sách nhóm (log: `bỏ N nút BATCH/ZIP`).
- **Cấu trúc THẬT của Movies4U (đọc trực tiếp bài Reacher + trang m4ulinks.site ngày 1/9, hết đoán):**
  - bài viết: mỗi nhóm là `<h4>Season 4 [Hindi ORG. + English] 480p [250MB/E]</h4>` rồi MỘT anchor
    có chữ `Download Links` trỏ `https://m4ulinks.site/number/<id>`; `BATCH/ZIP [1.5GB]` là anchor
    KHÁC cùng dòng (nhiều nhóm trỏ cùng một id zip) -> loại bằng CHỮ TRÊN NÚT, không phải bằng heading.
  - trang nhóm (`m4ulinks.site/number/<id>`): `##### -:Episodes: 1:-` rồi các anchor
    `[🚀 Hub-Cloud [DD]](https://hubcloud.cx/drive/xxx)`, `[🚀 GDFlix](https://gdflix.dev/file/yyy)`.
    **Không có class `downloads-btns-div` ở tầng này** => tập phải bucket theo heading gần nhất, còn
    `Episodes: 1` thì `EpisodeNumber` phải khớp được dạng "Episodes:" (mẫu `ep(?:isode)?` cũ là fail).
  Kết luận cho đời sau: ở module này, CHỮ TRÊN NÚT + HEADING là hợp đồng; class chỉ là tối ưu.
- Sau mỗi commit sửa module: đổi `Build` (hiện `v19-m4ulinks`) và nhắc marker trong message commit,
  vì đó là cách duy nhất log tự chứng minh máy đang chạy bản nào (module compile trong bộ nhớ).

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
