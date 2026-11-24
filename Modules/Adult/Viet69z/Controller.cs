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

namespace Viet69z;

public class Viet69zController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public Viet69zController() : base(ModInit.conf) { }

    [HttpGet]
    [Route("viet69z")]
    async public Task<ActionResult> Index(string search, string sort, string c, string t, int pg = 1, string rel = null)
    {
        if (!string.IsNullOrWhiteSpace(rel))
            return await Related(rel, pg);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"viet69z:{search}:{sort}:{c}:{t}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(Viet69To.Uri(init.host, search, sort, c, t, pg), span =>
            {
                playlists = Viet69To.Playlist("viet69z/vidosik", span.ToString());
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, Viet69To.Menu(host));
    }

    static object EmptyPopup() => new { items = new object[0], groups = new object[0] };

    async Task<string> FetchHtmlAsync(string url)
    {
        string html = null;
        try
        {
            await httpHydra.GetSpan(url, span => { html = span.ToString(); }, addheaders: HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", Viet69To.SiteHost + "/")));
        }
        catch { }
        if (!string.IsNullOrEmpty(html) && html.Length > 1000)
            return html;
        try
        {
            html = await Http.Get(url, timeoutSeconds: 20, httpversion: init.httpversion, proxy: proxy,
                headers: HeadersModel.Init(
                    ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                    ("Referer", Viet69To.SiteHost + "/")));
        }
        catch { }
        return html;
    }

    [HttpGet]
    [Route("viet69z/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(EmptyPopup());

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = Viet69To.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(EmptyPopup());

        string memKey = ipkey($"viet69z:info:{pageUrl}");
        if (!hybridCache.TryGetValue(memKey, out List<(string name, string path)> chips))
        {
            string page = await FetchHtmlAsync(pageUrl);
            chips = Viet69To.Chips(page);
            if (chips != null && chips.Count > 0)
                hybridCache.Set(memKey, chips, cacheTime(360));
        }

        var groups = new List<object>();

        var cats = (chips ?? new List<(string name, string path)>())
            .Where(x => !x.path.StartsWith("tag/")).ToList();
        var tags = (chips ?? new List<(string name, string path)>())
            .Where(x => x.path.StartsWith("tag/")).ToList();

        if (cats.Count > 0)
            groups.Add(new
            {
                title = "Thể loại",
                items = cats.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/viet69z?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        if (tags.Count > 0)
            groups.Add(new
            {
                title = "Thẻ",
                items = tags.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/viet69z?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Phim liên quan",
                    url = $"{host}/viet69z?rel={HttpUtility.UrlEncode(pageUrl)}",
                    plain = true
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> Related(string uri, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = Viet69To.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<List<PlaylistItem>>(
            ipkey($"viet69z:rel:{pageUrl}"), 20, async e =>
        {
            string page = await FetchHtmlAsync(pageUrl);
            var playlists = Viet69To.Related("viet69z/vidosik", page ?? "", pageUrl);

            if (playlists == null || playlists.Count == 0)
                return e.Success(new List<PlaylistItem>());

            return e.Success(playlists);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value, cache.ISingleCache, Viet69To.Menu(host), total_pages: 1);
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"viet69z:view:{uri}";

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
                string url = uri;
                if (uri.StartsWith("/"))
                    url = $"https://viet69z.to{uri}";
                else if (!uri.StartsWith("http"))
                    url = $"https://viet69z.to/{uri}";

                List<string> uuids = null;

                await httpHydra.GetSpan(url, span =>
                {
                    uuids = Viet69To.GetServerUuids(span.ToString());
                });

                if (uuids == null || uuids.Count == 0)
                    return (null, false);

                string apiJson = null;
                foreach (string uuid in uuids)
                {
                    string apiUrl = $"https://emb.cd-vs.com/api/get-video?id={HttpUtility.UrlEncode(uuid)}&counter=0&tried_ids=";
                    apiJson = await Http.Get(apiUrl, timeoutSeconds: 20, headers: HeadersModel.Init(
                        ("referer", url),
                        ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36")
                    ));
                    var test = Viet69To.StreamLinks(apiJson ?? "");
                    if (test.Count > 0 && test["Auto"].Contains(".m3u8"))
                        break;
                    apiJson = null;
                }

                if (string.IsNullOrEmpty(apiJson))
                    return (null, false);

                cache.links = Viet69To.StreamLinks(apiJson);

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
    [Route("viet69z/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var (links, userch) = await ResolveLinksAsync(uri);

        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        if (userch)
            return OnResult(links);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/viet69z/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("viet69z/video")]
    [Route("viet69z/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        // Link gốc HLS (cd-vs m3u8, segment đuôi .png nhưng ruột TS sạch) —
        // phát qua route video.m3u8 để app dùng hls.js.
        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://viet69z.to/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }

    [HttpGet]
    [Route("viet69z/strem")]
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
        string semaphoreKey = $"viet69z:strem:{link}";

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
                    ("referer", "https://viet69z.to/")
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
                        ("referer", "https://viet69z.to/")
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
