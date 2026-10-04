using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Viet69be;

public class Viet69beController : BaseSisiController
{
    public Viet69beController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("viet69be")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var menuTask = MenuAsync();

        var cache = await InvokeCacheResult(ipkey($"viet69be:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string page = await GetPageAsync(Viet69beTo.Uri(init.host, search, c, pg));
            var playlists = Viet69beTo.Playlist("viet69be/vidosik", page);

            if (playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await menuTask);
    }

    // Menu dong: nav (ul#menu-home) + tag cloud tu trang chu (1 trang,
    // cache 360). Fetch that moi tra fallback tinh, khong cache.
    async Task<List<MenuItem>> MenuAsync()
    {
        string key = ipkey("viet69be:menu");

        if (hybridCache.TryGetValue(key, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        try
        {
            string home = await GetPageAsync(Viet69beTo.SiteHost + "/");
            var cats = Viet69beTo.NavCats(home);
            var tags = Viet69beTo.TagList(home);

            if (cats.Count > 0 || tags.Count > 0)
            {
                var menu = Viet69beTo.Menu(hostLocal,
                    cats.Count > 0 ? cats : null,
                    tags.Count > 0 ? tags : null);
                hybridCache.Set(key, menu, cacheTime(360), true);
                return menu;
            }
        }
        catch { }

        return Viet69beTo.Menu(hostLocal, null, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        string page = null;
        await httpHydra.GetSpan(url, span => page = span.ToString(), addheaders: PageHeaders(url));

        if (string.IsNullOrEmpty(page))
        {
            page = await Http.Get(
                url,
                timeoutSeconds: Math.Max(20, init.httptimeout),
                httpversion: init.httpversion,
                proxy: proxy,
                headers: PageHeaders(url));
        }

        return page;
    }

    // Chuoi player (da do end-to-end 2026-10-04):
    // detail -> div.movieLoader/button.video2-btn (movie b64, type)
    // -> POST {host}/get.video.php (type=10: get.xvideo.php)
    // -> iframe emb.cd-vs.com/embed/<uuid>
    // -> GET emb.cd-vs.com/api/get-video?id=<uuid>&counter=N
    // -> blogger video.g?token= -> batchexecute -> itag 18/22 (mp4).
    // Token blogger gan IP + het han ~8h + rang UA -> qua proxy, cache 10'.
    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string pageUrl = Viet69beTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return null;

        string memKey = ipkey($"viet69be:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        SemaphorManager semaphore = null;
        if (rch?.enable != true)
        {
            semaphore = new SemaphorManager($"viet69be:view:{pageUrl}", TimeSpan.FromSeconds(30));
            if (!await semaphore.WaitAsync())
                return null;
        }

        try
        {
            if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> current) &&
                current != null && current.Count > 0)
                return current;

            string page = await GetPageAsync(pageUrl);
            var servers = Viet69beTo.VideoServers(page);
            if (servers.Count == 0)
                return null;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var (movie, type, label) in servers)
            {
                n++;
                string prefix = servers.Count == 1 ? "" : "Server " + n + " ";
                foreach (var (itag, link) in await ResolveServerAsync(pageUrl, movie, type))
                {
                    string key = prefix + Viet69beTo.ItagLabel(itag);
                    string k = key;
                    int suffix = 2;
                    while (result.ContainsKey(k))
                        k = key + " " + suffix++;
                    if (!result.ContainsValue(link))
                        result[k] = link;
                }
            }

            if (result.Count == 0)
                return null;

            proxyManager?.Success();
            hybridCache.Set(memKey, result, cacheTime(10));
            return result;
        }
        finally
        {
            semaphore?.Release();
        }
    }

    async Task<List<(int itag, string url)>> ResolveServerAsync(string pageUrl, string movie, string type)
    {
        var res = new List<(int itag, string url)>();
        try
        {
            string endpoint = Viet69beTo.PlayerEndpoint(type);
            string embedHtml = null;

            using (var content = new StringContent(
                "movie_id=" + Uri.EscapeDataString(movie) + "&type=" + Uri.EscapeDataString(type) + "&index=1",
                Encoding.UTF8, "application/x-www-form-urlencoded"))
            {
                embedHtml = await Http.Post(endpoint, content,
                    timeoutSeconds: Math.Max(20, init.httptimeout),
                    headers: PlayerHeaders(pageUrl), proxy: proxy,
                    httpversion: init.httpversion, statusCodeOK: false);
            }

            if (string.IsNullOrEmpty(embedHtml))
            {
                embedHtml = await httpHydra.Post(endpoint,
                    "movie_id=" + Uri.EscapeDataString(movie) + "&type=" + Uri.EscapeDataString(type) + "&index=1",
                    addheaders: PlayerHeaders(pageUrl), statusCodeOK: false);
            }

            string uuid = Viet69beTo.EmbedUuid(embedHtml);
            if (string.IsNullOrEmpty(uuid))
                return res;

            string embedUrl = Viet69beTo.EmbHost + "/embed/" + uuid;
            var embHeaders = EmbHeaders(embedUrl);
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);

            for (int counter = 0; counter <= 2; counter++)
            {
                string apiUrl = Viet69beTo.EmbHost + "/api/get-video?id=" + uuid +
                    "&counter=" + counter;
                string apiJson = await Http.Get(apiUrl,
                    timeoutSeconds: Math.Max(20, init.httptimeout),
                    httpversion: init.httpversion, proxy: proxy, headers: embHeaders);

                if (string.IsNullOrEmpty(apiJson))
                    apiJson = await httpHydra.Get(apiUrl, addheaders: embHeaders, statusCodeOK: false);

                string vurl = Viet69beTo.EmbVideoUrl(apiJson);
                if (string.IsNullOrEmpty(vurl))
                    continue;

                // Server tra thang media (phong thu, hien chi thay blogger).
                if (Viet69beTo.IsMedia(vurl))
                {
                    res.Add((vurl.Contains("m3u8") ? 22 : 18, vurl));
                    break;
                }

                string token = Viet69beTo.BloggerToken(vurl);
                if (string.IsNullOrEmpty(token) || !seenTokens.Add(token))
                    continue;

                var links = await BloggerLinksAsync(token);
                if (links.Count > 0)
                    res.AddRange(links);
                break;
            }
        }
        catch { }

        return res;
    }

    // Blogger batchexecute (F14): BAT BUOC HttpClient thuong + UA goc cua
    // proxy (Shared Http.UserAgent) — impersonate bi 403, UA la thi proxy
    // 403 theo. URL gan IP may + expire ~8h.
    async Task<List<(int itag, string url)>> BloggerLinksAsync(string token)
    {
        var res = new List<(int itag, string url)>();
        try
        {
            string body = "f.req=" + Uri.EscapeDataString(
                "[[[\"WcwnYd\",\"[\\\"" + token + "\\\",null,0]\",null,\"generic\"]]]") + "&";

            string raw;
            using (var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"))
            {
                raw = await Http.Post(
                    "https://www.blogger.com/_/BloggerVideoPlayerUi/data/batchexecute?rpcids=WcwnYd&source-path=%2Fvideo.g&hl=en-US&rt=c",
                    content, timeoutSeconds: Math.Max(20, init.httptimeout),
                    headers: BloggerHeaders(), proxy: proxy,
                    httpversion: init.httpversion, statusCodeOK: false);
            }

            foreach (var (itag, url) in Viet69beTo.BloggerLinks(raw))
            {
                if (!res.Any(x => x.url == url))
                    res.Add((itag, url));
            }
        }
        catch { }

        return res.OrderByDescending(x => x.itag).ToList();
    }

    [HttpGet, Staticache(manually: true)]
    [Route("viet69be/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, pair => StreamRoute(uri, pair.Key, pair.Value)));
    }

    [HttpGet]
    [Route("viet69be/video")]
    [Route("viet69be/video.m3u8")]
    [Route("viet69be/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        // Link goc HLS (giong Viet69z): proxy voi referer SITE, khong dung
        // referer blogger/UA la (upstream tu choi manifest).
        var headers = httpHeaders(init, HeadersModel.Init(
            ("referer", Viet69beTo.SiteHost + "/")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }

    [HttpGet]
    [Route("viet69be/strem")]
    async public Task<ActionResult> Strem(string link, string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (!string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(q))
        {
            var links = await ResolveLinksAsync(uri);
            if (links == null || !links.TryGetValue(q, out link) || string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        var headers = httpHeaders(init, HeadersModel.Init(
            ("referer", Viet69beTo.SiteHost + "/")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }

    string StreamRoute(string uri, string quality, string link)
    {
        string route = Viet69beTo.IsHls(link) ? "video.m3u8" : "video.mp4";
        return $"{host}/viet69be/{route}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(quality)}";
    }

    static IReadOnlyList<HeadersModel> PageHeaders(string referer)
    {
        return HeadersModel.Init(
            ("User-Agent", Viet69beTo.ChromeUA),
            ("Referer", string.IsNullOrEmpty(referer) ? Viet69beTo.SiteHost + "/" : referer),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
        );
    }

    static IReadOnlyList<HeadersModel> PlayerHeaders(string referer)
    {
        return HeadersModel.Init(
            ("User-Agent", Viet69beTo.ChromeUA),
            ("Referer", referer),
            ("Origin", Viet69beTo.SiteHost),
            ("Accept", "*/*"),
            ("X-Requested-With", "XMLHttpRequest")
        );
    }

    static IReadOnlyList<HeadersModel> EmbHeaders(string referer)
    {
        return HeadersModel.Init(
            ("User-Agent", Viet69beTo.ChromeUA),
            ("Referer", referer),
            ("Accept", "application/json, text/plain, */*"),
            ("X-Requested-With", "XMLHttpRequest")
        );
    }

    static IReadOnlyList<HeadersModel> BloggerHeaders()
    {
        return HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", "https://www.blogger.com/"),
            ("X-Same-Domain", "1"),
            ("Accept", "*/*")
        );
    }
}
