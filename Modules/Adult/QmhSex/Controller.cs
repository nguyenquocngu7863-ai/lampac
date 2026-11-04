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

namespace QmhSex;

public class QmhSexController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);

    public QmhSexController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("qmhsex")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // danh sách "Gợi ý cho bạn" (popup phim liên quan)
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        // sort chi hop le o category/tag/search (trang chu bo qua orderby)
        sort = QmhSexTo.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"qmh:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string html = await GetHtmlAsync(QmhSexTo.Uri(init.host, search, c, pg, sort));

            if (string.IsNullOrEmpty(html) || !html.Contains("data-video-uid"))
            {
                // trang het du lieu (search rong / page ngoai phan trang) -> list rong,
                // khong Fail. Chi Fail khi fetch that su hong.
                bool soft = !string.IsNullOrWhiteSpace(search)
                    || !string.IsNullOrWhiteSpace(c) || pg > 1;

                return soft
                    ? e.Success((new List<PlaylistItem>(), 0))
                    : e.Fail("playlists", refresh_proxy: true);
            }

            var playlists = QmhSexTo.Playlist("qmhsex/vidosik", html);
            return e.Success((playlists, QmhSexTo.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await menuTask, total_pages: cache.Value.total_pages);
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        // "related" = danh sách gợi ý -> chỉ dòng 1 (Tìm kiếm) + menu base, ẩn dòng sắp xếp
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/qmhsex"
                }
            }
            : QmhSexTo.MenuHead(host, search, sort, c);

        res.AddRange(await MenuBaseAsync());
        return res;
    }

    // Danh muc + the lay tu WP REST, cache 12 gio.
    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("qmh:menu:v2");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        if (!await menuLock.WaitAsync(3000))
            return new List<MenuItem>();

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;

            string fields = "_fields=id,name,slug,count";
            var tasks = new List<Task<string>>()
            {
                GetHtmlAsync(QmhSexTo.SiteHost + "/wp-json/wp/v2/categories?per_page=100&" + fields),
                // the: chi top 100 theo so luong (the cua tung phim da co trong chip popup)
                GetHtmlAsync(QmhSexTo.SiteHost
                    + "/wp-json/wp/v2/tags?per_page=100&orderby=count&order=desc&" + fields)
            };

            var bodies = await Task.WhenAll(tasks);

            var cats = QmhSexTo.Taxonomy(bodies[0]);
            var tags = QmhSexTo.Taxonomy(bodies[1]);

            var menu = QmhSexTo.Menu(host, cats, tags);
            if (menu.Count > 0)
                hybridCache.Set(memKey, menu, cacheTime(720));

            Console.WriteLine($"QmhSex: menu cats={cats.Count} tags={tags.Count}");
            return menu;
        }
        catch { return new List<MenuItem>(); }
        finally { menuLock.Release(); }
    }

    async Task<string> GetHtmlAsync(string url, string referer = null)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", QmhSexTo.ChromeUA),
                ("Referer", referer ?? (QmhSexTo.SiteHost + "/"))
            ));

            return html;
        }
        catch { return null; }
    }

    // ===== PLAYER =====
    // trang detail -> var sources=[...] -> POST admin-ajax (action=getplayer)
    // -> trang embed (go1.qooglevideo.shop) -> jwplayer sources[] -> m3u8
    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"qmh:links:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cached)
            && cached != null && cached.Count > 0)
            return cached;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : QmhSexTo.SiteHost + "/" + uri.Trim('/');

        string html = await GetHtmlAsync(pageUrl);
        var (sourcesJson, servers) = QmhSexTo.PlayerInfo(html);
        if (string.IsNullOrEmpty(sourcesJson))
            return null;

        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (int srv in servers)
        {
            string json = await PostPlayerAsync(sourcesJson, srv, pageUrl);
            foreach (string embed in QmhSexTo.EmbedFiles(json))
            {
                string embedHtml = await GetHtmlAsync(embed, pageUrl);
                foreach (var (label, url) in QmhSexTo.Streams(embedHtml))
                    if (!links.ContainsKey(label))
                        links[label] = url;
            }

            if (links.Count > 0)
                break;
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(10));
        return links;
    }

    async Task<string> PostPlayerAsync(string sourcesJson, int server, string pageUrl)
    {
        try
        {
            string form = "action=getplayer&source=" + HttpUtility.UrlEncode(sourcesJson)
                + "&nonce=&server=" + server;

            return await httpHydra.Post(QmhSexTo.SiteHost + "/wp-admin/admin-ajax.php", form,
                addheaders: HeadersModel.Init(
                    ("User-Agent", QmhSexTo.ChromeUA),
                    ("Referer", pageUrl),
                    ("X-Requested-With", "XMLHttpRequest")
                ), statusCodeOK: false);
        }
        catch { return null; }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("qmhsex/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(uri))
            return OnError("stream_links", refresh_proxy: true);

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/qmhsex/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("qmhsex/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(q))
            return OnError("stream_links", refresh_proxy: true);

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", QmhSexTo.ChromeUA)
        ));

        return Redirect(HostStreamProxy(link, headers));
    }

    #region info (popup long-press) — "Gợi ý cho bạn" + chip danh mục/thẻ
    // cache RAM tinh (khong dung hybridCache: ValueTuple khong deserialize lai duoc)
    static readonly Dictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("qmhsex/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : QmhSexTo.SiteHost + "/" + uri.Trim('/');

        if (!pageUrl.Contains("qmhsex.shop", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(uri, out var hit)
                && DateTime.UtcNow - hit.at < InfoTtl && hit.chips != null)
            {
                return InfoResult(hit.chips, hit.rel, uri);
            }
        }

        string html = await GetHtmlAsync(pageUrl);
        var parsed = QmhSexTo.Chips(html ?? "");
        var names = QmhSexTo.PageNames(html ?? "");

        // ten dep: uu tien danh muc / the trong MENU (lay tu WP REST),
        // fallback la ten trong trang -> cuoi cung la slug de dep
        var menu = await MenuBaseAsync();
        foreach (var kv in QmhSexTo.NamesFromMenu(menu))
            names[kv.Key] = kv.Value;

        List<(string kind, string slug, string name)> chips = parsed.Select(c =>
            (kind: c.kind, slug: c.slug,
             name: names.TryGetValue(c.kind + "/" + c.slug, out var nm) && !string.IsNullOrEmpty(nm)
                 ? nm : c.name)).ToList();

        List<PlaylistItem> rel = QmhSexTo.PlaylistRelated("qmhsex/vidosik", html ?? "");

        if (chips.Count > 0 || rel.Count > 0)
        {
            lock (_infoCache)
            {
                _infoCache[uri] = (DateTime.UtcNow, chips, rel);
            }
        }

        return InfoResult(chips, rel, uri);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips,
        List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in new[] { "cat", "tag" })
        {
            var items = chips?.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/qmhsex?c={kind}/{HttpUtility.UrlEncode(x.slug)}"
            }).ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title = kind == "cat" ? "Danh mục" : "Thẻ", items });
        }

        if (rel != null && rel.Count > 0)
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
                        url = $"{host}/qmhsex?rel={HttpUtility.UrlEncode(uri)}"
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
            ipkey($"qmh:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rel : QmhSexTo.SiteHost + "/" + rel.Trim('/');

            string html = await GetHtmlAsync(url);
            var playlists = QmhSexTo.PlaylistRelated("qmhsex/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 0));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null, related: true), total_pages: 1);
    }
    #endregion
}
