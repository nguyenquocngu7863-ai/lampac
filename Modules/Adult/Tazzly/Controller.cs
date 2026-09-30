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
                // Chay song song API + HTML, duong nao ra phim thi lay.
                // Noi tiep (API treo 14s roi HTML 12s = 26s) vuot timeout app.
                // Song song thi tran = duong cham nhat (~14s), thuong ~2s.
                var jsonTask = GetJsonAsync(url);
                var htmlTask = GetPageAsync(TazzlyTo.Uri(init.host, null, null, pg));
                await Task.WhenAll(jsonTask, htmlTask);

                playlists = TazzlyTo.PlaylistFromJson("tazzly/vidosik", await jsonTask, out total_pages);

                if (playlists.Count == 0)
                    playlists = TazzlyTo.PlaylistFromHtml("tazzly/vidosik", await htmlTask, out total_pages);
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
        // Site treo that thuong o ket noi dau. httpHydra KHONG follow
        // redirect: trang 301 (162 byte) khong trong nen roi dung vao nhanh
        // nay, parse ra 0 card. Trang that ~50-70KB, nen chi giu ket qua
        // dai hon 1KB; chay song song 2 duong theo vong 4s trong tran 14s.
        var deadline = DateTime.UtcNow.AddSeconds(14);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            string page = await RaceGetAsync(url, PageHeaders(),
                (int)Math.Ceiling(Math.Min(4, left)));

            if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                return page;
        }

        return null;
    }

    async Task<string> RaceGetAsync(string url,
        IReadOnlyList<HeadersModel> headers, int seconds)
    {
        string a = null, b = null;

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

    async Task<string> GetJsonAsync(string url)
    {
        // Giong GetPageAsync nhung chi nhan JSON that (bat dau bang `{`).
        // Trang 301 cua httpHydra (khong follow redirect) bi loai ngay.
        var deadline = DateTime.UtcNow.AddSeconds(14);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            string json = await RaceGetAsync(url, ApiHeaders(),
                (int)Math.Ceiling(Math.Min(4, left)));

            if (!string.IsNullOrEmpty(json) && json.TrimStart().StartsWith("{"))
                return json;
        }

        return null;
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
