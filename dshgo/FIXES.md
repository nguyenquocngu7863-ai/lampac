# dsh-go branch — Lampac trên Ubuntu glibc (không phải Termux)

Nhánh này là bản Lampac chạy trong **dsh-go** (Ubuntu 24.04.5, glibc 2.39, aarch64).
Khác với nhánh Termux ở chỗ: **không dùng proot-distro**, dotnet/ffmpeg/gst đều cài
native bằng apt.

## Vì sao tách nhánh

Termux dùng **bionic libc** → .NET không có RID chính thức → buộc phải chạy trong
proot-distro Ubuntu. dsh-go là Ubuntu thật nên thoát được vòng đó:

| | Termux | dsh-go |
|---|---|---|
| .NET SDK native | ❌ bionic | ✅ apt dotnet-sdk-10.0 |
| proot-distro | bắt buộc | không cần |
| patchelf dotnet | ✅ | ❌ |
| Chrome | wrapper + libseccomp-shim | patchelf 1 lần |
| curl | native | shim curl_cffi |
| GStreamer | binary Termux | apt 1.24.2 |

## 6 lỗi đã gặp và cách sửa

### 1. GStreamer `undefined symbol: gst_structure_filter_and_map_in_place_id_str`
**Nguyên nhân:** `$NATIVE/bin/gst-*` là binary Termux, lệch ABI với lib 1.24.2 Ubuntu.
**Sửa:** bỏ `$NATIVE/bin` khỏi PATH, dùng `/usr/bin/gst-*` của apt.
`apt install gstreamer1.0-plugins-base-apps` (chứa gst-discoverer-1.0).

### 2. MissAV list chậm 27–50s mỗi lần
**Nguyên nhân:** `MissAVTaxCache` ghi vào `$HOME/lampac-run/cache/missav-tax.json`.
Nếu `HOME` sai thì `Write()` bị `catch{}` nuốt → mỗi request fetch lại 24 trang.
**Sửa:** `export HOME=/root/lampac` trong launcher.

### 3. MissAV player `502 missav: master playlist fetch failed`
**Nguyên nhân:** `MissAVTo.CurlGet()` spawn `curl` tải playlist `surrit.com`.
curl thường (OpenSSL) bị Cloudflare chặn 403 vì TLS fingerprint.
**Sửa:** `dshgo/curl-shim` dùng `curl_cffi` `impersonate="chrome124"`,
đặt tại `lampac-native/bin/curl` và cho lên đầu PATH.

### 4. Po85 `/proxy/` trả 403 Cloudflare
**Nguyên nhân:** Po85 không đăng ký `ProxyApiOverride` như MissAV → `/proxy/`
dùng HttpClient .NET → bị CF chặn.
**Sửa:** thêm `ProxyOverride` trong `ModInit.cs` + `CurlGetBytes` trong `Service.cs`
(2 file trong commit này).

### 5. Po85 resolver port 9196 không chạy
**Nguyên nhân:** ModInit cần `module/Adult/Po85/uhd/` + `/usr/bin/node` + `/usr/bin/npm`.
Thiếu 1 trong 3 là `return` im lặng.
**Sửa:** copy `uhd/` vào module dir, symlink node/npm, `npm i playwright-core`.

### 6. Chrome không chạy được
**Nguyên nhân:** binary build cho glibc Termux (`PT_INTERP` trỏ
`/data/data/com.termux/files/usr/glibc/bin/ld.so`).
**Sửa:** `patchelf --set-interpreter /lib/ld-linux-aarch64.so.1` + cài bộ lib
(`libnspr4 libnss3 libatk... `) + copy `chrome_crashpad_handler`, `icudtl.dat`,
`locales/`, `libvk_swiftshader.so`.

## Phục hồi sau reset

```bash
git clone -b dshgo/01a0673e-lampac https://github.com/nguyenquocngu7863-ai/lampac.git lampac
bash lampac/dshgo/setup.sh
```

Script cài apt (dotnet-sdk-10.0, ffmpeg, gstreamer, curl, lib cho Chrome),
pip curl_cffi, symlink node/npm, tải **google-chrome-stable arm64 native**,
đặt curl shim, cài playwright-core cho Po85.

## KHONG can build (quan trong)

**Core tu compile module bang Roslyn luc start** — doi chieu `Shared.dll` cua
runtime. Nghia la module **khong can** `dotnet build`, khong can `bin/obj`,
khong can publish. Xem skill `lampac-build` §3.

Vong lap sua module (~1.5 s check + ~20 s restart):

```bash
# 1. sua .cs trong repo
# 2. check cu phap (~1-2 s, khong build)
bash lampac-native/csc/check.sh Modules/Adult/<Name>

# 3. copy 5 file (KHONG copy bin/obj/csproj)
D=lampac-run/module/Adult/<Name>
cp Modules/Adult/<Name>/{Controller.cs,ModInit.cs,Service.cs,GlobalUsings.cs,manifest.json} "$D/"

# 4. restart (bat buoc)
pkill -f 'Core\.dll'; sleep 3
nohup bash lampac-native/run-lampac.sh > lampac-native/lampac-native.log 2>&1 &

# 5. verify
grep -nE "compilation error|loaded module: <Name>" lampac-native/lampac-native.log | tail -5
```

Chi `--only-app` (publish) khi sua `Core/` hoac `Shared/` — ton RAM, tranh dung
tren mobile.

> Luu y: `csc/check.sh` can `Shared.dll` cua runtime. Neu chua co, dat
> `SHARED_DLL=lampac-run/Shared.dll`.

## Còn lại

- **Không có GPU** (`/dev/kgsl` không tồn tại) → Chrome dùng swiftshader (CPU),
  render cold ~3.2s
- **`CapEff=0` / `Seccomp=2`** → Chrome phải `--no-sandbox` (đã có trong init.conf)
- Module chưa sửa xong: xem log `lampac-native/start*.log`
