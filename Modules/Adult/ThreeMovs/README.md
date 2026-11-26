# ThreeMovs

Module SISI cho **3Movs** — `https://www.3movs.com` (tube tiếng Anh, MP4 trực tiếp, không cần giải mã player).

Route: **`threemovs`** · alias hiển thị: **ThreeMovs** · manifest `dynamic: true` (không cần `.csproj`).

## Đo site (2026-11-26)

| path | kết quả |
|---|---|
| `/` (home) | 200, 200 KB — home trộn nhiều block; module dùng `latest-updates` làm mặc định |
| `/latest-updates/` | 200, 36 item, phân trang `…/2/` (3664 trang) |
| `/latest-updates/2/` | 200, 36 item, **khác** trang 1 |
| `/top-rated/week/`, `/top-rated/week/2/` | 200, 36 item (site tự link phân trang về `/top-rated/2/` — bỏ period, module tự ghép đúng) |
| `/most-viewed/{today,week,month,all-time}/` | 200 |
| `/categories/` | 200, 82 category (1 trang, không phân trang) |
| `/categories/anal/` | 200, 36 item, phân trang `…/2/` → 428 trang |
| `/categories/anal/most-viewed/month/` | 200 — sort + page nằm SAU path |
| `/pornstars/` | 200, ~39/trang, 5 trang → module lấy 3 trang đầu |
| `/pornstars/angela-white/` | 200, 36 item, sort + `…/2/` giống category |
| `/tags/busty/` | 200, 36 item, sort + `…/2/` |
| `/search_videos/?q=anal` | 200, 24 item — **site KHÔNG phân trang search** (`/search_videos/2/?q=` trả lại đúng trang 1) |
| `/search_videos/{latest-updates,most-viewed/month,top-rated/month,longest}/?q=` | 200 — sort cho search |
| detail `/videos/<id>/<slug>/` | 200 — `var flashvars = { video_url: '…469113.mp4/?v-acctoken=…', video_url_text, video_alt_url …_lq.mp4 }` |

## Stream

`video_url` (HD) + `video_alt_url` (SD) nằm thẳng trong `flashvars` của trang detail — KVS, không obfuscate.

- Kiểm chứng: `get_file/…mp4/?v-acctoken=…` trả **206** `video/mp4` (82 MB), ffprobe `h264 1280x720` + `aac`, ffmpeg decode 5 s OK.
- `v-acctoken` sinh theo request trang detail → module luôn fetch detail mới, không cache token.
- `streamproxy = true` + `headers_stream` (UA Chrome, `Referer: https://www.3movs.com/`) vì CDN đòi Referer.

## Menu SISI

- Dòng 1: **Tìm kiếm**.
- Dòng 2: **Sắp xếp: …** chỉ ở context có sort thật (category / tag / pornstar / search) — `latest-updates`, `top-rated/*`, `most-viewed/*` là **listing** (đổi tập) nên ẩn dòng 2.
  - Category/tag/pornstar: Mới nhất · Xem nhiều · Đánh giá cao · Dài nhất.
  - Search: Liên quan nhất · Mới nhất · Xem nhiều · Đánh giá cao · Dài nhất.
- Dòng 3: **Danh sách** 8 mục (latest-updates, most-viewed month/week, top-rated today/week/month/all-time, longest).
- Dòng 4: **Thể loại** 82 (từ `/categories/`, cache 12 h), **Diễn viên** ~90 (từ `/pornstars/` trang 1–3).

## Popup (nhấn giữ)

- `/threemovs/genres?uri=<detail>` → chip từ khối `<strong>Tags:</strong>` / `Categories:` / `Models:` trong trang detail (giới hạn từ đó tới `video_comments_` để không lấy sidebar "Recently Active Pornstars").
- `?rel=<detail>` → mục **Liên quan** (`plain: true`), list lấy từ block `list_videos_related_videos_items` sẵn trong trang detail (không fetch thêm), `pg=2` → rỗng.

## Verify (2026-11-26)

```
check.sh            0 error
home                36 item · tp=3664
pg=2                36 item · khác trang 1
search=anal         24 item · tp=1 · menu có "Sắp xếp: liên quan nhất"
c=categories/anal&sort=most-viewed/month   36 item · tp=428 · menu "Sắp xếp: xem nhiều"
genres              3 nhóm / 6 chip (Thể loại 3 · Thẻ 2 · Liên quan 1)
rel                 16 item · tp=1
vidosik             HD + SD · ffprobe h264 1280x720 + aac · decode 5 s OK
popup.sh threemovs  PASS
```
