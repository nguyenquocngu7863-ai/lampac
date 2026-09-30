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
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // Home pg1 chi lay section phim-moi dau tien, bo cac row duoi
        bool homeFirst = string.IsNullOrEmpty(search) && string.IsNullOrEmpty(c) && pg == 1;

        var cache = await InvokeCacheResult(ipkey($"sexdep:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            // Site LANG: do 5 lan thi chi 1-2 lan tra 200, con lai treo
            // het 15s (dung bo dem timeout) va tra 0 byte. Dung "timeout
            // ngan + retry nhieu" chu KHONG dung "mot lan" — khong retry thi
            // 2/3 so lan mo manh hinh trong.
            var fetchHeaders = HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", "https://z.sexdep.co.uk/")
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

    async Task<List<PlaylistItem>> GetPlaylistWithPlaywrightAsync(string url, bool firstOnly = false)
    {
        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin, new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
                    ["Referer"] = "https://z.sexdep.co.uk/"
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
                    pageUrl = "https://z.sexdep.co.uk" + uri;
                else if (!uri.StartsWith("http"))
                    pageUrl = "https://z.sexdep.co.uk/phim/" + uri.Trim('/');

                string pageHtml = null;
                await httpHydra.GetSpan(pageUrl, span =>
                {
                    pageHtml = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                    ("Referer", "https://z.sexdep.co.uk/")
                ));

                // Fallback to Playwright if HTTP empty
                if (string.IsNullOrEmpty(pageHtml) || pageHtml.Length < 1000)
                {
                    pageHtml = await GetPageWithPlaywrightAsync(pageUrl);
                }

                if (string.IsNullOrEmpty(pageHtml))
                    return (null, false);

                links = SexDepTo.StreamLinks(pageHtml);

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
                    ["Referer"] = "https://z.sexdep.co.uk/"
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
            $"{host}/sexdep/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("sexdep/video")]
    [Route("sexdep/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://z.sexdep.co.uk/")
        ));
        return Redirect(HostStreamProxy(link, direct));
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

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://z.sexdep.co.uk/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}