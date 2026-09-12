# AdminPanel

Веб-**админка** Lampac: статический UI (**`auth.html`**, **`index.html`**) и JSON API для просмотра и правки **`init.conf`**, снимка **`current.conf`** и пользователей **`users.json`** (режим **accsdb**). Доступ к закрытым маршрутам — после ввода **root-пароля** из файла **`passwd`** (cookie **`accspasswd`**; см. ниже); при отказе **`[Authorization]`** перенаправляет на **`/adminpanel/auth`**.

## Lampa plugin

Built-in **`adminpanel.js`** (LampaWeb `initPlugins.adminpanel`) opens the same
API inside Lampa: Settings → **Admin Panel** → **Mở Admin Panel**. Password is
entered once, stored on the device, and sent to **`POST /adminpanel/api/login`**
(cookie **`accspasswd`**). Back stays in Lampa; it does **not** navigate to
**`/adminpanel/auth`**.

JSON helpers for the plugin (all **`AllowAnonymous`**, then cookie for the rest):

| Method | Route | Role |
|--------|-------|------|
| **`GET /adminpanel/api/session`** | `{ok:true}` or **401** `{ok:false}` |
| **`POST /adminpanel/api/login`** | JSON `{password, remember}` |
| **`POST /adminpanel/api/logout`** | Clears the cookie |

The HTML **`/adminpanel`** page remains available in a browser.

## Быстрый старт

1. Включите модуль в **`manifest.json`** (`"enable": true`). По умолчанию в репозитории стоит **`false`**.
2. Соберите и задеплойте сборку так же, как остальные модули (динамический модуль; список файлов — поле **`tree`** в **`manifest.json`**).
3. Откройте **`http(s)://<хост>:<порт>/adminpanel/auth`** и войдите **root-паролем из файла `passwd`** в рабочей директории сервера (см. [Вход](#вход)).
4. Рабочая панель: **`/adminpanel`**.

Удобный порт в dev-сборке через Docker часто задаёт compose (например **9118**); точный адрес зависит от **`listen`** в конфигурации Lampac.

## Вход и сессия

Страница **`/adminpanel/auth`** сохраняет пароль в cookie **`accspasswd`** (срок **180 дней**, **`path=/`**, **`SameSite=Lax`**, в HTTPS добавляется **`Secure`**).

Панель дергает API с **`credentials: 'same-origin'`**, поэтому cookie должна быть установлена для того же origin, что и сервер Lampac.

### Файл `passwd` (root-пароль)

Вход в админку **не** берётся из **`users.json`** и не совпадает с **`accsdb.shared_passwd`** в **`init.conf`**. Для маршрутов с **`[Authorization]`** ядро сравнивает cookie **`accspasswd`** с **`CoreInit.rootPasswd`**, который читается из файла **`passwd`** в **рабочей директории** процесса (рядом с **`init.conf`**):

- если **`passwd`** есть при старте — содержимое файла загружается в память, пробелы и переводы строк снимаются;
- если файла нет при старте — генерируется случайная строка и записывается в **`passwd`** (имеет смысл сразу задать свой пароль и при необходимости перезапустить процесс).

В форме входа нужно указывать **именно эту** root-строку.

Дополнительно ядро ограничивает число **разных** попыток пароля в cookie с одного IP за сутки; после порога ответы к защищённым маршрутам выглядят как **404** (см. **`Core/Middlewares/Accsdb.cs`**).

Тот же **`rootPasswd`** используется для внутренних вызовов с HTTP-заголовком **`lcrqpasswd`** (это не форма входа в браузере).

Остальная политика (локальный доступ без проверки, **accsdb** для клиентов Lampa, WAF и т.д.) накладывается **поверх** этой проверки — см. конфигурацию ядра. Не выкладывайте **`passwd`** в Git и не публикуйте админку в открытый интернет без осознанной защиты. В Docker/Kubernetes **`passwd`** обычно монтируют вместе с **`init.conf`** (см. комментарии в **`docker-compose.yaml`**, чарт **`charts/lampac`**).

## Где лежат файлы

Пути **`passwd`**, **`init.conf`**, **`current.conf`**, **`users.json`** задаются относительно **текущей рабочей директории процесса** Lampac (не от каталога модуля).

- Если **`current.conf`** на диске отсутствует или повреждён как JSON, при чтении подставляется снимок из **`CoreInit.CurrentConf`** (если доступен хостом).
- HTML читаются из **`ModInit.modpath`** (каталог загруженного модуля): **`auth.html`**, **`index.html`**.

В Docker проверьте, что том с конфигом смонтирован туда же, откуда процесс видит те же имена файлов, что вы правите через UI.

### Запись на диск

Сохранение **`init.conf`** и **`users.json`** делается атомарно: сначала запись во временный файл, затем **`Move`** с перезаписью. Если на bind mount **`Move`** даёт ошибку занятости цели (**EBUSY**), выполняется прямая запись в целевой файл — это осознанный fallback для некоторых сценариев Docker.

Ответ успешной записи: **`{"ok":true}`**. Ошибки: **`400`/`500`** с телом **`{"error":"...", "detail":"..."}`** (поле **`detail`** опционально).

## Различие «init» и «current» в UI

| Файл | Роль |
|------|------|
| **`init.conf`** | То, что можно **сохранять** через API: целиком (**`POST /adminpanel/api/init`**) или по корневым ключам (**`POST .../init/section/{key}`**). |
| **`current.conf`** | **Только чтение** в админке (**`GET /adminpanel/api/current`**): актуальный снимок/мердж конфигурации для просмотра и группировки секций в интерфейсе. Прямой POST для **`current`** в контроллере не предусмотрен. |

Если нужно изменить один корневой ключ в **`init.conf`**, тело **`POST /adminpanel/api/init/section/{key}`** — валидный JSON **значения** секции (объект, массив, примитив — как в корне конфига). Ключ секции не должен содержать **`/`** или **`\`**.

## HTTP API

Подробности тел запросов и валидации — в **`AdminPanelController.cs`**.

| Метод и маршрут | Описание |
|-----------------|----------|
| **`GET /adminpanel/auth`** | Страница входа (**`AllowAnonymous`**). |
| **`GET /adminpanel`** | Главная панель (**`index.html`**). |
| **`GET /adminpanel/api/groups`** | Группы секций по ключам, **присутствующим** в **`current.conf`** (или в памяти, если файл не прочитан), плюс блок **«Прочее»** для ключей без каталога. |
| **`GET /adminpanel/api/groups/catalog`** | Полный **каталог** групп из **`ConfigSectionGroups.Catalog`**; если в актуальном конфиге есть ключи вне каталога — добавляется **«Прочее»** для них (подписи ориентированы на ключи из current). |
| **`GET /adminpanel/api/init`** | Содержимое **`init.conf`** (пустой объект, если файла нет); JSON приводится к отформатированному виду когда возможно. |
| **`POST /adminpanel/api/init`** | Тело — один JSON **объект** (корень **`init.conf`**). Целиком перезаписывает файл. |
| **`POST /adminpanel/api/init/section/{key}`** | Обновить один корневой ключ в **`init.conf`**. |
| **`GET /adminpanel/api/current`** | **`current.conf`** или fallback из **`CoreInit.CurrentConf`**. |
| **`GET /adminpanel/api/users-json`** | Массив пользователей; если файла нет — **`[]`**. |
| **`POST /adminpanel/api/users-json`** | Тело — JSON **массив** объектов пользователей (схема **accsdb**); перезаписывает **`users.json`**. |

## Группы в редакторе

Класс **`ConfigSectionGroups`** задаёт человекочитаемые группы и подсказки для бокового каталога: какие корневые ключи конфига к какой группе относятся (рантайм, listen, модули, источники и т.д.).

При добавлении нового модуля с секцией в корне JSON имеет смысл дописать её ключ в **`Catalog`** нужной группы или в блок **«modules»**, чтобы ключ не попадал только в **«Прочее»**.

Публичный **`GroupDto`** (см. низ **`ConfigSectionGroups.cs`**): **`id`**, **`title`**, **`hint`**, массив **`keys`**.

## Конфигурация модуля

Отдельной секции настроек в **`ModInit`** нет — только **`manifest.json`** (**`enable`**, **`dynamic`**, **`tree`**).

Статические ассеты панели лежат рядом с кодом модуля (**`index.html`**, **`auth.html`**); при изменении их в репозитории нужна перезагрузка/пересборка в соответствии с тем, как хост подхватывает динамические модули.

## Файлы модуля

| Файл | Роль |
|------|------|
| **`AdminPanelController.cs`** | Маршруты UI и API, работа с файлами конфигурации. |
| **`ConfigSectionGroups.cs`** | Каталог групп и билдер **`GroupDto`**. |
| **`ModInit.cs`** | **`IModuleLoaded`**: сохраняет **`modpath`** для раздачи HTML. |
| **`manifest.json`** | Включение модуля и список подгружаемых **`.cs`**. |
| **`auth.html`**, **`index.html`** | Интерфейс входа и панели. |

---

## Từ điển key trong admin panel (tiếng Việt)

Quy ước: sửa xong mục nào bấm **Lưu**, đổi config cần **Restart** mới ăn.
Lưu trong panel là **ghim đè** giá trị vào `init.conf` — cập nhật yaml/code sau này
không thắng được giá trị đã ghim cho tới khi xóa ô đó.

### Key dùng chung của mọi nguồn

| Key | Nghĩa |
|-----|-------|
| `enable` / `enabled` | Bật/tắt nguồn. Tắt flag vẫn tốn giờ compile lúc start — muốn nhẹ máy thì cho vào `BaseModule.SkipModules`. |
| `displayname` | Tên hiện trong app. |
| `displayindex` | Thứ tự hiện: số nhỏ lên trước (menu 18+ xếp Việt 10-12, quốc tế 20-44...). |
| `apihost` | Host API của nguồn. Đổi mirror khi host cũ chết là đổi ô này. |
| `host` | Host trang web nguồn (dùng khi cạo web / dựng link). |
| `cookie` | Cookie dán thêm khi nguồn chặn bot hoặc đòi đăng nhập. |
| `cache_time` / `cacheSeconds` | Giữ kết quả resolve bao lâu (giây). Link ký theo giờ thì cache phải NGẮN hơn hạn link. |
| `geo_hide` | Tự ẩn nguồn khi bị chặn địa lý thay vì báo lỗi. |

### Nhóm proxy / phát (dễ nhầm nhất)

| Key | Nghĩa |
|-----|-------|
| `useproxy` | Nguồn dùng proxy để **tải danh sách/link** (vượt chặn IP hosting). |
| `useproxystream` | Dùng proxy cả khi **phát** (tốn băng thông server, chỉ bật khi máy xem không tới được upstream). |
| `streamproxy` | Phát qua proxy của lampac (server rewrite playlist). Tắt = máy xem tải trực tiếp upstream. |
| `rch_access` | Ai được dùng RCH - nhờ máy client tải hộ (`apk` = app, `cors`/`web` = web). |
| `stream_access` | Ai được xem stream (`apk`, `cors`/`web`). |
| `rchstreamproxy` | Kênh RCH đi qua proxy (`web`, `phone`, `tv`, `all`). |
| `streamproxy_preview` | Proxy cả ảnh preview/poster. |
| `rch` (mục riêng) | Remote Client Helper: nhờ điện thoại tải hộ các host chặn IP datacenter (vd Chaturbate). |
| `proxy` (mục riêng) | Proxy server dùng để **tải** (list `ip:port`, `url` hoặc `file`). |
| `serverproxy` (mục riêng) | Proxy server dùng để **phát** (`/proxy/`): `enable`, `cache_hls`, `showOrigUri` (bật tạm để soi URL gốc upstream rồi tắt). |
| `priorityBrowser` | Kênh tải ưu tiên (`http` = HTTP thường, thay vì mở trình duyệt Chromium). |
| `rhub` | Hub trung gian cho một số nguồn. |
| `apn` / `apnstream` | Kênh proxy riêng của lampac cho client có hỗ trợ. |

### Site NextHUB (yaml: xasiat, vnchich...)

File gốc nằm `Modules/NextHUB/sites/*.yaml`. Panel chỉ cho sửa các ô override
an toàn (`enable`, `displayname`, `host`, `displayindex`, `streamproxy`,
`stream_access`, `rch_access`, `rchstreamproxy`, `streamproxy_preview`,
`useproxy`, `useproxystream`, `priorityBrowser`, `rhub`, ...). Sửa sâu
(menu/list/search/view) phải sửa thẳng file yaml rồi restart.

### Hệ thống hay đụng

| Mục | Ô quan trọng |
|-----|--------------|
| `listen` | `port` (9118), `ip`. Đổi port là đổi luôn URL plugin/app. |
| `cub` | `api_key` TMDB (trống là liệt kê mùa ENG rỗng), `mirror`, `viewru` (false = tiêu đề Anh). |
| `PidTor` | `torrs` (TS đang dùng — remote Oracle của user, restore cũ hay làm mất), `play_timeout` (0 = tắt chờ torrent). |
| `TorrServer` | TS local (`enable`, `tsport`, `CacheSize`, `PreloadCache`, `TorrentDisconnectTimeout`). |
| `JacRed` | Nguồn torrent Red/Jackett (`apikey`, `disableJackett`). |
| `Jackett` | Tracker local (port 9117, `api_key` lấy từ ServerConfig.json của Jackett). |
| `gst` | Transcode: `enable`, `transcodeAV1/H264/H265/VP9`, `hdr_to_sdr`. Tắt là `/gst/*` 403 hẳn. |
| `online` | `with_search` (nguồn nào vào tìm kiếm chung). |
| `accsdb` | Tài khoản, giới hạn request, khóa IP. |
| `cache` / `Staticache` | Cache RAM/đĩa. Sửa plugin JS mà app vẫn cũ là do 2 lớp này + cache WebView. |
| `GC` | Giới hạn RAM cho build Roslyn trong container. |
| `chromium` | Trình duyệt headless cho site khó (85po 4K). |
| `LampaWeb` | `initPlugins` (plugin nào nạp cho app: online, sisi, gst, sub...). Tắt ở đây không gỡ plugin khỏi app đã cài. |
