using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SexNetVc;

public class SexNetVcController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);

    public SexNetVcController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sexnetvc")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // "Video lien quan" tu popup long-press
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1)
            pg = 1;

        var menuTask = MenuAsync();

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"nvc:{search}:{c}:{pg}"), 10, async e =>
            {
                string html = await GetHtmlAsync(NvcTo.Uri(init.host, search, c, pg));

                var playlists = NvcTo.Playlist("sexnetvc/vidosik", html);

                // Het du lieu / the loai khong ton tai (site tra 404) / trang ngoai
                // phan trang = list rong, khong phai loi.
                if (playlists.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(c) || pg > 1)
                        return e.Success((playlists, 0));

                    return e.Fail("playlists", refresh_proxy: true);
                }

                return e.Success((playlists, NvcTo.Pages(html)));
            });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var menu = await menuTask;

        // PlaylistResult() coi list rong la loi -> tra JSON rong bang tay
        // (tim khong ra / het trang / the loai khong ton tai).
        if (cache.Value.playlists == null || cache.Value.playlists.Count == 0)
        {
            // total_pages phai > 0: 0/missing = "con trang vo han" voi SISI.
            return Json(new
            {
                count = 0,
                total_pages = cache.Value.total_pages > 0 ? cache.Value.total_pages : 1,
                list = new List<PlaylistItem>(),
                menu
            });
        }

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            menu, total_pages: cache.Value.total_pages);
    }

    // Duong dan thay query (app ghep `pg` de hon):
    //   /sexnetvc/p/the-loai/khong-che/2   /sexnetvc/p/tag/xnxx
    //   /sexnetvc/p/search/jav%20hd        /sexnetvc/p/2
    [HttpGet, Staticache(manually: true)]
    [Route("sexnetvc/p/{**rest}")]
    async public Task<ActionResult> Path(string rest, int pg = 1)
    {
        if (pg < 1)
            pg = 1;

        string search = null, c = null;
        string[] seg = (rest ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (seg.Length > 0 && seg[seg.Length - 1].All(char.IsDigit)
            && int.TryParse(seg[seg.Length - 1], out int dp) && dp > 1)
        {
            pg = dp;
            seg = seg.Take(seg.Length - 1).ToArray();
        }

        if (seg.Length >= 2 && seg[0].Equals("search", StringComparison.OrdinalIgnoreCase))
            search = string.Join("/", seg.Skip(1));
        else if (seg.Length >= 2 && (seg[0].Equals("the-loai", StringComparison.OrdinalIgnoreCase)
              || seg[0].Equals("tag", StringComparison.OrdinalIgnoreCase)
              || seg[0].Equals("actresses", StringComparison.OrdinalIgnoreCase)))
            c = string.Join("/", seg).Trim('/');
        else if (seg.Length >= 1)
            c = seg[0];

        return await Index(search, c, pg, null);
    }

    // ===== popup long-press =====
    [HttpGet]
    [Route("sexnetvc/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : NvcTo.SiteHost + "/" + uri.Trim('/');

        // Chan SSRF: chi fetch host cua site
        if (pageUrl.IndexOf("sexnetvc.com", StringComparison.OrdinalIgnoreCase) < 0)
            return Json(new { items = new object[0], groups = new object[0] });

        string memKey = ipkey($"nvc:info:{uri}");

        if (!hybridCache.TryGetValue(memKey,
            out (List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel) hit)
            || hit.chips == null)
        {
            string html = await GetHtmlAsync(pageUrl);
            var chips = NvcTo.Chips(html);

            string slug = pageUrl.TrimEnd('/').Split('/').Last().Split('?')[0];
            string json = await GetHtmlAsync(NvcTo.SiteHost + "/ajax/suggestions/" + slug, pageUrl);
            var rel = NvcTo.Related("sexnetvc/vidosik", json);

            hit = (chips ?? new List<(string kind, string slug, string name)>(), rel ?? new List<PlaylistItem>());

            if (hit.chips.Count > 0 || hit.rel.Count > 0)
                hybridCache.Set(memKey, hit, cacheTime(360));
        }

        return InfoResult(uri, hit.chips, hit.rel);
    }

    ActionResult InfoResult(string uri,
        List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)
    {
        var groups = new List<object>();

        foreach (string kind in new[] { "cat", "tag" })
        {
            var items = (chips ?? new List<(string kind, string slug, string name)>())
                .Where(x => x.kind == kind)
                .Select(x => new
                {
                    title = x.name,
                    url = $"{host}/sexnetvc?c={kind switch { "cat" => "the-loai", _ => "tag" }}/{HttpUtility.UrlEncode(x.slug)}"
                })
                .ToList();

            if (items.Count == 0)
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
                        url = $"{host}/sexnetvc?rel={HttpUtility.UrlEncode(uri)}"
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

        var cache = await InvokeCacheResult(
            ipkey($"nvc:rel:{rel}"), 180, jsonContext.ListPlaylistItem, async e =>
            {
                string pageUrl = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? rel
                    : NvcTo.SiteHost + "/" + rel.Trim('/');

                string slug = pageUrl.TrimEnd('/').Split('/').Last().Split('?')[0];
                string json = await GetHtmlAsync(NvcTo.SiteHost + "/ajax/suggestions/" + slug, pageUrl);

                var playlists = NvcTo.Related("sexnetvc/vidosik", json);
                if (playlists.Count == 0)
                    return e.Success(new List<PlaylistItem>());

                return e.Success(playlists);
            });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        // KHONG tra menu day du o day: popup chi can list, tranh fetch them.
        var menu = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/sexnetvc"
            }
        };

        if (cache.Value == null || cache.Value.Count == 0)
        {
            return Json(new
            {
                count = 0,
                total_pages = 1,
                list = new List<PlaylistItem>(),
                menu
            });
        }

        return PlaylistResult(cache.Value, cache.ISingleCache, menu, total_pages: 1);
    }

    // ===== menu =====
    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("nvc:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        if (!await menuLock.WaitAsync(3000))
            return NvcTo.Menu(host, NvcTo.FallbackGenres, NvcTo.FallbackTags);

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;

            IReadOnlyList<(string name, string path)> genres = NvcTo.FallbackGenres;
            IReadOnlyList<(string name, string path)> tags = NvcTo.FallbackTags;

            try
            {
                var g = NvcTo.Genres(await GetHtmlAsync(NvcTo.SiteHost + "/?view=the-loai"));
                if (g.Count > 0)
                    genres = g;

                var t = NvcTo.Tags(await GetHtmlAsync(NvcTo.SiteHost + "/"));
                if (t.Count > 0)
                    tags = t;
            }
            catch { }

            var menu = NvcTo.Menu(host, genres, tags);
            hybridCache.Set(memKey, menu, cacheTime(720));
            return menu;
        }
        finally
        {
            menuLock.Release();
        }
    }

    async Task<string> GetHtmlAsync(string url, string referer = null)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        try
        {
            string html = null;

            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", NvcTo.ChromeUA),
                ("Referer", referer ?? (NvcTo.SiteHost + "/"))));

            return html;
        }
        catch
        {
            return null;
        }
    }

    // ===== player =====
    [HttpGet, Staticache(manually: true)]
    [Route("sexnetvc/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/sexnetvc/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : NvcTo.SiteHost + "/phim-sex/" + uri.Trim('/');

        string memKey = ipkey($"nvc:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache)
            && cache != null && cache.Count > 0)
            return cache;

        string page = await GetHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return null;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, src) in NvcTo.Servers(page))
        {
            string url = await ResolveServerAsync(src);
            if (string.IsNullOrEmpty(url))
                continue;

            string key = name;
            int n = 2;
            while (links.ContainsKey(key))
                key = name + " " + (n++);

            links[key] = url;
        }

        if (links.Count == 0)
            return null;

        // master.m3u8 cua wogplayer co `?e=` het han ~1h -> cache ngan.
        hybridCache.Set(memKey, links, cacheTime(5));
        return links;
    }

    async Task<string> ResolveServerAsync(string src)
    {
        try
        {
            var u = new Uri(src);

            // Alias — player tu khai window.videoData.sources[].file
            if (u.Host.EndsWith("vcast.name", StringComparison.OrdinalIgnoreCase))
            {
                string pl = await GetHtmlAsync(src);
                string f = NvcTo.FileUrl(pl);
                if (string.IsNullOrEmpty(f))
                    return null;

                if (f.StartsWith("/"))
                    f = "https://" + u.Host + f;

                return f.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? f : null;
            }

            // StreamQQ — 302 sang host that (wogplayer) roi POST
            // /videos/<id>/config?d=<domain> de nhan sources.
            string final = await Http.GetLocation(
                src,
                referer: NvcTo.SiteHost + "/",
                timeoutSeconds: 12,
                httpversion: init.httpversion,
                allowAutoRedirect: true,
                proxy: proxy);

            if (string.IsNullOrEmpty(final))
                return null;

            var fu = new Uri(final);
            string id = NvcTo.VideoId(final);
            if (string.IsNullOrEmpty(id))
                return null;

            string cfg = fu.Scheme + "://" + fu.Authority + "/videos/" + id
                + "/config?d=" + HttpUtility.UrlEncode(NvcTo.Domain);

            var headers = HeadersModel.Init(
                ("User-Agent", NvcTo.ChromeUA),
                ("Referer", fu.Scheme + "://" + fu.Authority + "/"));

            string json = await Http.Post(
                cfg, "",
                headers: headers,
                timeoutSeconds: 12,
                httpversion: init.httpversion,
                proxy: proxy);

            return NvcTo.FileUrl(json);
        }
        catch
        {
            return null;
        }
    }

    [HttpGet]
    [Route("sexnetvc/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || string.IsNullOrEmpty(q)
            || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        // Referer bat buoc va KHAC NHAU: vcast treo Referer site -> 403.
        string referer = link.IndexOf("vcast.name", StringComparison.OrdinalIgnoreCase) >= 0
            ? "https://vcast.name/"
            : NvcTo.SiteHost + "/";

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", NvcTo.ChromeUA),
            ("Referer", referer)
        ));

        return Redirect(HostStreamProxy(link, direct));
    }
}
