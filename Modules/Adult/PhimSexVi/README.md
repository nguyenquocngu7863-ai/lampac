# PhimSexVi (kidayteknoloji.com)

Module Adult route `phimsexvi`. Site SSR (PHP). **Site chạy nhiều mirror** — trang trả về link
tuyệt đối của mirror khác (`beyazotokiralama.com`), path giống hệt nhau.

## List
- Card: `article.video-card` → `a.video-thumb` (href `/video/<slug>`) + `img src` + `time.video-badge` + `h3.video-title`.
- Home: `/` (40 card). Trang 2+: `/videos/new?page=N` (20 card).
- Category: `/<path>` (`?c=category/<slug>`), `?page=N`. Search: `/search?q=<kw>&page=N`.
- Nav thể loại lấy động từ trang chủ (`href="/category/<slug>" title="..."`), cache 360 phút.
- Site không có tổng trang ⇒ `total_pages: 0`.

## Player (2026-11-24 sửa)
- Detail `/video/<slug>` có `<iframe src="https://embed.<mirror>/embed/<id>/p1?...">`.
- **Host embed đổi theo mirror**: trước đây hardcode `embed.kidayteknoloji.com` ⇒ detail lấy từ
  `beyazotokiralama.com` không match ⇒ `vidosik` báo `stream_links`. Nay `ExtractEmbed(html)`
  trả `(host, id)` bắt từ chính iframe, không phụ thuộc mirror.
- Trang embed: `m3u8_file_url = '/phim-sex/<id>/cdn.m3u8'` → `https://<embed-host>/<path>`.
- Proxy qua `phimsexvi/video.m3u8?uri=` (kiểm `#EXTINF` trước khi trả).

## Popup (long-press) — 2026-11-24

| route | nguồn |
|---|---|
| `/phimsexvi/genres?uri=<detail>` | `p.term-block` trong detail: `Thể loại:` → `a.pill[href=/category/<slug>]`, `Tags:` → `a.pill[href=/tag/<slug>]` → `?c=<category|tag>/<slug>`; `Liên quan`: 1 item `plain:true` → `?rel=<uri>` |
| `/phimsexvi?rel=<detail>` | `section[aria-labelledby="related-title"]` ("Phim liên quan") → parse lại `article.video-card`, `total_pages: 1`, `pg>1` → rỗng |

- **SSRF + mirror**: `DetailPath(uri)` chỉ lấy path và popup/rel **luôn fetch `SiteHost + path`**
  (các mirror phục vụ nội dung giống hệt) ⇒ không fetch host lạ, không phải sửa khi site đổi mirror.
  Path không phải `/video/...` ⇒ popup rỗng 200 / `{tp:1, list:[]}`.
- Cache: `phimsexvi:info:{path}` 360 phút, `phimsexvi:rel:{uri}` 180 phút.

## Đã đo (2026-11-24)

- home 40 item, menu `Tìm kiếm` / `Sắp xếp` / `Thể loại`; `pg=2` 20 item; `search=jav` 24;
  `c=category/javhd&pg=2` 24; `c=tag/mat-xinh` 24
- `genres?uri=` → **Thể loại 1** (Sex châu âu) + **Thẻ 2** (mặt xinh, úp thìa) + **Liên quan 1**
- `?rel=` **12 item** `tp=1`; `pg=2` rỗng; uri lạ → `{tp:1,list:[]}`; `genres` uri lạ → `{items:[],groups:[]}`
- `vidosik` → `{"HLS": .../phimsexvi/video.m3u8}`; `play.sh` → **PASS** (h264 1280x720 · 328.1s,
  segment 47/47 qua Lampac, decode 5s đầu **và** cuối OK)

## Build
- `~/lampac-native/csc/check.sh ~/lampac-clean/Modules/Adult/PhimSexVi` → 0 error
- copy `.cs` + `manifest.json` sang `~/lampac-run/module/Adult/PhimSexVi/` → `nrestart`
  (runtime cần cả `GlobalUsings.cs` đang có sẵn ở đó)
