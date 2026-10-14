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

namespace SexVnMe;

public class SexVnMeController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10000;
    public SexVnMeController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sexvnme")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;
        // Search khong co phan trang (trang 2 = 0 item) -> khoa o trang 1
        if (!string.IsNullOrWhiteSpace(search)) pg = 1;

        var menuTask = MenuAsync(search);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"sexvnme:v1:{search}:{c}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(SexVnMeTo.Uri(init.host, search, c, pg));
            var playlists = SexVnMeTo.Playlist("sexvnme/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, SexVnMeTo.Pages(html)));
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

    async Task<List<MenuItem>> MenuAsync(string search)
    {
        var menu = SexVnMeTo.MenuHead(host, search);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("sexvnme:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return SexVnMeTo.Menu(hostLocal, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return SexVnMeTo.Menu(hostLocal, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        try
        {
            string html = await GetPageAsync(SexVnMeTo.SiteHost + "/");
            var tags = SexVnMeTo.HotTags(html ?? "");
            var menu = SexVnMeTo.Menu(hostLocal, tags);
            if (tags.Count > 0)
                hybridCache.Set(memKey, menu, cacheTime(720), true);
            Console.WriteLine($"SexVnMe: menu hottags={tags.Count}");
            return menu;
        }
        catch { }
        return SexVnMeTo.Menu(hostLocal, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 2) break;
            string page = await RaceGetAsync(url, (int)Math.Ceiling(Math.Min(6, left)));
            if (!string.IsNullOrEmpty(page) && page.Length >= 5000)
                return page;
            await Task.Delay(1500);
        }
        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;
        var headers = HeadersModel.Init(
            ("User-Agent", SexVnMeTo.ChromeUA),
            ("Referer", SexVnMeTo.SiteHost + "/"),
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
            if (!string.IsNullOrEmpty(a)) return a;
            if (!string.IsNullOrEmpty(b)) return b;
            if (t1.IsCompleted && t2.IsCompleted) break;
            await Task.Delay(300);
        }
        if (!string.IsNullOrEmpty(a)) return a;
        return !string.IsNullOrEmpty(b) ? b : null;
    }

    // ========== PLAYER: POST /api/player (lazy-resolve, chuan SupJav §11c2) ==========
    // /vidosik KHONG resolve: chi fetch detail lay video-id + server list,
    // /video resolve DUNG 1 server user bam + cache 10p.
    async Task<(string videoId, List<(string id, string num)> servers)> DetailAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return (null, null);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : SexVnMeTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"sexvnme:detail:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out (string videoId, List<(string id, string num)> servers) cached)
            && cached.servers != null && cached.servers.Count > 0)
            return cached;
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page)) return (null, null);
        string vid = SexVnMeTo.VideoId(page);
        var servers = SexVnMeTo.Servers(page);
        if (string.IsNullOrEmpty(vid) || servers.Count == 0) return (null, null);
        var res = (vid, servers);
        hybridCache.Set(memKey, res, cacheTime(15));
        return res;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sexvnme/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        var (videoId, servers) = await DetailAsync(uri);
        if (string.IsNullOrEmpty(videoId) || servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, _) in servers)
        {
            string label = "Server " + id;
            if (dict.ContainsKey(label))
                continue;
            dict[label] =
                $"{host}/sexvnme/video.m3u8"
              + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(id)}";
        }
        return Json(dict);
    }

    async Task<string> ResolveOneAsync(string pageUrl, string videoId, string serverId)
    {
        string api = await SexVnMeTo.PostPlayerAsync(pageUrl, videoId, serverId, proxy, init.httpversion, Math.Max(15, init.httptimeout));
        if (string.IsNullOrEmpty(api))
            api = await SexVnMeTo.PostPlayerAsync(pageUrl, videoId, serverId, null, init.httpversion, 15);
        var (link, referer) = SexVnMeTo.ParsePlayerJson(api ?? "", pageUrl);
        if (string.IsNullOrEmpty(link)) return null;
        return link + "\n" + (referer ?? pageUrl);
    }

    [HttpGet]
    [Route("sexvnme/video")]
    [Route("sexvnme/video.m3u8")]
    [Route("sexvnme/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        string serverId = !string.IsNullOrEmpty(srv) ? srv : q;
        if (!string.IsNullOrEmpty(serverId) && serverId.StartsWith("Server ", StringComparison.OrdinalIgnoreCase))
            serverId = serverId.Substring(7).Trim();
        if (string.IsNullOrEmpty(serverId))
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : SexVnMeTo.SiteHost + "/" + (uri ?? "").Trim('/');

        string streamKey = ipkey($"sexvnme:stream:{pageUrl}:{serverId}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            var (videoId, servers) = await DetailAsync(uri);
            if (string.IsNullOrEmpty(videoId) || servers == null || servers.Count == 0)
                return OnError("stream_links", refresh_proxy: true);
            var pick = servers.FirstOrDefault(x =>
                string.Equals(x.id, serverId, StringComparison.OrdinalIgnoreCase));
            if (pick.id == null)
                return OnError("stream_links", refresh_proxy: true);
            packed = await ResolveOneAsync(pageUrl, videoId, pick.id);
            if (string.IsNullOrEmpty(packed))
                return OnError("stream_links", refresh_proxy: true);
            hybridCache.Set(streamKey, packed, cacheTime(10));
        }

        {
            string link = packed, referer = pageUrl;
            int nl = packed.IndexOf('\n');
            if (nl > 0) { link = packed.Substring(0, nl); referer = packed.Substring(nl + 1); }
            if (await VerifyLinkAsync(link, referer))
            {
                var direct = httpHeaders(init, HeadersModel.Init(("referer", referer)));
                return Redirect(HostStreamProxy(link, direct));
            }
        }
        return OnError("stream_links", refresh_proxy: true);
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
            ("User-Agent", SexVnMeTo.ChromeUA),
            ("Referer", referer));
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
