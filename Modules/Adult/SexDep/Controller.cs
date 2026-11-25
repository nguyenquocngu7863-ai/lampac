using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace SexDep;

public class SexDepController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public SexDepController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sexdep")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (!string.IsNullOrWhiteSpace(rel))
            return await Related(rel, pg);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // Home pg1 chi lay section phim-moi dau tien, bo cac row duoi
        bool homeFirst = string.IsNullOrEmpty(search) && string.IsNullOrEmpty(c) && pg == 1;

        var cache = await InvokeCacheResult(ipkey($"sexdep:v3:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            // Site LANG: do 5 lan thi chi 1-2 lan tra 200, con lai treo
            // het 15s (dung bo dem timeout) va tra 0 byte. Dung "timeout
            // ngan + retry nhieu" chu KHONG dung "mot lan" — khong retry thi
            // 2/3 so lan mo manh hinh trong.
            var fetchHeaders = HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", "https://sexngot.blog/")
            );

            for (int attempt = 0; attempt < SexDepTo.FetchAttempts; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(400);

                try
                {
                    await httpHydra.GetSpan(SexDepTo.Uri(init.host, search, c, pg), span =>
                    {
                        string h = span.ToString();
                        if (string.IsNullOrEmpty(h))
                            return;

                        if (homeFirst)
                            h = SexDepTo.FirstSectionOnly(h);

                        var got = SexDepTo.Playlist("sexdep/vidosik", h);
                        if (got != null && got.Count > 0)
                            playlists = got;
                    }, addheaders: fetchHeaders);
                }
                catch { }

                if (playlists != null && playlists.Count > 0)
                    break;
            }

            // Fallback to Playwright if HTTP returns empty
            if (playlists == null || playlists.Count == 0)
            {
                if (rch?.enable != true && init.priorityBrowser != "http")
                {
                    playlists = await GetPlaylistWithPlaywrightAsync(SexDepTo.Uri(init.host, search, c, pg), homeFirst);
                }
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, SexDepTo.Menu(host));
    }

    // ===== POPUP: /sexdep/genres?uri=<uri detail> + /sexdep?rel=<uri detail> =====

    static object EmptyPopup() => new { items = new object[0], groups = new object[0] };

    // Trang detail cua site hay treo (do 5 lan chi 1-2 lan 200) -> vai lan thu ngan,
    // roi moi den Playwright. Popup can nhanh nen khong thu qua 3 lan.
    async Task<string> FetchDetailHtmlAsync(string pageUrl)
    {
        var fetchHeaders = HeadersModel.Init(
            ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
            ("Referer", SexDepTo.SiteHost + "/")
        );

        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(300);

            string html = null;
            try
            {
                await httpHydra.GetSpan(pageUrl, span =>
                {
                    string h = span.ToString();
                    if (!string.IsNullOrEmpty(h) && h.Length > 2000)
                        html = h;
                }, addheaders: fetchHeaders);
            }
            catch { }

            if (html != null)
                return html;
        }

        if (rch?.enable != true && init.priorityBrowser != "http")
            return await GetPageWithPlaywrightAsync(pageUrl);

        return null;
    }

    [HttpGet]
    [Route("sexdep/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(EmptyPopup());

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = SexDepTo.DetailUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(EmptyPopup());

        string memKey = ipkey($"sexdep:info:{pageUrl}");
        if (!hybridCache.TryGetValue(memKey,
            out (List<(string name, string path)> countries,
                 List<(string name, string path)> genres,
                 List<(string name, string path)> tags) info))
        {
            string page = await FetchDetailHtmlAsync(pageUrl);
            if (!string.IsNullOrEmpty(page))
            {
                info = SexDepTo.Chips(page);
                if ((info.countries?.Count ?? 0) + (info.genres?.Count ?? 0) + (info.tags?.Count ?? 0) > 0)
                    hybridCache.Set(memKey, info, cacheTime(360));
            }
        }

        var groups = new List<object>();

        if (info.countries != null && info.countries.Count > 0)
            groups.Add(new
            {
                title = "Quốc gia",
                items = info.countries.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/sexdep?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        if (info.genres != null && info.genres.Count > 0)
            groups.Add(new
            {
                title = "Thể loại",
                items = info.genres.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/sexdep?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        if (info.tags != null && info.tags.Count > 0)
            groups.Add(new
            {
                title = "Thẻ",
                items = info.tags.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/sexdep?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    url = $"{host}/sexdep?rel={HttpUtility.UrlEncode(pageUrl)}",
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

        string pageUrl = SexDepTo.DetailUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"sexdep:rel:{pageUrl}"), 20, async e =>
            {
                string page = await FetchDetailHtmlAsync(pageUrl);
                var playlists = SexDepTo.Related(page ?? "", pageUrl);

                if (playlists == null || playlists.Count == 0)
                    return e.Success((new List<PlaylistItem>(), 1));

                return e.Success((playlists, 1));
            });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            SexDepTo.Menu(host), total_pages: 1);
    }

    async Task<List<PlaylistItem>> GetPlaylistWithPlaywrightAsync(string url, bool firstOnly = false)
    {
        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin, new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
                    ["Referer"] = "https://sexngot.blog/"
                });

                if (page == null)
                    return null;

                await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = 20000,
                    WaitUntil = WaitUntilState.Load
                });

                await Task.Delay(3000);

                var content = await page.ContentAsync();
                if (firstOnly)
                    content = SexDepTo.FirstSectionOnly(content);
                return SexDepTo.Playlist("sexdep/vidosik", content);
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"SexDep: playlist playwright error {ex.Message}");
            return null;
        }
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"sexdep:view:{uri}";

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
                var links = new Dictionary<string, string>();

                string pageUrl = uri;
                if (uri.StartsWith("/"))
                    pageUrl = "https://sexngot.blog" + uri;
                else if (!uri.StartsWith("http"))
                    pageUrl = "https://sexngot.blog/phim/" + uri.Trim('/');

                string pageHtml = null;
                await httpHydra.GetSpan(pageUrl, span =>
                {
                    pageHtml = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                    ("Referer", "https://sexngot.blog/")
                ));

                // Fallback to Playwright if HTTP empty
                if (string.IsNullOrEmpty(pageHtml) || pageHtml.Length < 1000)
                {
                    pageHtml = await GetPageWithPlaywrightAsync(pageUrl);
                }

                if (string.IsNullOrEmpty(pageHtml))
                    return (null, false);

                links = SexDepTo.StreamLinks(pageHtml, pageUrl);

                if (links == null || links.Count == 0)
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

    async Task<string> GetPageWithPlaywrightAsync(string url)
    {
        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin, new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
                    ["Referer"] = "https://sexngot.blog/"
                });

                if (page == null)
                    return null;

                await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = 20000,
                    WaitUntil = WaitUntilState.Load
                });

                await Task.Delay(3000);
                return await page.ContentAsync();
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"SexDep: page playwright error {ex.Message}");
            return null;
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sexdep/vidosik")]
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
        {
            // Blogger la MP4 goc (googlevideo) -> route .mp4, con lai la HLS
            string route = v.Value.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0
                ? "video.m3u8" : "video.mp4";
            return $"{host}/sexdep/{route}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}";
        }));
    }

    [HttpGet]
    [Route("sexdep/video")]
    [Route("sexdep/video.m3u8")]
    [Route("sexdep/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        bool blog = link.IndexOf("blogger.com/video.g", StringComparison.OrdinalIgnoreCase) >= 0;

        if (blog)
        {
            link = await ResolveBloggerAsync(link);
            if (string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        // googlevideo.com tra 403 khi UA/Referer la cua site (da do 2026-11-25):
        // phai dung UA Chrome + Referer blogger.
        var direct = blog
            ? httpHeaders(init, HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"),
                ("referer", "https://www.blogger.com/")
            ))
            : httpHeaders(init, HeadersModel.Init(
                ("referer", "https://sexngot.blog/")
            ));
        return Redirect(HostStreamProxy(link, direct));
    }

    // Embed Blogger -> batchexecute (rpc WcwnYd) -> URL googlevideo tai duoc bang
    // HTTP thuong. KHONG dung URL nhung trong HTML trang video.g: ban do chi qua
    // duoc TLS gia lap trinh duyet (curl_cffi 206) con .NET bi 403 (do 2026-11-25).
    async Task<string> ResolveBloggerAsync(string embedUrl)
    {
        string token = SexDepTo.BloggerToken(embedUrl);
        if (string.IsNullOrEmpty(token))
            return null;

        string memKey = $"sexdep:blogger:v2:{token}";
        if (hybridCache.TryGetValue(memKey, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        // KHONG tu dat User-Agent: URL googlevideo bi rang buoc DUNG UA da goi
        // batchexecute (do 2026-11-25: batch UA 120 -> proxy UA 146 bi 403).
        // Bo trong de ca 2 phia dung Http.UserAgent cua Lampac.
        var blogHeaders = HeadersModel.Init(
            ("Referer", "https://www.blogger.com/"),
            ("X-Same-Domain", "1"));

        string resp = null;
        try
        {
            resp = await Http.Post(SexDepTo.BatchUrl, SexDepTo.BatchBody(token),
                headers: blogHeaders,
                timeoutSeconds: 15,
                httpversion: init.httpversion,
                proxy: proxy);
        }
        catch { }

        string mp4 = SexDepTo.BloggerBestUrl(resp);
        if (string.IsNullOrEmpty(mp4))
            return null;

        // URL gan IP client + het han ~8h -> cache ngan, van phai qua proxy Lampac
        hybridCache.Set(memKey, mp4, cacheTime(5));
        return mp4;
    }

    [HttpGet]
    [Route("sexdep/strem")]
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

        bool blog = link.IndexOf("blogger.com/video.g", StringComparison.OrdinalIgnoreCase) >= 0;

        if (blog)
        {
            link = await ResolveBloggerAsync(link);
            if (string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        // googlevideo.com tra 403 khi UA/Referer la cua site (da do 2026-11-25):
        // phai dung UA Chrome + Referer blogger.
        var direct = blog
            ? httpHeaders(init, HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"),
                ("referer", "https://www.blogger.com/")
            ))
            : httpHeaders(init, HeadersModel.Init(
                ("referer", "https://sexngot.blog/")
            ));
        return Redirect(HostStreamProxy(link, direct));
    }
}