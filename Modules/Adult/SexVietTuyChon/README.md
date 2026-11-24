# SexVietTuyChon (dongsexviet.com)

Module Adult route `sexviettuyenchon`. Site SSR (PHP), player jwplayer qua `POST /api/player`.

> **2026-11-24 — đổi domain**: `sexviettuyenchon.com` giờ **301 → `dongsexviet.com`**.
> Redirect biến `POST` thành `GET` ⇒ `POST https://sexviettuyenchon.com/api/player`
> trả **404** và `vidosik` fail `player`. Đã đổi `SiteHost` sang `dongsexviet.com`,
> và **POST `/api/player` luôn gọi theo origin của trang detail** (`SexVietTuyChonTo.Origin(detailUrl)`)
> để nếu site đổi mirror lần nữa thì player vẫn sống.

## List
- Card: `<li class="item-movie">` → `<a title=".." href="https://<host>/<slug>.html">`.
- Thumb: `data-original="https://domain115.com/.../hinh.jpg"` (host ảnh khác site — proxy qua `headers_image`).
- `time` = 1 nhãn site in trên card (`VN` / `VIETSUB`), `quality` null.
- Phân trang: `/`, `/<danh-muc>/`, `/search/<slug>/` + `trang/N/`; `total_pages` = max `/trang/N/`.
- Search: form GET → `slugify` bỏ dấu tiếng Việt (`assets/js/media.js`), không phải query param.
- 5 danh mục cố định (menu tầng 2): Việt Nam / Nhật Bản / Trung Quốc / Châu Âu / Vietsub.
- Site không có sort ⇒ không có dòng 2 menu.

## Player
- Detail: `<div id="video" data-video-id="4488">`, server: `data-server-id="1"`.
- `POST <origin detail>/api/player` body `id=<videoId>&server=<id>` →
  `{"status":"success","data":{"type":"jwplayer","sources":[{"file":"https://domain115.com/.../output.m3u8?cdn=tt","type":"hls"}]}}`
  (có nhánh `type:"iframe"` → `data.source`).
- Mỗi server là một qualitys; `streamproxy=true`.

## Popup (long-press)

| route | nguồn |
|---|---|
| `/sexviettuyenchon/genres?uri=<detail>` | `Thể loại`: 1 chip `phim-sex-*/`; `Thẻ`: `div#extras` → `<a href=".../tag/<slug>/">`; `Liên quan`: 1 item `plain:true` → `?rel=<uri>` |
| `/sexviettuyenchon?rel=<detail>` | parse lại card trong trang detail (bỏ chính video đang xem), `total_pages: 1`, `pg>1` → rỗng |

- Cache: `sexviettuyenchon:info:{uri}` 360 phút, `sexviettuyenchon:rel:{uri}` 180 phút.
- Chặn SSRF: `uri` phải bắt đầu bằng `SiteHost`, nếu không → popup rỗng 200.

## Đã đo (2026-11-24, sau khi đổi domain)

- home 28 item `tp=159`; `c=phim-sex-viet-nam&pg=2` 28 `tp=111` (khác trang 1);
  `search=em xinh` 28 `tp=3`; `c=tag/jav` 28 `tp=2`; `c=phim-sex-vietsub` 28 `tp=10`
- `genres?uri=` → Thể loại 1 + Thẻ 10 + Liên quan 1; `?rel=` 16 item `tp=1`
- `vidosik` → `qualitys{1: /proxy/...m3u8}`; `play.sh` → **PASS**: h264 1920x1080 · 405.1s,
  segment 82/82 qua Lampac, decode 5s đầu **và** cuối OK, cold 0.16s / warm 0.12s

## Build
- `~/lampac-native/csc/check.sh ~/lampac-clean/Modules/Adult/SexVietTuyChon` → 0 error
- copy 4 file `.cs`/`manifest.json` sang `~/lampac-run/module/Adult/SexVietTuyChon/` → `nrestart`
