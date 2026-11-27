# Mbbg (mbbg.website)

Module Adult route `mbbg`. Site đổi host liên tục: `.gay` → `.hair` → **`.website`** (2026-11-27).

## List
- Home `https://mbbg.website/` — card `<a href="…/<slug>.html" title="…">` + `div.post-thumbnail`.
- Card có thể trỏ host khác (`mbbg.guru`…) nhưng cùng nội dung; parser lấy URL tuyệt đối trong card.

## Player (2 chặng)
1. Trang detail: theme mới **không còn** `var wpst_ajax_var`/`nonce`; player nằm ở
   `<meta itemprop="contentURL" content="https://…/embed/<b64>.html">`.
2. `POST /wp-admin/admin-ajax.php` với `action=getplayer&source=["<contentURL>"]&server=1`
   (nonce tuỳ chọn — có thì gửi) → `{"file":"https://www.blogger.com/video.g?token=…"}`.
3. `batchexecute` (rpcid `WcwnYd`) đổi token → URL `googlevideo.com/videoplayback` (MP4).
   Route `mbbg/video.mp4?uri=&q=` stream MP4 (Range).

## Bẫy
- Embed host trong HTML (`s63.xvideo.lat`) có thể **NXDOMAIN** — không cần fetch embed,
  chỉ cần `contentURL` để gọi `getplayer`.
- `nonce` không bắt buộc: thiếu nonce mà vẫn `return null` thì mất toàn bộ phim (lỗi 2026-11-27).
- Trang list ghi `total_pages: 0` (site không trả số trang) — app cuộn vô hạn.

## Đã đo (2026-11-27)
- `/mbbg` 20 item · `vidosik` `{qualitys: 360p/720p, recomends}` · `ffprobe` route 720p:
  h264 404x720 + aac 64.9s.
