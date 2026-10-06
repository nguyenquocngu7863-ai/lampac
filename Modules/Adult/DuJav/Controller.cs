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

namespace DuJav;

public class DuJavController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10_000;
    public DuJavController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("dujav")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // Gop sort ve dung tap cua context (search thi sort la no-op) —
        // TRUOC khi lap vao cache key. Clamp ve null = khong sort.
        sort = DuJavTo.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult(
            ipkey($"dujav:{search}:{c}:{sort}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(
                DuJavTo.Uri(init.host, search, c, pg, sort));
            var playlists = DuJavTo.Playlist("dujav/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await menuTask, total_pages: 0);
    }

    // menu = head (phu thuoc search/sort/c, dung lai moi request — rat re)
    //        + base (taxonomy, cache 1 lan, khong phu thuoc context)
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var menu = DuJavTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("dujav:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        if (!await menuLock.WaitAsync(2000))
            return DuJavTo.Menu(hostLocal, null, null);

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2)
                && hit2 != null && hit2.Count > 0)
                return hit2;

            var build = BuildMenuAsync(hostLocal, memKey);

            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return DuJavTo.Menu(hostLocal, null, null);

            return await build;
        }
        finally
        {
            menuLock.Release();
        }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        try
        {
            // topics (~500, 1 trang) + top dien vien theo works (p1+p2 = ~48).
            // stars co ~800 trang (38k dien vien) nen chi lay top.
            var topicsTask = TaxonomiesAsync("/vi/topics", "topics");
            var starsTask1 = TaxonomiesAsync("/vi/stars?sort=works", "stars");
            var starsTask2 = TaxonomiesAsync("/vi/stars?sort=works&page=2", "stars", "stars-p2");
            await Task.WhenAll(topicsTask, starsTask1, starsTask2);

            var topics = await topicsTask;
            var stars = await starsTask1;
            foreach (var s in await starsTask2)
            {
                bool dup = false;
                foreach (var x in stars)
                    if (x.path == s.path) { dup = true; break; }
                if (!dup) stars.Add(s);
            }

            if (topics.Count > 0 || stars.Count > 0)
            {
                var menu = DuJavTo.Menu(hostLocal, topics, stars);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                return menu;
            }
        }
        catch { }

        return DuJavTo.Menu(hostLocal, null, null);
    }

    async Task<List<(string name, string path)>> TaxonomiesAsync(string page, string kind, string cacheKey = null)
    {
        string memKey = ipkey($"dujav:tax:{cacheKey ?? kind}");

        if (hybridCache.TryGetValue(memKey, out List<(string name, string path)> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync($"{DuJavTo.SiteHost}{page}");
        var res = DuJavTo.Taxonomies(html, kind);
        if (res.Count == 0)
            return res;

        hybridCache.Set(memKey, res, cacheTime(720), true);
        return res;
    }

    async Task<string> GetPageAsync(string url)
    {
        try
        {
            return await Http.Get(url, timeoutSeconds: 15,
                headers: HeadersModel.Init(("User-Agent", DuJavTo.ChromeUA)));
        }
        catch
        {
            return null;
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("dujav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/dujav/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : DuJavTo.SiteHost + "/" + uri.Trim('/');

        string memKey = ipkey($"dujav:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        // 1 chat luong duy nhat (master 1080p). Token + m3u8 dung lai duoc
        // nen cache 30p thay vi resolve moi lan bam.
        string m3u8 = await DuJavTo.ResolveM3U8(pageUrl);
        if (string.IsNullOrEmpty(m3u8))
            return null;

        var res = new Dictionary<string, string>() { { "1080p", m3u8 } };
        hybridCache.Set(memKey, res, cacheTime(30), true);
        return res;
    }

    [HttpGet]
    [Route("dujav/video")]
    [Route("dujav/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", DuJavTo.SiteHost + "/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
