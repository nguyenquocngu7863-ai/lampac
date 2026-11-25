# HQporner

Раздел **Sisi**: источник **HQporner** (`https://m.hqporner.com`). Префикс маршрутов **`hqr`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`** — один канал **`hqporner.com`**.

## Конфигурация

Секция в `init.conf`: **`HQporner`** (`SisiSettings`).

По умолчанию: **`displayindex = 15`**, **`rch_access` / `stream_access`** (`apk`, `cors`, `web`), **`geostreamproxy = ["ALL"]`**, заголовки **`referer`** для страниц и изображений.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`hqr`** | Основная выдача. |
| **`hqr/vidosik`** | Карточка видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.

## Popup nhấn giữ (2026-11-25)

- `GET /hqr/genres?uri=<detail>` → `{items:[],groups:[…]}`; uri rỗng/host lạ → `groups: []` (200, không 500).
- Dữ liệu lấy từ trang **desktop** `https://hqporner.com/<uri>` (mobile `m.hqporner.com` không có khối "Similar"), cache RAM 6h theo URL:

  | nhóm | nguồn HTML | mở bằng |
  |---|---|---|
  | **Diễn viên** | `<li class="icon fa-star-o">featuring` → `/actress/<slug>` | `?c=actress/<slug>` → `/actress/<slug>/<pg>` |
  | **Thể loại** | `<h3>This video belongs to the following categories</h3>` → `/category/<slug>` | `?c=cat/<slug>` → `/category/<slug>/<pg>` |
  | **Liên quan** | `<h2 class="major"><span>Similar HD porn</span></h2>` | `?rel=<uri>` → 36 item, `total_pages: 1` |

- `Uri()` nhận tiền tố `cat|actress|studio`; slug trần vẫn hiểu là `category` (tương thích menu `Thể loại` cũ).
- Parser related dùng card desktop: `<a href="/hdporn/…" class="… atfib">` + `<h3 class="meta-data-title"><a>tên</a></h3>` + `<span class="icon fa-clock-o meta-data">thời lượng</span>`.
- `DetailUrl()` chỉ cho `*.hqporner.com` và luôn dựng lại bằng host desktop (uri tương đối `hdporn/…`).

## Kiểm chứng (2026-11-25, localhost 9118)

```
GET /hqr                                        -> 46 item
GET /hqr?pg=2                                   -> 46, khác trang 1
GET /hqr/genres?uri=hdporn/128743-…             -> Diễn viên 1 · Thể loại 8 · Liên quan 1
GET /hqr?c=cat/blonde                           -> 46 · ?c=cat/tattooed 46 (feed khác) · ?c=actress/sarah-harlow 2
GET /hqr?rel=…                                  -> 36 item, tp=1; pg=2 -> {total_pages:1,list:[]}
GET /hqr/genres?uri=<host lạ>|<rỗng>            -> groups [] (200)
GET /hqr/vidosik?uri=hdporn/128735-…            -> 1080p/720p/360p (bigcdn.cc)
play.sh (720p)                                  -> PHÁT ĐƯỢC, HTTP 206, mp4 960 MB
```
