# VidLink

## Trạng thái: ĐÓNG dự án (2026-09-01) — mặc định tắt, code giữ nguyên

Bằng chứng: cùng một trang được test bằng **hai stack độc lập** — module Lampac (C#) và plugin
CloudStream (Kotlin, chính là stack mà CSX dùng) — **cả hai đều không lấy được link**. Kết luận:
`vidlink.pro` chặn theo **chính sách embed** (link/CDN chỉ sống trong session embed của player
nhúng), không phải selector sai, không phải thiếu header, không phải thiếu Playwright. Loại này
không cày được bằng scraper; mỗi vòng sửa chỉ tốn thời gian mà không có đường thắng.

Vì vậy `ModInit.cs` đổi mặc định về `enable = false, enabled = false`. Không phải để "tắt cho
khỏi lỗi" mà vì khi bật, **mọi** lần mở phim đều gọi `vidlink.pro` với `httptimeout: 20` — một
nguồn chết làm chậm toàn bộ danh sách Online.

| Muốn | Làm |
|---|---|
| Tắt hẳn (mặc định mới) | không cần sửa gì; nếu `init.conf` từng có `"VidLink": {"enable": true}` thì xoá dòng đó |
| Thử lại / đào tiếp | `"VidLink": { "enable": true, "enabled": true }` rồi `lampac stop && lampac start` |
| Con đường duy nhất còn lại | trình duyệt thật: vào trang nhúng, **nghe request** của player (đúng kiểu Mirage/Playwright làm) — không phải `?s=` hay regex |

Toàn bộ phần dưới còn nguyên để người sau biết từng thử gì (XSalsa20-Poly1305 token,
`/api/b/movie|tv`, `bcdn.hakunaymatata.com` 429 khi probe, `playlist.m3u8` rewrite segment).
Quy tắc chung cho mọi nguồn: mục "Khi nào nên DỪNG" trong
[`notes/FILEHOST-SOURCE-FORMULA.md`](../../../notes/FILEHOST-SOURCE-FORMULA.md).

Nguồn ENG `https://vidlink.pro`. Resolver mặc định là **HTTP** (token XSalsa20-Poly1305 → `/api/b/movie|tv/...`). Không cần Playwright. Playwright chỉ còn là bước dự phòng.

## Hiện nguồn

- `disableEng: false` — hiện như các nguồn ENG khác.
- `disableEng: true` (mặc định Termux) — vẫn hiện vì `enabled` mặc định `true`. Tắt bằng `"VidLink": { "enabled": false }` hoặc `"enable": false`.

## Cấu hình

```json
"VidLink": {
  "enable": true,
  "enabled": true,
  "streamproxy": true,
  "httptimeout": 20
}
```

## HTTP

CDN (`bcdn.hakunaymatata.com`) 429 nếu probe nhiều lần. Không probe lúc resolve. Play URL luôn `/lite/vidlink/playlist.m3u8?uri=` (HLS). Header CDN `filmboom.top`. Token enc-dec. Segment `media.ts?uri=`, không `/proxy/`.

| Route | Việc |
|-------|------|
| `lite/vidlink` | Danh sách phim/tập |
| `lite/vidlink/video` | Stream |
| `lite/vidlink/video.m3u8` | Cùng resolver |
| `lite/vidlink/playlist.m3u8` | Tải HLS, viết lại segment |
| `lite/vidlink/media.mp4` | Stream MP4 (có `uri=`) |
| `lite/vidlink/file.mp4` | Alias MP4 (có `uri=`) |
| `lite/vidlink/media.ts` | Segment HLS |

## Files

`ModInit.cs`, `Controller.cs`.
