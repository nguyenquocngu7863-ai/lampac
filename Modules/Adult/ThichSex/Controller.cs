using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace ThichSex;

public class ThichSexController : BaseSisiController
{
    public ThichSexController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("thichsex")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;
        sort = ThsTo.ClampSort(sort, search, c);     // clamp TRUOC cache key

        // Taxonomy fetch song song voi list (app cache response dau ca phien).
        var menuTask = MenuAsync(search, c, sort);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"thsx:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await FetchHtmlAsync(ThsTo.ListUri(init.host, search, c, pg, sort));
            var playlists = ThsTo.Playlist("thichsex/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
            {
                // Trang cuoi / khong co ket qua -> success rong, khong 503.
                if (pg > 1 || !string.IsNullOrWhiteSpace(search))
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success((playlists, ThsTo.Pages(html)));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await menuTask, total_pages: cache.Value.total_pages);
    }

    #region fetch
    async Task<string> FetchHtmlAsync(string url, string referer = null)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span => { html = span.ToString(); },
                addheaders: HeadersModel.Init(
                    ("User-Agent", ThsTo.ChromeUA),
                    ("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8"),
                    ("Referer", string.IsNullOrEmpty(referer) ? ThsTo.SiteHost + "/" : referer)
                ));
            return html;
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region menu
    // head (dong 1+2) dung lai moi request; base (dong 3+) cache 720 phut.
    async Task<List<MenuItem>> MenuAsync(string search, string c, string sort)
    {
        var menu = ThsTo.MenuHead(host, search, c, sort);
        menu.AddRange(await MenuBaseAsync());
        return menu;
    }

    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 6000;

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("thsx:v2:tax");
        if (hybridCache.TryGetValue(key, out (List<(string name, string slug)> cats, List<(string name, string slug)> tags) hit)
            && hit.cats != null && hit.cats.Count > 0)
            return ThsTo.MenuBase(host, hit.cats, hit.tags);

        if (!await menuLock.WaitAsync(2000))
            return ThsTo.MenuBase(host, ThsTo.FallbackCategories(), null);

        try
        {
            if (hybridCache.TryGetValue(key, out (List<(string name, string slug)> cats, List<(string name, string slug)> tags) hit2)
                && hit2.cats != null && hit2.cats.Count > 0)
                return ThsTo.MenuBase(host, hit2.cats, hit2.tags);

            var fetch = FetchTaxAsync(key);
            if (fetch != await Task.WhenAny(fetch, Task.Delay(MenuFetchBudget)))
                return ThsTo.MenuBase(host, ThsTo.FallbackCategories(), null);
            return await fetch;
        }
        finally
        {
            menuLock.Release();
        }
    }

    async Task<List<MenuItem>> FetchTaxAsync(string key)
    {
        var cats = (IReadOnlyList<(string name, string slug)>)ThsTo.FallbackCategories();
        var tags = (IReadOnlyList<(string name, string slug)>)new List<(string, string)>();

        try
        {
            string html = await FetchHtmlAsync(ThsTo.SiteHost + "/");
            if (!string.IsNullOrEmpty(html))
            {
                var g = ThsTo.Categories(html);
                var t = ThsTo.Tags(html);
                if (g.Count > 0) cats = g;
                if (t.Count > 0) tags = t;
            }
        }
        catch { }

        hybridCache.Set(key, (cats.ToList(), tags.ToList()), cacheTime(720), true);
        return ThsTo.MenuBase(host, cats.ToList(), tags.ToList());
    }
    #endregion

    #region popup (long-press)
    static readonly ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _detailCache = new();
    static readonly TimeSpan DetailTtl = TimeSpan.FromHours(6);

    // 1 fetch detail -> chips + related (dung chung /genres va ?rel=).
    async Task<(List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> DetailInfoAsync(string uri)
    {
        if (_detailCache.TryGetValue(uri, out var hit)
            && DateTime.UtcNow - hit.at < DetailTtl && hit.chips != null)
            return (hit.chips, hit.rel);

        string page = ThsTo.NormalizePageUrl(uri);
        string html = string.IsNullOrEmpty(page) ? null : await FetchHtmlAsync(page);
        var chips = ThsTo.Chips(html ?? "");
        var rel = ThsTo.PlaylistRelated("thichsex/vidosik", html ?? "");

        if (!string.IsNullOrEmpty(html))
            _detailCache[uri] = (DateTime.UtcNow, chips, rel);

        return (chips, rel);
    }

    [HttpGet]
    [Route("thichsex/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (ThsTo.NormalizePageUrl(uri) == null)
            return Json(new { items = new object[0], groups = new object[0] });

        var (chips, rel) = await DetailInfoAsync(uri);
        var groups = new List<object>();

        foreach (var (kind, label) in new[] { ("cat", "Thể loại"), ("tag", "Thẻ") })
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = kind == "cat"
                    ? $"{host}/thichsex?c=" + HttpUtility.UrlEncode(x.slug)
                    : $"{host}/thichsex?c=" + HttpUtility.UrlEncode("tag/" + x.slug)
            }).ToList();
            if (items.Count > 0)
                groups.Add(new { title = label, items });
        }

        if (rel != null && rel.Count > 0)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new { title = "Video liên quan", plain = true,
                          url = $"{host}/thichsex?rel={HttpUtility.UrlEncode(uri)}" }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var (_, playlists) = await DetailInfoAsync(rel);
        if (playlists == null || playlists.Count == 0)
            return Json(new { count = 0, total_pages = 1, list = new List<PlaylistItem>() });

        var menu = new List<MenuItem>()
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/thichsex" }
        };
        return PlaylistResult(playlists, false, menu, total_pages: 1);
    }
    #endregion

    #region stream
    // /vidosik chi LIET KE 1 nguon (site co 1 player). Resolve that su o /video.
    [HttpGet, Staticache(manually: true)]
    [Route("thichsex/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string page = ThsTo.NormalizePageUrl(uri);
        if (page == null)
            return OnError("stream_links", refresh_proxy: true);

        string player = ThsTo.PlayerUrl(await FetchHtmlAsync(page));
        if (string.IsNullOrEmpty(player))
            return OnError("stream_links", refresh_proxy: true);
        return Json(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Auto"] = $"{host}/thichsex/video.m3u8?uri={HttpUtility.UrlEncode(uri)}"
        });
    }

    async Task<string> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        string page = ThsTo.NormalizePageUrl(uri);
        if (page == null) return null;

        string key = ipkey($"thsx:v2:view:{page}");
        if (hybridCache.TryGetValue(key, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        string detail = await FetchHtmlAsync(page);
        if (string.IsNullOrEmpty(detail)) return null;

        // Thu lan luot tung nguon: embed.php co the tra file rong (bai hong),
        // khi do phai sang ung vien ke tiep thay vi bo cuoc.
        foreach (string player in ThsTo.PlayerUrls(detail))
        {
            string embed = await FetchHtmlAsync(player, page);
            string master = ThsTo.MasterUrl(embed ?? "");
            if (string.IsNullOrEmpty(master)) continue;

            hybridCache.Set(key, master, cacheTime(10), true);
            return master;
        }

        return null;
    }

    [HttpGet]
    [Route("thichsex/video")]
    [Route("thichsex/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string master = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(master))
            return OnError("stream_links", refresh_proxy: true);

        // /proxy/ tu rewrite segment tuyet doi trong m3u8 (ProxyAPI.M3u8.cs)
        // va giu header; StripPngJunk (ModInit) cat 265B PNG dau TS segment.
        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", ThsTo.ChromeUA),
            ("Referer", ThsTo.PlayerHost + "/")
        ));

        return Redirect(HostStreamProxy(master, headers));
    }
    #endregion
}
