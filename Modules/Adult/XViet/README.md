# XViet (xviet.live)

Tube Việt (PHP/WordPress-like, HTML server-side). Trang list dùng `<article id="post-N">`,
chi tiết có `window.VB_PLAY = { s1, s2 }` (2 token) → `/play/?token=...` → embed
(`play.kbj.lol` jwplayer / `vscloud.club` Nid Player) → HLS.

## Routes

| route | ghi chú |
|---|---|
| `/xviet` | `?search=` `?c=the-loai/<slug>` `?c=tag/<slug>` `?pg=` |
| `/xviet/genres?uri=<detail>` | `groups`: `Thể loại` (4, từ `/the-loai/` trong article chính → `?c=the-loai/<slug>`), `Thẻ` (25, `rel="tag"` → `?c=tag/<slug>`), `Liên quan` (`plain: true` → `?rel=<uri>`) |
| `/xviet?rel=<detail>` | 1 trang, 8 item từ khối `<h1>Video liên quan</h1>` cuối trang, `total_pages: 1`, `pg>1` → `{tp:1,list:[]}`, uri lạ/rỗng → 200 rỗng |
| `/xviet/vidosik?uri=<detail>` | `{ "Server 1": "<host>/xviet/video.m3u8?uri=&srv=1" }` |
| `/xviet/video.m3u8?uri=&srv=` | resolve token → play page → embed → m3u8, redirect qua `HostStreamProxy` |

Chip popup chỉ **1 lần fetch detail**, cache `xviet:info:{uri}` 360 phút; `?rel=`
cache `xviet:rel:{uri}` 20 phút (dùng chung `GetPageAsync` retry 30s).

## Đã đo (2026-11-24)

- home 20 item · `tp=299`; `pg2` 20; `search=teen` 20 · `tp=249`
- `c=the-loai/teen` 20 · `tp=2`; `c=tag/sinh-vien` 20 · `tp=148`
- `genres?uri=` → 3 nhóm (4 thể loại / 25 thẻ / 1 liên quan)
- `?rel=` 8 item · `tp=1`; `pg=2` rỗng; `rel=https://evil.example/x` rỗng
- `play.sh` → **PASS**: h264 720x1280 · 72s, cold 0.16s / warm 0.17s, segment 24/24,
  decode 5s đầu **và** cuối OK

## Ghi chú

- TLS tới `xviet.live` từ IP này chập chờn (`curl_cffi` hay `SSL_connect closed`) —
  module đã có sẵn `RaceGetAsync` (httpHydra chạy song song `Http.Get`) + retry 30s nên
  vẫn lấy được trang; **không cần relay**.
- Trang detail không có pornstar/studio (chỉ thể loại + tag) nên popup không có nhóm đó.
