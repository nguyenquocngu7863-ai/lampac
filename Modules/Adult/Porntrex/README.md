# Porntrex

Раздел **Sisi**: источник **Porntrex** (`https://www.porntrex.com`). Префикс маршрутов **`ptx`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один канал **`porntrex.com`**.

## Конфигурация

Секция в `init.conf`: **`Porntrex`** (`SisiSettings`).

По умолчанию: **`displayindex = 18`**, **`streamproxy = true`**, **`rch_access` / `stream_access = apk`**, для потока и картинок задан **`referer`** на домен Porntrex.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`ptx`** | Основная выдача. |
| **`ptx/vidosik`** | Карточка видео. |
| **`ptx/strem`** | Потоковый endpoint (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.

## Popup nhấn giữ (2026-11-25)

- `GET /ptx/genres?uri=<detail>` → `{items:[],groups:[…]}`, không bao giờ 500 (uri rỗng/SSRF → `groups: []`).
- Chip lấy từ các khối `<span class="title-item">Nhãn:</span>` của detail (KVS theme porntrex):

  | nhóm | nguồn | mở bằng |
  |---|---|---|
  | **Thể loại** | `Categories:` → `a.js-cat` `/categories/<slug>/` | `?c=cat/<slug>` |
  | **Diễn viên** | `Models:` → `/models/<slug>/` | `?c=model/<slug>` |
  | **Kênh** | `Channel:` → `/channels/<slug>/` | `?c=channel/<slug>` |
  | **Thẻ** | `Tags:` → `/tags/<slug>/` | `?c=tag/<slug>` |

  `Uri()` map tiền tố `cat|tag|model|channel` → `categories|tags|models|channels`; slug không tiền tố vẫn hiểu là thể loại.
- **Liên quan**: `<div class="related-videos" id="list_videos_related_videos">` → `?rel=<uri>` trả 12 item, `total_pages: 1`, `pg>1` rỗng.
- Cache chip+rel trong RAM 6h theo pageUrl; `DetailUrl()` chặn host lạ (chỉ `*.porntrex.com`, uri tương đối `video/<id>/<slug>` được ghép `init.host`).
- Bẫy đã dính: pattern `<a …>(?:[^<]*)</a>` bỏ sót Models/Channel vì trong `<a>` có `<i class="fa …">` icon → đổi thành `[\s\S]{0,200}?</a>`.

## Kiểm chứng (2026-11-25, localhost 9118)

```
GET /ptx                                        -> 85 item, tp=0
GET /ptx?pg=2                                   -> trang 2 khác trang 1
GET /ptx?search=teen                            -> 79 item
GET /ptx?c=cat/teen                             -> 120 item
GET /ptx/genres?uri=video/1311225/…             -> Thể loại 17 · Diễn viên 1 · Kênh 1 · Thẻ 21 · Liên quan 1
GET /ptx?c=model/blake-blossom                  -> 119 · ?c=channel/pov-sex 11 · ?c=tag/pov 120
GET /ptx?rel=…                                  -> 12 item, tp=1; pg=2 rỗng
GET /ptx/genres?uri=<host lạ>|<rỗng>            -> groups [] (200)
play.sh (720p)                                  -> PHÁT ĐƯỢC h264 1280x720 1113.2s (decode 5s đầu/cuối)
```
