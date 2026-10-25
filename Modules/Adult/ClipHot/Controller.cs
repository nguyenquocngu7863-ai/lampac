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

namespace ClipHot;

public class ClipHotController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 25000;
    public ClipHotController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("cliphot")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        sort = ClipHotTo.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"cliphot:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(ClipHotTo.Uri(init.host, search, c, sort, pg));
            var playlists = ClipHotTo.Playlist("cliphot/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, ClipHotTo.Pages(html)));
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
        var menu = ClipHotTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("cliphot:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return ClipHotTo.Menu(hostLocal, null, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return ClipHotTo.Menu(hostLocal, null, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<(string slug, string name)>> TagsAsync()
    {
        string memKey = ipkey("cliphot:tax:tags");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) && hit != null && hit.Count > 0)
            return hit;
        var tasks = new List<Task<string>>(5);
        tasks.Add(GetPageAsync($"{ClipHotTo.SiteHost}/"));
        for (int p = 2; p <= 5; p++)
            tasks.Add(GetPageAsync($"{ClipHotTo.SiteHost}/trang/{p}/"));
        await Task.WhenAll(tasks);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(string slug, string name)>();
        foreach (var t in tasks)
        {
            string html = await t;
            if (string.IsNullOrEmpty(html)) continue;
            foreach (var it in ClipHotTo.Tags(html))
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
            var homeTask = GetPageAsync(ClipHotTo.SiteHost + "/");
            var tagTask = TagsAsync();
            await Task.WhenAll(homeTask, tagTask);
            var cats = ClipHotTo.Categories(await homeTask ?? "");
            var tags = await tagTask;
            if (cats.Count > 0 || tags.Count > 0)
            {
                var menu = ClipHotTo.Menu(hostLocal, cats, tags);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine($"ClipHot: menu cats={cats.Count} tags={tags.Count} ({Environment.TickCount64 - tr}ms)");
                return menu;
            }
        }
        catch { }
        Console.WriteLine($"ClipHot: menu fail ({Environment.TickCount64 - tr}ms)");
        return ClipHotTo.Menu(hostLocal, null, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", ClipHotTo.ChromeUA),
            ("Referer", ClipHotTo.SiteHost + "/"),
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

    // ========== VIDOSIK: lấy videoId + serverId từ trang chi tiết ==========
    [HttpGet, Staticache(manually: true)]
    [Route("cliphot/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (string.IsNullOrWhiteSpace(uri))
            return OnError("stream_links", refresh_proxy: true);

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : ClipHotTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"cliphot:vidosik:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out string cached) && !string.IsNullOrEmpty(cached))
            return Json(JsonSerializer.Deserialize<Dictionary<string, string>>(cached));

        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return OnError("stream_links", refresh_proxy: true);

        string videoId = ClipHotTo.VideoId(page);
        string serverId = ClipHotTo.ServerId(page);
        if (string.IsNullOrEmpty(videoId))
            return OnError("stream_links", refresh_proxy: true);

        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Server 1"] = $"{host}/cliphot/video?uri={HttpUtility.UrlEncode(uri)}&vid={videoId}&sid={serverId}"
        };

        string json = JsonSerializer.Serialize(dict);
        hybridCache.Set(memKey, json, cacheTime(10));
        return Json(dict);
    }

    // ========== VIDEO: gọi API /api/player để lấy sources ==========
    [HttpGet]
    [Route("cliphot/video")]
    [Route("cliphot/video.m3u8")]
    [Route("cliphot/video.mp4")]
    async public Task<ActionResult> Video(string uri, string vid, string sid, string q = null, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        if (string.IsNullOrEmpty(vid))
            return OnError("stream_links", refresh_proxy: true);

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : ClipHotTo.SiteHost + "/" + (uri ?? "").Trim('/');
        string server = string.IsNullOrEmpty(sid) ? "1" : sid;

        string streamKey = ipkey($"cliphot:stream:{pageUrl}:{server}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            packed = await ResolvePlayerAsync(pageUrl, vid, server);
            if (string.IsNullOrEmpty(packed))
                return OnError("stream_links", refresh_proxy: true);
            hybridCache.Set(streamKey, packed, cacheTime(10));
        }

        string link = packed, referer = pageUrl;
        int nl = packed.IndexOf('\n');
        if (nl > 0) { link = packed.Substring(0, nl); referer = packed.Substring(nl + 1); }

        var h2 = httpHeaders(init, HeadersModel.Init(
            ("user-agent", ClipHotTo.ChromeUA),
            ("referer", referer)));
        return Redirect(HostStreamProxy(link, h2));
    }

    async Task<string> ResolvePlayerAsync(string pageUrl, string videoId, string serverId)
    {
        try
        {
            var content = new System.Net.Http.StringContent(
                $"id={System.Uri.EscapeDataString(videoId)}&server={System.Uri.EscapeDataString(serverId)}",
                System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

            var headers = HeadersModel.Init(
                ("User-Agent", ClipHotTo.ChromeUA),
                ("Referer", pageUrl),
                ("Origin", ClipHotTo.SiteHost),
                ("X-Requested-With", "XMLHttpRequest"),
                ("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8"));

            string json = await Http.Post(ClipHotTo.SiteHost + "/api/player",
                content, timeoutSeconds: 15, headers: headers,
                httpversion: init.httpversion, disposeData: true);

            if (string.IsNullOrEmpty(json)) return null;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("status", out var statusEl) || statusEl.GetString() != "success")
                return null;
            if (!doc.RootElement.TryGetProperty("data", out var dataEl)) return null;

            string type = dataEl.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            if (type == "iframe")
            {
                if (dataEl.TryGetProperty("source", out var srcEl))
                    return srcEl.GetString() + "\n" + pageUrl;
                return null;
            }

            if (type == "jwplayer")
            {
                if (!dataEl.TryGetProperty("sources", out var sourcesEl)) return null;
                foreach (var src in sourcesEl.EnumerateArray())
                {
                    string file = src.TryGetProperty("file", out var fileEl) ? fileEl.GetString() : null;
                    string typ = src.TryGetProperty("type", out var typEl) ? typEl.GetString() : null;
                    if (!string.IsNullOrEmpty(file) && file.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        // Trả về link m3u8 hoặc mp4 kèm referer
                        return file + "\n" + pageUrl;
                    }
                }
                return null;
            }

            return null;
        }
        catch { return null; }
    }
}
