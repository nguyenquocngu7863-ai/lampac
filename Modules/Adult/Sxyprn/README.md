# Sxyprn (YourPorn / SexyPorn)

Module Lampac cho `https://www.sxyprn.com` (site trong ThePornDude top 10 — "SxyPrn / YourPorn").

## Điểm khó

URL stream **không có trong HTML**. Trang post chứa:

```html
<span class='vidsnfo' data-vnfo='{"6b081e3576dc2":"/cdn/c7/<dir>/<sig1>/<exp>/<sig2>/<file>.vid"}'></span>
```

`/js/main2.js` (`getvsrc`) biến đổi path đó thành URL thật trước khi gán cho `<video>`:

1. `t = src.split('/')`
2. `t[1] += "8/" + boo(sum(t[6]), sum(t[7]))`
3. `t[5] = int(t[5]) - sum(t[6]) - sum(t[7])`
4. `boo(a,b) = base64("a-www.sxyprn.com-b")`, thay `+`→`-`, `/`→`_`, `=`→`.`

`sum()` = tổng các chữ số trong chuỗi (`ssut51`). Kết quả `/cdn8/...` trả **302** sang
`c7.trafficdeposit.com/vidi/...` (MP4 progressive, có `Accept-Ranges`, không cần Referer/cookie).
Path được ký lại mỗi lần tải trang → module tính lại từ HTML lúc play (`sxyprn/vidosik`), cache 20 phút.

## Route

| Route | Chức năng |
|---|---|
| `/sxyprn` | Listing "Mới nhất" (`/All.html?page=`) — 30 item/trang, 297 trang |
| `/sxyprn?pg=2` | Trang 2 (offset 30) |
| `/sxyprn?sort=trending\|views\|orgasmic` | Sort thật của site (`sm=`) |
| `/sxyprn?c=top` / `?c=viewed` | `/popular/top-pop.html`, `/popular/top-viewed.html` (1 trang) |
| `/sxyprn?c=tag:Hardcore` | Trang tag (`/Hardcore.html?page=&sm=`) |
| `/sxyprn?search=blake` | `POST /php/livesearch.php` → tag gợi ý đầu tiên → list |
| `/sxyprn/genres?uri=` | Popup: Diễn viên / Thể loại / Liên quan |
| `/sxyprn?rel=` | Video liên quan (parse block sau `Related by:`), `total_pages=1` |
| `/sxyprn/vidosik?uri=` | `{ qualitys: { MP4 }, recomends }` |

Menu: `Sắp xếp: …` (chỉ listing tag/All — site không sort ở top/viewed/search), `Mới nhất`,
`Thịnh hành`, `Xem nhiều`, `Thể loại` (60 tag lấy từ trang chủ, cache 12h).

## Ghi chú

- Item "EXTERNAL LINK" (post chỉ có link ngoài, không host video) bị loại ngay ở parser vì
  không có `duration_small` dạng `HH:MM:SS`.
- Trang chủ (`/`) chỉ có ~100 post và **không phân trang** (`?p=`, `?page=` đều trả cùng pool)
  → dùng `/All.html` làm listing chính.
- `streamproxy = false`, trả URL MP4 tuyệt đối.

## Verify (2026-11-27)

- `/sxyprn` 30 item `tp=297` · `pg=2` item khác · `sort=views` khác thứ tự · `c=tag:Hardcore` 30 `tp=3799` · `pg=2` khác · `search=blake` 30 · `c=top` 30 `tp=1`
- `genres` 3 nhóm (Diễn viên 23 · Thể loại 54 · Liên quan 1); `?rel=` 6 item `tp=1`
- `popup.sh sxyprn` **PASS**
- Stream: `ffprobe` `h264 1280x720 + aac`, duration 7307 s; `ffmpeg -ss 0` và `-ss 7200` decode 3 s exit 0
- `check.sh` 0 error
