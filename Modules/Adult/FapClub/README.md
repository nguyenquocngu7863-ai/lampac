# FapClub — https://xxx.fapclub.vip

Module Adult cho site FapClub (tube phương Tây). HTML thuần, không Cloudflare, không CSR.

## Routes
- `/fapclub` — home (30 item/trang)
- `/fapclub?pg=N` — `/latest-updates/N/`
- `/fapclub?c=<slug>` — `/categories/<slug>/[N/]` (50 category, lấy menu từ home)
- `/fapclub?search=<kw>` — `/search/?q=<kw>`, trang `/search/N/?q=<kw>`
- `/fapclub/menu` — menu category (2 nhóm × 25)
- `/fapclub/vidosik?uri=/videos/<id>/<slug>/` — stream

## Player (khó nhất)
HTML không chứa URL stream. Phải giải mã JS `hplayer1/jwplayer.js` + `jss/KernelTeamp.js`
(obfuscated array-rotation) để lấy công thức:

```
https://{data-n}.vstor.top/whpvid/{ts}/{token}/{folder}/{id}/{id}_{quality}.mp4
folder = floor(id/1000)*1000
```

`data-n`, `data-id`, `data-q` lấy từ `<div id="player">`. `data-q` là danh sách
`quality;hash;label;size;ts;token` phân bằng dấu phẩy — mỗi quality một token riêng.
Sonnet 4.5 bó tay chỗ này (không suy ra được từ HTML); giải bằng deobfuscate offline.

## Card
`name`/`time` có thật từ HTML (`.time` = 34:57). `quality` site không ghi badge → null.
Thumbnail `i.fapclub.vip` có rotation (onmouseover) nhưng dùng `src` tĩnh là đủ.

## Verify 2026-11-22
- check.sh: 0 error (17 warning)
- smoke 5 endpoint: 200, 30 item mỗi route, first-item khác nhau
- vidosik: 4 quality 1080p/720p/480p/240p + proxy
- `play.sh`: **PASS** — h264 1920x1080, 1823.9s, decode 5s đầu/cuối, cold/warm 0.39/0.37s

## Chưa làm
- popup `/fapclub/genres?uri=` + `?rel=` (theo `todo.md`)
