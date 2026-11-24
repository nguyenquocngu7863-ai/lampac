# Nguồn gốc module & hạ tầng (gốc upstream vs tự làm)

> **Ưu tiên:** nhóm **tự làm** trước; module **gốc upstream** làm sau, từ từ (chủ repo upstream
> bảo trì). **Không sync upstream** — xem luật ở mục 5.

So sánh trực tiếp với repo upstream **`lampac-nextgen/lampac`** — HEAD `7559b2c` (2026-11-24),
clone nông `--depth 1 --filter=blob:none` + sparse-checkout, rồi `diff -r` từng thư mục
(bỏ `bin/`, `obj/`, `*.dll/pdb/so`).

## 0. Tổng quan

| khu vực | gốc upstream | trong đó đã sửa | không đổi | tự làm | upstream có / mình không |
|---|---|---|---|---|---|
| `Modules/Adult` | 14 | 14 | 0 | **65** | 0 |
| `Modules/*` khác | 34 | 25 | 7 | 3 | 2 (`NativeAppleTV`, `SeriesNotify`) |
| `Online/` | 1 | 1 | 0 | 0 | 1 (`Online/tests`) |
| `SISI/` | 4 | 2 | 2 | 0 | 0 |
| `Core/` + `Shared/` | 2 | 2 | 0 | 0 | 0 |
| **tổng module** | **48** | 39 | 9 | **68** | 3 |

> Ghi chú: `Modules/*` khác = 34 thư mục upstream (35 trừ `Adult`); 2 cái upstream mới thêm chưa có
> ở mình → 32 thư mục chung = **25 đã sửa** + **7 nguyên bản**. Còn 3 thư mục mình tự thêm.
> `đã sửa` gồm cả thay đổi nội dung file lẫn file/thư mục chỉ một bên có.

## 1. `Modules/Adult` — gốc upstream (14)

`dòng khác` = số dòng khác upstream (`diff -r | grep '^[<>]'`).

| module | route | bật | dòng khác | popup | ghi chú |
|---|---|---|---|---|---|
| `Xnxx` | `xnx` | on | 4 | — | sửa nhẹ |
| `Tizam` | `tizam` | on | 10 | — | sửa nhẹ |
| `HQporner` | `hqr` | on | 18 | — | sửa nhẹ |
| `Porntrex` | `ptx` | on | 22 | — | sửa nhẹ |
| `XvideosRED` | `xdsred` | **off** | 24 | — | sửa nhẹ |
| `Xvideos` | `xds` | on | 101 | — | sửa vừa |
| `Runetki` | `runetki` | on | 179 | — | sửa vừa |
| `BongaCams` | `bgs` | on | 180 | — | sửa vừa |
| `Chaturbate` | `chu` | on | 214 | — | **sửa nhiều** |
| `Ebalovo` | `elo` | on | 264 | — | **sửa nhiều** |
| `PornHub` | `phub` | on | 290 | — | **sửa nhiều** |
| `Spankbang` | `sbg` | on | 359 | ✅ | **sửa nhiều** |
| `Xhamster` | `xmr` | on | 363 | — | **sửa nhiều** |
| `Eporner` | `epr` | on | 488 | ✅ | **sửa nhiều** |

> **LUẬT: KHÔNG BAO GIỜ sync/merge/pull/rebase với upstream** — cả 14 module này đã bị sửa khác
> upstream (popup, harden, đổi domain, fix player); merge sẽ ghi đè và **mất toàn bộ công sức**.
> Upstream chỉ để **đọc tham khảo** (`diff -r`), cần gì **port tay** đúng phần đó.
> Nhóm này do chủ repo upstream bảo trì ⇒ **làm sau, từ từ**.

## 2. `Modules/Adult` — tự làm (65)

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

## 3. Module ngoài Adult

### 3a. Gốc upstream, ĐÃ SỬA (25 thư mục)

| thư mục | file khác | dòng khác | ghi chú |
|---|---|---|---|
| `NextHUB` | 59 | 5049 | **sửa nhiều nhất** (+1 file riêng) |
| `WatchTogether` | 6 | 4422 | +4 file riêng, 3 file upstream không có ở mình |
| `Music` | 42 | 4309 | 3 file upstream không có ở mình |
| `LogUserRequest-Lite` | 13 | 3750 |  |
| `LampaWeb` | 14 | 2580 | +16 file riêng, 2 file upstream |
| `Sync` | 16 | 2567 |  |
| `OnlineRUS` | 26 | 1956 | +1 riêng, 10 file upstream không có ở mình |
| `OnlineENG` | 13 | 1747 | +9 file riêng |
| `DatabaseEditor` | 7 | 1180 |  |
| `GStreamer` | 16 | 1105 | +1 riêng, 1 file upstream |
| `AdminPanel` | 6 | 1001 | +1 file riêng |
| `Tracks` | 2 | 729 |  |
| `OnlinePaid` | 17 | 459 |  |
| `Community` | 5 | 212 | 2 file upstream không có ở mình |
| `OnlineAnime` | 17 | 204 | 1 file upstream không có ở mình |
| `PidTor` | 1 | 86 |  |
| `Proxy` | 3 | 63 |  |
| `JacRed` | 4 | 51 |  |
| `TorrServer` | 5 | 33 |  |
| `DLNA` | 2 | 33 |  |
| `Kit` | 2 | 15 |  |
| `OnlineGEO` | 5 | 13 |  |
| `OnlineUKR` | 5 | 10 |  |
| `ForkPlayerXML` | 1 | 2 |  |
| `Transcoding` | 1 | 2 |  |

### 3b. Gốc upstream, KHÔNG ĐỔI (7 thư mục)

`Catalog`, `ExternalBind`, `LampacApk`, `MsxNative`, `Potok`, `Telemetry`, `WebLog`

### 3c. Tự làm (upstream không có)

| module | đường dẫn |
|---|---|
| `DevFetch` | `Modules/DevFetch` |
| `OnlineVN` | `Modules/OnlineVN` |
| `Tg-notify.bot` | `Modules/Tg-notify.bot` |

### 3d. Upstream có, repo mình KHÔNG có

`Modules/NativeAppleTV`, `Modules/SeriesNotify`, `Online/tests` — không có ở mình (đã xoá hoặc upstream thêm sau).

## 4. Hạ tầng dùng chung (không phải module)

| đường | file khác | dòng khác | ghi chú |
|---|---|---|---|
| `Online/` | 2 | — | `ModInit.cs`, `plugin.js` sửa; thiếu `Online/tests` |
| `SISI/Controllers` | 2 | 8 | |
| `SISI/plugins` | 2 | 326 | +1 file riêng |
| `SISI/Models`, `SISI/SQL` | 0 | 0 | y upstream |
| `Core/` | 13 | 421 | |
| `Shared/` | 18 | 724 | |

⇒ Sửa `Core/`/`Shared/`/`.csproj` thì phải build bằng proot (xem skill `lampac-build`),
không dùng được `csc/check.sh` cho phần core.

## 5. LUẬT + việc phát hiện khi soi

- **KHÔNG BAO GIỜ sync/merge với upstream** (đã ghi vào memory). Chỉ đọc tham khảo + port tay.
- `SevenMM`, `SexHihi` (nhóm tự làm): có `genres` nhưng **thiếu `?rel=`** → sửa trước, rẻ nhất.
- 21 module Adult đang bật chưa có popup: **11 gốc upstream** (`BongaCams`, `Chaturbate`, `Ebalovo`,
  `HQporner`, `PornHub`, `Porntrex`, `Runetki`, `Tizam`, `Xhamster`, `Xnxx`, `Xvideos`) + **10 tự làm**
  (`Lon69`, `PhimSexVN88`, `PhimSexViet113`, `SexDep`, `Sexgenz2`, `Sieusex`, `SupJav` PAUSED,
  `TangThon`, `TopGai`, `Viet69Av`).
- Đếm lại bằng script: **54 module có `genres`**, **52 có cả `?rel=`** (todo cũ ghi 52 chỉ đúng phần rel).
- `XvideosRED` (gốc) và `Viet69kz` (tự làm) đang tắt.
- `NextHUB` là module gốc bị sửa nhiều nhất (59 file / 5.049 dòng) — **không merge**, chỉ port tay.

## 6. Chạy lại phép so sánh

```sh
TMP="${TMPDIR:-$PREFIX/tmp}"; rm -rf "$TMP/up-lampac"
git clone --depth 1 --filter=blob:none --no-checkout \
  https://github.com/lampac-nextgen/lampac.git "$TMP/up-lampac"
cd "$TMP/up-lampac" && git sparse-checkout init --cone && \
  git sparse-checkout set Modules Online SISI Core Shared && git checkout
cd ~/lampac-clean
# so từng thư mục, bỏ rác build
diff -rq -x bin -x obj -x "*.dll" "$TMP/up-lampac/Modules/NextHUB" Modules/NextHUB | head
```
