using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace XHeoVL;

public class XHeoVLController : BaseSisiController
{
    public XHeoVLController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("xheovl")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // danh sach "Video lien quan" (popup long-press)
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;
        if (!string.IsNullOrWhiteSpace(search)) pg = 1;

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"xheovl:v1:{search}:{c}:{pg}"), 10, async e =>
        {
            string url = XHeoVLTo.UriMethod(init.host, search, c, pg);
            string html = await XHeoVLTo.RenderPageAsync(url);
            var playlists = XHeoVLTo.Playlist("xheovl/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, XHeoVLTo.Pages(html ?? "")));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var menu = new List<MenuItem>(XHeoVLTo.MenuHead(host, search))
        {
            // menu 2 tầng: Tìm kiếm + Danh mục (mới nhất, girl xinh, ...)
            new MenuItem(){ title = "Danh mục", playlist_url = "submenu", submenu = XHeoVLTo.Categories(host) }
        };

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            menu,
            total_pages: cache.Value.total_pages
        );
    }

    #region info (popup long-press)
    ActionResult EmptyInfo() => Content("{\"items\":[],\"groups\":[]}", "application/json");

    [HttpGet]
    [Route("xheovl/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return EmptyInfo();

        string pageUrl = XHeoVLTo.NormalizePageUrl(uri);
        if (pageUrl == null)
            return EmptyInfo();

        string key = ipkey($"xheovl:info:{pageUrl}");
        if (hybridCache.TryGetValue(key, out string hit) && !string.IsNullOrEmpty(hit))
            return Content(hit, "application/json");

        string html = await GetPageAsync(pageUrl);
        var chips = XHeoVLTo.Chips(html ?? "");
        string json = InfoJson(chips, uri);
        if (chips.Count > 0)
            hybridCache.Set(key, json, cacheTime(360));
        WarmRelated(pageUrl);
        return Content(json, "application/json");
    }

    string InfoJson(List<(string kind, string slug, string name)> chips, string uri)
    {
        var groups = new List<object>();

        void Add(string title, string kind)
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/xheovl?c=" + HttpUtility.UrlEncode("/" + kind + "/" + x.slug)
            }).ToList();
            if (items.Count == 0) return;
            groups.Add(new { title, items });
        }

        Add("Thể loại", "the-loai");
        Add("Tag", "tag");

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = $"{host}/xheovl?rel=" + HttpUtility.UrlEncode(uri)
                }
            }
        });

        return System.Text.Json.JsonSerializer.Serialize(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = XHeoVLTo.NormalizePageUrl(rel);
        if (pageUrl == null)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var playlists = await GetRelatedAsync(pageUrl);

        return PlaylistResult(playlists, false, XHeoVLTo.Menu(host), total_pages: 1);
    }

    async Task<List<PlaylistItem>> GetRelatedAsync(string pageUrl)
    {
        string key = ipkey($"xheovl:rel:{pageUrl}");
        if (hybridCache.TryGetValue(key, out string raw) && !string.IsNullOrEmpty(raw))
        {
            try
            {
                var hit = System.Text.Json.JsonSerializer.Deserialize<List<PlaylistItem>>(raw);
                if (hit != null) return hit;
            }
            catch { }
        }

        var list = XHeoVLTo.PlaylistRelated("xheovl/vidosik",
            await GetPageAsync(pageUrl) ?? "");
        if (list.Count > 0)
            hybridCache.Set(key, System.Text.Json.JsonSerializer.Serialize(list), cacheTime(180));
        return list;
    }

    // warm nen khi popup mo: bam "Video lien quan" la cache hit
    void WarmRelated(string pageUrl)
    {
        _ = Task.Run(async () =>
        {
            try { await GetRelatedAsync(pageUrl); }
            catch { }
        });
    }

    async Task<string> GetPageAsync(string url)
    {
        try
        {
            return await Http.Get(url, timeoutSeconds: 25,
                headers: HeadersModel.Init(
                    ("User-Agent", XHeoVLTo.ChromeUA),
                    ("Referer", XHeoVLTo.SiteHost + "/")));
        }
        catch { return null; }
    }
    #endregion

    // Resolve m3u8 + referer qua XHeoVLTo.ResolveStreamAsync (2 request HTTP), cache 30s.
    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        string pageUrl = XHeoVLTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl)) return null;

        string memKey = ipkey($"xheovl:vid:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cached) &&
            cached != null && cached.Count > 0)
            return cached;

        string resolved = await XHeoVLTo.ResolveStreamAsync(pageUrl, proxy, init.httpversion, Math.Max(15, init.httptimeout));
        if (string.IsNullOrEmpty(resolved))
            resolved = await XHeoVLTo.ResolveStreamAsync(pageUrl, null, init.httpversion, 15);
        if (string.IsNullOrEmpty(resolved)) return null;

        int nl = resolved.IndexOf('\n');
        string link = nl > 0 ? resolved.Substring(0, nl) : resolved;
        if (string.IsNullOrEmpty(link)) return null;

        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HLS"] = link
        };
        hybridCache.Set(memKey, dict, cacheTime(30));
        return dict;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("xheovl/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue("HLS", out string hls) || string.IsNullOrEmpty(hls))
            return OnError("stream_links", refresh_proxy: true);

        // Trả về route URL (giống Jable) để app gọi lại /video.m3u8 qua HostStreamProxy
        // thay vì raw URL ngoài (bị CORS chặn segments).
        var outDict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HLS"] = $"{host}/xheovl/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q=HLS"
        };
        return Json(outDict);
    }

    [HttpGet]
    [Route("xheovl/video")]
    [Route("xheovl/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue("HLS", out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("Referer", XHeoVLTo.SiteHost + "/")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }
}
