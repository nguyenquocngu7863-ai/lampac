# Lampac NextGen cho Termux (Android)

Bản hướng dẫn này dành cho cách chạy Lampac trên **Android qua Termux**. Script [`setup-termux.sh`](setup-termux.sh) tạo một Ubuntu bằng `proot-distro`, cài .NET 10 và chạy Lampac bên trong Ubuntu đó.

> Đây là cách chạy phù hợp để tự dùng trên điện thoại/TV box Android. Android có thể dừng tiến trình nền để tiết kiệm pin; không nên xem đây là máy chủ luôn hoạt động 24/7.

## Yêu cầu

- Thiết bị Android 64-bit (`arm64` là phổ biến; script cũng hỗ trợ `amd64`).
- Cài **Termux từ F-Droid**: <https://f-droid.org/packages/com.termux/>. Không dùng bản Termux cũ trên Google Play.
- Kết nối Internet ổn định, còn vài GB bộ nhớ trống và pin/sạc đủ trong lần cài đầu.
- Nên tắt tối ưu pin cho Termux nếu muốn server chạy lâu hơn.

## Cài đặt nhanh

> Nhánh đang dùng: **`arena/01a06710-lampac`** — nhánh thử nghiệm đang giữ module `VidCore` và bản sửa `lampac update`.
> Đừng clone `main` — `main` đang chậm hơn và thiếu bản vá. Nhánh dự phòng: **`arena/01a06710-lampac`**.
>
> `LAMPAC_CUSTOM_SOURCE_BASE` (nơi `--sync` / `--sync-all` / `install_custom_modules()` tải module về máy) cũng mặc định
> trỏ nhánh 5799. Muốn quay lại `arena/01a06710-lampac` sau khi merge VidCore: chỉ cần `export`, không phải sửa script.

Mở Termux, tải script rồi chạy:

```bash
pkg update -y && pkg install -y git curl
git clone --depth 1 --branch arena/01a06710-lampac https://github.com/nguyenquocngu7863-ai/lampac.git
cd lampac
bash setup-termux.sh --install
```

Sau khi cài xong, script hỏi có khởi động Lampac ngay không. Chọn `Y` hoặc chỉ nhấn Enter để chạy ngay.

### Cài đặt nhưng chưa chạy

```bash
bash setup-termux.sh --install
```

### Đổi port hoặc mật khẩu root ngay từ đầu

```bash
LAMPAC_PORT=8080 LAMPAC_PASSWD='mat-khau-cua-ban' bash setup-termux.sh --install
```

- Port mặc định: `9118`.
- Mật khẩu mặc định: `lampac`. Hãy đổi bằng `LAMPAC_PASSWD` khi cài mới, hoặc sửa file cấu hình/mật khẩu sau khi cài.

## Script làm gì?

Khi chạy lần đầu, `setup-termux.sh` thực hiện tuần tự các bước sau:

1. Cập nhật package của Termux, cài `proot-distro`, `git`, `curl`, `wget`.
2. Cài Ubuntu trong `proot-distro` (hoặc sửa/cài lại Ubuntu nếu môi trường đang hỏng).
3. Trong Ubuntu, cài các thư viện cần thiết, GStreamer và **ASP.NET Core Runtime .NET 10** tại `/opt/dotnet`.
4. Tải bản phát hành Lampac NextGen mới nhất, giải nén vào `/root/lampac` trong Ubuntu.
5. Tạo `init.conf` tối ưu cho Termux: `lowMemoryMode`, GStreamer; mặc định đặt `disableEng: true` để ẩn nhóm ENG nhưng vẫn bật Chromium với đường dẫn `/usr/bin/google-chrome-stable` cho các module cần Playwright.
6. Xoá nguồn đã ngừng dùng/lỗi **NguonC**, rồi đồng bộ module tuỳ biến: **KKPhim, K20, VsMov, AIOStreams, GStreamer** và **LampaWeb/StremioSub**; mã các nguồn ENG vẫn được giữ để phát triển nhưng không xuất hiện khi `disableEng` đang bật.
7. Cài controller tùy chọn cho **AIOStreams** (port `3002`) và **Jackett** (port `9117`).
8. Tạo các lệnh `lampac`, `aio` và `jackett` để quản lý từ Termux.

Lần cài đầu có thể mất vài phút vì phải tải Ubuntu, runtime .NET và bản phát hành Lampac. AIOStreams/Jackett chỉ được tải khi bạn chạy lệnh cài riêng. Không đóng Termux trong lúc cài.

## Quản lý Lampac sau khi cài

```bash
lampac start     # Khởi động; Ctrl+C để dừng khi chạy ở terminal hiện tại
lampac stop      # Dừng tiến trình Lampac
lampac status    # Kiểm tra trạng thái
lampac info      # Hiện URL, port và vị trí config
lampac config    # Mở init.conf bằng nano trong Ubuntu
lampac update    # Cập nhật Lampac và đồng bộ lại thiết lập tuỳ biến
```

> **Sửa lỗi (2026-08-31):** `lampac update` trước đây chạy một khối shell **inline** tham chiếu
> 22 biến kiểu `$KKBase`, `$VideasyBase`, `$LampaWebBase`, `$BaseConfUrl`… mà **không biến nào được
> định nghĩa** trong `setup-termux.sh`. Hệ quả: URL curl sinh ra thiếu host (`curl: (3) URL rejected:
> No host part`) và `set -euo pipefail` dừng ngay lệnh đầu tiên ⇒ lệnh update không đồng bộ được module nào.
> Nay `lampac update` delegate thẳng `bash setup-termux.sh --update` (giống cách `lampac sync` /
> `lampac sync-all` đang làm), tức đi qua `install_custom_modules()` nơi `${CUSTOM_SOURCE_BASE}`
> được nội suy đúng. Khối cũ không còn trong tree; đối chiếu bằng `git show 8ed1ad8:setup-termux.sh` (dòng 924-1192).

Lệnh `lampac start` in ra địa chỉ local, địa chỉ mạng LAN và cổng đang dùng. Thông thường bạn truy cập từ thiết bị khác cùng Wi-Fi qua:

```text
http://IP_CUA_ANDROID:9118
```

Nếu không thấy địa chỉ IP, chạy trong Termux:

```bash
ip addr show wlan0
```

## Cập nhật và đồng bộ module

Quy trình đầy đủ giữa Termux, GitHub và bản Lampac đang chạy nằm trong [`docs/TERMUX-GITHUB-LAMPAC.md`](docs/TERMUX-GITHUB-LAMPAC.md). Đọc mục này khi cần đưa branch agent vào `main`, xử lý thay đổi local hoặc xoá module cũ trong Ubuntu proot.

Có **ba lệnh**. Script trên điện thoại tự chứa danh sách file, nên sau mỗi bản vá phải tải lại `setup-termux.sh` một lần rồi mới sync — nếu không, `--sync` vẫn dùng list cũ.

> ⚠️ **Điểm yếu quy trình (đã dính thật — 2026-08-30):** patch thêm **file mới**
> (ví dụ `autotracks.js`) mà chạy `--sync` bằng script cũ thì script kéo được
> `ApiController.cs` mới (đăng ký plugin) nhưng **không kéo file js mới** →
> Lampa hiện plugin với trạng thái **404 Lỗi**. Triệu chứng: thẻ plugin trong
> Tiện ích mở rộng báo `404`. Cách phòng: **luôn tải lại script trước khi
> sync**, gộp thành một lệnh duy nhất:
>
> ```bash
> curl -fsSL "https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac/setup-termux.sh?cb=$(date +%s)" -o setup-termux.sh \
>   && bash setup-termux.sh --sync && lampac stop && lampac start
> ```
>
> (`?cb=` để né cache của raw.githubusercontent.com.)
>
> **Clone nhánh chưa đủ.** `--sync` / `--sync-all` / `--update` curl file từ
> `LAMPAC_CUSTOM_SOURCE_BASE`, không phải từ `git branch` đang checkout.
> Mặc định hiện tại: nhánh `arena/01a06710-lampac`. Đừng dùng `main`.
> Dự phòng: `arena/01a06710-lampac`.

```bash
# Bước 0 — lấy script mới (làm một lần sau mỗi patch)
curl -fsSL https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac/setup-termux.sh -o setup-termux.sh
```

### `LAMPAC_CUSTOM_SOURCE_BASE` — nguồn file khi sync

`git clone` / `git pull` chỉ cập nhật repo Termux (`~/lampac`). Bản Lampac đang chạy trong Ubuntu (`/root/lampac`) được vá bằng curl, lấy URL gốc từ biến này.

Mặc định trong script:

```text
https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac
```

Kiểm tra đang trỏ đâu:

```bash
echo "${LAMPAC_CUSTOM_SOURCE_BASE:-https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac}"
```

Dùng **một lần** (fork riêng, hoặc quay về nhánh dự phòng 63):

```bash
LAMPAC_CUSTOM_SOURCE_BASE='https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac' \
  bash setup-termux.sh --sync
lampac stop && lampac start
```

Gắn **vĩnh viễn** trong Termux:

```bash
echo "export LAMPAC_CUSTOM_SOURCE_BASE=https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac" >> ~/.bashrc
source ~/.bashrc
echo "$LAMPAC_CUSTOM_SOURCE_BASE"
bash setup-termux.sh --sync && lampac stop && lampac start
```

Phải khớp **hai chỗ**: URL tải `setup-termux.sh` và `LAMPAC_CUSTOM_SOURCE_BASE`. Chỉ đổi một bên thì script mới vẫn kéo file từ nhánh cũ (hoặc ngược lại).

### Curl một file thẳng vào Ubuntu

`--sync` chỉ kéo **list có sẵn trong script**. File mới, file C# vừa vá, hoặc lần `--sync` cũ không đụng VidLink/`lampainit.js` thì file trên máy không đổi. Khi chat đưa **lệnh dài** `proot-distro login ubuntu -- bash -lc ... curl ...`, đó **không** phải cài lại Lampac — chỉ chép **một file** từ GitHub (nhánh đang dùng) vào bản đang chạy.

Hai thư mục khác nhau:

| Nơi | Đường dẫn | Việc gì |
|---|---|---|
| Termux (git) | `~/lampac` = `/data/data/com.termux/files/home/lampac` | clone/script. **Không** phải server |
| Ubuntu proot | `/root/lampac` | Lampac thật. Module ở `module/…` (và `mods/…` nếu có) |

`curl -o ~/lampac/module/...` ghi vào Termux → thường `curl: (23) write` / `No such file`. Phải vào Ubuntu.

Mẫu — **URL ghi cứng trong Ubuntu** (copy nguyên khối). Không dùng `\$LAMPAC_CUSTOM_SOURCE_BASE` trong `bash -lc`: Ubuntu **không** nhận biến Termux → `curl: (3) URL rejected: No host part`.

```bash
proot-distro login ubuntu -- bash -lc '
set -e
base=https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac
stamp=$(date +%s)
curl -fSL --retry 3 "$base/Modules/LampaWeb/plugins/lampainit.js?cb=$stamp" \
  -o /root/lampac/module/LampaWeb/plugins/lampainit.js.tmp
mv /root/lampac/module/LampaWeb/plugins/lampainit.js.tmp \
   /root/lampac/module/LampaWeb/plugins/lampainit.js
if [ -d /root/lampac/mods/LampaWeb/plugins ]; then
  cp /root/lampac/module/LampaWeb/plugins/lampainit.js \
     /root/lampac/mods/LampaWeb/plugins/lampainit.js
fi
grep -n hasCyrillicText /root/lampac/module/LampaWeb/plugins/lampainit.js
'
```

Từng dòng:

1. `base=https://…/arena/01a06710-lampac` — URL raw **trong Ubuntu**. Giữ nhánh hiện tại; 63 chỉ dự phòng. `export` Termux vẫn dùng cho `--sync`, không cần cho lệnh này.
2. `stamp=$(date +%s)` rồi `?cb=$stamp` — chạy **trong** Ubuntu; né cache `raw.githubusercontent.com`.
3. `proot-distro login ubuntu -- bash -lc '…'` — nháy **đơn** quanh script. Nháy kép + `\$VAR` dễ mất host.
4. `curl … -o ….tmp` rồi `mv` — ghi xong mới thay file, tránh file dở khi mạng đứt.
5. `if [ -d mods/… ]` — có overlay `mods/` thì chép; không có thì bỏ qua. Đừng copy `&amp;&amp;` từ web.
6. `grep` — xác nhận file mới. Không thấy = chưa vào đúng chỗ.

**Nhiều file trong một lệnh.** Muốn chép vài file khác module, gộp các cặp
`curl … -o ….tmp` + `mv` vào cùng `bash -lc '…'` (mỗi file một cặp, `set -e` dừng
sớm nếu một file lỗi). Bản vá hiện tại cho **GStreamer `gst.js`** và **Cam4
`cam4.yaml`** copy nguyên khối:

```bash
proot-distro login ubuntu -- bash -lc '
set -e
base=https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06ac7-lampac
stamp=$(date +%s)

# gst.js (plugin GStreamer)
curl -fSL --retry 3 "$base/Modules/GStreamer/plugins/gst.js?cb=$stamp" \
  -o /root/lampac/module/GStreamer/plugins/gst.js.tmp
mv /root/lampac/module/GStreamer/plugins/gst.js.tmp /root/lampac/module/GStreamer/plugins/gst.js
[ -d /root/lampac/mods/GStreamer/plugins ] && cp /root/lampac/module/GStreamer/plugins/gst.js /root/lampac/mods/GStreamer/plugins/gst.js

# cam4.yaml (site NextHUB)
curl -fSL --retry 3 "$base/Modules/NextHUB/sites/cam4.yaml?cb=$stamp" \
  -o /root/lampac/module/NextHUB/sites/cam4.yaml.tmp
mv /root/lampac/module/NextHUB/sites/cam4.yaml.tmp /root/lampac/module/NextHUB/sites/cam4.yaml
[ -d /root/lampac/mods/NextHUB/sites ] && cp /root/lampac/module/NextHUB/sites/cam4.yaml /root/lampac/mods/NextHUB/sites/cam4.yaml

# Xác nhận file mới đã vào đúng chỗ
echo "--- gst.js ---"
grep -n "Chọn audio" /root/lampac/module/GStreamer/plugins/gst.js
echo "--- cam4.yaml (phải thấy total_pages: 1) ---"
grep -n "total_pages: 1" /root/lampac/module/NextHUB/sites/cam4.yaml
'
```

- `base` phải khớp nhánh **đang chạy** (kiểm tra `git branch --show-current` và
  `LAMPAC_CUSTOM_SOURCE_BASE`). Mẫu trên dùng nhánh làm việc `arena/01a06ac7-lampac`.
- `[ -d /root/lampac/mods/… ] && cp …` — chỉ chép sang overlay `mods/` khi nó tồn tại;
  không có thì bỏ qua (đừng copy `&amp;&amp;` lẫn từ web).
- Toàn khối `echo` / `grep … || echo "OK"` ở cuối là **mẫu xác nhận của bản vá cụ thể**
  (`gst.js` → "Chọn audio", `cam4.yaml` → có "total_pages: 1" — bản vá phân trang cam4:
  tắt 30 trang ảo lặp nội dung, danh mục đổi thành đường dẫn thật). Đây **không phải** lệnh cứng:
  khi chép file khác thì **đổi dòng grep** cho khớp nội dung đang sửa (chuỗi mới cần có / chuỗi
  cũ cần mất). Muốn "chỉ cần file, không cần kiểm tra" thì bỏ hẳn phần `echo`/`grep` cuối.
- `grep … || echo "OK"` — tránh để `set -e` dừng khi file mới **không còn** chuỗi cũ
  (điều mình muốn). Nếu kiểm tra chuỗi **phải có** thì bỏ `|| echo`, để grep tự fail.
- Sau đó: `lampac stop && lampac start` (NextHUB đọc YAML lúc khởi động; `gst.js` là
  plugin client — thoát hẳn Lampa / hard refresh là đủ, restart không hại).

Sau khi curl:

- File **C#** (`.cs`, controller): `lampac stop && lampac start` (compile lúc start).
- File **JS** plugin (`lampainit.js`, `vietnamese.js`, …): hard refresh / thoát hẳn Lampa. Restart Lampac không bắt buộc nhưng không hại.

Không `cd ~/lampac` rồi curl vào đó. Không bỏ `export` của nhánh đang dùng.

| Lệnh | Khi nào dùng | Tải gì |
|---|---|---|
| `--sync` | Vá nhỏ vừa ship (Chaturbate, proxy 18+, NextHUB YAML, …) | **Chỉ file của bản vá mới nhất.** Không tải Chrome, hls.js, KKPhim, LampaWeb |
| `--sync-all` | Muốn lấy lại **mọi** module tuỳ biến / Chrome bị hỏng / plugin LampaWeb | Chrome/Chromium + KKPhim/K20/VsMov + SISI + NextHUB + LampaWeb/hls.js + AdminPanel |
| `--update` | Có release Lampac mới | `lampac-nextgen.zip` rồi chạy cùng bước với `--sync-all` |

### Sync nhẹ — chỉ file bản vá mới nhất

Nhanh. Dùng khi chat bảo “chạy `--sync`”.

```bash
bash setup-termux.sh --sync
lampac stop && lampac start
```

Hoặc một lệnh (vẫn nên tải script mới trước):

```bash
curl -fsSL https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac/setup-termux.sh | bash -s -- --sync
lampac stop && lampac start
```

List file của `--sync` nằm trong `sync_latest_modules()` của script; mỗi patch sẽ thay list này. Bản vá hiện tại kéo `Modules/LampaWeb/plugins/online-compact.js` (card danh sách Online ở chế độ dọc: chiều cao cố định, mô tả phim 1 dòng + thông tin file 1 dòng, poster vuông 1:1 dính viền card; chế độ ngang giữ nguyên bố cục gốc). Không dùng `--sync` khi cần đồng bộ tiếng Việt lõi, Chrome, hls.js hoặc Jackett/AIO controller.

### Sync đầy đủ — mọi module tuỳ biến + runtime trình duyệt

```bash
bash setup-termux.sh --sync-all
lampac stop && lampac start
```

Lấy lại **KKPhim, K20, VsMov, WebStreamr, Sootio, AIOStreams, GStreamer, SISI, Eporner, Chaturbate, NextHUB YAML, LampaWeb/StremioSub/hls.js**, sửa Chrome nếu thiếu. Nặng hơn `--sync`, dùng khi cài lại module hoặc vá không nằm trong list nhẹ.

### Cập nhật đầy đủ, không mất dữ liệu

`--update` thay toàn bộ release trong `/root/lampac`. Không backup/restore thư mục `module/`, `Core.dll`, `Shared.dll` hoặc `wwwroot/lampa-main`, vì đây là code phải lấy từ bản mới. Tách backup thành hai nhóm:

- **Dữ liệu an toàn:** cấu hình, user, database, bookmark, TorrServer và keystore APK — tự restore sau update.
- **Override cần review:** `mods/` và `plugins/` — chỉ giải nén để kiểm tra, không chép đè tự động. Module cùng tên trong `mods/` được load trước `module/`, nên restore mù có thể vô hiệu hóa bản update.

#### 1. Dừng dịch vụ

```bash
lampac stop
aio stop
jackett stop
```

Jackett có lifecycle độc lập nên cần dừng bằng lệnh riêng.

#### 2. Tạo backup ngoài thư mục Lampac

```bash
proot-distro login ubuntu -- bash -lc '
  set -euo pipefail
  mkdir -p /root/lampac-backups

  stamp=$(date +%Y%m%d-%H%M%S)
  data_backup="/root/lampac-backups/data-${stamp}.tar.gz"
  override_backup="/root/lampac-backups/overrides-${stamp}.tar.gz"

  cd /root/lampac

  data_items=()
  for path in \
    init.conf init.yaml passwd users.json \
    database data/ts wwwroot/bookmarks
  do
    [ -e "$path" ] && data_items+=("$path")
  done

  [ "${#data_items[@]}" -gt 0 ] || {
    echo "Không tìm thấy dữ liệu để backup"
    exit 1
  }

  tar -czf "$data_backup" "${data_items[@]}"
  printf "%s\n" "$data_backup" > /root/lampac-backups/LATEST_DATA

  override_items=()
  for path in mods plugins; do
    [ -e "$path" ] && override_items+=("$path")
  done

  if [ "${#override_items[@]}" -gt 0 ]; then
    tar -czf "$override_backup" "${override_items[@]}"
    printf "%s\n" "$override_backup" > /root/lampac-backups/LATEST_OVERRIDES
    echo "Override backup: $override_backup"
    du -h "$override_backup"
  fi

  echo "Data backup: $data_backup"
  du -h "$data_backup"
  tar -tzf "$data_backup" | sed -n "1,30p"
'
```

`database/` chứa bookmark/timecode, Storage và keystore ký Lampac APK. Không backup `cache/`: cache có thể tạo lại và cache module cũ không nên quay lại sau update.

#### 3. Tải release mới và áp dụng lại phần tùy biến

Không cần clone Git:

```bash
curl -fsSL https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac/setup-termux.sh \
  | bash -s -- --update
```

Script tải `lampac-nextgen.zip` mới nhất, thay Core/module chính thức rồi đồng bộ lại các module tùy biến của branch này, bao gồm các site definition NextHUB đã vá.

#### 4. Chỉ khôi phục dữ liệu an toàn

```bash
proot-distro login ubuntu -- bash -lc '
  set -euo pipefail
  backup=$(cat /root/lampac-backups/LATEST_DATA)
  [ -f "$backup" ] || {
    echo "Không tìm thấy backup: $backup"
    exit 1
  }

  echo "Restore data: $backup"
  tar -xzf "$backup" -C /root/lampac
'
```

`init.conf` được giữ vì chứa cấu hình người dùng, nhưng section riêng của module vẫn có thể override default mới. Sau update, tìm domain/setting cũ nếu hành vi không thay đổi:

```bash
proot-distro login ubuntu -- grep -niE \
  "sex-studentki|overridehost|host" /root/lampac/init.conf
```

#### 5. Review `mods/` và `plugins/`, không restore mù

```bash
proot-distro login ubuntu -- bash -lc '
  set -euo pipefail
  rm -rf /root/lampac-override-review
  mkdir -p /root/lampac-override-review

  if [ -f /root/lampac-backups/LATEST_OVERRIDES ]; then
    backup=$(cat /root/lampac-backups/LATEST_OVERRIDES)
    tar -xzf "$backup" -C /root/lampac-override-review
    find /root/lampac-override-review -maxdepth 3 -type f | sed -n "1,80p"
  else
    echo "Không có mods/plugins cũ"
  fi
'
```

Chỉ copy từng mod/plugin thật sự tự viết sau khi đã kiểm tra nó không trùng module mới. Ví dụ kiểm tra NextHUB có bị mod cũ che không:

```bash
proot-distro login ubuntu -- bash -lc '
  find /root/lampac/mods /root/lampac/module \
    -path "*/NextHUB/manifest.json" -print 2>/dev/null
'
```

Nếu có cả `mods/NextHUB` và `module/NextHUB`, bản trong `mods/` có thể được load trước; nên di chuyển bản cũ ra thư mục review thay vì giữ hai bản.

#### 6. Khởi động và xác minh code mới

```bash
lampac start
```

Trong session Termux khác:

```bash
lampac status
aio status
jackett status

proot-distro login ubuntu -- grep "^host:" \
  /root/lampac/module/NextHUB/sites/sex-studentki.yaml
```

Kết quả NextHUB mới phải là:

```text
host: https://sex-studentki.one
```

Nếu cần Jackett:

```bash
jackett start
```

Không xóa `/root/lampac-backups/` cho tới khi đã kiểm tra bookmark, user, TorrServer và APK. Backup override vẫn còn nguyên để lấy lại từng file khi cần, nhưng không tự động đè code mới.

### Chỉ chạy server

```bash
bash setup-termux.sh --run
# hoặc
lampac start
```

## Cấu hình Termux

File cấu hình nằm **bên trong Ubuntu proot**:

```text
/root/lampac/init.conf
```

Cách đơn giản nhất để sửa:

```bash
lampac config
```

Cấu hình mặc định của script đã bao gồm:

```jsonc
{
  "listen": {
    "ip": "0.0.0.0",
    "port": 9118,
    "scheme": "http"
  },
  "lowMemoryMode": true,
  "disableEng": true,
  "gst": {
    "enable": true,
    "hdr_to_sdr": true,
    "useGpu": true,
    "hardwareAcceleration": false,
    "x264Ultrafast": true,
    "segment_seconds": 2,
    "segment_buffer": 4
  },
  "chromium": { "enable": false }
}
```

Thiết lập này bật sẵn HDR-to-SDR bằng plugin native đã build, dùng OpenCL nếu thiết bị có driver và tự fallback về CPU. Nếu máy yếu, không nên bật đồng thời AIOStreams, Jackett, nhiều module nặng hoặc transcoding khác.

### Chẩn đoán GStreamer copy mode

GStreamer chỉ proxy/remux nguồn MKV/WebM nhận diện được; nó không ép mọi MP4, HLS hoặc live stream vào copy mode. Nếu log có:

```text
External plugin loader failed
GStreamer: add rejected source. Reason=probe
```

thì `gst-discoverer-1.0` chưa chạy được `gst-plugin-scanner`, nên lỗi xảy ra trước khi kiểm tra container và codec. Đây không phải do `hdr_to_sdr`, `useGpu` hoặc các tùy chọn `transcode*`. Trên Termux/Ubuntu proot, xem hướng dẫn sửa và lệnh kiểm tra trong [`Modules/GStreamer/README.md`](Modules/GStreamer/README.md#termux-ошибка-reasonprobe), rồi restart Lampac.

## Sổ tay cấu hình proxy cho từng nguồn

Lampac có hai lớp thường bị gọi chung là “proxy”, nhưng mục đích khác nhau:

- `streamproxy: true`: điện thoại/Lampac tải video rồi chuyển tiếp cho player. Dùng để gắn `Referer`, `Origin`, User-Agent, xử lý CORS/hotlink. **Không đổi IP ra Internet**.
- `useproxy` / `useproxystream`: request đi ra Internet qua một HTTP/SOCKS proxy bên ngoài. Dùng khi domain/CDN chặn IP hoặc giới hạn vùng.

### Chọn cấu hình theo lỗi

| Triệu chứng | Cấu hình nên dùng |
|---|---|
| Danh sách mở được, video 403/không hỗ trợ | `streamproxy` + `headers_stream` |
| Trang nguồn/search bị 403 hoặc chặn vùng | `useproxy` |
| Cả trang nguồn và CDN video đều chặn IP | `useproxy` + `useproxystream` + `streamproxy` |
| Chỉ ảnh/poster bị chặn | `headers_image`, hoặc image proxy của server |
| Cloudflare yêu cầu JS/cookie | Playwright/FlareSolverr; proxy IP đơn thuần có thể chưa đủ |

### 1. Chỉ proxy stream qua Lampac

Đây là cấu hình nhẹ nhất và nên thử đầu tiên. Ví dụ cho một section nguồn:

```jsonc
"TenNguon": {
  "enable": true,
  "streamproxy": true,
  "headers_stream": {
    "Referer": "https://website.example/",
    "Origin": "https://website.example",
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
    "Accept": "video/webm,video/mp4,video/*;q=0.9,*/*;q=0.5"
  }
}
```

Sau khi áp dụng, URL player phải đi qua:

```text
http://IP_LAMPAC:9118/proxy/...
```

Nếu player vẫn nhận URL CDN trực tiếp, kiểm tra `Kit` có ghi đè `streamproxy` hay không. Với nguồn tự quản lý có thể cần:

```jsonc
"kit": false,
"rhub": false,
"qualitys_proxy": false,
"url_reserve": false
```

### 2. Khai báo proxy Internet dùng chung

Trong root của `init.conf`:

```jsonc
"globalproxy": [
  {
    "name": "proxy-eu",
    "BypassOnLocal": true,
    "maxRequestError": 2,
    "list": [
      "http://username:password@proxy.example:3128"
    ]
  }
]
```

Có thể khai báo nhiều endpoint để Lampac chọn và đổi sau lỗi:

```jsonc
"list": [
  "http://user:pass@host-1:3128",
  "http://user:pass@host-2:3128",
  "socks5://user:pass@host-3:1080"
]
```

Nếu username/password chứa `@`, `:`, `/` hoặc ký tự đặc biệt, phải URL-encode chúng. Không commit hoặc gửi proxy credential vào Git/chat/log.

### 3. Gắn proxy dùng chung vào một nguồn

```jsonc
"TenNguon": {
  "enable": true,
  "useproxy": true,
  "globalnameproxy": "proxy-eu"
}
```

`useproxy` áp dụng cho request trang, API, search và resolver của nguồn. Stream chưa chắc đi qua proxy ngoài.

### 4. Cho cả stream đi qua proxy Internet

Chỉ dùng khi CDN cũng chặn IP server:

```jsonc
"TenNguon": {
  "enable": true,
  "useproxy": true,
  "useproxystream": true,
  "streamproxy": true,
  "globalnameproxy": "proxy-eu",
  "headers_stream": {
    "Referer": "https://website.example/",
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
  }
}
```

Flow lúc này:

```text
Player → Lampac /proxy → external proxy → CDN
```

Cách này tốn băng thông, tăng độ trễ và tải CPU/RAM; không bật hàng loạt cho mọi nguồn.

### 5. Proxy riêng, không cần `globalproxy`

```jsonc
"TenNguon": {
  "enable": true,
  "useproxy": true,
  "proxy": {
    "BypassOnLocal": true,
    "list": [
      "http://user:pass@proxy.example:3128"
    ]
  }
}
```

Hoặc credentials tách riêng:

```jsonc
"proxy": {
  "useAuth": true,
  "username": "user",
  "password": "pass",
  "list": ["http://proxy.example:3128"]
}
```

Nếu nguồn có `proxy.list`, nó được ưu tiên trước `globalnameproxy` và root `proxy`.

### 6. Root proxy mặc định

Có thể khai báo:

```jsonc
"proxy": {
  "list": ["http://user:pass@proxy.example:3128"]
}
```

Nguồn có `useproxy: true` nhưng không có `proxy.list` hoặc `globalnameproxy` sẽ dùng root proxy này. Chỉ nên dùng khi nhiều nguồn thật sự cần cùng một tuyến proxy.

### 7. Kiểm tra proxy trước khi nhập Admin Panel

HTTP proxy:

```bash
proot-distro login ubuntu -- curl -fsS --max-time 15 \
  -x 'http://user:pass@proxy.example:3128' \
  https://api.ipify.org
```

SOCKS5, có DNS qua proxy:

```bash
proot-distro login ubuntu -- curl -fsS --max-time 15 \
  --proxy 'socks5h://user:pass@proxy.example:1080' \
  https://api.ipify.org
```

Lệnh phải in ra IP của proxy, không phải IP mạng điện thoại.

### 8. Áp dụng và kiểm tra cấu hình hiệu lực

Sau khi lưu Admin Panel, restart để loại trừ cache/module state:

```bash
lampac stop
lampac start
```

Kiểm tra section đã merge vào `current.conf`:

```bash
proot-distro login ubuntu -- python3 -c '
import json, pprint
with open("/root/lampac/current.conf", encoding="utf-8") as f:
    data = json.load(f)
pprint.pp(data.get("TenNguon"))
'
```

Thay `TenNguon` bằng đúng tên section trong Admin Panel, phân biệt hoa/thường theo catalog. Nếu `init.yaml` cũng tồn tại, nó có thể ghi đè `init.conf`; script sẽ cảnh báo khi sync.

### Lưu ý an toàn và hiệu năng

- Không dùng proxy công cộng miễn phí cho token/cookie/tài khoản.
- `streamproxy` khiến dữ liệu video đi qua điện thoại; tốc độ tối đa phụ thuộc cả download lẫn upload nội bộ.
- `useproxystream` có thể tiêu tốn rất nhiều traffic của proxy trả phí.
- Không bật proxy cho nguồn đang chạy bình thường.
- Test từng nguồn và từng lỗi; chỉ nâng từ `streamproxy` lên external proxy khi thật sự cần đổi IP.

### NextHUB (YAML) — những site đã bật `streamproxy`

Không bật hàng loạt 50+ YAML. Chỉ những nguồn Android không phát thẳng CDN:

| Site | Lý do |
|---|---|
| PornOne, NoodleMagazine, SEX Studentki, 24rolika, FapGuru, Uporno, Beeg, PerfektDamen | Đã bật từ trước (`streamproxy` / `geostreamproxy: ALL`) |
| **Cam4** | Live HLS trong playlist — cùng kiểu Chaturbate |
| **Oxax, WatchPorn** | YAML đã có `headers_stream` (Referer) nhưng chưa proxy → APK không gửi header |
| **ProstoPorno, yaeby** | KVS `/get_file/` + `bindingToIP` — redirect khóa Referer/IP |

Tube còn lại (Youjizz, Porndig, Porn4days, đa số KVS Nga với `rchstreamproxy: web`) để APK phát thẳng. `rchstreamproxy: web` **không** phải `streamproxy` cho điện thoại.

## Việt hóa bền vững qua các lần update

Bản Việt hóa gồm hai lớp. File ngôn ngữ lõi độc lập `Modules/LampaWeb/lang/vi.js` có cùng toàn bộ key với `en.js`, không import/spread/fallback runtime. File được chèn vào **file gốc** của frontend Lampa:

- `wwwroot/lampa-main/lang/vi.js` — dictionary Lampa tải lúc boot (`./lang/{code}.js`)
- `wwwroot/lampa-main/lang/meta.js` — registry nguồn
- `wwwroot/lampa-main/app.min.js` — Lampa bundle `meta.languages` vào đây; chỉ vá `meta.js` thì bộ chọn ngôn ngữ lúc boot vẫn không có `vi`

Người dùng tự chọn **Tiếng Việt** trong Interface; Lampa reload rồi tải `lang/vi.js` theo luồng gốc. Hệ thống không tự đổi ngôn ngữ. Plugin `/vietnamese.js` **không** là lớp ngôn ngữ lõi: chỉ dịch chuỗi hardcode của addon (Online, SISI, v.v.). Không gọi `Lang.addCodes` trong overlay vì API đó xóa dictionary `vi` vừa load.

Không sửa trực tiếp file addon upstream chỉ để dịch. Khi `--update` thay release, `--sync-all` sẽ cài lại `vi.js`, vá `meta.js` **và** `app.min.js`, rồi chép overlay `vietnamese.js`. `LampaCron` cũng tự kiểm tra và cài lại language pack gốc sau mỗi lần frontend được tải/cập nhật, tránh race khi thư mục `lampa-main/lang` xuất hiện sau lúc sync. Có thể bật/tắt lớp addon tại **Settings → Interface → Lớp Việt hóa addon**.

Muốn 100% tiếng Việt lõi, mở giao diện Lampac `http://IP:9118` (file gốc đã vá). App Lampa Android (`file:`) vẫn tải `lang/{code}.js` từ GitHub/lampa.mx; plugin không sửa được `app.min.js` đóng gói trong APK.

Khi người dùng chọn `vi`, giao diện Lampac vẫn tiếng Việt. Catalog TMDB (tựa, poster, logo) mặc định English (`tmdb_lang=en`) để khớp nguồn xem; request ảnh dùng `include_image_language=en,null`.

Online và addon thông thường tiếp tục dùng catalog overlay. Riêng SISI được Việt hóa trực tiếp trong `SISI/plugins/*.js` và menu `Modules/Adult/*/Service.cs` để không dịch nhầm tiêu đề video. NextHUB: YAML site Nga đã đổi nhãn sort/category sang tiếng Việt (slug/host/parse giữ nguyên); tube quốc tế chỉ đổi nhãn sort tiếng Nga. `CategoryVi.cs` vẫn dịch nhãn Cyrillic còn lại theo slug lúc server dựng menu; tên playlist/video không đi qua bộ dịch này. Khi upstream update SISI/NextHUB, merge source và giữ catalog Việt tương ứng.

## Giao diện Online gọn trên điện thoại

Plugin built-in `/online-compact.js` được bật mặc định qua `LampaWeb.initPlugins.onlineCompact`. Trên màn hình tối đa 720px, plugin dành thêm chiều ngang cho nội dung, cho title/metadata xuống dòng và tăng khoảng cách dọc để card dễ đọc hơn; không thay đổi model hoặc link phát. Có thể bật/tắt trực tiếp trong **Settings → Interface → Danh sách Online thoáng**.

## Plugin phụ đề

Lampac có SubSense Auto, SubSense, SubFinder và StremioSub. Vì các plugin tự động đều bọc `Lampa.Player.play`, chỉ nên bật **một** provider. Mặc định dùng `stremiosub`; `subsenseAuto`, `subsense` và `subfinder` là opt-in. Server sẽ ưu tiên đúng một provider nếu lỡ bật nhiều cờ, đồng thời client có khóa chung để raw URL cũ không bọc player lần nữa.

## StremioSub — plugin phụ đề built-in

`StremioSub` là plugin built-in của Lampac: **không cài bằng URL jsDelivr trong mục Extensions**. Nếu module AIOStreams đã bật, plugin ưu tiên subtitle resource từ AIO; nếu chưa thì dùng fallback SubDL + SubSource. Sau một lần `--sync-all` và restart Lampac, Lampa nhận plugin nội bộ với tên **StremioSub — SubDL + SubSource/AIOStreams**.

Kiểm tra Lampac đã đưa plugin vào init chưa:

```bash
curl -s http://127.0.0.1:9118/lampainit.js | grep -oE 'StremioSub[^" ]*|stremiosub\.js'
```

Nếu lệnh không in ra `stremiosub.js`, đồng bộ đầy đủ LampaWeb rồi khởi động lại:

```bash
curl -fsSL https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac/setup-termux.sh | bash -s -- --sync-all
lampac stop
lampac start
```

Xóa các card **Untitled** đã cài thủ công từ jsDelivr trước khi mở lại Lampa để không tải trùng plugin.

### Muốn dùng `subsense-auto.js` ngang hàng với `ts.js`

Không thêm cùng lúc URL này vào `customPlugins`. Bật nó trong danh sách built-in:

```json
"LampaWeb": {
  "initPlugins": {
    "torrserver": true,
    "subsenseAuto": true,
    "subsense": false,
    "subfinder": false,
    "stremiosub": false
  }
}
```

Lampac sẽ phục vụ script tại `/subsense-auto.js` và đăng ký nó cùng danh sách với `/ts.js`. Nếu đang dùng SubSense Auto thì phải tắt `subsense`, `subfinder` và `stremiosub`; nếu không, các addon đều có thể gọi phụ đề cho cùng một lần phát. Trong lúc host SubSense trả 502, nên để `subsenseAuto: false` và dùng `stremiosub: true`.

## AIOStreams — cầu nối Stremio tổng quát

Lampac có module **AIOStreams** tùy chọn. Module này chỉ gọi manifest và API Stremio chuẩn qua HTTP; không cần chạy Node.js trong Lampac hoặc Lampa WebView. Ní tạo/cấu hình AIOStreams ở trang của AIO, sau đó nhập manifest URL cá nhân vào AdminPanel:

```json
"AIOStreams": {
  "enable": true,
  "manifest": "https://your-aiostreams-instance/stremio/.../manifest.json",
  "streams": true,
  "subtitles": true,
  "streamproxy": true,
  "timeoutSeconds": 30,
  "maxStreams": 100,
  "cacheSeconds": 120
}
```

Manifest URL có thể chứa UUID, password, API key hoặc token; không đưa nó vào Git, log hoặc chat. Các nguồn cũ như **HDVB, KKPhim, K20, WebStreamr và Sootio** vẫn giữ nguyên làm fallback. AIOStreams chỉ là nguồn riêng thêm vào, không thay thế chúng.

### Local host AIOStreams trên Ubuntu proot

Sau khi chạy `bash setup-termux.sh --sync-all`, cài AIOStreams chính chủ bằng:

```bash
aio install
aio info
```

Bản cài này clone repo chính chủ ở tag ổn định, cài Node/pnpm và build trong `/root/aiostreams`. AIO dùng port nội bộ `3002`; Lampac tự khởi động AIO nếu đã cài. Mở dashboard theo URL `aio info`, cấu hình addon/subtitle trong AIO, rồi dán manifest URL local vào section **AIOStreams** trong AdminPanel Lampac. Các lệnh quản lý:

```bash
aio start
aio stop
aio status
aio logs
```

AIOStreams chạy Node.js riêng, không được nhúng vào Lampac. Bản local dùng SQLite và cần thêm dung lượng/RAM khi build; nếu build source lỗi do native `yencode`, xem log bằng `aio logs`.

## Jackett local — port 9117

Sau `bash setup-termux.sh --sync-all`, cài gói Jackett chính chủ phù hợp với kiến trúc Ubuntu proot:

```bash
jackett install
jackett info
```

Controller tải binary `LinuxARM64`, `LinuxAMDx64` hoặc `LinuxARM32` từ GitHub Releases, giữ dữ liệu tại `/root/.config/Jackett` và chạy dashboard trên:

```text
http://127.0.0.1:9117/UI/Dashboard
```

Trong Admin Panel của Lampac (`http://IP_ANDROID:9118`), mở **Dịch vụ cục bộ → Jackett** rồi nhập:

```json
"Jackett": {
  "enable": true,
  "url": "",
  "port": 9117,
  "api_key": "API_KEY_TỪ_DASHBOARD_JACKETT",
  "proxy_downloads": true
}
```

Mặc định `proxy_downloads: true`: search chỉ proxy JSON nên trả kết quả nhanh. Với kết quả chỉ có link `/dl/`, `jackett.js` đợi đến lúc người dùng bấm phát mới gọi `/jackett/resolve`; Lampac tải đúng torrent đã chọn, tính info-hash SHA-1, giữ tracker và thay link bằng magnet trước khi request `/torrents` được gửi tới TorrServer. Vì vậy TorrServer local lẫn remote đều không phải truy cập ngược URL nội bộ của điện thoại. API key được chèn server-side, không nằm trong URL gửi xuống client. Đặt `proxy_downloads: false` chỉ khi muốn Lampa gọi trực tiếp URL Jackett trong `url`.

Các lệnh quản lý:

```bash
jackett start
jackett stop
jackett restart
jackett status
jackett logs
jackett update
```

`lampac start` chỉ tự khởi động AIOStreams rồi chạy Lampac. Jackett có lifecycle độc lập để dễ đo hiệu năng: chạy `jackett start` khi cần dùng và `jackett stop` khi muốn giải phóng tài nguyên; `lampac stop` không dừng Jackett. AIOStreams có thể dùng URL/API key của Jackett theo cấu hình addon riêng trong dashboard AIO. Jackett lắng nghe mạng LAN để thiết bị khác mở dashboard; không đưa port `9117` ra Internet, đồng thời không commit hoặc chia sẻ API key.

## Nguồn VidCore (4K, không cần Playwright)

`Modules/OnlineENG/VidCore` là module động mới, resolver thuần HTTP theo công thức API của
VidCore (`vidcore.io`, có biến thể 4K). Không phụ thuộc Chromium nên vẫn chạy khi Playwright hỏng;
đóng vai trò nguồn ENG dự phòng khi HDVB/Mirage không lên.

**Trạng thái: đã xác minh trên thiết bị 2026-08-31** — movie `1288445` ra `2/5 streams`,
TV `125988/1/1` (Silo S1E1) ra `1/5`, và mở player gốc `vidcore.io/tv/125988/1/1` thì cũng
chỉ có Supreme + Prime không bị khiên đỏ: **số server sống bằng đúng số nguồn có**, không phải
resolver hụt. 4K (`Premiere 4K`) chỉ xuất hiện ở phim lẻ mới phát hành.

**Người dùng mới**: `bash setup-termux.sh --install` là đủ — khối VidCore được đặt **đầu**
`install_custom_modules()` (ngay sau bước dọn module đã gỡ) và tự `mkdir`, nên dù các khối
phía sau có 404 mà chết giữa hàm (`set -euo pipefail` + `curl -f` trần — lỗi có từ trước,
không riêng VidCore) thì module vẫn được chép xuống. `lampac sync` sau đó cập nhật tiếp.

Điểm cần biết: module **không có trong `lampac-nextgen.zip`**, nên lần đầu phải có
`--install` / `--sync-all` / `--update` (các lệnh này tạo thư mục module + chép `manifest.json`
qua `install_custom_modules()`). Sau khi xác minh thiết bị, nó **đã vào `--sync` nhẹ**
(`sync_latest_modules()`) cùng VidLink — `lampac sync` là đủ để lấy bản mới.

Test: mở Lampa -> xem danh sách Online (nguồn `VidCore (ENG)`) hoặc probe một dòng:

```bash
lampac vidcore 155          # tmdb / movie
lampac vidcore 2389 1 1     # series: tmdb season episode
```

Nếu Lampa báo "Nguồn (vidcore) không trả về kết quả" mà Lampac **không có log nào**, kiểm tra
nhanh route có bị redirect không (5 giây, khỏi đọc log):

```bash
proot-distro login ubuntu -- bash -lc 'curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" "http://127.0.0.1:9000/lite/vidcore?id=155"'
```

`200 ` = route chạy. `302 https://vidcore.io/...` = `overridehosts` bị set (xem
`Modules/OnlineENG/VidCore/README.md` — mục "Bẫy đã gặp").
Nếu `--sync-all` không chép tới nơi (khối `install_custom_modules()` chạy với
`set -euo pipefail`, nên **một file 404 là dừng cả hàm** — lỗi có từ trước, không riêng VidCore),
chép tay 3 file theo mẫu "Curl một file thẳng vào Ubuntu":

```bash
proot-distro login ubuntu -- bash -lc '
set -e
d=/root/lampac/module/OnlineENG/VidCore
mkdir -p "$d"
base=https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac/Modules/OnlineENG/VidCore
stamp=$(date +%s)
for f in manifest.json Controller.cs ModInit.cs; do
    curl -fSL --retry 3 "$base/$f?cb=$stamp" -o "$d/$f"
    echo "  [OK] $f"
done
ls -l "$d"
'
lampac stop && lampac start
```

Toàn bộ flow, route kiểm tra và cách tắt: [`Modules/OnlineENG/VidCore/README.md`](Modules/OnlineENG/VidCore/README.md).

**Lùi về nhánh cũ nếu VidCore làm phiền:** code cũ vẫn nguyên ở `arena/01a06710-lampac`.

```bash
export LAMPAC_CUSTOM_SOURCE_BASE=https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac
rm -rf /root/lampac/module/OnlineENG/VidCore     # module này không có trong lampac-nextgen.zip nên xoá là sạch
bash setup-termux.sh --sync-all && lampac stop && lampac start
```

## Nguồn MoviesHub (MoviesDrive + Movies4U — HubCloud/Google Drive)

`Modules/OnlineENG/MoviesHub` chứa **hai** nguồn file-host trong một module vì chúng dùng
chung một resolver (HubCloud `…/drive/<id>` và Google Drive) — xem
[`Modules/OnlineENG/MoviesHub/README.md`](Modules/OnlineENG/MoviesHub/README.md) để biết
luồng, selector và cách đọc log.

| Route | Nguồn |
|---|---|
| `lite/moviesdrive[/video]` | search theo IMDb id → link HubCloud theo quality (có 4K ở phim mới) |
| — | mỗi link file-host (.mkv) là **một nút nguồn**, không vào menu chất lượng — để GStreamer của Lampac xử lý |
| `lite/movies4u[/video]` | WordPress search theo tên+năm (TMDB) + `Cookie: xla=s4t`; series: mùa và nhóm release đọc từ **nút "Download Links" + heading ngay trước nó**, không dùng class |

Cả hai được `install_custom_modules()` (khối `--install` / `--sync-all` / `--update`) và
`sync_latest_modules()` (`lampac sync`) chép xuống, nằm cạnh khối VidCore ở **đầu** hàm để
không bị các khối `curl -f` phía sau cắt ngang. Từ 2026-09-01 danh sách file `.cs` không còn ghi
hardcode trong script nữa: script tải `manifest.json` của module và lấy `tree`, nên thêm nguồn cùng
họ chỉ cần sửa `manifest.json` trong repo (xem [công thức](notes/FILEHOST-SOURCE-FORMULA.md)).

**Trạng thái: đã xác minh trên thiết bị 2026-09-01** với `v20-seasons-from-buttons` (Reacher Season
1–4): `Mùa` ra đủ 4 mùa, mỗi mùa có danh sách nhóm riêng (`480p [250MB/E]` … `2160p 4K`), nút
BATCH/ZIP bị loại, chọn nhóm là dịch cả mùa của nhóm đó. Bài học lớn nhất của vòng này — đừng đoán
DOM, phải đọc trang thật — nằm trong `notes/FILEHOST-SOURCE-FORMULA.md`.

## UhdMovies — đã đóng (2026-09-01), code giữ làm nguyên liệu cho nguồn phim khác

Test trên máy đã chạy được (tìm bài → 2 trang countdown → `driveseed /zfile/` → link `workers.dev` tua được,
mỗi tập của Reacher S02 đều có nút, bài collection LOT R tách đúng 3 phim), nhưng **file của họ không được bảo trì**:
bài cũ trả link mà stream không ra hình, site thiên về "tải về" hơn "xem". Nên rút khỏi `manifest.json` → `tree`
và bỏ section config; `UhdmoviesController.cs` vẫn nằm trong repo vì đó là bằng chứng chạy được
của cả bộ máy: `Bypass`, `ResumeLink`, `LabelBlocks`, `Playable`, `IsResume`, `Unwrap`.


Anh em họ UHDMovies/MoviesMod/TopMovies **không dùng HubCloud**: mỗi nút "Episode N"/chất lượng trỏ
`…?sid=<blob mã hoá>` rồi đi qua trang verify của WordPress → shortener → trang file
DriveLeech/DriveSeed, link cuối là CDN (thường `.mkv` trên `*.workers.dev`/`.r2.dev`). Toàn bộ chuỗi
request (5 bước, có 2 POST form + `CookieContainer` dùng chung) và cấu trúc bài viết đã đọc trực tiếp
từ code CSX còn sống + trang thật, ghi lại ở [`notes/UHD-MOVIES.md`](notes/UHD-MOVIES.md).

Module: `Modules/OnlineENG/MoviesHub/UhdmoviesController.cs` — **cùng assembly** với Movies4U/MoviesDrive
để ăn lại `CollectionCore` + templates đã được thiết bị xác minh (module compile fail là Lampac không
boot bất kể file hỏng nằm ở đâu, nên "để riêng cho an toàn" không mua được gì). Route
`Lite/uhdmovies[/video|file.mkv]`, section config riêng `"UhdMovies"` (displayindex 1019), và
`manifest.json` → `tree` đã liệt kê file nên `lampac sync` tự kéo, không phải sửa `setup-termux.sh`.

**Vòng 1 (1/9) fail ngay ở tìm bài** — `0 bài ứng viên` với `a=26`: `Anchors()` để mặc định
`onlyFileHost: true` nên link kết quả (nằm trên chính `uhdmovies.autos`) bị lọc sạch, và code chỉ thử
`?s=` khi trang trả về RỖNG trong khi trang "không kết quả" của site lại không rỗng. Cả hai đã sửa ở
v22: thử `?s=` trước `/search/`, nhận trang khi có chuỗi `/download-`, log in `dh=`/`sid=` để phân
biệt "site không trả kết quả" với "mình lọc sai". Chi tiết + cấu trúc bài phim lẻ thật: note mục 7.

**Vòng 3 (1/9) — chain chạy thật trên máy**: `?sid=` → 2 countdown → `driveseed.org/r?key=` →
`/file/<id>` → tìm đủ 3 nút, video play được. Còn sai ở CHỌN NÚT: `Resume Cloud` (`/zfile/<id>`,
302 tới `*.workers.dev/<hex>::<hex>/<tên file>.mkv` — link duy nhất tua được) fail im lặng nên module
rơi xuống `Instant Download` (`cdn.video-gen.xyz` → Google `video-downloads…` = link tải, không seek).
v24: gọi `/zfile/` bằng `GetLocation` trước, ưu tiên tuyệt đối theo host `workers.dev|r2.dev`
(không theo `::` — cả hai loại đều có `::`), dán nhãn ` · tua được` / ` [download]`, dừng ngay khi có
link worker nhưng vẫn thêm bản tải bằng href trần (0 request). Toàn bộ hiện trường: note mục 9.

## VidLink — đã đóng (2026-09-01)

Cùng một trang được test bằng **hai stack độc lập**: module Lampac (C#) và plugin CloudStream
(Kotlin) — cả hai đều không lấy được link. Vậy `vidlink.pro` chặn theo chính sách **chỉ cho embed**
(token/Referer/Origin sống chết theo session nhúng), không phải selector hay resolver sai. Không cày
tiếp: module giữ nguyên code nhưng đổi mặc định về `enable: false, enabled: false` để mỗi lần mở
phim không còn gọi `vidlink.pro` với `httptimeout: 20` cho một nguồn chết. Chi tiết và cách bật lại:
[`Modules/OnlineENG/VidLink/README.md`](Modules/OnlineENG/VidLink/README.md).

Kết luận rộng hơn (nguồn nào nên bỏ, nguồn nào đáng viết, và Kotlin còn thắng ở đâu) nằm ở mục 11
của [`notes/FILEHOST-SOURCE-FORMULA.md`](notes/FILEHOST-SOURCE-FORMULA.md).

## Trạng thái nguồn ENG và Mirage

Bản cài Termux mặc định dùng `disableEng: true` để ẩn nhóm ENG, nhưng Chromium được bật với đường dẫn rõ ràng `/usr/bin/google-chrome-stable`; các nguồn browser-backed khác vẫn có thể dùng Playwright. `--sync-all`/`--update` không nên ghi đè lựa chọn `disableEng` hoặc section Chromium chi tiết của người dùng. Muốn hiện nguồn ENG, đổi `disableEng` thành `false`. AIOStreams vẫn hoạt động độc lập khi section `AIOStreams` có `enable: true` và manifest hợp lệ.

Mirage không bị xóa: module mặc định `enable: false` và tự ẩn khi Chromium/Playwright bị tắt. Nguồn này cần Google Chrome/Edge và khoảng 1 GB RAM. Muốn bật, cấu hình đường dẫn Chrome thật rồi restart:

```jsonc
"disableEng": false,
"chromium": {
  "enable": true,
  "executablePath": "/usr/bin/google-chrome-stable",
  "context": { "keepopen": false, "min": 0, "max": 1 }
},
"Mirage": {
  "enable": true,
  "m4s": false
}
```

Không dùng đường dẫn trên nếu file không tồn tại; kiểm tra bằng `command -v google-chrome-stable || command -v microsoft-edge` trong Ubuntu proot. Chromium thường của distro không đáp ứng yêu cầu Mirage.

## Thêm/sửa plugin LampaWeb vào bản Lampac trong `/root`

Bản Termux chạy release ở `/root/lampac`; LampaWeb là **dynamic module**. Vì vậy chỉ thêm file `.js` vào repository là chưa đủ: release đang chạy còn dùng controller/model cũ để tạo `/lampainit.js`.

Khi thêm một plugin built-in mới, cập nhật cả ba phần sau:

1. **Script plugin:** `Modules/LampaWeb/plugins/<ten>.js`.
2. **Đăng ký server:** thêm cờ `initPlugins.<ten>` trong `Modules/LampaWeb/Models/InitPlugins.cs`, route JS và entry vào cả danh sách `/lampainit.js` lẫn `/on.js` trong `Modules/LampaWeb/Controllers/ApiController.cs`.
3. **Deploy Termux:** thêm các file nguồn cần thiết vào `install_custom_modules()` trong `setup-termux.sh`. File được chép phải đúng cây runtime:

   ```text
   /root/lampac/module/LampaWeb/Controllers/
   /root/lampac/module/LampaWeb/Models/
   /root/lampac/module/LampaWeb/plugins/
   ```

Sau khi mirror branch, luôn áp dụng bằng `--sync-all` rồi restart `lampac`. Không chép sang `/root/lampac/plugins/`: LampaWeb không đọc plugin từ đường dẫn đó. `--sync-all` cũng bảo đảm `wwwroot/lampa-main/index.html` có thẻ `/lampainit.js`; nếu thiếu thẻ này, URL gốc vẫn mở Lampa nhưng giống một app mới tinh và không nhận bất kỳ plugin built-in nào.

## Xử lý lỗi thường gặp

### Script báo không chạy trong Termux

Hãy dùng Termux từ F-Droid và chạy lại trong ứng dụng Termux; không chạy script từ shell Android khác.

### `proot-distro` hoặc Ubuntu bị lỗi

```bash
pkg update -y
pkg install -y proot-distro
proot-distro reset ubuntu
bash setup-termux.sh --install
```

Lệnh `reset` xoá Ubuntu proot hiện tại, vì vậy cần cài lại Lampac sau đó.

### Không truy cập được từ thiết bị khác trong Wi-Fi

- Kiểm tra `lampac status`.
- Dùng đúng IP Wi-Fi của Android, không dùng `localhost` từ thiết bị khác.
- Đảm bảo cả hai thiết bị cùng mạng Wi-Fi và router không chặn client-to-client.
- Kiểm tra port trong `lampac info` hoặc `init.conf`.

<a id="termux-playwright-recovery"></a>
### Khôi phục Playwright trên Termux

Nếu log báo `chromium is not installed` dù Chrome đã cài, đừng chỉ kiểm tra package. Lampac có thể đang dùng đường dẫn mặc định không đúng, đặc biệt trên Android ARM64. Hãy cấu hình rõ executable:

```json
"chromium": {
  "enable": true,
  "Headless": true,
  "executablePath": "/usr/bin/google-chrome-stable",
  "Args": [
    "--no-sandbox",
    "--disable-setuid-sandbox",
    "--disable-dev-shm-usage",
    "--disable-gpu"
  ]
}
```

Mẫu khôi phục tối thiểu có sẵn tại [`config/termux-recovery.init.conf`](config/termux-recovery.init.conf). Mẫu này giữ `disableEng: false`, bật SISI và giữ `hochutv.enable: true`; nó không chứa mật khẩu, cookie, token hay proxy.

Nếu `init.conf` bị mất hoàn toàn, backup trước rồi mới lấy mẫu:

```bash
proot-distro login ubuntu -- bash -lc '
  set -e
  mkdir -p /root/lampac-backups
  [ -f /root/lampac/init.conf ] && cp -a /root/lampac/init.conf /root/lampac-backups/init.conf.before-recovery
  [ -f /root/lampac/passwd ] && cp -a /root/lampac/passwd /root/lampac-backups/passwd.before-recovery
  curl -fsSL https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a06710-lampac/config/termux-recovery.init.conf \
    -o /root/lampac/init.conf
'
```

Nếu `init.conf` còn các cấu hình riêng, không chép đè cả file; chỉ hợp nhất ba phần `disableEng`, `chromium` và `hochutv` từ mẫu. Nếu có `/root/lampac/init.yaml`, kiểm tra nó nữa vì `init.yaml` có thể ghi đè `init.conf`.

Nếu Chrome thật sự không còn, dùng `--sync-all` để cài lại browser và module mà không xóa `/root/lampac`:

```bash
bash setup-termux.sh --sync-all
```

Không cần dùng `--update` chỉ để khôi phục Playwright. Sau khi cấu hình xong:

```bash
lampac stop
lampac start
```

Log thành công phải có đủ:

```text
Chromium: Initialization
Chromium: CreateAsync
Chromium: LaunchAsync
Chromium: v... / headless / True
```

### Không thấy nguồn ENG/Playwright

Đổi `disableEng` thành `false` để hiện nguồn ENG. Nếu vẫn không hiện, kiểm tra log có đủ dòng `Chromium: LaunchAsync` và `Chromium: v... / headless / True` hay chưa; chỉ có `Chromium: CreateAsync` chưa chứng minh browser đã khởi chạy.

### Jackett báo `GC heap initialization failed ... 0x8007000E`

Đây là giới hạn reserve bộ nhớ ảo của CoreCLR trên Android/proot ARM64, không nhất thiết là điện thoại đã hết RAM. Controller mặc định đặt `DOTNET_GCHeapHardLimit=40000000` (hex, tương đương 1 GiB) và tắt Server GC riêng cho Jackett. Đồng bộ controller mới rồi khởi động lại:

```bash
bash setup-termux.sh --sync-all
jackett restart
```

Nếu thiết bị vẫn lỗi, thử giới hạn 512 MiB:

```bash
JACKETT_GC_HEAP_HARD_LIMIT=20000000 jackett start
```

### Android tự dừng server

Giữ Termux ở foreground khi cần độ ổn định cao, tắt battery optimization cho Termux và tránh để hệ thống đóng ứng dụng khi thiếu RAM.

## Quy trình làm addon (học từ cái giá của sisi-layout)


> **Cảnh nghèo rút kinh nghiệm**: đừng bao giờ nghĩ ra một addon mới, viết thẳng vào trong
> `Core/wwwroot/` hoặc module chính (`SISI/`, `LampaWeb/`, …), push lên `main`, rồi để
> `setup-termux.sh --sync` tự kéo nó về cho mọi người. Thử nghiệm thế này từng phá hỏng
> hàng loạt cài đặt (curl 404, layout vỡ, plugin registry bị lệch) và phải gỡ từng dòng
> trong khi người dùng đã sync phải bản lỗi.

### Workflow đúng

1. **Viết addon như một file `.js` độc lập** trong một thư mục ngoài repo (ví dụ
   `~/lampac-dev/my-addon.js`), tham khảo cách các addon Lampa chuẩn hoạt động
   (`component: 'setting'`, không đụng global CSS, không chèn class tùy tiện vào
   `body`).
2. **Test cục bộ trên máy mình trước**: copy file `.js` vào thư mục plugin của Lampa
   bằng tay, mở URL `http://127.0.0.1:9118` và chạy trong vài ngày ở các màn hình
   khác nhau (chính, phụ, tìm kiếm, bookmark, history, TV, player) để bắt lỗi bố cục.
   Không test 5 phút rồi push.
3. **Chỉ khi chạy ổn cả tuần trên máy mình** mới cân nhắc nhét vào repo chính. Lúc đó
   vẫn không chèn thẳng vào layout mặc định của Lampa/SISI — giữ addon là một module
   riêng, tắt theo mặc định, người dùng bật lên trong Cài đặt (Settings → Addons) khi
   họ muốn thử.
4. **Không thêm addon đang dev vào `setup-termux.sh --sync`** cho đến khi addon ổn định
   và đã nằm ngoài giai đoạn thử nghiệm. File trong loop `curl … --sync` được tải
   xuống *tất cả* người dùng ở lần update kế tiếp — dù họ có bật addon hay không —
   nên một link 404 ở đó là hỏng cả bước sync của mọi người.
5. **Khi cần bỏ / thu hồi addon**: nhớ kiểm tra và xoá đồng bộ ở 3 chỗ — file addon
   chính, dòng đăng ký trong `ApiController.cs`/`SisiApi.cs`, và **2 vòng lặp curl
   trong `setup-termux.sh`** (block `install_custom_modules()` và block `--sync-all`).
   Thiếu một chỗ là `curl 404` làm toang người khác.

### Cách cài addon thử nghiệm an toàn (không đụng root repo)

Không cần sửa code C# / build lại / đợi script sync. Cách đơn giản nhất:

```bash
# Copy file .js của addon vào thư mục plugins của LampaWeb trong Ubuntu
proot-distro login ubuntu -- bash -c 'mkdir -p /root/lampac/module/LampaWeb/plugins && cat > /root/lampac/module/LampaWeb/plugins/my-addon.js' <<'JS'
// nội dung addon ở đây
Lampa.Plugins.add(function(){ this.add = function(){ console.log("my addon loaded"); }; });
JS

# Khởi động lại Lampac
lampac stop && lampac start
```

Sau đó vào Lampa → Settings → Plugins → gõ URL
`http://127.0.0.1:9118/my-addon.js` để bật addon. Addon nào làm vỡ giao diện thì
xoá file `.js` đi và restart là mọi thứ trở lại bình thường — **không bao giờ**
phải `git reset` hay clone lại cả repo chỉ vì một addon hỏng.

## Lưu ý an toàn

- Đổi mật khẩu mặc định `lampac`.
- Không mở port Lampac trực tiếp ra Internet nếu không có reverse proxy, firewall và xác thực phù hợp.
- Không chia sẻ `init.conf`, `passwd`, cookie, token hoặc tài khoản cá nhân.
- Chỉ sử dụng nội dung mà bạn có quyền truy cập theo luật pháp và điều khoản của từng nguồn.


## Tài liệu mã nguồn

- [Script cài Termux](setup-termux.sh)
- [Cấu hình mẫu](config/example.init.conf)
- [Mẫu khôi phục Termux + Playwright](config/termux-recovery.init.conf)
- [Module LampaWeb](Modules/LampaWeb/README.md)
- [Danh sách module](Modules/)
- [Giấy phép MIT](LICENSE)
