# JavSub (javsub.blog)

Module Adult cho javsub.blog (Việt + Nhật).

## Cấu hình
- `manifest.json`: module Adult, `dynamic: true` (compile runtime, không cần csproj)
- `ModInit.cs`: `SisiSettings("JavSub", https://javsub.blog)` — host đọc từ `init.conf` nếu có
  `JavSub.host`, không thì dùng `JavSubTo.SiteHost`

## Route
| Route | Việc |
|---|---|
| `GET javsub` | list / search / `?c=` danh mục (the-loai) |
| `GET javsub/vidosik?uri=` | danh sách server của 1 phim — trả `{"Server N": url}` |
| `GET javsub/video.m3u8?uri=&srv=` | resolve server → 302 `/proxy/<token>` |
| `GET javsub/video?uri=&srv=` / `javsub/video.mp4` | như trên (giữ cho tương thích) |

## Player: 2 backend lazy-load
Trang chi tiết chỉ có nút `button.set-player-source[data-source]` (embed host), **không** có link
trực tiếp. Mỗi server phải resolve riêng:

1. `GET <embed-host>/videos/<id>/play?...` → HTML embed; nếu có `sources` inline (kiểu vcast) là xong.
2. Không có thì `POST <embed-host>/videos/<id>/config?d=` (body rỗng, JSON) → `{"sources":[{"file":"…m3u8"}]}`.
3. 404 với mọi biến thể → fallback Chrome (Playwright) mở embed, bắt response `/config`.

Kết quả cache `javsub:stream:{pageUrl}:{label}` 10 phút.

## Bẫy: URL phải có đuôi `.m3u8`
Lampa chọn player theo **đuôi URL** (`app.min.js`: `if (/\.m3u8/.test(src))` mới dùng hls.js).
Link `/javsub/video?...` không đuôi → Lampa phát file m3u8 bằng thẻ `<video>` → **đen hình mọi server**
(site vẫn 200, curl vẫn ra `#EXTM3U`, nên rất dễ kết luận sai là "site đổi cơ chế").

`vidosik` build `video.m3u8` (mặc định), và nếu cache đã có link `.mp4` thì đổi sang `video.mp4`.

## Đo (2026-10-21)
- 8 video liên tiếp: 1–2 server/video, cả 2 backend trả `200 application/vnd.apple.mpegurl`.
- Master → variant → segment: `200`, segment `video/mp2t`, byte đầu `0x47` (TS).
- Playlist của backend 2 là **media playlist** (932–1478 segment), segment URI tương đối →
  Core proxy đã rewrite thành `/proxy/<token>` (OK).
