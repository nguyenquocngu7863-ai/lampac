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

namespace Youporn;

public class YoupornController : BaseSisiController
{
    public YoupornController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("youporn")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel);

        if (pg < 1)
            pg = 1;

        sort = YoupornTo.ClampSort(sort);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"youporn:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await FetchHtmlAsync(YoupornTo.ListUrl(init.host, search, c, sort, pg));

            var playlists = string.IsNullOrEmpty(html)
                ? null
                : YoupornTo.Playlist("youporn/vidosik", html);

            if (playlists == null || playlists.Count == 0)
            {
                if (pg > 1 || !string.IsNullOrWhiteSpace(search))
                    return e.Success((new List<PlaylistItem>(), 1));

                return e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success((playlists, YoupornTo.TotalPages(html)));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(search, c, sort), total_pages: cache.Value.total_pages);
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
                ("User-Agent", YoupornTo.ChromeUA),
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                ("Accept-Language", "en-US,en;q=0.9"),
                ("Referer", YoupornTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }

    async Task<string> FetchJsonAsync(string url, string referer)
    {
        try
        {
            string json = null;
            await httpHydra.GetSpan(url, span =>
            {
                json = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", YoupornTo.ChromeUA),
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
        var res = YoupornTo.MenuHead(host, search, c, sort);
        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("youporn:taxonomy");

        if (!hybridCache.TryGetValue(key, out List<(string name, string slug)> categories)
            || categories == null || categories.Count == 0)
        {
            string html = await FetchHtmlAsync(YoupornTo.CategoriesUrl(YoupornTo.SiteHost));
            categories = YoupornTo.Categories(html ?? "");

            if (categories.Count > 0)
                hybridCache.Set(key, categories, cacheTime(720));
        }

        return YoupornTo.Menu(host, categories);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("youporn/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string videoId = VideoId(uri);
        if (string.IsNullOrEmpty(videoId))
            return OnError("stream_links", refresh_proxy: true);

        var cache = await InvokeCacheResult(ipkey($"youporn:view:{videoId}"), 20, jsonContext.StreamItem, async e =>
        {
            string watchUrl = YoupornTo.WatchUrl(YoupornTo.SiteHost, videoId);
            string html = await FetchHtmlAsync(watchUrl);

            var (mp4, hls) = YoupornTo.StreamMetas(html ?? "");
            if (string.IsNullOrEmpty(mp4) && string.IsNullOrEmpty(hls))
                return e.Fail("stream_links", refresh_proxy: true);

            var qualitys = new Dictionary<string, string>();

            foreach (string meta in new[] { mp4, hls })
            {
                if (string.IsNullOrEmpty(meta))
                    continue;

                string json = await FetchJsonAsync(meta, watchUrl);
                qualitys = YoupornTo.Qualities(json ?? "");

                if (qualitys.Count > 0)
                    break;
            }

            if (qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(new StreamItem() { qualitys = qualitys });
        });

        return OnResult(cache);
    }

    #region info (long-press popup menu)
    [HttpGet]
    [Route("youporn/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        string videoId = VideoId(uri);
        if (string.IsNullOrEmpty(videoId))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"youporn:info:{videoId}");
        if (hybridCache.TryGetValue(cacheKey, out List<(string kind, string slug, string name)> hit)
            && hit != null && hit.Count > 0)
        {
            return InfoResult(hit, uri);
        }

        string html = await FetchHtmlAsync(YoupornTo.WatchUrl(YoupornTo.SiteHost, videoId));
        var chips = YoupornTo.Chips(html ?? "");

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, chips, cacheTime(360));

        return InfoResult(chips, uri);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, string uri)
    {
        var groups = new List<object>();

        var cats = (chips ?? new List<(string kind, string slug, string name)>())
            .Where(x => x.kind == "category")
            .Select(x => new
            {
                title = x.name,
                url = $"{host}/youporn?c={HttpUtility.UrlEncode("category/" + x.slug)}"
            }).ToList();

        if (cats.Count > 0)
            groups.Add(new { title = "Thể loại", items = cats });

        var tags = (chips ?? new List<(string kind, string slug, string name)>())
            .Where(x => x.kind == "tag")
            .Select(x => new
            {
                title = x.name,
                url = $"{host}/youporn?c={HttpUtility.UrlEncode("porntags/" + x.slug)}"
            }).ToList();

        if (tags.Count > 0)
            groups.Add(new { title = "Thẻ", items = tags });

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
                        url = $"{host}/youporn?rel={HttpUtility.UrlEncode(uri)}"
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
            ipkey($"youporn:rel:{videoId}"), 180, async e =>
        {
            string html = await FetchHtmlAsync(YoupornTo.WatchUrl(YoupornTo.SiteHost, videoId));
            var playlists = string.IsNullOrEmpty(html)
                ? new List<PlaylistItem>()
                : YoupornTo.Playlist("youporn/vidosik", html).Where(x => !x.bookmark.href.Contains("/" + videoId + "/")).ToList();

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null), total_pages: 1);
    }
    #endregion

    // uri = "/watch/189068251/" hoac URL tuyet doi
    static string VideoId(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !uri.Contains("/watch/", StringComparison.OrdinalIgnoreCase))
            return null;

        var m = Regex.Match(uri, "/watch/(\\d{3,})/");
        return m.Success ? m.Groups[1].Value : null;
    }
}
