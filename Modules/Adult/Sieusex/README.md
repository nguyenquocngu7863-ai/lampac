# Sieusex — module Adult (Lampac NextGen)

Route: `/sieusex` · `/sieusex/vidosik` · `/sieusex/video.m3u8`
Site: `https://biphim.net` (theme WordPress **video3x**)

> **Lưu ý domain (bẫy lớn nhất của module này):** domain cũ `sieusex.net` đã
> **302 sang `biphim.net`** và **mất path** (`sieusex.net/new/2/` → `biphim.net/`,
> tức là mọi phân trang sẽ lặp lại trang 1). Vì vậy:
> - `SiteHost` trong `Service.cs` là **domain hiện hành** (`https://biphim.net`).
> - **Không** dùng domain cũ làm fallback: nó nuốt path.
> - Regex parse **không hardcode host** (nav/card/search đều absolute) → khi
>   domain xoay tiếp, chỉ cần đổi 1 dòng `SiteHost`.

## 1. URL công thức (đo 2026-10-26)

| Việc | URL |
|---|---|
| Trang chủ = list phim mới | `/new/1/` (trang `/` trùng nội dung) |
| Phân trang list | `/new/{N}/` (112 trang) |
| Danh mục (nav) | `/{slug}/` — 17 slug: `phim-sex-vietsub`, `phim-sex-khong-che`, `phim-sex-loan-luan`, `phim-sex-vung-trom`, `phim-sex-viet-nam`, `phim-sex-trung-quoc`, `phim-sex-nhat-ban`, `phim-sex-chau-au`, `phim-sex-dit-nhau`, `phim-sex-hoc-sinh`, `phim-sex-hiep-dam`, `sextop1`, `vlxx`, `xnxx`, `jav-hd`, `phim-xxx`, `phim-heo` |
| Phân trang danh mục | `/{slug}/page/{N}/` (trailing slash bắt buộc) |
| Search | `/search/{slug}/` + `/search/{slug}/page/{N}/` (10 trang cho "nguoi") |
| Top tìm kiếm (menu) | block `search-history` → `/search/<q>/` và `/tag/<q>/` |
| Chi tiết | `/{slug}.html` (link absolute trên domain hiện hành) |

### Bẫy search (đã sửa 2026-10-26)
- Site **404** nếu slug có unicode: `/search/ng%C6%A1i/` → 404.
  → `SearchSlug()` phải **bỏ dấu tiếng Việt** (`FormD` + bỏ `NonSpacingMark`):
  `ngợi` → `ngoi`, `người` → `nguoi`.
- `/?s=<kw>` trả 200 nhưng **không lọc** (ra trang chủ) → không dùng.
- Search **có phân trang** (`/search/<slug>/page/N/`), không khoá 1 trang như bản cũ.

## 2. Parser (server-side HTML, không cần Chrome)

Card: `<div id="video-{id}" class="video-item"><a title="..." href="{abs}.html">`
→ regex bắt `title` + `href`, `NormalizePageUrl()` **không whitelist host** (domain xoay).

Menu (2 tầng, đúng §9g):
1. `Tìm kiếm` (`search_on`, `playlist_url` = route local)
2. `Danh mục` → `submenu` 17 slug (lấy từ `id="primary-nav"`, bỏ slug `new`)
3. `Top tìm kiếm` → `submenu` 29 mục (block `search-history`)

Mọi mục cấp 1 có `playlist_url` **hoặc** `submenu`; item cấp 2 có `playlist_url`
thật. Không dùng `playlist_url == "submenu"` cho item cấp 2 (màn hình trắng).

## 3. Player chain

```
detail .html  →  POST /wp-admin/admin-ajax.php
                 body: player_server={postId}&server={1..3}&vast_seen=&is_mobile=
                 → JSON {"player":"<iframe src=\"https://hidecdn.net/embed/{hash32}?cdn=tt3\">"}
              →  GET embed hidecdn.net
                 → biến JS `serverPlDataMap = { "tt3": "<base64>", "tt": "...", ... }`
                 → giải mã: base64 decode → XOR 0x5A mỗi byte → text m3u8
              →  m3u8 (23 segment) trên tiktokcdn / ibyteimg (không cần Referer)
              →  Controller bọc segment qua HostStreamProxy → /proxy/...
```

- `postId` lấy từ `<div id="video" post-id="3400" data-sv="1">`.
- Có 3 server (`server(1..3, postId)` trong `single.js`); server chết → fallback
  thử server còn lại (11c).
- `cdn=tt3` là key đúng; nếu decode fail thử lần lượt `tt3/tt2/tt/tt1` cùng embed.

## 4. Bảo trì / deploy

```bash
cd ~/lampac-clean
SHARED_DLL=$HOME/lampac-run/Shared.dll ~/lampac-native/csc/check.sh Modules/Adult/Sieusex
cp Modules/Adult/Sieusex/{Controller.cs,ModInit.cs,Service.cs} ~/lampac-run/module/Adult/Sieusex/
pkill -f "Core\.dll"; sleep 3
nohup bash ~/lampac-native/run-lampac.sh > ~/lampac-native/lampac-native.log 2>&1 &
# ~25s rồi: grep "loaded module: Sieusex"
```

Không copy `.csproj` / `bin` / `obj` sang runtime.

Cache (hybridCache): `sieusex:v3:{search}:{c}:{pg}` (list), `sieusex:menu:v2` (menu),
`sieusex:pl:{url}:{sv}` (playlist 120s). Đổi công thức URL → **bump version** key.

## 5. Smoke test (đã chạy 2026-10-26, tất cả 200)

```bash
curl "http://127.0.0.1:9118/sieusex?pg=1"          # 30 item, 112 trang, menu 3
curl "http://127.0.0.1:9118/sieusex?pg=2"          # item khác trang 1
curl "http://127.0.0.1:9118/sieusex?c=jav-hd&pg=1" # 30 item, 42 trang
curl "http://127.0.0.1:9118/sieusex?c=jav-hd&pg=2" # item khác
curl "http://127.0.0.1:9118/sieusex?search=ng%C6%B0%E1%BB%9Di&pg=1"   # 30 item, 10 trang
curl "http://127.0.0.1:9118/sieusex?search=ng%C6%B0%E1%BB%9Di&pg=2"   # item khác
curl "http://127.0.0.1:9118/sieusex/vidosik?uri=https%3A%2F%2Fbiphim.net%2Fchen-em-tiktoker-xinh-dep-mong-tron-trang-non-cuc-ngon.html"
curl "http://127.0.0.1:9118/sieusex/video.m3u8?uri=...&sv=1"          # #EXTM3U, 23 segment
```

Lưu ý: search không có kết quả → list rỗng → framework trả 503
`{"error":true,"msg":"playlists"}` (hành vi chung, giống các module khác).

## 6. Lịch sử lỗi

| Ngày | Lỗi | Nguyên nhân | Sửa |
|---|---|---|---|
| 2026-10-26 | `/sieusex` → 503 `playlists` | site đổi domain `sieusex.net` → `biphim.net`; regex whitelist host + `NormalizePageUrl` loại mọi link → 0 card | đổi `SiteHost`, bỏ whitelist host, regex host-agnostic |
| 2026-10-26 | search có dấu → 503 sau 23s | slug unicode → site 404, controller retry tới deadline | `SearchSlug()` bỏ dấu |
| 2026-10-26 | search khoá 1 trang | bản cũ tưởng site không có trang 2 | bỏ khoá, thêm `/search/<slug>/page/N/` |
