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
