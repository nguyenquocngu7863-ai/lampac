# Pornhat

Lampac Adult dynamic module for `https://www.pornhat.one`.

## Routes

- `/pornhat` — latest videos; `/pornhat?pg=2` — pagination.
- `/pornhat?c=popular` and `/pornhat?c=trending` — site lists.
- `/pornhat?search=milf` — search (`/search/milf/`).
- `/pornhat/vidosik?uri=<detail>` — selected MP4 source parsed from detail HTML.
- `/pornhat/video.m3u8?uri=<detail>&q=360p` — lazy resolves the HLS source through Lampac stream proxy (the site `<source>` URL is an MP4 path that actually serves an HLS master).
- `/pornhat/genres?uri=<detail>` — detail chips and one related-video entry.

## Verified site structure

Static server-side HTML. List cards use `.item.thumb-bl-video`, pagination paths `/2/`, `/search/<query>/2/`, `/popular/2/`; cards print duration in `.video-meta`. Detail pages include direct `<source>` URLs in `/get_file/` for MP4 qualities (sample: Auto/360p/480p/720p). No sort control was exposed in the site menu.

Menu and popup labels are localized in Vietnamese; taxonomy chip names remain the names supplied by the site.