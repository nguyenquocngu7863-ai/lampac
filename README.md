# Lampac NextGen cho Termux (Android)

Bản hướng dẫn này dành cho cách chạy Lampac trên **Android qua Termux**. Script [`setup-termux.sh`](setup-termux.sh) tạo một Ubuntu bằng `proot-distro`, cài .NET 10 và chạy Lampac bên trong Ubuntu đó.

> Đây là cách chạy phù hợp để tự dùng trên điện thoại/TV box Android. Android có thể dừng tiến trình nền để tiết kiệm pin; không nên xem đây là máy chủ luôn hoạt động 24/7.

## Yêu cầu

- Thiết bị Android 64-bit (`arm64` là phổ biến; script cũng hỗ trợ `amd64`).
- Cài **Termux từ F-Droid**: <https://f-droid.org/packages/com.termux/>. Không dùng bản Termux cũ trên Google Play.
- Kết nối Internet ổn định, còn vài GB bộ nhớ trống và pin/sạc đủ trong lần cài đầu.
- Nên tắt tối ưu pin cho Termux nếu muốn server chạy lâu hơn.

## Cài đặt nhanh

Mở Termux, tải script rồi chạy:

```bash
pkg update -y && pkg install -y curl
curl -fLO https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a036c7-lampac/setup-termux.sh
bash setup-termux.sh
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
5. Tạo `init.conf` tối ưu cho Termux: `lowMemoryMode`, GStreamer; tạm đặt `disableEng: true` và tắt Chromium trong lúc luồng ENG embed được sửa.
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

Lệnh `lampac start` in ra địa chỉ local, địa chỉ mạng LAN và cổng đang dùng. Thông thường bạn truy cập từ thiết bị khác cùng Wi-Fi qua:

```text
http://IP_CUA_ANDROID:9118
```

Nếu không thấy địa chỉ IP, chạy trong Termux:

```bash
ip addr show wlan0
```

## Cập nhật và đồng bộ module

### Cập nhật đầy đủ

```bash
lampac update
# hoặc
bash setup-termux.sh --update
```

Cập nhật sẽ tải release mới, giữ lại `init.conf` và `passwd`, áp dụng lại cấu hình Termux (`disableEng: true`, Chromium tắt) và đồng bộ các module tuỳ biến. Thư mục nguồn lỗi cũ `NguonC` cũng bị xoá; `VsMov` được đồng bộ lại.

### Chỉ đồng bộ module tuỳ biến

Dùng khi release Lampac vẫn giữ nguyên nhưng bạn muốn lấy lại các thay đổi tuỳ biến (**KKPhim, K20, VsMov, WebStreamr, Open Directory, Sootio, AIOStreams, GStreamer và LampaWeb/StremioSub**):

```bash
bash setup-termux.sh --sync
```

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
    "useGpu": false,
    "hardwareAcceleration": false
  },
  "chromium": { "enable": false }
}
```

Thiết lập này ưu tiên ổn định và tiết kiệm RAM. Nếu máy yếu, không nên bật đồng thời AIOStreams, Jackett, nhiều module nặng hoặc transcoding.

## Plugin phụ đề

Lampac có SubSense Auto, SubSense, SubFinder và StremioSub. Vì các plugin tự động đều bọc `Lampa.Player.play`, chỉ nên bật **một** provider. Mặc định dùng `stremiosub`; `subsenseAuto`, `subsense` và `subfinder` là opt-in. Server sẽ ưu tiên đúng một provider nếu lỡ bật nhiều cờ, đồng thời client có khóa chung để raw URL cũ không bọc player lần nữa.

## StremioSub — plugin phụ đề built-in

`StremioSub` là plugin built-in của Lampac: **không cài bằng URL jsDelivr trong mục Extensions**. Nếu module AIOStreams đã bật, plugin ưu tiên subtitle resource từ AIO; nếu chưa thì dùng fallback SubDL + SubSource. Sau một lần `--sync` và restart Lampac, Lampa nhận plugin nội bộ với tên **StremioSub — SubDL + SubSource/AIOStreams**.

Kiểm tra Lampac đã đưa plugin vào init chưa:

```bash
curl -s http://127.0.0.1:9118/lampainit.js | grep -oE 'StremioSub[^" ]*|stremiosub\.js'
```

Nếu lệnh không in ra `stremiosub.js`, đồng bộ và khởi động lại:

```bash
curl -fsSL https://raw.githubusercontent.com/nguyenquocngu7863-ai/lampac/arena/01a036c7-lampac/setup-termux.sh | bash -s -- --sync
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

Manifest URL có thể chứa UUID, password, API key hoặc token; không đưa nó vào Git, log hoặc chat. Các nguồn cũ như **HDVB, KKPhim, K20, WebStreamr, Open Directory và Sootio** vẫn giữ nguyên làm fallback. AIOStreams chỉ là nguồn riêng thêm vào, không thay thế chúng.

### Local host AIOStreams trên Ubuntu proot

Sau khi chạy `bash setup-termux.sh --sync`, cài AIOStreams chính chủ bằng:

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

Sau `bash setup-termux.sh --sync`, cài gói Jackett chính chủ phù hợp với kiến trúc Ubuntu proot:

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

Mặc định `proxy_downloads: true`: `/jackett.js` cho Lampa gọi API proxy cùng origin trên Lampac. Lampac tự chèn API key ở server, đổi các link `/dl/` thành `/jackett/download`, tải và kiểm tra bencode torrent thật trước khi TorrServer tính hash. Cách này tránh lỗi “Không lấy được HASH” ở các tracker mà URL Jackett trả redirect hoặc nội dung trung gian. API key không được nhúng vào link tải gửi xuống client. Đặt `proxy_downloads: false` chỉ khi muốn Lampa gọi trực tiếp URL Jackett trong `url`.

Các lệnh quản lý:

```bash
jackett start
jackett stop
jackett restart
jackett status
jackett logs
jackett update
```

Khi AIOStreams hoặc Jackett đã được cài, `lampac start` tự khởi động chúng trước Lampac. `lampac stop` dừng cả ba; `lampac status` hiển thị trạng thái từng dịch vụ. AIOStreams có thể dùng URL/API key của Jackett theo cấu hình addon riêng trong dashboard AIO. Jackett lắng nghe mạng LAN để thiết bị khác mở dashboard; không đưa port `9117` ra Internet, đồng thời không commit hoặc chia sẻ API key.

## Trạng thái nguồn ENG

Các nguồn ENG embed đang tạm ẩn bằng `disableEng: true`; Chromium cũng bị tắt để giảm RAM và tránh flow headless chưa ổn định. AIOStreams vẫn hoạt động độc lập khi section `AIOStreams` có `enable: true` và manifest hợp lệ. Khi muốn thử lại nguồn ENG, đổi `disableEng` thành `false` và tự cấu hình Chromium trước khi restart Lampac.

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

Sau khi mirror branch, luôn áp dụng bằng `--sync` rồi restart `lampac`. Không chép sang `/root/lampac/plugins/`: LampaWeb không đọc plugin từ đường dẫn đó. `--sync` cũng bảo đảm `wwwroot/lampa-main/index.html` có thẻ `/lampainit.js`; nếu thiếu thẻ này, URL gốc vẫn mở Lampa nhưng giống một app mới tinh và không nhận bất kỳ plugin built-in nào.

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

### Không thấy nguồn ENG/Playwright

Đây là trạng thái tạm thời có chủ đích: profile Termux đặt `disableEng: true` và `chromium.enable: false`. Hiện nên dùng nguồn Việt, AIOStreams hoặc luồng torrent Jackett. Chỉ bật lại ENG/Chromium khi tiếp tục kiểm thử embed.

### Jackett báo `GC heap initialization failed ... 0x8007000E`

Đây là giới hạn reserve bộ nhớ ảo của CoreCLR trên Android/proot ARM64, không nhất thiết là điện thoại đã hết RAM. Controller mặc định đặt `DOTNET_GCHeapHardLimit=40000000` (hex, tương đương 1 GiB) và tắt Server GC riêng cho Jackett. Đồng bộ controller mới rồi khởi động lại:

```bash
bash setup-termux.sh --sync
jackett restart
```

Nếu thiết bị vẫn lỗi, thử giới hạn 512 MiB:

```bash
JACKETT_GC_HEAP_HARD_LIMIT=20000000 jackett start
```

### Android tự dừng server

Giữ Termux ở foreground khi cần độ ổn định cao, tắt battery optimization cho Termux và tránh để hệ thống đóng ứng dụng khi thiếu RAM.

## Lưu ý an toàn

- Đổi mật khẩu mặc định `lampac`.
- Không mở port Lampac trực tiếp ra Internet nếu không có reverse proxy, firewall và xác thực phù hợp.
- Không chia sẻ `init.conf`, `passwd`, cookie, token hoặc tài khoản cá nhân.
- Chỉ sử dụng nội dung mà bạn có quyền truy cập theo luật pháp và điều khoản của từng nguồn.

## Tài liệu mã nguồn

- [Script cài Termux](setup-termux.sh)
- [Cấu hình mẫu](config/example.init.conf)
- [Module LampaWeb](Modules/LampaWeb/README.md)
- [Danh sách module](Modules/)
- [Giấy phép MIT](LICENSE)
