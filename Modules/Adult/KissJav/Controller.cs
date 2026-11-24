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

namespace KissJav;

public class KissJavController : BaseSisiController
{
    public KissJavController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("kissjav")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // clamp TRUOC cache key (sort la -> mac dinh cua context, khong sinh entry rac)
        sort = KissJavTo.ClampSort(sort, search, c);

        // site BO QUA sort_by o trang >= 2 -> dang sort thi chi co trang 1
        if (!string.IsNullOrEmpty(sort) && pg > 1)
            pg = 1;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        var menuTask = MenuAsync(search, c, sort);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"kissjav:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string url = !string.IsNullOrWhiteSpace(search)
                ? KissJavTo.SearchUrl(init.host, search, sort, pg)
                : KissJavTo.ListUrl(init.host, c, sort, pg);

            string html = await GetPageAsync(url);

            if (KissJavTo.IsBlocked(html))
                return e.Fail("playlists", refresh_proxy: true);

            var playlists = KissJavTo.Playlist("kissjav/vidosik", html);
            int total = string.IsNullOrEmpty(sort) ? KissJavTo.Pages(html) : 1;
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
            total_pages: cache.Value.total_pages);
    }

    // ================= Menu (danh muc + sort) =================
    async Task<List<MenuItem>> MenuAsync(string search, string c, string sort, bool allowSort = true)
    {
        string memKey = ipkey("kissjav:nav:v1");
        var nav = KissJavTo.FallbackNav;

        if (hybridCache.TryGetValue(memKey, out List<(string path, string name)> hit) && hit != null && hit.Count > 0)
            nav = hit;
        else
        {
            try
            {
                string html = await GetPageAsync((string.IsNullOrEmpty(init.host) ? KissJavTo.SiteHost : init.host).TrimEnd('/') + "/");
                var t = KissJavTo.Nav(html);
                if (t.Count > 0)
                {
                    nav = t;
                    hybridCache.Set(memKey, nav, cacheTime(720), true);
                }
            }
            catch { }
        }

        return KissJavTo.Menu(host, nav, search, c, sort, allowSort);
    }

    // ================= Vidosik =================
    [HttpGet, Staticache(manually: true)]
    [Route("kissjav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string stream = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(stream))
            return OnError("stream_links", refresh_proxy: true);

        return Json(new Dictionary<string, string>()
        {
            ["MP4"] = $"{host}/kissjav/video.mp4?uri={HttpUtility.UrlEncode(uri)}"
        });
    }

    // ================= Video =================
    // MP4 progressive (/get_file/...) -> proxy (can referer kissjav.li)
    [HttpGet]
    [Route("kissjav/video.mp4")]
    async public Task<ActionResult> Video(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string stream = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(stream))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", KissJavTo.ChromeUA),
            ("Referer", KissJavTo.SiteHost + "/")
        ));

        return Redirect(HostStreamProxy(stream, direct));
    }

    // ================= Resolve =================
    async Task<string> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : KissJavTo.SiteHost + "/" + uri.TrimStart('/');

        string memKey = ipkey($"kissjav:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        string html = await GetPageAsync(pageUrl);
        string stream = KissJavTo.PlayUrl(html);
        if (string.IsNullOrEmpty(stream))
            return null;

        hybridCache.Set(memKey, stream, cacheTime(120));
        return stream;
    }

    #region popup (long-press)
    [HttpGet]
    [Route("kissjav/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = new { items = new object[0], groups = new object[0] };

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string path = KissJavTo.DetailPath(uri);
        if (path == null)
            return Json(empty);

        string cacheKey = ipkey($"kissjav:info:{path}");
        if (!hybridCache.TryGetValue(cacheKey, out List<(string path, string name)> chips) || chips == null)
        {
            string html = await GetPageAsync(KissJavTo.SiteHost + path);
            chips = KissJavTo.Chips(html);
            if (chips.Count == 0)
                return Json(empty);
            hybridCache.Set(cacheKey, chips, cacheTime(360));
        }

        var groups = new List<object>();

        var items = chips.Select(x => new
        {
            title = x.name,
            url = KissJavTo.MenuUrl(host, x.path)
        }).ToList();

        if (items.Count > 0)
            groups.Add(new { title = "Thể loại", items });

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Phim liên quan",
                    plain = true,
                    url = $"{host}/kissjav?rel={HttpUtility.UrlEncode(uri)}"
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult(
            ipkey($"kissjav:rel:{rel}"),
            180, jsonContext.ListPlaylistItem, async e =>
        {
            string path = KissJavTo.DetailPath(rel);
            if (path == null)
                return e.Success(new List<PlaylistItem>());

            string html = await GetPageAsync(KissJavTo.SiteHost + path);
            var playlists = KissJavTo.RelatedPlaylist(
                "kissjav/vidosik", KissJavTo.RelatedBlock(html));

            return e.Success(playlists ?? new List<PlaylistItem>());
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        if (cache.Value == null || cache.Value.Count == 0)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        return PlaylistResult(cache, await MenuAsync(null, null, null, allowSort: false), total_pages: 1);
    }
    #endregion

    // ================= Fetch =================
    async Task<string> GetPageAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var headers = HeadersModel.Init(
            ("User-Agent", KissJavTo.ChromeUA),
            ("Accept-Language", "en-US,en;q=0.9,vi;q=0.8"),
            ("Referer", KissJavTo.SiteHost + "/"));

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
                if (!string.IsNullOrEmpty(a) && a.Length >= 1000) break;
                if (!string.IsNullOrEmpty(b) && b.Length >= 1000) break;
                if (t1.IsCompleted && t2.IsCompleted) break;
                await Task.Delay(200);
            }

            last = !string.IsNullOrEmpty(a) && a.Length >= 1000 ? a : b;

            if (!KissJavTo.IsBlocked(last))
                return last;

            await Task.Delay(1000);
        }

        return last;
    }
}
