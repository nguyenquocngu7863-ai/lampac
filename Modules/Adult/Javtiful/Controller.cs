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

namespace Javtiful;

public class JavtifulController : BaseSisiController
{
    public JavtifulController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javtiful")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        // Gop sort ve dung tap cua context TRUOC khi lap cache key —
        // `sort=popular_week` o /vn/category/* da do la no-op.
        sort = JavtifulTo.ClampSort(sort, search, c);

        var cache = await InvokeCacheResult(ipkey($"javtiful:{search}:{c}:{sort}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(
                JavtifulTo.Uri(init.host, search, c, pg, sort));

            var playlists = JavtifulTo.Playlist(
                "javtiful/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync(search, sort, c));
    }

    // httpHydra.GetSpan re-entrant (bien cuc bo moi lan goi) nen goi
    // song song cho cac trang /vn/channels duoc.
    async Task<string> FetchHtmlAsync(string url)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", JavtifulTo.ChromeUA),
                ("Referer", JavtifulTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }

    // head (Tìm kiếm + "Sắp xếp: <sort hiện tại>") phụ thuộc c/search/sort
    // -> dựng lại mỗi request; base (nhóm dòng 3 trở xuống) mới cache.
    // `related` = block "Video liên quan" (tĩnh, không sort được) -> chỉ dòng 1
    // + base, ẩn dòng 2 (menu-sisi §9g quy tắc 4).
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/javtiful"
                }
            }
            : JavtifulTo.MenuHead(host, search, sort, c);

        res.AddRange(await MenuBaseAsync());
        return res;
    }

    // Menu "Kenh" boc tu /vn/channels (24 kenh/trang, 13 trang).
    // Fetch cac trang con SONG SONG roi gom trung ten, cache 6 gio RAM.
    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("javtiful:channels");

        if (!hybridCache.TryGetValue(key,
            out List<(string name, string slug)> chans)
            || chans == null || chans.Count == 0)
        {
            string first = await FetchHtmlAsync(
                JavtifulTo.SiteHost + "/vi/channels");

            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var pool = new List<(string, string)>();

            void Add(string html)
            {
                if (string.IsNullOrEmpty(html))
                    return;
                foreach (var c in JavtifulTo.ChannelList(html))
                    if (seen.Add(c.slug))
                        pool.Add(c);
            }

            Add(first);

            int pages = JavtifulTo.ChannelPages(first);
            if (pool.Count > 0 && pages > 1)
            {
                var tasks = new List<Task<string>>();
                for (int p = 2; p <= pages; p++)
                    tasks.Add(FetchHtmlAsync(JavtifulTo.SiteHost
                        + "/vi/channels?page=" + p));

                foreach (var html in await Task.WhenAll(tasks))
                    Add(html);
            }

            chans = pool;
            if (chans.Count > 0)
                hybridCache.Set(key, chans, cacheTime(360));

            Console.WriteLine("Javtiful: channels pages="
                + pages + " n=" + chans.Count);
        }

        return JavtifulTo.Menu(host, chans);
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"javtiful:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = "https://javtiful.com" + pageUrl;

        var links = new Dictionary<string, string>();
        var seenUrls = new HashSet<string>();

        void AddLink(string u)
        {
            if (string.IsNullOrWhiteSpace(u) || !seenUrls.Add(u))
                return;

            string label = u.Contains(".m3u8") ? "HLS" : "MP4";
            string key = label;
            int n = 2;
            while (links.ContainsKey(key))
                key = label + " " + (n++);

            links.TryAdd(key, u);
        }

        string pageHtml = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            pageHtml = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", JavtifulTo.ChromeUA),
            ("Referer", "https://javtiful.com/")
        ));

        if (!string.IsNullOrEmpty(pageHtml))
        {
            foreach (var u in JavtifulTo.StreamUrls(pageHtml))
                AddLink(u);
        }

        // Player moi (2026-10): trang khong con <source>, chi co
        // "playback":{"endpoint":"/vn/video/{id}/playback","ticket":"p1..."}
        // -> POST ticket=... (X-Requested-With) -> {sources:[{src}]}
        if (links.Count == 0 && !string.IsNullOrEmpty(pageHtml))
        {
            var (endpoint, ticket) = JavtifulTo.Playback(pageHtml);
            if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(ticket))
            {
                string playUrl = endpoint.StartsWith("http") ? endpoint : "https://javtiful.com" + endpoint;
                var playHeaders = HeadersModel.Init(
                    ("User-Agent", JavtifulTo.ChromeUA),
                    ("Referer", pageUrl),
                    ("Origin", "https://javtiful.com"),
                    ("Accept", "application/json"),
                    ("X-Requested-With", "XMLHttpRequest")
                );

                // ticket dung 1 lan: 409 + renewTicket -> doi roi POST lai (1 lan)
                for (int attempt = 0; attempt < 2 && links.Count == 0; attempt++)
                {
                    string body = null;
                    await httpHydra.PostSpan(playUrl, "ticket=" + HttpUtility.UrlEncode(ticket), span =>
                    {
                        body = span.ToString();
                    }, addheaders: playHeaders, statusCodeOK: false);

                    foreach (var u in JavtifulTo.SourceUrls(body))
                        AddLink(u);

                    if (links.Count == 0)
                    {
                        string renew = JavtifulTo.RenewTicket(body);
                        if (string.IsNullOrEmpty(renew))
                            break;
                        ticket = renew;
                    }
                }
            }
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javtiful/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/javtiful/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("javtiful/video")]
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
            ("User-Agent", JavtifulTo.ChromeUA),
            ("Referer", "https://javtiful.com/"),
            ("Origin", "https://javtiful.com")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }

    #region info (long-press popup menu)
    [HttpGet]
    [Route("javtiful/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.Contains("/video/", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"javtiful:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey, out (List<(string kind, string slug, string name)> chips, List<(string slug, string title, string dur)> rel) hit)
            && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.rel, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : init.host + uri;
        string html = await FetchHtmlAsync(pageUrl);

        var chips = JavtifulTo.Chips(html);
        var rel = JavtifulTo.Related(html);

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, (chips, rel), cacheTime(360));

        return InfoResult(chips, rel, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "actress" => "Diễn viên",
        "channel" => "Kênh",
        "category" => "Danh mục",
        "search" => "Thẻ",
        _ => char.ToUpperInvariant(kind[0]) + kind.Substring(1)
    };

    static int KindRank(string kind) => kind switch
    {
        "actress" => 0,
        "channel" => 1,
        "category" => 2,
        "search" => 3,
        _ => 99
    };

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, List<(string slug, string title, string dur)> rel, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct()
                     .OrderBy(KindRank).ThenBy(x => x))
        {
            var items = chips.Where(x => x.kind == kind).Select(x =>
            {
                // Uri() tu ghep host + "/vi/" + c, nen `c` KHONG duoc bat dau
                // bang "/vi/" (se thanh /vi/vi/...). The (search) di duong
                // `?search=` vi slug o day da encode san dang "3p+4p".
                string url = kind == "search"
                    ? $"{host}/javtiful?search={HttpUtility.UrlEncode(HttpUtility.UrlDecode(x.slug))}"
                    : $"{host}/javtiful?c={HttpUtility.UrlEncode(kind + "/" + x.slug)}";

                return new
                {
                    title = x.name,
                    url
                };
            }).ToList();

            if (items.Count == 0) continue;

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
                        url = $"{host}/javtiful?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1) return Json(new { total_pages = 1, list = new List<Shared.Models.SISI.Base.PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<Shared.Models.SISI.Base.PlaylistItem> playlists, int total_pages)>(
            ipkey($"javtiful:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? rel : init.host + rel;
            string html = await FetchHtmlAsync(url);

            var playlists = JavtifulTo.PlaylistRelated("javtiful/vidosik", html);
            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<Shared.Models.SISI.Base.PlaylistItem>(), 0));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null, related: true), total_pages: 1);
    }
    #endregion
}
