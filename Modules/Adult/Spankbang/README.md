# Spankbang

Раздел **Sisi**: источник **SpankBang** (`https://ru.spankbang.com`). Префикс маршрутов **`sbg`**. **18+**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleSisi`**.

## Регистрация канала (`Invoke`)

Канал **`spankbang.com`** добавляется только при условии HTTP/`rhub`/Playwright/**`overridehost(s)`** — см. **`ModInit.Invoke`** (аналогично Chaturbate/BongaCams).

## Конфигурация

Секция в `init.conf`: **`Spankbang`** (`SisiSettings`).

По умолчанию: **`httpversion = 2`**, **`displayindex = 16`**, **`rch_access`** и **`stream_access`** включают **`apk`**, **`cors`**, **`web`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`sbg`** | Основная выдача (`pg`, `search`, `sort`, `path` — страницы актёра/канала/тега). |
| **`sbg?rel=<uri>`** | «Похожие видео»: `total_pages: 1`, `pg > 1` → пустой список (кэш общий с `vidosik`). |
| **`sbg/genres?uri=`** | Чипы для popup: `Диễn viên` / `Kênh` / `Thẻ` + `Liên quan` (`plain: true` → `?rel=`). |
| **`sbg/vidosik`** | Карточка видео (`qualitys`, `recomends`; `related=true` — старый путь, оставлен для совместимости). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
