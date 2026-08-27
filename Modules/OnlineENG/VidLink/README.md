# VidLink

Онлайн-источник **VidLink** (`https://vidlink.pro`) для ENG. Resolver получает актуальный id через `enc-dec.app` и запрашивает `/api/b` с playback environment `standard`, затем выбирает максимальную H.264 rendition из `stream.qualities`. Файлы с `requiresProxy` преобразуются в официальный relay `/mp` на `noon.mooncase.online`. DASH/WebKit со signed cookies остаётся только запасным вариантом: Lampa на Android открывал MPD, но бесконечно ждал сегменты. Playwright оставлен последним fallback.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`vidlink`**, имя **`VidLink`**, суффикс **` (ENG)`**.

При глобальном `"disableEng": true` источник включается отдельно:

```json
"VidLink": {
  "enable": true,
  "enabled": true,
  "streamproxy": true
}
```

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`VidLink`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 1015`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "vidlink"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/vidlink`** | Основная выдача. |
| **`lite/vidlink/video`** | Видео. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
