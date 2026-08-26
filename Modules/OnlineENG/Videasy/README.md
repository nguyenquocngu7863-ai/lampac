# Videasy

Онлайн-источник **Videasy** (`https://player.videasy.net`) для ENG по тем же правилам, что **MovPI** (**`disableEng`**, **`tmdb`/`cub`**, Playwright).

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Плагин **`videasy`**, имя **`Videasy`**, суффикс **` (ENG)`** — см. **`ModInit`**.

Для изолированной проверки при глобальном `"disableEng": true` задайте:

```json
"Videasy": {
  "enabled": true
}
```

Поле `enabled` здесь является явным opt-in только для Videasy; оно не включает остальные ENG-модули.

## Глобальный поиск

Нет **`with_search.Add`** в **`ModInit`**.

## Конфигурация

Секция в `init.conf`: **`Videasy`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 1020`**, **`streamproxy = true`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "videasy"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/videasy`** | Основная выдача. |
| **`lite/videasy/video`**, **`lite/videasy/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
