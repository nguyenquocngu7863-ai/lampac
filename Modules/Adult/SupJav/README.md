# SupJav (supjav.com)
Module Adult cho supjav.com (JAV không che, nhiều server: EVS/VOE/VAS/ST/LUC).
## Cấu hình
- `manifest.json`: module Adult, `dynamic: true` (compile runtime, không cần csproj)
- `ModInit.cs`: SisiSettings "SupJav"; `useproxy=false` (site mở direct, proxy bị CF chặn)
## Route
| Route | Việc |
|---|---|
| `GET supjav` | list / search / `?c=` / `?sort=` / `?pg=` |
| `GET supjav/vidosik?uri=` | danh sách server của phim — trả `{"EVS": url, "VOE": url, ...}` |
| `GET supjav/video.m3u8?uri=&srv=` | resolve đúng 1 server → 302 `/proxy/<token>` |
| `GET supjav/video` / `supjav/video.mp4` | như trên (giữ tương thích + server ST trả mp4) |
## Luồng resolve (2026-10-22)
1. `vidosik`: fetch trang chi tiết → `.btn-server[data-link]` → trả list (không resolve).
2. `video`: `data-link` → `FinalUrl` (bỏ `?l=&c=` cấp 1) → gateway `lk1.supremejav.com/supjav.php?l=…`
   → HTML embed → tách link theo từng loại server (LUC packer / VAS API / StreamHg jwplayer / StreamTape).
3. Cache `supjav:stream:{pageUrl}:{label}` 10 phút.
## Bẫy 1: gateway trả 403/502 chập chờn (nguyên nhân "cả 5 server 503")
`lk1.supremejav.com` khi thì 200 (trang `<title>Embed</title>`), khi thì **403/502** (Cloudflare).
Trước đây resolve chỉ thử 1 lần → gateway lỗi là **toàn bộ server** trả
`503 {"msg":"stream_links"}`, hls.js báo `manifestLoadError` (chỉ là hệ quả, không phải lỗi player).
Nay: `RaceGetAsync` (httpHydra + `Http.Get` song song) **retry tới 40s** trước khi bỏ.
Nếu vẫn không có HTML → fallback `chrome:` (Playwright mở gateway, JS redirect localStorage rồi bắt
`.m3u8/.mp4`), giống đường VOE.
## Bẫy 2: HEAD ≠ GET
CDN của server (nginx) trả **403 cho HEAD** nhưng **200 cho GET**. Đừng verify bằng HEAD rồi kết luận
link chết (đã từng làm LUC "chết oan" đúng lúc GET vẫn 200).
## Bẫy 3: LUC (LuluStream) — CDN 403 vì `Accept-Encoding: br` ⇒ đã fix bằng relay (2026-10-28)

Đo 2026-10-22 (video 466575 / 466752 / 466753):

| Ai fetch master `*.tnmr.org` | Kết quả |
|---|---|
| `Http.Get` (stack module, header riêng) | **200 `#EXTM3U`** |
| `curl_cffi impersonate=chrome120` | 403 nginx |
| Chrome thật mở player | `net::ERR_FAILED` (403) |
| `/proxy/<token>` của Lampac | 403 |

**Nguyên nhân gốc** (tìm ra khi đối chiếu module JAVSnap, cùng CDN): `ProxyAPI` luôn ghép
`Http.defaultFullHeaders` vào request, trong đó có **`Accept-Encoding: gzip, deflate, br`**, và
**ghi đè** header do module truyền vào (xem `Core/Middlewares/ProxyAPI.Utilities.cs`
→ `CreateProxyHttpRequest`: `addHeaders` của module bị `defaultFullHeaders` đè sau). nginx của
LuluStream trả **403 khi thấy `br`**. Token KHÔNG phải one-time — chỉ là header.

**Cách fix: relay HLS ngay trong module** (client không bao giờ chạm CDN):

| Route | Việc |
|---|---|
| `supjav/relay.m3u8?uri=<token>` | fetch master/variant bằng `Http.Get` (200), rewrite mọi URI về module |
| `supjav/relay.seg?uri=<token>` | fetch segment/key bằng `Http.BaseDownload`, trả nguyên bytes |

- Token = `EncryptQuery(url + '\u0001' + referer)` (AES-CBC key trong `cache/aeskey`), có
  `accsArgs` ⇒ không phải open proxy.
- Rewrite cả `#EXT-X-STREAM-INF`, `#EXT-X-MEDIA`, `#EXT-X-I-FRAME`, `#EXT-X-KEY`, `#EXT-X-MAP`.
- `Range` của client được chuyển tiếp; playlist trả `application/vnd.apple.mpegurl`.
- `Video` với label `LUC` → `302` sang `relay.m3u8` thay vì `HostStreamProxy`.

**Đo 2026-10-28** (site đang chặn IP nên test bằng HLS công khai `test-streams.mux.dev`):
master `200` → variant `200` (12 119 B) → segment `200` (1 915 156 B, sync `0x47` = MPEG-TS thật).

## Đo (2026-10-22)
- `vidosik` 466753: `EVS, VAS, ST, VOE` (LUC đã ẩn).
- `video.m3u8/mp4` 4 server: `200` — EVS/VOE/VAS `application/vnd.apple.mpegurl` (`#EXTM3U`),
  ST `video/mp4` (`ftypisom`). Thời gian resolve 0.6–11s (cache 10 phút).
