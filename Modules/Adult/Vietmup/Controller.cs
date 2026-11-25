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

        // /drama/<slug> không phải list — trang series, phải cắt danh sách tập
        if (!string.IsNullOrEmpty(c) && c.StartsWith("drama/", StringComparison.Ordinal))
            return await Episodes(c.Substring(6).Trim('/'), pg, c);

        // sort lạ / sai ngữ cảnh -> mặc định của ngữ cảnh (site không có sort đó)
        sort = VietmupTo.ClampSort(c, search, sort);

        var cache = await InvokeCacheResult(ipkey($"vietmup:v2:{search}:{c}:{sort}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(
                VietmupTo.Uri(init.host, search, c, pg, sort));

            // Chi fetch loi (mang/5xx/timeout) moi la loi that. Tim kiem khong co
            // ket qua la chuyen binh thuong: trang tra HTML hop le nhung 0 item.
            if (html == null)
                return e.Fail("playlists", refresh_proxy: true);

            var playlists = VietmupTo.Playlist(
                "vietmup/vidosik", html);

            if (playlists == null)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        // PlaylistResult(...) bien list rong thanh 503 "playlists", nen khi khong
        // co ket qua phai tu tra JSON rong (count=0) de app hien "Khong tim thay"
        // thay vi loi do.
        var mMenu = await MenuAsync(search, sort, c);

        if (!cache.IsSuccess)
            return PlaylistResult(cache, mMenu);

        var list = cache.Value;
        if (list == null || list.Count == 0)
            return EmptyPlaylistResult(mMenu);

        // /search và /drama của site KHÔNG phân trang (bỏ qua &page) -> chặn pg=2 lặp lại trang 1
        int totalPages = string.IsNullOrWhiteSpace(search) || c == "drama" ? 1 : 0;
        return PlaylistResult(cache, mMenu, total_pages: totalPages);
    }

    // Danh sách tập của 1 series phim ngắn (/drama/<slug> -> /drama/<slug>/ep-N).
    // Mỗi tập là 1 item phát được (trang tập có data-manifest-url).
    async Task<ActionResult> Episodes(string slug, int pg, string c)
    {
        if (string.IsNullOrEmpty(slug) || pg > 1)
            return EmptyPlaylistResult(await MenuAsync(null, null, c), total_pages: 1);

        string pageUrl = VietmupTo.SiteHost + "/drama/" + slug;

        var cache = await InvokeCacheResult(
            ipkey($"vietmup:eps:{slug}"), 180, jsonContext.ListPlaylistItem, async e =>
            {
                string html = await FetchHtmlAsync(pageUrl);
                if (html == null)
                    return e.Fail("episodes", refresh_proxy: true);

                var (eps, poster) = VietmupTo.DramaEpisodes(html);
                if (eps.Count == 0)
                    return e.Success(new List<PlaylistItem>());

                string name = VietmupTo.SeriesName(html, slug);
                string href = "/drama/" + slug;

                var items = eps.Select(n => new PlaylistItem()
                {
                    video = "vietmup/vidosik?uri=" + HttpUtility.UrlEncode(href + "/ep-" + n),
                    name = $"{name} - Tập {n}",
                    picture = poster ?? "",
                    json = true,
                    bookmark = new Bookmark()
                    {
                        site = "vietmup",
                        href = href + "/ep-" + n,
                        image = poster ?? ""
                    }
                }).ToList();

                return e.Success(items);
            });

        var mMenu = await MenuAsync(null, null, c);

        if (!cache.IsSuccess)
            return PlaylistResult(cache, mMenu);

        if (cache.Value == null || cache.Value.Count == 0)
            return EmptyPlaylistResult(mMenu, total_pages: 1);

        return PlaylistResult(cache.Value, cache.ISingleCache, mMenu, total_pages: 1);
    }

    // MVC dung JsonSerializerOptions.DefaultIgnoreCondition = WhenWritingDefault nen
    // count=0 / total_pages=0 bi bo khoi JSON. Voi ket qua rong phai tra JSON tay de
    // app van thay count=0 (hien "Khong tim thay ket qua") thay vi loi "playlists".
    static readonly System.Text.Json.JsonSerializerOptions menuJsonOptions =
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

    ActionResult EmptyPlaylistResult(IReadOnlyList<MenuItem> menu, int total_pages = 1)
    {
        Response.Headers.CacheControl = "no-cache";

        string body = "{\"count\":0,\"total_pages\":" + total_pages
            + ",\"menu\":" + System.Text.Json.JsonSerializer.Serialize(menu ?? new List<MenuItem>(), menuJsonOptions)
            + ",\"list\":[]}";

        return Content(body, "application/json; charset=utf-8");
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
            catch (Exception ex)
            {
                Console.WriteLine($"Vietmup: fetch {url} attempt {attempt} -> {ex.Message}");
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
        var catsTask = CategoriesAsync();
        var tagsTask = TagsAsync();
        var extrasTask = FooterExtrasAsync();
        await Task.WhenAll(catsTask, tagsTask, extrasTask);

        var tags = tagsTask.Result;
        var (trends, sugg) = extrasTask.Result;

        // "Tags gợi ý" (footer) đứng trước, sau đó tới /tag; dedup theo slug
        if (sugg.Count > 0)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<(string name, string slug)>(sugg.Count + tags.Count);

            foreach (var t in sugg)
                if (seen.Add(t.slug))
                    merged.Add(t);

            foreach (var t in tags)
                if (seen.Add(t.slug))
                    merged.Add(t);

            tags = merged;
        }

        return VietmupTo.Menu(host, catsTask.Result, tags, trends);
    }

    // Footer mọi trang có "Mọi người đang tìm" + "Tags gợi ý" — 1 lần fetch home, cache 6h
    async Task<(List<string> trends, List<(string name, string slug)> tags)> FooterExtrasAsync()
    {
        string tk = ipkey("vietmup:trends");
        string sk = ipkey("vietmup:suggtags");

        bool hasTrends = hybridCache.TryGetValue(tk, out List<string> trends) && trends != null;
        bool hasTags = hybridCache.TryGetValue(sk, out List<(string name, string slug)> sugg) && sugg != null;

        if (hasTrends && hasTags)
            return (trends, sugg);

        string html = await FetchHtmlAsync(VietmupTo.SiteHost + "/");
        trends = VietmupTo.SearchTrends(html ?? "");
        sugg = VietmupTo.SuggestedTags(html ?? "");

        if (trends.Count > 0)
            hybridCache.Set(tk, trends, cacheTime(360));

        if (sugg.Count > 0)
            hybridCache.Set(sk, sugg, cacheTime(360));

        Console.WriteLine($"Vietmup: trends={trends.Count} suggtags={sugg.Count}");
        return (trends, sugg);
    }

    async Task<List<(string name, string slug)>> CategoriesAsync()
    {
        string key = ipkey("vietmup:categories:v2");

        if (hybridCache.TryGetValue(key, out List<(string name, string slug)> cats)
            && cats != null && cats.Count > 0)
            return cats;

        // /category là trang mục lục đầy đủ (59 thể loại); trang chủ chỉ 12
        string html = await FetchHtmlAsync(VietmupTo.SiteHost + "/category");
        cats = VietmupTo.Categories(html ?? "");

        if (cats.Count == 0)
        {
            html = await FetchHtmlAsync(VietmupTo.SiteHost + "/");
            cats = VietmupTo.Categories(html ?? "");
        }

        if (cats.Count > 0)
            hybridCache.Set(key, cats, cacheTime(360));

        Console.WriteLine("Vietmup: categories n=" + cats.Count);
        return cats;
    }

    async Task<List<(string name, string slug)>> TagsAsync()
    {
        string key = ipkey("vietmup:tags:v2");

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

    // value trong dict có tiền tố loại nguồn: "hls|<url>", "mp4|<url>", "abyss|<url>"
    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"vietmup:view:v2:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache)
            && cache != null && cache.Count > 0)
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
            // Trang watch / films: nhiều server (m3u8 · hls · direct · abyss)
            foreach (var (label, kind, url) in VietmupTo.Servers(pageHtml))
            {
                string key = label;
                int n = 2;
                while (links.ContainsKey(key))
                    key = label + " " + n++;

                if (kind == "abyss")
                    links.TryAdd(key, "abyss|" + url);
                else if (kind == "direct")
                    links.TryAdd(key, "mp4|" + url);
                else if (kind == "embed")
                    continue;   // iframe bên thứ ba: không resolve được
                else
                    links.TryAdd(key, "hls|" + url);
            }

            // Trang tập phim ngắn: data-manifest-url (HLS) / data-direct-url (mp4)
            if (links.Count == 0)
            {
                var (manifest, direct) = VietmupTo.DramaManifest(pageHtml);

                if (!string.IsNullOrEmpty(manifest))
                    links.TryAdd("HLS", "hls|" + manifest);
                if (!string.IsNullOrEmpty(direct))
                    links.TryAdd("MP4", "mp4|" + direct);
            }
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
        // Route phải khớp dạng thật: hls.js chỉ bật khi URL có .m3u8 (sisi.js applyHlsType);
        // mp4/abyss đi route .mp4 để <video> nhận file trực tiếp.
        return Json(links.ToDictionary(k => k.Key, v =>
        {
            string route = v.Value.StartsWith("hls|", StringComparison.Ordinal) ? "video.m3u8" : "video.mp4";
            return $"{host}/vietmup/{route}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}";
        }));
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

        string kind = "hls";
        int bar = link.IndexOf('|');
        if (bar > 0)
        {
            kind = link.Substring(0, bar);
            link = link.Substring(bar + 1);
        }

        if (kind == "abyss")
            return Redirect($"{host}/vietmup/abyss.mp4?p={HttpUtility.UrlEncode(link)}");

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

        string ck = ipkey("vietmup:categories:v2");
        if (hybridCache.TryGetValue(ck, out List<(string name, string slug)> cats) && cats != null)
            foreach (var c in cats)
                map["cat/" + c.slug] = c.name;

        string tk = ipkey("vietmup:tags:v2");
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

            if (html == null)
                return e.Fail("related", refresh_proxy: true);

            var playlists = VietmupTo.PlaylistRelated("vietmup/vidosik", html);

            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var mMenu = await MenuAsync(null, null, null);

        if (cache.Value.playlists == null || cache.Value.playlists.Count == 0)
            return EmptyPlaylistResult(mMenu);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            mMenu, total_pages: 1);
    }
    #endregion
}