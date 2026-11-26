# XHeoVL (xheovl.sbs)

Module Adult cho xheovl.sbs (HeoVL). Site **SSR** — list lấy bằng HTTP, player 3 host.

> **Domain (2026-11-26)**: `xheovl.site` **chết** — curl_cffi nhận `SSL_connect: Connection closed abruptly`
> dù DNS vẫn trỏ Cloudflare; Chrome mở được nhưng canonical + beacon trỏ **`xheovl.sbs`**.
> Đo lại: `.sbs` trả 200 bằng HTTP thường (`site.sh get`), 18 card/trang, 514 trang.
> Đã đổi `SiteHost` + `SisiSettings` + manifest; `NormalizePageUrl` vẫn nhận `.site`/`.lat` để
> không vỡ uri cũ đã lưu trong lịch sử/bookmark.

## Cấu hình
- `manifest.json`: module Adult, `dynamic: true` (compile runtime, không cần csproj)
- `ModInit.cs`: `SisiSettings("XHeoVL", https://xheovl.sbs)`
  — `displayindex 25`, `httpversion 1`, `httptimeout 30`, `streamproxy true`, `rch/stream_access apk`
- `Service.SiteHost` là nguồn host duy nhất; đổi domain chỉ sửa 1 chỗ

## Route
| Route | Việc |
|---|---|
| `GET xheovl` | list trang chủ / `?search=` / `?c=/the-loai/<slug>` / `?pg=` |
| `GET xheovl/vidosik?uri=` | resolve trang chi tiết → `{"HLS": "<route>/video.m3u8?uri=…"}` |
| `GET xheovl/video.m3u8?uri=&q=HLS` | resolve → 302 `/proxy/<token>` (Core proxy) |
| `GET xheovl/video` | như trên (giữ tương thích) |

## List: SSR — KHÔNG cần Playwright (verify 2026-10-27)
`RenderPageAsync` ưu tiên **HTML thô** (`GetHtml`), Playwright chỉ còn là fallback
khi raw không có card (site quay lại CSR).

Đo HTML thô bằng `curl_cffi`:

| Trang | `vb-card__title` | card thật | ghi chú |
|---|---|---|---|
| home | 18 | 18 | 78 anchor `/video/` nhưng chỉ 18 có title card |
| category | 7–18 | 7–18 | `hack-cam` 7, `gai-xinh` 18 |

Marker CSR (`x-data` / `x-for` / `x-init` / `Alpine`) = **0** ở cả home lẫn category.
Tốc độ list sau khi bỏ render: **0.08–1.2s** (trước: 3–9s + semaphore chặn song song).

Menu 2 tầng: 10 category đã verify từng cái bằng HTTP (200 + có card).
**Không** đưa `/the-loai/jav` vào menu — slug này **404** trên chính site
(đã thay bằng `Cưỡi Ngựa` = `/the-loai/cuoi-ngua`).

## Player: 3 host song song
1. **`player.vstream.uk/e/<uuid>`** → HLS
   `<link rel="preload" as="fetch" href="…/hls/<token>">` — media playlist, segment S3 signed.
2. **`play.avcl.uk/zpic/?id=<id>&sv=<n>`** → MP4 (ArtPlayer)
   Embed chứa `const url = "https://sN.zpicdn.xyz/<id>.mp4"`; `artconfig.js` phát thẳng `url`
   (xử lý cả mp4 lẫn m3u8 nên không được biến đổi gì thêm).
3. **`vscloud.club/player/<uuid>`** → HLS (host đang dùng, đo 2026-11-26)
   Embed 1.4 KB chỉ có `window.PLAYER_CONFIG = {"id":"…","source":"https://cdn.vscloud.club/videos/<uuid>/index.m3u8"}`
   → regex `.m3u8` tổng quát đã bắt được, **không cần nhánh riêng**.
   Playlist là media playlist VOD: 44 segment, `EXT-X-BYTERANGE`, tên file `.png` nhưng payload
   **MPEG-TS** ở offset 74, có `ENDLIST` → trả `hls = true`, để client tự tải segment.

Fallback: detail không có JSON-LD → `<iframe src="/play/?token=…">` → fetch `/play/` →
`const url = "…"` (vstream hoặc avcl).

Resolve thuần HTTP, không render. Cache `xheovl:vid:{pageUrl}` 30 (giây) trong `hybridCache`.

## Bẫy 1 (lớn nhất): "CSR/AlpineJS → phải Playwright" là kết luận SAI, và tự sinh ra bug
Comment cũ trong `ModInit.cs`: *"CSR site (AlpineJS), cần Playwright render"* — không ngày,
không bằng chứng, không ai đo lại.

Đo lại 2026-10-27: HTML thô đã có đủ card + href **đầy đủ**; không có dấu vết Alpine.

Hệ quả thật khi tin comment đó:
- Playwright đọc **DOM sau JS** = artefact phái sinh. Với một số trang category, DOM trả
  href slug bị **cắt đúng 44 ký tự** (`…buoi-hoc-rieng-k`) trong khi server trả 61
  (`…rieng-keo-dai-hon-9-phut`).
- Detail 404 → `vidosik` trả `503 {"msg":"stream_links"}` → **chỉ item category fail,
  trang chủ vẫn phát** (đúng triệu chứng user báo).
- Biến thể slug cụt biến mất sau ~30 phút (site fix) → rất dễ kết luận nhầm "site chập chờn,
  code ổn". Cache đĩa `cache/fdb` rỗng nên không phải cache cũ.

Luật rút ra:
- Câu "site CSR hay SSR?" **chỉ** trả lời được bằng `curl_cffi` raw + `grep` marker card.
  `jina` / Chrome / Playwright đều render JS → **xoá mất khác biệt cần đo**.
- Trang có JS (Alpine, ArtPlayer…) không có nghĩa nội dung do JS sinh — progressive enhancement
  là bình thường.
- Nếu vẫn phải phòng site đổi: HTTP-first + Playwright fallback, đừng đặt cược một chiều.

Dấu hiệu nhận dạng slug cụt: độ dài **đúng 44** (slug thật 51–94). Gặp `stream_links` hàng loạt
thì so `len(slug)` và so HTML thô vs DOM trước khi đụng vào parser player.

## Bẫy 2: JSON escape `\u0026` trong embedUrl / const url
`"embedUrl":"https://play.avcl.uk/zpic/?id=Juk45\u0026sv=12"` — regex thô sẽ lấy literal
`\u0026` → URL sai → 404. `JsonUnescape()` bỏ `\u0026 \u002f \u003d \u003a \/` trước khi dùng
(áp cho cả JSON-LD, `const url` ở `/play/` và ở embed).

## Bẫy 3: player avcl — CDN `zpicdn.xyz` chưa có file (lỗi site, 2026-10-27)
- Root `https://s12.zpicdn.xyz/` = trang mặc định nginx **"恭喜，站点创建成功！"** (vhost mới tạo, rỗng).
- `<id>.mp4` → `404 text/html` 138 byte, kể cả khi qua proxy Oracle Singapore.
- Module vẫn parse đúng (vidosik 200 → route `.mp4`) nhưng phát ra 404 từ CDN.
  → Video thuộc player này **chưa phát được ở đâu cả**; chờ site nạp file, không sửa gì thêm.

## Đo (2026-10-27, sau deploy)
| Endpoint | Kết quả |
|---|---|
| `xheovl` | 200, 18 item, 439 trang, 0.08s |
| `xheovl?c=/the-loai/gai-xinh` | 200, 18 item, 18 trang |
| `xheovl?c=/the-loai/cuoi-ngua` | 200, 18 item, 101 trang, 1.2s |
| `xheovl?pg=2` | 200, item đầu khác trang 1 |
| `xheovl?search=doggy` | 200, 18 item chứa keyword |
| `vidosik` item category (vstream) | 200 → `video.m3u8` → proxy trả `#EXTM3U` |
| `vidosik` item category (avcl) | 200 → route `.mp4`; CDN 404 (bẫy 3) |

## Check / deploy
- Check compile (tự ref Playwright + Bcl.AsyncInterfaces, không cần thêm ref tay):

```bash
~/lampac-native/csc/check.sh ~/lampac-clean/Modules/Adult/XHeoVL
```

- Deploy: copy 4 file (`Controller.cs`, `ModInit.cs`, `Service.cs`, `manifest.json`) sang
  `~/lampac-run/module/Adult/XHeoVL/` (không copy csproj) → restart → log phải có
  `loaded module: XHeoVL`, không có `error CS`.
