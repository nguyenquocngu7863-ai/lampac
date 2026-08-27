# VidSrc

Онлайн-источник **VidSrc** (`https://vsembed.su`) для ENG. Используются документированные iframe-маршруты `/embed/movie/{id}` и `/embed/tv/{id}/{season}/{episode}`. Lampac создаёт нейтральную parent-страницу через `/api/chromium/iframe`, нажимает Play внутри iframe и перехватывает HLS; CineWave для инициализации больше не нужен.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`vidsrc`**, имя **`VidSrc`**, суффикс **` (ENG)`**.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Vidsrc`** (ключ инициализации **`Vidsrc`** — см. **`ModuleInvoke.Init`** в **`ModInit`**).

По умолчанию: **`displayindex = 1005`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "vidsrc"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/vidsrc`** | Основная выдача. |
| **`lite/vidsrc/video`**, **`lite/vidsrc/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
