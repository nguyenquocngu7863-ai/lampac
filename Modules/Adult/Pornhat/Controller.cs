using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Models.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace Pornhat;

public class PornhatController : BaseSisiController
{
    public PornhatController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("pornhat")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"pornhat:v2:{search}:{c}:{pg}"), 10, async e =>
        {
            string target = PornhatTo.Uri(init.host, search, c, pg);
            string html = await FetchHtmlAsync(target);
            var list = PornhatTo.Playlist("pornhat/vidosik", html ?? "");
            if (list == null || list.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((list, PornhatTo.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache, await MenuAsync(search, c), total_pages: cache.Value.total_pages);
    }

    async Task<string> FetchHtmlAsync(string url)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span => html = span.ToString(), addheaders: HeadersModel.Init(
                ("User-Agent", PornhatTo.ChromeUA),
                ("Referer", PornhatTo.SiteHost + "/"),
                ("Accept-Language", "en-US,en;q=0.9")
            ));
            return html;
        }
        catch { return null; }
    }

    static readonly System.Threading.SemaphoreSlim menuLock = new(1, 1);

    async Task<List<MenuItem>> MenuAsync(string search, string c)
    {
        string root = host.TrimEnd('/') + "/pornhat";
        var menu = new List<MenuItem>
        {
            new() { title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };

        if (!string.IsNullOrWhiteSpace(search))
            return menu;

        var trends = new[]
        {
            ("Mới nhất", ""),
            ("Phổ biến", "popular"),
            ("Thịnh hành", "trending")
        };
        var sub = trends.Select(x => new MenuItem(x.Item1, root + (string.IsNullOrEmpty(x.Item2) ? "" : "?c=" + x.Item2))).ToList();
        menu.Add(new MenuItem { title = "Danh sách", playlist_url = "submenu", submenu = sub });

        string menuKey = ipkey("pornhat:v2:tags-menu");
        if (!hybridCache.TryGetValue(menuKey, out List<(string slug, string name)> cachedTags))
            cachedTags = null;
        if (cachedTags == null || cachedTags.Count == 0)
        {
            if (!await menuLock.WaitAsync(8000))
                return menu;
            try
            {
                if (!hybridCache.TryGetValue(menuKey, out cachedTags) || cachedTags == null || cachedTags.Count == 0)
                {
                    string tagHtml = await FetchHtmlAsync(PornhatTo.SiteHost + "/tags/");
                    cachedTags = PornhatTo.Tags(tagHtml ?? "");
                    if (cachedTags.Count > 0)
                        hybridCache.Set(menuKey, cachedTags, cacheTime(720));
                }
            }
            catch { cachedTags = null; }
            finally { menuLock.Release(); }
        }

        if (cachedTags != null && cachedTags.Count > 0)
            menu.AddRange(PornhatTo.TagMenu(host, cachedTags));
        return menu;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("pornhat/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1)) return badInitMsg;

        string page = DetailUrl(uri);
        if (page == null) return OnError("stream_links", refresh_proxy: true);

        string key = ipkey("pornhat:v3:streams:" + page);
        var links = await InvokeCacheResult(key, 20, jsonContext.StreamItem, async e =>
        {
            string html = await FetchHtmlAsync(page);
            var sources = PornhatTo.StreamUrls(html ?? "");
            if (sources.Count == 0) return e.Fail("stream_links", refresh_proxy: true);

            var qualitys = new Dictionary<string, string>();
            foreach (string source in sources)
            {
                string label = PornhatTo.QualityLabel(source);
                if (string.Equals(label, "auto", StringComparison.OrdinalIgnoreCase))
                    label = "360p";
                qualitys[label] = $"{host}/pornhat/video.m3u8?uri={HttpUtility.UrlEncode(page)}&q={HttpUtility.UrlEncode(label)}";
            }
            return e.Success(new StreamItem { qualitys = qualitys });
        });

        if (!links.IsSuccess)
            return OnError(links.ErrorMsg);

        // Keep the module route visible to the player. OnResult would proxy the
        // local route too early and strip its .m3u8 extension.
        return Json(new { qualitys = links.Value.qualitys });
    }

    [HttpGet]
    [Route("pornhat/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true)) return badInitMsg;
        if (string.IsNullOrWhiteSpace(q)) return OnError("stream_links", refresh_proxy: true);

        string page = DetailUrl(uri);
        if (page == null) return OnError("stream_links", refresh_proxy: true);
        string html = await FetchHtmlAsync(page);
        var sources = PornhatTo.StreamUrls(html ?? "");
        string raw = sources.FirstOrDefault(x => PornhatTo.QualityLabel(x).Equals(q, StringComparison.OrdinalIgnoreCase))
            ?? sources.FirstOrDefault();
        string link = PornhatTo.QualityUrl(raw);
        if (string.IsNullOrEmpty(link)) return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", PornhatTo.ChromeUA),
            ("Referer", PornhatTo.SiteHost + "/"),
            ("Origin", PornhatTo.SiteHost)
        ));

        // The /get_file/ path looks like MP4 but returns an HLS master. The
        // fragment tells Lampac's proxy to keep the player-facing .m3u8 suffix;
        // it is stripped before the upstream request.
        return Redirect(HostStreamProxy(link + "#.m3u8", headers));
    }

    string DetailUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        string url = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : PornhatTo.SiteHost + "/" + uri.TrimStart('/');
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || !parsed.Host.Equals("www.pornhat.one", StringComparison.OrdinalIgnoreCase)
            || !parsed.AbsolutePath.StartsWith("/video/", StringComparison.OrdinalIgnoreCase))
            return null;
        return url;
    }

    [HttpGet]
    [Route("pornhat/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        string page = DetailUrl(uri);
        if (page == null) return EmptyGenres();
        string cacheKey = ipkey("pornhat:v2:info:" + page);
        if (hybridCache.TryGetValue(cacheKey, out List<(string kind, string slug, string name)> hit))
            return InfoResult(hit, page);

        string html = await FetchHtmlAsync(page);
        var chips = PornhatTo.Chips(html ?? "");
        hybridCache.Set(cacheKey, chips, cacheTime(360));
        return InfoResult(chips, page);
    }

    static ActionResult EmptyGenres() => new JsonResult(new { items = new object[0], groups = new object[0] });

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, string page)
    {
        var groups = new List<object>();
        foreach (string kind in new[] { "channel", "model", "tag" })
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = host + "/pornhat?c=" + HttpUtility.UrlEncode(x.slug)
            }).ToList();
            if (items.Count == 0) continue;
            groups.Add(new { title = kind == "channel" ? "Kênh" : kind == "model" ? "Diễn viên" : "Thẻ", items });
        }
        var url = host + "/pornhat?rel=" + HttpUtility.UrlEncode(page);
        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = url
                }
            }
        });
        return new JsonResult(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1) return EmptyRelated();
        string page = DetailUrl(rel);
        if (page == null) return EmptyRelated();
        string html = await FetchHtmlAsync(page);
        var list = PornhatTo.Playlist("pornhat/vidosik", html ?? "").Take(20).ToList();
        if (list.Count == 0) return EmptyRelated();
        return PlaylistResult(list, false, new List<MenuItem>
        {
            new() { title = "Tìm kiếm", search_on = "search_on", playlist_url = host.TrimEnd('/') + "/pornhat" }
        }, total_pages: 1);
    }

    static ActionResult EmptyRelated()
        => new JsonResult(new { total_pages = 1, list = new List<PlaylistItem>() });
}
