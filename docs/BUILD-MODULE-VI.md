---
title: "Hướng dẫn tự build module Lampac: biên dịch Roslyn"
sidebarTitle: "Tự build module"
description: "Tạo module của riêng bạn trong thư mục mods/. Roslyn biên dịch các file .cs khi khởi động; bật dynamic:true để hot-reload."
keywords: ["Lampac custom modules", "Roslyn", "manifest.json", "mods", "C#"]
---

# Hướng dẫn tự build module Lampac (biên dịch Roslyn)

Lampac NextGen cho phép bạn mở rộng chức năng bằng **module tự viết**. Chỉ cần tạo một thư mục trong `mods/`, thêm `manifest.json` và các file `.cs` — Roslyn sẽ tự biên dịch code khi server khởi động. Nếu bật cờ `dynamic: true`, code sẽ được **tự biên dịch lại** mỗi khi bạn sửa file, không cần khởi động lại server.

## Các bước

### 1. Tạo thư mục module

Tạo một thư mục nằm trong `mods/`, đặt tên tùy ý, ví dụ `mods/MyModule/`.

### 2. Viết manifest.json

Manifest mô tả metadata của module và điều khiển cách nó được tải.

```json
{
  "enable": true,
  "dynamic": true,
  "tree": [
    "Controller.cs",
    "ModInit.cs"
  ]
}
```

| Trường | Kiểu | Mô tả |
| --- | --- | --- |
| `enable` | boolean | Có bật module hay không |
| `dynamic` | boolean | Hot-reload khi file `.cs` thay đổi |
| `tree` | string[] | Các file được đưa vào biên dịch Roslyn |

### 3. Viết các file .cs

Thêm vào `mods/MyModule/` một hoặc nhiều file `.cs` chứa logic của module. Dùng các interface `IModuleLoaded` và `IModuleConfigure` để gắn vào vòng đời của ứng dụng.

```csharp
using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;

namespace MyModule
{
    public class ModInit : IModuleLoaded
    {
        public static ModuleBaseConf conf;

        public void Loaded(InitspaceModel baseconf)
        {
            updateConf();
            EventListener.UpdateInitFile += updateConf;
        }

        public void Dispose()
        {
            EventListener.UpdateInitFile -= updateConf;
        }

        void updateConf()
        {
            conf = ModuleInvoke.Init("MyModule", new ModuleBaseConf { enable = true });
        }
    }
}
```

### 4. Khởi động lại server

Lần tải đầu tiên, Roslyn biên dịch module của bạn. Nếu bật `dynamic`, các thay đổi `.cs` sau đó sẽ được biên dịch lại tự động.

## Lọc module bằng LoadModules

Bạn có thể giới hạn danh sách module được tải bằng `BaseModule.LoadModules` trong `init.conf`. Hỗ trợ: tên module, nhóm (ví dụ `OnlineUKR`) hoặc mặt nạ bằng regex.

| Mẫu | Ví dụ | Mô tả |
| --- | --- | --- |
| Tên module | `MyModule` | Tải đúng module cụ thể |
| Nhóm | `OnlineUKR` | Tải tất cả module của nhóm |
| Mặt nạ | `LME.*` | Tải module theo biểu thức chính quy |

```json
{
  "BaseModule": {
    "LoadModules": [".*"]
  }
}
```

## Mount trong Docker

Khi chạy Docker, hãy mount thư mục chứa module tự viết thành một volume:

```yaml
services:
  lampac:
    image: ghcr.io/lampac-nextgen/lampac
    volumes:
      - ./lampac-docker/mods/MyModule:/lampac/mods/MyModule
```

> **Mẹo:** đặt `"dynamic": true` trong `manifest.json`. Roslyn theo dõi sự thay đổi của các file `.cs` trong thư mục module và biên dịch lại ngay lập tức. Rất tiện cho việc phát triển và gỡ lỗi.
