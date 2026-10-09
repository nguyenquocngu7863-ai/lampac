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

namespace Mbbg;

public class MbbgController : BaseSisiController
{
    public MbbgController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("mbbg")]
    async public Task<ActionResult> Index(
        string search, string c, int pg = 1, string q = null)
        => await Core(search ?? q, c, pg);

    async Task<ActionResult> Core(string search, string c, int pg)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        var cache = await InvokeCacheResult(
            ipkey($"mbbg:{search}:{c}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(MbbgTo.Uri(init.host, search, c, pg));

            var playlists = string.IsNullOrEmpty(html)
                ? null
                : MbbgTo.Playlist("mbbg/vidosik", html);

            // Tim khong co ket qua -> SUCCESS + list rong, khong phai loi.
            if (playlists != null && playlists.Count == 0)
                return e.Success(playlists);

            if (playlists == null && !string.IsNullOrWhiteSpace(search))
                return e.Success(new List<PlaylistItem>());

            if (playlists == null)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync());
    }

    // App CACHE response DAU CA PHIEN -> phai tra menu day du ngay lan dau.
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 8_000;

    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("mbbg:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit)
            && hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        if (!await menuLock.WaitAsync(3000))
            return Fallback(hostLocal);

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2)
                && hit2 != null && hit2.Count > 0)
                return hit2;

            var build = BuildMenuAsync(hostLocal, memKey);

            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return Fallback(hostLocal);

            return await build;
        }
        finally
        {
            menuLock.Release();
        }
    }

    List<MenuItem> Fallback(string hostLocal)
        => MbbgTo.Menu(hostLocal, MbbgTo.FallbackGenres, MbbgTo.FallbackTags);

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        var genres = MbbgTo.FallbackGenres;
        var tags = MbbgTo.FallbackTags;

        try
        {
            // Trang chu mot lan du ca 7 danh muc (truoc `<article` dau)
            // va 50 tu khoa (tag cloud). Khong co trang index rieng.
            string html = await GetPageAsync(MbbgTo.SiteHost + "/");
            if (!string.IsNullOrEmpty(html))
            {
                var g = MbbgTo.Genres(html);
                var t = MbbgTo.Tags(html);

                if (g.Count > 0) genres = g;
                if (t.Count > 0) tags = t;
            }
        }
        catch { }

        var menu = MbbgTo.Menu(hostLocal, genres, tags);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            string page = await RaceGetAsync(
                url, (int)Math.Ceiling(Math.Min(4, left)));

            if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                return page;

            await Task.Delay(1000);
        }

        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;

        var headers = HeadersModel.Init(
            ("User-Agent", MbbgTo.ChromeUA),
            ("Referer", MbbgTo.SiteHost + "/"));

        var t1 = Task.Run(async () =>
        {
            try
            {
                await httpHydra.GetSpan(url, span =>
                {
                    a = span.ToString();
                }, addheaders: headers);
            }
            catch { }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                b = await Http.Get(
                    url,
                    timeoutSeconds: Math.Max(6, seconds),
                    httpversion: init.httpversion,
                    proxy: proxy,
                    headers: headers);
            }
            catch { }
        });

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrEmpty(a))
                return a;

            if (!string.IsNullOrEmpty(b))
                return b;

            if (t1.IsCompleted && t2.IsCompleted)
                break;

            await Task.Delay(200);
        }

        return !string.IsNullOrEmpty(a) ? a : b;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("mbbg/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // HLS di route .m3u8, MP4 di route .mp4 (app bao "no EXTM3U delimiter"
        // neu lan lon).
        return Json(links.ToDictionary(k => k.Key, k =>
        {
            string v = k.Value;
            int nl = v.IndexOf('\n');
            if (nl > 0) v = v.Substring(0, nl);
            string route = v.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0
                || v.IndexOf("/hls/", StringComparison.OrdinalIgnoreCase) >= 0
                ? "video.m3u8" : "video.mp4";
            return $"{host}/mbbg/{route}?uri={HttpUtility.UrlEncode(uri)}"
                + $"&q={HttpUtility.UrlEncode(k.Key)}";
        }));
    }

    // Cong thuc F14 (skill lampac-deobfuscate):
    //   trang chi tiet -> getplayer (ma blogger) -> batchexecute -> googlevideo
    // URL tra ve gan IP client va het han ~8h -> cache 5 phut, phai di qua proxy.
    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : MbbgTo.SiteHost + "/" + uri.Trim('/');

        string memKey = ipkey($"mbbg:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache)
            && cache != null && cache.Count > 0)
            return cache;

        string html = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return null;

        string nonce = MbbgTo.Nonce(html);
        string sources = MbbgTo.Sources(html);

        if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(sources))
            return null;

        // [1] getplayer — POST dung host cua detail (nonce gan voi host,
        // do 2026-10-09: POST ve host cu an 400 "0").
        string ajaxBase = MbbgTo.HostOf(pageUrl);
        var siteHeaders = HeadersModel.Init(
            ("User-Agent", MbbgTo.ChromeUA),
            ("Referer", ajaxBase + "/"));

        string form = "action=getplayer"
            + "&source=" + HttpUtility.UrlEncode(sources)
            + "&nonce=" + HttpUtility.UrlEncode(nonce)
            + "&server=1";

        string aj = await Http.Post(
            ajaxBase + "/wp-admin/admin-ajax.php",
            form,
            headers: siteHeaders,
            timeoutSeconds: 15,
            httpversion: init.httpversion,
            proxy: proxy);

        string token = MbbgTo.Token(MbbgTo.FileFrom(aj));
        if (string.IsNullOrEmpty(token))
        {
            // [1b] mirror qooglevideo (Blogger goc chet): file la iframe mirror
            // co san sources (HLS xvideos-cdn + MP4). Do 2026-10-09.
            string frame = MbbgTo.FileFrom(aj);
            if (!string.IsNullOrEmpty(frame)
                && frame.IndexOf("qooglevideo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var qh = await GetMirrorAsync(frame, pageUrl);
                var qs = MbbgTo.QoogleSources(qh ?? "");
                if (qs.Count > 0)
                {
                    var ordered = qs.OrderBy(kv =>
                        kv.Value.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                        .ToDictionary(kv => kv.Key, kv => kv.Value + "\n" + frame);
                    hybridCache.Set(memKey, ordered, cacheTime(5));
                    return ordered;
                }
            }
            return null;
        }

        // [2] batchexecute — doi ma token thanh URL googlevideo (MP4).
        // PHAI dung HTTP thuong: impersonate bi 403 (da do, xem F14).
        var blogHeaders = HeadersModel.Init(
            ("User-Agent", MbbgTo.ChromeUA),
            ("Referer", "https://www.blogger.com/"),
            ("X-Same-Domain", "1"));

        string resp = await Http.Post(
            MbbgTo.BatchUrl,
            MbbgTo.BatchBody(token),
            headers: blogHeaders,
            timeoutSeconds: 15,
            httpversion: init.httpversion,
            proxy: proxy);

        var links = MbbgTo.Formats(resp);
        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(5));
        return links;
    }

    async Task<string> GetMirrorAsync(string url, string referer)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", MbbgTo.ChromeUA),
            ("Referer", referer));
        try
        {
            string h = await Http.Get(url, timeoutSeconds: 12,
                headers: headers, httpversion: init.httpversion);
            if (!string.IsNullOrEmpty(h)) return h;
        }
        catch { }
        try
        {
            return await Http.Get(url, timeoutSeconds: 12,
                headers: headers, proxy: proxy, httpversion: init.httpversion);
        }
        catch { return null; }
    }

    [HttpGet]
    [Route("mbbg/video")]
    [Route("mbbg/video.m3u8")]
    [Route("mbbg/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || string.IsNullOrEmpty(q)
            || !links.TryGetValue(q, out string packed) || string.IsNullOrEmpty(packed))
            return OnError("stream_links", refresh_proxy: true);

        // Pack "url\nreferer" (mirror); Blogger cu chi co url tran.
        string link = packed, referer = "https://www.blogger.com/";
        int nl = packed.IndexOf('\n');
        if (nl > 0)
        {
            link = packed.Substring(0, nl);
            referer = packed.Substring(nl + 1);
        }

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", MbbgTo.ChromeUA),
            ("Referer", referer)));

        return Redirect(HostStreamProxy(link, direct));
    }
}
