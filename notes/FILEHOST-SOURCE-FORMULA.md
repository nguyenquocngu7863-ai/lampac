# Công thức nguồn file-host 2 tầng (họ Movies4U / MoviesDrive)

Viết ngày 2026-09-01, sau khi bản `v20-seasons-from-buttons` chạy đúng trên thiết bị (Reacher
Season 1–4: đủ 4 mùa, đủ 5 nhóm mỗi mùa, đủ tập của nhóm đã chọn). Áp dụng cho **mọi** nguồn cùng
kiểu: site dạng bài viết (WordPress/BBCode) đặt link thành **nút**, còn link thật nằm ở một
**trang trung gian** riêng, mỗi tập một dòng.

Module tham chiếu: `Modules/OnlineENG/MoviesHub/` (`Movies4UController.cs` là bản chuẩn nhất;
`MoviesDriveController.cs` cùng họ nhưng một tầng).

---

## 0. Đọc trang THẬT trước khi viết một dòng code

Ba vòng lỗi liên tiếp (nhãn sai → mùa trùng danh sách → mất sạch dòng lọc) đều có cùng một gốc:
đoán DOM từ ảnh chụp. Ảnh chụp chỉ cho biết **có** nút, không cho biết nút nằm trong thẻ nào và
heading ở trước hay ở sau nó.

Việc bắt buộc, theo thứ tự:

1. Mở **bài viết** mẫu (đúng bài người dùng gửi, không phải bài em tự chọn) → ghi lại nguyên văn
   khối `Download Links`: heading, text của nút, `href` của nút.
2. Mở **một trang nhóm** (chính cái `href` ở bước 1) → ghi lại cách site ngăn cách các tập.
3. Ghi cả hai vào `notes/<tên-nguồn>.md` **trước** khi sửa controller. Sau này site đổi markup,
   người sửa đọc note đó là đủ, không phải dò lại từ đầu.

> Trong sandbox của agent: `curl` không ra kết quả (không có egress), nhưng `fetch_page` thì vào
> được cả `movies4u.clinic` lẫn `m4ulinks.site`. Ảnh chụp của người dùng chỉ là **bằng chứng phụ**.

---

## 1. Tầng 1 — bài viết: hợp đồng là CHỮ TRÊN NÚT + HEADING, class chỉ là tối ưu

Cấu trúc thật của Movies4U (đã kiểm ngày 1/9):

```html
<h4>Season 4 [Hindi ORG. + English] 480p [250MB/E]</h4>
<a href="https://m4ulinks.site/number/62782">🚀 Download Links 🚀</a>
<a href="https://m4ulinks.site/number/36737">🚀 BATCH/ZIP [1.5GB] 🚀</a>   <!-- nút KHÁC, cùng dòng -->
```

Ba luật rút ra (đều đã trả giá bằng bug):

- **Một nhóm = một anchor có chữ khớp `download\s*-?\s*links?`**. Quét toàn bài bằng
  `AnchorPattern`, không bọc trong `div.download-links-div`: site đổi/ bỏ class là nguồn chết
  (log thiết bị cho thấy class này *không* tồn tại ở tầng nhóm và không đáng tin ở tầng bài).
- **Nhãn của nhóm = heading gần nhất TRƯỚC anchor** (`NearestLabelBefore(html, m.Index)`). Ở
  Movies4U heading đó đã chứa sẵn "Season N" + chất lượng + size ⇒ vừa là nhãn chip, vừa là khoá
  lọc mùa, vừa là thứ người dùng cần để phân biệt 5 nhóm của cùng một mùa.
- **BATCH/ZIP loại bằng chữ trên nút, TRƯỚC khi dedupe theo url.** Trên bài Reacher, mọi nút
  BATCH/ZIP của cả 4 mùa trỏ **cùng một id** (`36737`): nếu dedupe url trước thì một nút thật bị ăn
     mất (coi là trùng url); nếu kiểm BATCH trên *heading* thì không ăn (nút nằm ở dòng khác heading) và menu rỗng.

Lọc mùa: nếu **có** nhãn tự nhắc mùa ⇒ **bắt buộc** khớp mùa (`require = true`); nếu không nhãn nào
nhắc mùa (bài một mùa) ⇒ nhận hết. Nhờ đó "cả 4 mùa dùng chung một danh sách" không quay lại được.

## 2. Tầng 2 — trang nhóm: bucket theo heading, đừng tin class nào

```
##### -:Episodes: 1:-
[🚀 Hub-Cloud [DD]](https://hubcloud.cx/drive/kk1lk7kvdmvim8m) [🚀 GDFlix](https://gdflix.dev/file/oar2tdq6PtMjKZr)
##### -:Episodes: 2:-
...
```

- **Một heading = một tập**; mọi anchor file-host từ heading đó đến heading kế tiếp là các **host
  của cùng tập** ⇒ vào `streamquality` của tập (biến thể được phép ở TẬP; cái bị cấm là nhét link
  chất lượng phim lẻ vào menu chất lượng).
- `EpisodeNumber` phải khớp được **cả** `-:Episodes: 1:-` (mẫu `e(?:pi)?sod(?:e|es)?[^0-9]{0,4}(?<n>\d{1,3})`),
  `Ep 7`, `S04E05` và số đứng cuối. Mẫu kiểu `ep(?:episode)?\.?\s*(\d+)` **không** khớp "Episodes:".
- Nhặt số bằng **named group** `m.Groups["n"]`, không bằng chỉ mục — thêm mẫu là chỉ mục lệch, mọi
  tập thành tập 0 mà không lỗi.
- Không có heading tập nào (phim lẻ / trang gộp) ⇒ **một** entry `season,0`, không bịa "mỗi anchor
  một tập" (bản fallback cũ tạo 5 "tập" giả từ 5 host ⇒ người dùng thấy 5 tập ma).

## 3. Host: allow-list + deny-list, không hardcode domain

- `LooksLikeFileHost` = những host em **giải được** (hubcloud `…/drive/<id>`, Google Drive
  `drive.usercontent.google.com`, …). Host lạ → bỏ, nhưng **in ra** `hosts=` để vòng sau biết.
- `DeadHost` = những host đã chết thật (gdflix/…): chặn ở **cổ chai cuối** `Push()` (mọi đường
  đi qua) chứ không chỉ chỗ thêm link.
- TLD của cả site nguồn lẫn host file **luân phiên** (`new5.movies4u.clinic`, `hubcloud.cx/.foo`):
  không bao giờ hardcode host trong code; `host` để trong `init.conf` section của nguồn.

## 4. Đường phát: link trần, một lần 302

- Mỗi link file-host là **một nút nguồn** (`EpisodeTpl`/`ContentTpl`), **không** vào
  `StreamQualityTpl` — người dùng cần bấm thẳng để GStreamer bắt được đuôi `.mkv`.
- Nút phát: `method:"play"` + `accsArgs("{host}/lite/{plugin}/{RouteFor(label,url)}?src=…&label=…&play=true")`;
  route **302 verbatim** sang link extractor đã giải. Không `/proxy`, không JSON, không re-encode,
  không HLS hop (luật 15: "đưa qua /lite chi nữa… để nguyên cái link").
- `streamproxy = false` trong `ModInit.Section()`; bật lên là path mất `.mkv` ⇒ GStreamer tắt.

## 5. Ánh xạ UI Lampa (series nhiều nhóm)

| Khái niệm site | Chỗ trong UI | Cách nối |
|---|---|---|
| Mùa | `Mùa` (`SeasonTpl`) | `HubEntry("Mùa N", url, N, 0)` từ `CollectSeasons` |
| Nhóm release (chất lượng × audio) | **Bộ lọc → thuyết minh** (`VoiceTpl`) | chip = nhãn ngắn `GroupShort` (đã bỏ "Season N" thừa, còn `480p [250MB/E]`), link chứa `&g=<i+1>` |
| Host phụ của một tập | ngay trong danh sách tập | `streamquality` + `streamlink` trên `EpisodeTpl` |
| Nguồn đang chọn | cache key | `CollectCached` phải có `ReleaseGroup`, không thì đổi nhóm không có tác dụng |

Một nhóm đã chọn ⇒ phải attach **toàn bộ** tập của nhóm đó (không lấy `Links[0]` của khối mùa rồi
dừng — đó là bug "mỗi tập đúng 1 link").

## 6. Config & module layout (một assembly, mỗi nguồn một section)

- Một module compile riêng từng thư mục ⇒ muốn **dùng chung** resolver thì các nguồn phải ở cùng
  thư mục (đó là lý do MoviesDrive + Movies4U nằm trong `MoviesHub`), nhưng mỗi nguồn vẫn có
  `init.conf` section riêng (`"Movies4U"`, `"MoviesDrive"`): host/`enable`/`displayindex` độc lập.
- `ModInit`: thêm `Section("<Tên>", "<host mặc định>", <displayindex>)`, thêm `ModuleOnlineItem`
  (route `"<tên-hoàn-toa-thấp>"`) và thêm `balanser` đó vào `OnlineApiQuality`.
- `manifest.json`: `"dynamic": true` + `"tree"` liệt kê **đủ** file `.cs`. `tree` là danh sách để
  `setup-termux.sh` biết phải kéo file nào (cả `--sync` lẫn `--install/--sync-all`), nên **thêm
  nguồn mới chỉ cần sửa `manifest.json`**, không sửa script.

## 7. Log tự chẩn đoán (mỗi bước một dòng, đếm được)

Nguồn mới phải in ít nhất: `mùa: N [...] đúc từ X nút download` · `bỏ N nút BATCH/ZIP` ·
`mùa S nhãn nhóm: [...]` · `mùa S nhóm i/M '<heading>': K heading tập, L link` ·
`0 nhóm | a=… classes=… hosts=…`. Nguyên tắc: **mọi nhánh "0 kết quả" phải tự giải thích bằng
dữ liệu thô** (`a=` số anchor, `classes=` histogram, `hosts=` histogram) để một lần chạy log là
kết luận được, không phải hỏi người dùng chụp màn hình.

Marker build: đổi `Build` trong `HubController.cs` **mỗi** commit sửa module, và nhắc marker đó
trong message commit + trong lệnh kéo file.

## 8. Luật biên dịch (toàn bộ từng làm hỏng build trên thiết bị)

`Modules/OnlineENG/MoviesHub/README.md` — "Luật viết code của vùng này" là bản đầy đủ; tóm lại:
không `var x = [..]` (CS9176), BCL phải có lớp đứng trước (`Uri.TryCreate`, CS0103 làm **Lampac
không boot**), không `MatchCollection[^1]`, `short`+`Math.Max` phải cast, raw string khi patch
bằng python rồi quét control char.

## 9. Quy trình giao hàng cho thiết bị (máy anh ấy là compiler duy nhất)

```bash
proot-distro login ubuntu -- bash -lc '
set -e
d=/root/lampac/module/OnlineENG/MoviesHub; sha=<SHA-ĐẦY-ĐỦ>
for f in HubController.cs Movies4UController.cs; do
  curl -fsSL --retry 3 "https://github.com/nguyenquocngu7863-ai/lampac/raw/$sha/Modules/OnlineENG/MoviesHub/$f" -o /tmp/$f
done
grep -c "<marker-trong-message-commit>" /tmp/HubController.cs
mv /tmp/HubController.cs /tmp/Movies4UController.cs "$d/"
lampac stop; rm -rf "$d/obj" "$d/bin" "$d"/*.dll; lampac start'
```

Lampac **không có** `restart`: chỉ `stop` rồi `start`. Sau khi push, đối chiếu `md5sum` blob trên
remote với file trên đĩa (SHA-pinned, không tin `main`, không tin `raw.githubusercontent.com` còn
cache).

## 10. Checklist khi nhận thêm nguồn cùng kiểu

Anh gửi cho em: **URL bài mẫu** (đúng bài có link) + **URL một trang nhóm** + tên muốn hiển thị.
Còn lại là:

- [ ] fetch 2 tầng, ghi markup thật vào `notes/<nguồn>.md`
- [ ] `cp Movies4UController.cs <Tên>Controller.cs`, sửa 4 chỗ: `Search`/`IdLookup`, `CollectMovie`,
      `CollectSeasons`+`AllGroups`, `CollectEpisodes`
- [ ] selector theo hợp đồng tầng 1/tầng 2 (nút + heading), **không** lấy class làm điều kiện sống
- [ ] cho `href` của nút vào `IsGate`/`LooksLikeFileHost` phù hợp; host lạ thêm vào `DeadHost` nếu chắc chết
- [ ] `ModInit.cs`: `Section("<Tên>", …)`, `ModuleOnlineItem`, `OnlineApiQuality`
- [ ] `manifest.json` → `tree` thêm file (đây là chỗ DUY NHẤT phải sửa để `setup-termux.sh` biết)
- [ ] `Build` mới + log đếm ở mọi nhánh; chạy scanner nội bộ ở mục 8
- [ ] push, đối chiếu md5, gửi lệnh SHA-pinned

## 11. Khi nào nên DỪNG — và điều vòng này thực sự chứng minh được

**Nguồn chết thật ≠ selector sai.** Phép thử hai stack, làm trước khi bỏ thêm buổi nào vào một nguồn:

| Quan sát | Kết luận |
|---|---|
| Lampac (C#) fail **và** plugin CloudStream (Kotlin) cũng fail trên cùng trang | site chặn theo **chính sách** (embed-only, token gắn session) → **dừng**, không có bug để sửa |
| Lampac fail, CSX ăn được | mình còn thiếu bước: mở `Extractors.kt` của CSX dịch đúng chuỗi request (đã áp dụng cho HubCloud/GDrive — mục 3–4) |
| Cả hai ăn, nhưng link chết sau vài phút | token ngắn hạn ⇒ resolve lúc bấm play, không cache link |

VidLink đóng ngày 2026-09-01 đúng bằng dòng đầu tiên của bảng: hai stack độc lập cùng fail ⇒
`vidlink.pro` chỉ cho embed. Cái duy nhất còn lại là **trình duyệt thật + nghe request của player**
(kiểu Mirage/Playwright), không còn là công thức này nữa — và module đã đổi mặc định về tắt để
không ai phải trả `httptimeout` cho một nguồn chết.

**Điều vòng Movies4U chứng minh — và chỉ đúng điều này:** lớp nguồn "bài viết đặt nút Download +
trang trung gian + file-host" (HubCloud, Google Drive, các site họ Movies4U/MoviesDrive) làm **trọn
vẹn bằng C#/Lampac trên Termux**: parse hai tầng, dựng link, 302 trần cho player, GStreamer tự bật
theo đuôi `.mkv`. Không cần viết plugin Kotlin nào.

Kotlin/CloudStream vẫn hơn đúng ba chuyện:

1. **DRM/Widevine** — Lampac trên Termux không có đường decrypt.
2. **JS nặng** (token tính bằng VM, PoW/anti-bot): C# phải mượn Playwright/Chromium, chậm và giòn
   hơn một JS engine trong app.
3. **68 provider có sẵn** của CSX: ăn nhanh nếu chỉ dùng, không tiết kiệm nếu phải tự viết.

Ngược lại, phần mà CSX không có và đây là lý do nên ở lại Lampac: section config sửa được trên thiết
bị (host/timeout/bật-tắt mỗi nguồn độc lập), log tự chẩn đoán in ra `a=`/`classes=`/`hosts=` khi
không lấy được gì, và site đổi markup thì chỉ cần kéo lại file `.cs` + `lampac stop && lampac start`
— không build APK, không ký, không cài lại.
