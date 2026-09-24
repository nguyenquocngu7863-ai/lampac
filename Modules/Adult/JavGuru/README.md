# JavGuru (jav.guru)

Module Adult, route `javguru`, displayindex 21 (ngay sau JavHD).
Parse tĩnh bằng `httpHydra` + regex, **không dùng Playwright, không hook global**.

## List

- Trang chủ `/`, phân trang `/page/N/`.
- Tìm kiếm `/?s=q` → `/page/N/?s=q`.
- Danh mục/tag `/category/{slug}/`, `/tag/{slug}/` + `page/N/`.
- BXH `/most-watched-rank/`: chỉ có 1 trang (~100 phim), module tự cắt 24 phim/trang.
- Card: `div.inside-article` (BXH: `.rank-item`), link `https://jav.guru/{id}/{slug}/`,
  tiêu đề `h2 > a`, ảnh `img@src`.
- Ảnh `cdn.javmiku.com` bị Cloudflare challenge nên đổi sang
  `https://jav.guru/wp-content/...` (cùng path, trả 200 khi có UA + Referer jav.guru).
  `headers_image` đã set sẵn.
- Menu: Tìm kiếm, Mới nhất, Xem nhiều, Danh mục (Uncensored, Decensored, English
  subbed, Amateur, Idol, FC2, 4K, 1080p, VR), Thể loại (20 tag).

## Phát (chuỗi 4 bước)

1. Trang phim có tối đa 6 `"iframe_url":"<base64>"` → `https://jav.guru/searcho/?{x|u|t|c|h|o}d=...`.
2. Trang loader `searcho` có `window.cfg = {cid, base, rtype, keys:['data-…']}` và
   `div#<cid>` chứa token. Ghép các thuộc tính theo thứ tự `keys`, **đảo ngược**, rồi
   gọi `{base}/?{rtype}r=<token>`. Nếu không có cfg thì dùng kiểu cũ: đảo ngược giá trị `?xd=`.
3. Request đó trả 302 sang host nhúng. Module dùng HttpClient riêng
   (`AllowAutoRedirect=false`) để đọc header `Location` thô.
4. Lấy stream theo host:

| Host | Cách lấy | Nhãn |
|---|---|---|
| emturbovid → turbovidhls | `var urlPlay = '...mp4'` / m3u8 trần / `data-hash` | Turbo |
| vidara.to (+ mirror) | `POST /api/stream {filecode, device}` → `streaming_url` | Vidara |
| javclan / streamwish | giải `eval(p,a,c,k,e,d)` → `hls2`/`hls4` | JavClan |
| javlesbians → VOE | JSON `application/json`: rot13 → bỏ junk → b64 → −3 → đảo → b64 | VOE |
| maxstream | giải packer → `file:` | MaxStream |
| dood / vide0 | `/pass_md5/` + chuỗi ngẫu nhiên + `token`, `expiry` | Dood |

- Các mirror được resolve **song song**. Khi đã có server đầu tiên chạy được, module
  chờ thêm 5s rồi trả về (giới hạn cứng 30s). Thứ tự ưu tiên:
  Turbo > Vidara > JavClan > VOE > MaxStream > Dood.
- `vidosik` trả `qualitys` = danh sách server (nhãn theo host) + `recomends`
  (phim liên quan, lấy từ `div.woo-sc-related-posts`).
- `/javguru/video.m3u8|mp4?uri=&q=<server>` → `HostStreamProxy` kèm Referer/Origin
  của host nhúng. `streamproxy = true` mặc định vì token Vidara gắn với IP server và
  mỗi host cần Referer riêng.
- Cache kết quả resolve 15 phút (memoryCache). `SemaphorManager` chống resolve trùng.
- `?uri=` chỉ chấp nhận `jav.guru` / `init.host` (chống SSRF).

## Rủi ro đã biết (theo khảo sát 09/2026)

- JavClan đôi khi dùng `hg-function.js` bị obfuscate (không có packer) → mirror đó bỏ qua.
- vide0.net (Dood) hay dính Cloudflare với IP datacenter; IP nhà (Termux) thường qua được.
- Hop `emturbovid` có lúc chứa byte điều khiển trong `Location`. Nếu trang player rơi vào
  `/sandbox`, module thử gọi thẳng `turbovidhls.com/t/<id>`.

## Cài / cập nhật

- `bash setup-termux.sh --sync` sẽ kéo `Modules/Adult/JavGuru/*` về
  `/root/lampac/module/Adult/JavGuru/`. Có thể copy tay rồi `lampac stop && lampac start`
  (không cần `dotnet build`, module được biên dịch runtime).
- Kiểm tra: `/javguru` ra list, `/javguru/vidosik?uri=https://jav.guru/<id>/<slug>/`
  ra `qualitys`, `/javguru/video.m3u8?...` trả 302 tới link proxy.
- Tắt/đổi host trong `init.conf`: `"JavGuru": { "enable": false }` hoặc `"host": "..."`.
