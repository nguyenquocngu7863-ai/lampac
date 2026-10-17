# TangThon

Module SISI cho `tangthon.com`.

## Bẫy domain xoay (2026-10-17)

Site chạy nhiều domain song song và **dùng chung một DB phim**:

| vai trò | domain |
|---|---|
| site (list/detail) | `tangthon.com`, `billygreyguitar.com`, `sexhaypro.com` … |
| player embed | `embed.billygreyguitar.com` (trước là `embed.tangthon.com`) |

- `https://tangthon.com/phim-sex-moi` → 200, nhưng **card href trỏ sang domain khác**
  (`https://billygreyguitar.com/phim-sex/<slug>`) → parser phải nhận href **absolute**,
  không được ghép cứng với `host`.
- Trang detail dựng iframe `<iframe src="https://embed.<domain>/embed/<id>/pN?logo=9&autoplay=1">`.
  **Không hardcode host embed** — phải bám theo chính `src` của iframe, vì host embed
  đổi theo domain mà site đang phục vụ.
- Trang embed chứa `let m3u8_file_url = '\/phim-sex\/<id>\/cdn.m3u8'` → đường dẫn
  **relative**, nối vào chính host embed.

Đã từng hỏng vì regex cứng `embed\.tangthon\.com` → `GetEmbedId` trả null → `/vidosik`
trả 503 `stream_links` dù list vẫn ra 20 item.

## Công thức resolve

```
detail (bất kỳ domain)  →  regex  (?:https?:)?//<host>/embed/<id>/p\d+     => embedUrl
embedUrl                →  regex  m3u8_file_url\s*=\s*['"]([^'"]+)['"]     => path
path (relative)         →  nối scheme://host của embedUrl                  => cdn.m3u8
```

`Referer` bắt buộc (thiếu → 403). Nhưng server chấp nhận **bất kỳ** domain nào của
site (`embed.*`, site chính, mirror) — nên `RefererOf(link)` lấy host của chính link
là đủ và tự đúng khi domain xoay.

## Segment

`https://p16-oec-sg.ibyteimg.com/obj/tos-alisg-i-aphluv4xwc-sg/<hash>`

- Playlist là **1 level** (VOD, 815 segment, `#EXTINF:9.876533`), segment URL absolute.
- CDN có lúc trả segment **gắn tiền tố PNG** (`89504e47…`, `Content-Type: image/png`,
  TS thật bắt đầu ở offset ~95) và có lúc trả TS thuần (`47 40 00 10`).
  Patch `PngStripLoader` trong `Modules/LampaWeb/plugins/lampainit.js` xử lý cả hai →
  **không cần strip ở server**.
- Segment **không** cần Referer; playlist thì có.

## Kiểm chứng (2026-10-17, localhost 9118)

```
GET /tangthon                                  -> 200, 20 item
GET /tangthon?c=phim-sex-hot                   -> 200, 20 item
GET /tangthon?c=phim-sex-danh-muc/phim-sex-viet-nam -> 200, 24 item
GET /tangthon?search=em                        -> 200, 24 item
GET /tangthon/vidosik?uri=<detail>             -> 200 {"HLS":".../tangthon/video.m3u8?...&q=HLS"}
GET /tangthon/video.m3u8?uri=<detail>&q=HLS    -> 302 /proxy/...  -> 200 #EXTM3U (815 #EXTINF)
GET <segment qua proxy>                        -> 200 video/mp2t, magic 47 40 00 10
```
