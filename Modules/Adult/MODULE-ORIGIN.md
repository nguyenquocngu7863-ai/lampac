# Nguồn gốc module Adult — 2 nguồn upstream: **SISI** và **NextHUB**

> **Ưu tiên:** module **tự làm** trước; 2 nguồn upstream (site Nga, ít dùng) **làm sau, từ từ**.
> **KHÔNG BAO GIỜ sync/merge/pull/rebase với upstream** — xem mục 5.

So trực tiếp với **`lampac-nextgen/lampac`** HEAD `7559b2c` (2026-11-24), clone nông
`--depth 1 --filter=blob:none` + sparse-checkout, `diff -r` (bỏ `bin/`, `obj/`, `*.dll/pdb/so`).

## 0. Hai nguồn adult của upstream

| nguồn | upstream gồm | mình có | đã Việt hoá / sửa |
|---|---|---|---|
| **SISI** | `Modules/Adult` (14 balancer) + `SISI/` (plugin + controller) | 14/14 + `SISI/` | 14/14 module đã sửa; `SISI/plugins` +326 dòng, thêm `sisi-restyle.js` |
| **NextHUB** | `Modules/NextHUB` (57 `sites/*.yaml` + `Root.cs`/controller) | 57/57 yaml | **59 file / 5.049 dòng**, **55/57 yaml** đã Việt hoá, thêm `CategoryVi.cs` |
| **ngoài 2 nguồn** | — | **66 module tự làm** | không có ở upstream |

Tổng `Modules/Adult`: **80 = 14 (SISI) + 66 tự làm**. Bật **76** (13 SISI + 63 tự làm).
(KissJav thêm 2026-11-24, sau lần đếm gốc.)
Popup: **54 module đủ** `genres` + `?rel=`; **22 chưa có popup** (11 SISI + 11 module cũ tự làm).

> Đo thêm: 57 site NextHUB **không trùng domain** với 65 module tự làm (chỉ `rt.pornhub.com`),
> tức 65 module này **không phải port từ NextHUB** mà viết mới.

## 1. Nguồn SISI — `Modules/Adult` gốc upstream (14)

| module | route | bật | dòng khác | popup |
|---|---|---|---|---|
| `Xnxx` | `xnx` | on | 4 | — |
| `Tizam` | `tizam` | on | 10 | — |
| `HQporner` | `hqr` | on | 18 | — |
| `Porntrex` | `ptx` | on | 22 | — |
| `XvideosRED` | `xdsred` | **off** | 24 | — |
| `Xvideos` | `xds` | on | 101 | — |
| `Runetki` | `runetki` | on | 179 | — |
| `BongaCams` | `bgs` | on | 180 | — |
| `Chaturbate` | `chu` | on | 214 | — |
| `Ebalovo` | `elo` | on | 264 | — |
| `PornHub` | `phub` | on | 290 | — |
| `Spankbang` | `sbg` | on | 359 | ✅ |
| `Xhamster` | `xmr` | on | 363 | — |
| `Eporner` | `epr` | on | 488 | ✅ |

`SISI/` (hạ tầng, không phải module):
| đường | khác upstream |
|---|---|
| `SISI/plugins/sisi.js` | 326 dòng (đổi UI menu 2 tầng) |
| `SISI/plugins/sisi-restyle.js` | file riêng, upstream không có |
| `SISI/plugins/startpage.js` | y nguyên |
| `SISI/Controllers` | 2 file / 8 dòng |
| `SISI/Models`, `SISI/SQL` | y nguyên |

⇒ Sửa `SISI/` hoặc 14 module này: **càng phải port tay**, merge upstream là mất menu SISI 2 tầng.

## 2. Nguồn NextHUB — `Modules/NextHUB`

| phần | khác upstream |
|---|---|
| `sites/*.yaml` | **55/57 file** — Việt hoá `menu.categories` (vd `Nga có thuyết minh`, `Hậu môn`, `Cực khoái`) |
| `CategoryVi.cs` | **file riêng** — bảng dịch thể loại + `Translate()` |
| `Root.cs` | đổi menu: `title = "Tìm kiếm"`, `"Sắp xếp: …"` (upstream: `Поиск`, `Сортировка: …`) |
| `Controllers/ListController.cs`, `ViewController.cs` | sửa theo menu tiếng Việt |
| `manifest.json` | sửa (enable/conf) |
| tổng | **59 file / 5.049 dòng** — module bị sửa nhiều nhất trong repo |

⇒ Đây là chỗ merge upstream **phá nhiều nhất**: 55 file yaml + bảng dịch sẽ bị ghi đè.

## 3. 66 module Adult tự làm (không thuộc SISI/NextHUB)

| module | route | bật | popup | ngày thêm | tác giả |
|---|---|---|---|---|---|
| `AnySex` | `anysex` | on | ✅ | 2026-11-06 | nguyenquocngu7863-ai |
| `AsianGirl` | `asiangirl` | on | ✅ | 2026-11-07 | nguyenquocngu7863-ai |
| `Av123` | `av123` | on | ✅ | 2026-09-23 | nguyenquocngu7863-ai |
| `DuJav` | `dujav` | on | ✅ | 2026-10-06 | nguyenquocngu7863-ai |
| `FapClub` | `fapclub` | on | ✅ | 2026-11-22 | nguyenquocngu7863-ai |
| `FreePornVideos` | `fpv` | on | ✅ | 2026-11-21 | nguyenquocngu7863-ai |
| `Gapedk` | `gapedk` | on | ✅ | 2026-11-20 | agent |
| `HeoVl` | `heovl` | on | ✅ | 2026-09-20 | nguyenquocngu7863-ai |
| `Jable` | `jable` | on | ✅ | 2026-10-02 | nguyenquocngu7863-ai |
| `Jav18` | `jav18` | on | ✅ | 2026-11-07 | nguyenquocngu7863-ai |
| `JavCc` | `javcc` | on | ✅ | 2026-11-21 | nguyenquocngu7863-ai |
| `JavCt` | `javct` | on | ✅ | 2026-10-01 | nguyenquocngu7863-ai |
| `JavGuru` | `javguru` | on | ✅ | 2026-09-27 | nguyenquocngu7863-ai |
| `JavHDToday` | `javhdtoday` | on | ✅ | 2026-10-02 | nguyenquocngu7863-ai |
| `JavMit` | `javmit` | on | ✅ | 2026-11-20 | nguyenquocngu7863-ai |
| `JavMoi` | `javmoi` | on | ✅ | 2026-09-29 | nguyenquocngu7863-ai |
| `JavQuick` | `javquick` | on | ✅ | 2026-11-06 | nguyenquocngu7863-ai |
| `JAVSnap` | `javsnap` | **off** | ⚠️ thiếu `?rel=` | 2026-10-26 | nguyenquocngu7863-ai |
| `JavSub` | `javsub` | on | ✅ | 2026-10-09 | nguyenquocngu7863-ai |
| `Javtiful` | `javtiful` | on | ✅ | 2026-09-22 | nguyenquocngu7863-ai |
| `JavTsunami` | `javtsunami` | **off** | — | 2026-09-23 | nguyenquocngu7863-ai |
| `KawaiJAV` | `kawaijav` | on | ✅ | 2026-10-25 | nguyenquocngu7863-ai |
| `KissJav` | `kissjav` | on | ✅ | 2026-11-24 | nguyenquocngu7863-ai |
| `Lon69` | `lon69` | on | — | 2026-10-26 | nguyenquocngu7863-ai |
| `Manko` | `manko` | on | ✅ | 2026-10-14 | opencode |
| `Mbbg` | `mbbg` | on | ✅ | 2026-10-04 | nguyenquocngu7863-ai |
| `MissAV` | `missav` | on | ✅ | 2026-09-22 | nguyenquocngu7863-ai |
| `Phe69` | `phe69` | on | ✅ | 2026-09-17 | nguyenquocngu7863-ai |
| `PhimSexVi` | `phimsexvi` | on | ✅ | 2026-10-25 | nguyenquocngu7863-ai |
| `PhimSexViet113` | `phimsexviet113` | on | — | 2026-10-23 | nguyenquocngu7863-ai |
| `PhimSexVN88` | `phimsexvn88` | on | — | 2026-11-01 | nguyenquocngu7863-ai |
| `Po85` | `po85` | on | ✅ | 2026-09-05 | nguyenquocngu7863-ai |
| `Pornhat` | `pornhat` | on | ✅ | 2026-11-21 | nguyenquocngu7863-ai |
| `PubJav` | `pubjav` | on | ✅ | 2026-09-30 | nguyenquocngu7863-ai |
| `QmhSex` | `qmhsex` | on | ✅ | 2026-11-04 | nguyenquocngu7863-ai |
| `SakuraSins` | `sakurasins` | on | ✅ | 2026-11-07 | nguyenquocngu7863-ai |
| `SevenMM` | `sevenmm` | on | ✅ | 2026-10-17 | nguyenquocngu7863-ai |
| `SexDep` | `sexdep` | on | — | 2026-09-20 | nguyenquocngu7863-ai |
| `Sexgenz2` | `sgz2` | on | — | 2026-10-24 | nguyenquocngu7863-ai |
| `SexHihi` | `sexhihi` | on | — | 2026-10-26 | nguyenquocngu7863-ai |
| `SexNetVc` | `sexnetvc` | on | ✅ | 2026-11-13 | nguyenquocngu7863-ai |
| `SexTb` | `sextb` | on | ✅ | 2026-10-01 | nguyenquocngu7863-ai |
| `SexVietDam` | `sexvietdam` | on | ✅ | 2026-10-03 | nguyenquocngu7863-ai |
| `SexVietop` | `sexvietop` | on | ✅ | 2026-11-13 | nguyenquocngu7863-ai |
| `SexVietTuyChon` | `sexviettuyenchon` | on | ✅ | 2026-11-21 | nguyenquocngu7863-ai |
| `Sieusex` | `sieusex` | on | — | 2026-10-26 | nguyenquocngu7863-ai |
| `Suong3x` | `suong3x` | on | ✅ | 2026-11-05 | nguyenquocngu7863-ai |
| `SupJav` | `supjav` | on | — | 2026-10-05 | nguyenquocngu7863-ai |
| `TangThon` | `tangthon` | on | — | 2026-10-11 | nguyenquocngu7863-ai |
| `TellyHD` | `tellyhd` | on | ✅ | 2026-11-18 | nguyenquocngu7863-ai |
| `ThichSex` | `thichsex` | on | ✅ | 2026-11-14 | arena |
| `TopGai` | `topgai` | on | — | 2026-09-21 | nguyenquocngu7863-ai |
| `Viet69Av` | `viet69av` | on | — | 2026-11-01 | nguyenquocngu7863-ai |
| `Viet69kz` | `viet69kz` | **off** | — | 2026-09-26 | nguyenquocngu7863-ai |
| `Viet69z` | `viet69z` | on | ✅ | 2026-09-21 | nguyenquocngu7863-ai |
| `Vietmup` | `vietmup` | on | ✅ | 2026-11-01 | nguyenquocngu7863-ai |
| `VietSexBlog` | `vietsexblog` | on | ✅ | 2026-09-15 | nguyenquocngu7863-ai |
| `Vjav` | `vjav` | on | ✅ | 2026-10-04 | nguyenquocngu7863-ai |
| `Vlxx` | `vlxx` | on | ✅ | 2026-09-21 | nguyenquocngu7863-ai |
| `VnChich` | `vnchich` | on | ✅ | 2026-11-05 | nguyenquocngu7863-ai |
| `VNJav` | `vnjav` | on | ✅ | 2026-11-13 | nguyenquocngu7863-ai |
| `VNsexTop1` | `vnsextop1` | on | ✅ | 2026-10-11 | nguyenquocngu7863-ai |
| `XHeoVL` | `xheovl` | on | ✅ | 2026-10-26 | nguyenquocngu7863-ai |
| `XNhau` | `xnhau` | on | ✅ | 2026-09-13 | nguyenquocngu7863-ai |
| `XszAv` | `xszav` | on | ✅ | 2026-10-14 | Codex |
| `XViet` | `xviet` | on | ✅ | 2026-11-01 | nguyenquocngu7863-ai |

## 4. Việc phát hiện

- **22 module bật chưa có popup** — 11 nguồn SISI: `BongaCams`, `Chaturbate`, `Ebalovo`, `HQporner`, `PornHub`, `Porntrex`, `Runetki`, `Tizam`, `Xhamster`, `Xnxx`, `Xvideos`.
  11 tự làm: `Lon69`, `PhimSexVN88`, `PhimSexViet113`, `SexDep`, `SexHihi`, `Sexgenz2`, `Sieusex`, `SupJav`, `TangThon`, `TopGai`, `Viet69Av`.
- Đếm bằng route thật (`Route("<x>/genres")` + `string rel`), không grep chữ `genres`:
  grep chữ thường đếm oan `SexHihi`/`JavTsunami` (chỉ có chữ trong menu, **không có route**).
- Đang tắt: `XvideosRED` (SISI), `Viet69kz` (tự làm).
- NextHUB: 57 yaml, 52 enable — cả 57 đều có ở upstream, mình chỉ sửa nội dung.

## 5. LUẬT

- **KHÔNG BAO GIỜ sync/merge/pull/rebase với upstream** (`lampac-nextgen/lampac`).
  Mọi module đã bị sửa khác đi (popup, harden, đổi domain, fix player, Việt hoá menu); merge sẽ
  **ghi đè và mất toàn bộ công sức**. Upstream chỉ để **đọc tham khảo** → `diff -r` → **port tay**.
- 2 nguồn upstream đều là **site Nga, ít dùng** ⇒ xếp **ưu tiên cuối**; nhóm tự làm: **ưu tiên**.

## 6. Chạy lại

```sh
TMP="${TMPDIR:-$PREFIX/tmp}"; rm -rf "$TMP/up-lampac"
git clone --depth 1 --filter=blob:none --no-checkout \
  https://github.com/lampac-nextgen/lampac.git "$TMP/up-lampac"
cd "$TMP/up-lampac" && git sparse-checkout init --cone && \
  git sparse-checkout set Modules/Adult Modules/NextHUB SISI && git checkout
cd ~/lampac-clean
diff -rq -x bin -x obj -x "*.dll" "$TMP/up-lampac/Modules/Adult" Modules/Adult | head
diff -rq -x bin -x obj "$TMP/up-lampac/Modules/NextHUB/sites" Modules/NextHUB/sites | wc -l   # 55
diff -r -x bin -x obj "$TMP/up-lampac/Modules/NextHUB" Modules/NextHUB | grep -c "^[<>]"      # 5049
```
