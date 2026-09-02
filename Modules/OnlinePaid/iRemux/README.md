# iRemux

Онлайн-источник **iRemux** (`https://megaoblako.com`): в **`Invoke`** — плагин **`remux`**, имя **`iRemux`**. В шаблоне **`enable = false`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Условие (`Invoke`)

Пункт добавляется при **`args.serial == -1`** или **`args.serial == 0`**.

## Глобальный поиск

**`with_search.Add("remux")`**.

## Конфигурация

Секция в `init.conf`: **`iRemux`** (`OnlinesSettings`).

По умолчанию: **`displayindex = 537`**, **`stream_access = apk,cors,web`**, **`plugin = "remux"`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "remux"`** → **` ~ 2160p`**.

## Доступ к ссылкам

Некоторые карточки MegaOblako показывают описание и качество без авторизации, но сами ссылки могут быть закрыты VIP-статусом. Модуль не обходит это ограничение. Для собственного действующего аккаунта можно задать в `init.conf` один из вариантов:

```json
"iRemux": {
  "enable": true,
  "login": "ваш логин",
  "passwd": "ваш пароль"
}
```

или передать актуальные cookie сайта через поле `cookie`. Если сайт изменил форму входа или cookie истёк, обновите их и перезапустите Lampac.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/remux`** | Основная выдача. |
| **`lite/remux/movie`** | Фильм / разбор (см. **`Controller.cs`**). |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`OnlineApi.cs`**, **`Model.cs`**.
