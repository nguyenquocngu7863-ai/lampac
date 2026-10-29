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

namespace Viet69Life;

public class Viet69LifeController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 20000;

    public Viet69LifeController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("viet69life")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (pg < 1) pg = 1;

        sort = Viet69LifeTo.NormalizeSort(sort);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"viet69life:v3:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(Viet69LifeTo.Uri(search, c, sort, pg));
            var playlists = Viet69LifeTo.Playlist("viet69life/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, Viet69LifeTo.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache, await menuTask, total_pages: cache.Value.total_pages);
    }

    // menu: search + categories (cache 1 lần)
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var menu = new List<MenuItem>
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/viet69life" }
        };
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("viet69life:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return new List<MenuItem>();
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return new List<MenuItem>();
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        string home = await GetPageAsync(Viet69LifeTo.SiteHost + "/");
        var cats = Viet69LifeTo.NavCategories(home ?? "");
        var menu = new List<MenuItem>();
        if (cats.Count > 0)
        {
            var gm = new List<MenuItem>(cats.Count);
            foreach (var (slug, name) in cats)
                gm.Add(new MenuItem(string.IsNullOrEmpty(name) ? slug : name,
                    hostLocal + "/viet69life?c=" + HttpUtility.UrlEncode(slug)));
            menu.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = gm });
        }
        if (menu.Count > 0)
        {
            hybridCache.Set(memKey, menu, cacheTime(720), true);
            Console.WriteLine($"Viet69Life: menu cats={cats.Count}");
            return menu;
        }
        Console.WriteLine($"Viet69Life: menu fail");
        return new List<MenuItem>();
    }

    // ========== Fetch trang (CF protection nhẹ) ==========
    async Task<string> GetPageAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var hdr = HeadersModel.Init(
            ("User-Agent", Viet69LifeTo.ChromeUA),
            ("Referer", Viet69LifeTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        try
        {
            string html = await Http.Get(url, timeoutSeconds: 25, headers: hdr, httpversion: init.httpversion);
            if (!string.IsNullOrEmpty(html) && html.Length > 3000 && !html.Contains("Just a moment"))
                return html;
        }
        catch { }
        try
        {
            string html = await Http.Get(url, timeoutSeconds: 25, headers: hdr, proxy: proxy, httpversion: init.httpversion);
            if (!string.IsNullOrEmpty(html) && html.Length > 3000 && !html.Contains("Just a moment"))
                return html;
        }
        catch { }
        return null;
    }

    async Task<string> GetHtmlAsync(string url, string referer, int timeoutSeconds = 25)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var hdr = HeadersModel.Init(
            ("User-Agent", Viet69LifeTo.ChromeUA),
            ("Referer", referer ?? Viet69LifeTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        try
        {
            string html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: hdr, httpversion: init.httpversion);
            if (!string.IsNullOrEmpty(html) && html.Length > 1000 && !html.Contains("Just a moment"))
                return html;
        }
        catch { }
        return null;
    }

    // ========== Detail servers ==========
    async Task<List<(string label, string link)>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : Viet69LifeTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"viet69life:servers:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<(string label, string link)> cached) && cached != null && cached.Count > 0)
            return cached;
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page)) return null;
        var servers = Viet69LifeTo.Servers(page);
        if (servers.Count == 0) return null;
        hybridCache.Set(memKey, servers, cacheTime(15));
        return servers;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("viet69life/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, _) in servers)
        {
            if (dict.ContainsKey(label)) continue;
            dict[label] =
                $"{host}/viet69life/video.m3u8"
              + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(label)}";
        }
        return Json(dict);
    }

    // ========== Video: iframe embed -> jwplayer m3u8 -> HostStreamProxy ==========
    [HttpGet]
    [Route("viet69life/video")]
    [Route("viet69life/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        string label = srv;
        if (string.IsNullOrEmpty(label))
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : Viet69LifeTo.SiteHost + "/" + (uri ?? "").Trim('/');

        string streamKey = ipkey($"viet69life:stream:{pageUrl}:{label}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            var servers = await DetailServersAsync(uri);
            if (servers == null || servers.Count == 0)
                return OnError("stream_links", refresh_proxy: true);
            var pick = servers.FirstOrDefault(x => string.Equals(x.label, label, StringComparison.OrdinalIgnoreCase));
            if (pick.label == null)
                return OnError("stream_links", refresh_proxy: true);
            packed = await ResolveOneAsync(pageUrl, pick.label, pick.link);
            if (string.IsNullOrEmpty(packed))
                return OnError("stream_links", refresh_proxy: true);
            hybridCache.Set(streamKey, packed, cacheTime(10));
        }

        string link = packed, referer = Viet69LifeTo.SiteHost + "/";
        int nl = packed.IndexOf('\n');
        if (nl > 0) { link = packed.Substring(0, nl); referer = packed.Substring(nl + 1); }

        // Fetch trang embed (iframe) rồi boc jwplayer m3u8.
        string embedHtml = await GetHtmlAsync(link, pageUrl);
        if (string.IsNullOrEmpty(embedHtml))
            return OnError("stream_links", refresh_proxy: true);
        string master = Viet69LifeTo.JwplayerMaster(embedHtml);
        if (string.IsNullOrEmpty(master))
            return OnError("stream_links", refresh_proxy: true);

        bool ok = await VerifyLinkAsync(master, referer);
        if (!ok)
        {
            Console.WriteLine($"Viet69Life: verify failed master={master}");
            return OnError("stream_links", refresh_proxy: true);
        }
        var direct = httpHeaders(init, HeadersModel.Init(("referer", referer)));
        return Redirect(HostStreamProxy(master, direct));
    }

    async Task<string> ResolveOneAsync(string pageUrl, string label, string link)
    {
        // iframe embed -> jwplayer m3u8 (tra kèm referer = trang embed)
        if (string.IsNullOrEmpty(link)) return null;
        return link + "\n" + link;
    }

    static bool IsMediaContentType(string ct)
    {
        if (string.IsNullOrEmpty(ct)) return false;
        ct = ct.ToLowerInvariant();
        return ct.Contains("mpegurl") || ct.Contains("mp2t") || ct.Contains("octet-stream")
            || ct.Contains("video/") || ct.Contains("application/vnd.apple");
    }

    async Task<bool> VerifyLinkAsync(string link, string referer)
    {
        if (string.IsNullOrEmpty(link)) return false;
        var headers = HeadersModel.Init(
            ("User-Agent", Viet69LifeTo.ChromeUA),
            ("Referer", referer));
        try
        {
            using var resp = await Http.ResponseHeaders(link, timeoutSeconds: 8, headers: headers, httpversion: init.httpversion);
            if (resp != null && resp.IsSuccessStatusCode && IsMediaContentType(resp.Content?.Headers?.ContentType?.ToString()))
                return true;
        }
        catch { }
        return false;
    }
}
