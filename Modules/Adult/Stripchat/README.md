# Stripchat

Nguồn livecam Stripchat (`stripchat.com`). Route: `/strp`.

## API

`GET /api/front/models?limit=90&offset=O&primaryTag=girls|boys|couples|trans&filterGroup=presets`
- cần header `Accept: application/json`, `Referer`/`Origin: https://stripchat.com`
- intermittent TLS EOF trên mạng Datacenter → retry phía Python, phía C# dùng HttpClient HTTP/2 + httpHydra

Model đã chứa sẵn `hlsPlaylist` (ví dụ `https://edge-hls.doppiocdn.media/hls/<id>/master/<id>_240p.m3u8`), `presets`, `previewUrlThumbSmall`.

## Routes

| Route | Tham số | Mô tả |
|---|---|---|
| `/strp` | `sort` (girls/boys/couples/trans), `pg` | Playlist 90 phòng |
| `/strp/potok?hls=<url>&baba=<name>` | | Trả `qualitys: {auto: <hls>}` → SISI player |

Playlist trả `video: "strp/potok?hls=...&baba=..."` — stream không cần fetch lần 2. HLS phát trực tiếp qua edge-hls (không cần proxy Referer đặc biệt), Lampa sẽ proxy qua `/proxy/` nếu cấu hình.

## Khác biệt với Chaturbate

- Chaturbate: `GET /api/ts/roomlist/room-list/` → list, rồi `GET /<username>/` → scrape `playlist.m3u8` từ HTML.
- Stripchat: list đã có `hlsPlaylist` → một bước.
