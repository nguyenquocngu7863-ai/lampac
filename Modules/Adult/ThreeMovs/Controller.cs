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

namespace ThreeMovs;

public class ThreeMovsController : BaseSisiController
{
    public ThreeMovsController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("threemovs")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        // site khong phan trang search -> luon page 1
        bool isSearch = !string.IsNullOrWhiteSpace(search);
        if (isSearch)
            pg = 1;

        sort = ThreeMovsTo.ClampSort(sort, search, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"threemovs:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await FetchHtmlAsync(
                ThreeMovsTo.Uri(init.host, search, c, pg, sort));

            var playlists = ThreeMovsTo.Playlist("threemovs/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
            {
                if (pg > 1 || isSearch)
                    return e.Success((new List<PlaylistItem>(), 0));

                return e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success((playlists, isSearch ? 1 : ThreeMovsTo.Pages(html)));
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
                ("User-Agent", ThreeMovsTo.ChromeUA),
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                ("Accept-Language", "en-US,en;q=0.9"),
                ("Referer", ThreeMovsTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/threemovs"
                }
            }
            : ThreeMovsTo.MenuHead(host, search, sort, c);

        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("threemovs:taxonomy");

        if (!hybridCache.TryGetValue(key, out (List<(string name, string slug)> categories, List<(string name, string slug)> pornstars) tax)
            || tax.categories == null || tax.categories.Count == 0)
        {
            string catHtml = await FetchHtmlAsync(ThreeMovsTo.SiteHost + "/categories/");
            var categories = ThreeMovsTo.CategoryList(catHtml);

            // /pornstars/ co nhieu trang -> lay 3 trang dau (~90 muc)
            var psTasks = new List<Task<string>>();
            for (int p = 1; p <= 3; p++)
                psTasks.Add(FetchHtmlAsync(ThreeMovsTo.SiteHost + "/pornstars/" + (p > 1 ? p + "/" : "")));

            var pornstars = new List<(string name, string slug)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string html in await Task.WhenAll(psTasks))
            {
                if (string.IsNullOrEmpty(html))
                    continue;
                foreach (var ps in ThreeMovsTo.PornstarList(html))
                    if (seen.Add(ps.slug))
                        pornstars.Add(ps);
            }

            tax = (categories, pornstars);
            if (categories.Count > 0)
                hybridCache.Set(key, tax, cacheTime(720));
        }

        return ThreeMovsTo.Menu(host, tax.categories, tax.pornstars);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("threemovs/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(uri))
            return OnError("stream_links", refresh_proxy: true);

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = ThreeMovsTo.SiteHost + pageUrl;

        var cache = await InvokeCacheResult(ipkey($"threemovs:view:{pageUrl}"), 30, jsonContext.StreamItem, async e =>
        {
            string html = await FetchHtmlAsync(pageUrl);
            var qualitys = ThreeMovsTo.StreamLinks(html ?? "");

            if (qualitys == null || qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(new StreamItem() { qualitys = qualitys, recomends = ThreeMovsTo.PlaylistRelated("threemovs/vidosik", html ?? "") });
        });

        if (related)
            return PlaylistResult(cache.Value?.recomends, cache.ISingleCache, null, total_pages: 1);

        return OnResult(cache);
    }

    #region info (long-press popup menu)
    [HttpGet]
    [Route("threemovs/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.Contains("/videos/", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"threemovs:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey, out (List<(string kind, string slug, string name)> chips, bool related) hit)
            && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.related, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : ThreeMovsTo.SiteHost + uri;
        string html = await FetchHtmlAsync(pageUrl);

        var chips = ThreeMovsTo.Chips(html ?? "");
        bool related = ThreeMovsTo.HasRelated(html);

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, (chips, related), cacheTime(360));

        return InfoResult(chips, related, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "model" => "Diễn viên",
        "category" => "Thể loại",
        "tag" => "Thẻ",
        _ => char.ToUpperInvariant(kind[0]) + kind.Substring(1)
    };

    static int KindRank(string kind) => kind switch
    {
        "model" => 0,
        "category" => 1,
        "tag" => 2,
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
                    "model" => $"{host}/threemovs?c={HttpUtility.UrlEncode("pornstars/" + x.slug)}",
                    "tag" => $"{host}/threemovs?c={HttpUtility.UrlEncode("tags/" + x.slug)}",
                    _ => $"{host}/threemovs?c={HttpUtility.UrlEncode("categories/" + x.slug)}"
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
                        url = $"{host}/threemovs?rel={HttpUtility.UrlEncode(uri)}"
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
            ipkey($"threemovs:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? rel : ThreeMovsTo.SiteHost + rel;
            string html = await FetchHtmlAsync(url);

            var playlists = ThreeMovsTo.PlaylistRelated("threemovs/vidosik", html ?? "");
            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null, related: true), total_pages: 1);
    }
    #endregion
}
