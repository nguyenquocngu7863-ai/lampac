# Vlxx (vlxx.phd, motchill huid)

Module Adult route `vlxx`, displayindex 16.

## URLs
- List home: `https://vlxx.phd/` (card `a[title][href=/video/]`, title truoc href)
- Category: `https://vlxx.phd/{slug}/?page=N` dang path: `/{slug}/{N}/`
- Search: `https://vlxx.phd/search/{slug}/` (+ `{N}/` khi pg>1)
- Home pg>1: `https://vlxx.phd/new/{N}/` (trang chu bo qua ?page, `/page/2/` 404)
- Video: `https://vlxx.phd/video/{slug}/{id}/` (slug truoc, id sau)
- Poster: `https://vlxx.phd/img/{id}.jpg` (absolute, dung truc tiep tu id)

## Player (3 chang)
1. Trang video: `div#video[data-id][data-sv]` + server `onclick="server(N,id)"`.
2. `POST /ajax.php {vlxx_server:1|2, id, server}` (kem X-Requested-With)
   -> JSON `{player: "<iframe ...>", ...}`.
3. Iframe `https://play.vlstream.net/embed/{hash}/s{N}` -> `window.__SRC`
   `[{label, type:hls, file}]` -> file `https://rrN---sn-*.qooglevideo.com/manifest-s1/{id}.vl`
   (m3u8 that, da verify 206 application/x-mpegurl).

## Cache & Build
- List cache 10p, resolve cache 20p (hybridCache).
- Copy `.cs` vao `/root/lampac/module/Adult/Vlxx/` + restart, khong can `dotnet build`.
- Verify: `/vlxx?pg=1` co list + poster, `vlxx/vidosik?uri=` ra link HLS,
  `vlxx/video.m3u8` 302 ve `/proxy/`.
