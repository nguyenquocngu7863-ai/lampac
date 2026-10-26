using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Lon69;

public class Lon69Controller : BaseSisiController
{
    public Lon69Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lon69")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (pg < 1) pg = 1;

        sort = Lon69To.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"lon69:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string url = Lon69To.BuildUri(init.host, search, c, sort, pg);
            string html = await GetPageAsync(url);
            var playlists = Lon69To.Playlist("lon69/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, Lon69To.Pages(html)));
        });

        if (rch?.enable == true) StatiCacheDisabled = true;
        if (!cache.IsSuccess) return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache, await menuTask, total_pages: cache.Value.total_pages);
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var head = Lon69To.MenuHead(host, search, sort, c);
        var baseMenu = await MenuBaseAsync();
        if (baseMenu != null && baseMenu.Count > 0) head.AddRange(baseMenu);
        return head;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("lon69:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        // Fetch home để lấy categories
        string html = await GetPageAsync(Lon69To.SiteHost + "/");
        var cats = Lon69To.Categories(html ?? "");
        // Tags từ nav menu (giới hạn 4 tag)
        var tags = Lon69To.NavTags(html ?? "");
        var menu = Lon69To.Menu(host, cats, tags);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    async Task<string> GetPageAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var headers = HeadersModel.Init(
            ("User-Agent", Lon69To.ChromeUA),
            ("Referer", Lon69To.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        try
        {
            string html = await Http.Get(url, timeoutSeconds: 20, headers: headers, httpversion: init.httpversion);
            if (!string.IsNullOrEmpty(html) && html.Length > 2000 && !html.Contains("Just a moment"))
                return html;
        }
        catch { }
        try
        {
            string html = await Http.Get(url, timeoutSeconds: 20, headers: headers, proxy: proxy, httpversion: init.httpversion);
            return html;
        }
        catch { return null; }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lon69/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (string.IsNullOrWhiteSpace(uri))
            return OnError("stream_links", refresh_proxy: true);

        string pageUrl = uri.StartsWith("http", System.StringComparison.OrdinalIgnoreCase) ? uri : Lon69To.SiteHost + "/" + uri.Trim('/');
        string detailHtml = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(detailHtml))
            return OnError("stream_links", refresh_proxy: true);

        var iframeMatch = Regex.Match(detailHtml, @"<iframe[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase);
        if (!iframeMatch.Success)
            return OnError("stream_links", refresh_proxy: true);
        string iframeUrl = iframeMatch.Groups[1].Value;

        string iframeHtml = await GetPageAsync(iframeUrl);
        if (string.IsNullOrEmpty(iframeHtml))
            return OnError("stream_links", refresh_proxy: true);

        var srcMatch = Regex.Match(iframeHtml, @"const\s+source\s*=\s*""(https?://[^""]+)""", RegexOptions.IgnoreCase);
        if (!srcMatch.Success)
            srcMatch = Regex.Match(iframeHtml, @"const\s+source\s*=\s*'(https?://[^']+)'", RegexOptions.IgnoreCase);
        if (!srcMatch.Success)
            return OnError("stream_links", refresh_proxy: true);

        string source = srcMatch.Groups[1].Value.Replace("\\u0026", "&");

        var qualitys = new Dictionary<string, string>();
        qualitys["Server 1"] = source;

        return OnResult(new StreamItem() { qualitys = qualitys });
    }
}
