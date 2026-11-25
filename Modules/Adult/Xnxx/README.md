# Xnxx

Раздел **Sisi**: источник **XNXX** (`https://www.xnxx-ru.com`). Префикс маршрутов **`xnx`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один канал **`xnxx.com`**.

## Конфигурация

Секция в `init.conf`: **`Xnxx`** (`SisiSettings`).

По умолчанию: **`httpversion = 2`**, **`displayindex = 19`**, **`rch_access` / `stream_access`** (`apk`, `cors`, `web`).

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`xnx`** | Основная выдача. |
| **`xnx/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.

## Popup nhấn giữ (2026-11-25)

- `GET /xnx/genres?uri=<detail>` → `{items:[],groups:[…]}`, uri rỗng/host lạ → `groups: []` (200).
- Trang detail mirror `www.xnxx-ru.com` nhúng config JS, không có HTML tag list:

  | nhóm | nguồn | mở bằng |
  |---|---|---|
  | **Diễn viên** | `"video_models":{"…":{"name":…}}` | `?c=model/<tên>` → `/search/<tên>/<pg>` |
  | **Thẻ** | `"video_tags":["anal","black",…]` | `?c=tag/<slug>` → `/search/<slug>/<pg>` |
  | **Liên quan** | `var video_related=[{"u":"/video-…","t":…,"d":"11min","i":thumb}]` | `?rel=<uri>` → 40 item, `total_pages: 1` |

- Tag/pornstar **không có trang listing parse được**: `/tags/<slug>` là landing SEO (pagination trỏ `/search/…`), `/pornstar/<slug>` render 50 card rỗng bằng JS → cả hai chip đều mở qua `/search/<kw>/<pg>` (có phân trang thật).
- `video_related` là JSON nhúng: tên bị escape `\uXXXX`, ảnh escape `https:\/\/…` → phải `JsonUnescape()` (`\uXXXX` → char, `\/` → `/`) rồi mới `HtmlDecode`.
- `Uri()` thêm overload `(host, search, c, pg)`; `DetailUrl()` chỉ `*.xnxx-ru.com|*.xnxx.com` và luôn dựng bằng host module.
- Cache chip+rel RAM 6h.

## Kiểm chứng (2026-11-25, localhost 9118)

```
GET /xnx                                        -> 36 item
GET /xnx?pg=2                                   -> 36, khác trang 1
GET /xnx?search=anal                            -> 36
GET /xnx/genres?uri=video-1ixqmv79/_            -> Diễn viên 2 · Thẻ 11 · Liên quan 1
GET /xnx?c=tag/anal                             -> 36 (pg2 36) · ?c=model/Fernanda%20Chocolatte 36
GET /xnx?rel=…                                  -> 40 item, tp=1; pg=2 -> {total_pages:1,list:[]}
GET /xnx/genres?uri=<host lạ>|<rỗng>            -> groups [] (200)
play.sh (HLS proxy)                             -> PASS h264 854x480 730.2s (decode 5s đầu/cuối)
```
