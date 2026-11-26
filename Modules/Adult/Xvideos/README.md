# Xvideos

Раздел **Sisi**: источник **Xvideos** (базовый хост в конфиге **`https://www.xv-ru.com`** — зеркало/регион). Префикс маршрутов **`xds`**. Контент **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`**.

## Каналы (`Invoke`)

- Всегда: **`xvideos.com`** → **`xds`**.
- Если в событии **`args.lgbt`** включён режим LGBT: добавляются **`xdsgay`** и **`xdstrans`** (внутренние идентификаторы **`xdsgay`**, **`xdssml`**).

## Конфигурация

Секция в `init.conf`: **`Xvideos`** (`SisiSettings`).

По умолчанию: **`httpversion = 2`**, **`displayindex = 12`**, **`rch_access` / `stream_access`** включают **`apk`**, **`cors`**, **`web`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`xds`**, **`xdsgay`**, **`xdssml`** | Списки/разделы (`?t=<tag>` — тег, `?rel=<uri>` — похожие, `total_pages: 1`). |
| **`xds/stars`**, **`xdsgay/stars`**, **`xdssml/stars`** | Избранное/«stars» (pornstar/channel JSON). |
| **`xds/vidosik`** | Карточка видео. |
| **`xds/genres`** | Popup (long-press): актёры / канал / теги / «похожие» (`uri` = detail `video.*`). |

Попап: один запрос detail-страницы, кэш RAM 6 ч; чужие хосты в `uri` отбрасываются (SSRF).

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
