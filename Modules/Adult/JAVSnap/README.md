# JAVSnap

Site: `https://javsnap.com` (JAV HD, HTML server-side, 4 danh muc: censored / uncensored / subtitles / amateur).

## Trang thai

- [x] Danh sach: cat block `<div data-movie-item="…">`, title + link trong
      `<h2 class="item__title"><a href="…">`, poster `<img src>` **trong `item__cover`**.
- [x] Poster: URL that (`https://pics.javsnap.com/…webp`) — xem bay `onerror` ben duoi.
- [x] Detail: 4 server trong `<button class="ep-btn" data-url="…">LABEL</button>`
      (VIP / SHG / DDS / LLV).
- [x] Giai ma 4/4 server that: VIP (HLS), DDS (mp4), **STP (Streamtape, mp4)**, LLV (HLS).
      SHG (hgcloud → audinifer) dung MSE blob — khong ho tro (khong hien trong danh sach server).
- [x] Search: **path-based** `https://javsnap.com/en/search/<keyword>` (lay tu `action` cua form +
      ham `searchPageUrl()` trong `/assets/client/js/main.js`). Dang query cu `/search?q=…` tra 500.
      Ket qua search khong phan trang (`/en/search/<k>/pg-2` rong) → tra `total_pages: 1`.
- [x] Phan trang: `/<cat>/pg-N` va `/<taxonomy>/pg-N` (parser dem so lon nhat tu link `/pg-<n>`;
      censored = 247 trang, genre Hi-Def = 385 trang).
- [x] Trang chu: `/en/` **khong phan trang** (`/en/pg-N` = 404, `/en/?p=2` = trang 1) →
      "Mới nhất" = **gop `en/censored` + `en/uncensored`** theo id giam dan (2 danh muc nay
      khong trung nhau, phu toan bo phim): ~60 phim/trang, 247 trang.
- [x] Menu **2 tầng** (checklist 9g): `Tìm kiếm` (search_on) → `Danh sách` (6 list toan cuc) →
      `Thể loại` / `Hãng phim` / `Nhãn phim` (72 muc moi nhom, lay dong tu
      `/en/categories`, `/en/studios`, `/en/labels`, cache 12h).
- [x] Sort: site **khong co sort** (khong co control sort trong HTML; `?sort=views|popular|…`
      tra y nguyen list — do 2026-10-26 tren home/censored/genre/studio/label/search) →
      theo 9g #4 **khong dung dong 2**; van `NormalizeSort` (whitelist rong) de gia tri la
      khong lot vao `Uri`/cache key.
- [x] Xoa route debug (`/javsnap/conf`, `/javsnap/raw`, `/javsnap/unpack`, `/javsnap/segdbg`).
- [x] Verify end-to-end tren `127.0.0.1:9118`: home (60 item, 60 poster khac nhau, tp 247) /
      pg-2 (khong trung pg-1) / genre / censored / search / vidosik / DDS / STP / LLV.

## Bay da gap (quan trong)

- **Poster ra anh default-cover**: the `<img>` cua site co
  `onerror="this.src='/assets/client/img/default-cover.jpg'"`. Regex `<img\s+[^>]*src=` voi
  `[^>]*` **tham lam** nen khop vao `src` trong `onerror` → moi phim deu ra anh mac dinh.
  Phai: uu tien anh trong `item__cover`, dung `src` **dau tien** (`(?<![\w-])src` de khong
  dinh `data-src`), va bo qua `default-cover`.
- **Uncensored tra rong**: slug phim uncensored co them hau to
  (`dv-563-mosaic-6tKYGKCfzFD`) trong khi regex cu `^[a-z0-9]+-\d+-[a-z0-9]+$` doi hoi dung
  3 segment → loai sach. Doi thanh `^[a-z0-9]+-\d+-[a-z0-9-]+$`.
- **`httpHydra.GetSpan` tra rong voi `/en/uncensored`** (600 KB, 30 item) trong khi
  `httpHydra.Get(statusCodeOK: false)` cung URL tra du body (do 2026-10-26). `FetchTextAsync`
  dung `Get` + `marker` (`data-movie-item`, `category-card`) de retry khi site tra trang chan.
- `ProxyAPI` cua Lampac (`/proxy/…`, dung khi `HostStreamProxy`) goi CDN bang `HttpClient` +
  `Accept-Encoding: br` → CDN `*.tnmr.org` cua luluvid tra **403** (146 byte trang nginx),
  trong khi `Http.GetSpan`/`BaseGetReaderAsync` cua Lampac lai 200. Vi vay segment/key HLS phai
  di qua route rieng `/javsnap/seg?u=` (stream bang `Http.BaseGetReaderAsync`).
- CDN cung tra 403 neu gui `Accept-Language` hoac `Origin`/`Sec-Fetch-*` →
  header stream chi nen la `User-Agent` + `Referer` + `Accept: */*`.
- Proxy Oracle (168.107.66.134:3128) chap chon: CONNECT luc duoc luc khong, co luc tra 200 nhung
  body chi 5 KB (trang chan) → module fail ngau nhien. Site nay **truy cap truc tiep duoc**,
  nen de `useproxy=false` la on dinh nhat (va con dung yeu cau token gan IP).
- Site chan theo **TLS fingerprint + xac suat** (~30% request bi reset `SSL_connect` voi
  curl/curl_cffi `chrome124`) → module retry trong 12–15 s la qua.
- **Trang chu bi timeout (Lampa)**: ban dau tron 2 danh muc + 3 trang taxonomy deu goi
  **tuan tu** → xau nhat 12s x 2 + 12s x 3 = 60s. Da sua thanh **song song + deadline 8s**
  (`Task.WhenAll`), va menu nghi 2 phut sau khi taxonomy fail (`_taxFailAt`) de khong goi lai
  3 request cho moi request. Do lai sau restart: cold 3.3s, warm 33ms.
  Cache list trang chu de 25 phut (search/danh muc 10 phut), menu 12 gio (`javsnap:menu:v3`).


## Cong thuc stream (da test that)

### VIP — `vid-snap-player.com/embed/<uuid>` (HLS, 1080p)

```
GET https://vid-snap-player.com/api/playlist-token/<uuid>   Accept: application/json
  -> {"t":"<base64 token>"}          # token gan IP + TTL 1h
GET https://vid-snap-player.com/v/<t>                        (302)
  -> https://o-xxxx.vid-snap-player.com/u/<ts>/<nonce>/202610/<uuid>/playlist.sb
     content-type: application/vnd.apple.mpegurl
     #EXTM3U -> #EXT-X-STREAM-INF 1080p -> "1080p/video.sb"
     media playlist: segment "video0.png" (content-type video/mp2t)
```

Header bat buoc khi lay CDN: `Referer: https://vid-snap-player.com/`, UA Chrome. **Khong** gui
`Origin`/`Sec-Fetch-*`/`Accept-Language` (CDN khac trong cung site se tra 403). Luu y: HEAD bi 404,
phai GET. Base de rewrite playlist la URL **sau** 302 (proxy cua Lampac lay base
truoc 302 nen sai duong dan → module tu viet lai playlist qua route `/javsnap/hls?u=`, segment qua
`/javsnap/seg?u=`).

### LLV — `luluvid.xyz/e/<id>` (HLS, P.A.C.K.E.R)

Trang embed chua `eval(function(p,a,c,k,e,d){…}('<p>',<a>,<c>,'<k>'.split('|')))`.
Giai bang cach decode base-a tung token roi thay bang `k[idx]` (one-pass, khong replace tuan tu).
Ket qua la `jwplayer("vplayer").setup({sources:[{file:"https://<cdn>/hls2/…/master.m3u8?t=…&e=28800…"}]})`.
Token trong URL gan IP luc mo embed → phai fetch embed va stream cung mot IP.

### DDS — `playmogo.com/e/<id>` (DoodStream clone, mp4)

```
pass_md5/<hash>/<token>   (lay tu HTML embed)
GET https://playmogo.com/pass_md5/<hash>/<token>   Referer: <embed url>
  -> base
final = base + random10(alnum) + "?token=" + <token>
```

### STP — `streamtape.com/e/<id>` (mp4, robotlink)

Giong het cong thuc `SupJav` / `SexTb` / `JavHDToday` (da verify 2026-10-06):

```
trang embed co 2 token:
  <div id="robotlink">/streamtape.com/get_video?id=…&token=<CU></div>      # API tra 500
  script: robotlink').innerHTML = '//streamtape.com/get'+ ('xcd_video?id=…&token=<MOI>')
          .substring(2).substring(1)                                        # -> get_video…
  => chi lay match CUOI (script), ap substring theo thu tu
GET <api>  (Referer: <embed>)  -> 302 https://<n>.tapecontent.net/radosgw/…
     -> 206 video/mp4 (ho tro Range; `/proxy/` cua Lampac van ok vi la mp4, khong bi chan nhu tnmr)
```

## Van hanh

- Conf module nam o `~/lampac-run/init.conf` key `"JAVSnap"` (current.conf la file output,
  sua o do se bi ghi de moi lan khoi dong).
- `useproxy` + `useproxystream` + `proxy.list` de di qua proxy khi IP bi site chan
  (proxy Oracle trong skill `lampac-proxy`). Tat ca request phai di cung mot IP vi token gan IP.
- Build nhanh: `~/lampac-native/csc/check.sh ~/lampac-clean/Modules/Adult/JAVSnap`
