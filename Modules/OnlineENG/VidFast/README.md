# VidFast

Онлайн-источник **VidFast** (`https://vidfast.vc`) для ENG. Используются документированные iframe-маршруты `/movie/{id}` и `/tv/{id}/{season}/{episode}` (id — IMDB или TMDB). Lampac создаёт нейтральную parent-страницу через `/api/chromium/iframe`, включает autoPlay и перехватывает HLS.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`vidfast`**, имя **`VidFast`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`VidFast`** (ключ инициализации **`VidFast`** — см. **`ModuleInvoke.Init`** в **`ModInit`**).

По умолчанию: **`displayindex = 1010`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "vidfast"`** → **` ~ HD`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/vidfast`** | Основная выдача. |
| **`lite/vidfast/video`**, **`lite/vidfast/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**.
