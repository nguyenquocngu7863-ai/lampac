# MX Sub Bridge

APK bridge: **Lampa → SubSense → MX Player** (có phụ đề)

## Cách hoạt động

```
Lampa plugin (subsense-auto-v2.js)
  ↓ hook Lampa.Android.openPlayer()
  ↓ fetch sub từ SubSense
  ↓ mã hóa subtitleMeta (base64url)
  ↓ append &subtitleMeta=... vào URL
  ↓
MX Sub Bridge APK
  ↓ nhận video URL + subtitleMeta
  ↓ giải mã → lấy danh sách sub
  ↓ mở MX Player với video + sub
  ↓
MX Player (phát video + phụ đề)
```

## Build trên Termux

```bash
cd ~/mx-sub-bridge
bash build-termux.sh
```

**Yêu cầu:**
- Termux + `pkg install wget unzip openjdk-17 aapt2`
- Android SDK (script tự cài)

## Cài đặt

```bash
termux-open ~/mx-sub-bridge.apk
```

## Cấu hình Lampa

1. Cài plugin SubSense:
```
https://cdn.jsdelivr.net/gh/nguyenquocngu7863-ai/lampac@7c5aa21/subsense-auto-v2.js
```

2. Settings → SubSense → nhập Manifest URL

3. Khi phát video → chọn "Mở bằng MX Sub Bridge"

## subtitleMeta format

```
base64url(JSON([
  {"url": "https://...", "label": "Sub 1", "language": "vi"},
  {"url": "https://...", "label": "Sub 2", "language": "en"}
]))
```

## MX Player Intent extras

```java
intent.putExtra("subs", subUrls);      // ArrayList<String>
intent.putExtra("subs.name", subNames); // ArrayList<String>
```
