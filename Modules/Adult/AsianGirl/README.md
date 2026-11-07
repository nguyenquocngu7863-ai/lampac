# AsianGirl (asiangirl.porn)

Module Adult route `asiangirl`, displayindex 37. Parse tĩnh `httpHydra` + regex
(không Playwright, không JS). Site render server-side, không Cloudflare challenge.

## Route

| route | việc |
|---|---|
| `asiangirl?c=<path>&pg=N&sort=X` | danh sách + menu (mặc định `/new/`) |
| `asiangirl?search=<kw>&pg=N` | tìm kiếm |
| `asiangirl?rel=<detailUrl>` | video liên quan (`total_pages: 1`) |
| `asiangirl/vidosik?uri=<detailUrl>` | liệt kê nguồn (`{HLS: route}`) |
| `asiangirl/video.m3u8?uri=<detailUrl>&q=HLS` | proxy HLS qua `HostStreamProxy` |
| `asiangirl/genres?uri=<detailUrl>` | chip Thể loại + mục "Liên quan" |

## Đã đo (2026-11-06)

| path | kết quả |
|---|---|
| `/` | 72 card nhưng trộn nhiều section → **không dùng**, home = `/new/` |
| `/new/?from=N` | 24 item/trang, `from` = số trang (1-based) |
| `/categories/<slug>/?from=N` | 24 item, `from` = số trang |
| `/categories/<slug>/?sort_by=X` | 4 sort: `post_date`, `most_favourited`, `video_viewed_month`, `video_viewed` |
| `/hot/?from=N&sort_by=X` | 4 sort: `video_viewed` (mặc định), `_today`, `_week`, `_month` |
| `/search/?q=<kw>` | 24 item; phân trang bằng **`from_videos=<(pg-1)*24>`** (offset, KHÔNG phải `from`) |
| `/v/<id>/` | `var stream = '...m3u8'`, token sống ~2h |
| `/categories/` | 33 category (`card-cat-v2__link`) |

- Card list: `<div class="item card-video col-1">`, poster `data-src`, preview `data-preview`,
  thời lượng `card-video__duration`, tiêu đề `card-video__title` (bị site cắt "...", trang detail mới đủ).
- **Card quảng cáo dùng cùng class** nhưng `href` ra `t.fluxtrck.site` → lọc theo host site.
- Detail chỉ có **Thể loại** (không có diễn viên/tag). Liên quan nằm ở 2 block
  `list_videos_related_videos_items` + `list_videos_recommended_videos_items`.
- HLS: AES-128, key URI là file `.ts` 16B — Core proxy rewrite `EXT-X-KEY` nên chạy bình thường.
  `play.sh` → **PASS** (decode 5s đầu/cuối, h264 1280x720).
