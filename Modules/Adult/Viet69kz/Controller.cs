using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
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
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace Viet69kz;

public class Viet69kzController : BaseSisiController
{
    public Viet69kzController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("viet69kz")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"viet69kz:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string page = await GetPageAsync(Viet69kzTo.Uri(init.host, search, c, pg));
            var playlists = Viet69kzTo.Playlist("viet69kz/vidosik", page);

            if (playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, Viet69kzTo.Menu(host));
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

    async Task<string> GetPlayerAsync(string pageUrl, string id, string server)
    {
        string payload = JsonSerializer.Serialize(new { id, server });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        return await Http.Post(
            Viet69kzTo.PlayerApi,
            content,
            timeoutSeconds: Math.Max(20, init.httptimeout),
            headers: ApiHeaders(pageUrl),
            proxy: proxy,
            httpversion: init.httpversion,
            statusCodeOK: true);
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        string pageUrl = Viet69kzTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return (null, false);

        string memKey = ipkey($"viet69kz:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out (Dictionary<string, string> links, bool userch) cache) &&
            cache.links != null && cache.links.Count > 0)
            return cache;

        SemaphorManager semaphore = null;
        if (rch?.enable != true)
        {
            semaphore = new SemaphorManager($"viet69kz:view:{pageUrl}", TimeSpan.FromSeconds(30));
            if (!await semaphore.WaitAsync())
                return (null, false);
        }

        try
        {
            if (hybridCache.TryGetValue(memKey, out (Dictionary<string, string> links, bool userch) current) &&
                current.links != null && current.links.Count > 0)
                return current;

            string page = await GetPageAsync(pageUrl);
            var (id, servers) = Viet69kzTo.VideoData(page);
            if (string.IsNullOrEmpty(id))
                return (null, false);

            if (servers.Count == 0)
                servers.Add("1");

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string server in servers)
            {
                string apiJson = await GetPlayerAsync(pageUrl, id, server);
                var serverLinks = Viet69kzTo.StreamLinks(apiJson);
                if (serverLinks.Count > 0)
                {
                    AddServerLinks(result, serverLinks, server);
                    continue;
                }

                string iframe = Viet69kzTo.IframeSource(apiJson);
                if (string.IsNullOrEmpty(iframe))
                    continue;

                var iframeLinks = await ResolveIframeAsync(iframe, pageUrl);
                if (iframeLinks.Count > 0)
                    AddServerLinks(result, iframeLinks, server);
            }

            if (result.Count == 0)
                return (null, false);

            var value = (result, rch?.enable == true);
            proxyManager?.Success();
            hybridCache.Set(memKey, value, cacheTime(20));
            return value;
        }
        finally
        {
            semaphore?.Release();
        }
    }

    async Task<Dictionary<string, string>> ResolveIframeAsync(string iframeUrl, string referer)
    {
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var browser = new Shared.PlaywrightCore.PlaywrightBrowser();
            var page = await browser.NewPageAsync(init.plugin, new Dictionary<string, string>()
            {
                ["User-Agent"] = Viet69kzTo.ChromeUA,
                ["Referer"] = referer,
                ["Origin"] = Viet69kzTo.SiteHost
            }, keepopen: false);

            if (page == null)
                return links;

            var requests = new List<string>();
            page.Request += (_, request) =>
            {
                string url = request.Url;
                if (!Viet69kzTo.IsMedia(url))
                    return;

                lock (requests)
                {
                    if (!requests.Contains(url))
                        requests.Add(url);
                }
            };

            await page.GotoAsync(iframeUrl, new PageGotoOptions
            {
                Timeout = 20000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
            await Task.Delay(1500);

            string content = await page.ContentAsync();
            MergeLinks(links, Viet69kzTo.StreamLinks(content));

            try
            {
                string playlist = await page.EvaluateAsync<string>(@"() => {
                    try {
                        const player = jwplayer('player');
                        if (player && player.getPlaylist)
                            return JSON.stringify(player.getPlaylist());
                    } catch (e) {}
                    return '';
                }");
                MergeLinks(links, Viet69kzTo.StreamLinks(playlist));
            }
            catch
            {
            }

            lock (requests)
            {
                foreach (string request in requests)
                    AddMedia(links, request);
            }
        }
        catch
        {
        }

        if (links.Count == 0)
        {
            string content = await Http.Get(
                iframeUrl,
                timeoutSeconds: Math.Max(20, init.httptimeout),
                httpversion: init.httpversion,
                proxy: proxy,
                headers: PageHeaders(referer));
            MergeLinks(links, Viet69kzTo.StreamLinks(content));
        }

        return links;
    }

    static void AddServerLinks(Dictionary<string, string> target, Dictionary<string, string> source, string server)
    {
        foreach (var pair in source)
        {
            string label = source.Count == 1 ? "Server " + server : "Server " + server + " " + pair.Key;
            string key = label;
            int suffix = 2;
            while (target.ContainsKey(key))
                key = label + " " + suffix++;

            if (!target.ContainsValue(pair.Value))
                target[key] = pair.Value;
        }
    }

    static void MergeLinks(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (var pair in source)
        {
            string key = pair.Key;
            int suffix = 2;
            while (target.ContainsKey(key))
                key = pair.Key + " " + suffix++;

            if (!target.ContainsValue(pair.Value))
                target[key] = pair.Value;
        }
    }

    static void AddMedia(Dictionary<string, string> links, string url)
    {
        if (!Viet69kzTo.IsMedia(url) || links.ContainsValue(url))
            return;

        string label = Viet69kzTo.IsHls(url) ? "HLS" : "MP4";
        int suffix = links.Count + 1;
        string key = label;
        while (links.ContainsKey(key))
            key = label + " " + suffix++;

        links[key] = url;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("viet69kz/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var (links, userch) = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        if (userch)
            return OnResult(links);

        return Json(links.ToDictionary(k => k.Key, pair => StreamRoute(uri, pair.Key, pair.Value)));
    }

    [HttpGet]
    [Route("viet69kz/video")]
    [Route("viet69kz/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        return Redirect(HostStreamProxy(link, StreamHeaders(uri)));
    }

    [HttpGet]
    [Route("viet69kz/strem")]
    async public Task<ActionResult> Strem(string link, string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = Viet69kzTo.NormalizePageUrl(uri);
        if (!string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(q))
        {
            var (links, _) = await ResolveLinksAsync(uri);
            if (links == null || !links.TryGetValue(q, out link) || string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        if (string.IsNullOrEmpty(pageUrl))
            pageUrl = Viet69kzTo.SiteHost + "/";

        return Redirect(HostStreamProxy(link, StreamHeaders(pageUrl)));
    }

    string StreamRoute(string uri, string quality, string link)
    {
        string route = Viet69kzTo.IsHls(link) ? "video.m3u8" : "video";
        return $"{host}/viet69kz/{route}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(quality)}";
    }

    static IReadOnlyList<HeadersModel> PageHeaders(string referer)
    {
        return HeadersModel.Init(
            ("User-Agent", Viet69kzTo.ChromeUA),
            ("Referer", string.IsNullOrEmpty(referer) ? Viet69kzTo.SiteHost + "/" : referer),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
        );
    }

    static IReadOnlyList<HeadersModel> ApiHeaders(string referer)
    {
        return HeadersModel.Init(
            ("User-Agent", Viet69kzTo.ChromeUA),
            ("Referer", referer),
            ("Origin", Viet69kzTo.SiteHost),
            ("Accept", "application/json, text/plain, */*"),
            ("X-Requested-With", "XMLHttpRequest")
        );
    }

    static IReadOnlyList<HeadersModel> StreamHeaders(string pageUrl)
    {
        return HeadersModel.Init(
            ("User-Agent", Viet69kzTo.ChromeUA),
            ("Referer", Viet69kzTo.NormalizePageUrl(pageUrl) ?? Viet69kzTo.SiteHost + "/"),
            ("Origin", Viet69kzTo.SiteHost),
            ("Accept", "*/*")
        );
    }
}
