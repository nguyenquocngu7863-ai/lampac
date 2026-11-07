# TODO — Popup (long-press) cho module Adult

Mục tiêu: mỗi site có **route `/<mod>/genres?uri=`** (chip thông tin) + **mục "Liên quan" trỏ `?rel=<uri>`**.
Skill tham chiếu: `lampac-popup` (hợp đồng route, luật UX, anti-pattern) + `lampac-adult-module`.

## Định nghĩa "xong" cho 1 module

- [ ] `/genres?uri=` trả `{items:[],groups:[...]}`, nhóm rỗng bị bỏ, **không 500** (uri lạ → rỗng)
- [ ] Chip lấy **1 lần fetch detail**, có cache (6h) + warm nền cho related
- [ ] `?rel=<uri>` trả `total_pages: 1` (KHÔNG phải `0` — xem anti-pattern DuJav), `pg>1` → `{total_pages=1, list=[]}`
- [ ] Chặn SSRF: `uri` phải thuộc host site (NormalizePageUrl)
- [ ] `check.sh <module>` 0 error → copy 4 file sang `~/lampac-run/module/Adult/<Mod>/` → restart
- [ ] Smoke: home · pg2 · search · category · genres · rel
- [ ] Stream thật: `~/lampac-native/play.sh '<route video>'` → khối **SMOKE = PASS** (ffmpeg decode 5s đầu/cuối qua route Lampac; tự chạy với URL Lampac). `--no-smoke` khi cần nhanh
- [ ] (Nếu site đổi domain trong lúc làm) cập nhật `SiteHost` + manifest + README
- [ ] Dọn rác build (`lampac-cleanup` §2) trước khi bàn giao

## Trạng thái tổng

| nhóm | số module | trạng thái |
|---|---|---|
| Đã có popup | 19 | xong |
| Có genres, **thiếu rel** | 5 | Phase 0 |
| JAV / Á – Việt, chưa làm | 31 | Phase 1 |
| Tube phương Tây, chưa làm | 9 | Phase 2 |
| Cam live, chưa làm | 3 | Phase 3 |
| Đang tắt | 3 | bỏ qua |

---

## Phase 0 — Bù mục "Liên quan" (`?rel=`) cho 5 module đã có genres

- [ ] `Av123` — https://123av.com
- [ ] `JAVSnap` — https://javsnap.com
- [ ] `JavHDToday` — https://javhdz.today
- [ ] `Po85` — https://www.85po.com
- [ ] `XNhau` — https://xnhau.pink

## Phase 1 — JAV / Á – Việt (ưu tiên, có trang detail + vidosik)

- [ ] `HeoVl` — https://heovl.im
- [ ] `Jable` — https://en.jable.tv
- [ ] `JavMoi` — https://z.javmoi.blog
- [ ] `JavSub` — https://javsub.blog
- [ ] `KawaiJAV` — https://kawai-jav.com
- [ ] `Lon69` — https://lon69.site
- [x] `Manko` — https://manko.fun
- [ ] `Mbbg` — https://mbbg.hair
- [ ] `Phe69` — https://phe69.shop
- [ ] `PhimSexVN88` — https://phimsexvn88.com
- [ ] `PhimSexVi` — https://kidayteknoloji.com
- [ ] `PhimSexViet113` — https://sexvnonline.net
- [ ] `PubJav` — https://pubjav.com *(đã có popup nguồn phát ở vidosik — chỉ thêm chip + rel)*
- [ ] `SevenMM` — https://7mmtv.sx
- [ ] `SexDep` — https://z.sexdep.co.uk
- [ ] `SexHihi` — https://sexhihih.blog
- [ ] `SexVietDam` — https://sexvietdam.blog
- [ ] `Sexgenz2` — https://x.sexgenz.blog
- [ ] `Sieusex` — https://biphim.net
- [ ] `SupJav` — https://supjav.com *(PAUSED: Cloudflare chặn — chỉ probe khi user xác nhận)*
- [ ] `TangThon` — https://tangthon.com
- [ ] `Tazzly` — https://singapore-packaging.com
- [ ] `Tizam` — https://amu.tizam.org
- [ ] `TopGai` — https://topgai.net
- [ ] `Topsex` — https://s1.topsex.blog
- [ ] `VNsexTop1` — https://vnsextop1.com
- [ ] `Viet69Av` — https://viet69av.com
- [ ] `Viet69z` — https://viet69z.to
- [ ] `Vlxx` — https://vlxx.phd
- [ ] `XViet` — https://xviet.live
- [ ] `XszAv` — https://xsz-av.com
- [ ] `Vjav` — https://vjav.com

## Phase 2 — Tube phương Tây (chip = thể loại / pornstar / channel / tag)

- [ ] `Ebalovo` — https://www.ebalovo.pro
- [x] `Eporner` — https://www.eporner.com *(popup: thể loại `?c=` + thẻ `?t=` (trang `/tag/`) + video liên quan `?rel=`; detail page không có pornstar/channel. Sort: home 4, cat `SORT-*` 4, tag 5 — `shortest`(cat)/`top-monthly`(cat+tag)/search = no-op nên bỏ; bằng chứng ở skill lampac-sort cases.md)*
- [ ] `HQporner` — https://m.hqporner.com
- [ ] `PornHub` — https://rt.pornhub.com
- [ ] `Porntrex` — https://www.porntrex.com
- [ ] `Spankbang` — https://ru.spankbang.com
- [ ] `Xhamster` — https://xhamster.com
- [ ] `Xnxx` — https://www.xnxx-ru.com
- [ ] `Xvideos` — https://www.xvideos.com

## Phase 3 — Cam live (không có detail kiểu phim — cần thiết kế popup riêng)

- [ ] `BongaCams` — https://ee.bongacams.com *(gợi ý: chip = quốc gia/ngôn ngữ, "Liên quan" = model cùng tag)*
- [ ] `Chaturbate` — https://ru.chaturbate.com *(như trên)*
- [ ] `Runetki` — https://rus.runetki5.com *(như trên)*

## Bỏ qua (đang tắt)

`JavTsunami` · `Viet69kz` · `XvideosRED`

---

## Phase 4 — Việc đang treo (2026-11-07)

### 4a. Av123 → chuyển sang bản tiếng Việt `/vi` — ✅ XONG (2026-11-07)

**Bối cảnh**: 123av.com có bản web tự cung cấp `/vi` (tiếng Việt). Module `Av123`
bám `/en/` → chuyển sang `/vi` thì menu tự ra tiếng Việt, **khỏi viết bảng dịch**.

- [x] Đo lại DNS: đã nhả (`123av.com` → `104.21.93.106`), fetch được, không cần chờ wifi khác
- [x] Capture + so `/vi` với `/en`: **slug thể loại trùng 508/508**, markup card y hệt
      (`card__cover`/`card__link`/`card__dur`), maker `/vi` dùng `mcard`. Chỉ khác prefix.
- [x] Thêm `public const string Lang = "/vi";` (`Service.cs`) và thay **toàn bộ** đường dẫn
      sang `Lang` (regex card/vside/chip/taxo, MenuUrl, taxonomy prefix, BuildUrl, search,
      `/vidosik`, warm). `grep '"/en'` = 0.
- [x] Nhãn menu **lấy nguyên văn từ web `/vi`** (không tự dịch):
      `Ngày phát hành` / `Mới thêm gần đây` / `Nổi bật` / `Lượt xem hôm nay|tuần|tháng` /
      `Xem nhiều nhất` / `Theo dõi nhiều nhất` / `Dài nhất`;
      nhóm: `Mới` `Nổi bật` `Gần đây` · `Xu hướng` · `Loại` (Kiểm duyệt/Không kiểm duyệt/
      Rò rỉ không kiểm duyệt) · `Nghiệp dư` · `Không kiểm duyệt` · `Nhà sản xuất`.
- [x] `HasCjk()`: web `/vi` vẫn để **45 mục** tiếng Nhật (10 thể loại = tên đợt sale,
      35 maker lẻ) → ẩn khỏi menu/popup để đúng luật "menu chỉ Việt/Anh".
      Menu còn **962 mục**, quét CJK/Cyrillic/Thái/Hàn = **0**.
- [x] `taxo.json` giờ lưu kèm `lang`; load thấy `lang` khác `Av123To.Lang` → bỏ
      (đổi ngôn ngữ route là mất cache tên cũ).
- [x] Bump cache key `av123:v2` → `av123:v3` (playlist cũ chứa đường dẫn `/en`).
- [x] Fix phát hiện khi smoke: stream trả `{"Auto": ".../av123/hls?..."}` **thiếu đuôi
      `.m3u8`** → app coi là file thường. Thêm `[Route("av123/hls.m3u8")]` + đổi URL trả về
      (`Auto`, `HlsUrl`) sang `.m3u8` → `play.sh` **PHÁT ĐƯỢC** (h264 1280x720).
- [x] Smoke 9 endpoint (home, `/vi/new` pg2, thể loại, maker, search, search pg2, sort views,
      `/vi/uncensored`, popup + `rel`), tất cả trả `/vi` + HTTP 200; popup Hãng/Thể loại/Liên quan OK.

> **Bẫy đã dính (ghi lại)**: đổi code mà `/av123` vẫn ra tiếng Anh + `/en/` → KHÔNG phải
> `taxo.json` (cái đó chỉ ảnh hưởng tên thể loại). Thủ phạm là **cache file
> `~/lampac-run/cache/fdb/`** (`HybridFileCache`, gzip, **sống qua restart**), key
> `av123:v2:*`. Muốn xoá sạch: xoá file trong `cache/fdb` mà `zcat` chứa `av123/`
> rồi restart. Vì vậy lần này bump key lên `v3`.

### 4b. Menu không phải Việt/Anh (vi phạm luật mới)

Luật đã ghi vào `AGENTS.md` (LUẬT CỨNG) + skill `lampac-adult-module/references/menu-sisi.md` §9g.

- [ ] **XvideosRED** — 36 mục thể loại **tiếng Nga** (`Азиат`, `Анал`, `Соло`,
      `Тинейджеры`, `Фистинг`…) cần dịch sang Việt/Anh
- [ ] **Spankbang** — 2 mục sort tiếng Nga (`Трендовое`, `Популярное`) trộn tiếng Việt
      (`Mới`) → dịch cho đồng nhất
- [ ] **Po85** — nhãn song ngữ VN + chữ Trung kèm (`Tự chụp 自拍`, `Vú to 大奶`…)
      → bỏ phần chữ Hán, giữ tiếng Việt

---

## Log tiến độ

| ngày | module | việc | ghi chú |
|---|---|---|---|
| 2026-11-06 | VietSexBlog | fix domain `x.vietsex.blog` → `vietsexhd.blog` + popup (Thể loại/Từ khóa + Liên quan) | regex card đổi sang host-linh hoạt |
| 2026-11-06 | XHeoVL | popup (Thể loại/Tag + Liên quan) | rel parse card `vb-mini` |
| 2026-11-06 | DuJav | popup (Diễn viên/Hãng/Thể loại + Liên quan) | bài học `total_pages: 1` đã ghi vào skill |
| 2026-11-06 | Manko | popup (Diễn viên/Thể loại/Hãng + Liên quan) + guard SSRF; bỏ dòng "Sắp xếp"; tiêu đề để code trần; list rỗng trả `count:0` | **site KHÔNG có sort**: `sort=/order=/order_by=/most_viewed` bị API bỏ qua (id y hệt), còn `most_popular/top_rated/best` là **filter** (đổi tập phim) → không dựng dòng 2. List API không trả `video_duration` → thumb không có thời lượng (đã cài sẵn chỗ đọc nếu API bổ sung) |
| 2026-11-06 | DuJav (tắt) | tắt tạm thời | `~/lampac-run/init.conf`: `"dujav": {"enable": false}` (backup `init.conf.bak-20261106-1223`; bật lại = xoá key) |
| 2026-11-06 | JavQuick (mới) | module mới `javquick.com`: list/menu/popup + proxy `video.mp4` **tự giải mã AES-128-CTR đa part** | công thức F21 đã ghi vào skill `lampac-deobfuscate`; key hardcode trong `decryptor.wasm`, reset keystream mỗi 6 MB |
| 2026-11-06 | play.py | fix false-negative với input mp4: probe bằng Range + cờ bỏ kiểm duyệt đuôi segment chỉ chèn cho HLS | commit riêng `130db33f` |
| 2026-11-06 | AsianGirl (mới) | module mới `asiangirl.porn`: list/menu/popup (Thể loại + Liên quan) + HLS | home = `/new/` (trang chủ gốc trộn section); search phân trang bằng `from_videos=<offset>`, list bằng `from=<pg>`; card QC cùng class nên lọc theo host; `play.sh` PASS (AES-128) |
| 2026-11-07 | **Jav18 (mới)** | module mới `18jav.tv` (`/jav18`): list/menu/popup/path-route + HLS | KVS: phân trang **AJAX** `?mode=async&function=get_block&block_id=…&from=`, sort thật `release_at`/`views`/`stars` (search thêm `""`); trailing slash bắt buộc; search chỉ dạng `/search/<q>/`; fallback 14 thể loại + 113 tag; `play.sh` **PASS** (h264 720p, không AES). Bug đã sửa: `Uri()` trả path tương đối; race 2 đường gây rate-limit; 404 slug bịa → 503 (nay trả `count:0`); popup parse nhầm tag cloud toàn site (113 tag) → giới hạn vùng meta |
| 2026-11-07 | Jav18 | **dịch menu thể loại/từ khoá Trung → Việt** | bảng `Trans` (125 từ) + `Jav18To.Translate()`, áp cho menu **và** popup `/genres`; tên diễn viên Latin giữ nguyên |
| 2026-11-07 | AGENTS.md + skill | **LUẬT MỚI: menu phải Việt hoặc Anh** | ghi vào `AGENTS.md` mục LUẬT CỨNG + `references/menu-sisi.md` §9g khối "LUẬT CỨNG — NGÔN NGỮ MENU" (kèm lệnh grep CJK/Cyrillic/Thái/Hàn để quét) |
| 2026-11-07 | **Av123 → `/vi`** | chuyển hẳn sang bản tiếng Việt của site (`Lang = "/vi"`), nhãn menu lấy nguyên văn từ web, ẩn 45 mục site để tiếng Nhật, `taxo.json` khoá theo `lang`, bump cache key `v3` | slug thể loại trùng 508/508 nên chỉ đổi prefix; `play.sh` **PHÁT ĐƯỢC** sau khi thêm route `av123/hls.m3u8` (trước đó thiếu đuôi → app báo no supported source) |
| 2026-11-07 | (bài học) | **cache file `~/lampac-run/cache/fdb/`** (`HybridFileCache`) **sống qua restart** | đổi code mà `/av123` vẫn ra `/en/` + tên tiếng Anh; xoá entry có `zcat … | grep av123/` rồi restart mới sạch |

## Treo (Manko)

- [ ] Ẩn "Server 2" (helvid) khỏi danh sách nguồn — web **đã bỏ**, CF Worker của helvid chặn mọi client server-side (đo: UA/Referer/Origin/Sec-Fetch/cookie/`tm=1`, cả IPv4 & IPv6 đều 404; Chrome thật thì 200)
- [ ] Bật fallback API thật: `window.ip2` hiện là **`alpharomeo.shop`**, module đang hardcode `robustx.shop` và **không dùng** → chưa có fallback sống
- [ ] `IsSiteUri("abc")` (id rác) đi hết 2 lần timeout (15s direct + 15s proxy) → popup trả rỗng sau ~30s; muốn fail nhanh thì cắt retry khi API trả `data` rỗng

