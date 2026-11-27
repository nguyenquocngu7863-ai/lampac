# QmhSex (qmhsex.guru)

Module Adult route `qmhsex`. Host đổi: `qmhsex.shop` → **`qmhsex.guru`** (2026-11-27).
(`qmhsex.vip` / `qmhsex.top` là mirror khác theme, không dùng.)

## List
- Home `https://qmhsex.guru/` — WordPress: `<article data-post-id="…"><a href="/<id>" title="…">`
  + `div.post-thumbnail`, phân trang `?page=N` / `/page/N`.
- Popup: `qmhsex/genres?uri=` (chips), `?rel=` (Gợi ý cho bạn).

## Player (2 chặng)
1. Trang detail: `var sources=["https://s63.xvideo.lat/embed/<b64>.html"]` + `id="serverN"`.
2. `POST /wp-admin/admin-ajax.php` `action=getplayer&source=<sources>&server=N`
   → `{"file":"https://go1.qooglevideo.shop/x/<b64>.html"}` (iframe).
3. Trang embed: `jwplayer(...).setup({sources:[{file:"https://stream.bytecdn.cfd/…m3u8"}]})`
   → hai biến thể `DP 1` / `DP 2`.

## Ảnh hưởng DNS (VN)
- `go1.qooglevideo.shop`, `s63.xvideo.lat` **không resolve** từ mạng VN → phải fetch embed
  qua Worker relay: env `XNHAU_RELAY` (`https://xn-relay.nguyenquocngu7863.workers.dev`),
  allowlist phải có `qooglevideo.shop,xvideo.lat,bytecdn.cfd`.
- `stream.bytecdn.cfd` resolve bình thường.

## Segment PNG → TS
- m3u8 của site trỏ segment `.png`; nội dung thật = **header PNG (≈62 byte) + MPEG-TS**.
  ffmpeg/hls.js từ chối đuôi `.png` và cả header.
- Route `qmhsex/hls?uri=&q=` rewrite segment sang `qmhsex/seg/<ProxyLink>` (suffix `#.ts`),
  `qmhsex/seg/…` tải upstream, bỏ header PNG (tìm sync `0x47` mỗi 188 byte), trả `video/mp2t`.

## Đã đo (2026-11-27)
- `/qmhsex` 34 item `tp=102` · `vidosik` `{qualitys: DP 1/DP 2, recomends: 10}` ·
  `ffprobe` route `DP 1`: h264 720x1280 + aac, decode 6s đầu OK.
