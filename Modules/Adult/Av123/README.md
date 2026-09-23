# Av123 (123av.com)

Module Adult route `123av`, displayindex 20. Nhóm lai:
list qua Playwright (CF + Alpine CSR), stream qua
curl + override (CDN doi referer).

## Pages (PlaywrightBrowser, server-side)

- CF chan curl/`.NET` (stall), Chromium that 200.
- List Alpine CSR: poll den khi tile co title that
  (>30 ky tu, loai ma phim) + href on dinh, toi da ~30s,
  roi `EvaluateAsync` lay `[{u,t,p,d}]` (href strip `#`,
  cover `icdn.123av.me`, duration giay -> h:mm:ss).
- `Index` prefetch ngam trang `pg+1` (giong MissAV).
- List cache 10p.
- Menu: Tim kiem + Moi/Hot (new/hot/recent/censored/
  uncensored/uncensored-leaked) + The loai (14 slug da
  verify; site dung ca hash ID nhu `e22f5b4c15`).
- Search: `/vi/search?keyword=`.
- Luu y: mp4 `bkcdn.net` tren trang video la QUANG CAO,
  khong phai phim (bai hoc xuong mau).

## Player (javplayer.cc + hot-desert CDN)

- Moi phim 1 server: iframe `javplayer.cc/e/{hash}`.
- Flow: `GET /stream?id={hash}&poster=...`
  -> `{"media":{"stream":"...video.m3u8?v=2","vtt":"..."}}`.
- Master 1 variant `qc/v.m3u8` (relative), segment duoi
  gia web (`.css/.svg/...`) nhung ruot MPEG-TS, ton tai
  duoi dir cua variant (khong phai root token!).
- CDN doi referer: khong referer -> 403 (day la loi
  "thieu header" khi bam m3u8 truc tiep).
- `ProxyApiOverride` (SCOPE plugin Av123 + host
  hot-desert): master/variant tai bang curl
  (full Chrome UA + referer javplayer.cc + http2),
  resolve relative -> absolute, rewrite ve `/proxy/`;
  segment passthrough bytes. `ForceHttp2` cho hot-desert.
- `headers_stream`: referer + origin `javplayer.cc`.
- `Video` fallback: `q` la -> resolve tuoi -> link dau.

## Cache & Build

- Resolve cache 20p (token TTL chua do; neu dut giua
  phim thi rut xuong).
- Copy `.cs` vao `/root/lampac/module/Adult/Av123/`
  + restart, khong can `dotnet build`.
- Verify that: vidosik 4.2s ra HLS, master 200 rewrite
  `/proxy/`, variant 200, segment 200 MPEG-TS.
