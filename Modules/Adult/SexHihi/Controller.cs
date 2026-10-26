using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace SexHihi;

public class SexHihiController : BaseSisiController
{
    public SexHihiController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sexhihi")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        var menuTask = MenuAsync();

        // cache key phai du search/c/pg (checklist 9e) + version de doi hinh
        // thay doi khong doc nham cache cu.
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"sexhihi:v1:{search}:{c}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(ShhTo.Uri(init.host, search, c, pg));

            if (ShhTo.IsBlocked(html))
                return e.Fail("playlists", refresh_proxy: true);

            var playlists = ShhTo.Playlist("sexhihi/vidosik", html);

            // Site tra trang rong khi khong co ket qua -> SUCCESS + list rong
            // (PlaylistResult se tra 503 neu list rong, nen phai Success).
            return e.Success((playlists, ShhTo.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await menuTask,
            total_pages: cache.Value.total_pages);
    }

    // ================= Menu (2 tang) =================
    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("sexhihi:menu:v2");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        var tax = ShhTo.FallbackTax;

        try
        {
            // Nav day du (the loai + quoc gia) chi co o TRANG CHU "/" —
            // trang danh sach /danh-sach/phim-moi khong co link quoc-gia.
            string homeUrl = (string.IsNullOrEmpty(init.host) ? ShhTo.SiteHost : init.host).TrimEnd('/') + "/";
            string html = await GetPageAsync(homeUrl);
            if (!string.IsNullOrEmpty(html))
            {
                var t = ShhTo.Taxonomy(html);
                if (t.Count > 0)
                    tax = t;
            }
        }
        catch { }

        var menu = ShhTo.Menu(host, tax);
        hybridCache.Set(memKey, menu, cacheTime(720), true);

        return menu;
    }

    // ================= Vidosik =================
    [HttpGet, Staticache(manually: true)]
    [Route("sexhihi/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/sexhihi/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    // ================= Video =================
    [HttpGet]
    [Route("sexhihi/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || string.IsNullOrEmpty(q) ||
            !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", ShhTo.ChromeUA),
            ("Referer", ShhTo.SiteHost + "/")
        ));

        return Redirect(HostStreamProxy(link, direct));
    }

    // ================= Resolve =================
    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : ShhTo.SiteHost.TrimEnd('/') + "/" + uri.TrimStart('/');

        string memKey = ipkey($"sexhihi:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        string html = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return null;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (label, type, link) in ShhTo.Servers(html))
        {
            string key = label;
            int n = 2;
            while (links.ContainsKey(key))
                key = label + " " + (n++);

            links[key] = link;
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    // ================= Fetch =================
    // Site hay tra trang Cloudflare/loi -> thu lai 2 vong (2 nguon fetch song song).
    async Task<string> GetPageAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var headers = HeadersModel.Init(
            ("User-Agent", ShhTo.ChromeUA),
            ("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8"),
            ("Referer", ShhTo.SiteHost + "/"));

        string last = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string a = null, b = null;

            var t1 = Task.Run(async () =>
            {
                try
                {
                    await httpHydra.GetSpan(url, span => { a = span.ToString(); }, addheaders: headers);
                }
                catch { }
            });

            var t2 = Task.Run(async () =>
            {
                try
                {
                    b = await Http.Get(
                        url,
                        timeoutSeconds: 12,
                        httpversion: init.httpversion,
                        proxy: proxy,
                        headers: headers);
                }
                catch { }
            });

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (!string.IsNullOrEmpty(a) && a.Length >= 1000)
                    break;

                if (!string.IsNullOrEmpty(b) && b.Length >= 1000)
                    break;

                if (t1.IsCompleted && t2.IsCompleted)
                    break;

                await Task.Delay(200);
            }

            last = !string.IsNullOrEmpty(a) && a.Length >= 1000 ? a : b;

            if (!ShhTo.IsBlocked(last))
                return last;

            await Task.Delay(1200);
        }

        return last;
    }
}
