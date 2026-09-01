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

## 2e. Kiểm tra nguồn "UHDMovies của phisher98" (2026-09-01, vì anh bảo xem trước khi viết)

| Điều | Kết quả |
|---|---|
| `phisher98/cloudstream-extensions-phisher` @ `builds`, file `UHDmoviesProvider.cs3` | build cuối **2026-08-26 05:27Z**, message `Minor fix uhd` (GitHub Actions) — tức ~1 tuần trước ✓ anh nói đúng là có sửa gần đây |
| Nhánh `master` của repo đó | **chỉ còn `README.md` + `docs/`**; commit cuối 2026-07-09 chỉ đổi README. Repo chỉ có 2 nhánh (`builds`, `master`) ⇒ **source Kotlin KHÔNG còn ở public**, `.cs3` (ZIP chứa `plugin.apk`) là thứ duy nhất họ phát ra |
| `phisher98/TVVVV` (repo nguồn cùng tác giả) | `domains.json` @ main: `"UHDMovies": "https://uhdmovies.autos"` ✓ khớp đúng domain anh đưa (nguồn còn sống, được hot-swap domain, không hardcode), `"movies4u": "new5.movies4u.clinic"`, `"hubcloud": "hubcloud.cx"`, `"moviesmod": "moviesmod.army"`, `"topMovies": "moviesleech.bar"` |
| `UHDMoviesProvider.kt` trong TVVVV | 404 ở `app/src/main/java/com/lagradost/cloudstream3/animeproviders/` — em không đoán path tiếp, và cũng không nên: `Minor fix uhd` chỉ đọc được bằng cách **mổ `classes.dex` bên trong `plugin.apk`** |

**Kết luận cho mình:** "fix" tuần trước của họ **không đọc được dưới dạng source**. Nhưng nó không
chặn em viết module, vì phần logic chain (`form#landing` lặp + `?go=` cookie + meta refresh +
`div.text-center > a`) đã có hai bản độc lập trùng nhau (CSX Kotlin `Moviesmod/Utils.kt` và
Nuvio JS `linkResolver.js`) — và nếu họ sửa gì đó nhỏ, thứ hay đổi nhất là **domain** (đã có
`domains.json`), không phải thuật toán.

Nếu muốn em kiểm tra `Minor fix uhd` thật sự đổi gì: **tải `UHDmoviesProvider.cs3` về rồi bỏ vào
workspace** (không cần dán link — fetch của em không đọc được binary). Em mở ZIP bằng `zipfile`,
đọc `classes.dex` và quét chuỗi ASCII lấy mọi `https://…`, mọi regex chứa `go=`, `landing`,
`workers.dev`, `driveleech|driveseed`, `type=1`. Không cần jadx: đối chiếu các hằng số đó với spec
mục 2b/2c là đủ biết họ có thêm hop mới hay đổi host nào.

## 2f. Vì sao CSX coi UHDMovies là "dead source" trong khi nó sống khoẻ (2026-09-01)

Bằng chứng, không bình luận:

- Tìm trong toàn bộ lịch sử commit của `SaurabhKaperwan/CSX`: **`total_count: 1`** cho từ `uhd` —
  commit duy nhất là **`8310abd` 2024-10-15 "CInestream : Fix NFmirrir & UHD"**. Tức UHDMovies được
  sửa **đúng một lần, ~2 năm trước**, rồi không ai chạm nữa.
- `master` hiện tại: **không có file UHDMovies nào** (`…/providers/UhdMovies.kt` 404), và module
  `Moviesmod` chỉ khai `MoviesmodProvider.kt` + `TopMoviesProvider.kt` + `Utils.kt`. Thứ còn lại
  của họ UHDMovies là **`bypass()` + `Driveleech/Driveseed` trong `Utils.kt`** — tức phần solver,
  còn phần scrape thì bị bỏ; `urls.json` vẫn giữ `uhdmovies=uhdmovies.autos` vì file đó là bảng
  domain chung, không có nghĩa là provider còn chạy.

> **SỬA LẠI (cùng ngày — xem 2g):** kết luận "họ cắt UHDMovies" là **em đọc sai**. Em chỉ đoán path
> `providers/UhdMovies.kt` (404) rồi đếm commit message có chữ "uhd". Sự thật: provider **vẫn sống**,
> ghi danh trong monolith `CineStream` — `ProviderRegistry.kt:199`, key `p_uhdmovies`. Phần phân tích
> chi phí dưới đây vẫn dùng được (nó giải thích vì sao cách của họ không đủ cho mình), chỉ bỏ chữ
> "cắt/bỏ bê".

⇒ **"Remove dead source" ở đây không phải "site chết"** — UHDMovies vẫn đăng bài hằng ngày, domain
vẫn được hot-swap trong `domains.json` của tác giả khác. Nó là: **nguồn đắt hơn phần họ muốn trả**.
Chuỗi của UHDMovies là countdown ×2 + cookie session + 4–6 request cho MỖI TẬP, trong khi
`4khdhub`/`moviesdrive`/`hdhub4u` (nhóm mà Nuvio mô tả là "dùng HubCloud/GDFlix + ROT13+atob thay vì
hệ shortener") chỉ cần **một trang + một hàm giải mã**. Cùng 68 provider, người bảo trì thì ít ⇒
họ cắt cái đắt, giữ cái rẻ. 4khdhub "ngon hơn" theo nghĩa đó, không phải theo nghĩa chất lượng file.

**Bài học ghi cho mọi nguồn tương lai:** chi phí bảo trì phải được tính lúc CHỌN nguồn, không chỉ lúc
viết. UHDMovies cho 2160p HDR DoVi `.mkv` seek được (link `*.workers.dev` — tốt nhất họ này) nhưng
đổi lại là chuỗi verify mà mỗi lần site thêm một countdown là phải sửa code; 4khdhub cho cùng chất
lượng mà chỉ phụ thuộc markup trang chia sẻ HubCloud — thứ MoviesHub **đã có sẵn** (`HubExtract`,
`Atob`, `GdExtract`). Khi nào rảnh tay, **4khdhub là ứng viên số 1** cho nguồn mới của họ này;
UHDMovies để sau, theo quyết định ngày 2026-09-01 (spec ở trên vẫn giữ nguyên, không viết lại).

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

---

## 2g. MÓ CƯA code CSX hiện hành (đọc bằng `gh api` ngay trong sandbox — 2026-09-01)

Cách làm (lần đầu dùng được, ghi lại vì từ trước tới giờ em toàn chịu chết với file lớn):

```bash
gh api repos/SaurabhKaperwan/CSX/contents/CineStream/src/main/kotlin/com/megix/CineStreamExtractors.kt \
  --jq .content | base64 -d > /tmp/x.kt      # 4740 dòng
grep -n -iE "uhd|driveleech|driveseed|sid=|bypassHrefli|entry-content" /tmp/x.kt
```

Theo độ hữu ích cho module của mình:

1. **Provider vẫn đăng ký**: `ProviderRegistry.kt:199` — `key = "p_uhdmovies"`,
   `executeStandard = { … if (!res.isBollywood) invokeUhdmovies(title, year, season, episode, …) }`.
   Họ **gate theo "không phải Bollywood"** (UHDMovies mạnh ở series Netflix/HDR DoVi, yếu ở masala Hindi).
   Nên học: mình cũng nên bỏ qua bài Hindi-only khi `original_language` đã là `hi`? Chưa vội — Lampa
   đã lọc theo `original_language` ở `ModInit`.
2. **Tìm bài**: `app.get("$uhdmoviesAPI/search/$title $year")` rồi `article div.entry-image a` → url bài.
   `uhdmoviesAPI` KHÔNG phải JSON API: `ApiConstants.kt:91` → `api("uhdmovies")` = **domain hot-swap**
   từ `urls.json` (chỗ đó gọi là "API" cho oai). Đường `/search/<query>` của site này nên được **thử
   trước** `?s=<query>` (ít nhiễu trang hơn), `?s=` làm fallback.
3. **Tách nhóm/tập** — hay hơn `NearestLabelBefore` của em, và nó XÁC NHẬN cấu trúc ở mục 1:
   selector là `div.entry-content p:matches(<year>)` cho phim lẻ, `p:matches((?i)(S0?N|Season 0?N))`
   cho series, rồi **`nextElementSibling()`** mới tới `a:matches((?i)(Episode N))` / `a:matches((?i)(Download))`.
   ⇒ dòng release-name và dãy nút là **hai `<p>` anh-em ruột**. Luật thêm cho mình: nếu một anchor nằm
   trong phần tử liền sau `<p>` khớp `S0?N|Season 0?N` thì tin nó thuộc mùa đó (mạnh hơn đoán heading).
4. **Đường TẮT đáng giá**: `if (href.contains("driveleech") || href.contains("driveseed"))` ⇒ bỏ hẳn
   bypass, chỉ `GET` rồi đọc `window.location.replace("…")` + `getBaseUrl` = **tiết kiệm 3 request/lượt**.
5. **`bypassHrefli()` (CineStreamUtils.kt:869) GIỐNG HỆT `bypass()` của `Moviesmod/Utils.kt`**: 2 POST
   `form#landing` (gửi TOÀN BỘ input), `skToken` từ `<script>` chứa `?go=`, cookie **tên = `skToken`**,
   giá trị = `_wp_http2`, `meta refresh`, `replace("…")`, `/404` → null. ⇒ **2 POST là đủ**, khớp chính
   xác "2 lần countdown" anh báo. Vòng lặp ≤5 ở mục 2c vẫn giữ: rẻ, và là bảo hiểm cho lần thứ 3.
6. **Đuôi cùng như mình**: `loadSourceNameExtractor("UHDMovies", driveLink, …)` → dispatcher theo URL
   (`CineStreamUtils.kt:792`: `driveleech.`/`driveseed.` → `Driveleech().getUrl`), `class Driveseed :
   Driveleech()` ở `Extractors.kt:464`. ⇒ **mục 2b là bản mới nhất**, không có biến thể khác.
7. **Chỗ mình làm khác (và nhiều hơn)**: họ lấy `.attr("href")` = **một link mỗi nhóm** và ném hết
   cho extractor, không ưu tiên "Resume Cloud". Mình phải liệt kê **đủ tập** và chọn nút theo mục 2c.
   Không phải họ hay hơn — họ chỉ không cần UI chọn mùa/nhóm.
8. **`multiDecryptAPI = "https://enc-dec.app/api"`** vẫn nằm trong monolith (ngay dưới `vidlinkAPI`)
   ⇒ xác nhận thêm cho lý do đóng Mapple: cả repo phụ thuộc `enc-dec.app`, endpoint chết là chết dây chuyền.

### Bonus cho nguồn ĐÃ SHIP (Movies4U)

CSX tìm Movies4U bằng `"$movies4uAPI/?s=<title>+season+<N>"` (series) hoặc `+<year>` (phim lẻ),
headers `Cookie: xla=s4t` + UA desktop + `Referer: <site>/`, rồi lấy `article h3 a` ⇒ **`xla=s4t` là
chuẩn** (khớp module mình), và **`+season+<N>`** là ý đáng thử: nó trả đúng bài của mùa cần tìm thay
vì mò bài "Season 1-4" rồi lọc bằng nhãn. **Không đổi code vòng này** (MoviesHub `v20` đang chạy tốt);
ghi để vòng sau, kèm log so sánh `0 bài ứng viên` có còn xảy ra không.

---

## 6. Code v1 đã viết (2026-09-01) — vì sao commit này tồn tại

**Lý do commit (ghi ngay trong file, theo luật của repo):** anh revers lệnh hoãn ở vòng 24
(“Nếu đã tìm được nguồn để học thì ta cứ làm thôi”), và vòng 25 tìm ra nguồn sống để học là
`CineStream` monolith của CSX (mục 2g) ⇒ không còn lý do dừng ở spec. File này là hợp đồng
giữa đặc tả và code: đọc code mà không đọc mục 1/2b/2c/2d thì không hiểu **vì sao** mỗi dòng
regex lại hình dạng như vậy.

**Vật tạo ra:** `Modules/OnlineENG/MoviesHub/UhdmoviesController.cs` (712 dòng) +
`ModInit.cs` (section `"UhdMovies"`, host `https://uhdmovies.autos`, displayindex 1019,
item `uhdmovies`, `OnlineApiQuality` “ ~ 4K/1080p”) + `manifest.json` → `tree` (để
`lampac sync` tự kéo — luật vòng 17) + `HubController.Build` → `v21-uhdmovies-resolver`
(mỗi commit chạm MoviesHub là đổi marker — luật README).

**Ánh xạ spec → code:**

| Đặc tả | Code |
|---|---|
| mục 1: bài đặt nút, nhãn nhóm là khối text ngay trước nút | `Groups()` + `NearestLabelBefore`, lọc theo `episode|download` trên text NÚT |
| mục 1: BATCH/ZIP không lấy (dặn vòng 16) | bỏ khi text nút khớp `zip|pack|batch|rar`, in `bỏ N nút Zip/Pack/BATCH` |
| mục 2b/2g: `/search/<q>` trước, `?s=` sau | `Collect`: `{site}/search/{q}` rồi fallback `/?s={q}` |
| mục 2g: nháp `/download-` của phisher98 (đúng, không có source) | slug bắt buộc `/download-` + kiểm IMDb id trong bài |
| mục 2b: `driveseed|driveleech` đi thẳng, bỏ countdown | `Resolve`: `if (!file.Contains("driveseed") && !file.Contains("driveleech"))` |
| mục 2c: countdown LÀ MỘT VÒNG LẶP (dặn vòng 21) | `Bypass`: `for (i < 5)` trên `form#landing`, không đếm cứng 2 |
| mục 2c: ưu tiên nút theo “tua được không” | `Buttons()`: resume→cf→worker→instant/cloud; 2 nút cuối gắn `[download]` |
| mục 2c: worker die không phải lỗi chết | `try/catch` từng nút, log `nút X fail … thử nút kế tiếp` |
| mục 2d: link thật là `workers.dev/<hex>::<hex>/Tên (2025) 2160p … .mkv` | `IsMedia()` bắt đúng đuôi `.mkv|.mp4|.m4v|.avi|.mov|.ts|.m3u8` |
| vòng 15: link nào trả ra, phát link đó | `Video` → `RedirectToPlay(first.Url)` — **không** `VideoCore`, **không** `/proxy`, **không** HLS hop |

**Ba quyết định kỹ thuật đáng nhớ:**
1. `Video` tự viết thay vì gọi `VideoCore`: `ResolveHub` (`HubController.cs:413`) là `protected`
   mà **không `virtual`**, còn `StreamHeaders`/`IsPlayable` là `private`. Sửa base để virtual =
   đánh cược chuỗi play đã được thiết bị xác minh cho hai nguồn đang chạy ⇒ nguồn mới tự lo.
2. **Resolve lúc BẤM, không lúc dựng danh sách**: một lượt bypass = 3–6 request; 10 tập × 5 nhóm
   thì resolve trước là chết `httptimeout 30`. Nên `HubEntry.Url` CHỨA CHÍNH mồi `?sid=<base64>`,
   `src` đi qua `Enc`/`Dec` vì blob có `+` và `=`.
3. `sealed record Release` (không đặt tên `Group`) — base đã giải thích ở `HubController.cs:60`:
   tên `Group` che `System.Text.RegularExpressions.Group` và file này dùng `Match.Groups` dày đặc.

**Chưa có gì được xác minh bằng máy** — sandbox này không có compiler; Lampac compile module
bằng Roslyn trong bộ nhớ. Vòng test đầu trên thiết bị phải trả lời đúng 3 câu, và log đã được
in sẵn để trả lời:
1. `0 bài ứng viên | q=… a=… hosts=… classes=…` → cách tìm bài (search slug) sai hay host config sai;
   `không thấy bài nào` nhưng `a=` lớn → DOM bài khác giả định, đổi selector.
2. `mùa -1: …` / `0 mùa trong bài | nhãn=[…]` → nhãn nhóm có chứa “Season N” hay không
   (nếu không, menu mùa phải lấy từ TMDB, không từ bài viết).
3. `bypass ok (rounds=N) -> …` + `ăn: resume https://…workers.dev/….mkv` → chuỗi countdown sống;
   `bypass hết form (rounds=…) mà không thấy ?go=` → cần JS thật (lúc đó mới tính `rch`/`.cs3`).
   `0 link chơi được trên … head=…` → thứ tự nút sai, không phải chuỗi sai.

---

## 7. Vòng 1 trên thiết bị (1/9, `The Whisper Man (2026)`, tmdb 860508) — hai lỗi thật, của MÌNH

Log anh gửi:

```text
uhdmovies: 0 bài ứng viên | q='The Whisper Man 2026' a=26 hosts=uhdmovies.autos:22 uhdmovies.mov:2 modlist.in:2 classes=(không class nào gợi ý nút tải)
uhdmovies: 0 bài ứng viên | q='The Whisper Man' a=26 ...
uhdmovies: không lấy được bài nào (tmdb=860508, queries=2) | site=https://uhdmovies.autos
```

`a=26` GIỐNG HỆT nhau cho hai query = cùng một trang vô dụng (menu + links `uhdmovies.mov`,
`modlist.in`). Đối chiếu lại trang thật bằng tay, ra hai nguyên nhân:

1. **`Anchors(...)` giữ nguyên mặc định `onlyFileHost: true`.** Mặc định đó chỉ giữ hubcloud / gdflix /
   driveseed... còn link KẾT QUẢ TÌM KIẾM nằm trên chính `uhdmovies.autos` ⇒ bị lọc sạch, "0 bài ứng
   viên" dù trang CÓ bài. Movies4U truyền `onlyFileHost: false` từ lâu — đây là chỗ em chép thiếu.
2. **Điều kiện "trang tìm kiếm hợp lệ" là trang không rỗng.** `…/search/<q>` (mã hoá `%20`) trả trang
   "không kết quả" nhưng HTML vẫn đầy menu ⇒ code cũ coi là hợp lệ và không bao giờ thử `?s=`. Bằng
   chứng: `https://uhdmovies.autos/?s=the+whisper+man` được chính site chuyển về
   `/search/the+whisper+man` và trả đúng `/download-the-whisper-man-2026-…/`.
   ⇒ v22 thử `?s=` TRƯỚC, `/search/` sau; điều kiện nhận trang là **có chuỗi `/download-`**; mỗi lần thử
   in `tìm ăn ở dạng …` hoặc `dạng … không có bài (lần N, a=…)`.

### Cấu trúc bài PHIM LẺ thật (khác bài series ở mục 1 — ghi lại để khỏi đoán)

```text
## **Download The Whisper Man (2026)** **1080p Web-DL** **[Dual Audio]**
**The Whisper Man (2026) 2160p NF WEB-DL DV HDR 10bit HEVC [Hindi DDP 5.1 + English DDP 5.1] x265 (KRATOS-UHDMovies)**
**[16.42 GB]**
[Download (G-Drive)](https://cloud.unblockedgames.world/?sid=<blob>)
... lặp lại: 2160p SDR [13.39 GB], 1080p x264 [6.61 GB], 1080p HEVC 10bit [2.60 GB]
```

* 4 "nhóm" = 4 bản release, mỗi nhóm đúng MỘT nút `Download (G-Drive)` ⇒ movies branch dựng 4 nút
  nguồn, không có menu chất lượng — đúng luật vòng 13/15.
* Nhãn nằm ở HAI dòng: tên release (2160p/x265/tên encoder) rồi dòng `[dung tích]` sát nút.
  `NearestLabelBefore` một mình chỉ bắt được `[16.42 GB]` ⇒ 4 nút cùng kiểu nhãn "16 GB", không phân
  biệt được bản nào. Thêm `ReleaseLine()`: quét `<strong>` NGƯỢC từ nút (window 1500 ký tự), lấy dòng
  đầu tiên có `\d{3,4}p|4k|2160|x26[45]` rồi nối với nhãn cũ.
* Bài cũng có ~6 anchor `?sid=` MỒI (1080p x264, 4k HDR, UHDMOVIES, HEVC…) dài ~88 ký tự để câu view;
  `sid` thật dài ~344. Vẫn an toàn vì lọc theo CHỮ TRÊN NÚT (`episode|download`) chứ không theo host
  `?sid=` (kết luận ở mục 1). Nếu cần phân biệt thật thì dùng độ dài blob, đừng đoán host.
* Không có nhãn "Season N" nào ở bài phim lẻ ⇒ `SeasonOf` trả 0, movies branch không đụng mùa ✓.

### Vòng 2 cần thấy gì

`tìm ăn ở dạng ?s={0} (lần 1)` → `movie: 4 nút từ 4 nhóm` → bấm nút →
`bypass ok (rounds=…) -> …driveleech|driveseed…` → `ăn: resume https://…workers.dev/….mkv`.
Vẫn `0 bài ứng viên` mà `dh>0` ⇒ `Anchors` còn đang lọc gì đó, sửa tiếp; `dh=0` ⇒ site đổi trang tìm
kiếm (lúc đó mới xem `uhdmovies.mov` có phải domain mới thật không).

---

## 8. Vòng 2 trên thiết bị (1/9): chuỗi bypass SỐNG, kẹt ở trang file — và vì sao

```text
uhdmovies: bypass ok (rounds=2) -> https://driveseed.org/r?key=QWNZTTJu…
uhdmovies: trang file nháy tiếp -> https://driveseed.org/r?key=QWNZTTJu…
uhdmovies: 0 link chơi được trên https://driveseed.org/r?key=… | a=11 hosts=driveseed.org:9 cdn.video-gen.xyz:1 t.me:1
uhdmovies: 0 link chơi được từ https://cloud.unblockedgames.world/?sid=a3Y4azk3…
GStreamer: add rejected source. Reason=probe process exited with code 0
```

Ba thứ đã được xác nhận bằng máy (không còn là suy luận):
**`rounds=2` đúng như CSX** ⇒ chuỗi `form#landing` × 2 + `?go=` + cookie `_wp_http2` vượt được cả hai
trang countdown, không cần JS. Và **`driveseed.org/r?key=<base64>` là trang file thật** (không phải
`/f/<id>` như hồi mục 2d) — họ đã đổi endpoint sang `/r?key=`.

Còn "0 link" là **lỗi của mình, không phải của site**: `hosts=driveseed.org:9 cdn.video-gen.xyz:1`
nghĩa là trên trang CÓ một link sang `cdn.video-gen.xyz` — chính là file — nhưng `Resolve` bắt
`IsMedia()` (phải có đuôi `.mkv/.mp4/...`) nên link không đuôi bị loại sạch.

### Đối chiếu lại code CSX (`CineStream/src/main/kotlin/com/megix/Extractors.kt`, đọc 1/9)

| CSX | Mình (v23) |
|---|---|
| `class Driveseed : Driveleech()` (dùng chung `getUrl`), `requiresReferer = false` | không đòi Referer khi play ✓ (`PlayHeaders()` chỉ UA+Accept) |
| `if (url.contains("r?key="))` → `selectFirst("script").data().substringAfter("replace(").substringBefore(")")` → GET `baseUrl + temp` | `again` = regex `replace\(\s*["']…` trên TOÀN trang (rộng hơn) rồi GET + **đổi base** (fix log "nháy tiếp" vòng trước in `file+again` nên không đọc ra gì) |
| nút lấy từ `div.text-center > a`, không cần biết chữ | thêm tier `center`: anchor nằm trong khối `text-center` được nhận kể cả khi nhãn rỗng/chỉ có icon |
| `Cloud Download` → href trần; `Instant Download` → `app.get(href, allowRedirects=false).headers["location"].substringAfter("?url=")` | `Unwrap()`: nếu link/Location có `?url=` thì **dùng phần sau `?url=`** — đây là chỗ mình sẽ ăn trang HTML nhảy tiếp thay vì file nếu không cắt |
| `Resume Cloud` → GET href → `a.btn-success`; `Direct Links` → `?type=1`/`?type=2` → `a.btn-success` | `LinkFrom(..., "btn-success")` / `CfLinks` ✓ (LinkFrom giờ chọn btn* ĐẦU TIÊN trông như file, không lấy bừa cái đầu) |
| `Resume Worker Bot` → token + `fetch('/download?id=…')` + POST giữ `PHPSESSID` → JSON `.url` | `WorkerLink` ✓ y hệt |
| `Name`/`Size` từ `ul > li.list-group-item` (`substringAfter("Name : ")`) | `Text(page, "list-group-item…Name…")` ✓ |

### Thay đổi v23

1. `IsMedia()` → **`Playable(url, file, loose)`**: nhận link tuyệt đối không phải ảnh/css/js;
   `loose` cho cả link cùng host miễn khác trang vừa lấy (`/dl/<hash>`). `IsMedia` chỉ còn dùng để
   XẾP HẠNG (media đứng trên link không đuôi, vì không đuôi thường là link chỉ-tải-về → dán `[download]`).
2. Ba tầng nhận nút: từ khoá DriveLeech cũ → `class*="btn"` → `text-center`/host khác (tier `ext`).
3. `JunkLink()`: loại menu, `t.me`, social, ảnh/js (log có `t.me:1` — nếu không lọc, nó thành "nút").
4. Thăm `Http.GetLocation(file)` TRƯỚC khi mò nút: `/r?key=` nhiều khi 302 thẳng tới CDN ⇒ một request
   là xong (`ăn: redirect …`).
5. `AnchorDump(page, 8)` trong log "0 link chơi được" + bắt tên trang challenge
   (`just a moment|attention required|cf-challenge|checking your browser|…`) và in thẳng câu
   "đây là trang Cloudflare/JS → bật `rch`". Vòng sau KHÔNG cần đoán nữa: log sẽ nói luôn là
   "không có nút" hay "có nút mà site chặn bằng JS".

**Vòng 3 mong đợi**: `ăn: redirect https://cdn.video-gen.xyz/…` (hoặc `ăn: center/media/ext …`) rồi
`play … direct` và video chạy. Nếu log in `!! ĐÂY LÀ TRANG CHALLENGE` ⇒ bật `rch` trong `init.conf`
(Lampac đã có sẵn đường đó — `httpHydra` tự đi qua `rch.Get`), không cần viết gì thêm trong module.
