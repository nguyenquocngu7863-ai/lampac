# AGENTS.md — lampac-clean

> File này hướng dẫn agent khi làm việc trong repo `lampac-clean`.

## Project

- **Name**: Lampac NextGen (ASP.NET Core, net10.0)
- **Working dir**: `/data/data/com.termux/files/home/lampac-clean`
- **Repo nguồn DUY NHẤT**: `~/lampac-clean` (nhánh `arena/...`). `~/lampac` là **symlink** trỏ về đây — hai đường dẫn là **cùng một thư mục**, không có "repo phụ", không có chuyện đồng bộ ngược.
- **Runtime app**: `~/lampac-run` (app đang chạy, port 9118)
- **Toolchain**: `~/lampac-native` (csc shim + dotnet-host + `run-lampac.sh`)

### Môi trường (chốt 2026-10-26)

| đường dẫn | vai trò | ghi chú |
|---|---|---|
| `~/lampac-clean` | repo source (git) | sửa code ở đây |
| `~/lampac` | symlink → `~/lampac-clean` | dùng trong skill/lệnh cũ vẫn chạy |
| `~/lampac-run` | runtime đang chạy | chỉ copy file nguồn vào `module/Adult/<Name>/` |
| `~/lampac-native` | csc shim, `check.sh`, log | build nhanh, không cần proot (source: repo `termux-check.sh`) |

Trong proot Ubuntu, đường dẫn là `/data/data/com.termux/files/home/lampac-clean`
(`~/lampac` cũng resolve được qua symlink). Không dùng `cd ~/lampac` **bên trong proot**
vì `~` ở đó là `/root`.

## Skills đã import

Các skill từ OpenCode đã được import vào `.kilo/skills/`:
- `lampac-cleanup` — dọn rác build .NET (bin/obj/.playwright ~60G)
- `lampac-build` — build, deploy, smoke test module
- `lampac-auto` — tự động hóa task lampac
- `lampac-debug` — debug lampac
- `lampac-deobfuscate` — giải mã obfuscate
- `lampac-manual` — tham khảo thủ công
- `lampac-proxy` — proxy setup
- `lampac-adult-module` — tạo module Adult

## Quy trình chuẩn khi làm module Adult

1. Đọc `MODULE-GUIDE.md` trước khi viết code
2. Tạo `.csproj` (nếu cần), khai báo `NextGen.slnx` — **chỉ để build/proot; runtime KHÔNG đọc** (Core tự compile `*.cs`, `manifest dynamic:true` là đủ; đo 2026-10-27: 32/56 module không csproj vẫn load)
3. Build tới 0 error — **không có SDK native**: check nhanh `~/lampac-native/csc/check.sh <module>` (tự ref mọi dll trong `~/lampac-run/runtimes/references`, ~1–2 s), build thật qua `proot-distro login ubuntu` khi đổi `Core/`/`Shared/`/`.csproj` (xem skill `lampac-build` §3)
4. Copy 4 file source sang `~/lampac-run/module/Adult/<Name>/` (KHÔNG copy bin/obj/csproj)
5. Restart app, kiểm tra log `loaded module: <Name>`
6. Smoke test 5 endpoint: home, category, search, page2, vidosik
7. Nạp skill `lampac-cleanup` và chạy §2 trước khi bàn giao

## LUẬT CỨNG

- KHÔNG dùng `curl` thường cho request ra site thật — dùng `curl_cffi` impersonate="chrome124"
- `curl` chỉ hợp lệ: `127.0.0.1`/`localhost`, đo hạ tầng, hoặc đã hết cách
- Đo trước, xoá sau: luôn `du -sh` trước khi dọn rác
- Không tự xoá repo trùng (`lampac-src`, `lampac-native`) — hỏi user
- Không tự xoá `~/lampac/publish/` — hỏi user

## Commands

- `~/lampac-native/csc/check.sh <module-dir>` — check compile native ~1–2 s, không bin/obj.
  Tự ref ref-pack net10.0 + `Shared.dll` của runtime + mọi `*.dll` trong
  `~/lampac-run/runtimes/references` (Playwright, Bcl.AsyncInterfaces…) nên module dùng
  thư viện ngoài không báo oan CS0234/CS0012. Exit 0 = sạch, 1 = có `error CS` (in ra
  dòng lỗi), 2 = thiếu môi trường. Thêm ref riêng: `REFS_EXTRA=dir1:dir2`.
  Source of truth: repo `termux-check.sh` → deploy `cp ~/lampac-clean/termux-check.sh ~/lampac-native/csc/check.sh`
- `proot-distro login ubuntu -- bash -lc 'cd ~/lampac && dotnet build Modules/Adult/<Name>/<Name>.csproj -c Release --nologo'` — build thật (~23 s)
- `dotnet build ...` trực tiếp trong Termux — **KHÔNG dùng**: máy không có SDK (`dotnet --list-sdks` rỗng)
- `nstop` / `nstart` / `nrestart` / `nstatus` — điều khiển app (script `/data/data/com.termux/files/usr/bin/lampac`)
- `git status --short` — kiểm tra thay đổi