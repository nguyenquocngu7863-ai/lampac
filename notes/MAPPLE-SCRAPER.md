# Mapple TV — scraper: khảo sát đầy đủ (2026-08-31) — **DỰ ÁN ĐÓNG, 2026-09-01**

> **Lý do đóng (nói thẳng, không vòng):** mọi đường vào Mapple đều đi qua endpoint giải mã của bên
> thứ ba `enc-dec.app/api/enc-mapple`, và endpoint đó **404** (bảng mục 1). Không phải selector sai,
> không phải thiếu header, không có chỗ nào để vá trong Lampac — người phải sửa là chủ `enc-dec.app`.
> Cùng kiểu gãy với 3 provider CSX phụ thuộc `enc-dec.app` (ghi ở `notes/CINESTREAM-CSX.md` mục 6):
> service bên thứ ba bỏ endpoint là provider chết, không tự phục hồi.
>
> File này **được commit có ý thức** từ 2026-09-01, làm hồ sơ "đã khảo sát đến đâu và vì sao dừng",
> để nếu ai đó (kể cả em) thấy Mapple xuất hiện lại thì đọc trước khi bỏ một buổi nữa vào nó.
> Mở lại chỉ khi: hoặc `enc-dec.app/api/enc-mapple` sống lại, hoặc tìm được nguồn key/flow tự giải mã.
>
> Ghi chú cũ (vẫn đúng): mọi thứ ghi ngoài `/home/user/lampac` bị xoá giữa các phiên — đó là lý do
> note nằm trong repo chứ không nằm ngoài.

## 1. Kết luận nhanh

| Nguồn | Flow | Hôm nay còn chạy? |
|---|---|---|
| **CSX** (SaurabhKaperwan) `CineStream/invokeMapple` | `mapple.uk` + `/api/stream-token` + `/api/stream?apikey=mptv_sk_a8f29c4e7b3d1f` | ❌ **0%** — route đã 404, code bị xoá 2026-07-02 |
| **chrome-controller** (PanPanBoom) `MappleTVSource.js` | `mapple.tv` + `/api/playback-init` → PoW → `/api/encrypt` → `/api/stream-encrypted` | ✅ **flow duy nhất khớp API hiện tại** |
| **Aniyomi** (yuzono/anime-extensions) `Mapple.kt` | `enc-dec.app/api/enc-mapple` → Next.js server action (`Next-Action`) | ❌ **0%** — `enc-mapple` trả 404 Not-Found |
| **nuvio-providers** (michat88) `providers/mapple.js` | **giống hệt Aniyomi** (`enc-dec.app/api/enc-mapple`) | ❌ **0%** — chết cùng nguyên nhân |

⇒ 4 cái thì 3 chết. **Đừng** dựa vào nuvio-providers.

## 2. nuvio-providers: có JS, nhưng repo không "hot"

- Manifest có thiệt: `{"id":"mapple", "filename":"providers/mapple.js"}` — **481 dòng**, đọc được toàn bộ.
- `const API_BASE = "https://enc-dec.app/api"` + `getSessionId()` gọi `GET ${API_BASE}/enc-mapple` → **endpoint này mình đo trưa 2026-08-31 trả `{"status":404,"result":"Not-Found"}`** ⇒ provider này không lấy được session, fail ngay bước 1.
- `const SOURCES = ["mapple","sakura","alfa","oak","wiggles"]` — **trùng 100%** danh sách hoster của bản Aniyomi ⇒ cùng tác giả `tapframe`, người cũng là chủ `enc-dec.app`. Đây là lý do nó chết theo.
- Nó **không tự giải PoW** — delegt toàn bộ token/session cho enc-dec.app (bên thứ 3). Site đổi API ⇒ phụ thuộc chết theo, không tự phục hồi.
- Sức khoẻ repo: ★11, fork 4, **watcher 1**, contributors `tapframe 205 / michat88 21` (repo chủ yếu là mirror của tapframe), **open issues 0**.
- Nhịp commit theo tháng: `2025-10: 56`, `2025-11: 14`, `2025-12: 30`, **rồi dừng hẳn**.
- Commit cuối repo: **2025-12-30** (~8 tháng). Commit cuối của riêng `providers/mapple.js`: **2025-11-10** (~9.5 tháng, message "update mapple").
- 0 issue mở **không** phải dấu hiệu tốt ở đây — repo chết nên không còn ai báo lỗi.

## 3. Cả hệ sinh thái Nuvio: không có Mapple

| Repo | ★ | pushed | Có mapple? |
|---|---|---|---|
| NuvioMedia/NuvioMobile | 3220 | **2026-08-31** | ❌ |
| NuvioMedia/NuvioTV | 2500 | **2026-08-31** | ❌ |
| NuvioMedia/NuvioDesktop | 1315 | 2026-08-30 | ❌ |
| NuvioMedia/NuvioTVSmart | 632 | 2026-08-30 | ❌ |
| `org:NuvioMedia mapple` (code search) | — | — | **total = 0** |
| tapframe/NuvioStreamsAddon | 341 | 2026-02-07 | ❌ (4khdhub, moviebox, showbox, VidZee, moviesdrive…) |
| yoruix/nuvio-providers | 215 | 2026-06-07 (README) — commit cuối **2026-02-12** | ❌ `providers/` chỉ có `_template.js`; `src/` = allmovieland, animepahe, anizone, cinemacity, dooflix… |

⇒ Nuvio **hot thật**, nhưng hot ở **app client**; provider Mapple thì không ai trong hệ đó维护.

## 4. Bằng chứng "ai đang dùng flow mới" — code search GitHub (chỉ index default branch)

| Query | total | Repo |
|---|---|---|
| `"stream-encrypted" api/encrypt` | 3 file / **2 repo** | `SaugatXthaa/PhoeniX` (StellarRip, commit **hôm nay**) · `PanPanBoom/chrome-controller` |
| `"mapple.tv/api/playback-init"` | 1 | chrome-controller |
| `"mapple.uk/api/stream-token"` | **0** | không ai còn dùng flow CSX |
| `"mapple.tv"` | 3 | chrome-controller + **`fmhy/edit docs/video.md`** + Madzman0/ResouraX (danh bạ FMHY) |

Hai điểm đáng chú ý:
- **FMHY list `mapple.tv` trong `docs/video.md`** ⇒ site được cộng đồng lớn công nhận **còn sống**.
- `chrome-controller` ★1 và `PhoeniX` ★1 — **cả 2 bản còn sống đều là repo 1 sao của 1 tác giả**, không phải "cộng đồng". Đây là rủi ro lớn nhất của plan.

## 5. Mapple.tv ≡ Stellar.rip (cùng backend) — nên lấy thông số từ bản mới nhất

| id | StellarRip | có trong list Mapple TV? |
|---|---|---|
| `s2` | Rigel (4K confirmed, multi-audio) | ✅ |
| `s25` | Vega (4K confirmed) | ✅ |
| `s19` | Betelgeuse (4K confirmed, multi-audio) | ✅ |
| `s13` | Arcturus (4K possible) | ✅ |
| `s26` `s27` `s0` `s4` | Capella, Canopus, Sirius, Procyon | ❌ (bản Mapple TV chưa biết) |

StellarRip ghi: *"the full 19-server list includes many **slow/dead** servers"* → họ cắt còn 8, `concurrency: 8`, timeout tổng 25s, retry backoff cho 403 transient, **một keep-alive agent duy nhất** ("IP-bound tokens, no IP rotation"), CDN trả **CF 1010** nếu UA không phải trình duyệt.

Xác suất có ≥1 stream = `1-(1-p)^n`: 5 server @p=0.5 → 96.9%; 8 server @p=0.5 → 99.6%.
⇒ **số server thử song song là biến số lớn nhất**, không phải flow.

## 6. Probe route (Next.js: 404 có nội dung = route chết; HTML shell rỗng = route sống)

| Probe | Kết quả |
|---|---|
| `mapple.tv/` | 200 — "Mapple — Stream Movies, TV, Anime & More" |
| `mapple.tv/api/stream-token` | **trang 404** ⇒ flow CSX chết |
| `mapple.tv/api/playback-init` | shell rỗng ⇒ **sống** |
| `mapple.tv/api/encrypt` | shell rỗng ⇒ **sống** |
| `mapple.uk/api/playback-init` | shell rỗng ⇒ `.uk` vẫn sống, đã lên API mới (dùng làm fallback domain) |
| `enc-dec.app/api/enc-mapple` | `{"status":404,"result":"Not-Found"}` ⇒ **bị gỡ có chủ đích** |
| `enc-dec.app/api/enc-hexa` | `{"status":500,"error":"oop! too many requests"}` ⇒ enc-dec **vẫn chạy** ⇒ Mapple bị bỏ, không phải sập |

## 7. Kế hoạch port sang Lampac (`Modules/OnlineENG/Mapple4K` nếu hồi sinh)

Flow: ① GET `<host>/watch/{movie|tv}/<tmdbId>[/<s>-<e>]` → regex `window\.__REQUEST_TOKEN__="…"` + giữ cookie `set-cookie`
② POST `/api/playback-init` `{mediaId, mediaType, requestToken, tv_slug}` → `{token}` hoặc `{pow:{challengeId,challenge,difficulty}}`
③ PoW: `SHA256(challenge+nonce)` có `difficulty` bit đầu = 0 (cap ~10M nonce) → POST lại kèm `pow`
④ POST `/api/encrypt` `{data:{mediaId,mediaType,tv_slug,source}, endpoint:"stream-encrypted", requestToken}` → `{url}`
⑤ GET `<host><url>&requestToken&…&token=…` → `data.stream_url` (m3u8) → về qua `/proxy/` của Lampac.

Khác biệt so với bản CSX: **không còn `apikey=mptv_sk_…`**, thêm bước `encrypt`, tên server đổi (tree/cây → s-number).

Yêu cầu khi viết module:
- `enable: false` mặc định; `host[]`, `sources[]`, `timeoutMs`, `powMaxNonce` đưa vào `init.conf` để hot-fix không build lại.
- PoW chạy `Task.Run` + cache token theo `mediaId` (TTL ngắn), **không** block request thread.
- `streamproxy: true` để stream đi đúng IP vừa resolve (token bind IP — chính là bài học 85po ở `experiments/README.md`).
- Set `User-Agent` trong `headers_stream` (CDN chặn UA lạ = CF 1010).
- Song song ≥8 server, tổng timeout 25s, retry backoff cho 403 transient.

**License**: CSX GPLv3 · michat88 GPLv3 · yoruix GPLv3 · chrome-controller **không license** · PhoeniX **không license**. Lampac MIT ⇒ chỉ lấy **công thức/flow (facts)**, tự viết code, không copy dòng nào. Repo không license = mặc định "all rights reserved", càng không được copy.

## 8. File đã trích (mất khi ở ngoài repo → cần thì lấy lại từ lệnh dưới)

```bash
# chrome-controller (flow đang sống, 2026-08)
gh api "repos/PanPanBoom/chrome-controller/contents/chrome_controller_server/src/source/MappleTVSource.js" --jq .content | base64 -d
# CSX invokeMapple + solvePowChallenge (bản đầy đủ nhất của PoW)
cd <CSX clone> && git show eaea08ea:CineStream/src/main/kotlin/com/megix/CineStreamExtractors.kt | sed -n '864,968p'
# nuvio-providers mapple.js (481 dòng — chỉ để xem, đừng dùng)
gh api "repos/michat88/nuvio-providers/contents/providers/mapple.js" --jq .content | base64 -d
# StellarRip (thông số vận hành)
gh api "repos/SaugatXthaa/PhoeniX/contents/src/source/StellarRip.js" --jq .content | base64 -d
```

---

# Phụ lục: hệ Nuvio — link nào test được, link nào rỗng (2026-08-31)

| Manifest để dán vào Nuvio → Settings → Plugins | Kết quả thực tế |
|---|---|
| `https://raw.githubusercontent.com/yoruix/nuvio-providers/refs/heads/main/manifest.json` | ❌ **rỗng**: 1 entry `template-provider` → `providers/template.js` **không tồn tại** (repo chỉ có `providers/_template.js`). Repo là skeleton/template; `src/` có 16 provider chưa build (allmovieland, animepahe, anizone, cinemacity, dooflix, hdhub4u, hianime, kurage, movieblast, moviebox, moviesdrive, moviesmod, mycima, netmirror, reanime, uhdmovies) — **không có mapple**. Commit cuối 2026-02-12. |
| `https://raw.githubusercontent.com/michat88/nuvio-providers/refs/heads/main/manifest.json` | ✅ Load được **30 provider** (kisskh, 4khdhub, animekai, castle, cinevibe, dahmermovies, dvdplay, hdhub4u, hdrezka, idlix, mallumv, **mapple**, moviebox, moviesmod, myflixer-extractor, netmirror, showbox, streamflix, uhdmovies, videasy, vidlink, vidnest-anime, vidnest, vidrock, vidsrc, vixsrc, watch32, xprime, yflix, adimoviebox) — nhưng `mapple` chết vì `enc-dec.app/api/enc-mapple` = 404 |
| `https://raw.githubusercontent.com/tapframe/NuvioStreamsAddon/refs/heads/main/manifest.json` | ⚠️ Đây là **Stremio addon manifest** (`id: org.nuvio.streams`, v0.5.17), không phải manifest provider của Nuvio → dán vào Plugins sẽ không ra gì |

**Contract 1 provider của Nuvio** (từ `providers/_template.js`, build bằng `node build.js [name]` qua esbuild):
```js
module.exports = { getStreams };   // getStreams(id, type, season, episode) -> Promise<Stream[]>
require('cheerio-without-node-native');  // module do app cung cấp
```

**Test lẻ 1 provider**: Nuvio → Settings → Developer → **Plugin Tester** → dán thẳng URL file `.js`,
ví dụ `https://raw.githubusercontent.com/michat88/nuvio-providers/refs/heads/main/providers/mapple.js`.
Chuỗi lỗi cần tìm (có sẵn trong source, để đối chiếu):
`Session ID API error: 404 …` hoặc `Invalid session ID response format` ⇒ khớp chẩn đoán "enc-mapple đã bị gỡ".

**Control group**: bật thêm `videasy` hoặc `vidlink` (cùng manifest) — nếu 2 cái đó ra stream mà mapple không
⇒ framework Nuvio OK, lỗi là ở provider Mapple, không phải ở app.
