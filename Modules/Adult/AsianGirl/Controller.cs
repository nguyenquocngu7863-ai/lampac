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

namespace AsianGirl;

public class AsianGirlController : BaseSisiController
{
    public AsianGirlController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("asiangirl")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        sort = AsianGirlTo.ClampSort(sort, search, c);

        var cache = await InvokeCacheResult(
            ipkey($"asiangirl:{search}:{c}:{sort}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(
                AsianGirlTo.Uri(init.host, search, c, pg, sort));

            var playlists = AsianGirlTo.Playlist(
                "asiangirl/vidosik", init.host, html);

            if (playlists == null || playlists.Count == 0)
                return e.Success(new List<PlaylistItem>());

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        var menu = await MenuAsync(search, sort, c);

        if (!cache.IsSuccess)
            return PlaylistResult(cache, menu);

        if (cache.Value == null || cache.Value.Count == 0)
            return EmptyPlaylistResult(menu);

        return PlaylistResult(cache, menu);
    }

    // MVC bo count=0 khoi JSON -> tra tay de app hien "Khong tim thay ket qua"
    static readonly System.Text.Json.JsonSerializerOptions menuJsonOptions =
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

    ActionResult EmptyPlaylistResult(IReadOnlyList<MenuItem> menu)
    {
        Response.Headers.CacheControl = "no-cache";

        string body = "{\"count\":0,\"total_pages\":0"
            + ",\"menu\":" + System.Text.Json.JsonSerializer.Serialize(menu ?? new List<MenuItem>(), menuJsonOptions)
            + ",\"list\":[]}";

        return Content(body, "application/json; charset=utf-8");
    }

    async Task<string> FetchHtmlAsync(string url, string referer = null)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", AsianGirlTo.ChromeUA),
                ("Referer", string.IsNullOrEmpty(referer) ? AsianGirlTo.SiteHost + "/" : referer)
            ));

            return html;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AsianGirl: fetch {url} -> {ex.Message}");
            return null;
        }
    }

    #region menu
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/asiangirl"
                }
            }
            : AsianGirlTo.MenuHead(host, search, sort, c);

        res.AddRange(await MenuBaseAsync());
        return res;
    }

    // /categories/ (33 muc) — cache 6 gio
    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("asiangirl:cats");

        if (!hybridCache.TryGetValue(key, out List<(string slug, string name)> cats)
            || cats == null || cats.Count == 0)
        {
            string html = await FetchHtmlAsync(AsianGirlTo.SiteHost + "/categories/");
            cats = AsianGirlTo.Categories(html);

            if (cats.Count > 0)
                hybridCache.Set(key, cats, cacheTime(360));
        }

        return AsianGirlTo.Menu(host, cats);
    }
    #endregion

    #region stream
    async Task<string> StreamAsync(string uri)
    {
        if (!IsSiteUri(uri))
            return null;

        string key = ipkey($"asiangirl:stream:{uri}");
        if (hybridCache.TryGetValue(key, out string hit) && !string.IsNullOrEmpty(hit))
            return hit;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : AsianGirlTo.SiteHost + uri;

        string html = await FetchHtmlAsync(pageUrl);
        string link = AsianGirlTo.StreamUrl(html);
        if (string.IsNullOrEmpty(link))
            return null;

        hybridCache.Set(key, link, cacheTime(5));
        return link;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("asiangirl/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string link = await StreamAsync(uri);
        if (string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        return Json(new Dictionary<string, string>()
        {
            ["HLS"] = $"{host}/asiangirl/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q=HLS"
        });
    }

    [HttpGet]
    [Route("asiangirl/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(q))
            return OnError("stream_links", refresh_proxy: true);

        string link = await StreamAsync(uri);
        if (string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", AsianGirlTo.ChromeUA),
            ("Referer", AsianGirlTo.SiteHost + "/")
        ));

        return Redirect(HostStreamProxy(link, headers));
    }
    #endregion

    #region info (long-press popup menu)
    [HttpGet]
    [Route("asiangirl/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (!IsSiteUri(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"asiangirl:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey, out (List<(string slug, string name)> chips, bool hasRel) hit)
            && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.hasRel, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : AsianGirlTo.SiteHost + uri;

        string html = await FetchHtmlAsync(pageUrl);

        var chips = AsianGirlTo.Chips(html);
        bool hasRel = html != null
            && html.IndexOf("list_videos_related_videos_items", StringComparison.OrdinalIgnoreCase) >= 0;

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, (chips, hasRel), cacheTime(360));

        return InfoResult(chips, hasRel, uri);
    }

    ActionResult InfoResult(List<(string slug, string name)> chips, bool hasRel, string uri)
    {
        var groups = new List<object>();

        if (chips.Count > 0)
        {
            groups.Add(new
            {
                title = "Thể loại",
                items = chips.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/asiangirl?c={HttpUtility.UrlEncode("categories/" + x.slug)}"
                }).ToList()
            });
        }

        if (hasRel)
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
                        url = $"{host}/asiangirl?rel={HttpUtility.UrlEncode(uri)}"
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

        if (!IsSiteUri(rel))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"asiangirl:rel:{rel}"), 180, async e =>
        {
            string pageUrl = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rel
                : AsianGirlTo.SiteHost + rel;

            string html = await FetchHtmlAsync(pageUrl);
            var playlists = AsianGirlTo.Playlist(
                "asiangirl/vidosik", init.host, html, skipHref: pageUrl);

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null, related: true), total_pages: 1);
    }

    // SSRF guard: chi cho uri thuoc host site (hoac path tuong doi)
    static bool IsSiteUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        if (uri.StartsWith("/", StringComparison.Ordinal))
            return uri.StartsWith("/v/", StringComparison.OrdinalIgnoreCase);

        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri u))
            return false;

        return u.Scheme == "https"
            && u.Host.Equals(new Uri(AsianGirlTo.SiteHost).Host, StringComparison.OrdinalIgnoreCase)
            && u.AbsolutePath.StartsWith("/v/", StringComparison.OrdinalIgnoreCase);
    }
    #endregion
}
