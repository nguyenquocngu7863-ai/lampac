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

namespace XNhau;

public class XNhauController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public XNhauController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("xnhau")]
    async public Task<ActionResult> Index(string search, string sort, string c, string t, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
        return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"xnhau:{search}:{sort}:{c}:{t}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            string dbgUrl = XNhauTo.Uri(init.host, search, sort, c, t, pg);
            await httpHydra.GetSpan(dbgUrl, span =>
            {
                string h = span.ToString();
                playlists = XNhauTo.Playlist("xnhau/vidosik", span);
            }, addheaders: HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", "https://xnhau.limo/")
            ));

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache,
            XNhauTo.Menu(host, search, sort, c, t)
        );
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"xnhau:view:{uri}";

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

                string url = XNhauTo.StreamLinksUri(uri);

                // Bookmark/history chi luu id so: mo trang video truc tiep theo id
                // Nếu bookmark lưu số thuần (vd: "540932") hoặc URL đầy đủ chứa ?uri=
                if (!string.IsNullOrEmpty(url)) {
                    if (System.Text.RegularExpressions.Regex.IsMatch(url, @"^[0-9]+$"))
                        url = $"{init.host}/video/{url}/";
                    else if (url.Contains("?uri=")) {
                        // Bookmark lưu URL như: https://xnhau.cab/video/540932/?uri=https://xnhau.cab/video/540932/
                        // Giữ nguyên URL (đã có host và video path)
                        url = url.Split("?uri=")[0]; // Lấy phần trước ?uri= để đảm bảo URL sạch
                        if (url.EndsWith("/")) url = url.TrimEnd('/');
                    } else if (url.StartsWith("/")) {
                        url = $"https://xnhau.cab{url}";
                    }
                }

                if (url == null)
                    return (null, false);

                await httpHydra.GetSpan(url, span =>
                {
                    cache.links = XNhauTo.StreamLinks(span);
                });

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
    [Route("xnhau/vidosik")]
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
            $"{host}/xnhau/strem?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }


    [HttpGet]
    [Route("xnhau/uploader")]
    async public Task<ActionResult> Uploader(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return Json(new { name = "", member = "" });

        string url = XNhauTo.StreamLinksUri(uri);
        if (!string.IsNullOrEmpty(url) && System.Text.RegularExpressions.Regex.IsMatch(url, @"^[0-9]+$"))
            url = $"{init.host}/video/{url}/";
        if (string.IsNullOrEmpty(url))
            return Json(new { name = "", member = "" });

        string memKey = ipkey($"xnhau:uploader:{url}");
        if (!hybridCache.TryGetValue(memKey, out Dictionary<string, string> info))
        {
            info = null;
            await httpHydra.GetSpan(url, span =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(span.ToString(),
                    @"<a class=""avatar"" href=""((?:https?://[^/]+)?/members/[0-9]+/)""[^>]*title=""([^""]+)""");
                if (m.Success)
                {
                    string murl = m.Groups[1].Value;
                    if (murl.StartsWith("/"))
                        murl = $"{init.host}{murl}";
                    info = new Dictionary<string, string>
                    {
                        ["name"] = HttpUtility.HtmlDecode(m.Groups[2].Value),
                        ["member"] = murl
                    };
                }
            });

            if (info == null)
                return OnError("uploader");

            hybridCache.Set(memKey, info, cacheTime(60));
        }

        return Json(info);
    }

    [HttpGet]
    [Route("xnhau/strem")]
    async public Task<ActionResult> Strem(string link, string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (rch?.enable == true && 484 > rch.InfoConnected()?.apkVersion)
        {
            rch.Disabled();
            if (!init.rhub_fallback)
                return OnError("apkVersion", false);
        }

        if (!string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(q))
        {
            var (links, _) = await ResolveLinksAsync(uri);
            if (links == null || !links.TryGetValue(q, out link) || string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        SemaphorManager semaphore = null;
        string semaphoreKey = $"xnhau:strem:{link}";

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
                    ("referer", "https://xnhau.cab/")
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
                        ("referer", "https://xnhau.cab/")
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
