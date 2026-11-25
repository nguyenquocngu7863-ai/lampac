# TopGai (topgai.net)

Module Adult route `topgai`, displayindex 17.
Clone HeoVl (cùng embed streamforester/vcast).

## URLs
- List home: `https://topgai.net/` (?page=N, 0 overlap
  đã verify)
- Category: `https://topgai.net/sex/{slug}?page=N`
- Search: `https://topgai.net/search/{slug}?page=N`
  (slug Việt không dấu, `?k=` redirect về canonical;
  `?page=` trên `?k=` bị bỏ qua nên phải dùng slug)
- Video: `https://topgai.net/phim-sex/{slug}`

## Player
- Trang video chứa `button.set-player-source` với
  `data-cdn-name` + `data-source="https://{embed}/videos/{id}/play?..."`.
- Resolve bằng Playwright (curl embed bị CF 302):
  intercept `.m3u8/.mp4` + `jwplayer().getPlaylist()`
  + response `/config`.
- Loc quang cao: `IsAdUrl` (vast/playhubconnect/ssp/...);
  jwplayer/config la chinh, network chi fallback.
- Chuoi phat: master wogplayer.top qua `/proxy/` nho
  `ProxyApiOverride` tai bang curl process + rewrite
  (WAF wogplayer tra 401 cho TLS .NET, curl 200);
  segment f-seg-1 di proxy thuong (.NET duoc).
- Chu Y: URL trong JSON cua jwplayer/config bi escape
  (`\/`, `\u0026`) — phai `UnescapeUrl` truoc khi dung,
  khong la upstream 401 (bai hoc xuong mau).
- Embed đã thấy: `e.streamforester.name`, `vcast.name`.
- Lay het link ca 2 server (khong break sau embed dau);
  nhan theo `data-cdn-name`. Resolve cham hon (2 browser)
  nhung cache 20p ganh. Test playback bang phim CHUA
  tung query (token ky master ngan han).

## Pagination
- Home/category/search đều `?page=N` truyền thống.

## Cache & Build
- List cache 10p, resolve cache 20p (hybridCache).
- Copy `.cs` vào `/root/lampac/module/Adult/TopGai/`
  + restart, không cần `dotnet build`.
- Verify: `/topgai?pg=1` 20 items,
  `topgai/vidosik?uri=` ra HLS,
  `topgai/video.m3u8` 302 THANG upstream (khong qua
  `/proxy/`: wogplayer tra 401 cho TLS .NET,
  trong khi curl/python/dien thoai 200).
- Strem giữ bước redirect cũ (GetLocation + RCH)
  như Po85: proxy thẳng route là app gãy.

## Popup nhấn giữ (2026-11-25)

- `GET /topgai/genres?uri=<detail>` → `{items:[],groups:[…]}`, không bao giờ 500.
- **Thể loại** (10): badge của detail — `<a href="https://topgai.net/sex/<slug>" class="badge">…<span>Tên</span></a>`.
  Mở bằng `?c=<URL tuyệt đối>`; `Uri()` nhận `c` bắt đầu `http` thì dùng nguyên (trước đó ghép `host + "/" + c` → 503).
  Bỏ badge `/trang/<slug>` vì mục đó trên site đã chết (503).
- **Liên quan**: khối "Phim liên quan" render bằng Alpine `x-data="list('/ajax/suggestions/<slug>')"`,
  KHÔNG có trong HTML → module gọi JSON đó (`X-Requested-With: XMLHttpRequest`), parse `data[]`
  (`title`, `url`, `thumbnail_file_url`) → `?rel=<uri>` trả 20 item, `total_pages: 1`.
- Detail không có tag/diễn viên riêng (chỉ `meta.code`), nên popup chỉ có Thể loại + Liên quan.
- Cache chip+rel trong RAM 6h theo pageUrl.
