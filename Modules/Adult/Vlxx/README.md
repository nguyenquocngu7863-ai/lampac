# Vlxx (vlxx.phd, motchill huid)

Module Adult route `vlxx`, displayindex 16.

## URLs
- List home: `https://vlxx.phd/` (card `a[title][href=/video/]`, title truoc href)
- Category: `https://vlxx.phd/{slug}/?page=N` dang path: `/{slug}/{N}/`
- Search: `https://vlxx.phd/search/{slug}/` (+ `{N}/` khi pg>1)
- Home pg>1: `https://vlxx.phd/new/{N}/` (trang chu bo qua ?page, `/page/2/` 404)
- Video: `https://vlxx.phd/video/{slug}/{id}/` (slug truoc, id sau)
- Poster: `https://vlxx.phd/img/{id}.jpg` (absolute, dung truc tiep tu id)

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
- **`vlxx.phd` trả 301 sang `vlxx.net`** và TLS `.phd` hay bị cắt → `FetchHtmlAsync` và
  `ResolveLinksAsync` đều **đổi host sang `.net` trước** (fallback về `.phd`), `POST /ajax.php`
  gọi thẳng `https://vlxx.net/ajax.php` + `Origin/Referer: https://vlxx.net`.
  (Trước 2026-11-24 `vidosik` trả `stream_links` liên tục vì post vào `.phd`.)

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
