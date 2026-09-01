# XdMovies — hồ sơ nguồn mới (mở 2026-09-01, sau khi đóng UhdMovies)

Đọc cho hết trước khi code: mục 3–4 là lý do nguồn này **khác hẳn** uhd về chuyện bypass.
Bài học cũ của MoviesHub vẫn áp dụng y nguyên: `notes/MOVIES-HUB.md` mục 8 (tôn trọng 302, không
`FollowRedirects`, `/lite/*` để thoát CORS), `notes/FILEHOST-SOURCE-FORMULA.md`, và toàn bộ
`notes/UHD-MOVIES.md` mục 11 (chuỗi countdown/lưu đồ helper).

## 1. Hai post anh đưa

* `https://top.xdmovies.wtf/series/reacher-2160p-1080p-hindi-english-download-108978` (phim bộ)
* `https://top.xdmovies.wtf/movies/the-whisper-man-2160p-1080p-hindi-english-download-860508` (lẻ)

Em đã đọc bài lẻ (1/9/2026). Cấu trúc render:

```text
![poster](https://image.tmdb.org/t/p/w500/<path>.jpg)
## The Whisper Man
**Rating:** 6.3 / 10 | **Genres:** ... | **Release Date:** 2026-08-27
**Audios:** Hindi, English, Tamil, Telugu | **Sources:** Netflix
<overview>
### Star Cast: ...
### Download Links:
Netflix Versions(7)                                   <- nhãn nhóm + số file
The.Whisper.Man.2026.720p.NF.WEB-DL.DDP5.1.Atmos.H.264-XDMovies.com.mkv
[0.72 GB](https://link.xdmovies.wtf/download/Aj9q80U_puqGlE3hVx7d0QnpujCCzoZTARM5rKI7lrw)
The.Whisper.Man.2026.1080p.NF.WEB-DL.DDP5.1.Atmos.H.265-XDMovies.com.mkv
[1.62 GB](https://link.xdmovies.wtf/download/SRiYjUa3GdF040854J88l34JgJ1ZQW2Y3Z9P0K9Z1gQ)
... (2160p DV.HDR ... 7 file)
```

Ba điều rút ra ngay:

1. **Mỗi chất lượng MỘT link**, tên file nằm trên dòng link ⇒ nhãn chất lượng KHÔNG nằm trong thẻ
   `<a>`, nhưng cũng chẳng cần `LabelBlocks` lằng nhằng như uhd: đọc dòng tên file đứng ngay trước
   link là đủ (kể cả khi site nhét nhiều cặp vào một dòng, kiểu `href` nọ kế `href` kia — vẫn đúng).
2. **Số cuối slug = TMDB id**: `860508` khớp đúng `tmdb=860508` mà log uhd resolve cho The Whisper Man.
   Nếu server chịu resolve theo id (thử `…/movies/x-860508` hoặc `/movies/-860508`) thì **khỏi search**,
   mở post thẳng từ `tmdb_id` — nhanh hơn và hết luôn cảnh lộn mùa (bài Reacher của xdmovies có slug
   `-108978`, TMDB series id 108978 = Reacher ✓).
3. Tên file là **x mediainfo** (`1080p.NF.WEB-DL.DDP5.1.Atmos.H.265`), `XDMovies.com` gắn cuối tên
   ⇒ parse chất lượng/audio/codec bằng `QualityLabel` + regex tên file của `HubController`.

## 2. Trang link: đây mới là vấn đề (đọc kỹ)

`https://link.xdmovies.wtf/download/<token 43 ký tự urlsafe-base64>` **không** trả file. Em đọc được
(trạng thái 302 tự động) tới:

```text
https://latestnewsonline.sbs/r/jqkKyHK4
title: "XDMovies - Get Your Link"
- "Click the button below to generate your download link"
- "6" + "seconds" + "⏸️ Timer paused - please stay on this tab"   <- ĐẾM NGƯỢC 6s, chạy bằng JS
- Cloudflare TURNSTILE: challenges.cloudflare.com/.../turnstile/f/av0/rch/uo6hr/
  sitekey 0x4AAAAAACwMJhFoINTv6AGb   <- "Checking your Browser… / Verifying… / Success!"
- 3 nút: "Generate Link" -> "➡️ Continue to Step 2" -> "🔗 Go to Link"
```

⇒ Khác uhd ở hai chỗ: (a) uhd chỉ là **form POST + meta-refresh** (HttpClient làm được), còn đây là
**đếm ngược JS + Turnstile** (HttpClient *bất lực*, không có token là không có link); (b) uhd chỉ 1 vòng
redirect còn đây là 3 bước (generate → step 2 → go). Server **fls** và **pixel** anh nói nằm ở step 2,
em chưa thấy HTML thật của bước đó.

## 3. Hướng giải quyết (chưa code, cần anh chốt)

**Kế hoạch chính: đi qua `rch`.** Lampac đã có sẵn công cụ:

```csharp
var res = await rch.Headers(url, null, headers);   // (JObject headers, string currentUrl, string body)
```

`Shared/Services/HTTP/RchClient.cs:230`, đang được `Ebalovo`, `Porntrex`, `VideoDB` dùng để theo chuỗi
redirect *trong trình duyệt thật của client*. Trang đếm ngược + Turnstile đúng là việc của trình duyệt
thật ⇒ gọi `rch.Headers("https://link.xdmovies.wtf/download/<tok>")`:
* `currentUrl` sau khi JS chạy = URL cuối (rất có thể là thẳng con **fls** hoặc **pixel**);
* `body` = HTML step 2, bóc các nút server ra, ưu tiên theo thứ tự `fls` > `pixel` (pixel =
  pixeldrain? nếu đúng thì **cực đẹp**: `pixeldrain.com/api/file/<id>?download` là link thẳng, có
  `Accept-Ranges`, tua được, khỏi cần vượt gì cả — chỉ cần lấy được `<id>`).
Nếu `rch` không bật (`IsRchEnable` false, `HttpHydra.cs:100`) thì nguồn này **không có cửa tự động**:
phải hide nút hoặc mở link bằng `webview` (lại rơi vào cái bẫy mà VidLink/Movies4U chết vì CORS — nhưng
với webview *để người dùng tự bấm* thì khác, nó không phải fetch).

**Việc anh cần trả lời/lấy cho em (3 thứ):**
1. Máy anh đã bật `rhub` / có rch client chưa (`init.conf` → `rch_access`, `SettingController:350`)?
   Không có thì nguồn này bỏ, đừng code.
2. Cho em **URL cuối cùng** sau khi bấm hết 3 bước trên điện thoại (hoặc HTML của bước "Go to Link"),
   kèm tên host của **fls** và **pixel**. Chỉ cần một mẫu là viết được resolver.
3. `gamerxyt.com/bgmi/` em vào thử là **trang tin game BGMI 4.0** (Battlegrounds Mobile India), không phải
   kho phim ⇒ có gõ nhầm link không? Nếu ý anh là "fls/pixel thấy ở trang đó" thì gửi lại link đúng.

## 4. Thiết kế module khi đã trả lời xong

* File mới `Modules/OnlineENG/MoviesHub/XdmoviesController.cs` — **cùng assembly** (luật vòng 7), route
  `lite/xdmovies`, section `XdMovies` (host `https://top.xdmovies.wtf`, `displayindex` lấy chỗ uhd để lại).
* `Collect`: `Metadata` trước; nếu có `tmdb_id` thì thử URL dựng theo id, nếu fail mới fallback tìm
  theo tên (`/search?q=…` — chưa đọc trang search của site này); `SourcedTpl` dùng `Bypass` của uhd
  (sửa: `Anchors` ở đây phải chấp nhận thẻ `[size GB](/download/<tok>)` — nếu HTML thật là markdown-like
  thì phải parse text, **phải đọc HTML thô trước khi quyết**, đừng tin bản render).
* `Resolve`: token `/download/<tok>` → `rch.Headers` (xem mục 3) → chọn `fls`, fallback `pixel`; nếu
  không phải media host thì 302 verbatim (luật vòng 15); `streamproxy:false` cho link range-capable.
* Nhóm theo `Sources:`/`Netflix Versions(7)` ⇒ cùng bài có thể có nhiều "version" (HQ/NF) ⇒ `SameFilm`
  của uhd dùng được nếu cần tách; series thì phải đọc bài Reacher trước (chưa đọc, đừng đoán).
* Mỗi commit MoviesHub: sửa `Build` trong `HubController.cs`.

## 5. 01/9 — hai phát hiện đổi hướng đi (đọc mục này trước khi code)

**(a) `gamerxyt.com` là mặt nạ của chính kho này, và nó phát link HubCloud.**
`https://gamerxyt.com/bgmi/` là trang **WordPress** (cấu trúc "Skip to content" + khối comment) —
không phải kho phim. Nó là **trang ngụy trang**: không có `Referer` từ "trang trước" thì chỉ hiện
link game; kèm referer thật thì hiện link phim. Referer anh đưa:

    https://hubcloud.cx/drive/qosca4tao0ob1ss

⇒ ý nghĩa kỹ thuật: request phải gửi `Referer: <link hubcloud>` (không phải User-Agent, không phải cookie).
Đây là chi tiết DUY NHẤT khác một module WordPress bình thường.

**(b) HubCloud không cần vượt rào — `HubController` đã có resolver và MÁY ĐÃ XÁC MINH.**
`HubController.cs`: `#region hubcloud / gdrive resolver` (~408), `IsHubHost` (~811) nhận
`hubcloud|hubcdn|hubdrive|vcloud`, `HubExtract` (~968) bóc `var url = '…'` trong `<script>` (vcloud:
`atob(atob())`), `WithLatestMirror/LatestRoot` (~893~948) tự đổi sang mirror sống mới nhất, và
`ResolveHub` đã xử `?downloadfile=true` + `/drive/<id>/<tên>.mkv`. **Movies4U chính là engine này** —
comment ở `Movies4UController.cs:352` ghi nguyên văn kiểu link y hệt:
`[🚀 Hub-Cloud [DD]](https://hubcloud.cx/drive/kk1lk7kvdmvim8m) [🚀 GDFlix](https://gdflix.dev/…)`.

⇒ Kết luận chiến lược: **đừng đánh nhau với Turnstile của xdmovies nữa.** Nguồn mới nên là
"gamerxyt (mở khoá bằng Referer) → link HubCloud/GDFlix → `ResolveHub` sẵn có". xdmovies giữ làm
fallback nếu sau này cần, và khi đó mới tính `rch`.
Caveat phải nói trước: HubCloud ăn file từ Google Drive nên **không bảo đảm `Accept-Ranges`** ⇒
có thể tua kém (đúng bệnh của Movies4U/MoviesDrive hiện tại). Nếu `pixel` của xdmovies là
pixeldrain.com thì nó mới là món tua được; cái đó nằm sau Turnstile ⇒ để vòng sau.

**(c) `rch`/`rhub` là gì (anh hỏi) — trả lời ngắn:** `Shared/Services/HTTP/RchClient.cs` mở một
kênh websocket `/nws`; mỗi client (app Lampa trên TV/điện thoại, hay một Lampac khác) đăng ký vào
`RchClient.clients`, và Lampac có thể **nhờ client đó mở URL bằng trình duyệt thật**:
`rch.Headers(url)` trả `(headers, currentUrl, body)` sau khi JS chạy xong — vì thế nó mới đi qua
được Cloudflare/đếm ngược. Bật = `init.conf` có `"rhub": true` và `rch_access` (chuỗi cho phép
`apk` / `cors` / `web`, xem `Shared/Models/Base/BaseSettings.cs:70-95`) — `RchClient.enable`
(~123) là `init.rhub && enableRhub`; không bật thì `Http.Get(..., safety: true)` tự lặng lẽ về
`Http` thường (nên bật hay không đều không hỏng gì). Nói cách khác: **điện thoại/TV của anh phải là
trình duyệt chạy hộ Lampac** — Turnstile sẽ do máy anh giải, chứ server không tự qua được.
Vòng này chưa cần: xem (a)(b).

**(d) Ghi cho đời sau khỏi mất thì giờ:** sandbox coding **không vào được Internet mở** — chỉ
`api.github.com` sống; `curl` vào gamerxyt/hubcloud/google đều `SSL_ERROR_SYSCALL` (allowlist, không
phải site chết). `fetch_page` thì đọc được gamerxyt nhưng **không tự đặt header được** ⇒ muốn thử
referer phải chạy curl trên máy anh:

    curl -s -A "Mozilla/5.0" -e "https://hubcloud.cx/drive/qosca4tao0ob1ss" https://gamerxyt.com/bgmi/ \
      | grep -Eo 'href="https?://[^"]+"' | sort -u | grep -Ev 'gamerxyt|wp-|gravatar' | head -40
