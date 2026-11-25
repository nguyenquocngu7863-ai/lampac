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
  - `/storage/m3u8/<slug>/index.m3u8` → `HLS` (proxy Lampac);
  - `https://www.blogger.com/video.g?token=…` → `Blogger`: module fetch `video.g`, bóc URL `googlevideo.com/videoplayback` (mp4).
    **Đã đo**: `curl_cffi` tải được 206 `video/mp4`, nhưng **proxy của Lampac (.NET) bị Google trả 403** ⇒ phim Blogger hiện chưa phát được (ghi nhận, chưa fix).
- Nhiều phim dùng Blogger (đo 2/5 phim đầu trang chủ), phần còn lại HLS `/storage`.

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
- Stream HLS: route → 302 `/proxy/…m3u8` (9 929 B) → segment `200 video/mp2t` 6 441 648 B, `ffprobe` h264 720×1280 + aac.
