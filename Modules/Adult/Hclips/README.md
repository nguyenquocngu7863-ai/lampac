# Hclips (hclips.com)

Module SISI cho HClips — cùng backend JSON API với HClips / Upornia (mạng tubecup),
nhưng mỗi site một module để `SiteHost` và bộ thể loại tách riêng.

## Route

| route | ý nghĩa |
|---|---|
| `/hclips` | list (home / `?c=` category / `?search=` / `?sort=` / `?pg=`) |
| `/hclips/vidosik?uri=/videos/<id>/<dir>/` | resolve stream (server-side, không lộ token) |
| `/hclips/genres?uri=...` | popup chip thể loại + mục "Liên quan" |
| `/hclips?rel=<uri>` | list video liên quan |

## API đã đo (offline, capture `~/lampac-native/tmp/capture/hclips.com/`)

```
list      /api/json/videos2/14400/str/{sort}/60/{section}.{object_id}.{pg}.all...json
          section="" + object_id=""            -> home
          section="categories" + object_id=dir -> /categories/<dir>/
search    /api/videos2.php?params=259200/str/relevance/60/search..{pg}.all..&s=<kw>
categories/api/json/categories/14400/str.all.en.json          (197 muc)
detail    /api/json/video/86400/{id/1e6}/{id/1000*1000}/{id}.json
related   /api/json/videos_related2/432000/50/{id/1e6}/{id/1000*1000}/{id}.all.{pg}.json
stream    /api/videofile.php?video_id=<id>&lifetime=8640000
```

- `sort` dùng được: `latest-updates`, `most-popular`, `top-rated`, `longest`.
  `most-viewed` trùng `most-popular` → không đưa vào menu.
- **Search luôn bị server ép `sort=relevance`** → khi `?search=` có thì KHÔNG render
  dòng "Sắp xếp" (sort là no-op).
- `section` phải rỗng với list thường; gán `all` → `{"error":1,"code":"invalid_params_section"}`.
- Category `hd` / `4k` / `vr` là filter chất lượng, `/categories/hd/` qua API trả 0 video
  → lọc khỏi menu (chỉ còn 197 mục thật dùng được).

## Stream

`videofile.php` trả `[{"format":"_hq.mp4","video_url":"<base64 bị thay ký tự>"}]`.

- Base64 nhưng ASCII bị thay bằng chữ Cyrillic đồng dạng: `А В С Е М Н О Р Т У Х К`
  (và chữ thường `а е о р с х`), `~` = padding bị ẩn.
- Upornia/… còn thay `,` cho `/` → `DecodeUrl` map `,` → `/` trước khi decode.
- URL sau decode: `/get_file/<n>/<token>/<id1>/<id>/<id>_hq.mp4/?d=&br=&ti=` → 302 sang
  `*.ahcdn.com` (HLS/MP4, token `end=` ~24h). Module đưa qua `/proxy/` của Lampac
  (`streamproxy = true`) nên client không thấy token gốc.

## Verify (2026-11-26)

home 60 item tp=51951 · search "anal" 60 item tp=5084 · category Brunette
60 item tp=14808 + `sort=longest` đổi thứ tự · genres 10 chip + Liên quan · `?rel=` 50 item ·
popup.sh PASS · stream HD `206` qua `/proxy/`, ffprobe `h264 1280x720` + `aac`, ffmpeg decode ok.
