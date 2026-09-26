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

    async Task<(string content, string tilesJson)> PageFetchAsync(string url)
    {
        IPage page = null;
        var acquired = await _pageFetchLock.WaitAsync(TimeSpan.FromSeconds(30));
        if (!acquired)
            return (null, null);
        try
        {
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
                        return { u: a ? a.getAttribute('href') : '', t: lines.join('\n'), p: p, a: a ? (a.getAttribute('alt') || '') : '' };
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
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        async Task<CacheResult<List<PlaylistItem>>> GetPageAsync(string search, string c, int page, bool allowRefresh)
        {
            return await InvokeCacheResult(ipkey($"missav:{search}:{c}:{page}"), 10, jsonContext.ListPlaylistItem, async e =>
            {
                List<PlaylistItem> playlists = null;

                string pageUrl = MissAVTo.Uri(init.host, search, c, page);
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

        return PlaylistResult(cache, MissAVTo.Menu(host));
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
