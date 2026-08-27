# Videasy

Онлайн-источник **Videasy** (`https://player.videasy.to`) для ENG. Актуальный resolver работает напрямую через metadata/seed API `speedracelight.com` и расшифровывает payload `enc=2`; Playwright больше не требуется. Реализация протокола сверена с MIT-референсом `KitsuneKode/kunai` и проверена на его публичном fixture `wings-enc2-neon2` (magic `mvm1`, 2 sources).

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

По умолчанию: **`displayindex = 1020`**, **`streamproxy = true`**. Resolver опрашивает `cdn`, `neon2`, `m4uhd`, `meine`, `lamovie`, удаляет дубликаты и возвращает все найденные варианты в меню качества Lampa (Yoru/Neon/Breach/Killjoy/Omen, включая 4K при наличии).

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "videasy"`** → **` ~ 1080p`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/videasy`** | Основная выдача. |
| **`lite/videasy/video`**, **`lite/videasy/video.m3u8`** | Видео / HLS. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
