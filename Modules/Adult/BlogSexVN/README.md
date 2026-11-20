# BlogSexVN

Module Adult cho **blogsexvn.net** — phim sex Việt Nam, clip sex mới cập nhật liên tục.

## Site

- Host: `https://blogsexvn.net` (Laravel + theme Flowbit, `lang="vi-VN"`)
- List HTML server-side, không cần Playwright
- Phân trang: `/trang/N/` (home, category, search)

## Cấu trúc site đo được (2026-11-20)

| thứ | URL / markup |
|---|---|
| Card list | `<li class="item-movie">` → `<a title href>` + `<img data-original>` + `<span class="top-view">` + `<div class="label">VN</div>` + `<div class="title-movie">` |
| Category | `/phim-sex-viet-nam/` `/phim-sex-nhat-ban/` `/phim-sex-trung-quoc/` `/phim-sex-chau-au/` `/phim-sex-vietsub/` |
| Tag | `/tag/<slug>/` — 50 tag lấy từ khối "THẺ HOT" trang chủ, **không phân trang** (site trả 0 item ở `/tag/x/trang/2/`) |
| Search | `/search/<slug-khong-dau>/` (JS site: slugify → lower → space → `-`) |
| Detail | `<div id="video" data-video-id="4201">` + `<li class="video-server" data-server-id="1">` |
| Player API | `POST /api/player` form `id={videoId}&server={serverId}` → JSON `{status, data:{type:"jwplayer", sources:[{file:"…m3u8"}]}}` |
| CDN stream | `https://domain115.com/<path>/output.m3u8?cdn=tt` (HLS, không AES) |

## Bẫy đã gặp — segment PNG-decoy (quan trọng)

Segment HLS **KHÔNG phải** file `.ts` thật. Mỗi segment trả về:

```
[PNG 1x1 + 151 byte fill 0xFF]  ← 238 byte đầu
[MPEG-TS thật]                  ← H.264 1920x1080 + AAC, 14360 gói 188 byte
```

- `Content-Type: image/png`, magic `89504e47` → nhìn như ảnh rác
- Nhưng TS offset **cố định 238**: `data[238] == data[238+188] == data[238+376] == 0x47`
- `ffprobe` đọc được `h264 1920x1080` sau khi cắt prefix
- `hls.js` **không** chịu được tiền tố này → báo *"Found N TS packet/s that do not start with 0x47"* → màn hình đen

⇒ Bắt buộc cắt prefix **server-side** trước khi trả cho player. Module làm việc này
qua `EventListener.ProxyApiOverride` (`ModInit.StripPngJunk`): tải segment, dò
sync-byte TS đầu tiên, trả lại `video/mp2t` sạch.

Segment URL nằm trên `*.tiktokcdn.com` (host `p16/p19-ad-site-sign-sg.tiktokcdn.com`),
không phải `domain115.com` — nên điều kiện lọc URI phải nhận **cả hai**.

Referer **không** ảnh hưởng tới CDN (đã thử blogsexvn / vietsex.blog / rỗng / random → kết quả giống nhau).

## Menu (chuẩn 9g)

- dòng 1: `Tìm kiếm` (`search_on`)
- dòng 2: **bỏ hẳn** — site không có sort thật ở bất kỳ context nào (đo: `total` + sequence không đổi)
- dòng 3+: `Danh sách` (Mới nhất + 5 category) và `Tag hot` (50 tag), cache 720 phút

Menu **100% tiếng Việt/Anh** — cả 50 tag của site đã là tiếng Việt, không cần bảng dịch.

## Thumbnail

Site **không** in duration, **không** in badge chất lượng trên card.
→ `name` = tiêu đề card, `time` = nhãn site in (`<div class="label">VN</div>`),
`quality` = `null`. Không bịa nhãn.

## Kiểm thử

```
# 5 endpoint
curl "http://127.0.0.1:9118/blogsexvn"                                  # home  (28 item, 147 trang)
curl "http://127.0.0.1:9118/blogsexvn?c=phim-sex-viet-nam"             # category
curl "http://127.0.0.1:9118/blogsexvn?c=tag/khong-che"                  # tag
curl "http://127.0.0.1:9118/blogsexvn?search=sexy"                      # search
curl "http://127.0.0.1:9118/blogsexvn?pg=2"                            # trang 2
curl "http://127.0.0.1:9118/blogsexvn/vidosik?uri=<url detail>"        # server
curl -L "http://127.0.0.1:9118/blogsexvn/video.m3u8?uri=<url detail>&srv=1"

# smoke phát thật
~/lampac-native/play.sh "http://127.0.0.1:9118/blogsexvn/video.m3u8?uri=<url detail>&srv=1"
```

Kết quả 2026-11-20: 2/2 video **PASS** (31/31 và 20/20 segment qua Lampac, TS offset 0,
decode 5s đầu + 5s cuối OK, h264 1920x1080).
