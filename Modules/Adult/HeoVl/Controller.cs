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

namespace HeoVl;

public class HeoVlController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public HeoVlController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("heovl")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"heovl:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(HeoVlTo.Uri(init.host, search, c, pg), span =>
            {
                playlists = HeoVlTo.Playlist("heovl/vidosik", span.ToString());
            }, addheaders: HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", "https://heovl.im/")
            ));

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, HeoVlTo.Menu(host));
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"heovl:view:{uri}";

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

                // Get embed URL from heovl page
                string pageUrl = uri;
                if (uri.StartsWith("/"))
                    pageUrl = "https://heovl.im" + uri;
                else if (!uri.StartsWith("http"))
                    pageUrl = "https://heovl.im/videos/" + uri.Trim('/');

                string pageHtml = null;
                await httpHydra.GetSpan(pageUrl, span =>
                {
                    pageHtml = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                    ("Referer", "https://heovl.im/")
                ));

                if (string.IsNullOrEmpty(pageHtml))
                    return (null, false);

                var embeds = HeoVlTo.GetEmbeds(pageHtml);
                if (embeds.Count == 0)
                    return (null, false);

                foreach (var (label, embed) in embeds)
                {
                    var streamLinks = await GetStreamFromPlaywrightAsync(embed, pageUrl);
                    if (streamLinks != null)
                    {
                        foreach (var kv in streamLinks)
                        {
                            string key = label;
                            if (streamLinks.Count > 1 || links.ContainsKey(key))
                                key = label + " " + kv.Key;
                            if (!links.ContainsValue(kv.Value))
                                links.TryAdd(key, kv.Value);
                        }
                        if (links.Count > 0)
                            break;
                    }
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

    async Task<Dictionary<string, string>> GetStreamFromPlaywrightAsync(string embedUrl, string referer)
    {
        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin, new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
                    ["Referer"] = referer
                });

                if (page == null)
                    return null;

                var links = new Dictionary<string, string>();

                // Intercept network requests to find m3u8/mp4
                page.Request += (_, request) =>
                {
                    var url = request.Url;
                    if (url.Contains(".m3u8") || url.Contains(".mp4"))
                    {
                        string label = url.Contains(".m3u8") ? "HLS" : "MP4";
                        string key = label + (links.Count == 0 ? "" : " " + (links.Count + 1));
                        if (!links.ContainsValue(url))
                            links.TryAdd(key, url);
                    }
                };

                // Also intercept API config response
                string configJson = null;
                page.Response += (_, response) =>
                {
                    var url = response.Url;
                    if (url.Contains("/config"))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                configJson = await response.TextAsync();
                            }
                            catch { }
                        });
                    }
                };

                await page.GotoAsync(embedUrl, new PageGotoOptions
                {
                    Timeout = 15000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });

                // Wait for JW Player to load
                await Task.Delay(3000);

                // Try to click play button
                try
                {
                    var playButton = await page.QuerySelectorAsync("#play-btn");
                    if (playButton != null)
                    {
                        await playButton.ClickAsync();
                        await Task.Delay(5000);
                    }
                }
                catch { }

                // Try to get sources from JW Player API
                try
                {
                    var sources = await page.EvaluateAsync<object>(@"() => {
                        try {
                            var p = jwplayer('player');
                            if (p && p.getPlaylist) {
                                var pl = p.getPlaylist();
                                if (pl && pl[0] && pl[0].sources) {
                                    return pl[0].sources;
                                }
                            }
                        } catch(e) {}
                        return null;
                    }");

                    if (sources != null)
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(sources);
                        var matches = Regex.Matches(json, @"""file""\s*:\s*""(https?[^""]+)""");
                        foreach (Match m in matches)
                        {
                            string file = m.Groups[1].Value.Replace("\\/", "/");
                            if (file.Contains(".m3u8") || file.Contains(".mp4"))
                            {
                                string label = file.Contains(".m3u8") ? "HLS" : "MP4";
                                string key = label + (links.Count == 0 ? "" : " " + (links.Count + 1));
                                if (!links.ContainsValue(file))
                                    links.TryAdd(key, file);
                            }
                        }
                    }
                }
                catch { }

                // Also check config JSON
                if (links.Count == 0 && !string.IsNullOrEmpty(configJson))
                {
                    var parsed = HeoVlTo.StreamLinksFromConfig(configJson);
                    foreach (var kv in parsed)
                    {
                        if (!links.ContainsValue(kv.Value))
                            links.TryAdd(kv.Key, kv.Value);
                    }
                }

                return links.Count > 0 ? links : null;
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"HeoVl: playwright error {ex.Message}");
            return null;
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("heovl/vidosik")]
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
            $"{host}/heovl/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("heovl/video")]
    [Route("heovl/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://heovl.im/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }

    [HttpGet]
    [Route("heovl/strem")]
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
            ("referer", "https://heovl.im/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
