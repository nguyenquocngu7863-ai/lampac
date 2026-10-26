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

namespace Sieusex;

public class SieusexController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 20000;

    public SieusexController() : base(ModInit.conf) { }

    // HOME (c rong) = list phim moi /new/1/ -> phan trang /new/N/ (112 trang,
    // khong lap vi /new/N/ khac nhau tung trang, da do 2026-10-26).
    [HttpGet, Staticache(manually: true)]
    [Route("sieusex")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        var menuTask = MenuAsync();

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            // cache key du search/c/pg (checklist 9e)
            ipkey($"sieusex:v3:{search}:{c}:{pg}"), 10, async e =>
        {
            string target = SieusexTo.Uri(init.host, search, c, pg);
            string html = await GetPageAsync(target);
            var playlists = SieusexTo.Playlist("sieusex/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            // so trang: list = /new/N/, search = /search/<slug>/page/N/
            // (da do 2026-10-26 tren biphim.net: search co phan trang that)
            int total = SieusexTo.Pages(html);
            return e.Success((playlists, total));
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

    // menu = Tim kiem (search_on, tro root tran - 9e) + base (Danh muc + Top tim
    // kiem, cache 720p, khong phu thuoc context)
    async Task<List<MenuItem>> MenuAsync()
    {
        var menu = new List<MenuItem>(3)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/sieusex" }
        };
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("sieusex:menu:v2");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return SieusexTo.Menu(hostLocal, null, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return SieusexTo.Menu(hostLocal, null, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        long tr = Environment.TickCount64;
        try
        {
            // trang chu = /new/1/: co ca nav danh muc lan khung Top tim kiem
            string html = await GetPageAsync(SieusexTo.SiteHost + "/new/1/");
            var cats = SieusexTo.NavCategories(html ?? "");
            var hot = SieusexTo.HotSearches(html ?? "");
            if (cats.Count > 0 || hot.Count > 0)
            {
                var menu = SieusexTo.Menu(hostLocal, cats, hot);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine($"Sieusex: menu cats={cats.Count} hot={hot.Count} ({Environment.TickCount64 - tr}ms)");
                return menu;
            }
        }
        catch { }
        Console.WriteLine($"Sieusex: menu fail ({Environment.TickCount64 - tr}ms)");
        return SieusexTo.Menu(hostLocal, null, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 2) break;
            string page = await RaceGetAsync(url, (int)Math.Ceiling(Math.Min(8, left)));
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
            ("User-Agent", SieusexTo.ChromeUA),
            ("Referer", SieusexTo.SiteHost + "/"),
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
            if (!string.IsNullOrEmpty(a) && a.Length > 5000) return a;
            if (!string.IsNullOrEmpty(b) && b.Length > 5000) return b;
            if (t1.IsCompleted && t2.IsCompleted) break;
            await Task.Delay(300);
        }
        if (!string.IsNullOrEmpty(a) && a.Length > 5000) return a;
        return !string.IsNullOrEmpty(b) && b.Length > 5000 ? b : null;
    }

    // ========== PLAYER: LAZY-RESOLVE (11c2) ==========
    // /vidosik KHONG resolve: chi fetch detail -> postId + so server, tra dict
    // server -> /video.m3u8. /video resolve dung 1 server (detail -> admin-ajax
    // -> embed hidecdn -> playlist XOR 0x5A), cache 120p (VOD nhung san trong
    // trang, khong token).
    [HttpGet, Staticache(manually: true)]
    [Route("sieusex/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        string pageUrl = SieusexTo.DetailUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return OnError("stream_links", refresh_proxy: true);
        var info = await DetailInfoAsync(pageUrl);
        if (info == null)
            return OnError("stream_links", refresh_proxy: true);

        int n = info.Value.servers > 0 ? Math.Min(info.Value.servers, 3) : 1;
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i <= n; i++)
            dict["#" + i] = $"{host}/sieusex/video.m3u8?uri={HttpUtility.UrlEncode(pageUrl)}&sv={i}";

        // warm server 1 nen khi bam an lien (2-3 request, ~2s)
        string wurl = pageUrl;
        _ = Task.Run(async () => { try { await ResolvePlaylistAsync(wurl, 1); } catch { } });

        return Json(dict);
    }

    async Task<(string postId, int servers)?> DetailInfoAsync(string pageUrl)
    {
        string memKey = ipkey($"sieusex:detail:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out string packed) && !string.IsNullOrEmpty(packed))
        {
            int at = packed.IndexOf(':');
            if (at > 0 && int.TryParse(packed[(at + 1)..], out int sv0))
                return (packed[..at], sv0);
        }
        string html = await GetPageAsync(pageUrl);
        string postId = SieusexTo.PostId(html ?? "");
        if (string.IsNullOrEmpty(postId)) return null;
        int servers = SieusexTo.ServerCount(html ?? "");
        hybridCache.Set(memKey, postId + ":" + servers, cacheTime(60), true);
        return (postId, servers);
    }

    // POST admin-ajax -> URL embed (cache 30p: hash+cdn on dinh theo phim)
    async Task<string> AjaxEmbedAsync(string postId, int sv, string referer)
    {
        string key = ipkey($"sieusex:embed:{postId}:{sv}");
        if (hybridCache.TryGetValue(key, out string hit) && !string.IsNullOrEmpty(hit))
            return hit;
        var headers = HeadersModel.Init(
            ("User-Agent", SieusexTo.ChromeUA),
            ("Referer", referer),
            ("Origin", SieusexTo.SiteHost),
            ("X-Requested-With", "XMLHttpRequest"));
        string body = $"player_server={postId}&server={sv}&vast_seen=1&is_mobile=0";
        string json = null;
        try
        {
            using var content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            json = await Http.Post(SieusexTo.SiteHost + "/wp-admin/admin-ajax.php", content,
                timeoutSeconds: 15, headers: headers, httpversion: init.httpversion, disposeData: true);
        }
        catch { }
        string embed = SieusexTo.EmbedFromPlayer(json ?? "");
        if (!string.IsNullOrEmpty(embed))
            hybridCache.Set(key, embed, cacheTime(30), true);
        return embed;
    }

    // GET trang embed hidecdn. hidecdn hay ngat SSL ket noi lan dau
    // (BoringSSL SSL_connect closed, da do 50% so vong) -> race + retry 4 vong.
    async Task<string> GetEmbedAsync(string embedUrl, string referer)
    {
        string memKey = ipkey($"sieusex:embedhtml:{embedUrl}");
        if (hybridCache.TryGetValue(memKey, out string hit) && !string.IsNullOrEmpty(hit))
            return hit;
        var headers = HeadersModel.Init(
            ("User-Agent", SieusexTo.ChromeUA),
            ("Referer", referer ?? (SieusexTo.SiteHost + "/")));
        for (int i = 0; i < 4; i++)
        {
            string a = null, b = null;
            var t1 = Task.Run(async () =>
            {
                try { await httpHydra.GetSpan(embedUrl, span => { a = span.ToString(); }, addheaders: headers); }
                catch { }
            });
            var t2 = Task.Run(async () =>
            {
                try { b = await Http.Get(embedUrl, timeoutSeconds: 12, httpversion: init.httpversion, proxy: proxy, headers: headers); }
                catch { }
            });
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                if (!string.IsNullOrEmpty(a) && a.Length > 5000) { hybridCache.Set(memKey, a, cacheTime(30), true); return a; }
                if (!string.IsNullOrEmpty(b) && b.Length > 5000) { hybridCache.Set(memKey, b, cacheTime(30), true); return b; }
                if (t1.IsCompleted && t2.IsCompleted) break;
                await Task.Delay(250);
            }
            await Task.Delay(600 * (i + 1));
        }
        return null;
    }

    // Fallback: /embed/<hash>/pl?c=<cdn> -> {"d":"<b64>"}
    async Task<string> PlApiAsync(string embedUrl, string cdn, string referer)
    {
        string hash = SieusexTo.EmbedHash(embedUrl);
        string host = SieusexTo.EmbedHost(embedUrl);
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(host)) return null;
        var headers = HeadersModel.Init(
            ("User-Agent", SieusexTo.ChromeUA),
            ("Referer", embedUrl));
        string json = null;
        try { json = await Http.Get($"{host}/embed/{hash}/pl?c={cdn ?? "tt3"}", timeoutSeconds: 12, httpversion: init.httpversion, proxy: proxy, headers: headers); }
        catch { }
        return SieusexTo.PlApiData(json ?? "");
    }

    async Task<string> ResolvePlaylistAsync(string uri, int sv)
    {
        string pageUrl = SieusexTo.DetailUrl(uri);
        if (string.IsNullOrEmpty(pageUrl)) return null;
        string memKey = ipkey($"sieusex:pl:{pageUrl}:{sv}");
        if (hybridCache.TryGetValue(memKey, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var info = await DetailInfoAsync(pageUrl);
        if (info == null || string.IsNullOrEmpty(info.Value.postId)) return null;
        string embed = await AjaxEmbedAsync(info.Value.postId, sv, pageUrl);
        if (string.IsNullOrEmpty(embed))
        {
            // chuoi du phong 11c: server chet -> thu server khac
            foreach (int alt in new[] { 1, 2, 3 })
            {
                if (alt == sv) continue;
                embed = await AjaxEmbedAsync(info.Value.postId, alt, pageUrl);
                if (!string.IsNullOrEmpty(embed)) { sv = alt; memKey = ipkey($"sieusex:pl:{pageUrl}:{sv}"); break; }
            }
        }
        if (string.IsNullOrEmpty(embed)) return null;

        string html = await GetEmbedAsync(embed, pageUrl);
        if (string.IsNullOrEmpty(html)) return null;

        string cdn = SieusexTo.CdnParam(embed);
        string m3u = SieusexTo.DecodePl(SieusexTo.PlData(html, cdn));
        if (string.IsNullOrEmpty(m3u))
            m3u = SieusexTo.DecodePl(await PlApiAsync(embed, cdn, pageUrl));
        if (string.IsNullOrEmpty(m3u))
        {
            // thu het cac key cua cung embed (tt3/tt2/tt/tt1 - cung video, khac CDN)
            foreach (string k in new[] { "tt3", "tt2", "tt", "tt1" })
            {
                if (string.Equals(k, cdn, StringComparison.OrdinalIgnoreCase)) continue;
                m3u = SieusexTo.DecodePl(SieusexTo.PlData(html, k));
                if (!string.IsNullOrEmpty(m3u)) break;
            }
        }
        if (string.IsNullOrEmpty(m3u) || !m3u.Contains("#EXTM3U")) return null;

        hybridCache.Set(memKey, m3u, cacheTime(120), true);
        return m3u;
    }

    [HttpGet]
    [Route("sieusex/video")]
    [Route("sieusex/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string sv = "1")
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        int idx = int.TryParse(sv, out int v) ? Math.Clamp(v, 1, 3) : 1;
        string m3u = await ResolvePlaylistAsync(uri, idx);
        if (string.IsNullOrEmpty(m3u))
            return OnError("stream_links", refresh_proxy: true);

        // Playlist nhung san trong trang embed: segment absolute tren TikTok CDN
        // (khong can Referer, da do 200 no-ref). qua HostStreamProxy de app khong
        // cham URL upstream truc tiep (streamproxy = true).
        string pageUrl = SieusexTo.DetailUrl(uri) ?? SieusexTo.SiteHost + "/";
        var segHeaders = HeadersModel.Init(("user-agent", SieusexTo.ChromeUA));
        var sb = new StringBuilder(m3u.Length * 2);
        foreach (string raw in m3u.Split('\n'))
        {
            string t = raw.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith("#")) { sb.AppendLine(t); continue; }
            string abs = SieusexTo.AbsoluteUrl(t, pageUrl) ?? SieusexTo.AbsoluteUrl(t, "");
            if (string.IsNullOrEmpty(abs)) continue;
            sb.AppendLine(HostStreamProxy(abs, segHeaders));
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "application/vnd.apple.mpegurl");
    }
}
