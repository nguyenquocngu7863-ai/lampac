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

## 3. Thiết kế module (quyết định, không phải gợi ý)

1. **Module riêng `Modules/OnlineENG/UhdMovies/`**, KHÔNG nhét vào MoviesHub. Lý do đúng theo
   luật đã ghi ở `notes/FILEHOST-SOURCE-FORMULA.md` mục 6: chỉ ở cùng assembly khi **dùng chung
   resolver**; đây là resolver khác hoàn toàn (DriveLeech/DriveSeed + WP landing vs HubCloud). Để
   riêng còn vì: module MoviesHub **đang chạy tốt**, một file mới trong assembly của nó mà biên dịch
   fail là **sập cả Movies4U + MoviesDrive**.
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
