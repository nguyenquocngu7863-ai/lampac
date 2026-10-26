using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
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
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

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
            new MenuItem(){ title = "Danh mục", playlist_url = "submenu", submenu = XHeoVLTo.Categories() }
        };

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            menu,
            total_pages: cache.Value.total_pages
        );
    }

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
