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

namespace Vietmup;

public class VietmupController : BaseSisiController
{
    public VietmupController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("vietmup")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        var cache = await InvokeCacheResult(ipkey($"vietmup:{search}:{c}:{sort}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(
                VietmupTo.Uri(init.host, search, c, pg, sort));

            var playlists = VietmupTo.Playlist(
                "vietmup/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync(search, sort, c));
    }

    async Task<string> FetchHtmlAsync(string url)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string html = null;
                await httpHydra.GetSpan(url, span =>
                {
                    html = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", VietmupTo.ChromeUA),
                    ("Referer", VietmupTo.SiteHost + "/")
                ));

                if (!string.IsNullOrEmpty(html))
                    return html;
            }
            catch
            {
                // retry
            }

            if (attempt == 0)
                await Task.Delay(700);
        }

        Console.WriteLine("Vietmup: fetch fail " + url);
        return null;
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var res = VietmupTo.MenuHead(host, search, sort, c);
        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        var cats = await CategoriesAsync();
        var tags = await TagsAsync();
        return VietmupTo.Menu(host, cats, tags);
    }

    async Task<List<(string name, string slug)>> CategoriesAsync()
    {
        string key = ipkey("vietmup:categories");

        if (hybridCache.TryGetValue(key, out List<(string name, string slug)> cats)
            && cats != null && cats.Count > 0)
            return cats;

        string html = await FetchHtmlAsync(VietmupTo.SiteHost + "/");
        cats = VietmupTo.Categories(html ?? "");

        if (cats.Count > 0)
            hybridCache.Set(key, cats, cacheTime(360));

        Console.WriteLine("Vietmup: categories n=" + cats.Count);
        return cats;
    }

    async Task<List<(string name, string slug)>> TagsAsync()
    {
        string key = ipkey("vietmup:tags");

        if (hybridCache.TryGetValue(key, out List<(string name, string slug)> tags)
            && tags != null && tags.Count > 0)
            return tags;

        string html = await FetchHtmlAsync(VietmupTo.SiteHost + "/tag");
        tags = VietmupTo.Tags(html ?? "");

        if (tags.Count > 0)
            hybridCache.Set(key, tags, cacheTime(720));

        Console.WriteLine("Vietmup: tags n=" + tags.Count);
        return tags;
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"vietmup:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = VietmupTo.SiteHost + pageUrl;

        var links = new Dictionary<string, string>();

        string pageHtml = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            pageHtml = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", VietmupTo.ChromeUA),
            ("Referer", VietmupTo.SiteHost + "/")
        ));

        if (!string.IsNullOrEmpty(pageHtml))
        {
            string tokenPath = VietmupTo.StreamToken(pageHtml);
            if (!string.IsNullOrEmpty(tokenPath))
            {
                string streamUrl = VietmupTo.SiteHost + tokenPath;
                links.TryAdd("HLS", streamUrl);
            }

            string abyssUrl = VietmupTo.AbyssUrl(pageHtml);
            if (!string.IsNullOrEmpty(abyssUrl))
                links.TryAdd("Abyss", "abyss|" + abyssUrl);
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("vietmup/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Duoi route phai khop dang that cua nguon: app Lampa chi bat hls.js khi
        // URL co `.m3u8` (SISI/plugins/sisi.js applyHlsType). Thieu duoi ->
        // player dua thang URL vao <video> -> "The element has no supported
        // sources" (va tua la khong the). Abyss la mp4 -> di route `.mp4`.
        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/vietmup/video{(v.Key == "Abyss" ? ".mp4" : ".m3u8")}"
            + $"?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("vietmup/video")]
    [Route("vietmup/video.m3u8")]
    [Route("vietmup/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(q))
            return OnError("stream_links", refresh_proxy: true);

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        if (link.StartsWith("abyss|", StringComparison.Ordinal))
            return Redirect($"{host}/vietmup/abyss.mp4?p={HttpUtility.UrlEncode(link.Substring(6))}");

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", VietmupTo.ChromeUA),
            ("Referer", VietmupTo.SiteHost + "/")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }

    [HttpGet]
    [Route("vietmup/abyss")]
    [Route("vietmup/abyss.mp4")]
    async public Task<ActionResult> Abyss(string p)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(p) || !p.StartsWith("https://player.abyssplayer.com/", StringComparison.Ordinal))
            return OnError("stream_links", refresh_proxy: true);

        string html = null;
        await httpHydra.GetSpan(p, span =>
        {
            html = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", VietmupTo.ChromeUA),
            ("Referer", VietmupTo.SiteHost + "/")
        ));

        var info = await AbyssTo.ResolveAsync(html, p);
        if (info == null)
            return OnError("stream_links", refresh_proxy: true);

        if (!await AbyssTo.StreamAsync(HttpContext, info, HttpContext.RequestAborted))
            return OnError("stream_links", refresh_proxy: true);

        return new EmptyResult();
    }

    #region info (popup long-press) — chip thể loại/thẻ + "Video liên quan"
    // cache RAM tinh (ValueTuple khong deserialize lai duoc tu hybridCache)
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    // sisi.js gọi đúng route "<mod>/genres?uri=<uri detail>"
    [HttpGet]
    [Route("vietmup/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : VietmupTo.SiteHost + "/" + uri.Trim('/');

        // chống SSRF: chỉ trang của site
        if (!pageUrl.Contains("vietmup.lol", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        if (_infoCache.TryGetValue(uri, out var hit)
            && DateTime.UtcNow - hit.at < InfoTtl && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.rel, uri);
        }

        string html = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return Json(new { items = new object[0], groups = new object[0] });

        var chips = VietmupTo.Chips(html);
        var rel = VietmupTo.PlaylistRelated("vietmup/vidosik", html);

        // tên đẹp: ưu tiên tên trong menu (chỉ đọc cache, KHÔNG fetch trong popup)
        var names = await TaxonomyNamesAsync();
        chips = chips.Select(x =>
        {
            string key = x.kind + "/" + x.slug;
            return names.TryGetValue(key, out string nm) && !string.IsNullOrEmpty(nm)
                ? (x.kind, x.slug, nm) : x;
        }).ToList();

        if (chips.Count > 0 || rel.Count > 0)
            _infoCache[uri] = (DateTime.UtcNow, chips, rel);

        return InfoResult(chips, rel, uri);
    }

    // map "<kind>/<slug>" -> tên hiển thị, chỉ lấy từ cache của menu
    async Task<Dictionary<string, string>> TaxonomyNamesAsync()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string ck = ipkey("vietmup:categories");
        if (hybridCache.TryGetValue(ck, out List<(string name, string slug)> cats) && cats != null)
            foreach (var c in cats)
                map["cat/" + c.slug] = c.name;

        string tk = ipkey("vietmup:tags");
        if (hybridCache.TryGetValue(tk, out List<(string name, string slug)> tags) && tags != null)
            foreach (var t in tags)
                map["tag/" + t.slug] = t.name;

        // cache trống (chưa ai mở menu) → nạp song song, có cache 6-12h nên chỉ chậm 1 lần đầu
        if (map.Count == 0)
        {
            var catTask = CategoriesAsync();
            var tagTask = TagsAsync();
            await Task.WhenAll(catTask, tagTask);

            foreach (var c in catTask.Result)
                map["cat/" + c.slug] = c.name;
            foreach (var t in tagTask.Result)
                map["tag/" + t.slug] = t.name;
        }

        return map;
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
                url = $"{host}/vietmup?c={kind}/{HttpUtility.UrlEncode(x.slug)}"
            }).ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title = kind == "cat" ? "Thể loại" : "Thẻ", items });
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
                        url = $"{host}/vietmup?rel={HttpUtility.UrlEncode(uri)}"
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

        string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? rel : VietmupTo.SiteHost + "/" + rel.Trim('/');

        if (!url.Contains("vietmup.lol", StringComparison.OrdinalIgnoreCase))
            return OnError("stream_links");

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"vietmup:rel:{url}"), 180, async e =>
        {
            string html = await FetchHtmlAsync(url);
            var playlists = VietmupTo.PlaylistRelated("vietmup/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("related");

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null), total_pages: 1);
    }
    #endregion
}