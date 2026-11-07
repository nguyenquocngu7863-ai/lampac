using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace SakuraSins;

public class SakuraSinsController : BaseSisiController
{
    public SakuraSinsController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sakurasins")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel);

        if (pg < 1) pg = 1;

        // gọt sort về đúng tập của context TRƯỚC khi lập cache key —
        // sort=views ở studio/performer là no-op (trang server-side chỉ ?page=N).
        sort = SakuraSinsTo.ClampSort(sort, search, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"sakurasins:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string url = SakuraSinsTo.Uri(init.host, search, c, pg, sort);
            string html = await FetchHtmlAsync(url);

            if (SakuraSinsTo.IsApi(search, c))
            {
                var (frag, hasMore) = SakuraSinsTo.FeedJson(html);
                var playlists = SakuraSinsTo.Playlist("sakurasins/vidosik", frag);
                if (playlists == null || playlists.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                        return e.Success((new List<PlaylistItem>(), 0));
                    return e.Fail("playlists", refresh_proxy: true);
                }
                return e.Success((playlists, hasMore ? 0 : pg));
            }

            var pl = SakuraSinsTo.Playlist("sakurasins/vidosik", html ?? "");
            if (pl == null || pl.Count == 0)
            {
                if (pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((pl, SakuraSinsTo.TotalPages(html, pg)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await MenuAsync(search, sort, c),
            total_pages: cache.Value.total_pages
        );
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
                ("User-Agent", SakuraSinsTo.ChromeUA),
                ("Referer", SakuraSinsTo.SiteHost + "/")
            ));
            return html;
        }
        catch
        {
            return null;
        }
    }

    // head (Tìm kiếm + "Sắp xếp: <sort hiện tại>") phụ thuộc search/sort/c
    // -> dựng lại mỗi request; base (Danh mục/Studio/Diễn viên/Bộ sưu tập) cache 6h.
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/sakurasins"
                }
            }
            : SakuraSinsTo.MenuHead(host, search, sort, c);

        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("sakurasins:menu");
        if (hybridCache.TryGetValue(key, out List<MenuItem> hit)
            && hit != null && hit.Count > 0)
            return hit;

        // 4 trang taxonomy (categories ~44, studios ~498->300, performers ~630->300,
        // collections 3) — fetch song song 1 lần, cache 6 giờ.
        var tasks = new[]
        {
            Task.Run(() => SakuraSinsTo.Categories(FetchOrEmpty(SakuraSinsTo.SiteHost + "/categories"))),
            Task.Run(() => SakuraSinsTo.Studios(FetchOrEmpty(SakuraSinsTo.SiteHost + "/studios"))),
            Task.Run(() => SakuraSinsTo.Performers(FetchOrEmpty(SakuraSinsTo.SiteHost + "/performers"))),
            Task.Run(() => SakuraSinsTo.Collections(FetchOrEmpty(SakuraSinsTo.SiteHost + "/collections"))),
        };
        var res = await Task.WhenAll(tasks);

        var menu = SakuraSinsTo.Menu(host, res[0], res[1], res[2], res[3]);
        if (menu.Count > 0)
            hybridCache.Set(key, menu, cacheTime(360));

        return menu;
    }

    string FetchOrEmpty(string url)
    {
        try
        {
            string html = null;
            httpHydra.GetSpan(url, span => html = span.ToString(), addheaders: HeadersModel.Init(
                ("User-Agent", SakuraSinsTo.ChromeUA),
                ("Referer", SakuraSinsTo.SiteHost + "/")
            )).GetAwaiter().GetResult();
            return html ?? "";
        }
        catch
        {
            return "";
        }
    }

    // ===== player: detail -> extract.php -> master HLS =====
    // Client: GET /ph/extract.php?v=<mid>&retries=5 -> {session, ...}
    //   -> master = /ph/stream/<session>/playlist.m3u8 (variants quality/N.m3u8,
    //      segments ../seg-N.ts?q=N — relative, HostStreamProxy tự viết lại).
    // DO 2026-11-06: segment trực tiếp OK (TS 1.5MB, HTTP 200).
    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"sakurasins:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : SakuraSinsTo.SiteHost + uri;

        string detail = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(detail))
            return null;

        var mid = Regex.Match(detail, @"data-mid=""(\d+)""");
        if (!mid.Success)
            return null;

        string extractUrl = SakuraSinsTo.SiteHost + "/ph/extract.php?v="
            + HttpUtility.UrlEncode(mid.Groups[1].Value) + "&retries=5";
        string json = null;
        try
        {
            await httpHydra.GetSpan(extractUrl, span => json = span.ToString(),
                addheaders: HeadersModel.Init(
                    ("User-Agent", SakuraSinsTo.ChromeUA),
                    ("Referer", pageUrl),
                    ("Origin", SakuraSinsTo.SiteHost)
                ));
        }
        catch
        {
            return null;
        }

        var sm = Regex.Match(json ?? "", @"""session""\s*:\s*""([^""]+)""");
        if (!sm.Success)
            return null;

        var links = new Dictionary<string, string>()
        {
            ["HLS"] = SakuraSinsTo.SiteHost + "/ph/stream/"
                + HttpUtility.UrlEncode(sm.Groups[1].Value) + "/playlist.m3u8"
        };

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sakurasins/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/sakurasins/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("sakurasins/video")]
    [Route("sakurasins/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(q))
            return OnError("stream_links", refresh_proxy: true);

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", SakuraSinsTo.ChromeUA),
            ("Referer", SakuraSinsTo.SiteHost + "/"),
            ("Origin", SakuraSinsTo.SiteHost)
        ));
        return Redirect(HostStreamProxy(link, headers));
    }

    #region info (long-press popup menu)
    [HttpGet]
    [Route("sakurasins/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.Contains("/watch/", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"sakurasins:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey, out (List<(string kind, string slug, string name)> chips, bool hasRel) hit)
            && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.hasRel, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : SakuraSinsTo.SiteHost + uri;
        string html = await FetchHtmlAsync(pageUrl);

        var chips = SakuraSinsTo.Chips(html ?? "");
        bool hasRel = (html ?? "").IndexOf("related-videos", StringComparison.OrdinalIgnoreCase) >= 0;

        if (chips.Count > 0 || hasRel)
            hybridCache.Set(cacheKey, (chips, hasRel), cacheTime(360));

        return InfoResult(chips, hasRel, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "category" => "Danh mục",
        "studio" => "Studio",
        _ => kind
    };

    static int KindRank(string kind) => kind switch
    {
        "category" => 0,
        "studio" => 1,
        _ => 99
    };

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, bool hasRel, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct()
                     .OrderBy(KindRank).ThenBy(x => x))
        {
            var items = chips.Where(x => x.kind == kind).Select(x =>
            {
                string url = $"{host}/sakurasins?c={HttpUtility.UrlEncode(kind + "/" + x.slug)}";
                return new
                {
                    title = x.name,
                    url
                };
            }).ToList();

            if (items.Count == 0) continue;

            groups.Add(new { title = KindLabel(kind), items });
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
                        url = $"{host}/sakurasins?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel)
    {
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"sakurasins:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rel : SakuraSinsTo.SiteHost + rel;
            string html = await FetchHtmlAsync(url);

            var playlists = SakuraSinsTo.PlaylistRelated("sakurasins/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null, related: true), total_pages: 1);
    }
    #endregion
}
