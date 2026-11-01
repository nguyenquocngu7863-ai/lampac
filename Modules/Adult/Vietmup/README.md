# Vietmup Module

**Site**: https://vietmup.lol  
**Status**: ✅ Hoạt động  
**Deploy**: 2026-10-30

## Cấu trúc

- `manifest.json` — khai báo module dynamic
- `ModInit.cs` — IModuleLoaded + IModuleSisi, SisiSettings
- `Service.cs` — Uri(), Playlist(), StreamToken(), Categories(), MenuHead(), Menu()
- `Controller.cs` — Index, Vidosik, Video

## Smoke Test (Python)

```python
from curl_cffi import requests
import json

s = requests.Session(impersonate="chrome124")
H = {"User-Agent": "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36"}
B = "http://127.0.0.1:9118"

def test(name, url):
    r = s.get(url, headers=H, timeout=30)
    d = r.json()
    count = d.get("count", 0)
    list_items = d.get("list", [])  # ⚠️ SISI trả "list", KHÔNG phải "items"
    menu = d.get("menu", [])
    print(f"{name}: count={count}, list={len(list_items)}, menu={len(menu)}")
    return count > 0

test("HOME", f"{B}/vietmup")
test("HOME pg=2", f"{B}/vietmup?pg=2")
test("CATEGORY vietnam", f"{B}/vietmup?c=vietnam")
test("SEARCH tiktok", f"{B}/vietmup?search=tiktok")
```

## ⚠️ Bài học test

**Response SISI trả `"count"` + `"list"` chứ KHÔNG phải `"items"`**.

Khi smoke test bằng script ngoài, phải đọc đúng field:
- ✅ Đúng: `d.get("count", 0)` và `d.get("list", [])`
- ❌ Sai: `d.get("items", [])` → luôn trả 0, kết luận oan "module lỗi"

Trên app Lampa hoạt động bình thường vì app đọc đúng schema của `PlaylistResult`.

## Stream

- Player có 4 server: Embed Source, Google, Abyss, **Vip (m3u8)**
- Server Vip: `/api/stream?t=<token>` → trả `#EXTM3U` trực tiếp
- Module resolve server Vip, proxy qua Lampac

## Menu

- Dòng 1: Tìm kiếm
- Dòng 2: Sắp xếp (Mặc định / Mới nhất / Xem nhiều nhất / Hot trong tuần)
- Dòng 3: Danh mục (14 category lấy động từ home page)