using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SupJav;

public class SupJavController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 25000;
    public SupJavController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("supjav")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;
        var menuTask = MenuAsync();

        var cache = await InvokeCacheResult(ipkey($"supjav:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(SupJavTo.Uri(init.host, search, c, pg));
            var playlists = SupJavTo.Playlist("supjav/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search))
                    return e.Success(new List<PlaylistItem>());
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await menuTask, total_pages: 0);
    }

    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("supjav:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return SupJavTo.Menu(hostLocal, null, null, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return SupJavTo.Menu(hostLocal, null, null, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    // /tag phan 291 muc, /maker 808 muc theo /page/N/ (trang 1 khong lo link).
    // Crawl song song den khi het (toi da 12/4 trang), gop dedup.
    async Task<List<(string slug, string name)>> TagsAsync()
    {
        string memKey = ipkey("supjav:tax:tags");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) && hit != null && hit.Count > 0)
            return hit;
        var tasks = new List<Task<string>>(5);
        tasks.Add(GetPageAsync($"{SupJavTo.SiteHost}/tag"));
        for (int p = 2; p <= 5; p++)
            tasks.Add(GetPageAsync($"{SupJavTo.SiteHost}/tag/page/{p}/"));
        await Task.WhenAll(tasks);
        for (int i = 0; i < tasks.Count; i++)
        {
            string html = await tasks[i];
            if (string.IsNullOrEmpty(html))
            {
                await Task.Delay(3000);
                tasks[i] = GetPageAsync(i == 0 ? $"{SupJavTo.SiteHost}/tag" : $"{SupJavTo.SiteHost}/tag/page/{i + 1}/");
            }
        }
        await Task.WhenAll(tasks);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(string slug, string name)>();
        foreach (var t in tasks)
        {
            string html = await t;
            if (string.IsNullOrEmpty(html)) continue;
            foreach (var it in SupJavTo.Tags(html))
                if (seen.Add(it.slug)) all.Add(it);
        }
        all.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        if (all.Count == 0) return all;
        hybridCache.Set(memKey, all, cacheTime(720), true);
        return all;
    }

    async Task<List<(string slug, string name)>> MakersAsync()
    {
        string memKey = ipkey("supjav:tax:makers");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit2) && hit2 != null && hit2.Count > 0)
            return hit2;
        var tasks = new List<Task<string>>(12);
        tasks.Add(GetPageAsync($"{SupJavTo.SiteHost}/maker?sort=quantity"));
        for (int p = 2; p <= 12; p++)
            tasks.Add(GetPageAsync($"{SupJavTo.SiteHost}/maker/page/{p}/?sort=quantity"));
        await Task.WhenAll(tasks);
        // CF challenge theo dot: trang ve rong thi doi 3s thu lai 1 lan
        for (int i = 0; i < tasks.Count; i++)
        {
            string html = await tasks[i];
            if (string.IsNullOrEmpty(html))
            {
                await Task.Delay(3000);
                tasks[i] = GetPageAsync(i == 0 ? $"{SupJavTo.SiteHost}/maker?sort=quantity" : $"{SupJavTo.SiteHost}/maker/page/{i + 1}/?sort=quantity");
            }
        }
        await Task.WhenAll(tasks);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(string slug, string name)>();
        foreach (var t in tasks)
        {
            string html = await t;
            if (string.IsNullOrEmpty(html)) continue;
            foreach (var it in SupJavTo.Makers(html))
                if (seen.Add(it.slug)) all.Add(it);
        }
        // GIU nguyen thu tu site (sort=quantity: nhieu phim len truoc), khong sort A-Z
        if (all.Count == 0) return all;
        hybridCache.Set(memKey, all, cacheTime(720), true);
        return all;
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        long tr = Environment.TickCount64;
        try
        {
            var homeTask = GetPageAsync(SupJavTo.SiteHost + "/");
            var makerTask = MakersAsync();
            var tagTask = TagsAsync();
            await Task.WhenAll(homeTask, makerTask, tagTask);
            var cats = SupJavTo.Taxonomies(await homeTask ?? "");
            var makers = await makerTask;
            var tags = await tagTask;
            if (cats.Count > 0 || makers.Count > 0 || tags.Count > 0)
            {
                var menu = SupJavTo.Menu(hostLocal, cats, makers, tags);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine($"SupJav: menu cats={cats.Count} makers={makers.Count} tags={tags.Count} ({Environment.TickCount64 - tr}ms)");
                return menu;
            }
        }
        catch { }
        Console.WriteLine($"SupJav: menu fail ({Environment.TickCount64 - tr}ms)");
        return SupJavTo.Menu(hostLocal, null, null, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 2) break;
            string page = await RaceGetAsync(url, (int)Math.Ceiling(Math.Min(6, left)));
            if (!string.IsNullOrEmpty(page) && page.Length >= 5000 && !page.Contains("Just a moment"))
                return page;
            await Task.Delay(1500);
        }
        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;
        var headers = HeadersModel.Init(
            ("User-Agent", SupJavTo.ChromeUA),
            ("Referer", SupJavTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        var t1 = Task.Run(async () =>
        {
            try { await httpHydra.GetSpan(url, span => { a = span.ToString(); }, addheaders: headers); }
            catch { }
        });
        var t2 = Task.Run(async () =>
        {
            try
            {
                b = await Http.Get(url, timeoutSeconds: Math.Max(8, seconds), httpversion: init.httpversion, proxy: proxy, headers: headers);
            }
            catch { }
        });
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrEmpty(a) && !a.Contains("Just a moment")) return a;
            if (!string.IsNullOrEmpty(b) && !b.Contains("Just a moment")) return b;
            if (t1.IsCompleted && t2.IsCompleted) break;
            await Task.Delay(300);
        }
        if (!string.IsNullOrEmpty(a) && !a.Contains("Just a moment")) return a;
        return !string.IsNullOrEmpty(b) && !b.Contains("Just a moment") ? b : null;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("supjav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        return Json(links.ToDictionary(k => k.Key, k =>
        {
            string v = k.Value;
            string link = v.Contains('\n') ? v.Substring(0, v.IndexOf('\n')) : v;
            // chrome: = VOE/VAS resolve that o /video, optimistic HLS
            string route = link.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase) || link.Contains(".m3u8") || link.Contains("/hls/") || link.Contains("master.txt") ? "video.m3u8" : "video.mp4";
            return $"{host}/supjav/{route}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}";
        }));
    }

    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : SupJavTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"supjav:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) && cache != null && cache.Count > 0)
            return cache;
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page)) return null;
        var servers = SupJavTo.Servers(page);
        if (servers.Count == 0) return null;
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, link) in servers)
        {
            // VOE (localStorage redirect) + VAS (pako/crypto): curl khong ra link,
            // giu nhan de /video resolve that bang Chrome (ton 4-12s). optimistic .m3u8.
            if (SupJavTo.IsVoeLabel(label))
            {
                string final = SupJavTo.FinalUrl(link);
                if (!string.IsNullOrEmpty(final) && !links.ContainsKey(label))
                    links.TryAdd(label, "chrome:" + final + "\n" + pageUrl);
                continue;
            }
            string packed = await ResolveServerAsync(pageUrl, label, link);
            if (!string.IsNullOrEmpty(packed) && !links.ContainsKey(label))
                links.TryAdd(label, packed);
        }
        if (links.Count == 0) return null;
        hybridCache.Set(memKey, links, cacheTime(10));
        return links;
    }

    async Task<string> ResolveServerAsync(string pageUrl, string label, string link)
    {
        // Gateway: supjav.php?l=<link> -> ?c=<reversed>
        string final = SupJavTo.FinalUrl(link);
        if (string.IsNullOrEmpty(final)) return null;
        string gw = await SupJavTo.GetHtmlAsync(final, pageUrl, 25, proxy, init.httpversion);
        if (string.IsNullOrEmpty(gw)) return null;
        // FST (StreamHg): unpack -> hls3>hls2
        var masters = SupJavTo.StreamHgMasters(gw);
        if (masters.Count > 0)
            return masters[0] + "\n" + final;
        // ST (StreamTape): /e/ID -> #robotlink mp4
        string stId = SupJavTo.StreamTapeId(gw);
        if (string.IsNullOrEmpty(stId))
        {
            var m = System.Text.RegularExpressions.Regex.Match(gw, @"streamtape\.com/e/([A-Za-z0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) stId = m.Groups[1].Value;
        }
        if (!string.IsNullOrEmpty(stId))
        {
            string embed = await SupJavTo.GetHtmlAsync("https://streamtape.com/e/" + stId, pageUrl, 20, proxy, init.httpversion);
            string mp4 = SupJavTo.StreamTapeMp4(embed ?? "");
            if (!string.IsNullOrEmpty(mp4))
                return mp4 + "\nhttps://streamtape.com/";
        }
        return null;
    }

    [HttpGet]
    [Route("supjav/video")]
    [Route("supjav/video.m3u8")]
    [Route("supjav/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue(q, out string packed) || string.IsNullOrEmpty(packed))
            return OnError("stream_links", refresh_proxy: true);
        string link = packed, referer = SupJavTo.SiteHost + "/";
        int nl = packed.IndexOf('\n');
        if (nl > 0) { link = packed.Substring(0, nl); referer = packed.Substring(nl + 1); }
        // nhan chrome: resolve that bang Playwright roi redirect
        if (link.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase))
        {
            string final = link.Substring(7);
            string voe = await SupJavTo.VoeSourceAsync(final, referer, 15000);
            if (string.IsNullOrEmpty(voe))
                return OnError("stream_links", refresh_proxy: true);
            var h2 = httpHeaders(init, HeadersModel.Init(("referer", referer)));
            return Redirect(HostStreamProxy(voe, h2));
        }
        var direct = httpHeaders(init, HeadersModel.Init(("referer", referer)));
        return Redirect(HostStreamProxy(link, direct));
    }
}
