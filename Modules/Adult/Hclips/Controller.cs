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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Hclips;

public class HclipsController : BaseSisiController
{
    public HclipsController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("hclips")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel);

        if (pg < 1) pg = 1;

        sort = HclipsTo.ClampSort(sort);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"hclips:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string url = string.IsNullOrWhiteSpace(search)
                ? HclipsTo.ListUrl(init.host, c, sort, pg)
                : HclipsTo.SearchUrl(init.host, search, pg);

            string json = await FetchAsync(url, HclipsTo.SiteHost + "/");
            var (playlists, pages) = HclipsTo.Playlist("hclips/vidosik", json ?? "");

            if (playlists == null || playlists.Count == 0)
            {
                // search khong co ket qua -> list rong, khong Fail
                if (pg > 1 || !string.IsNullOrWhiteSpace(search))
                    return e.Success((new List<PlaylistItem>(), 1));

                return e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success((playlists, pages < 1 ? 1 : pages));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(search, c, sort), total_pages: cache.Value.total_pages);
    }

    async Task<string> FetchAsync(string url, string referer)
    {
        try
        {
            string json = null;
            await httpHydra.GetSpan(url, span =>
            {
                json = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", HclipsTo.ChromeUA),
                ("Accept", "application/json, text/plain, */*"),
                ("Accept-Language", "en-US,en;q=0.9"),
                ("Referer", referer)
            ));

            return json;
        }
        catch
        {
            return null;
        }
    }

    async Task<List<MenuItem>> MenuAsync(string search, string c, string sort)
    {
        var res = HclipsTo.MenuHead(host, search, c, sort);
        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("hclips:taxonomy");

        if (!hybridCache.TryGetValue(key, out List<(string name, string slug)> categories)
            || categories == null || categories.Count == 0)
        {
            string json = await FetchAsync(HclipsTo.CategoriesUrl(HclipsTo.SiteHost), HclipsTo.SiteHost + "/");
            categories = HclipsTo.Categories(json ?? "");

            if (categories.Count > 0)
                hybridCache.Set(key, categories, cacheTime(720));
        }

        return HclipsTo.Menu(host, categories);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("hclips/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string videoId = VideoId(uri);
        if (string.IsNullOrEmpty(videoId))
            return OnError("stream_links", refresh_proxy: true);

        var cache = await InvokeCacheResult(ipkey($"hclips:view:{videoId}"), 30, jsonContext.StreamItem, async e =>
        {
            string json = await FetchAsync(HclipsTo.StreamUrl(HclipsTo.SiteHost, videoId), $"{HclipsTo.SiteHost}/videos/{videoId}/");
            var qualitys = HclipsTo.StreamLinks(HclipsTo.SiteHost, json ?? "");

            if (qualitys == null || qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            // "lien quan" -> playlist "tiep theo" trong player
            List<PlaylistItem> recomends = null;
            string relJson = await FetchAsync(HclipsTo.RelatedUrl(HclipsTo.SiteHost, videoId, 1), $"{HclipsTo.SiteHost}/videos/{videoId}/");
            (recomends, _) = HclipsTo.Playlist("hclips/vidosik", relJson ?? "");

            return e.Success(new StreamItem() { qualitys = qualitys, recomends = recomends });
        });

        if (related)
            return PlaylistResult(cache.Value?.recomends, cache.ISingleCache, null, total_pages: 1);

        return OnResult(cache);
    }

    #region info (long-press popup menu)
    [HttpGet]
    [Route("hclips/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        string videoId = VideoId(uri);
        if (string.IsNullOrEmpty(videoId))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"hclips:info:{videoId}");
        if (hybridCache.TryGetValue(cacheKey, out List<(string kind, string slug, string name)> hit)
            && hit != null && hit.Count > 0)
        {
            return InfoResult(hit, uri);
        }

        string json = await FetchAsync(HclipsTo.VideoInfoUrl(HclipsTo.SiteHost, videoId), $"{HclipsTo.SiteHost}/videos/{videoId}/");
        var chips = HclipsTo.Chips(json ?? "");

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, chips, cacheTime(360));

        return InfoResult(chips, uri);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, string uri)
    {
        var groups = new List<object>();

        var items = (chips ?? new List<(string kind, string slug, string name)>())
            .Where(x => x.kind == "category")
            .Select(x => new
            {
                title = x.name,
                url = $"{host}/hclips?c={HttpUtility.UrlEncode("categories/" + x.slug)}"
            }).ToList();

        if (items.Count > 0)
            groups.Add(new { title = "Thể loại", items });

        if (!string.IsNullOrEmpty(uri))
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
                        url = $"{host}/hclips?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel)
    {
        string videoId = VideoId(rel);
        if (string.IsNullOrEmpty(videoId))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"hclips:rel:{videoId}"), 180, async e =>
        {
            string json = await FetchAsync(HclipsTo.RelatedUrl(HclipsTo.SiteHost, videoId, 1), $"{HclipsTo.SiteHost}/videos/{videoId}/");
            var (playlists, _) = HclipsTo.Playlist("hclips/vidosik", json ?? "");
            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null), total_pages: 1);
    }
    #endregion

    // uri = "/videos/18145559/tarzan-x-shame-of-jane/" hoac URL tuyet doi
    static string VideoId(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !uri.Contains("/videos/", StringComparison.OrdinalIgnoreCase))
            return null;

        var m = Regex.Match(uri, "/videos/(\\d{3,})/");
        return m.Success ? m.Groups[1].Value : null;
    }
}
