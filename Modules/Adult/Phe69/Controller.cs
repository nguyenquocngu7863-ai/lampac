using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Phe69;

public class Phe69Controller : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public Phe69Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("phe69")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"phe69:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(Phe69To.Uri(init.host, search, c, pg), span =>
            {
                playlists = Phe69To.Playlist("phe69/vidosik", span.ToString());
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, Phe69To.Menu(host));
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"phe69:view:{uri}";

        if (rch?.enable != true)
        {
            semaphore ??= new SemaphorManager(semaphoreKey, System.TimeSpan.FromSeconds(30));
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return (null, false);
        }

        try
        {
            string memKey = ipkey(semaphoreKey);
            if (!hybridCache.TryGetValue(memKey, out (Dictionary<string, string> links, bool userch) cache))
            {
                if (init.httpversion == 1)
                    httpHydra.RegisterHttp(httpClient);

                string url = uri;
                if (uri.StartsWith("/"))
                    url = $"https://phe69.shop{uri}";
                else if (!uri.StartsWith("http"))
                    url = $"https://phe69.shop/{uri}";

                string iframe = null;

                await httpHydra.GetSpan(url, span =>
                {
                    iframe = Phe69To.GetIframe(span.ToString());
                });

                if (string.IsNullOrEmpty(iframe))
                    return (null, false);

                string playerHtml = null;

                await httpHydra.GetSpan(iframe, span =>
                {
                    playerHtml = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("referer", "https://phe69.shop/")
                ));

                if (string.IsNullOrEmpty(playerHtml))
                    return (null, false);

                cache.links = Phe69To.StreamLinks(Phe69To.UnpackDeanEdwards(playerHtml));

                if ((cache.links == null || cache.links.Count == 0) && !string.IsNullOrEmpty(uri))
                {
                    string slug = uri.TrimEnd('/').Split('/')[^1];
                    if (!string.IsNullOrEmpty(slug))
                    {
                        System.Console.WriteLine($"Phe69: unpack empty, fallback slug guess {slug}");
                        cache.links = new Dictionary<string, string>()
                        {
                            ["Auto"] = $"https://phim.phe69.uk/{slug}.mp4"
                        };
                    }
                }

                if (cache.links == null || cache.links.Count == 0)
                    return (null, false);

                proxyManager?.Success();
                cache.userch = rch?.enable == true;
                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            return (cache.links, cache.userch);
        }
        finally
        {
            semaphore?.Release();
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("phe69/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    reset:
        var (links, userch) = await ResolveLinksAsync(uri);

        if (links == null || links.Count == 0)
        {
            if (IsRhubFallback())
                goto reset;

            return OnError("stream_links", refresh_proxy: true);
        }

        if (userch)
            return OnResult(links);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/phe69/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("phe69/video")]
    [Route("phe69/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://phe69.shop/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
