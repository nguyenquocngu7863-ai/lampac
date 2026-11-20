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

namespace JavMit;

public class JavMitController : BaseSisiController
{
    public JavMitController() : base(ModInit.conf) { }

    static readonly SemaphoreSlim menuLock = new(1, 1);

    [HttpGet, Staticache(manually: true)]
    [Route("javmit")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        // Sort chi co o /en/video/ — category/tag/actor da do la no-op.
        sort = JavMitTo.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"javmit:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await FetchHtmlAsync(JavMitTo.Uri(init.host, search, c, pg, sort));
            var playlists = JavMitTo.Playlist("javmit/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
            {
                // Tim khong co ket qua khong phai loi — tra list rong.
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }

            // Trang chu = /en/video/ (co phan trang that).
            return e.Success((playlists, JavMitTo.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await menuTask,
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
                ("User-Agent", JavMitTo.ChromeUA),
                ("Referer", JavMitTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var res = JavMitTo.MenuHead(host, search, sort, c);
        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("javmit:menu:v2");

        if (hybridCache.TryGetValue(key, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        if (!await menuLock.WaitAsync(8000))
            return JavMitTo.Menu(host);

        try
        {
            if (hybridCache.TryGetValue(key, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;

            var catsTask = FetchHtmlAsync(JavMitTo.SiteHost + JavMitTo.BasePath + "/categories/");
            var tagsTask = FetchHtmlAsync(JavMitTo.SiteHost + JavMitTo.BasePath + "/tags/");
            var actorsTask = FetchActorsAsync();

            var build = Task.WhenAll(catsTask, tagsTask, actorsTask);
            if (build != await Task.WhenAny(build, Task.Delay(10000)))
                return JavMitTo.Menu(host);

            var cats = JavMitTo.Categories(catsTask.Result);
            var tags = JavMitTo.Tags(tagsTask.Result);
            var actors = actorsTask.Result;

            var menu = JavMitTo.Menu(host, cats, tags, actors);
            if (cats.Count > 0 || tags.Count > 0 || actors.Count > 0)
                hybridCache.Set(key, menu, cacheTime(360));

            return menu;
        }
        catch
        {
            return JavMitTo.Menu(host);
        }
        finally
        {
            menuLock.Release();
        }
    }

    async Task<List<(string name, string slug, int count)>> FetchActorsAsync()
    {
        var pool = new List<(string, string, int)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tasks = new List<Task<string>>();
        for (int p = 1; p <= 3; p++)
            tasks.Add(FetchHtmlAsync(JavMitTo.SiteHost + JavMitTo.BasePath + "/actors/page/" + p + "/"));

        foreach (var html in await Task.WhenAll(tasks))
        {
            if (string.IsNullOrEmpty(html))
                continue;
            foreach (var a in JavMitTo.Actors(html))
                if (seen.Add(a.slug))
                    pool.Add(a);
        }

        return pool;
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"javmit:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : JavMitTo.SiteHost + uri;

        if (!pageUrl.Contains("javmit.com", StringComparison.OrdinalIgnoreCase))
            return null;

        string html = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return null;

        var all = JavMitTo.StreamLinks(html);
        if (all.Count == 0)
            return null;

        // Master da chua du variant -> chi tra 1 nguon, player tu chon chat luong.
        var master = all.FirstOrDefault(x =>
            x.label.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
            || x.url.Contains("master.m3u8", StringComparison.OrdinalIgnoreCase));

        var links = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(master.url))
        {
            links["AUTO"] = master.url;
        }
        else
        {
            foreach (var (label, url) in all)
                links.TryAdd(label, url);
        }

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javmit/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/javmit/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("javmit/video")]
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
            ("User-Agent", JavMitTo.ChromeUA),
            ("Referer", JavMitTo.SiteHost + "/"),
            ("Origin", JavMitTo.SiteHost)
        ));

        return Redirect(HostStreamProxy(link, headers));
    }

    #region info (long-press popup menu)
    [HttpGet]
    [Route("javmit/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.Contains("javmit.com", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"javmit:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey,
            out (List<(string kind, string path, string name)> chips,
                 List<(string href, string title, string poster, string dur)> rel) hit)
            && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.rel, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : init.host + uri;

        string html = await FetchHtmlAsync(pageUrl);

        var chips = JavMitTo.Chips(html);
        var rel = JavMitTo.Related(html);

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, (chips, rel), cacheTime(360));

        return InfoResult(chips, rel, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "actor" => "Diễn viên",
        "category" => "Thể loại",
        "tag" => "Thẻ",
        _ => char.ToUpperInvariant(kind[0]) + kind.Substring(1)
    };

    static int KindRank(string kind) => kind switch
    {
        "actor" => 0,
        "category" => 1,
        "tag" => 2,
        _ => 99
    };

    ActionResult InfoResult(
        List<(string kind, string path, string name)> chips,
        List<(string href, string title, string poster, string dur)> rel,
        string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct()
                     .OrderBy(KindRank).ThenBy(x => x))
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/javmit?c={HttpUtility.UrlEncode(x.path)}"
            }).ToList();

            if (items.Count == 0)
                continue;

            groups.Add(new { title = KindLabel(kind), items });
        }

        if (rel.Count > 0)
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
                        url = $"{host}/javmit?rel={HttpUtility.UrlEncode(uri)}"
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
            ipkey($"javmit:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rel
                : init.host + rel;

            string html = await FetchHtmlAsync(url);
            var playlists = new List<PlaylistItem>();

            foreach (var (href, title, poster, dur) in JavMitTo.Related(html))
            {
                playlists.Add(new PlaylistItem
                {
                    name = title,
                    video = "javmit/vidosik?uri=" + HttpUtility.UrlEncode(href),
                    picture = poster,
                    time = dur,
                    json = true,
                    bookmark = new Bookmark
                    {
                        site = "javmit",
                        href = href,
                        image = poster
                    }
                });
            }

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null), total_pages: 1);
    }
    #endregion
}
