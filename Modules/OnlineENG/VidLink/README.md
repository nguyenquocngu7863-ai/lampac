# VidLink

Онлайн-источник **VidLink** (`https://vidlink.pro`) для ENG. Основной resolver использует зашифрованный endpoint `/api/b` самого web-player и получает поле `stream.playlist` напрямую; Playwright оставлен только как fallback.

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
