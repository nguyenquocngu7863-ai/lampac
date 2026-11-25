# Ebalovo

Раздел **Sisi**: источник **EBALOVO** (`https://www.ebalovo.pro`). Внутренний идентификатор канала **`ebalovo.porn`**, префикс маршрутов **`elo`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один пункт в **`Invoke`**.

## Конфигурация

Секция в `init.conf`: **`Ebalovo`** (`SisiSettings`).

По умолчанию: **`displayindex = 14`**, **`rch_access = apk`**, **`stream_access = apk,cors`**, **`rchstreamproxy = web`**, **`headers`** и **`headers_stream`** с типичными fetch-заголовками для страниц и видео.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`elo`** | Основная выдача. |
| **`elo/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.

## Popup nhấn giữ (2026-11-25)

- `GET /elo/genres?uri=<detail>` → `{items:[],groups:[…]}`; uri rỗng/host lạ → `groups: []` (200, không 500).
- Chip lấy từ 1 lần fetch trang detail:

  | nhóm | nguồn HTML | mở bằng |
  |---|---|---|
  | **Thể loại** | `<strong>В этом порно:</strong>` → `/porno/<slug>/` | `?c=cat/<slug>` (hoặc slug trần) |
  | **Diễn viên** | `span.link-models` → `<strong>Модели:</strong>` → `/models/<slug>/` | `?c=model/<slug>` |
  | **Hãng phim** | `<strong>Порно студия:</strong>` → `/site/<slug>/`, `/sites/<slug>/` | `?c=site/<slug>` · `?c=sites/<slug>` |

- **Liên quan**: `<div class="video-related">` (`#list_videos_related_videos_items`, heading "Ещё порно видео:") → `?rel=<uri>` trả 12 item, `total_pages: 1`, `pg>1` rỗng.
- Tên thể loại **phải tiếng Việt**: bảng `CatSlugs` (124 cặp slug→tên) nay là nguồn duy nhất cho cả menu `Danh mục:` và popup; slug site in khác menu (`a1-russian`, `b1-masturbation`, `b1-solo`…) map qua `CatAlias`; còn lạ thì `PrettySlug()` (bỏ tiền tố `a1-`/`b1-`, Title Case) — không để lọt chữ Nga.
- Bẫy: link model/studio có `</a >` (dấu cách trước `>`) → regex phải là `</a\s*>`, nếu không **mất cả nhóm Diễn viên/Hãng phim**.
- Cache chip+rel RAM 6h; `DetailUrl()` chặn SSRF (chỉ `*.ebalovo.porn|click|pro`).

## Kiểm chứng (2026-11-25, localhost 9118)

```
GET /elo                                        -> 30 item
GET /elo?pg=2                                   -> 24, khác trang 1
GET /elo?search=anal                            -> 36
GET /elo/genres?uri=video/appetitnaya-…         -> Thể loại 12 · Diễn viên 1 · Hãng phim 2 · Liên quan 1
GET /elo?c=cat/a1-russian                       -> 24
GET /elo?c=model/miss-len                       -> 1
GET /elo?c=site/nubiles-network                 -> 24 · ?c=sites/nubiles 24
GET /elo?rel=…                                  -> 12 item, tp=1; pg=2 -> {total_pages:1,list:[]}
GET /elo/genres?uri=<host lạ>|<rỗng>            -> groups [] (200)
play.sh                                         -> PASS h264 1280x720
```
