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

namespace PhimSexVN88;

public class PhimSexVN88Controller : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10000;
    public PhimSexVN88Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("phimsexvn88")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (pg < 1) pg = 1;
        var menuTask = MenuAsync(search);
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"p88:v1:{search}:{c}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(PhimSexVN88To.Uri(init.host, search, c, pg));
            var playlists = PhimSexVN88To.Playlist("phimsexvn88/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(c) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, PhimSexVN88To.Pages(html)));
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
        var menu = PhimSexVN88To.MenuHead(host, search);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("p88:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return PhimSexVN88To.Menu(hostLocal, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return PhimSexVN88To.Menu(hostLocal, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        try
        {
            string html = await GetPageAsync(PhimSexVN88To.SiteHost + "/");
            var tags = PhimSexVN88To.HotTags(html ?? "");
            var menu = PhimSexVN88To.Menu(hostLocal, tags);
            if (tags.Count > 0)
                hybridCache.Set(memKey, menu, cacheTime(720), true);
            Console.WriteLine($"PhimSexVN88: menu tags={tags.Count}");
            return menu;
        }
        catch { }
        return PhimSexVN88To.Menu(hostLocal, null);
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
                || page.Contains("item-movie") || page.Contains("data-video-id")
                || page.Contains("video-server") || page.Contains("title-movie")))
                return page;
            await Task.Delay(1500);
        }
        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;
        var headers = HeadersModel.Init(
            ("User-Agent", PhimSexVN88To.ChromeUA),
            ("Referer", PhimSexVN88To.SiteHost + "/"),
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

    async Task<DetailInfo> DetailAsync(string pageUrl)
    {
        string memKey = ipkey($"p88:detail:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out DetailInfo hit) && hit != null && !string.IsNullOrEmpty(hit.videoId))
            return hit;
        string html = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(html)) return null;
        var info = new DetailInfo()
        {
            videoId = PhimSexVN88To.VideoId(html),
            servers = PhimSexVN88To.Servers(html)
        };
        if (!string.IsNullOrEmpty(info.videoId))
            hybridCache.Set(memKey, info, cacheTime(15));
        return info;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("phimsexvn88/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (string.IsNullOrWhiteSpace(uri))
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : PhimSexVN88To.SiteHost + "/" + uri.Trim('/');
        var info = await DetailAsync(pageUrl);
        if (info == null || info.servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (sid, name) in info.servers)
        {
            string label = string.IsNullOrEmpty(name) ? ("Server " + sid) : name;
            if (dict.ContainsKey(label)) continue;
            dict[label] =
                $"{host}/phimsexvn88/video.m3u8"
              + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(sid)}";
        }
        return Json(dict);
    }

    // ===== resolve: POST /api/player {id,server} -> jwplayer sources[0].file (m3u8) =====
    async Task<string> ResolveAsync(string pageUrl, string serverId)
    {
        var info = await DetailAsync(pageUrl);
        if (info == null || string.IsNullOrEmpty(info.videoId)) return null;

        var headers = HeadersModel.Init(
            ("User-Agent", PhimSexVN88To.ChromeUA),
            ("Referer", pageUrl),
            ("X-Requested-With", "XMLHttpRequest"));

        string form = "id=" + HttpUtility.UrlEncode(info.videoId) + "&server=" + HttpUtility.UrlEncode(serverId);
        string json = null;
        try
        {
            json = await Http.Post(PhimSexVN88To.SiteHost + "/api/player", form,
                headers: headers, timeoutSeconds: Math.Max(15, init.httptimeout),
                httpversion: init.httpversion, proxy: proxy);
        }
        catch { }
        if (string.IsNullOrEmpty(json))
        {
            try
            {
                json = await Http.Post(PhimSexVN88To.SiteHost + "/api/player", form,
                    headers: headers, timeoutSeconds: Math.Max(15, init.httptimeout),
                    httpversion: init.httpversion);
            }
            catch { }
        }
        if (string.IsNullOrEmpty(json)) return null;

        var (type, url) = PhimSexVN88To.ParsePlayer(json);
        if (string.IsNullOrEmpty(url)) return null;

        string target = url, referer = pageUrl;
        if (string.Equals(type, "iframe", StringComparison.OrdinalIgnoreCase))
        {
            if (url.Contains(".m3u8") || url.Contains(".mp4"))
            {
                target = url;
            }
            else
            {
                string embed = await GetPageAsync(url);
                string src = PhimSexVN88To.FindMedia(embed ?? "");
                if (string.IsNullOrEmpty(src)) return null;
                target = src;
                referer = url;
            }
        }
        return target + "\n" + referer;
    }

    [HttpGet]
    [Route("phimsexvn88/video")]
    [Route("phimsexvn88/video.m3u8")]
    [Route("phimsexvn88/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        string serverId = !string.IsNullOrEmpty(srv) ? srv : q;
        if (string.IsNullOrEmpty(serverId))
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : PhimSexVN88To.SiteHost + "/" + (uri ?? "").Trim('/');

        string streamKey = ipkey($"p88:stream:{pageUrl}:{serverId}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            packed = await ResolveAsync(pageUrl, serverId);
            if (string.IsNullOrEmpty(packed))
                return OnError("stream_links", refresh_proxy: true);
            hybridCache.Set(streamKey, packed, cacheTime(10));
        }

        string link = packed, referer = pageUrl;
        int nl = packed.IndexOf('\n');
        if (nl > 0) { link = packed.Substring(0, nl); referer = packed.Substring(nl + 1); }
        if (await VerifyLinkAsync(link, referer))
        {
            var direct = httpHeaders(init, HeadersModel.Init(("referer", referer)));
            return Redirect(HostStreamProxy(link, direct));
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
            ("User-Agent", PhimSexVN88To.ChromeUA),
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
