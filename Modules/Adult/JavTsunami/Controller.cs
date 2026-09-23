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

namespace JavTsunami;

public class JavTsunamiController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public JavTsunamiController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javtsunami")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"javtsunami:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            for (int t = 0; t < 3 && (playlists == null || playlists.Count == 0); t++)
            {
                if (t > 0)
                    await Task.Delay(1200);
                await httpHydra.GetSpan(JavTsunamiTo.Uri(init.host, search, c, pg), span =>
                {
                    var pl = JavTsunamiTo.Playlist("javtsunami/vidosik", span.ToString());
                    if (pl.Count > 0)
                        playlists = pl;
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", JavTsunamiTo.ChromeUA),
                    ("Referer", "https://javtsunami.com/")
                ));
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, JavTsunamiTo.Menu(host));
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"javtsunami:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = "https://javtsunami.com" + pageUrl;

        var links = new Dictionary<string, string>();

        string pageHtml = null;
        // javtsunami/turbovidhls hay timeout443/000 chập chờn -> retry
        for (int t = 0; t < 3 && string.IsNullOrEmpty(pageHtml); t++)
        {
            if (t > 0)
                await Task.Delay(1200);
            await httpHydra.GetSpan(pageUrl, span =>
            {
                string s = span.ToString();
                if (s.Contains("thumb-block") || s.Contains("video-player") || s.Contains("<article"))
                    pageHtml = s;
            }, addheaders: HeadersModel.Init(
                ("User-Agent", JavTsunamiTo.ChromeUA),
                ("Referer", "https://javtsunami.com/")
            ));
        }

        if (!string.IsNullOrEmpty(pageHtml))
        {
            foreach (string embed in JavTsunamiTo.EmbedUrls(pageHtml))
            {
                string stream = null;
                string label = "HLS";

                if (embed.Contains("turbovidhls.com") || embed.Contains("turboviplay.com"))
                {
                    string embHtml = null;
                    for (int t = 0; t < 3 && string.IsNullOrEmpty(embHtml); t++)
                    {
                        if (t > 0)
                            await Task.Delay(1200);
                        await httpHydra.GetSpan(embed, span =>
                        {
                            string s = span.ToString();
                            if (s.Contains("data-hash"))
                                embHtml = s;
                        }, addheaders: HeadersModel.Init(
                            ("User-Agent", JavTsunamiTo.ChromeUA),
                            ("Referer", "https://javtsunami.com/")
                        ));
                    }
                    stream = JavTsunamiTo.TurboM3u8(embHtml);
                    label = "Turbo HLS";
                }

                if (string.IsNullOrEmpty(stream))
                    continue;

                // segment nam tren googleusercontent (rate-limit IP server khi proxy)
                // -> ban direct truoc (player tai bang IP nguoi dung), proxy lam du phong
                if (!links.ContainsKey(label))
                    links.TryAdd(label, stream);
                string pkey = label + " (proxy)";
                if (!links.ContainsKey(pkey))
                    links.TryAdd(pkey, stream);

                break;
            }
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javtsunami/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/javtsunami/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("javtsunami/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        // direct: master/variant khong can auth header -> player tai thang bang IP user
        if (!q.Contains("(proxy)"))
            return Redirect(link);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JavTsunamiTo.ChromeUA),
            ("Referer", "https://turbovidhls.com/"),
            ("Origin", "https://turbovidhls.com")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }
}
