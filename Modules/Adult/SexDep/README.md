# SexDep (sexngot.blog, theme vlxx)

Module Adult route `sexdep`, displayindex 16.

## Domain
- 2026-11-25: `z.sexdep.co.uk` chết TLS server-side (m3u8 + ảnh đều fail) → **`https://sexngot.blog`**.
- Site còn mirror `s.sexdep.co.uk` (cùng backend). Vì vậy:
  - `SiteHost` = `sexngot.blog`;
  - **poster** lấy theo host của chính card (`Playlist` đọc origin từ href) — mirror nào cũng ra URL sống;
  - **stream** lấy theo host của trang detail (`StreamLinks(html, origin)`).
- `DetailUrl()` (genres/rel) nhận `*.sexngot.blog` + `*.sexdep.co.uk`, chỉ path `/phim/`.

## URLs
- List home: `https://sexngot.blog/` (parse `a.m-block.movie-item[href=/phim/][title]`; home chỉ lấy section `header-list-index` đầu)
- Category: `/{the-loai|quoc-gia|tu-khoa}/<slug>/?page=N` (bắt buộc `/` cuối)
- Search: `/?search=<kw>&page=N`
- Video: `/phim/<slug>`

## Popup (long-press)
- `GET /sexdep/genres?uri=<uri detail>` — **1 lần fetch detail**, cache `sexdep:info:*` 360 phút:
  - `Quốc gia`: BreadcrumbList JSON-LD (`/quoc-gia/<slug>`) — trang detail không in khối quốc gia nào khác;
  - `Thể loại` / `Thẻ`: các block `div#extras` nhãn `Thể loại:` (`the-loai`), `Từ khóa:` (`tu-khoa`), `Diễn viên:` (`dien-vien`, hiện luôn rỗng → nhóm bị bỏ);
  - `Liên quan` (`plain: true`) trỏ `?rel=<uri detail>`.
- `GET /sexdep?rel=<uri detail>` — cắt khối `ul#more-movie` sau heading “Có thể bạn thích?” (10 card), bỏ chính phim đang xem, `total_pages: 1`, `pg>1` → list rỗng.
- SSRF: uri host lạ / không phải `/phim/` → `{items:[],groups:[]}` (200), không 500.

## Player
- Trang detail có `<a class="server" data-link data-type>`:
  - `/storage/m3u8/<slug>/index.m3u8` → `HLS` (route `sexdep/video.m3u8`);
  - `https://www.blogger.com/video.g?token=…` → `Blogger` (route `sexdep/video.mp4`).
- **Blogger**: lấy `token` từ `data-link`, POST `blogger.com/_/BloggerVideoPlayerUi/data/batchexecute`
  (`rpcids=WcwnYd`, body `f.req=[[["WcwnYd","[\"<token>\",null,0]",null,"generic"]]]`) → trả danh sách
  `googlevideo.com/videoplayback` (itag **22** = 720p, **18** = 360p). Chọn 22 trước. Cache `sexdep:blogger:v2:<token>` 5p.
- **Bẫy đã đo (2026-11-25)**: URL `googlevideo` bị ràng buộc **đúng User-Agent của lần gọi batchexecute**.
  Proxy Lampac luôn ghi đè `user-agent` bằng `Http.UserAgent` (Chrome 146) ⇒ khi gọi batch **không được** tự đặt UA
  (đặt Chrome 120 → URL chỉ đúng với 120 → proxy 146 nhận **403**). Bỏ trống UA ở cả 2 phía là khớp.
- URL nhúng trong HTML trang `video.g` thay vào đó chỉ qua được TLS giả lập trình duyệt (`curl_cffi` 206), .NET 403 — **không dùng**.
- Đo 2/5 phim đầu trang chủ dùng Blogger, phần còn lại HLS `/storage`.

## Pagination
- Mọi tuyến dùng `?page=N` (`&page=N` khi đã có query). Site trả `total_pages: 0` (SISI hiểu = còn trang).

## Cache & Build
- List cache 10p (key `sexdep:v3:*` — bump khi đổi domain để bỏ poster `z.` cũ), resolve 20p, popup 360p, blogger 20p.
- Copy `.cs` vào `~/lampac-run/module/Adult/SexDep/` + restart, không cần `dotnet build`.

## Verify (2026-11-25)
- `/sexdep` 12 item 0.66 s · detail host `sexngot.blog` · poster `200 image/jpeg` (16 906 B).
- `/sexdep/genres` 4 nhóm (Quốc gia 1 · Thể loại 13 · Thẻ 15 · Liên quan 1), lạnh 0.43 s → cache 0.008 s.
- `?rel=` 10 item `tp=1`, `pg=2` rỗng; SSRF (host lạ, `the-loai/` thay vì `/phim/`) → rỗng 200.
- `?c=quoc-gia/viet-nam|the-loai/clip-sex|tu-khoa/clip-sex-telegram` → 24 item.
- Stream HLS: route → 302 `/proxy/…m3u8` (9 929 B) → segment `200 video/mp2t` 6 441 648 B, `ffprobe` h264 720×1280 + aac
  · `play.sh` **PASS** (41/41 segment, h264 720×1280 + aac, 810 s).
- Stream Blogger: `sexdep/video.mp4?q=Blogger` → 302 `/proxy/…` → `206 video/mp4` 11 644 349 B,
  `play.sh` **PASS** (`decode 5s đầu ✔ · 5s cuối ✔`, h264 406×720 + aac, 87 s); phim Blogger thứ 2: `206` h264 540×720.
