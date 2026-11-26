using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace FreePornVideos;

public class FreePornVideosController : BaseSisiController
{
    public FreePornVideosController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("fpv")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        // gop sort ve dung tap cua context TRUOC khi lap cache key
        sort = FreePornVideosTo.ClampSort(sort, search, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"fpv:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await FetchHtmlAsync(
                FreePornVideosTo.Uri(init.host, search, c, pg, sort));

            var playlists = FreePornVideosTo.Playlist("fpv/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
            {
                // trang cuoi / khong co ket qua -> success rong, khong 503
                if (pg > 1 || !string.IsNullOrWhiteSpace(search))
                    return e.Success((new List<PlaylistItem>(), 0));

                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));
            }

            return e.Success((playlists, FreePornVideosTo.Pages(html)));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(search, sort, c), total_pages: cache.Value.total_pages);
    }

    async Task<string> FetchHtmlAsync(string url)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", FreePornVideosTo.ChromeUA),
                ("Accept-Language", "vi,en;q=0.8"),
                ("Referer", FreePornVideosTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }

    // dong 1+2 dung lai moi request, dong 3+ cache 1 lan
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/fpv"
                }
            }
            : FreePornVideosTo.MenuHead(host, search, sort, c);

        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("fpv:taxonomy");

        if (!hybridCache.TryGetValue(key, out (List<(string name, string slug)> categories, List<(string name, string slug)> sites) tax)
            || tax.categories == null || tax.categories.Count == 0)
        {
            string catHtml = await FetchHtmlAsync(FreePornVideosTo.SiteHost + FreePornVideosTo.Lang + "/categories/");
            var categories = FreePornVideosTo.CategoryList(catHtml);

            string siteHtml = await FetchHtmlAsync(FreePornVideosTo.SiteHost + FreePornVideosTo.Lang + "/sites/");
            var sites = FreePornVideosTo.SiteList(siteHtml);

            int pages = FreePornVideosTo.SitePages(siteHtml);
            if (sites.Count > 0 && pages > 1)
            {
                var tasks = new List<Task<string>>();
                for (int p = 2; p <= pages; p++)
                    tasks.Add(FetchHtmlAsync(FreePornVideosTo.SiteHost + FreePornVideosTo.Lang + "/sites/" + p + "/"));

                var seen = new HashSet<string>(sites.Select(x => x.slug), StringComparer.OrdinalIgnoreCase);
                foreach (string html in await Task.WhenAll(tasks))
                {
                    if (string.IsNullOrEmpty(html))
                        continue;
                    foreach (var s in FreePornVideosTo.SiteList(html))
                        if (seen.Add(s.slug))
                            sites.Add(s);
                }
            }

            tax = (categories, sites);
            if (categories.Count > 0)
                hybridCache.Set(key, tax, cacheTime(720));
        }

        return FreePornVideosTo.Menu(host, tax.categories, tax.sites);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("fpv/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(uri))
            return OnError("stream_links", refresh_proxy: true);

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = FreePornVideosTo.SiteHost + pageUrl;

        var cache = await InvokeCacheResult(ipkey($"fpv:view:{pageUrl}"), 30, jsonContext.StreamItem, async e =>
        {
            string html = await FetchHtmlAsync(pageUrl);
            var qualitys = FreePornVideosTo.StreamLinks(html ?? "");

            if (qualitys == null || qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(new StreamItem() { qualitys = qualitys, recomends = FreePornVideosTo.PlaylistRelated("fpv/vidosik", html ?? "") });
        });

        if (related)
            return PlaylistResult(cache.Value?.recomends, cache.ISingleCache, null, total_pages: 1);

        return OnResult(cache);
    }

    #region info (long-press popup menu)
    [HttpGet]
    [Route("fpv/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.Contains("/videos/", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"fpv:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey, out (List<(string kind, string slug, string name)> chips, bool related) hit)
            && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.related, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : FreePornVideosTo.SiteHost + uri;
        string html = await FetchHtmlAsync(pageUrl);

        var chips = FreePornVideosTo.Chips(html ?? "");
        bool related = FreePornVideosTo.HasRelated(html);

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, (chips, related), cacheTime(360));

        return InfoResult(chips, related, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "model" => "Diễn viên",
        "site" => "Kênh",
        "network" => "Network",
        "category" => "Thể loại",
        _ => char.ToUpperInvariant(kind[0]) + kind.Substring(1)
    };

    static int KindRank(string kind) => kind switch
    {
        "model" => 0,
        "site" => 1,
        "network" => 2,
        "category" => 3,
        _ => 99
    };

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, bool related, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct().OrderBy(KindRank).ThenBy(x => x))
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = kind switch
                {
                    "model" => $"{host}/fpv?c={HttpUtility.UrlEncode("models/" + x.slug)}",
                    "site" => $"{host}/fpv?c={HttpUtility.UrlEncode("sites/" + x.slug)}",
                    "network" => $"{host}/fpv?c={HttpUtility.UrlEncode("networks/" + x.slug)}",
                    _ => $"{host}/fpv?c={HttpUtility.UrlEncode("categories/" + x.slug)}"
                }
            }).ToList();

            if (items.Count == 0)
                continue;

            groups.Add(new { title = KindLabel(kind), items });
        }

        if (related)
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
                        url = $"{host}/fpv?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"fpv:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? rel : FreePornVideosTo.SiteHost + rel;
            string html = await FetchHtmlAsync(url);

            var playlists = FreePornVideosTo.PlaylistRelated("fpv/vidosik", html ?? "");
            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null, related: true), total_pages: 1);
    }
    #endregion
}