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
                    // `vid` la id cua chinh video nay. Bat buoc phai loc theo no:
                    // trang player nap quang cao chay song song va cac request
                    // m3u8 cua quang cao cung di qua trinh bat network, nen
                    // khong loc thi app nhan nham phim voi quang cao.
                    var (_, vid) = HeoVlTo.ParseEmbed(embed);

                    var streamLinks = await GetStreamFromPlaywrightAsync(embed, pageUrl, vid);
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

    async Task<Dictionary<string, string>> GetStreamFromPlaywrightAsync(string embedUrl, string referer, string vid)
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

                // PHẢI xét đuôi đường dẫn, không xét `Contains(".m3u8")`. JW
                // Player bắn ping analytics dạng
                // `prd.jwpltx.com/.../ping.gif?...&mu=<...master.m3u8...>`,
                // chuỗi `.m3u8` nằm trong query nen Contains nhận nhầm, app
                // nhận 1 pixel 204 No Content làm link chết.
                bool IsMedia(string url)
                {
                    try
                    {
                        var path = new Uri(url).AbsolutePath;
                        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                }

                // Chi nhan m3u8 cua dung video nay. Khong co `vid` (URL embed
                // doi hinh dang) thi bo qua chuc nhan, vi luc do khong phan biet
                // duoc video nao voi quang cao nao.
                bool Accept(string url)
                {
                    if (!IsMedia(url))
                        return false;

                    if (!string.IsNullOrEmpty(vid) && !url.Contains(vid))
                        return false;

                    if (links.ContainsValue(url))
                        return false;

                    // So chay tu 1 cho het, khong bo qua link dau tien.
                    links.TryAdd((url.Contains(".m3u8") ? "HLS" : "MP4") + " " + (links.Count + 1), url);
                    return true;
                }

                // Intercept network requests to find m3u8/mp4
                page.Request += (_, request) =>
                {
                    var url = request.Url;
                    Accept(url);
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
                            string file = HeoVlTo.NormalizeStreamUrl(m.Groups[1].Value);
                            Accept(file);
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
                        if (!string.IsNullOrEmpty(vid) && !kv.Value.Contains(vid))
                            continue;

                        Accept(kv.Value);
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
