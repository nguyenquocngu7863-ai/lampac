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

## 6. 01/9 — đính chính của anh (quan trọng, đừng lặp lại lỗi em) + vòng 1 đã code

**(a) Em kết luận SAI ở mục 5(b): không phải "khỏi vượt Turnstile".** Anh nói: *phải vượt được trang
link mới có link hubcloud* ⇒ HubCloud là **kết quả SAU gate** (bước "Go to Link"), không phải đường
tắt. Ghi lại cho đời sau: trình tự đúng là

    bài post -> link.xdmovies.wtf/download/<token> -> [latestnewsonline.sbs/r/<code>: đếm ngược 6s +
    Turnstile + 3 nút] -> server fls / pixel / **HubCloud** -> ResolveHub -> file

**(b) rhub:** anh nói "tui đang dùng lampa và xem được pỏn hub nên chắc là có rhub". Chưa chắc:
Pornhub/Onlyfans chạy bằng `Http` thường cũng được. Bằng chứng duy nhất là `rch.enable`
= `init.rhub && enableRhub` (`RchClient.cs:123`) **và** client phải `apkVersion >= 484`
(`RchClient.cs:236` — `Headers()` về `default` nếu dưới). Module mới in ra đúng câu đó khi tắt, nên
log thiết bị sẽ trả lời dứt điểm, khỏi đoán.

**(c) Vòng 1 (`v27-xdmovies-rch-gate`) — `Modules/OnlineENG/MoviesHub/XdmoviesController.cs`:**
* `Collect`: `TmdbMeta` -> tìm `?s=`/`/search/` -> **nhận bài theo TMDB id cuối slug** (`IdOf`), đọc bài,
  `Blocks()` quét MỘT LƯỢT (không cửa sổ ký tự — luật vòng 30 bên uhd), `FileNameNear` lấy tên file,
  `QualityLabel`, `IsPack` bỏ zip/batch, `ParseEpisode` cho `S02E05`/`1x05`/`season..episode`,
  `ShortGroup` giữ nhãn nhóm ("Netflix Versions"). `HubEntry(Label, Url=link /download/<tok>, S, E, Group=phim)`.
* `Resolve`: `rch.Headers(token, <JS>, headers)` — JS tự chờ + bấm `Generate Link` -> `Continue/Step 2`
  -> `Go to Link`, rồi trả `JSON` kèm mọi `href` khớp `hubcloud|gdflix|pixeldrain|fls|filelions|drive|.mkv|.mp4`.
  `Rank()`: fls/filelions 5, pixel 4, hubcloud/vcloud/gdflix 3, media 2. HubCloud/GDFlix -> `ResolveHub`
  (có sẵn, mirror rotation có sẵn); pixeldrain -> `Pixel()` (có sẵn, `/api/file/<id>?download` **tua được**).
* Không có `rch` -> log một câu rõ, KHÔNG trả link giả (bài học "wins the wrong button").
`data` (script) là thứ Ebalovo/Porntrex/VideoDB chưa dùng bao giờ -> có thể client bỏ qua; module log đủ
`rch len=… cur=…` / `gate mở ra N url` để vòng 2 biết đường đổi sang `rch.Get`+bóc form hoặc bắt người
dùng bấm.

## 7. 01/9 — bài học từ lần biên dịch đầu tiên trên máy (đừng lặp)

Log thiết bị: `XdmoviesController.cs(309,49): error CS9008` (em đánh nhầm `@@"` -> `\@` bị hiểu là
escape) và **hậu quả không chỉ là module không nạp**: `Core.Startup.ConfigureServices` nổ
`Unhandled exception` rồi `proot info: vpid 1: terminated with signal 6` ⇒ **Lampac không bật được
gì cả**, kể cả các nguồn khác. Hệ quả quy trình:
1. Trước khi ship file .cs phải tự soi ba thứ: `@@`, escape trong string thường (`\s`, `\.` phải là
   `\\s`/`\\.` hoặc chuyển sang verbatim `@"..."`), và ngoặc cân bằng.
2. `error CS` của module nào cũng làm chết cả tiến trình, không "tử tế" bỏ qua module lỗi.
   Đường thoát cho anh khi kẹt: `rm -f /root/lampac/module/OnlineENG/MoviesHub/<file>.cs` rồi
   `lampac stop; lampac start` (module cả cụm sẽ không nạp nhưng máy xem được tiếp).
3. `--sync` của script CŨ không xoá file đã rút khỏi tree ⇒ `UhdmoviesController.cs` ở lại, gọi
   `ModInit.uhd` đã gỡ -> CS0117. Đã sửa trong `sync_latest_modules()` (commit `ea050cf`).

## 8. Lệnh ĐO khi test trên máy (chụp một phát là đủ viết vòng 2 — đừng đoán nữa)

Module đã tự log 4 thứ, đọc theo thứ tự này:

| dòng log | ý nghĩa | nếu sai thì sao |
|---|---|---|
| `rhub enable=… client=…` | rhub bật chưa, client là app nào, `apkVersion` bao nhiêu | `client=KHONG CO` ⇒ không ai giải Turnstile hộ, mọi đường khác đều vô ích |
| `tìm q=… bai=… khop_id=…` | trang tìm kiếm có bài không, bài có khớp TMDB id ở slug không | `bai=0` ⇒ site không dùng `?s=`, phải đọc HTML trang search (lệnh P2) |
| `bài <url>: N nút từ M khối` | parser ăn được bao nhiêu link | `0 link /download/` ⇒ HTML không phải khối `<p>` (dùng P1 để biết thật sự là gì) |
| `rch len=… cur=…` + `gate mở ra N url` | client có chạy JS mình gửi không, gate nhả những link nào | `len=0`/`cur=` rỗng ⇒ `data` bị bỏ qua, vòng 2 đổi sang `rch.Get` hoặc bấm tay |

### P0 — gom log (chỉ gõ sau khi đã bấm một chất lượng trong Lampa)

```bash
lampac logs 2>/dev/null | grep -a "xdmovies:" | tail -60
```

### P1 — HTML THÔ của bài post (_markup thật_, không phải bản render)

```bash
cd ~
curl -fsSL -A "Mozilla/5.0" "https://top.xdmovies.wtf/movies/the-whisper-man-2160p-1080p-hindi-english-download-860508" -o xd-post.html
echo "size=$(wc -c < xd-post.html) nextdata=$(grep -c '__NEXT_DATA__' xd-post.html) dl=$(grep -o '/download/' xd-post.html | wc -l)"
grep -oE 'href="[^"]*/download/[^"]*"' xd-post.html | head -3
grep -oE '<(p|h[1-6]|li|div|td|a)[^>]{0,90}' xd-post.html | sort | uniq -c | sort -rn | head -18
```

Ba con số đó quyết định tất cả: `nextdata>0` ⇒ link nằm trong JSON của app (phải parse JSON, đừng
parse HTML); `dl=0` ⇒ bài render bằng JS (⇒ module phải đọc qua `rch` luôn, kể cả trang post);
đoạn `uniq -c` cho biết nhãn chất lượng nằm trong thẻ gì để `Blocks()` bắt đúng.

### P2 — dựng URL thẳng từ TMDB id (bỏ được search thì bỏ)

```bash
for u in movies/x-860508 movies/-860508 movies/860508 movies/the-whisper-man-860508 series/x-108978; do
  printf '%-34s ' "$u"
  curl -s -o /dev/null -w 'code=%{http_code} size=%{size_download} -> %{redirect_url}\n' -A "Mozilla/5.0" "https://top.xdmovies.wtf/$u"
done
```

`code=200 size>20000` ở dòng nào ⇒ module dựng URL theo đúng dạng đó, khỏi search, khỏi lộn mùa.

### P3 — đo cái gate (quan trọng nhất)

Lấy một token từ bài (nó nằm trong `href`), rồi:

```bash
T='<dán token 43 ký tự vào đây>'
curl -fsSL -L -A "Mozilla/5.0" -c xd-ck.txt -e "https://top.xdmovies.wtf/" -o xd-gate.html \
  -w 'code=%{http_code} url=%{url_effective} size=%{size_download}\n' "https://link.xdmovies.wtf/download/$T"
echo "--- api/form mà trang gọi:"
grep -oE '"/[a-z0-9_/-]*(api|link|generate|step|check)[a-z0-9_/-]*"|fetch\([^)]{0,70}|action="[^"]+"' xd-gate.html | sort -u | head -20
echo "--- link server lọt ra ngoài chưa:"
grep -oE 'https?://[^"'"'"' <>]+' xd-gate.html | grep -Ei 'fls|pixel|hubcloud|gdflix|drive|\\.mkv|\\.mp4' | sort -u | head -20
echo "--- turnstile/cloudflare:"
grep -oE 'turnstile|sitekey|cf-turnstile|challenges\.cloudflare' xd-gate.html | sort | uniq -c
```

Nếu `--- link server` đã có gì đó ⇒ gate nhả link trong HTML (HttpClient đủ, khỏi `rch`). Nếu rỗng mà
`--- api/form` chỉ ra một endpoint POST ⇒ vòng 2 gọi đúng endpoint đó với token giả/xin.

### P4 — referer của gamerxyt (thuyết "trang ngụy trang")

```bash
curl -s -A "Mozilla/5.0" -o gx-plain.html https://gamerxyt.com/bgmi/
curl -s -A "Mozilla/5.0" -e "https://hubcloud.cx/drive/qosca4tao0ob1ss" -o gx-refer.html https://gamerxyt.com/bgmi/
echo "plain=$(wc -c < gx-plain.html) refer=$(wc -c < gx-refer.html) khac=$(cmp -s gx-plain.html gx-refer.html && echo 0 || echo 1)"
grep -oE 'href="https?://[^"]+"' gx-refer.html | grep -Ei 'hubcloud|gdflix|drive|\\.mkv' | sort -u | head -20
```

`khac=1` + có link hubcloud ⇒ đúng là mặt nạ, và khi đó gamerxyt (không cần gate) mới là đường chính.

### Thoát kẹt khi module làm chết Lampac

`error CS` của bất kỳ module nào cũng nổ `Startup.ConfigureServices` ⇒ Lampac không bật. Đường lùi:

```bash
proot-distro login ubuntu -- bash -c 'rm -f /root/lampac/module/OnlineENG/MoviesHub/XdmoviesController.cs; rm -rf /root/lampac/module/OnlineENG/MoviesHub/obj /root/lampac/module/OnlineENG/MoviesHub/bin'
lampac stop; sleep 2; lampac start
```

## 9. 01/9 — log máy thật vòng 1: `blocked ở collection`, `Collect` chưa chạy

Anh gửi: `xdmovies: collection rỗng (tmdb=860508, imdb=tt11561116)` + `xdmovies: blocked ở collection
(enable=True, rip=False)`, trong khi `moviesdrive: 8 link cho collection ... build=v27-...` chạy ngon.
Suy ra đúng, không phải parser: `CollectionCore` gọi `IsRequestBlocked(rch: false)` TRƯỚC khi gọi
`Collect` (`HubController.cs:130`) nên **không có dòng `tìm q=…` nào là bình thường** — module chưa tới
được trang site. `enable=True, rip=False` loại 2 điều kiện đầu của `IsRequestBlockedRchOrDisable`, còn
lại đúng 3 khả năng: `NoAccessGroup` (`BaseController.cs:1098`, chỉ chặn khi `init.group > 0`),
`init.workinghours` (`BaseOnlineController.cs:203`), hoặc handler `EventListener.BadInitialization`
của module khác (ForkPlayerXML / MsxNative / Potok). Log cũ in mỗi enable+rip nên KHÔNG phân biệt được
⇒ từ build này in cả `group / user / workinghours / accsErr / badInit=<type>`.

Cách đọc: `group>0` + `user=null` ⇒ section XdMovies bị gán nhóm truy cập (sửa trong Admin Panel hoặc
`init.conf`); `workinghours>0` ⇒ họ/anh đặt giờ làm việc; `badInit=<Type>` mà accsErr=`-` ⇒ thủ phạm là
handler của module khác chứ không phải config của XdMovies.

## 10. 01/9 — vì sao `blocked ở collection` với enable=True, rip=False (đọc trước khi ai đó sửa lại)

Số đo từ máy: `group=0, user=null, workinghours=0, accsErr=-, badInit=StatusCodeResult` ⇒ loại
NoAccessGroup và workinghours. `StatusCodeResult` + Lampa báo `503 Service Unavailable` chỉ có một nơi
đặt: `IsCacheError` (`Shared/Controllers/BaseController.cs:1003-1017`). Ba sự thật rút ra:

1. `IsCacheError` **chỉ chạy khi `init.rhub == false`** (dòng 1005: `if (init.rhub) return false;`).
   `HubController.Section()` đặt `rhub = false` cho mọi section của MoviesHub ⇒ XdMovies bị chặn.
2. Error cache ghi theo key của response (`ResponseCache.ErrorKey`) và `OnError(...)` mặc định
   `gbcache: true` — nên một lần lỗi (502 không có link, resolve fail) là **đầu độc cả nguồn**:
   mọi request sau đó bị trả 503 trước khi `Collect` chạy, không log dòng nào của module.
   Triệu chứng đúng như máy anh báo: `collection rỗng` + `blocked` mà không có `tìm q=…`.
3. `rch:` trong `IsRequestBlocked` không phải "nguồn này CÓ dùng rch hay không" mà là câu hỏi
   "request này đến từ rch hay không": gọi `IsRequestBlocked(rch: false)` trên section có `rhub=true`
   bị block ngay (`BaseOnlineController.cs:237-241`), nên CollectionCore phải hỏi `rch: init.rhub`.

Sửa (build này): `CollectionCore` -> `IsRequestBlocked(rch: init.rhub, rch_check: false)`;
`Video` của XdMovies -> `rch: init.rhub, rch_check: !play`; `xd.rhub = true` trong `ModInit`
(vừa là điều kiện để `safety:true` thật sự đi qua rch, vừa làm `IsCacheError` tắt ngìm với nguồn này);
mọi `OnError` trong XdMovies -> `gbcache: false` để không tự đầu độc nữa. MoviesDrive/Movies4U giữ
`rhub=false` như cũ (chúng không cần rch).

## 11. 01/9 — máy báo "trang tìm rỗng": site này KHÔNG có search kiểu WordPress

`?s=` và `/search/` đều rỗng với `top.xdmovies.wtf` (log `tìm q='The Whisper Man 2026'` 4 lần, rồi
`không lấy được bài nào`) — trong khi `fetch_page` đọc bài `/movies/the-whisper-man-...-860508` bình
thường ⇒ site là app kiểu Next.js, **không có trang tìm kiếm** ⇒ tìm bằng search là đường cụt, đừng sửa
cách search. Thay vào đó: `Build v28-xdmovies-direct-id` (a) dựng URL thẳng từ TMDB id qua 12 dạng
(`{movies|movie|film}/{series|tv|show}` × `x-<id>`, `-<id>`, `<id>`, `<slug>-<id>`) — em chỉ biết chắc
phần đuôi `-<id>`; (b) in một dòng chẩn đoán TRƯỚC mọi thứ: `GET <site>/` len=...
* `len=0` ở trang chủ ⇒ site chặn request thường ⇒ phải qua `rch` cho cả trang bài, không riêng gate
  (và khi đó module phải dùng `rch.Get`, không dùng `Http`).
* `len>0` ⇒ chỉ sai dạng URL; dòng `dựng-theo-id … len=… ✓ có /download/` sẽ nói dạng nào ăn.

## 12. 01/9 — DẠNG TÌM KIẾM THẬT (anh cung cấp, hết đoán)

```text
https://top.xdmovies.wtf/search.html?q=<truy vấn, urlencoded>
```

Và ghi chú ngay trong UI của họ: *"Can't find the exact title? Try searching with the TMDB ID from
themoviedb.org."* ⇒ `q=<TMDB id>` là đường đáng tin nhất. `v29-xdmovies-search-html`:
* `queries` được **chèn `tmdbId` lên đầu**, tên phim (kèm năm với phim lẻ) chỉ là fallback;
* series **không** nhét "season N" vào query nữa — một bài chứa cả series (bài Reacher `-108978` có đủ
  các mùa), thêm chữ season chỉ làm trượt kết quả. Mùa lọc ở bước `IdOf`/`ParseEpisode`;
* form tìm duy nhất là `search.html?q=` (bỏ `?s=` và `/search/` — hai cái đó là em áp WordPress của
  uhd vào site Next.js, mất 2 vòng vô ích).

Bài học cho mọi nguồn mới: **hỏi người dùng dạng URL tìm kiếm trước khi đoán**, vì log "trang tìm rỗng"
không phân biệt được 404-sai-đường với 403-bị-chặn (vì vậy mới có dòng `chẩn đoán … trang chủ len=`).

## 13. 01/9 — hai cú sửa vì em đoán (anh phải chỉnh cả hai)

1. **`search.html?q=` trả về DANH SÁCH KẾT QUẢ, không phải trang phim.** Phải lấy link bài đầu tiên
   trong danh sách rồi mới đọc bài. Vòng trước em không hiểu nên sinh ra trò "dựng 12 biến thể URL"
   (`/movies/x-<id>`, `/movie/<id>`, …) — sai hoàn toàn, mất 12 request cho một lần mở phim. Đã bỏ.
   `q=<TMDB id>` vẫn là query tốt nhất vì chính site khuyên thế, nhưng nó chỉ là **bước 1**.
2. **`chẩn đoán: trang chủ len=0` không có nghĩa là "mạng hỏng"**: `GetPage` của HubController in câu
   `bị Cloudflare chặn (js challenge)` với prefix **`MoviesHub:`**, không phải `xdmovies:` ⇒ ai lọc log
   theo `xdmovies:` thì KHÔNG BAO GIỜ thấy nó. Bài học: prefix log của helper chung phải được nhắc tới
   khi bảo người dùng grep. Site này đứng sau Cloudflare ở MỌI trang.

Hệ quả (v30-xdmovies-rch-pages): thêm `Read(url)` = thử `GetPage` 1 lần (rẻ) → nếu rỗng thì
`rch.Headers(url)` rồi dùng `body`, và chỉ báo lỗi khi cả hai đều trắng. Trang tìm kiếm, trang bài và
trang gate đều đi qua `Read`, nên khi Cloudflare chặn thì client Lampa (apk, đã nối `/nws`) lãnh đủ cả
ba — không có chế độ "đọc bài bằng HTTP, bấm gate bằng rch" nửa vời.

## 14. 01/9 — `rch len=0 cur=` nghĩa là gì (em gộp lỗi, anh thành nạn nhân của cái log)

Máy báo mọi URL (trang chủ, `search.html?q=860508`, `…?q=The Whisper Man 2026`) đều
`HTTP thường rỗng -> rch len=0 cur=` rồi em in "vẫn là trang challenge". **Sai**: `len=0` **và**
`cur=` rỗng nghĩa là chưa có bất kỳ HTML nào — `rch.Headers()` trả `default` mà không tải trang.
Hai cửa duy nhất gây ra thế (RchClient.cs):
* `:235` `if (484 > InfoConnected()?.apkVersion) return default;` ⇒ app Lampa cũ hơn 484 là rch câm
  hoàn toàn (đây là giả thuyết số 1 của em cho log của anh, vì `enable=true` — nếu không bật thì đã
  in câu khác);
* `SendHub` `:387-394` trả `null` khi `Nws == null` hoặc không chọn được client ⇒ client "có đăng ký"
  trong `clients` nhưng không nhận lệnh.
v31-xdmovies-rch-diag tách ba nhánh (rch tắt / apk<484 / client không tải / đúng-là-challenge), in
`apk=` và `connectionMsg=`, và **fail-fast**: chết là nghỉ ngay, không gọi 4 lần cho một request (log
trước lặp 4 dòng cùng một lý do). Đọc log giờ chỉ cần một dòng.

Nếu đúng là apk < 484 thì đường scraping chết hẳn, chỉ còn đường "Lampa tự mở trang": module nhả nút
dẫn thẳng `search.html?q=<TMDB id>` (webview) để anh bấm tay — quyết định đó để anh, em không tự làm.

## 15. 01/9 — TẠM DỪNG: trang của site nằm sau Cloudflare *tương tác*, không phải chỉ trang gate

Kết luận của anh sau vòng 31: *"Tìm đúng bài rồi nhưng không mở được page, ai biết tại sao đâu, chắc
cloudflare nó bắt tích vào thủ công rồi, chịu thôi."*

Ghi lại cho đúng, để đời sau không mở lại bằng cách đoán tiếp:

* **Đã chắc**: dạng tìm kiếm là `https://top.xdmovies.wtf/search.html?q=<urlencoded>` và nó trả về
  DANH SÁCH KẾT QUẢ — phải lấy link bài đầu tiên (ưu tiên bài có `-<TMDB id>` khớp). Slug luôn kết thúc
  bằng TMDB id. Mỗi chất lượng một link `link.xdmovies.wtf/download/<token>`; tên file (x mediainfo) nằm
  ngay trên link; `fls` thơm, `pixel` luôn sống là server sau gate.
* **Đã chắc**: `top.xdmovies.wtf` chặn request thường ở MỌI trang (kể cả trang chủ — `chẩn đoán …
  trang chủ len=0`), không riêng trang phim. Nên đây không phải bài toán "bypass một trang đếm ngược"
  như uhd, mà là bài toán "mọi request phải đi qua trình duyệt thật".
* **CHƯA chắc (điểm mù cuối cùng)**: log vòng 31 không có dòng `apkVersion=` nào ⇒ ta chưa biết
  `rch.Headers` có được gọi thật hay không. Trước khi bỏ hẳn, chỉ cần chạy lại và lấy:

      lampac logs | grep -aE "xdmovies:|MoviesHub:|rhub" | tail -30

  - `rch TỪ CHỐI vì client: apkVersion=NNN < 484` → chỉ là app Lampa cũ; cập nhật là chơi tiếp.
  - `rch không trả gì (connectionMsg=...)` → client nhận lệnh nhưng không tải.
  - `rch tải xong nhưng vẫn là trang challenge` → đúng như anh đoán: Turnstile bắt tích bằng tay.
* **Phương án còn để ngỏ (anh chưa chọn)**: bỏ scrape, XdMovies chỉ nhả MỘT nút "Tìm trên XDMovies" trỏ
  `search.html?q=<TMDB id>` cho Lampa mở webview — anh bấm tay, không có danh sách chất lượng trong
  Lampa. Ghi ở đây để nếu cần thì làm trong ~30 dòng, không phải điều tra lại từ đầu.

Code vẫn nằm trong tree (`XdmoviesController.cs`, build từ `v31-xdmovies-rch-diag`): nguồn hiện trong
danh sách nhưng luôn trả collection rỗng. Muốn ẩn thì `"XdMovies": {"enable": false}` trong `init.conf`
hoặc tắt trong Admin Panel — không cần sửa code.

## 16. 01/9 — XÓA DỰ ÁN (anh quyết), kiến thức giữ lại ở file này

*"Thôi không quan trọng tui đã có đủ nguồn tui cần"* ⇒ không mở XdMovies nữa. Đã xoá sạch khỏi build:
`XdmoviesController.cs` bị gạch khỏi repo, `manifest.json` → `tree` còn 4 file, `ModInit.cs` bỏ field
`xd` / `Allow(xd)` / section `"XdMovies"` (kể cả `xd.rhub = true`), `Build` → `v32-xdmovies-rch-diag`
thành `v32-xdmovies-removed`. **Không xoá §1–§15**: ba dữ kiệnđáng giá cho mọi nguồn phim kiểu này là
(1) `search.html?q=` trả danh sách kết quả chứ không phải trang phim, (2) slug luôn kết thúc bằng TMDB id
nên `q=<TMDB id>` là đường tìm đáng tin nhất, (3) Cloudflare của họ chặn ở MỌI trang (trang chủ cũng
`len=0`) nên hoặc mọi trang đi qua `rch`, hoặc không đi được — đừng quay lại giữa đường.
Máy ai còn dính file cũ: `rm -f /root/lampac/module/OnlineENG/MoviesHub/XdmoviesController.cs` (và trong
`/root/lampac/mods/...` nếu có overlay), hoặc `bash setup-termux.sh --sync` — khối dọn orphan trong
`sync_latest_modules()` tự xoá file không còn trong tree.
