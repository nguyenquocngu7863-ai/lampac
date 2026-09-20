# SexDep (x.sexdep.co.uk, motchill)

Module Adult route `sexdep`, displayindex 15.

## URLs
- List home: `https://x.sexdep.co.uk/` (parse `a.m-block.movie-item[href=/phim/][title]` + `div.lazyload[data-original]`)
- Category: `https://x.sexdep.co.uk/{the-loai/...|quoc-gia/...}?page=N`
- Search: `https://x.sexdep.co.uk/?search={kw}&page=N` (form GET `name=search`, KHONG phai `/search/slug`)
- Video: `https://x.sexdep.co.uk/phim/{slug}`

## Player
- Trang video chua `a.server[data-link="/storage/m3u8/{slug}/index.m3u8"]` (relative, phai kem host).
- Poster nam trong `div.lazyload[data-original="/storage/..."]` (relative, phai kem host; co ca `.avif`).

## Pagination
- Moi tuyen dung `?page=N` (`&page=N` khi da co query search).

## Cache & Build
- List cache 10p, resolve cache 20p (hybridCache).
- Copy `.cs` vao `/root/lampac/module/Adult/SexDep/` + restart, khong can `dotnet build`.
- Verify: `/sexdep?pg=1` co picture absolute, `?c=quoc-gia/viet-nam` dung list, `sexdep/vidosik?uri=` ra link, `sexdep/video.m3u8` 302 ve `/proxy/`.
