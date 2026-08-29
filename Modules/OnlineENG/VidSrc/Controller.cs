using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VidSrc;

public class VidSrcController : BaseENGController
{
    public VidSrcController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidsrc")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidsrc/video")]
    [Route("lite/vidsrc/video.m3u8")]
    public async Task<ActionResult> Video(long id, string imdb_id, short s = -1, short e = -1, bool play = false)
    {
        if (id == 0)
            return OnError();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        // The official API is an iframe API, not a direct stream endpoint.
        // Load the documented embed URL inside Lampac's neutral parent page,
        // then interact with the player frame and capture its HLS request.
        string embed = $"{init.host}/embed/movie/{id}";
        if (s > 0)
            embed = $"{init.host}/embed/tv/{id}/{s}/{e}";

        string iframePage = PlaywrightBase.IframeUrl(embed);
        var result = await black_magic(id, iframePage);
        if (result.m3u8 == null)
            return OnError("m3u8", 502);

        string hls = HostStreamProxy(result.m3u8, headers: result.headers);

        if (play)
            return RedirectToPlay(hls);

        return ContentTo(VideoTpl.ToJson(
            "play",
            hls,
            "English",
            vast: init.vast,
            headers: init.streamproxy ? null : result.headers,
            httpContext: HttpContext
        ));
    }


    async Task<(string m3u8, List<HeadersModel> headers)> black_magic(long id, string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return default;

        try
        {
            string memKey = $"vidsrc:black_magic:{uri}";
            if (!hybridCache.TryGetValue(memKey, out (string m3u8, List<HeadersModel> headers) cache))
            {
                using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                {
                    var page = await browser.NewPageAsync(init.plugin, httpHeaders(init)?.ToDictionary(), proxy_data);
                    if (page == null)
                        return default;

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (browser.IsCompleted || Regex.IsMatch(route.Request.Url.Split("?")[0], "\\.(woff2?|vtt|srt|css|ico)$"))
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            if (await PlaywrightBase.AbortOrCache(page, route, fullCacheJS: true))
                                return;

                            if (route.Request.Url.Contains(".m3u8"))
                            {
                                cache.headers = new List<HeadersModel>();
                                foreach (var item in route.Request.Headers)
                                {
                                    if (item.Key.ToLower() is "host" or "accept-encoding" or "connection" or "range")
                                        continue;

                                    cache.headers.Add(new HeadersModel(item.Key, item.Value.ToString()));
                                }

                                PlaywrightBase.ConsoleLog(() => ($"Playwright: SET {route.Request.Url}", cache.headers));
                                browser.completionSource.TrySetResult(route.Request.Url);

                                // Let the manifest complete so VidSrc can set
                                // up its session and child-playlist cookies.
                                await route.ContinueAsync();
                                return;
                            }

                            await route.ContinueAsync();
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_zp5in04r");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, uri);
                    await Task.Delay(2500);

                    // VidSrc changes the play control between nested buttons,
                    // overlays and iframe players. Click the small set of
                    // controls used by its current player.
                    foreach (IFrame frame in page.Frames)
                    {
                        try
                        {
                            await frame.EvaluateAsync(
                                @"() => {
                                    const selectors = '[aria-label*=""play"" i], [data-action*=""play"" i], [class*=""play"" i], .vjs-big-play-button, button:has(svg), video';
                                    Array.from(document.querySelectorAll(selectors)).slice(0, 5).forEach(node => {
                                        if (node.tagName === 'VIDEO') node.play().catch(() => {});
                                        else node.click();
                                    });
                                }"
                            );
                        }
                        catch { }
                    }

                    cache.m3u8 = await browser.WaitPageResult(15);

                    // Service-worker requests can bypass page.RouteAsync.
                    // Recover the HLS URL from each frame's Performance API.
                    if (cache.m3u8 == null)
                    {
                        foreach (IFrame frame in page.Frames)
                        {
                            try
                            {
                                string[] resources = await frame.EvaluateAsync<string[]>(
                                    "() => performance.getEntriesByType('resource').map(item => item.name)"
                                );
                                string hls = resources?.FirstOrDefault(url =>
                                    url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase));
                                if (hls == null)
                                    continue;

                                cache.m3u8 = hls;
                                cache.headers = HeadersModel.Init(
                                    ("User-Agent", Http.UserAgent),
                                    ("Referer", frame.Url)
                                );
                                break;
                            }
                            catch { }
                        }
                    }
                }

                if (cache.m3u8 == null)
                {
                    proxyManager?.Refresh();
                    return default;
                }

                proxyManager?.Success();
                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            return cache;
        }
        catch
        {
            return default;
        }
    }
}
