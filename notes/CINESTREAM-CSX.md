# CineStream (CSX) — báo cáo "test" 2026-08-31

> Commit có ý thức từ 2026-09-01: đây là căn cứ cho `notes/FILEHOST-SOURCE-FORMULA.md` (vì sao chọn
> port extractor thay vì viết plugin Kotlin) và `notes/UHD-MOVIES.md` (nhịp đổi domain qua
> `urls.json`/`domains.json`, và cái giá của provider bám vào service bên thứ ba như `enc-dec.app`).

Repo: <https://github.com/SaurabhKaperwan/CSX> · branch `master` HEAD `f1b19bd` (2026-08-31, "Remove dead sources")
License **GPLv3**. Không có GitHub Releases — APK/`.cs3` publish trên **branch `builds`**.

## 1. Link để test trên CloudStream 3

Repo URL (dán vào Settings → Repository → Add):
```
https://raw.githubusercontent.com/SaurabhKaperwan/CSX/builds/plugins.json
```
"Repo của repo" (nếu fork hỗ trợ `pluginLists`, vd. bản có CS.json):
```
https://raw.githubusercontent.com/SaurabhKaperwan/CSX/builds/CS.json
```
Cài lẻ 1 plugin (`.cs3`) nếu app hỗ trợ mở file trực tiếp:
```
https://raw.githubusercontent.com/SaurabhKaperwan/CSX/builds/CineStream.cs3
```

**Độ tươi của bản build** (quan trọng, đã kiểm): commit cuối branch `builds` có
`author=2026-01-20` (do workflow `git commit --amend`) nhưng **`committer=2026-08-31T05:29`**
và message `Build f1b19bdf…` = đúng HEAD master hôm nay ⇒ `CineStream.cs3` (716,815 B, **v481**) **mới 100%**,
CI tự build sau mỗi push. Các plugin khác: Bollyflix v33, MoviesDrive v33, Moviesmod v33, VegaMovies v82.

## 2. Inventory CineStream v481

| Chỉ số | Giá trị |
|---|---|
| Provider đăng ký trong `ProviderRegistry.builtInProviders` | **68** |
| …dùng host **hot-swap** từ `urls.json` | **21** |
| …phụ thuộc `enc-dec.app` (bên thứ 3) | **3** (hexa, xpass, + Extractors) |
| …là torrent (`isTorrent=true`) | 3 (Torrentio, TorrentsDB, …) |
| Provider bị comment/ẩn | 1 (`ProviderRegistry.kt:104`) |
| Secret nhồi lúc build (`BuildConfig`) | `CASTLE_KEY`, `MOVIEBLAST_TOKEN/API/KEY` (+ `SIMKL_API`) |

`urls.json` (nơi họ đổi domain source **không cần cập nhật app**):
<https://raw.githubusercontent.com/SaurabhKaperwan/Utils/refs/heads/main/urls.json>

```
4khdhub=4khdhub.one · bollyflix=bollyflix.af · hdmovie2=hdmovie2a.cfd · rtally=rtally.link
hindmoviez=hindmovie.fit · moviesdrive=new3.moviesdrive.christmas · movies4u=new5.movies4u.clinic
multimovies=multimovies.makeup · nfmirror=tv.imgcdn.kim/newtv · skymovies=skymovieshd.forex
uhdmovies=uhdmovies.autos · moviesmod=moviesmod.zone · topmovies=moviesleech.art
vegamovies=new2.vegamovies.futbol · rogmovies=new2.rogmovies.click · gdflix=new3.gdflix.io
hubcloud=hubcloud.cx · toonstream=toon-stream.site · zinkmovies=zinkmovies.org · vcloud=vcloud.fit
dudefilms=dudefilms.garden · m4ufree=ww4.m4ufree.lat · animedao=anidao.to · mlsbd=mlsbd.co · fibwatch=fibwatch.art
```

**Nhịp bảo trì file đó** (minh chứng "cộng đồng hot" thật sự nằm ở đây, không phải repo provider):
```
2026-08-30 Update urls.json          2026-08-21 Update provider URLs
2026-08-30 Update provider URLs      2026-08-18 Update URL for rogmovies
2026-08-27 Update provider URLs      2026-08-15 Update provider URLs
2026-08-24 Update provider URLs      2026-08-14 Modify URLs for vegamovies and vcloud
```
⇒ **cách 1–3 ngày / lần**, có cả commit `[skip ci]` tự động. Domain source chết là họ đổi trong ~24h mà người dùng không phải làm gì.

## 3. Probe sống/chết (fetch từ sandbox, 04:05 2026-08-31)

| Host | Kết quả | Đọc |
|---|---|---|
| `vidcore.io` | 200, title "VidCore — Next-Gen Video Embedding" | **SỐNG**, và có **docs công khai**: `GET https://vidcore.io/movie/{imdb_or_tmdb_id}?server=&sub=&autoPlay=` — dễ port nhất từ trước tới nay |
| `api.speedracelight.com` (Videasy mới) | `:)` | sống (Lampac đang có module Videasy — **cần kiểm tra host đã đổi từ `api.videasy.to` → `speedracelight.com` chưa**) |
| `api.hlowb.com` (Castle) | JSON `{"status":404,...,"timestamp":"2026-08-31T13:04:30Z"}` | API **đang chạy** (Spring Boot), nhưng cần `CASTLE_KEY` build-time |
| `streamdata.vaplayer.ru` | HTTP 500 / fetch fail | có thể geo-block RU |
| `mapple.tv`, `enc-dec.app` | (đã đo ở phần trước) | flow Mapple: playback-init+encrypt còn sống |

## 4. Kiến trúc đáng "ăn cắp" (đây là phần hữu ích nhất cho Lampac)

```kotlin
data class ProviderDef(
    val key: String, val displayName: String, val isTorrent: Boolean = false,
    val executeStandard: (suspend CineStreamExtractors.(res, subCb, cb) -> Unit)? = null,
    val executeAnime:    (…)? = null,
    val executeMalSync:  (…)? = null,
)
```
+ `safeAmap` (`CineStreamUtils.kt:558`): `supervisorScope { items.map { runCatching { f(it) } }.awaitAll().filterNotNull() }`
⇒ **68 provider chạy song song, 1 cái chết không làm sập cả dãy**, và bật/tắt từng cái qua `settings/SettingsProviders.kt`.

So với Lampac: mình có `disableEng` (tắt nguyên cả **nhóm**) + `manifest.json` từng module — **không có** công tắc per-source và **không có** cơ chế đổi domain từ xa. Đó chính là lý do cảm giác "kho của tui còn kém hơn": không phải vì source mình kém, mà vì mình **sửa source = phải vá file + restart**, còn họ **sửa source = 1 commit json**.

## 5. Áp dụng vào repo này (đề xuất cụ thể)

1. **`Modules/sources.json`** (hoặc `LampaWeb/sources.json`): map `tên source → base URL + headers + enable`.
   Server đọc lúc boot + reload theo `intervalupdate`; `init.conf` chỉ còn phần credential.
2. `setup-termux.sh --sync`: **bỏ hardcode danh sách file** → tải 1 `manifest.json` tự mô tả file nào cần kéo
   (hết cảnh "script cũ không biết file mới" → triệt tiêu đúng cái bẫy `autotracks.js` 404 trong README).
3. `enable`/`disable` per-source trong AdminPanel (đã có AdminPanel native → chỉ cần đọc sources.json).
4. Với Mapple/Stellar: giữ `host[]` + `sources[]` trong json ⇒ site đổi domain, **không cần build lại module**.
5. Ghi vào README mục "Việt hóa bền vững"/"Cập nhật": thêm "nguồn cấu hình động" ngang hàng.

## 6. Caveat khi test CineStream

- **`CASTLE_KEY` / `MOVIEBLAST_*` không có trong repo** (build secret) ⇒ 2 provider Castle & MovieBlast chỉ chạy được với bản `.cs3` CI build; nếu ní tự build từ source sẽ fail 2 provider này.
- 3 provider đi qua `enc-dec.app` ⇒ **cùng kiểu gãy** như Mapple/nuvio: service bên thứ 3 bỏ endpoint là provider chết, không tự phục hồi.
- Repo **không có releases**, không có file cài đặt trong `master` → nếu ní tìm "Download APK" ở Releases là thấy trống, phải dùng `builds`.
- `plugins.json` là **array**, không phải object `{providers:[...]}` (cẩn thận khi tự parse bằng script).
