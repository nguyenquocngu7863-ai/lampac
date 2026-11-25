# Tizam

Раздел **Sisi**: источник **TIZAM** (`https://tv4.tizam.org`), канал в клиенте **`tizam.pw`**, префикс маршрутов **`tizam`**. Контент может быть **18+** в зависимости от каталога сайта.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — всегда один пункт в **`Invoke`**.

## Конфигурация

Секция в `init.conf`: **`Tizam`** (`SisiSettings`).

По умолчанию в коде: **`displayindex = 21`**, **`rch_access` / `stream_access`** (`apk`, `cors`), **`rchstreamproxy = web`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`tizam`** | Основная выдача. |
| **`tizam/vidosik`** | Карточка/поток видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.

## Popup nhấn giữ + fix RCH (2026-11-25)

- **Fix quan trọng**: `ModInit` có `rchstreamproxy = "web"` nhưng thiếu `streamproxy = true` → `RchClient.IsRequiredConnected()` luôn true nên **mọi route** (`/tizam`, `/tizam/vidosik`) trả `{"rch":true,"nws":…}` cho client web/APK không nối RCH. Thêm `streamproxy = true` (giống Ebalovo) là module chạy lại bình thường.
- `GET /tizam/genres?uri=<detail>` → `{items:[],groups:[…]}`, uri rỗng/host lạ → `groups: []` (200):

  | nhóm | nguồn HTML | mở bằng |
  |---|---|---|
  | **Diễn viên** | `a.actor__cover` → `/aktrisy/<slug>/` + `h3.actor__title` | `?c=actress/<slug>` |
  | **Thể loại** | `<li><span>Категория:</span>` → `/fil_my_dlya_vzroslyh/<slug>/` | `?c=cat/<slug>` |
  | **Thẻ** | `div.film__tags` → `/podborki/<slug>/` | `?c=tag/<slug>` |
  | **Liên quan** | `<div class="row same_video">` heading "Похожие фильмы" | `?rel=<uri>` → 16 item, `total_pages: 1` |

- `Uri()` thêm overload `(host, c, pg)` map `cat|tag|actress` → `/fil_my_dlya_vzroslyh/…`, `/podborki/…`, `/aktrisy/…`; phân trang `?p={pg-1}` (trang 1 không có `?p`). Slug trần vẫn là thể loại.
- **Ngôn ngữ**: site Nga toàn bộ → bảng `CatVi` (21 thể loại) + `TagVi` (40+ thẻ phổ biến, map cả biến thể `chuzhie_zheny`/`chuzhie-zheny` do slug site không thống nhất), còn lạ thì `PrettySlug()` (Latin, không Cyrillic). Tên phim và tên diễn viên giữ nguyên (nội dung, không phải nhãn menu).
- Cache chip+rel RAM 6h; `DetailUrl()` chỉ `*.tizam.org|*.tizam.pw` và luôn dựng lại bằng host module.

## Kiểm chứng (2026-11-25, localhost 9118)

```
GET /tizam                                      -> 12 item (trước fix: {"rch":true})
GET /tizam/genres?uri=fil_my_dlya_vzroslyh/…    -> Diễn viên 12 · Thể loại 1 · Thẻ 11 · Liên quan 1
GET /tizam?c=cat/s_russkim_perevodom            -> 12; pg2 11 (khác trang 1)
GET /tizam?c=tag/bryunetki                      -> 10 · ?c=actress/dava-foxx 21
GET /tizam?rel=…                                -> 16 item, tp=1; pg=2 -> {total_pages:1,list:[]}
GET /tizam/genres?uri=<host lạ>|<rỗng>          -> groups [] (200)
play.sh                                         -> PASS h264 720x480 8493.4s (decode 5s đầu/cuối)
```
