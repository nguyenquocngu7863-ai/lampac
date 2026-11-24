# Nguồn gốc module Adult (gốc upstream vs tự làm)

So sánh với repo upstream **`lampac-nextgen/lampac`** — HEAD `7559b2c` (2026-11-24),
clone nông `--depth 1 --filter=blob:none` rồi `diff -r` từng module.

| nhóm | số module | đang bật | có popup (genres) |
|---|---|---|---|
| **gốc upstream** | 14 | 13 | 2 |
| **tự làm** | 65 | 62 | 52 |
| **tổng** | 79 | 75 | 54 |

## 1. Module gốc upstream (14) — có trong `lampac-nextgen/lampac`, mình chỉ sửa thêm

`dòng khác` = số dòng khác upstream hiện tại (`diff -r | grep '^[<>]'`).

| module | route | bật | dòng khác | popup | ghi chú |
|---|---|---|---|---|---|
| `BongaCams` | `bgs` | on | 180 | — | sửa vừa |
| `Chaturbate` | `chu` | on | 214 | — | **sửa nhiều** |
| `Ebalovo` | `elo` | on | 264 | — | **sửa nhiều** |
| `Eporner` | `epr` | on | 488 | ✅ | **sửa nhiều** |
| `HQporner` | `hqr` | on | 18 | — | sửa nhẹ |
| `PornHub` | `phub` | on | 290 | — | **sửa nhiều** |
| `Porntrex` | `ptx` | on | 22 | — | sửa nhẹ |
| `Runetki` | `runetki` | on | 179 | — | sửa vừa |
| `Spankbang` | `sbg` | on | 359 | ✅ | **sửa nhiều** |
| `Tizam` | `tizam` | on | 10 | — | sửa nhẹ |
| `Xhamster` | `xmr` | on | 363 | — | **sửa nhiều** |
| `Xnxx` | `xnx` | on | 4 | — | sửa nhẹ |
| `Xvideos` | `xds` | on | 101 | — | sửa vừa |
| `XvideosRED` | `xdsred` | **off** | 24 | — | sửa nhẹ |

> Upstream không đổi gì ở 14 module này khi mình thêm popup/harden — nghĩa là **sync upstream
> sẽ đụng** các file đã sửa. Khi merge: giữ phần của mình ở `Controller.cs` (route `genres`/`rel`),
> lấy phần upstream ở parser/Service nếu nó sửa site.

## 2. Module tự làm (65) — upstream KHÔNG có

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
| `JAVSnap` | `javsnap` | **off** | ⚠️ genres, thiếu `?rel=` | 2026-10-26 | nguyenquocngu7863-ai |
| `JavSub` | `javsub` | on | ✅ | 2026-10-09 | nguyenquocngu7863-ai |
| `Javtiful` | `javtiful` | on | ✅ | 2026-09-22 | nguyenquocngu7863-ai |
| `JavTsunami` | `javtsunami` | **off** | ⚠️ genres, thiếu `?rel=` | 2026-09-23 | nguyenquocngu7863-ai |
| `KawaiJAV` | `kawaijav` | on | ✅ | 2026-10-25 | nguyenquocngu7863-ai |
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
| `SevenMM` | `sevenmm` | on | ⚠️ genres, thiếu `?rel=` | 2026-10-17 | nguyenquocngu7863-ai |
| `SexDep` | `sexdep` | on | — | 2026-09-20 | nguyenquocngu7863-ai |
| `Sexgenz2` | `sgz2` | on | — | 2026-10-24 | nguyenquocngu7863-ai |
| `SexHihi` | `sexhihi` | on | ⚠️ genres, thiếu `?rel=` | 2026-10-26 | nguyenquocngu7863-ai |
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

## 3. Khác biệt ngoài Adult

| đường | trạng thái |
|---|---|
| `Modules/DevFetch`, `Modules/OnlineVN`, `Modules/Tg-notify.bot` | **tự làm** (upstream không có) |
| `Modules/NativeAppleTV`, `Modules/SeriesNotify` | **upstream có, repo mình không có** |
| `Modules/Adult/*` 14 module ở mục 1, `Core/`, `Shared/`, `SISI/` | dùng chung upstream, sửa tại chỗ |

## 4. Việc phát hiện khi soi

- `SevenMM`, `SexHihi`: có route `genres` nhưng **không có `?rel=`** → popup hiện nhưng mục
  "Liên quan" thiếu. Sửa 2 module này là rẻ nhất trong nhóm popup.
- 21 module đang bật **chưa có popup**: 11 gốc upstream (`BongaCams`, `Chaturbate`, `Ebalovo`,
  `HQporner`, `PornHub`, `Porntrex`, `Runetki`, `Tizam`, `Xhamster`, `Xnxx`, `Xvideos`) +
  10 tự làm (`Lon69`, `PhimSexVN88`, `PhimSexViet113`, `SexDep`, `Sexgenz2`, `Sieusex`,
  `SupJav` PAUSED, `TangThon`, `TopGai`, `Viet69Av`).
- `XvideosRED` đang tắt (`enable: false`) — module gốc.
- Số trong `todo.md` cũ (52 đủ popup) chỉ đúng phần `?rel=`; đếm lại: **54 có `genres`**, 52 có cả `rel`.

## 5. Chạy lại phép so sánh

```sh
TMP="${TMPDIR:-$PREFIX/tmp}"; rm -rf "$TMP/up-lampac"
git clone --depth 1 --filter=blob:none --no-checkout \
  https://github.com/lampac-nextgen/lampac.git "$TMP/up-lampac"
cd "$TMP/up-lampac" && git sparse-checkout init --cone && \
  git sparse-checkout set Modules/Adult && git checkout
cd ~/lampac-clean && diff -r "$TMP/up-lampac/Modules/Adult/Xnxx" Modules/Adult/Xnxx
```
