# HeoVl (heovl.im)

Module Adult route `heovl`, displayindex 14.

## URLs
- List home: `https://heovl.im/` (parse `a[href=/videos/][title]` + `img`)
- Category: `https://heovl.im/categories/{slug}?page=N`
- Search: `https://heovl.im/search/{slug}?page=N` (slug lowercase dash)
- Video: `https://heovl.im/videos/{slug}`

## Player
- Trang video chứa `button.set-player-source` với `data-cdn-name` + `data-source="https://{embed}/videos/{id}/play?..."`.
- Resolve: `POST {embedHost}/videos/{id}/config` (JSON `{}`), parse `"file":"...m3u8/mp4"`.
- Embed đã thấy: `e.streamforester.name`, `vcast.name`.

## Pagination
- Category/search dùng `?page=N`.
- Home pg>1 fallback `categories/viet-nam?page=N`.

## Cache & Build
- List cache 10p, resolve cache 20p (hybridCache).
- Copy `.cs` vào `/root/lampac/module/Adult/HeoVl/` + restart, không cần `dotnet build`.
- Verify: `/heovl?pg=1` có list, `heovl/vidosik?uri=` ra link, `heovl/video.m3u8` 302 về `/proxy/`.
