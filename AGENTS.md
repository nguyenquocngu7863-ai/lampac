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

## Skills

Skill nằm ở **`~/.config/opencode/skills/<name>/SKILL.md`** (KHÔNG phải `.kilo/skills/` —
thư mục đó không tồn tại; đường dẫn cũ trong tài liệu này là sai). Danh sách thật lấy bằng
`ls ~/.config/opencode/skills/`. Skills của DSH được load bằng tool `skill`.

Nhóm lampac (làm module / build / debug / deploy):
- `lampac-adult-module` — **LÕI**: manifest, ModInit, Service, Controller, parser, phân trang, menu SISI 2 tầng, stream HLS/MP4, cache, route runtime. Đọc trước khi viết module mới
- `lampac-popup` — popup long-press: route `/<mod>/genres?uri=`, mục "Liên quan" `?rel=`, chip thể loại/diễn viên
- `lampac-sort` — chuẩn SORT (đổi thứ tự) vs FILTER (đổi tập), dòng 2 menu "Sắp xếp: …"
- `lampac-build` — build, deploy, smoke test (csc shim nhanh / proot khi đổi Core/Shared)
- `lampac-debug` — cô lập lỗi theo tầng mạng → TLS → status → parse → route → client
- `lampac-smoke` — verify route video PHÁT ĐƯỢC thật bằng `~/lampac-native/play.sh` (không chỉ HTTP 200)
- `lampac-deobfuscate` — giải mã player/embed: P.A.C.K.E.R, AES-128-CBC, DoodStream pass_md5, Turbo data-hash
- `lampac-decode-offline` — **BẮT BUỘC** trước khi bắn request ra site thật: OFFLINE-FIRST, lấy 1 lượt hết dữ liệu rồi phân tích offline
- `lampac-curl-cffi` — mẫu `curl_cffi` impersonate trên Termux (bypass Cloudflare/TLS fingerprint)
- `lampac-cleanup` — dọn rác build .NET (bin/obj/.playwright ~60G)
- `lampac-manual` — chế độ RẢNH: user cùng làm, user tự test trên app
- `lampac-auto` — chế độ AUTO: user bận, tự soi site → làm → test → deploy → commit

Skill môi trường / công cụ:
- `termux-fish` — sửa config.fish, alias, theme, font slot, tmux (KHÔNG sửa nếu user chưa yêu cầu)
- `jina-reader-direct` — đọc URL → markdown qua `https://r.jina.ai`
- `karpathy-guidelines` — kỷ luật code: đơn giản, sửa tối thiểu, nêu giả định
- `opencode-tooling` — quy tắc gọi tool trong Code Mode (chặn lỗi tool ảo)

## Quy trình chuẩn khi làm module Adult

1. Đọc `MODULE-GUIDE.md` trước khi viết code
2. Tạo `.csproj` (nếu cần), khai báo `NextGen.slnx` — **chỉ để build/proot; runtime KHÔNG đọc** (Core tự compile `*.cs`, `manifest dynamic:true` là đủ; đo 2026-10-27: 32/56 module không csproj vẫn load)
3. Build tới 0 error — **không có SDK native**: check nhanh `~/lampac-native/csc/check.sh <module>` (tự ref mọi dll trong `~/lampac-run/runtimes/references`, ~1–2 s), build thật qua `proot-distro login ubuntu` khi đổi `Core/`/`Shared/`/`.csproj` (xem skill `lampac-build` §3)
4. Copy 4 file source sang `~/lampac-run/module/Adult/<Name>/` (KHÔNG copy bin/obj/csproj)
5. Restart app, kiểm tra log `loaded module: <Name>`
6. Smoke test 5 endpoint: home, category, search, page2, vidosik
7. Nạp skill `lampac-cleanup` và chạy §2 trước khi bàn giao

## LUẬT CỨNG

- **Xong việc là ghi luôn, không đợi user nhắc**: mỗi task (sửa module, popup, đổi host, fix
  player…) kết thúc bằng (1) thêm dòng vào `todo.md` — làm gì, số đo/verify, commit; (2) tick
  `- [ ]` → `- [x]` mục tương ứng trong checklist; (3) cập nhật `README.md` của module nếu
  hành vi/host đổi; (4) `memory_remember` khi có quyết định/đổi domain. Câu "ghi todo chưa"
  nghĩa là bước này đã bị bỏ — coi việc ghi là một phần của "xong".
- KHÔNG dùng `curl` thường cho request ra site thật — dùng `curl_cffi` impersonate="chrome124"
- `curl` chỉ hợp lệ: `127.0.0.1`/`localhost`, đo hạ tầng, hoặc đã hết cách
- **MENU PHẢI LÀ TIẾNG VIỆT HOẶC TIẾNG ANH** (user chốt 2026-11-07): mọi `title` trong
  menu SISI (dòng 1/2/3/4, submenu) và popup nhấn giữ `/genres` **không được** để
  chữ Trung/Nhật/Nga/Hàn/Thái… Site nguồn tiếng khác thì phải dịch (bảng
  `Trans` + `Translate()` trong `Service.cs`, áp cho cả menu lẫn popup, kể cả
  fallback tĩnh). Menu trộn ngôn ngữ (vd `Mới` + `Популярное`) là lỗi phải sửa.
  Quét: grep CJK `[一-鿿]`, Cyrillic `[А-Яа-я]`, Thái `[ก-๙]`, Hàn `[ㄱ-ㅎㅏ-ㅣ]`
  trong `new("…")` / `title = …` (bỏ comment). Chi tiết: skill
  `lampac-adult-module` → `references/menu-sisi.md` §9g, khối "LUẬT CỨNG — NGÔN NGỮ MENU".
- Đo trước, xoá sau: luôn `du -sh` trước khi dọn rác
- Không tự xoá repo trùng (`lampac-src`, `lampac-native`) — hỏi user
- Không tự xoá `~/lampac/publish/` — hỏi user

## Xem ảnh (vision) — đo 2026-11-22, đã verify

User gửi ảnh (đường dẫn, hoặc ảnh dán từ clipboard nằm ở `/sdcard/DCIM/Screenshots/…`)
→ đọc bằng `~/bin/vsee`, **đừng** dùng 3 đường sau (đều chết):

- `modlens_read_image` → provider `claude-cli` fail: "claude native binary not installed";
  `agy` (antigravity) không có; `pi` không nằm trong danh sách provider của modlens
- `read_image` (tool DSH) → model đang chạy không khai báo image input
- `pi -p @file` → `@` chỉ nhận text, ảnh **không** được attach (model trả "No image was attached")

Cách chạy thật (base64 → Kilo gateway, OpenAI-compatible):

```sh
vsee <đường-dẫn-ảnh> [prompt] [--model M]
# vd: vsee /sdcard/DCIM/Screenshots/Screenshot_2026-11-22-10-09-34-194_io.github.dsh_app.jpg
```

- Default model: `nvidia/nemotron-3.5-super-vl:free` (đọc chữ/布局 tốt, có quota riêng)
- Gateway: `https://api.kilo.ai/api/gateway/chat/completions`, key `~/.config/opencode/kilo.key`
- Chỉ dùng model có `meta.input` chứa `image` (list: `~/.pi/agent/models.json`).
  Model thường vd `baidu/ernie-5.5-flash-preview:free` **không** có vision → lỗi.
- Model :free ăn `429 FreeUsageLimitError` theo từng model, không phải lỗi app
- Source: `~/bin/vsee` (kèm docstring giải thích lý do)

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