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

namespace XViet;

public class XVietController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10000;
    public XVietController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("xviet")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        var menuTask = MenuAsync(search);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"xviet:v1:{search}:{c}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(XVietTo.Uri(init.host, search, c, pg));
            var playlists = XVietTo.Playlist("xviet/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(c) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, XVietTo.Pages(html)));
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
        var menu = XVietTo.MenuHead(host, search);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("xviet:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return XVietTo.Menu(hostLocal, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return XVietTo.Menu(hostLocal, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        try
        {
            string html = await GetPageAsync(XVietTo.SiteHost + "/");
            var tags = XVietTo.HotTags(html ?? "");
            var menu = XVietTo.Menu(hostLocal, tags);
            if (tags.Count > 0)
                hybridCache.Set(memKey, menu, cacheTime(720), true);
            Console.WriteLine($"XViet: menu hottags={tags.Count}");
            return menu;
        }
        catch { }
        return XVietTo.Menu(hostLocal, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 2) break;
            string page = await RaceGetAsync(url, (int)Math.Ceiling(Math.Min(6, left)));
            if (!string.IsNullOrEmpty(page) && (page.Length >= 500
                || page.Contains("PLAYER_CONFIG") || page.Contains("\"file\"")
                || page.Contains("jwplayer") || page.Contains("const url")))
                return page;
            await Task.Delay(1500);
        }
        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;
        var headers = HeadersModel.Init(
            ("User-Agent", XVietTo.ChromeUA),
            ("Referer", XVietTo.SiteHost + "/"),
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

    // ========== DETAIL: lay VB_PLAY tokens ==========
    async Task<List<(string server, string token)>> DetailTokensAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : XVietTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"xviet:detail:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<(string server, string token)> cached)
            && cached != null && cached.Count > 0)
            return cached;
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page)) return null;
        var tokens = XVietTo.VideoTokens(page);
        if (tokens.Count == 0) return null;
        hybridCache.Set(memKey, tokens, cacheTime(15));
        return tokens;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("xviet/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        var tokens = await DetailTokensAsync(uri);
        if (tokens == null || tokens.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (server, _) in tokens)
        {
            string label = "Server " + server;
            if (dict.ContainsKey(label)) continue;
            dict[label] =
                $"{host}/xviet/video.m3u8"
              + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(server)}";
        }
        return Json(dict);
    }

    // ========== RESOLVE: token -> play page -> embed -> jwplayer m3u8 ==========
    async Task<string> ResolveStreamAsync(string pageUrl, string serverId)
    {
        var tokens = await DetailTokensAsync(pageUrl);
        if (tokens == null || tokens.Count == 0) return null;
        var pick = tokens.FirstOrDefault(x => string.Equals(x.server, serverId, StringComparison.OrdinalIgnoreCase));
        if (pick.token == null) return null;

        string play = await GetPageAsync(XVietTo.PlayUrl(pick.token));
        if (string.IsNullOrEmpty(play)) return null;

        var (url, playType) = XVietTo.ParsePlayUrl(play);
        if (string.IsNullOrEmpty(url)) return null;

        string target = url;
        string referer = pageUrl;
        if (string.Equals(playType, "embed", StringComparison.OrdinalIgnoreCase))
        {
            string embed = await GetPageAsync(url);
            if (string.IsNullOrEmpty(embed)) return null;
            var srcs = XVietTo.ParseEmbedSources(embed);
            if (srcs.Count == 0) return null;
            target = srcs[0].file;
            // vscloud.club (Nid Player) từ chối segment nếu referer không phải trang player
            if (url.Contains("vscloud.club"))
                referer = url;
        }
        if (string.IsNullOrEmpty(target)) return null;
        return target + "\n" + referer;
    }

    [HttpGet]
    [Route("xviet/video")]
    [Route("xviet/video.m3u8")]
    [Route("xviet/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        string serverId = !string.IsNullOrEmpty(srv) ? srv : q;
        if (!string.IsNullOrEmpty(serverId) && serverId.StartsWith("Server ", StringComparison.OrdinalIgnoreCase))
            serverId = serverId.Substring(7).Trim();
        if (string.IsNullOrEmpty(serverId))
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : XVietTo.SiteHost + "/" + (uri ?? "").Trim('/');

        string streamKey = ipkey($"xviet:stream:{pageUrl}:{serverId}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            packed = await ResolveStreamAsync(pageUrl, serverId);
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
            ("User-Agent", XVietTo.ChromeUA),
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
