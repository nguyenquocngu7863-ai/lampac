# MissAV (missav.live)

Module Adult route `missav`, displayindex 18. Nhóm SISI lai:
list/page qua Playwright (CF + Alpine CSR), stream qua curl
override (surrit kén TLS .NET). Pattern gần TopGai.

## Vì sao không httpHydra thuần (nhóm Vlxx)

- `missav.live` CF chặn IP datacenter: curl/`.NET` stall
  hoặc 403, Chromium thật (Playwright) qua 200.
- Trang là AlpineJS CSR: `ContentAsync()` ngay
  `DOMContentLoaded` chỉ có template (`:href`/`:data-src`).
  Tile thật chỉ có sau khi Alpine hydrate.

## Pages (PlaywrightBrowser, server-side, không cần rch)

- `PageFetchAsync(url)` → `GotoAsync(DOMContentLoaded)` →
  poll mỗi 2s đếm `{titled, linked}`, dừng khi cả 2 ổn định
  3 lần liên tiếp (tối đa ~30s) → `EvaluateAsync` lấy JSON
  `[{u,t,p,a}]` từ `.thumbnail.group`.
- Tile hydrate: `img.src` luôn là placeholder lozad 1px →
  **ưu tiên `data-src`**, bỏ qua `data:`.
- Title hydrate sau duration → `t` tách duration ra để đếm;
  C# fallback sang `alt` (mã phim) khi tên rỗng hoặc chỉ là
  duration. Không bao giờ ra hàng trắng tên.
- `Index` prefetch ngầm trang `pg+1` qua đúng pipeline
  `InvokeCacheResult` (`refresh_proxy:false`) → lật trang nhanh.
- List cache 10p, resolve cache 20p (hybridCache).

## URLs

- Home: `https://missav.live/vi` (Alpine, tile
  `.thumbnail.group` → href `/vi/{slug}#frag`, cover
  `fourhoi.com/{code}/cover-t.jpg`).
- Category: giữ nguyên URL `/dmNN/` trong nav của site
  (vd `.../dm539/vi/new`); `Uri()` nhận cả URL tuyệt đối.
- Search: `https://missav.live/vi/search/{slug}`
  (slug Việt không dấu, lowercase).
- Pagination: `?page=N`.
- Menu: Tìm kiếm + Mới/Hot (8) + Thể loại (19 genre thật
  lấy từ nav) + Hãng phim (12 makers).

## Player

- Trang video chứa `eval(function(p,a,c,k,e,d))` lồng nhau →
  `StreamUrls` giải packer (port thuật toán Dean Edwards
  sang C#) → `https://surrit.com/{token}/playlist.m3u8`
  (master) + `720p/1080p/video.m3u8`.
- surrit chặn TLS `.NET` (curl/Chromium 200) →
  `ProxyApiOverride`: master/variant tải bằng curl process
  (full Chrome UA + referer missav + http2), resolve path
  relative về absolute rồi rewrite sang `/proxy/`;
  segment passthrough bytes qua curl (content-type theo ext).
- `ForceHttp2` ép h2 cho host surrit.
- Chuỗi phát: `/missav/vidosik` → dict Master/720p/1080p →
  `/missav/video.m3u8` → `HostStreamProxy` → `/proxy/`.

## Giới hạn đã biết

- Load đầu mỗi trang ~15-30s (browser + CF + chờ hydrate),
  các lần sau ăn cache.
- Hook `ProxyApiOverride`/`ForceHttp2` match theo host
  `surrit.com` (global) — module khác dùng chung CDN này
  sẽ đi qua override (curl, đã verify 200).

## Cache & Build

- Copy `.cs` vào `/root/lampac/module/Adult/MissAV/`
  + restart, không cần `dotnet build`.
- Verify: `/missav` ~70 items đủ title/cover,
  `missav/vidosik?uri=` ra Master/720p/1080p,
  master rewrite `/proxy/`, segment 200 MPEG-TS,
  `?search=` trúng.
