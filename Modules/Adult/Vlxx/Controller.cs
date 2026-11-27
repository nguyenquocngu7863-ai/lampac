using Microsoft.AspNetCore.Mvc;
using System;
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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Vlxx;

public class VlxxController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public VlxxController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("vlxx")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (!string.IsNullOrWhiteSpace(rel))
            return await Related(rel, pg);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"vlxx:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(VlxxTo.Uri(init.host, search, c, pg), span =>
            {
                playlists = VlxxTo.Playlist("vlxx/vidosik", span.ToString());
            }, addheaders: HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", "https://vlxx.net/")
            ));

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, VlxxTo.Menu(host));
    }

    // ========== POPUP ==========
    static readonly string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static object EmptyPopup() => new { items = new object[0], groups = new object[0] };

    async Task<string> FetchHtmlAsync(string url)
    {
        // .phd chuyen sang .net; giu fallback cho link cu con .phd
        var candidates = new List<string>(2);
        if (url.Contains("//vlxx.phd"))
            candidates.Add(url.Replace("//vlxx.phd", "//vlxx.net"));
        candidates.Add(url);

        foreach (string target in candidates)
        {
            string html = null;
            try
            {
                await httpHydra.GetSpan(target, span => { html = span.ToString(); }, addheaders: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", VlxxTo.SiteHost + "/")));
            }
            catch { }
            if (!string.IsNullOrEmpty(html) && html.Length > 1000)
                return html;
            try
            {
                html = await Http.Get(target, timeoutSeconds: 20, httpversion: init.httpversion, proxy: proxy,
                    headers: HeadersModel.Init(("User-Agent", ChromeUA), ("Referer", VlxxTo.SiteHost + "/")));
            }
            catch { }
            if (!string.IsNullOrEmpty(html) && html.Length > 1000)
                return html;
        }
        return null;
    }

    [HttpGet]
    [Route("vlxx/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(EmptyPopup());

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = VlxxTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(EmptyPopup());

        string memKey = ipkey($"vlxx:info:{pageUrl}");
        if (!hybridCache.TryGetValue(memKey,
            out (List<(string name, string path)> actresses, List<(string name, string path)> cats) info))
        {
            string page = await FetchHtmlAsync(pageUrl);
            info = (VlxxTo.Chips(page, true), VlxxTo.Chips(page, false));
            if (info.actresses.Count > 0 || info.cats.Count > 0)
                hybridCache.Set(memKey, info, cacheTime(360));
        }

        var groups = new List<object>();

        if (info.actresses != null && info.actresses.Count > 0)
            groups.Add(new
            {
                title = "Diễn viên",
                items = info.actresses.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/vlxx?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        if (info.cats != null && info.cats.Count > 0)
            groups.Add(new
            {
                title = "Thể loại",
                items = info.cats.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/vlxx?c={HttpUtility.UrlEncode(x.path)}"
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
                    url = $"{host}/vlxx?rel={HttpUtility.UrlEncode(pageUrl)}",
                    plain = true
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    // ?rel=<uri detail> — 1 trang (SISI: total_pages 0 = con trang vo han).
    async Task<ActionResult> Related(string uri, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = VlxxTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"vlxx:rel:{pageUrl}"), 20, async e =>
        {
            string page = await FetchHtmlAsync(pageUrl);
            var playlists = VlxxTo.Related("vlxx/vidosik", page ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            VlxxTo.Menu(host), total_pages: 1);
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"vlxx:view:{uri}";

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
                string pageUrl = uri;
                if (uri.StartsWith("/"))
                    pageUrl = "https://vlxx.net" + uri;
                else if (!uri.StartsWith("http"))
                    pageUrl = "https://vlxx.net/" + uri.Trim('/');

                // .phd 301 -> .net; doc/ghi tren .net cho on dinh
                string siteUrl = pageUrl.Contains("//vlxx.phd")
                    ? pageUrl.Replace("//vlxx.phd", "//vlxx.net")
                    : pageUrl;

                string pageHtml = null;

                foreach (string target in new[] { siteUrl, pageUrl })
                {
                    try
                    {
                        await httpHydra.GetSpan(target, span =>
                        {
                            pageHtml = span.ToString();
                        }, addheaders: HeadersModel.Init(
                            ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                            ("Referer", "https://vlxx.net/")
                        ));
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(pageHtml) && pageHtml.Length > 1000)
                        break;
                }

                if (string.IsNullOrEmpty(pageHtml))
                    return (null, false);

                var links = new Dictionary<string, string>();
                var ua = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

                // Trang video -> POST /ajax.php lay iframe embed -> mo embed lay file
                foreach (var (server, vid) in VlxxTo.GetServers(pageHtml, pageUrl))
                {
                    foreach (int vs in new int[] { 1, 2 })
                    {
                        try
                        {
                            var req = new HttpRequestMessage(HttpMethod.Post, "https://vlxx.net/ajax.php");
                            req.Content = new StringContent($"vlxx_server={vs}&id={HttpUtility.UrlEncode(vid)}&server={HttpUtility.UrlEncode(server)}", Encoding.UTF8, "application/x-www-form-urlencoded");
                            req.Headers.TryAddWithoutValidation("Referer", siteUrl);
                            req.Headers.TryAddWithoutValidation("Origin", "https://vlxx.net");
                            req.Headers.TryAddWithoutValidation("User-Agent", ua);
                            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

                            var resp = await httpClient.SendAsync(req);
                            string apiJson = resp != null ? await resp.Content.ReadAsStringAsync() : null;
                            if (string.IsNullOrEmpty(apiJson))
                                continue;

                            var pm = Regex.Match(apiJson, @"""player""\s*:\s*""((?:[^""\\]|\\.)*)""");
                            if (!pm.Success)
                                continue;

                            string playerHtml = pm.Groups[1].Value.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\n", "\n");
                            var im = Regex.Match(playerHtml, @"<iframe[^>]+src=[""']([^""']+/embed/[^""']+)[""']");
                            if (!im.Success)
                                continue;

                            string embedUrl = im.Groups[1].Value;
                            if (embedUrl.StartsWith("//"))
                                embedUrl = "https:" + embedUrl;

                            string embedHtml = null;
                            await httpHydra.GetSpan(embedUrl, span =>
                            {
                                embedHtml = span.ToString();
                            }, addheaders: HeadersModel.Init(
                                ("User-Agent", ua),
                                ("Referer", siteUrl)
                            ));

                            var got = VlxxTo.StreamLinksFromEmbed(embedHtml ?? "");
                            foreach (var kv in got)
                            {
                                string key = kv.Key;
                                int dup = 2;
                                while (links.ContainsKey(key))
                                    key = kv.Key + " " + (dup++);
                                if (!links.ContainsValue(kv.Value))
                                    links.TryAdd(key, kv.Value);
                            }

                            if (links.Count > 0)
                                break;
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    if (links.Count > 0)
                        break;
                }

                if (links.Count == 0)
                    return (null, false);

                cache.links = links;
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
    [Route("vlxx/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var (links, userch) = await ResolveLinksAsync(uri);

        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var recomends = await RecomendsAsync(uri);
        if (related)
            return PlaylistResult(recomends, false, null, total_pages: 1);

        var qualitys = links.ToDictionary(k => k.Key, v =>
            $"{host}/vlxx/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}");

        if (userch)
            return Json(new { qualitys, recomends });

        return Json(new { qualitys, recomends });
    }

    // Khoi "lien quan" trang detail -> playlist "tiep theo" trong player (cache 20 phut)
    async Task<List<PlaylistItem>> RecomendsAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return new List<PlaylistItem>();

        string memKey = ipkey($"vlxx:rec:{uri}");
        if (hybridCache.TryGetValue(memKey, out List<PlaylistItem> c) && c != null)
            return c;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : VlxxTo.SiteHost + "/" + uri.TrimStart('/');

        string html = await FetchHtmlAsync(pageUrl);
        var rec = VlxxTo.Related("vlxx/vidosik", html ?? "");
        if (rec == null)
            rec = new List<PlaylistItem>();

        foreach (var pl in rec)
        {
            if (pl?.video != null && !pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                pl.video = $"{host}/{pl.video.TrimStart('/')}";
        }

        if (rec.Count > 0)
            hybridCache.Set(memKey, rec, cacheTime(20));

        return rec;
    }

    [HttpGet]
    [Route("vlxx/video")]
    [Route("vlxx/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://vlxx.net/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }

    [HttpGet]
    [Route("vlxx/strem")]
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

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://vlxx.net/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
