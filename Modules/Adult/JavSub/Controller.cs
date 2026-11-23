using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace JavSub;

public class JavSubController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10000;
    public JavSubController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javsub")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // Danh sach "Video lien quan" tu popup long-press.
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        // Site khong co sort (do live 2026-10-09) -> clamp luon ve null de
        // cache key sach.
        sort = JavSubTo.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"javsub:v1:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(JavSubTo.Uri(init.host, search, c, pg));
            var playlists = JavSubTo.Playlist("javsub/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, JavSubTo.Pages(html)));
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
        var menu = JavSubTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("javsub:menu:v2");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return JavSubTo.Menu(hostLocal, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return JavSubTo.Menu(hostLocal, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        try
        {
            string html = await GetPageAsync(JavSubTo.SiteHost + "/?view=the-loai");
            var cats = JavSubTo.Taxonomies(html ?? "");
            if (cats.Count > 0)
            {
                var menu = JavSubTo.Menu(hostLocal, cats);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                return menu;
            }
        }
        catch { }
        return JavSubTo.Menu(hostLocal, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 2) break;
            string page = await RaceGetAsync(url, (int)Math.Ceiling(Math.Min(6, left)));
            if (!string.IsNullOrEmpty(page) && page.Length >= 3000)
                return page;
            await Task.Delay(1500);
        }
        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;
        var headers = HeadersModel.Init(
            ("User-Agent", JavSubTo.ChromeUA),
            ("Referer", JavSubTo.SiteHost + "/"),
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
        return !string.IsNullOrEmpty(a) ? a : b;
    }

    // AJAX JSON rieng: KHONG dung GetPageAsync (ham do doi HTML >= 3000B + retry
    // 30s, JSON ngan se bi coi la that bai va vuot budget cua InvokeCacheResult).
    async Task<string> FetchJsonAsync(string url)
    {
        try
        {
            string body = null;
            var headers = HeadersModel.Init(
                ("User-Agent", JavSubTo.ChromeUA),
                ("Referer", JavSubTo.SiteHost + "/"),
                ("Accept", "application/json, text/plain, */*"),
                ("X-Requested-With", "XMLHttpRequest"));

            await httpHydra.GetSpan(url, span => { body = span.ToString(); }, addheaders: headers);
            return body;
        }
        catch { return null; }
    }

    // ========== PLAYER: LAZY-RESOLVE (§11c2) ==========
    // Detail co 2 nut Server 1/2 (data-source -> player JW tren streamforester/
    // vcast). /vidosik chi liet ke (1 fetch); /video POST config resolve DUNG
    // 1 server + cache 10p.
    async Task<List<(string label, string link)>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;
        // Uri do client gui -> bat buoc dung host site + path /phim-sex/.
        string pageUrl = JavSubTo.SafeDetailUrl(uri);
        if (string.IsNullOrEmpty(pageUrl)) return null;
        string memKey = ipkey($"javsub:servers:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<(string label, string link)> cached) && cached != null && cached.Count > 0)
            return cached;
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page)) return null;
        var servers = JavSubTo.Servers(page);
        if (servers.Count == 0) return null;
        hybridCache.Set(memKey, servers, cacheTime(15));
        return servers;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javsub/vidosik")]
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
            if (dict.ContainsKey(label))
                continue;
            dict[label] =
                // Bat buoc co duoi .m3u8: app chon player theo duoi URL, `/javsub/video`
                // (body la HLS) bi coi la file thuong -> "no supported source".
                $"{host}/javsub/video.m3u8"
              + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(label)}";
        }
        return Json(dict);
    }

    // POST <embed>/videos/<id>/config?d= (giong JS site) -> sources[0].file.
    // POST truc tiep doi khi 404 tu server (do 2026-10-09) -> fallback Chrome
    // mo embed that (JS chay that lay duoc sources).
    async Task<string> ResolveOneAsync(string pageUrl, string label, string link)
    {
        // 0. Embed kieu vcast: sources nam san trong videoData (1 GET, re nhat).
        string embedHtml = await GetPageAsync(link);
        string inline = JavSubTo.EmbedInlineSource(embedHtml ?? "", link);
        if (!string.IsNullOrEmpty(inline)) return inline + "\n" + link;
        string api = JavSubTo.ConfigUrl(link);
        if (string.IsNullOrEmpty(api)) return null;
        var headers = HeadersModel.Init(
            ("User-Agent", JavSubTo.ChromeUA),
            ("Referer", pageUrl));
        foreach (bool useProxy in new[] { false, true })
        {
            try
            {
                using var content = new System.Net.Http.StringContent(
                    "{}", System.Text.Encoding.UTF8, "application/json");
                string json = useProxy
                    ? await Http.Post(api, content,
                        timeoutSeconds: Math.Max(15, init.httptimeout),
                        headers: headers,
                        proxy: proxy,
                        httpversion: init.httpversion,
                        statusCodeOK: true,
                        disposeData: true)
                    : await Http.Post(api, content,
                        timeoutSeconds: Math.Max(15, init.httptimeout),
                        headers: headers,
                        httpversion: init.httpversion,
                        statusCodeOK: true,
                        disposeData: true);
                string file = JavSubTo.SourceFile(json ?? "");
                if (!string.IsNullOrEmpty(file)) return file + "\n" + link;
            }
            catch { }
        }
        string chrome = await JavSubTo.ChromeSourceAsync(link, pageUrl);
        if (!string.IsNullOrEmpty(chrome)) return chrome + "\n" + link;
        return null;
    }

    [HttpGet]
    [Route("javsub/video")]
    [Route("javsub/video.m3u8")]
    [Route("javsub/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        string label = !string.IsNullOrEmpty(srv) ? srv : q;
        if (string.IsNullOrEmpty(label))
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : JavSubTo.SiteHost + "/" + (uri ?? "").Trim('/');

        string streamKey = ipkey($"javsub:stream:{pageUrl}:{label}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            var servers = await DetailServersAsync(uri);
            if (servers == null || servers.Count == 0)
                return OnError("stream_links", refresh_proxy: true);
            var pick = servers.FirstOrDefault(x =>
                string.Equals(x.label, label, StringComparison.OrdinalIgnoreCase));
            if (pick.label == null)
                return OnError("stream_links", refresh_proxy: true);
            packed = await ResolveOneAsync(pageUrl, pick.label, pick.link);
            if (string.IsNullOrEmpty(packed))
                return OnError("stream_links", refresh_proxy: true);
            hybridCache.Set(streamKey, packed, cacheTime(10));
        }

        {
            string link = packed, referer = JavSubTo.SiteHost + "/";
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

    async Task<bool> VerifyLinkAsync(string link, string referer)
    {
        if (string.IsNullOrEmpty(link)) return false;
        var headers = HeadersModel.Init(
            ("User-Agent", JavSubTo.ChromeUA),
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

    static bool IsMediaContentType(string ct)
    {
        if (string.IsNullOrEmpty(ct)) return false;
        ct = ct.ToLowerInvariant();
        return ct.Contains("mpegurl") || ct.Contains("mp2t") || ct.Contains("octet-stream")
            || ct.Contains("video/") || ct.Contains("application/vnd.apple");
    }

    #region info (popup long-press) — chip the loai/tag + "Xem them phim sex"
    static readonly Dictionary<string, (DateTime at, List<(string kind, string path, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("javsub/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = Json(new { items = new object[0], groups = new object[0] });

        string pageUrl = JavSubTo.SafeDetailUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return empty;

        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(pageUrl, out var hit) && DateTime.UtcNow - hit.at < InfoTtl)
                return InfoResult(hit.chips, hit.rel, uri);
        }

        string html = null;
        try { html = await GetPageAsync(pageUrl); }
        catch { }

        if (string.IsNullOrEmpty(html))
            return empty;

        var chips = JavSubTo.Chips(html);

        // "Video lien quan" nam trong AJAX Alpine.js, khong co trong HTML.
        var rel = new List<PlaylistItem>();
        string suggestUrl = JavSubTo.SuggestUrl(pageUrl);
        if (!string.IsNullOrEmpty(suggestUrl))
        {
            try
            {
                string json = await FetchJsonAsync(suggestUrl);
                rel = JavSubTo.Suggestions(json ?? "", "javsub/vidosik");
            }
            catch { }
        }

        lock (_infoCache)
        {
            _infoCache[pageUrl] = (DateTime.UtcNow, chips, rel);
            if (_infoCache.Count > 300)
            {
                var cutoff = DateTime.UtcNow - InfoTtl;
                foreach (string k in _infoCache.Where(x => x.Value.at < cutoff).Select(x => x.Key).ToList())
                    _infoCache.Remove(k);
            }
        }

        return InfoResult(chips, rel, uri);
    }

    ActionResult InfoResult(
        List<(string kind, string path, string name)> chips,
        List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        // Thu tu co dinh: dien vien -> the loai -> the -> lien quan.
        foreach (var (kind, title) in new[]
        {
            ("actor", "Diễn viên"), ("cat", "Thể loại"), ("tag", "Thẻ")
        })
        {
            var items = chips?.Where(x => x.kind == kind)
                .Select(x => new
                {
                    title = x.name,
                    url = $"{host}/javsub?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title, items });
        }

        if (rel != null && rel.Count > 0)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new
                    {
                        title = "Video liên quan",
                        plain = true,
                        url = $"{host}/javsub?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        // SISI hieu total_pages:0 = "con trang vo han" -> phai tra 1.
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = JavSubTo.SafeDetailUrl(rel);
        string suggestUrl = JavSubTo.SuggestUrl(pageUrl);
        if (string.IsNullOrEmpty(suggestUrl))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"javsub:rel:{pageUrl}"), 180, async e =>
        {
            string json = await FetchJsonAsync(suggestUrl);
            var playlists = JavSubTo.Suggestions(json ?? "", "javsub/vidosik");

            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null), total_pages: 1);
    }
    #endregion
}
