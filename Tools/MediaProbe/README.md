# MediaProbe — tool soi nhanh site mới

Truyền URL trang detail hoặc trang embed, tool fetch → liệt kê ứng viên → thử
các resolver đã biết → in link stream. Dùng lúc làm site mới để biết ngay nó
thuộc họ nào, khỏi mổ bằng tay từ đầu.

## Build & chạy

```sh
cd Tools/MediaProbe
dotnet build -v q --nologo
dotnet bin/Debug/net10.0/MediaProbe.dll '<url>' --proxy http://168.107.66.134:3128
```

`--proxy` (mặc định lấy `HTTPS_PROXY`/`HTTP_PROXY`), `--timeout` giây.

## Output

- `[CANDIDATES]`: iframe, `data-source`/`__pt`/`__pk`, `data-api`, số nút
  `data-id`, marker `pass_md5`/packer/`robotlink`/`ajax-player`.
- `[RESOLVED:<họ>/<hls|mp4>]`: link + referer. Exit 0 = có link, 2 = chỉ có
  ứng viên, 1 = fetch fail.
- Trang detail kiểu POST (`data-source` + `__pt` + nút `data-id`): embed nằm
  sau AJAX, tool KHÔNG resolve mà in hint — làm module POST riêng theo mẫu
  JavCt/SexTb (mỗi nút 1 `pt` tươi, `pt` xoay vòng `next_pt`).

## Các họ đã biết (copy công thức từ modules, giữ nguyên)

| Marker | Họ | Cách resolve |
|---|---|---|
| `pass_md5/...` | DoodStream | GET `<apiHost>/<pass_md5...>?referer=` |
| packer `function(p,a,c,k,e,d)` + `links:{hls3,hls2,hls4}` | StreamHg clone | unpack base36, ưu tiên hls3>hls2>hls4 |
| `player.upn.one#id` / `strp2p.com` | UPN | GET `/api/v1/video?id=` → hex → AES-128-CBC → `cfNative` |
| `playmate.to/embed/id` | Playmate | POST `/api/s {"c":id,"d":"desktop"}` → `sx` |
| `f4s.top/e/id` + `data-api` | F4 | GET api → JSON `{url:/v/token}` (token 5 phút) |
| `/e/<id>` + `POST /api/stream` | Vidara | POST `{filecode,device:web}` → `streaming_url` |
| `robotlink` | Streamtape | token MỚI từ script → `get_video` → 302 CDN |
| `/videos/<id>/play` + `videoData` | StreamQQ (streamforester/vcast) | vcast: `sources` inline sẵn; streamforester: POST `/videos/<id>/config?d=<domain>` body `{}` (rỗng bị 400); POST 404 từ server-side → module dùng Chrome fallback |

## Giới hạn (đừng kỳ vọng quá)

- IP/proxy bị dí nhiều sẽ ăn rate-limit (`adblock:true`, 403) — đợi rồi đo lại.
- Token `pt`/`next_pt` xoay vòng: đo thủ công phải lấy `pt` tươi mỗi lần.
- Site mới toanh không thuộc họ nào → tool báo đúng câu đó + ứng viên để mổ tiếp.
