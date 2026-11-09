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
bash dshgo/restore.sh
```

Script cài apt packages, pip curl_cffi, symlink node/npm, đặt curl shim,
cài playwright-core cho Po85.

## Còn lại

- **Không có GPU** (`/dev/kgsl` không tồn tại) → Chrome dùng swiftshader (CPU),
  render cold ~3.2s
- **`CapEff=0` / `Seccomp=2`** → Chrome phải `--no-sandbox` (đã có trong init.conf)
- Module chưa sửa xong: xem log `lampac-native/start*.log`
