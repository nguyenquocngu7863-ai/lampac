using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace Javtiful;

public class JavtifulController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public JavtifulController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javtiful")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"javtiful:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(JavtifulTo.Uri(init.host, search, c, pg), span =>
            {
                playlists = JavtifulTo.Playlist("javtiful/vidosik", span.ToString());
            }, addheaders: HeadersModel.Init(
                ("User-Agent", JavtifulTo.ChromeUA),
                ("Referer", "https://javtiful.com/")
            ));

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, JavtifulTo.Menu(host));
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
            {
                string label = u.Contains(".m3u8") ? "HLS" : "MP4";
                string key = label;
                int n = 2;
                while (links.ContainsKey(key))
                    key = label + " " + (n++);
                if (!links.ContainsValue(u))
                    links.TryAdd(key, u);
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
}
