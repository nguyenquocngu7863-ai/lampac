using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace MissAV;

public class MissAVController : BaseSisiController
{
    public MissAVController() : base(ModInit.conf) { }

    // missav.live la AlpineJS CSR: noi dung grid chi co sau khi Recombee
    // tra ve. Khong the lay bang HTTP tho (HTML tho chi co <template x-for>).
    // => van phai dung trinh duyet, nhung dung CHUNG mot context de tiet RAM:
    //    keepopen:true giu cookie/session (cung user_uuid nen cung goi y),
    //    moi request chi tao them 1 page va dong lai ngay khi lay xong.
    // Serialize cac lan render trinh duyet trong cung 1 context.
    // Neu nhieu request chay song song, chung se tao nhieu page -> RAM nhoe.
    static SemaphoreSlim _pageFetchLock = new SemaphoreSlim(1, 1);

    // Lock RIENG cho fetch taxonomy menu (genres 40 trang + makers 71
    // trang, cold load vai phut). Dung chung _pageFetchLock thi request
    // xem phim doi lock 30s -> fail trong luc menu dang load.
    static SemaphoreSlim _taxLock = new SemaphoreSlim(1, 1);

    async Task<(string content, string tilesJson)> PageFetchAsync(string url)
    {
        IPage page = null;
        var acquired = await _pageFetchLock.WaitAsync(TimeSpan.FromSeconds(30));
        if (!acquired)
            return (null, null);
        try
        {
            // Render bang Chrome (chromium) nhu ban goc.
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                page = await browser.NewPageAsync("MissAV", new Dictionary<string, string>
                {
                    ["User-Agent"] = MissAVTo.ChromeUA,
                    ["Referer"] = "https://missav.live/"
                }, keepopen: true);

                if (page == null)
                    return (null, null);

                var resp = await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = 20000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });

                if (resp == null || !resp.Ok)
                    return (null, null);

                string content = await page.ContentAsync();

                // Alpine hydrate tung hang recommendItems rieng tung dot: duration len truoc, title len sau.
                // Poll nhanh (700ms) cho den khi ca tile-co-title lan tile-co-href-that
                // deu on dinh 3 lan lien tiep, toi da ~10s.
                try
                {
                    int lastT = -1, lastL = -1, stable = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        int v = 0;
                        try
                        {
                            v = await page.EvaluateAsync<int>(@"() => {
                                let titled = 0, linked = 0;
                                document.querySelectorAll('.thumbnail.group').forEach(d => {
                                    const a = d.querySelector('a[href]');
                                    const href = a ? (a.getAttribute('href') || '') : '';
                                    if (href.length > 1 && href !== '#' && href.indexOf('itemUrl') < 0 && href.indexOf('javascript') < 0) linked++;
                                    const lines = (d.innerText || '').trim().split('\n').map(s => s.trim()).filter(s => s.length > 0);
                                    const body = lines.filter(s => !/^\d{1,3}:\d{2}(:\d{2})?$/.test(s));
                                    if (body.length > 0 && body[body.length - 1].length > 3) titled++;
                                });
                                return titled * 100000 + linked;
                            }");
                        }
                        catch { break; }
                        int t = v / 100000, l = v % 100000;
                        if ((t > 0 || l > 0) && t == lastT && l == lastL && ++stable >= 3)
                            break;
                        if (t != lastT || l != lastL)
                            stable = 0;
                        lastT = t; lastL = l;
                        await Task.Delay(700);
                    }
                }
                catch { }

                string tiles = null;
                try
                {
                    tiles = await page.EvaluateAsync<string>(@"() => JSON.stringify(Array.from(document.querySelectorAll('.thumbnail.group')).map(d => {
                        const a = d.querySelector('a[href]');
                        const img = d.querySelector('img');
                        let p = '';
                        if (img) {
                            const ds = img.getAttribute('data-src') || '';
                            // lozad lazy-load: img.src chi la placeholder 1px -> uu tien data-src
                            if (ds && !ds.startsWith('data:') && ds !== 'javascript:;') p = ds;
                            else if (img.src && !img.src.startsWith('data:')) p = img.src;
                        }
                        const lines = (d.innerText || '').trim().split('\n').map(s => s.trim()).filter(s => s.length > 0);
                        // Tile co HAI alt: <a alt=ma-phim> va <img alt=ten-day-du>.
                        // Phai lay alt cua img; lay alt cua a se ra ma phim.
                        const ia = img ? (img.getAttribute('alt') || '') : '';
                        return { u: a ? a.getAttribute('href') : '', t: lines.join('\n'), p: p, a: ia, c: a ? (a.getAttribute('alt') || '') : '' };
                    }))");
                }
                catch { }

                return (content, tiles);
            }
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            try { await page?.CloseAsync(); } catch { }
            _pageFetchLock.Release();
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("missav")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // gộp sort về đúng tập của site (46 giá trị thử, chỉ views/released_at
        // là thật) — giá trị vào luôn cache key, xem MissAVTo.NormalizeSort
        sort = MissAVTo.NormalizeSort(sort);

        async Task<CacheResult<List<PlaylistItem>>> GetPageAsync(string search, string c, int page, bool allowRefresh)
        {
            return await InvokeCacheResult(ipkey($"missav:{search}:{c}:{sort}:{page}"), 10, jsonContext.ListPlaylistItem, async e =>
            {
                List<PlaylistItem> playlists = null;

                string pageUrl = MissAVTo.Uri(init.host, search, c, page, sort);
                var (_, tilesJson) = await PageFetchAsync(pageUrl);
                if (!string.IsNullOrEmpty(tilesJson))
                    playlists = MissAVTo.Playlist("missav/vidosik", tilesJson);

                if (playlists == null || playlists.Count == 0)
                    return e.Fail("playlists", refresh_proxy: allowRefresh && string.IsNullOrEmpty(search));

                return e.Success(playlists);
            });
        }

        var cache = await GetPageAsync(search, c, pg, true);

        // lam nong truoc trang ke de lật trang nhanh (ngam, khong chan response)
        if (cache.IsSuccess)
        {
            string nsearch = search, nc = c;
            int npg = pg + 1;
            _ = Task.Run(async () =>
            {
                try { await GetPageAsync(nsearch, nc, npg, false); }
                catch { }
            });
        }

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync(search, sort, c));
    }

    // menu = head (phụ thuộc search/sort/c, dựng lại mỗi request — rất rẻ)
    //        + base (taxonomy ~nghìn mục, cache 1 lần, không phụ thuộc context)
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var menu = MissAVTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    // Menu doc tu DISK cache (MissAVTaxCache): lan dau chua co file thi
    // fetch blocking top 12 trang cho nhanh; job nen lum not trang
    // thieu sau do. Disk giu qua restart nen restart xong menu du ngay,
    // khong can browser. Du lieu disk deu la trang tai thanh cong nen
    // cu phuc vu (thieu ben nao thi ben do hien fallback tinh tam).
    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("missav:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        try
        {
            var (g, s) = MissAVTaxCache.LoadMerged();

            if (g.Count == 0 && s.Count == 0)
            {
                var t1 = TaxPagesAsync("/en/genres", 12);
                var t2 = TaxPagesAsync("/en/makers", 12);
                await Task.WhenAll(t1, t2);
                var pg = await t1;
                var ps = await t2;
                for (int i = 0; i < pg.Count; i++)
                    MissAVTaxCache.StorePage("genres", i + 1, pg[i]);
                for (int i = 0; i < ps.Count; i++)
                    MissAVTaxCache.StorePage("makers", i + 1, ps[i]);
                (g, s) = MissAVTaxCache.LoadMerged();
            }

            if (g.Count > 0 || s.Count > 0)
            {
                var menu = MissAVTo.Menu(hostLocal, g, s);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                FillMissingBackground(memKey, hostLocal);
                return menu;
            }
        }
        catch { }

        return MissAVTo.Menu(hostLocal, null, null);
    }

    // Job nen: 1 tab duy nhat, tuan tu tung trang + delay 2.5s (gia nguoi
    // duyet, tranh CF chan fetch don). Trang tai loi/chan thi BO QUA (khong
    // luu) de lan sau thu lai; trang tai duoc ma 0 muc -> qua trang cuoi,
    // danh dau end. Xong thi dung menu moi vao memory cache.
    static int _fillRunning = 0;

    void FillMissingBackground(string memKey, string hostLocal)
    {
        if (Interlocked.Exchange(ref _fillRunning, 1) == 1)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                var headers = new Dictionary<string, string>
                {
                    ["User-Agent"] = MissAVTo.ChromeUA,
                    ["Referer"] = "https://missav.live/"
                };
                using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
                {
                    IPage page = null;
                    try
                    {
                        page = await browser.NewPageAsync("MissAV", headers, keepopen: false);
                        if (page != null)
                        {
                            await FillKindAsync(page, "genres", "/en/genres", 50);
                            await FillKindAsync(page, "makers", "/en/makers", 80);
                        }
                    }
                    finally
                    {
                        try { if (page != null) await page.CloseAsync(); } catch { }
                    }
                }
                try
                {
                    var (g, s) = MissAVTaxCache.LoadMerged();
                    if (g.Count > 0 || s.Count > 0)
                        hybridCache.Set(memKey, MissAVTo.Menu(hostLocal, g, s), cacheTime(720), true);
                }
                catch { }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _fillRunning, 0);
            }
        });
    }

    async Task FillKindAsync(IPage page, string kind, string basePath, int max)
    {
        int end = MissAVTaxCache.End(kind);
        for (int n = 1; n <= max && n < end; n++)
        {
            if (MissAVTaxCache.HasPage(kind, n))
                continue;
            try
            {
                string url = MissAVTo.SiteHost + basePath + (n == 1 ? "" : "?page=" + n);
                var r = await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = 25000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
                if (r == null || !r.Ok)
                    continue;
                string html = await page.ContentAsync();
                if (string.IsNullOrEmpty(html) || html.IndexOf("text-nord13") < 0)
                    continue;
                var items = MissAVTo.Taxonomies(html);
                if (items.Count == 0)
                {
                    MissAVTaxCache.SetEnd(kind, n);
                    break;
                }
                MissAVTaxCache.StorePage(kind, n, items);
            }
            catch { }
            try { await Task.Delay(2500); } catch { }
        }
    }

    // maxPages: SO TRANG FETCH (tran du room: genres 40 + makers 71).
    // Tra ve theo tung trang (giu thu tu) de disk cache luu rieng.
    async Task<List<List<(string name, string url)>>> TaxPagesAsync(
        string page, int maxPages)
    {
        var per = new List<List<(string, string)>>();
        foreach (var html in await TaxPagesHtmlAsync(page, maxPages))
            per.Add(MissAVTo.Taxonomies(html));
        return per;
    }

    // missav chan TLS fingerprint cua .NET/curl (SSL_connect closed) ->
    // taxonomy phai lay bang real Chrome. Reuse 1 browser, 4 tab chay song
    // song de lan dau khong qua lau; dung chung semaphore voi PageFetchAsync.
    async Task<List<string>> TaxPagesHtmlAsync(string page, int maxPages)
    {
        var htmls = new List<string>();
        if (maxPages < 1)
            maxPages = 1;

        // Cho toi 5 phut: 111 trang cold load lau, lock chung se doi nhau.
        var acquired = await _taxLock.WaitAsync(TimeSpan.FromMinutes(5));
        if (!acquired)
            return htmls;

        var headers = new Dictionary<string, string>
        {
            ["User-Agent"] = MissAVTo.ChromeUA,
            ["Referer"] = "https://missav.live/"
        };

        try
        {
            // Render bang Chrome (chromium) — xem ghi chu o PageFetchAsync.
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                // trang 1 truoc de biet tong so trang
                string first = null;
                IPage p1 = await browser.NewPageAsync("MissAV", headers, keepopen: true);
                if (p1 == null)
                    return htmls;

                try
                {
                    var r1 = await p1.GotoAsync(MissAVTo.SiteHost + page,
                        new PageGotoOptions
                        {
                            Timeout = 20000,
                            WaitUntil = WaitUntilState.DOMContentLoaded
                        });
                    if (r1 != null && r1.Ok)
                        first = await p1.ContentAsync();
                }
                finally
                {
                    try { await p1.CloseAsync(); } catch { }
                }

                if (string.IsNullOrEmpty(first))
                    return htmls;

                htmls.Add(first);

                // Lay thang maxPages: trang 1 khong co link trang cuoi
                // (pagination rut gon) nen TaxPages parse thieu (8-9).
                int total = maxPages;
                if (total <= 1)
                    return htmls;

                // Tao tab truoc (tuan tu) roi moi cho chay song song — tranh
                // goi NewPageAsync dong thoi tren cung browser.
                int workers = Math.Min(4, total - 1);
                var pool = new List<IPage>(workers);
                for (int i = 0; i < workers; i++)
                {
                    IPage wp = await browser.NewPageAsync("MissAV", headers, keepopen: true);
                    if (wp != null)
                        pool.Add(wp);
                }

                if (pool.Count == 0)
                    return htmls;

                var result = new string[total + 1];
                int next = 2;
                var gate = new object();

                async Task Worker(IPage wp)
                {
                    while (true)
                    {
                        int n;
                        lock (gate) { n = next++; }
                        if (n > total)
                            break;

                        try
                        {
                            var r = await wp.GotoAsync(
                                MissAVTo.SiteHost + page + "?page=" + n,
                                new PageGotoOptions
                                {
                                    Timeout = 20000,
                                    WaitUntil = WaitUntilState.DOMContentLoaded
                                });
                            if (r != null && r.Ok)
                            {
                                string h = await wp.ContentAsync();
                                if (!string.IsNullOrEmpty(h))
                                    result[n] = h;
                            }
                        }
                        catch { }
                    }
                }

                try
                {
                    await Task.WhenAll(pool.Select(wp => Worker(wp)));
                }
                finally
                {
                    foreach (var wp in pool)
                    {
                        try { await wp.CloseAsync(); } catch { }
                    }
                }

                for (int n = 2; n <= total; n++)
                    if (!string.IsNullOrEmpty(result[n]))
                        htmls.Add(result[n]);
            }
        }
        catch { }
        finally
        {
            _taxLock.Release();
        }

        return htmls;
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"missav:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        int fi = pageUrl.IndexOf('#');
        if (fi >= 0)
            pageUrl = pageUrl.Substring(0, fi);
        if (pageUrl.StartsWith("/"))
            pageUrl = "https://missav.live" + pageUrl;

        var links = new Dictionary<string, string>();

        var (content, _) = await PageFetchAsync(pageUrl);
        if (!string.IsNullOrEmpty(content))
        {
            var urls = MissAVTo.StreamUrls(content);
            foreach (var u in urls)
            {
                if (!MissAVTo.IsStreamHost(u))
                    continue;
                string label = u.Contains("playlist.m3u8") ? "Master" :
                    Regex.Match(u, @"/(\d{3,4}p)/").Groups[1].Value;
                if (string.IsNullOrEmpty(label))
                    label = "HLS";
                string key = label;
                int n = 2;
                while (links.ContainsKey(key))
                    key = label + " " + (n++);
                if (!links.ContainsValue(u))
                    links.TryAdd(key, u);
            }
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("missav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/missav/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("missav/video")]
    [Route("missav/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        // Master phai qua /proxy/ (curl override tai surrit 200,
        // rewrite variant/segment ve /proxy/, segment passthrough bytes)
        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", MissAVTo.ChromeUA),
            ("Referer", "https://missav.live/"),
            ("Origin", "https://missav.live")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }

    [HttpGet]
    [Route("missav/strem")]
    async public Task<ActionResult> Strem(string link, string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", MissAVTo.ChromeUA),
            ("Referer", "https://missav.live/"),
            ("Origin", "https://missav.live")
        ));

        if (!string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(q))
        {
            var links = await ResolveLinksAsync(uri);
            if (links == null || !links.TryGetValue(q, out link) || string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        return Redirect(HostStreamProxy(link, headers));
    }
}
