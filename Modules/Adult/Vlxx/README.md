# Vlxx (vlxx.net — 2026-11-27: vlxx.phd chet, connect refused)

Module Adult route `vlxx`, displayindex 16.

## URLs
- List home: `https://vlxx.net/` (card `a[title][href=/video/]`, title truoc href)
- Category: `https://vlxx.net/{slug}/?page=N` dang path: `/{slug}/{N}/`
- Search: `https://vlxx.net/search/{slug}/` (+ `{N}/` khi pg>1)
- Home pg>1: `https://vlxx.net/new/{N}/` (trang chu bo qua ?page, `/page/2/` 404)
- Video: `https://vlxx.net/video/{slug}/{id}/` (slug truoc, id sau)
- Poster: `https://vlxx.net/img/{id}.jpg` (absolute, dung truc tiep tu id)

## Player (3 chặng)
1. Trang video: `div#video[data-id][data-sv]` + server `onclick="server(N,id)"`.
2. `POST /ajax.php {vlxx_server:1|2, id, server}` (kem X-Requested-With)
   -> JSON `{player: "<iframe ...>", ...}`.
3. Iframe `https://play.vlstream.net/embed/{hash}/s{N}` -> `window.__SRC`
   `[{label, type:hls, file}]` -> file `https://rrN---sn-*.qooglevideo.com/manifest-s1/{id}.vl`
   (m3u8 that, da verify 206 application/x-mpegurl).

## Popup (2026-11-24)

| route | ghi chú |
|---|---|
| `/vlxx/genres?uri=<detail>` | `Diễn viên` (`div.actress-tag` → `/cast/<slug>/` → `?c=cast/<slug>`, 1 chip), `Thể loại` (`div.category-tag` → `/<slug>/` → `?c=<slug>`, 5 chip), `Liên quan` (`plain: true` → `?rel=<uri>`) |
| `/vlxx?rel=<detail>` | `div#video-list` ("Phim liên quan") → 10 item, `total_pages: 1`, `pg>1` → `{tp:1,list:[]}`, uri lạ → 200 rỗng |

- Cache: `vlxx:info:{uri}` 360 phút, `vlxx:rel:{uri}` 20 phút.
- **2026-11-27: `vlxx.phd` CHẾT HẲN** (`curl: (7) Could not connect ... :443`, không còn 301) →
  đổi toàn bộ host sang **`vlxx.net`** (`SiteHost`, `SisiSettings`, `manifest.site`, referer).
  `vlxx.com` cũng trả 200 cùng nội dung nhưng canonical là `vlxx.net`.
  Code vẫn rewrite `//vlxx.phd` → `//vlxx.net` để link/bookmark cũ còn dùng được, và
  `NormalizeUrl` nhận cả 2 host.

## Đã đo (2026-11-27)

- Sau khi đổi `.net`: home 30 item · `vidosik` `{qualitys:{Auto}, recomends:10}` ·
  `play.sh` **PASS** h264 1920x1080 · 9722.7s · segment 985/985 qua Lampac · decode 5s đầu + cuối OK
- Ảnh list qua `proxyimg` 200.

## Đã đo (2026-11-24)

- home 30 item; `pg2` 30; `search=jav` 11; `c=jav` 30 (site không trả `pages` → `total_pages: 0` = cuộn vô hạn, như trước)
- `genres?uri=` → 3 nhóm (1 diễn viên / 5 thể loại / 1 liên quan)
- `?rel=` 10 item · `tp=1`; `pg=2` rỗng; `rel=https://evil.example/x` rỗng
- `vidosik` → `{"Auto": ...}`; `play.sh` → **PASS**: h264 1920x1080 · 7327s, segment 742/742,
  decode 5s đầu **và** cuối OK, cold 0.43s / warm 0.44s

## Cache & Build
- List cache 10p, resolve cache 20p (hybridCache).
- Copy `.cs` vao `/root/lampac/module/Adult/Vlxx/` + restart, khong can `dotnet build`.
- Verify: `/vlxx?pg=1` co list + poster, `vlxx/vidosik?uri=` ra link HLS,
  `vlxx/video.m3u8` 302 ve `/proxy/`.
