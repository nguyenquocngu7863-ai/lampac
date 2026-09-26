using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace Tazzly;

public class TazzlyController : BaseSisiController
{
    public TazzlyController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("tazzly")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // Trang chu phan trang bang JS nen phai dung API; search + danh muc
        // dung ?page=N cua HTML.
        bool api = TazzlyTo.IsApi(search, c);
        string url = api ? TazzlyTo.ApiLatest(init.host, pg) : TazzlyTo.Uri(init.host, search, c, pg);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(ipkey($"tazzly:{search}:{c}:{pg}"), 10, async e =>
        {
            int total_pages = 0;
            List<PlaylistItem> playlists;

            if (api)
            {
                string json = await GetJsonAsync(url);
                playlists = TazzlyTo.PlaylistFromJson("tazzly/vidosik", json, out total_pages);
            }
            else
            {
                string page = await GetPageAsync(url);
                playlists = TazzlyTo.PlaylistFromHtml("tazzly/vidosik", page, out total_pages);
            }

            if (playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: false);

            // Trang chi co 1 trang thi site khong render khối pagination => 0.
            // 0 lai bi Lampa hieu la "load vo han", nen san 1.
            if (total_pages < 1)
                total_pages = 1;

            return e.Success((playlists, total_pages));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            TazzlyTo.Menu(host),
            total_pages: cache.Value.total_pages
        );
    }

    async Task<string> GetPageAsync(string url)
    {
        string page = null;
        await httpHydra.GetSpan(url, span => page = span.ToString(), addheaders: PageHeaders());

        if (string.IsNullOrEmpty(page))
        {
            page = await Http.Get(
                url,
                timeoutSeconds: Math.Max(20, init.httptimeout),
                httpversion: init.httpversion,
                proxy: proxy,
                headers: PageHeaders());
        }

        return page;
    }

    async Task<string> GetJsonAsync(string url)
    {
        string json = null;
        await httpHydra.GetSpan(url, span => json = span.ToString(), addheaders: ApiHeaders());

        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
        {
            json = await Http.Get(
                url,
                timeoutSeconds: Math.Max(20, init.httptimeout),
                httpversion: init.httpversion,
                proxy: proxy,
                headers: ApiHeaders());
        }

        return json;
    }

    async Task<(string m3u8, bool userch)> ResolveLinksAsync(string uri)
    {
        string pageUrl = TazzlyTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return (null, false);

        string memKey = ipkey($"tazzly:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out (string m3u8, bool userch) cache) && !string.IsNullOrEmpty(cache.m3u8))
            return cache;

        SemaphorManager semaphore = null;
        if (rch?.enable != true)
        {
            semaphore = new SemaphorManager($"tazzly:view:{pageUrl}", TimeSpan.FromSeconds(30));
            if (!await semaphore.WaitAsync())
                return (null, false);
        }

        try
        {
            if (hybridCache.TryGetValue(memKey, out (string m3u8, bool userch) current) && !string.IsNullOrEmpty(current.m3u8))
                return current;

            string page = await GetPageAsync(pageUrl);
            string player = TazzlyTo.EmbedPlayer(page, pageUrl);
            if (string.IsNullOrEmpty(player))
                return (null, false);

            string playerHtml = await GetPageAsync(player);
            string m3u8 = TazzlyTo.M3u8Url(playerHtml);
            if (string.IsNullOrEmpty(m3u8))
                return (null, false);

            var value = (m3u8, rch?.enable == true);
            proxyManager?.Success();
            hybridCache.Set(memKey, value, cacheTime(20));
            return value;
        }
        finally
        {
            semaphore?.Release();
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("tazzly/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var (m3u8, userch) = await ResolveLinksAsync(uri);
        if (string.IsNullOrEmpty(m3u8))
            return OnError("stream_links", refresh_proxy: true);

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = m3u8
        };

        if (userch)
            return OnResult(links);

        return Json(links.ToDictionary(k => k.Key, pair => StreamRoute(uri, pair.Value)));
    }

    [HttpGet]
    [Route("tazzly/video")]
    [Route("tazzly/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (m3u8, _) = await ResolveLinksAsync(uri);
        if (string.IsNullOrEmpty(m3u8) || (!string.IsNullOrEmpty(q) && q != "1"))
            return OnError("stream_links", refresh_proxy: true);

        return Redirect(HostStreamProxy(m3u8, StreamHeaders()));
    }

    [HttpGet]
    [Route("tazzly/strem")]
    async public Task<ActionResult> Strem(string link, string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(link))
        {
            var (m3u8, _) = await ResolveLinksAsync(uri);
            if (string.IsNullOrEmpty(m3u8))
                return OnError("stream_links", refresh_proxy: true);

            link = m3u8;
        }

        return Redirect(HostStreamProxy(link, StreamHeaders()));
    }

    string StreamRoute(string uri, string link)
    {
        return $"{host}/tazzly/video.m3u8?uri={HttpUtility.UrlEncode(uri)}";
    }

    static IReadOnlyList<HeadersModel> PageHeaders()
    {
        return HeadersModel.Init(
            ("User-Agent", TazzlyTo.ChromeUA),
            ("Referer", TazzlyTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
        );
    }

    static IReadOnlyList<HeadersModel> ApiHeaders()
    {
        return HeadersModel.Init(
            ("User-Agent", TazzlyTo.ChromeUA),
            ("Referer", TazzlyTo.SiteHost + "/"),
            ("Accept", "application/json, text/plain, */*")
        );
    }

    // m3u8 nam tren embed host va tra 403 neu thieu Referer.
    static IReadOnlyList<HeadersModel> StreamHeaders()
    {
        return HeadersModel.Init(
            ("User-Agent", TazzlyTo.ChromeUA),
            ("Referer", TazzlyTo.EmbedHost + "/"),
            ("Origin", TazzlyTo.EmbedHost),
            ("Accept", "*/*")
        );
    }
}
