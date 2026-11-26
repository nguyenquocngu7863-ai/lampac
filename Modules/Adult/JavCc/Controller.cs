using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace JavCc;

public class JavCcController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);

    public JavCcController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javcc")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        var cache = await InvokeCacheResult(
            ipkey($"javcc:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(JavCcTo.Uri(init.host, search, c, pg));

            var playlists = JavCcTo.Playlist("javcc/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync());
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
                ("User-Agent", JavCcTo.ChromeUA),
                ("Referer", referer ?? JavCcTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }

    // ===== menu =====
    async Task<List<MenuItem>> MenuAsync()
    {
        const string key = "javcc:taxonomy:v1";
        if (hybridCache.TryGetValue(key, out List<MenuItem> cached) && cached != null && cached.Count > 1)
            return cached;

        if (!await menuLock.WaitAsync(8000))
            return JavCcTo.Menu(host);

        try
        {
            if (hybridCache.TryGetValue(key, out List<MenuItem> hit) && hit != null && hit.Count > 1)
                return hit;

            var catsTask = FetchHtmlAsync(JavCcTo.SiteHost + "/wp-json/wp/v2/categories?per_page=100");
            var tagsTask = FetchHtmlAsync(JavCcTo.SiteHost + "/wp-json/wp/v2/tags?per_page=100&orderby=count&order=desc");
            var load = Task.WhenAll(catsTask, tagsTask);
            if (load != await Task.WhenAny(load, Task.Delay(10000)))
                return JavCcTo.Menu(host);

            var menu = JavCcTo.Menu(host,
                JavCcTo.Taxonomy(catsTask.Result),
                JavCcTo.Taxonomy(tagsTask.Result));
            if (menu.Count > 1)
                hybridCache.Set(key, menu, cacheTime(360));
            return menu;
        }
        catch
        {
            return JavCcTo.Menu(host);
        }
        finally
        {
            menuLock.Release();
        }
    }

    // ===== resolve stream =====
    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"javcc:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = JavCcTo.SiteHost + pageUrl;

        var links = new Dictionary<string, string>();

        // 1) trang detail -> embed url
        string pageHtml = await FetchHtmlAsync(pageUrl);
        string embedUrl = JavCcTo.EmbedUrl(pageHtml);

        if (!string.IsNullOrEmpty(embedUrl))
        {
            // 2) trang embed -> master m3u8
            string embedHtml = await FetchHtmlAsync(embedUrl, pageUrl);
            string master = JavCcTo.MasterFromEmbed(embedHtml);

            if (!string.IsNullOrEmpty(master))
                links["HLS"] = master;
            else
                links["Embed"] = embedUrl;
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javcc/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var recomends = await RecomendsAsync(uri);
        if (related)
            return PlaylistResult(recomends, false, null, total_pages: 1);

        return Json(new
        {
            qualitys = links.ToDictionary(k => k.Key, v =>
                $"{host}/javcc/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"),
            recomends
        });
    }

    // Khoi "lien quan" trang detail -> playlist "tiep theo" trong player
    async Task<List<PlaylistItem>> RecomendsAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return new List<PlaylistItem>();

        string memKey = ipkey($"javcc:rel:{uri}");
        if (hybridCache.TryGetValue(memKey, out List<PlaylistItem> cache) && cache != null)
            return cache;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : (uri.StartsWith("/") ? JavCcTo.SiteHost + uri : JavCcTo.SiteHost + "/" + uri.Trim('/') + "/");

        string html = await FetchHtmlAsync(pageUrl);
        var rec = JavCcTo.PlaylistRelated("javcc/vidosik", html);
        if (rec == null)
            rec = new List<PlaylistItem>();

        foreach (var pl in rec)
        {
            if (pl?.video != null && !pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                pl.video = $"{host}/{pl.video.TrimStart('/')}";
        }

        if (rec.Count > 0)
            hybridCache.Set(memKey, rec, cacheTime(20));

        return rec;
    }

    // Route phai co duoi .m3u8: app chon engine player theo DUOI URL
    // (.m3u8 = hls.js, .mp4 = video/ExoPlayer). Khong co duoi -> "no supported source".
    [HttpGet]
    [Route("javcc/video")]
    [Route("javcc/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(q))
            return OnError("stream_links", refresh_proxy: true);

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        // embed thi redirect ra ngoai, con m3u8 thi qua proxy (CDN doi Referer videplay)
        if (q == "Embed")
            return Redirect(link);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JavCcTo.ChromeUA),
            ("Referer", "https://x.videplay.us/"),
            ("Origin", "https://x.videplay.us")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }

    #region info (long-press popup menu)
    [HttpGet]
    [Route("javcc/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        // chong SSRF: chi fetch host cua site
        if (!uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            uri = JavCcTo.SiteHost + (uri.StartsWith("/") ? uri : "/" + uri);

        if (!uri.Contains("javcc.me", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"javcc:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey, out List<(string kind, string slug, string name)> hit)
            && hit != null && hit.Count > 0)
        {
            return InfoResult(hit, uri);
        }

        string html = await FetchHtmlAsync(uri);
        var chips = JavCcTo.Chips(html);

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, chips, cacheTime(360));

        return InfoResult(chips, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "actor" => "Diễn viên",
        "studio" => "Nhà sản xuất",
        "category" => "Thể loại",
        "tag" => "Thẻ",
        _ => char.ToUpperInvariant(kind[0]) + kind.Substring(1)
    };

    static int KindRank(string kind) => kind switch
    {
        "actor" => 0,
        "studio" => 1,
        "category" => 2,
        "tag" => 3,
        _ => 99
    };

    // slug -> path cua site
    static string KindPath(string kind, string slug) => kind switch
    {
        "actor" => "actor/" + slug,
        "studio" => "studios/" + slug,
        "category" => "category/" + slug,
        "tag" => "tag/" + slug,
        _ => slug
    };

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, string uri)
    {
        var groups = new List<object>();

        if (chips != null)
        {
            foreach (string kind in chips.Select(x => x.kind).Distinct()
                         .OrderBy(KindRank).ThenBy(x => x))
            {
                var items = chips.Where(x => x.kind == kind).Select(x => new
                {
                    title = x.name,
                    url = $"{host}/javcc?c={HttpUtility.UrlEncode(KindPath(kind, x.slug))}"
                }).ToList();

                if (items.Count == 0)
                    continue;

                groups.Add(new { title = KindLabel(kind), items });
            }
        }

        // "Video lien quan" — luon 1 item plain, tro ?rel=
        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = $"{host}/javcc?rel={HttpUtility.UrlEncode(uri)}"
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        // total_pages = 1 (KHONG phai 0) — 0 = app nap lap vo han
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<Shared.Models.SISI.Base.PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<Shared.Models.SISI.Base.PlaylistItem> playlists, int total_pages)>(
            ipkey($"javcc:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? rel : init.host + rel;
            string html = await FetchHtmlAsync(url);

            var playlists = JavCcTo.PlaylistRelated("javcc/vidosik", html);
            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<Shared.Models.SISI.Base.PlaylistItem>(), 0));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(), total_pages: 1);
    }
    #endregion
}
