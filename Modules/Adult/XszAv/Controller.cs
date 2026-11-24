using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace XszAv;

public class XszAvController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 15000;
    public XszAvController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("xszav")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1, string rel = null)
    {
        if (!string.IsNullOrWhiteSpace(rel))
            return await Related(rel, pg);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        sort = XszAvTo.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"xszav:v1:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(XszAvTo.Uri(init.host, search, c, sort, pg));
            var playlists = XszAvTo.Playlist("xszav/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, XszAvTo.Pages(html)));
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
        var menu = XszAvTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("xszav:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        if (!await menuLock.WaitAsync(2000))
            return XszAvTo.Menu(host, null);

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;

            var build = BuildMenuAsync(host, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return XszAvTo.Menu(host, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        try
        {
            string html = await GetPageAsync(XszAvTo.SiteHost + "/tags");
            var tags = XszAvTo.Tags(html ?? "");
            if (tags.Count > 0)
            {
                var menu = XszAvTo.Menu(hostLocal, tags);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine($"XszAv: menu tags={tags.Count}");
                return menu;
            }
        }
        catch { }
        Console.WriteLine("XszAv: menu fail");
        return XszAvTo.Menu(hostLocal, null);
    }

    // ========== POPUP ==========
    static object EmptyPopup() => new { items = new object[0], groups = new object[0] };

    [HttpGet]
    [Route("xszav/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(EmptyPopup());

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = XszAvTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(EmptyPopup());

        string memKey = ipkey($"xszav:info:{pageUrl}");
        if (!hybridCache.TryGetValue(memKey,
            out (List<(string name, string path)> actresses, List<(string name, string path)> keywords) info))
        {
            string page = await GetPageAsync(pageUrl);
            info = (XszAvTo.Chips(page, true), XszAvTo.Chips(page, false));
            if (info.actresses.Count > 0 || info.keywords.Count > 0)
                hybridCache.Set(memKey, info, cacheTime(360));
        }

        var groups = new List<object>();

        if (info.actresses != null && info.actresses.Count > 0)
            groups.Add(new
            {
                title = "Diễn viên",
                items = info.actresses.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/xszav?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        if (info.keywords != null && info.keywords.Count > 0)
            groups.Add(new
            {
                title = "Thẻ",
                items = info.keywords.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/xszav?search={HttpUtility.UrlEncode(x.name)}"
                }).ToList()
            });

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    url = $"{host}/xszav?rel={HttpUtility.UrlEncode(pageUrl)}",
                    plain = true
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    // ?rel=<uri detail> — 1 trang (SISI: total_pages 0 = con trang vo han).
    async Task<ActionResult> Related(string uri, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = XszAvTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"xszav:rel:{pageUrl}"), 20, async e =>
        {
            string page = await GetPageAsync(pageUrl);
            var playlists = XszAvTo.Related("xszav/vidosik", page ?? "", pageUrl);

            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null), total_pages: 1);
    }

    async Task<string> GetPageAsync(string url)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", XszAvTo.ChromeUA),
            ("Referer", XszAvTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        for (int i = 0; i < 3; i++)
        {
            try
            {
                string page = await Http.Get(url, timeoutSeconds: 20, httpversion: init.httpversion, proxy: proxy, headers: headers);
                if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                    return page;
            }
            catch { }
            await Task.Delay(1200);
        }
        return null;
    }

    async Task<List<(string label, string link)>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : XszAvTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"xszav:servers:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<(string label, string link)> cached) && cached != null && cached.Count > 0)
            return cached;
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page)) return null;
        string mark = XszAvTo.VideoMark(page);
        if (string.IsNullOrEmpty(mark)) return null;
        string full = XszAvTo.NormalizeMediaUrl(mark);
        if (string.IsNullOrEmpty(full))
            full = XszAvTo.NormalizePageUrl(mark);
        if (string.IsNullOrEmpty(full)) return null;
        var servers = new List<(string label, string link)> { ("HD", full) };
        hybridCache.Set(memKey, servers, cacheTime(15));
        return servers;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("xszav/vidosik")]
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
            dict[label] = $"{host}/xszav/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(label)}";
        }
        return Json(dict);
    }

    [HttpGet]
    [Route("xszav/video")]
    [Route("xszav/video.m3u8")]
    [Route("xszav/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q = null, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        string label = !string.IsNullOrEmpty(srv) ? srv : q;
        if (string.IsNullOrEmpty(label))
            label = "HD";

        string streamKey = ipkey($"xszav:stream:{uri}:{label}");
        string link = null;
        if (hybridCache.TryGetValue(streamKey, out string cached) && !string.IsNullOrEmpty(cached))
            link = cached;
        else
        {
            var servers = await DetailServersAsync(uri);
            if (servers == null || servers.Count == 0)
                return OnError("stream_links", refresh_proxy: true);
            var pick = servers.FirstOrDefault(x => string.Equals(x.label, label, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(pick.label))
                pick = servers[0];
            link = pick.link;
            if (string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
            hybridCache.Set(streamKey, link, cacheTime(10));
        }

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", XszAvTo.SiteHost + "/"),
            ("origin", XszAvTo.SiteHost)));
        if (await VerifyLinkAsync(link, XszAvTo.SiteHost + "/"))
            return Redirect(HostStreamProxy(link, direct));
        return Redirect(HostStreamProxy(link, direct));
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
            ("User-Agent", XszAvTo.ChromeUA),
            ("Referer", referer),
            ("Origin", XszAvTo.SiteHost));
        try
        {
            using var resp = await Http.ResponseHeaders(link, timeoutSeconds: 6, headers: headers, httpversion: init.httpversion);
            if (resp != null && resp.IsSuccessStatusCode && IsMediaContentType(resp.Content?.Headers?.ContentType?.ToString()))
                return true;
        }
        catch { }
        try
        {
            using var resp = await Http.ResponseHeaders(link, timeoutSeconds: 6, headers: headers, proxy: proxy, httpversion: init.httpversion);
            return resp != null && resp.IsSuccessStatusCode && IsMediaContentType(resp.Content?.Headers?.ContentType?.ToString());
        }
        catch { return false; }
    }
}