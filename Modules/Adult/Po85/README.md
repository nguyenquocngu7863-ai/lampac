# Po85 (85po.com, KVS)

Nguồn Adult cho 85po.com (engine KVS, giống Porntrex). Route gốc: `/po85`.

## Routes

| Route | Chức năng |
|---|---|
| `/po85?search=&sort=&c=&pg=` | Menu + playlist (Mới nhất, 4K, Đánh giá cao, Xem nhiều nhất, Tìm kiếm) |
| `/po85/vidosik?uri=<url trang video>` | Trả dict link: nhãn chất lượng → URL `/po85/strem?link=` |
| `/po85/strem?link=<get_file>` | Resolve redirect, fallback proxy trực tiếp file kèm Referer |

## Cơ chế đã xác minh trên thiết bị (2026-09-05)

- List trang chủ: 60 items, selector `.thumb a[href*="/v/"]`, ảnh `data-original`, chất lượng ở class `qualtiy` (viết sai chính tả từ site), thời lượng `.time`.
- Trang video: `flashvars.video_url = 'function/0/<get_file mp4>?br=NNN'` — tiền tố `function/N/` chỉ là marker của KVS player, strip là dùng được. Token ổn định theo video (không đổi mỗi lần load).
- Link flashvars có thể 404 (hash cũ); link download trong dropdown (`?download_filename=...&download=true`, nhãn "MP4 480p, 18.93 Mb") trả `206 video/mp4` ổn định → module ưu tiên link download.
- `get_file` trả file trực tiếp, không redirect → `Strem` fallback `HostStreamProxy(link)` kèm `Referer: https://www.85po.com/` khi `GetLocation` rỗng.
- Cloudflare của 85po chặn fingerprint lạ (curl trần, Chrome headless proot) nhưng cho qua HTTP client của Lampac với header trình duyệt đầy đủ.
