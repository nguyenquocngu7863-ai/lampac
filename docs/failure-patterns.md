# Failure patterns — triệu chứng → nguyên nhân → cách sửa

Append-only. Mỗi lần debug xong một lỗi **thật** thì thêm **một dòng** ở bảng dưới:
triệu chứng quan sát được trước (cái nhìn thấy từ ngoài), rồi nguyên nhân, rồi cách sửa.
Không ghi suy đoán chưa verify. Không sửa dòng cũ — nếu pattern đổi thì thêm dòng mới.

Dùng khi: gặp triệu chứng quen, grep bảng này trước khi mò lại từ đầu.

## Bảng

| triệu chứng | nguyên nhân | cách sửa | đo ở |
|---|---|---|---|
| Home/list **0 item**, route trả 503 | `_itemRe` đòi host hardcode (`javmoi\.`) nhưng site trả link tuyệt đối sang host khác (`javchat.blog`, `javsubnet.com`) | regex chỉ khớp path (`/phim/`), lấy host từ `href`; thêm `SafeDetailUrl()` chặn SSRF | JavMoi, JavSub (2026-11-23) |
| Chip/menu bấm ra **503**, URL dạng `host/https://site/...` | `Uri()` ghép `host + "/" + c` trong khi `c` đã là URL tuyệt đối | `Uri()` trả nguyên `c` khi `c.StartsWith("http")` | TangThon, TopGai (2026-11-25) |
| List lọt item rác ("Chat Telegram", banner) | filter card không đòi thuộc tính `title=` | bắt buộc thẻ `<a>` có `title=` | Viet69Av (2026-11-25) |
| Domain chết **TLS với curl_cffi/.NET** nhưng Chrome thật vẫn mở | site chặn TLS fingerprint, không chặn nội dung | đổi sang mirror cùng DB (kiểm md5 trang detail trùng) hoặc ép Chrome (Playwright) | Viet69Av → `clipviet69s.net` (2026-11-25) |
| Domain chết **cả trong Chrome thật** (TLS reset/timeout) | site bỏ host | tìm mirror, verify trùng `detail md5` rồi đổi `SiteHost`/`ModInit`/`manifest` + bump cache key | TangThon → `finquesrossello.com`, SexDep → `sexngot.blog` |
| Sửa code xong mà SISI vẫn trả **kết quả cũ** | `HybridFileCache` ở `~/lampac-run/cache/fdb` sống qua restart | xoá `~/lampac-run/cache/fdb/*/*` + restart, hoặc bump cache key | nhiều module |
| `play.sh` **FAIL** nhưng stream thật vẫn phát | URL `/proxy/<hash>` không có đuôi file → tool không đoán được container | `ffprobe` tay 2 MB đầu (Range) rồi mới kết luận | FreePornVideos, XNhau (2026-11-22) |
| HTTP 200 nhưng Lampa **đen màn** / `manifestLoadError` | `vidosik` trả URL không đuôi (`.m3u8`/`.mp4`) → Lampa không chọn hls.js | trả đúng phần mở rộng theo loại stream | JavSub (2026-11-23) |
| Trang detail OK nhưng `?rel=` trả **503** | `GetPageAsync` đòi body ≥ 3000 B + retry; endpoint JSON nhỏ | thêm `FetchJsonAsync` cho endpoint nhỏ | JavSub (2026-11-23) |
| Menu "Sắp xếp" có mục nhưng **thứ tự không đổi** | site không có `?sort=` (dùng path `/most-view`, `/top-rate`) hoặc param sai tên | sort theo path/tab; nếu site không có sort thì **bỏ hẳn dòng 2** | KawaiJAV (2026-11-23) |
| Search trả **nguyên trang chủ** | form dùng `name="keyword"` nhưng code gửi `?q=` | đọc đúng tên param trong HTML form | KawaiJAV (2026-11-23) |
| "Liên quan" rỗng dù mục đó có trên trang | khối render bằng Alpine/JS, không nằm trong HTML | gọi endpoint JSON (`/ajax/suggestions/<slug>`) và parse JSON | JavSub, TopGai (2026-11-23/25) |
| Trang trả **"Just a moment"** / challenge | Cloudflare chặn curl_cffi | mở `~/lampac-native/x.sh <url> <giây>` (Chrome headful trên X) rồi mới kết luận; nếu chặn toàn site → pause module | SupJav (PAUSED) |
| Player chỉ có embed **Blogger** | iframe `video.g` cần token | `batchexecute` `WcwnYd` → `googlevideo` itag 22 (`video.mp4`, 206) | SexDep (2026-11-25) |
| Popup long-press **rỗng** dù todo ghi "đã verify popup" | item home trả uri tuyệt đối trỏ host mirror khác `SiteHost`; `SafeDetailUrl`/guard host từ chối → `/genres` trả 200 nhưng 0 chip | normalize host (lấy path) trước khi fetch detail; phát hiện bằng `popup.sh` (nó thử lại path-only và báo đúng câu này) | JavSub (`javsubnet.net`), TangThon (`pumpcotradindo.com`) — đo 2026-11-26 |
| Menu/popup lẫn chữ Trung/Nga/Thái | lấy nguyên taxonomy site | bảng `Trans` + `Translate()` cho cả menu lẫn popup, kể cả fallback tĩnh | LUẬT CỨNG trong AGENTS.md |

## Ghi chú

- Triệu chứng phải là thứ **đo được** (status, số item, md5 sequence, ffprobe), không phải
  "có vẻ lỗi".
- Trước khi thêm dòng: xác nhận nguyên nhân bằng một phép đo tách bạch (offline trên file đã
  capture, hoặc `localhost`), không suy từ việc "sửa xong thì hết".
- Nếu một dòng ở đây **sai** với code hiện tại thì thêm dòng mới ghi rõ đã bị thay thế, đừng xoá.
