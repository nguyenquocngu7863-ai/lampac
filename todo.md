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
| Đã có popup | 18 | xong |
| Có genres, **thiếu rel** | 5 | Phase 0 |
| JAV / Á – Việt, chưa làm | 32 | Phase 1 |
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
- [ ] `Manko` — https://manko.fun
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
- [ ] `Eporner` — https://www.eporner.com
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

## Log tiến độ

| ngày | module | việc | ghi chú |
|---|---|---|---|
| 2026-11-06 | VietSexBlog | fix domain `x.vietsex.blog` → `vietsexhd.blog` + popup (Thể loại/Từ khóa + Liên quan) | regex card đổi sang host-linh hoạt |
| 2026-11-06 | XHeoVL | popup (Thể loại/Tag + Liên quan) | rel parse card `vb-mini` |
| 2026-11-06 | DuJav | popup (Diễn viên/Hãng/Thể loại + Liên quan) | bài học `total_pages: 1` đã ghi vào skill |
