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
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SexVietop;

public class SexVietopController : BaseSisiController
{
    public SexVietopController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sexvietop")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rank = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // Popup "Video liên quan" -> chi tra danh sach 1 trang, total_pages = 1
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        // Bang xep hang (ajax/top): 1 trang, khong phan trang
        if (!string.IsNullOrWhiteSpace(rank))
            return await RankList(rank, pg);

        var cache = await InvokeCacheResult(
            ipkey($"sxvt:{search}:{c}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(
                SxvtTo.Uri(init.host, search, c, pg));

            var playlists = SxvtTo.Playlist("sexvietop/vidosik", html ?? "");

            // Tim khong co ket qua / `c` nham -> site tra 404 (ko phai loi).
            if (playlists != null && playlists.Count == 0)
                return e.Success(playlists);

            if (playlists == null && !string.IsNullOrWhiteSpace(search))
                return e.Success(new List<PlaylistItem>());

            if (playlists == null)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        // total_pages = 0 -> app tu tinh tu `count`; site kẹp pg > last = last.
        return PlaylistResult(cache, await MenuAsync(c));
    }

    #region fetch helpers
    async Task<string> FetchHtmlAsync(string url)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", SxvtTo.ChromeUA),
                ("Referer", SxvtTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }

    async Task<string> FetchJsonAsync(string url)
    {
        try
        {
            string json = null;
            await httpHydra.GetSpan(url, span =>
            {
                json = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", SxvtTo.ChromeUA),
                ("Referer", SxvtTo.SiteHost + "/"),
                ("Accept", "application/json"),
                ("X-Requested-With", "XMLHttpRequest")
            ));

            return json;
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region menu
    // App CACHE response DAU CA PHIEN -> phai tra menu day du ngay lan dau.
    // Fetch /?page=categories (35 the loai) + nav tag; fail -> fallback tinh.
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 8_000;

    async Task<List<MenuItem>> MenuAsync(string c)
    {
        // Key phai co `c`: menu dòng 3 có mục "Xem nhiều" phụ thuộc thể loại
        // đang mở. Key cố định -> menu của context đầu tiên bị dùng cho mọi
        // context khác (row/filter sai).
        string memKey = ipkey($"sxvt:menu:{c}");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        if (!await menuLock.WaitAsync(3000))
            return Fallback(c);

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) &&
                hit2 != null && hit2.Count > 0)
                return hit2;

            var build = BuildMenuAsync(c, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return Fallback(c);

            return await build;
        }
        finally
        {
            menuLock.Release();
        }
    }

    List<MenuItem> Fallback(string c)
        => SxvtTo.Menu(host, SxvtTo.FallbackCategories(), SxvtTo.FallbackTags(), c);

    async Task<List<MenuItem>> BuildMenuAsync(string c, string memKey)
    {
        // Taxonomy (35 thể loại + tag) là dữ liệu chung, không phụ thuộc `c`
        // -> cache riêng để đổi thể loại không phải fetch lại trang categories.
        string taxKey = ipkey("sxvt:tax");

        if (hybridCache.TryGetValue(taxKey, out (List<(string name, string path, int count)> cats, List<(string name, string path)> tags) tax))
        {
            var cached = SxvtTo.Menu(host, tax.cats, tax.tags, c);
            hybridCache.Set(memKey, cached, cacheTime(720), true);
            return cached;
        }

        var cats = (IReadOnlyList<(string name, string path, int count)>)SxvtTo.FallbackCategories();
        var tags = (IReadOnlyList<(string name, string path)>)SxvtTo.FallbackTags();

        try
        {
            // /?page=categories la trang duy nhat co grid the loai + tag nav.
            string html = await FetchHtmlAsync(SxvtTo.SiteHost + "/?page=categories");
            if (!string.IsNullOrEmpty(html))
            {
                var g = SxvtTo.Categories(html);
                var t = SxvtTo.Tags(html);
                if (g.Count > 0) cats = g;
                if (t.Count > 0) tags = t;
            }
        }
        catch { }

        hybridCache.Set(taxKey, (cats.ToList(), tags.ToList()), cacheTime(720), true);

        var menu = SxvtTo.Menu(host, cats.ToList(), tags.ToList(), c);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }
    #endregion

    #region rankings
    async Task<ActionResult> RankList(string rank, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string key = rank switch
        {
            "1h" => "1h",
            "week" => "week",
            _ => "day"
        };

        var cache = await InvokeCacheResult<List<PlaylistItem>>(
            ipkey($"sxvt:top:{key}"), 30, jsonContext.ListPlaylistItem, async e =>
        {
            string json = await FetchJsonAsync(SxvtTo.SiteHost + "/ajax/top/" + key);
            var list = SxvtTo.Rankings("sexvietop/vidosik", json ?? "");
            return list.Count == 0 ? e.Fail("playlists", refresh_proxy: true) : e.Success(list);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache, await MenuAsync(null), total_pages: 1);
    }
    #endregion

    #region related (popup ?rel=)
    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        // popup "Video liên quan" -> chỉ 1 trang, menu chỉ dòng Tìm kiếm.
        var menu = new List<MenuItem>
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/sexvietop"
            }
        };

        if (pg > 1)
            return Json(new { count = 0, total_pages = 1, list = new List<PlaylistItem>(), menu });

        string pageUrl = rel.Trim();
        pageUrl = HttpUtility.HtmlDecode(pageUrl);
        if (!pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            pageUrl = init.host + pageUrl;
        int qm = pageUrl.IndexOf('?');
        if (qm >= 0) pageUrl = pageUrl.Substring(0, qm);

        // /ajax/suggestions/<slug> (slug từ URL /phim-sex/<slug>, KHÔNG phải
        // video id — id nằm trong nút player, không có trên URL trang).
        string slug = null;
        var ms = Regex.Match(pageUrl, "/phim-sex/([a-z0-9-]+)", RegexOptions.IgnoreCase);
        if (ms.Success)
            slug = ms.Groups[1].Value;

        if (string.IsNullOrEmpty(slug))
            return Json(new { count = 0, total_pages = 1, list = new List<PlaylistItem>(), menu });

        // Cache tay: KHÔNG cache list rỗng. Site chặn/429 một lần mà cache
        // 180 phút thì popup "Video liên quan" rỗng mãi (đã dính).
        string memKey = ipkey($"sxvt:rel:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<PlaylistItem> hit) && hit != null && hit.Count > 0)
            return PlaylistResult(hit, true, menu, total_pages: 1);

        string json = await FetchJsonAsync(SxvtTo.SiteHost + "/ajax/suggestions/" + slug);
        var list = SxvtTo.Related("sexvietop/vidosik", json ?? "");

        if (list == null || list.Count == 0)
            return Json(new { count = 0, total_pages = 1, list = new List<PlaylistItem>(), menu });

        hybridCache.Set(memKey, list, cacheTime(180), true);

        // Qua PlaylistResult để video/picture được ghép host + proxy
        // (Json tự viết để lại URL tương đối -> app không phát được).
        return PlaylistResult(list, true, menu, total_pages: 1);
    }
    #endregion

    #region info (long-press popup)
    [HttpGet]
    [Route("sexvietop/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.Contains("/phim-sex/", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"sxvt:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey, out (List<(string kind, string slug, string name)> chips, string rel) hit)
            && hit.chips != null && hit.chips.Count > 0)
        {
            return InfoResult(hit.chips, hit.rel, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : init.host + uri;
        string html = await FetchHtmlAsync(pageUrl);

        var chips = SxvtTo.Chips(html ?? "");
        string rel = pageUrl;

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, (chips, rel), cacheTime(360), true);

        return InfoResult(chips, rel, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "the-loai" => "Thể loại",
        "tag" => "Từ khoá",
        _ => kind
    };

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, string rel, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct())
        {
            var items = chips.Where(x => x.kind == kind).Select(x =>
                new
                {
                    title = x.name,
                    url = $"{host}/sexvietop?c=" + HttpUtility.UrlEncode(kind + "/" + x.slug)
                }).ToList();

            if (items.Count == 0) continue;

            groups.Add(new { title = KindLabel(kind), items });
        }

        if (!string.IsNullOrEmpty(rel))
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
                        url = $"{host}/sexvietop?rel={HttpUtility.UrlEncode(rel)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }
    #endregion

    #region stream
    [HttpGet, Staticache(manually: true)]
    [Route("sexvietop/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/sexvietop/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : SxvtTo.SiteHost + "/phim-sex/" + uri.Trim('/');

        string memKey = ipkey($"sxvt:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        string page = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return null;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, src) in SxvtTo.Servers(page))
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
        hybridCache.Set(memKey, links, cacheTime(5), true);
        return links;
    }

    async Task<string> ResolveServerAsync(string src)
    {
        try
        {
            var u = new Uri(src);

            // [2] Alias — trang player tu khai `window.videoData.sources`.
            if (u.Host.EndsWith("vcast.name", StringComparison.OrdinalIgnoreCase))
            {
                string pl = await FetchHtmlAsync(src);
                string f = SxvtTo.FileUrl(pl);
                if (string.IsNullOrEmpty(f))
                    return null;
                if (f.StartsWith("/"))
                    f = "https://" + u.Host + f;
                return f.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? f : null;
            }

            // [1] StreamQQ — `data-source` tro e.streamforester.name nhung 302
            // sang host that (wogplayer.top). Lay host cuoi bang GetLocation,
            // roi POST /videos/<id>/config?d=<domain> de nhan sources.
            string final = await Http.GetLocation(
                src,
                referer: SxvtTo.SiteHost + "/",
                timeoutSeconds: 12,
                httpversion: init.httpversion,
                allowAutoRedirect: true,
                proxy: proxy);

            if (string.IsNullOrEmpty(final))
                return null;

            var fu = new Uri(final);
            string id = SxvtTo.VideoId(final);
            if (string.IsNullOrEmpty(id))
                return null;

            string cfg = fu.Scheme + "://" + fu.Authority + "/videos/" + id
                + "/config?d=" + HttpUtility.UrlEncode(SxvtTo.Domain);

            var headers = HeadersModel.Init(
                ("User-Agent", SxvtTo.ChromeUA),
                ("Referer", fu.Scheme + "://" + fu.Authority + "/"));

            string json = await Http.Post(
                cfg, "",
                headers: headers,
                timeoutSeconds: 12,
                httpversion: init.httpversion,
                proxy: proxy);

            return SxvtTo.FileUrl(json);
        }
        catch
        {
            return null;
        }
    }

    [HttpGet]
    [Route("sexvietop/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || string.IsNullOrEmpty(q) ||
            !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        // Referer bat buoc va KHAC NHAU:
        //   vcast.name : treo Referer sexvietop -> 403
        //   wogplayer  : khong can, sexvietop van 200
        string referer = link.IndexOf("vcast.name", StringComparison.OrdinalIgnoreCase) >= 0
            ? "https://vcast.name/"
            : SxvtTo.SiteHost + "/";

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", SxvtTo.ChromeUA),
            ("Referer", referer)
        ));

        return Redirect(HostStreamProxy(link, direct));
    }
    #endregion
}
