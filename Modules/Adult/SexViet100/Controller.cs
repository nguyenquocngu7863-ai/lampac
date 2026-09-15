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

namespace SexViet100;

public class SexViet100Controller : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public SexViet100Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sexviet100")]
    async public Task<ActionResult> Index(string search, string sort, string c, string t, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // Search của site không có phân trang thật (/search/{kw}/trang/2/ trả 0 item).
        // Trả rỗng cho pg>1 để app dừng ở trang 1 thay vì lặp lại 20 kết quả cũ.
        if (pg > 1 && !string.IsNullOrWhiteSpace(search))
            return Json(new { count = 0, total_pages = 1, menu = SexViet10To.Menu(host), list = new List<PlaylistItem>() });

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"sexviet100:{search}:{sort}:{c}:{t}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(SexViet10To.Uri(init.host, search, sort, c, t, pg), span =>
            {
                playlists = SexViet10To.Playlist("sexviet100/vidosik", span.ToString());
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, SexViet10To.Menu(host));
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"sexviet100:view:{uri}";

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
                    url = $"https://sexviet100.com{uri}";
                else if (!uri.StartsWith("http"))
                    url = $"https://sexviet100.com/{uri}";

                string videoId = null;

                await httpHydra.GetSpan(url, span =>
                {
                    videoId = SexViet10To.GetVideoId(span.ToString());
                });

                if (string.IsNullOrEmpty(videoId))
                    return (null, false);

                var apiUrl = "https://sexviet100.com/api/player";
                var content = new StringContent($"id={videoId}&server=1", Encoding.UTF8, "application/x-www-form-urlencoded");
                var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Content = content;
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");

                var response = await httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                cache.links = SexViet10To.StreamLinks(json);

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
    [Route("sexviet100/vidosik")]
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
            $"{host}/sexviet100/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("sexviet100/video")]
    [Route("sexviet100/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        // Player trong app chỉ dùng hls.js khi URL chứa ".m3u8".
        // Trả link qua route video.m3u8 của chính mình (redirect 302 sang proxy),
        // thay vì đưa thẳng link /proxy/ không đuôi (rơi vào native và gãy).
        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://sexviet100.com/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }

    [HttpGet]
    [Route("sexviet100/strem")]
    async public Task<ActionResult> Strem(string link, string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (!string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(q))
        {
            var (links, _) = await ResolveLinksAsync(uri);
            if (links == null || !links.TryGetValue(q, out link) || string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        SemaphorManager semaphore = null;
        string semaphoreKey = $"sexviet100:strem:{link}";

        if (rch?.enable != true)
        {
            semaphore ??= new SemaphorManager(semaphoreKey, System.TimeSpan.FromSeconds(30));
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return OnError();
        }

        try
        {
            string memKey = ipkey(semaphoreKey);
            if (!hybridCache.TryGetValue(memKey, out string location))
            {
                var headers = httpHeaders(init, HeadersModel.Init(
                    ("sec-fetch-dest", "document"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "none"),
                    ("referer", "https://sexviet100.com/")
                ));

                if (rch?.enable == true)
                {
                    var res = await rch.Headers(init.cors(link, headers, requestInfo), null, headers);
                    location = res.currentUrl;
                }
                else
                {
                    location = await Http.GetLocation(init.cors(link, headers, requestInfo), timeoutSeconds: init.httptimeout, httpversion: init.httpversion, proxy: proxy, headers: headers);
                }

                if (string.IsNullOrEmpty(location) || link == location)
                {
                    proxyManager?.Success();
                    var direct = httpHeaders(init, HeadersModel.Init(
                        ("referer", "https://sexviet100.com/")
                    ));
                    hybridCache.Set(memKey, link, cacheTime(40));
                    return Redirect(HostStreamProxy(link, direct));
                }

                proxyManager?.Success();
                hybridCache.Set(memKey, location, cacheTime(40));
            }

            return Redirect(HostStreamProxy(location));
        }
        finally
        {
            semaphore?.Release();
        }
    }
}
