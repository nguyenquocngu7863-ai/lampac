using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace KawaiJAV;

public class KawaiJAVController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 25000;
    public KawaiJAVController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("kawaijav")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        sort = KawaiJAVTo.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        string listUrl = KawaiJAVTo.Uri(init.host, search, c, sort, pg);
        Console.WriteLine($"KawaiJAV: Index url={listUrl} search={search} c={c} pg={pg}");

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"kawaijav:v1:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(KawaiJAVTo.Uri(init.host, search, c, sort, pg));
            Console.WriteLine($"KawaiJAV: Index fetch len={html?.Length ?? 0} url={listUrl}");
            var playlists = KawaiJAVTo.Playlist("kawaijav/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, KawaiJAVTo.Pages(html)));
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
        var menu = KawaiJAVTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("kawaijav:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return KawaiJAVTo.Menu(hostLocal, null, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return KawaiJAVTo.Menu(hostLocal, null, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<(string slug, string name)>> CategoriesAsync()
    {
        string memKey = ipkey("kawaijav:tax:cats");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) && hit != null && hit.Count > 0)
            return hit;
        string html = await GetPageAsync(KawaiJAVTo.SiteHost + "/en/categories.html");
        var cats = KawaiJAVTo.Categories(html ?? "");
        if (cats.Count > 0)
            hybridCache.Set(memKey, cats, cacheTime(720), true);
        return cats;
    }

    async Task<List<(string slug, string name)>> TagsAsync(string homeHtml)
    {
        string memKey = ipkey("kawaijav:tax:tags");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) && hit != null && hit.Count > 0)
            return hit;
        var tags = KawaiJAVTo.Tags(homeHtml ?? "");
        if (tags.Count > 0)
            hybridCache.Set(memKey, tags, cacheTime(720), true);
        return tags;
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        long tr = Environment.TickCount64;
        try
        {
            var homeTask = GetPageAsync(KawaiJAVTo.SiteHost + "/en/most-view.html");
            var catTask = CategoriesAsync();
            string homeHtml = await homeTask;
            var tagTask = TagsAsync(homeHtml ?? "");
            await Task.WhenAll(catTask, tagTask);
            var cats = await catTask;
            var tags = await tagTask;
            if (cats.Count > 0 || tags.Count > 0)
            {
                var menu = KawaiJAVTo.Menu(hostLocal, cats, tags);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine($"KawaiJAV: menu cats={cats.Count} tags={tags.Count} ({Environment.TickCount64 - tr}ms)");
                return menu;
            }
        }
        catch { }
        Console.WriteLine($"KawaiJAV: menu fail ({Environment.TickCount64 - tr}ms)");
        return KawaiJAVTo.Menu(hostLocal, null, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        Console.WriteLine($"KawaiJAV: GetPageAsync url={url}");
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
        {
            Console.WriteLine($"KawaiJAV: request blocked by rch");
            return null;
        }
        var headers = HeadersModel.Init(
            ("User-Agent", KawaiJAVTo.ChromeUA),
            ("Referer", KawaiJAVTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                string page = await Http.Get(url, timeoutSeconds: 15, headers: headers);
                Console.WriteLine($"KawaiJAV: direct len={page?.Length ?? 0}");
                if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                    return page;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"KawaiJAV: direct error={ex.Message}");
            }
            try
            {
                string page = await Http.Get(url, timeoutSeconds: 15, httpversion: init.httpversion, proxy: proxy, headers: headers);
                Console.WriteLine($"KawaiJAV: proxy len={page?.Length ?? 0}");
                if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                    return page;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"KawaiJAV: proxy error={ex.Message}");
            }
            await Task.Delay(1500);
        }
        Console.WriteLine($"KawaiJAV: GetPageAsync FAILED");
        return null;
    }

    // ========== VIDOSIK: parse videoData từ trang chi tiết ==========
    [HttpGet, Staticache(manually: true)]
    [Route("kawaijav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (string.IsNullOrWhiteSpace(uri))
            return OnError("stream_links", refresh_proxy: true);

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : KawaiJAVTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"kawaijav:vidosik:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out string cached) && !string.IsNullOrEmpty(cached))
            return Json(JsonSerializer.Deserialize<Dictionary<string, string>>(cached));

        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return OnError("stream_links", refresh_proxy: true);

        var (hd, sd) = KawaiJAVTo.ParseVideoData(page);
        if (string.IsNullOrEmpty(hd) && string.IsNullOrEmpty(sd))
            return OnError("stream_links", refresh_proxy: true);

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(hd))
            dict["HD"] = $"{host}/kawaijav/video?uri={HttpUtility.UrlEncode(uri)}&q=hd";
        if (!string.IsNullOrEmpty(sd))
            dict["SD"] = $"{host}/kawaijav/video?uri={HttpUtility.UrlEncode(uri)}&q=sd";

        string json = JsonSerializer.Serialize(dict);
        hybridCache.Set(memKey, json, cacheTime(10));
        return Json(dict);
    }

    // ========== VIDEO: trả link trực tiếp (.mp4) ==========
    [HttpGet]
    [Route("kawaijav/video")]
    [Route("kawaijav/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        if (string.IsNullOrEmpty(uri))
            return OnError("stream_links", refresh_proxy: true);

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : KawaiJAVTo.SiteHost + "/" + uri.Trim('/');
        string quality = string.IsNullOrEmpty(q) ? "hd" : q.ToLowerInvariant();

        string streamKey = ipkey($"kawaijav:stream:{pageUrl}:{quality}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            string page = await GetPageAsync(pageUrl);
            if (string.IsNullOrEmpty(page))
                return OnError("stream_links", refresh_proxy: true);

            var (hd, sd) = KawaiJAVTo.ParseVideoData(page);
            packed = quality == "sd" && !string.IsNullOrEmpty(sd) ? sd : hd;
            if (string.IsNullOrEmpty(packed))
                packed = hd ?? sd;
            if (string.IsNullOrEmpty(packed))
                return OnError("stream_links", refresh_proxy: true);

            hybridCache.Set(streamKey, packed, cacheTime(10));
        }

        string link = packed, referer = pageUrl;
        int nl = packed.IndexOf('\n');
        if (nl > 0) { link = packed.Substring(0, nl); referer = packed.Substring(nl + 1); }

        var h2 = httpHeaders(init, HeadersModel.Init(
            ("user-agent", KawaiJAVTo.ChromeUA),
            ("referer", referer)));
        return Redirect(HostStreamProxy(link, h2));
    }
}
