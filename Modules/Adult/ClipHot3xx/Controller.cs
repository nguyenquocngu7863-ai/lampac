using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace ClipHot3xx;

public class ClipHot3xxController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 25000;
    public ClipHot3xxController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("cliphot3xx")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        // No sort handling for now

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"cliphot3xx:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(ClipHot3xxTo.Uri(init.host, search, c, sort, pg));
            var playlists = ClipHot3xxTo.Playlist("cliphot3xx/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, ClipHot3xxTo.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await menuTask,
            total_pages: cache.Value.total_pages
        );
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var menu = ClipHot3xxTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("cliphot3xx:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return ClipHot3xxTo.Menu(hostLocal, null, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return ClipHot3xxTo.Menu(hostLocal, null, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<(string slug, string name)>> TagsAsync()
    {
        string memKey = ipkey("cliphot3xx:tax:tags");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) && hit != null && hit.Count > 0)
            return hit;
        var tasks = new List<Task<string>>(5);
        tasks.Add(GetPageAsync($"{ClipHot3xxTo.SiteHost}/"));
        for (int p = 2; p <= 5; p++)
            tasks.Add(GetPageAsync($"{ClipHot3xxTo.SiteHost}/?page={p}"));
        await Task.WhenAll(tasks);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(string slug, string name)>();
        foreach (var t in tasks)
        {
            string html = await t;
            if (string.IsNullOrEmpty(html)) continue;
            foreach (var it in ClipHot3xxTo.Tags(html))
                if (seen.Add(it.slug)) all.Add(it);
        }
        if (all.Count == 0) return all;
        hybridCache.Set(memKey, all, cacheTime(720), true);
        return all;
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        long tr = Environment.TickCount64;
        try
        {
            var homeTask = GetPageAsync(ClipHot3xxTo.SiteHost + "/");
            var tagTask = TagsAsync();
            await Task.WhenAll(homeTask, tagTask);
            var cats = ClipHot3xxTo.Categories(await homeTask ?? "");
            var tags = await tagTask;
            if (cats.Count > 0 || tags.Count > 0)
            {
                var menu = ClipHot3xxTo.Menu(hostLocal, cats, tags);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine($"ClipHot3xx: menu cats={cats.Count} tags={tags.Count} ({Environment.TickCount64 - tr}ms)");
                return menu;
            }
        }
        catch { }
        Console.WriteLine($"ClipHot3xx: menu fail ({Environment.TickCount64 - tr}ms)");
        return ClipHot3xxTo.Menu(hostLocal, null, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", ClipHot3xxTo.ChromeUA),
            ("Referer", ClipHot3xxTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                string page = await Http.Get(url, timeoutSeconds: 12, headers: headers);
                if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                    return page;
            }
            catch { }
            try
            {
                string page = await Http.Get(url, timeoutSeconds: 12, httpversion: init.httpversion, proxy: proxy, headers: headers);
                if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                    return page;
            }
            catch { }
            await Task.Delay(1200);
        }
        return null;
    }

    // ========== VIDOSIK: resolve blogger token -> googlevideo URLs ==========
    [HttpGet, Staticache(manually: true)]
    [Route("cliphot3xx/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (string.IsNullOrWhiteSpace(uri))
            return OnError("stream_links", refresh_proxy: true);

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in links)
            dict[kv.Key] = $"{host}/cliphot3xx/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(kv.Key)}";
        return Json(dict);
    }

    // ========== VIDEO: proxy googlevideo URL hoặc HLS m3u8 ==========
    [HttpGet]
    [Route("cliphot3xx/video")]
    [Route("cliphot3xx/video.m3u8")]
    [Route("cliphot3xx/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || string.IsNullOrEmpty(q) || !links.TryGetValue(q, out string streamUrl))
            return OnError("stream_links", refresh_proxy: true);

        // HLS stream -> referer la site
        if (q == "HLS" || streamUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            var h2 = httpHeaders(init, HeadersModel.Init(
                ("user-agent", ClipHot3xxTo.ChromeUA),
                ("referer", ClipHot3xxTo.SiteHost + "/")));
            return Redirect(HostStreamProxy(streamUrl, h2));
        }

        // GoogleVideo stream -> referer la blogger.com
        var h = httpHeaders(init, HeadersModel.Init(
            ("user-agent", ClipHot3xxTo.ChromeUA),
            ("referer", "https://www.blogger.com/"),
            ("origin", "https://www.blogger.com")));
        return Redirect(HostStreamProxy(streamUrl, h));
    }

    // ========== Resolve: blogger token -> batchexecute -> googlevideo
    // HOẶC player page -> HLS m3u8 ==========
    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : ClipHot3xxTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"cliphot3xx:stream:{pageUrl}");

        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache)
            && cache != null && cache.Count > 0)
            return cache;

        // Step 1: Fetch detail page -> extract embed URL
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page)) return null;

        string embed = ClipHot3xxTo.EmbedUrl(page);
        if (string.IsNullOrEmpty(embed)) return null;

        // TRƯỜNG HỢP 1: Blogger embed (blogger.com/video.g?token=...)
        if (embed.Contains("blogger.com/video.g", StringComparison.OrdinalIgnoreCase))
        {
            string token = ClipHot3xxTo.BloggerToken(embed);
            if (string.IsNullOrEmpty(token)) return null;

            var blogHeaders = HeadersModel.Init(
                ("User-Agent", ClipHot3xxTo.ChromeUA),
                ("Referer", "https://www.blogger.com/"),
                ("X-Same-Domain", "1"));

            string resp = null;
            // Không proxy trước
            try
            {
                resp = await Http.Post(
                    ClipHot3xxTo.BatchUrl,
                    ClipHot3xxTo.BatchBody(token),
                    headers: blogHeaders,
                    timeoutSeconds: 15,
                    httpversion: init.httpversion);
            }
            catch { }

            // Proxy Oracle
            if (string.IsNullOrEmpty(resp))
            {
                try
                {
                    resp = await Http.Post(
                        ClipHot3xxTo.BatchUrl,
                        ClipHot3xxTo.BatchBody(token),
                        headers: blogHeaders,
                        timeoutSeconds: 15,
                        httpversion: init.httpversion,
                        proxy: proxy);
                }
                catch { }
            }

            var links = ClipHot3xxTo.BloggerFormats(resp);
            if (links.Count == 0) return null;
            hybridCache.Set(memKey, links, cacheTime(5));
            return links;
        }

        // TRƯỜNG HỢP 2: Player page -> HLS m3u8
        if (embed.Contains("cliphot3xx.com/player/", StringComparison.OrdinalIgnoreCase))
        {
            string playerHtml = await GetPageAsync(embed);
            if (string.IsNullOrEmpty(playerHtml)) return null;

            string hls = ClipHot3xxTo.HlsUrl(playerHtml);
            if (string.IsNullOrEmpty(hls)) return null;

            var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HLS"] = hls
            };
            hybridCache.Set(memKey, links, cacheTime(10));
            return links;
        }

        return null;
    }
}
