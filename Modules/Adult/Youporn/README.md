# Youporn (www.youporn.com)

Module SISI cho YouPorn — site MindGeek: HTML server-rendered cho list, player lấy
`mediaDefinitions` rồi gọi thêm 1 API để ra CDN mp4 có hash.

## Route

| route | ý nghĩa |
|---|---|
| `/youporn` | list (home `?sort=` / `?c=` category-pornstar-tag / `?search=` / `?pg=`) |
| `/youporn/vidosik?uri=/watch/<id>/` | resolve stream (2 bước, server-side) |
| `/youporn/genres?uri=...` | popup chip thể loại + thẻ + mục "Liên quan" |
| `/youporn?rel=<uri>` | list video liên quan (parse luôn trang watch) |

## Cấu trúc trang (đo offline, capture `~/lampac-native/tmp/capture/www.youporn.com/`)

```
home mặc định   /browse/time/               36 card/trang, ?page=N
sort trang chủ  /most_viewed/ · /top_rated/ · /browse/duration/
category        /category/<slug>/           (82 mục, lấy từ /categories/)
pornstar        /pornstar/<slug>/
tag             /porntags/<slug>/
search          /search/?query=<kw>&page=N
```

Card: `<article class="video-box ..." data-video-id="...">` → title ở `aria-label`,
poster `data-src`, preview `data-mediabook` (mp4 40k), thời lượng trong
`<div class="video-duration"><span>09:02</span>`.

**Sort:** chỉ trang chủ có sort thật (4 trang trên trả 3 thứ tự khác nhau — đã đo).
`/category/<slug>/views|time|rating|duration/` **là no-op** (trả đúng id như trang gốc)
và `/search/` không nhận sort → module bỏ hẳn dòng "Sắp xếp" ở category/tag/search.

**Phân trang:** site chỉ hiện 4 số + nút Next → `total_pages = max(data-page-number) + 1`,
tăng dần khi người dùng đi sâu (luôn còn trang kế, không kẹt ở 4).

## Stream (2 bước)

1. Trang `/watch/<id>/` có `"mediaDefinitions":[{"format":"hls","videoUrl":"/media/hls/?s=..."},
   {"format":"mp4","videoUrl":"/media/mp4/?s=..."}]` (token base64, không mã hoá).
2. Gọi `/media/mp4/?s=...` → JSON `[{quality:"1080", videoUrl:"https://ev-ph.ypncdn.com/...mp4?validfrom=&validto=&hash="}]`
   (4 mức 1080/720/480/240, token ~2 giờ). Fallback sang `/media/hls/` nếu không có mp4.

Module map `quality` → key `1080p/720p/480p/240p`, `streamproxy = true` nên client chỉ thấy
`/proxy/...`; cache stream 20 phút (token sống 2 giờ).

## Verify (2026-11-26)

home 36 item `tp=5` · `?pg=2` khác trang 1 · 3 sort khác thứ tự · category Big Tits 32 item
`tp=5` (không có dòng sort) · search "anal" 31 item (không có dòng sort) · thể loại 82 ·
popup 3 nhóm (Thể loại 2 · Thẻ 16 · Liên quan 1) · `?rel=` 9 item `tp=1` · `popup.sh youporn`
**PASS** · stream `1080p` qua `/proxy/` `206 video/mp4`, ffprobe `h264 1920x1080` + `aac`,
ffmpeg decode ok · `check.sh` 0 error.
