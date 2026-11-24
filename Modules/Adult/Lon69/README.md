# Lon69

Site: **`https://lon69.ink`** (đổi từ `lon69.site` 2026-11-25 — `.site` chết TLS, markup cũng đổi sang theme **vlxx**).
Route: `/lon69` · `/lon69/vidosik` · `/lon69/video.m3u8` · `/lon69/video.mp4` · `/lon69/genres` · `/lon69?rel=`.

## Endpoint

| route | mô tả |
|---|---|
| `lon69?search=&c=&pg=` | list (home / thể loại / tag / search), `total_pages` từ `/page/N/` |
| `lon69/vidosik?uri=<detailUrl>` | trả `{"HLS": ...}` hoặc `{"MP4": ...}` theo nguồn thật |
| `lon69/video.m3u8?uri=` · `lon69/video.mp4?uri=` | proxy qua Lampac (Referer = origin của nguồn) |
| `lon69/genres?uri=<detailUrl>` | popup: **Thể loại · Thẻ · Liên quan** |
| `lon69?rel=<detailUrl>` | 30 phim "Phim Liên Quan" (`total_pages: 1`, `pg>1` → rỗng) |

## Markup (đo 2026-11-25, theme `vlxx`)

```html
<div id="video-36784" class="video-item">
  <a title="Tên" href="https://lon69.ink/<slug>/">
    <span class="nv-thumb">
      <img class="video-image" src="...360x270.jpg">
      <span class="nv-badge nv-q nv-q-fhd">1080p</span><span class="nv-badge nv-dur">31:58</span>
    </span>
  </a>
  <div class="video-name"><a href="...">Tên</a></div>
</div>
```

- `time` ← `span.nv-dur`, `quality` ← `span.nv-q` (thiếu badge → `null`, **không bịa**).
- Phân trang: `/page/N/` (home `tp 73`, notofans `tp 1`, `phim-sex-viet-nam` `tp 71`, search `jav` `tp 4`).
- **Site KHÔNG có sort** (0 chữ `sort` trong HTML mới) → **không có dòng 2** "Sắp xếp".

## Popup (long-press)

| nhóm | nguồn | ví dụ (co-giao-vu-to-kinh-can…) |
|---|---|---|
| `Thể loại` | `div.category-tag a[href=/phim-sex-…/]` | Việt nam |
| `Thẻ` | `div.actress-tag a[href=/tag/<slug>/]` (nhiều block) | chongtoico · gái non · heovl · không che … (9) |
| `Liên quan` | `aside.related_single` → `?rel=` (`plain:true`) | 30 phim |

- SSRF: `uri` chỉ nhận host `lon69.ink|lon69.site|lon69.com`; host lạ → `{"items":[],"groups":[]}`.
- Cache: chip 6h theo pageUrl, related 180s, stream 120s (`lon69:stream:v4:`).

## Player

1. Detail → `<noscript><iframe src="https://sever1.playphe3x.space/play.php?id=<id>">`.
2. Fetch iframe (thử UA mobile **rồi** desktop, `&autoplay=1`, `sever1..4`) → `let SRC = "https://sever4.playphe3x.space/hls.php?id=…&p=master.m3u8&token=…"`.
3. `SRC` là **HLS master** (đuôi `.m3u8` nằm trong query `p=master.m3u8`) → route `video.m3u8`.

**Bẫy đã dính**: trang detail luôn chứa quảng cáo preroll `sever3.playphe3x.space/tvc.mp4` (31s, ~18 MB).
Quét `.mp4` bừa trên detail/player → trả **nhầm ad** (vì `SRC` của bản mobile có thể không có). Nay:
chỉ đọc `SRC`/`hls.php` và **loại mọi URL chứa `tvc.mp4` / `/tvc` / `advideo`** (`Lon69To.IsAd`).

## Trạng thái (2026-11-25)

```
home 30 tp 73 · pg2 30 · phim-sex-viet-nam 30 tp 71 · onlyfans 23 tp 1 · tag/chim-to 7 tp 1
search "jav" 30 tp 4 · genres: Thể loại 1 + Thẻ 9 + Liên quan · rel 30 tp 1 · rel pg2 rỗng · SSRF rỗng
vidosik → HLS (SRC master.m3u8) · play.sh PASS h264 430x720 · 597.2s · decode 5s đầu/cuối
MP4 route giữ cho video nguồn progressive (đã đo PASS 1280x720 · 31.2s trước khi lọc ad)
```

Module đang **bật** (`init.conf` trước đây để `"enable": false`).
