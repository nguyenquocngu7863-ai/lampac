# UhdMovies (`uhdmovies.autos`) — spec trước khi viết module

Ngày 2026-09-01. Nguồn do người dùng đưa: 1 bài **series** và 1 bài **phim lẻ**. Kết luận của
anh đúng: **nguồn này không dùng HubCloud, phải viết extractor riêng** — họ này (UHDMovies /
MoviesMod / TopMovies / DramaDrip, cùng một chủ) đi qua **URL shortener + trang verify +
DriveLeech/DriveSeed**, không phải `/drive/<id>` của HubCloud.

Toàn bộ cấu trúc dưới đây ĐÃ ĐỌC TRỰC TIẾP (bài viết + trang verify), không đoán.

## 1. Bài viết: group = 2 dòng bold, phần tử = anchor theo TỪNG TẬP

```
## Download My Life With The Walter Boys (2023) (Season 1-2) 1080p WEB-DL [Dual Audio]

**My.Life.With.the.Walter.Boys.S01.1080p.NF.WEB-DL.DUAL.DDP5.1.Atmos.H.264-ShiNobi**
**[2GB/E] [20.08GB/Zip]**
[Episode 1](https://cloud.unblockedgames.world/?sid=<base64>) [Episode 2](…) … [Episode 10](…)
[Zip / Pack](https://cloud.unblockedgames.world/?sid=<base64> "Choose Zip to download")
```

Khác Movies4U ở ba điểm, và cả ba đều đổi cách viết code:

| | Movies4U | UhdMovies |
|---|---|---|
| Nhãn nhóm | `<h4>` heading | **2 dòng bold liên tiếp** (release name + `[xGB/E] [yGB/Zip]`) — không phải heading |
| Số tập | nằm ở **trang nhóm** (tầng 2) | nằm **ngay trong bài**, mỗi tập **một link riêng** |
| Link trỏ tới | `m4ulinks.site/number/<id>` (trang có các host) | `?sid=<blob>` — **mồi** cho chuỗi verify, không phải trang file |

- `Season` lấy từ release name: `…Walter.Boys.S01.1080p…` ⇒ `SeasonNumber` hiện tại khớp `s(\d{2})` ✓ dùng lại được.
- Chất lượng/size cũng trong release name (`1080p`, `1080p HEVC`, `HDR DoVi`, `2160p 4K`) + dòng
  `[2GB/E] [20.08GB/Zip]` ⇒ nhãn chip cho `VoiceTpl` tự dựng được, không cần thêm request.
- **`Zip / Pack` = BATCH** (title "Choose Zip to download"): loại bằng **chữ trên nút**, y luật
  Movies4U. Ở đây nó nằm trong cùng dãy anchor với các tập nên nếu không loại thì nó thành "tập 11".
- `?sid=` là **base64 của một blob đã mã hoá** (48 hoặc 192 byte, không phải text — đã thử b64 1–6
  lớp: lớp 2 là binary). **Đừng có cố giải mã ở C#**: không cần, và key nằm trong JS của họ.

## 2. Chuỗi resolve `sid` → link chơi được (đây là "extractor mới")

Tham chiếu đã kiểm: `utils/linkResolver.js` của `tapframe/NuvioStreamsAddon` (commit `085cb6d1`,
đọc 2026-09-01). Cùng bài toán, chạy được ngoài trình duyệt ⇒ làm được bằng `HttpHydra`.

**Phiên = một `CookieContainer` dùng chung cho cả 4 request.** Lampac có sẵn:
`httpHydra.Get(url, cookieContainer: jar)` và `httpHydra.Post(url, data, cookieContainer: jar)` ✓
(không cần tự chế client).

```
Step 0  GET  {sidUrl}                                  (vd cloud.unblockedgames.world/?sid=…)
        parse  form#landing -> action=action0, input[name=_wp_http]=wp_http
        nếu không có 2 cái này => DỪNG, in html 500 ký tự đầu (trang verify dạng JS challenge)

Step 1  POST action0   body: _wp_http=<wp_http>          Content-Type: application/x-www-form-urlencoded
                        Referer: <sidUrl>

Step 2  parse  form#landing -> action=action1, input[name=_wp_http2], input[name=token]
        POST action1   body: _wp_http2=<…>&token=<…>       Referer: <url thực của step 1>

Step 3  parse  <meta http-equiv="refresh" content="N;url=X">   => redirectUrl = new URL(X, origin(sidUrl))

Step 4  GET redirectUrl
        nếu html có window.location.replace("Y") => GET tiếp Y   (file page thật)

Step 5  Trên file page: đọc div.text-center > a, theo THỨ TỰ ưu tiên (lấy cái đầu tiên ra được link hợp lệ):
        a) "Instant Download"  href có ?url=<keys>  => POST {origin(href)}/api
                                                 form: keys=<keys>
                                                 header: x-token: <hostname của href>
                                                 => data.url
                            href đã là direct (workers.dev | .r2.dev | cdn.video-leech.pro,
                            hoặc http mà không có ?url=) => dùng luôn
        b) "Resume Worker Bot" GET href, tìm <script> chứa formData.append('token'  => token + id
                            từ fetch('/download?id=<id>')  => POST {origin}/download?id=<id>
                                                 form: token=<token>
                                                 header: x-requested-with: XMLHttpRequest, Referer: <href>
                                                 => data.url
        c) "Direct Links"      GET href + "?type=1"  => a.btn-success[href^=http] cái đầu
        d) "Resume Cloud"      GET href              => a.btn-success[href] (hoặc selector
                            a[href*="workers.dev"], …/driveleech.net/d/, …/driveseed.org/d/)
        Fallback cuối: quét trần a[href*="workers.dev"], a[href*=".r2.dev"],
                       a[href*="driveleech.net/d/"], a[href*="driveseed.org/d/"]
        Chốt: link có dấu cách ở filename => thay bằng %20 ở đúng segment cuối.
```

**Kết quả cuối = link CDN file (thường `.mkv`/`.mp4` trên `*.workers.dev` / `*.r2.dev` /
`cdn.video-leech.pro`)** ⇒ đúng loại link mà luật vùng này bắt buộc để **trên nút nguồn**, không nhét
vào menu chất lượng, để GStreamer của Lampac tự chơi. Không `/proxy`, không HLS hop: route chỉ việc
**302 verbatim** (`method:"play"` + `accsArgs`) — y hệt MoviesHub round 15.

## 2b. CSX đã làm thế nào (bản để port — `Moviesmod/src/main/kotlin/com/megix/Utils.kt`, repo `SaurabhKaperwan/CSX` @ `f1b19bd`, đọc 2026-09-01)

Anh bảo kiểm tra CSX: **có**, và đúng là họ bỏ bê — provider này không còn file riêng, nó nằm
chung module `Moviesmod` (cùng họ: MoviesMod/UHDMovies/TopMovies một chủ). Toàn bộ phần hard nhất
nằm trong 2 hàm, và **không cần crypto**:

```kotlin
suspend fun bypass(url: String): String? {                     // ?sid= -> trang file
    res = app.get(url).document
    formUrl = res select "form#landing" attr("action");  formData = "form#landing input" (TẤT CẢ input)
    res = app.post(formUrl, formData).document                 // lần 1
    formUrl = ...; formData = ...                              // lần 2 (lặp lại đúng kiểu)
    res = app.post(formUrl, formData).document
    skToken  = res: script chứa "?go=" -> substringAfter("?go=").substringBefore("\"")
    driveUrl = app.get("$host?go=$skToken", cookies = { skToken -> formData["_wp_http2"] })
                  .document: meta[http-equiv=refresh] -> substringAfter("url=")
    path     = app.get(driveUrl).text.substringAfter("replace(\"").substringBefore("\")")
    if (path == "/404") return null
    return fixUrl(path, getBaseUrl(driveUrl))                  // = URL trang file
}
```

Bốn chỗ em ghi ở mục 2 theo bản JS **thiếu/sai** so với bản Kotlin này, và port thì phải theo Kotlin:

1. POST **toàn bộ** `form#landing input` (không chọn mỗi `_wp_http`/`token`) ⇒ ít vỡ hơn khi họ thêm input.
2. Phải POST form **hai lần liên tiếp** (bản JS chỉ một), rồi mới tới `?go=`.
3. `skToken` không nằm trong form: nó nằm trong **`<script>` chứa `?go=`** của trang sau POST lần 2,
   và **tên cookie** chính là `skToken`, giá trị là `_wp_http2` — quirky nhưng đó là cái chốt cửa.
4. Sau `?go=` mới tới `meta refresh`, sau refresh mới tới `window.location.replace(...)`.

`Driveleech` / `Driveseed` (`class Driveseed : Driveleech()`, `requiresReferer = false`) đọc trang file:

- `ul > li.list-group-item:contains(Name)` → `Name : <tên file>` và `:contains(Size)` → `Size : <size>`
  ⇒ **label và dung tích lấy từ chính trang file**, không cần đoán từ release name (tốt hơn:
  `getIndexQuality(fileName)` = `(\d{3,4})[pP]`, rồi 8k/4k/2k).
- duyệt `div.text-center > a` theo CHỮ trên nút (mọi nút đều được `callback`, không phải "cái đầu"):
  `Cloud Download` → href thẳng; `Instant Download` → **`GET href` với `allowRedirects=false`, lấy
  header `Location`, `substringAfter("?url=")`** (đơn giản hơn hẳn bản JS POST `/api`);
  `Resume Worker Bot` → regex `formData\.append\('token', '([a-f0-9]+)'\)` +
  `fetch\('/download\?id=([a-zA-Z0-9/+]+)'`, **giữ `PHPSESSID` từ response GET**, POST
  `{base}/download?id=…` form `token=…` + headers `Origin`, `Sec-Fetch-Site: same-origin`, `Referer`
  → JSON `{"url": …}`; `Direct Links` → `GET baseUrl+href + "?type=1"` **và** `"?type=2"` →
  `a.btn-success[href]`; `Resume Cloud` → `GET baseUrl+href` → `a.btn-success`; `gofile` → `loadExtractor`.
- Trang file có biến thể `r?key=`: phải đọc `replace("…")` trong `<script>` đầu tiên rồi GET tiếp.

⇒ **Port sang C# là vừa**, mọi thứ đều có trong Lampac: `httpHydra.Get(url, cookieContainer: jar)`
cho chuỗi `bypass`, POST form qua `httpHydra.Post(url, "_wp_http=…&…")` với `Content-Type:
application/x-www-form-urlencoded`, và "allowRedirects=false + đọc header Location" =
`Http.GetLocation(url, …, allowAutoRedirect: false)` (có sẵn, `Shared/Services/HTTP/Http.cs:418`).
Điểm **không** có trong CSX mà mình vẫn phải giữ: `Zip / Pack` (CSX không lọc pack), và luật "link
`.mkv` để trên nút nguồn" của vùng này.

## 2c. Bằng chứng THIẾT BỊ (2026-09-01, người dùng mở `?sid=` bằng Chrome)

**Phải qua TỪNG ĐÓ hai lần countdown mới tới được trang driveseed.** Nghĩa là `bypass()` không được
viết kiểu "POST hai lần" cứng như CSX — phải là **vòng lặp**:

```
cho tới khi trang hiện tại KHÔNG còn form#landing (tối đa 5 vòng, quá thì DỪNG + in html 400 ký tự):
    POST action(form) với TOÀN BỘ input của form#landing, Referer = url trước đó
hết form => đọc <script> chứa "?go="  => skToken
GET  {host}?go={skToken}  +  cookie { skToken : _wp_http2 }   => meta refresh => url
GET  url                                                 => window.location.replace("path") => trang file
```

Mỗi vòng là một cái countdown; site thêm/bớt bước countdown thì module vẫn sống — đó là lý do phải
lặp thay vì đếm tay. `CookieContainer` **dùng chung từ vòng 0** (thiếu cookie là quay lại form đầu).

**Từ trang file, thứ tự ưu tiên KHÔNG theo CSX mà theo "cái này chơi được hay không"** (người dùng
test: *Resume Cloud hoặc worker CF là link play được; worker die thì chỉ còn link download, không
resume được*):

| # | Nút | Link ra gì | Dùng? |
|---|---|---|---|
| 1 | `Resume Cloud` | play được, **seek được** (Range) | mặc định |
| 2 | `Direct Links` (`?type=1`, `?type=2` → `a.btn-success`) | play được (Cloudflare worker) | nếu 1 fail |
| 3 | `Resume Worker Bot` | play được **khi worker còn sống** | nếu 1–2 fail; worker chết là **lỗi thường xuyên** ⇒ `catch` + log `worker die`, không được làm fail cả tập |
| 4 | `Instant Download` / `Cloud Download` | **chỉ để tải**, không resume/seek | vẫn đưa vào UI nhưng **gắn nhãn `[download]`**, không bao giờ là mặc định |

Vì resolve là lúc BẤM, `?srv=` phải chọn được nút: `lite/uhdmovies/video?src=<sid>&srv=resume|cf|worker|instant`
— `srv` rỗng = chạy theo thứ tự trên và **in ra** `uhdmovies:.play <file> qua <srv>`. Nhãn `src` mang
theo `label` (tên release + size) để log đọc được. Không cache link cuối (link worker/R2 hết hạn
nhanh — cùng bệnh mọi họ DriveLeech).

## 2d. HÌNH DẠNG link cuối (người dùng đưa thật ngày 2026-09-01, lấy từ nút "Resume Cloud")

```
https://worker-snowy-cell-60ac.kaxidaj969.workers.dev/<256 hex>::<32 hex>/Stolen%20Girl%20(2025)%202160p%20UHD%20BluRay%20HDR%2010bit%20HEVC%20[Hindi%20DDP%205.1%20%20English%20DD%205.1]%20x265%20(CYBER-UHDMovies).mkv
```

Năm thứ rút ra từ đúng chuỗi đó (đừng để vòng sau phải đoán lại):

1. **Host = `worker-<...>.<sub>.workers.dev`** (Cloudflare Worker công khai). Không `Referer`, không
   cookie cho link phát ⇒ `requiresReferer = false` như CSX, `StreamHeaders` chỉ cần `User-Agent`.
   Anh xác nhận: **link nhãn `resume` tua bình thường** ⇒ có `Range` ⇒ `streamproxy = false` là đúng,
   để player tự seek, và `Resume Cloud` xứng đáng làm mặc định.
2. **`path` kết thúc bằng `.mkv`** ⇒ `RouteFor()` trả `file.mkv` ✓ gst.js của Lampac bật ✓ (nó test
   path sau khi cắt query, nên query dài bao nhiêu cũng được).
3. **Không có query string** — token nằm TRONG path, sau đó là tên file. ⇒ 302 verbatim là xong,
   không được "làm sạch" url.
4. **Tên file `%20`-encoded và có `(`, `)`, `[`, `]`, `::`** ⇒ hai luật sống còn:
   - `href` lấy ra chỉ được `HtmlDecode` (`Unescape`), **tuyệt đối không** `Uri.UnescapeDataString`
     link cuối: `%20` thành dấu cách rồi bỏ vào `Location` header là link hỏng.
   - `Clean()` hiện tại vô hại với chuỗi này (nó chữa `&amp;` và `%252F`, không đụng `%20`) ⇒ dùng
     tiếp được, nhưng đừng có "nâng cấp" nó bằng UrlDecode.
5. **Tổng độ dài ~600 ký tự** ⇒ `src=` của route không được tự nhiên ghép chuỗi thô: may là
   `Enc()`/`Dec()` đã bọc **base64** (`HubController.cs:663/665`), nên `?sid=<base64 có + / =>`
   truyền qua query **không bị hỏng** (`+` thành dấu cách là chết token). UHDMovies dùng nguyên
   cơ chế đó, không thêm escape gì.
   -> Ghi thành luật chung cho họ này: **url nào cũng đi qua `Enc`, không bao giờ ghép `?src=<raw>`.**
6. **Không regex theo độ dài token** (256/32 hex là của link này, worker khác có thể khác). Nhận
   link bằng: host khớp `workers\.dev|\.r2\.dev|video-leech` **và** `path.EndsWith` một trong
   `.mkv|.mp4|.m4v|.avi|.ts` — còn lại bỏ, và in `hosts=`/`tail=` để kết luận một lần.

## 3. Thiết kế module (quyết định, không phải gợi ý)

1. **ĐẶT TRONG MoviesHub** (`Modules/OnlineENG/MoviesHub/UhdmoviesController.cs`), không lập module
   riêng. Em từng định để riêng vì sợ "file mới làm sập MoviesHub đang chạy", nhưng nghĩ lại thì
   sợ sai chỗ: Lampac compile module bằng Roslyn và **module không compile được là Lampac không boot
   được** (signal 6) — bất kể file hỏng nằm ở module nào, blast radius y hệt. Ở cùng assembly lại
   tiết kiệm đúng phần đã được thiết bị xác minh: `CollectionCore` (TMDB + cache + `SeasonTpl`/
   `VoiceTpl`/`EpisodeTpl` + `?g=`), `GetPage`, `HeadersModel`, log helpers — khoảng 600 dòng
   battle-tested, viết lại lần hai chỉ để gặp lại lỗi cũ. resolver mới (`bypass` + trang file) là thứ
   DUY NHẤT khác, và nó nằm gọn trong file mới.
2. **Resolve lúc BẤM, không resolve lúc dựng danh sách.** Mỗi tập tốn 4–6 request (2 GET + 2 POST
   + file page). Một nhóm 10 tập × 5 nhóm = **hơn 250 request** nếu làm lúc mở phim ⇒ chết timeout,
   chết cả `httptimeout: 30`. Nên `HubEntry.Url` = **chính url `?sid=`**, còn route
   `lite/uhdmovies/video?src=<sid>` mới chạy chuỗi trên rồi 302. (Movies4U thì ngược lại: trang nhóm
   rẻ nên resolve lúc dựng danh sách.)
3. **Cache**: không cache link cuối (CDN link của họ có hạn sử dụng ngắn — cùng bệnh với mọi họ
   DriveLeeech). Cache **bài viết** theo `CollectCached`; `sid` không cache kết quả resolve.
4. **UI**: không có "trang nhóm" ⇒ không dùng `Mùa`+`Thuyết minh` hai tầng như Movies4U; mỗi group
   (release name) = một `VoiceTpl` chip với nhãn `1080p X264 · 2GB/E` (từ release name + size line),
   tập = `EpisodeTpl` với link `…/lite/uhdmovies/video?src=<sid>&label=<release name>`. Phim lẻ: mỗi
   group = một **nút nguồn** trực tiếp (`ContentTpl`), không vào `StreamQualityTpl`.
5. **Host**: `cloud.unblockedgames.world`, `tech.unblockedgames.world`, `examzculture.in`, … đều là
   redirector đổi subdomain/domain theo tuần ⇒ không hardcode trong code, chỉ khớp bằng
   `sid=` trong query (đây là hợp đồng, không phải host). `modpro.blog` / `cinematickit.org` /
   `leechpro.blog` / `modrefer.in` là các shortener có thể chèn giữa ⇒ nếu Step 3 ra một host
   shortener chứ không phải driveseed, module phải in ra `step3=` để biết mà thêm hop (đừng giả định).
6. `manifest.json`: `"dynamic": true`, `tree` liệt kê đủ file (đây là danh sách để `setup-termux.sh`
   kéo file — xem `Modules/OnlineENG/MoviesHub/README.md`). `ModInit`: section `"UhdMovies"`,
   displayindex 1019, `streamproxy = false`, headers `User-Agent` + `Referer: https://uhdmovies.autos/`.

## 4. Rủi ro phải biết trước (đừng để anh mất thời gian vô ích)

- **Trang verify có thể cần JS.** Khi fetch `?sid=` không kèm cookie, em nhận được
  `#### Please verify that you are human / Start Verification` — rất khớp với `form#landing` ở
  Step 0/2 (nút verify chỉ POST form). Nếu sau 2 POST mà vẫn là trang challenge thì nguồn này
  **cần Playwright**, không phải scraper — lúc đó xử lý như VidLink: đóng, không cày.
- Ngin/CPU Termux chịu được ~4–6 request mỗi lần bấm là **có điều kiện**: phải bật `httptimeout`
  thấp cho route đó và không retry mù.
- Sandbox của em không có egress ⇒ **mọi con số request ở trên mới là đọc code nguồn khác, chưa
  phải tự chạy**. Vòng đầu trên thiết bị phải in đủ `step0/step1/step3/filepage` để kết luận một lần.

## 5. Log bắt buộc (mỗi nhánh một dòng, in dữ liệu thô khi 0 kết quả)

```
uhdmovies: bài: N group, M nút Episode, bỏ K nút Zip/Pack
uhdmovies: group 'My.Life…S01.1080p…' [2GB/E] 10 tập
uhdmovies: sid step0 ok=… form#landing=… | step1 len=… | step3=<redirectUrl> | filepage=<host>/<path>
uhdmovies: link cuối = https://…workers.dev/…mkv (via instant|worker|direct|resume|fallback)
uhdmovies: 0 link | step3=<…> hosts=<histogram> | head=<500 ký tự>      <- nhánh chết phải tự giải thích
```
