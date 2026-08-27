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

        // vsembed.su only initializes reliably inside the parent embed context
        // used by CineWave. Open that page, select only its VidSrc tab, then
        // capture the resulting HLS; do not scan the other 15 players.
        string embedContext = s > 0
            ? $"https://www.cinewave.su/tv/{id}"
            : $"https://www.cinewave.su/movie/{id}";

        var result = await black_magic(id, embedContext, s, e);
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


    async Task<(string m3u8, List<HeadersModel> headers)> black_magic(long id, string uri, short season, short episode)
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

                    bool captureEnabled = false;
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

                            if (captureEnabled && route.Request.Url.Contains(".m3u8"))
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
                    await Task.Delay(3500);

                    if (season > 0)
                    {
                        await ClickControl(page, $"Season {season}", startsWith: false);
                        await Task.Delay(700);
                        await ClickControl(page, $"E{episode} ·", startsWith: true);
                        await Task.Delay(1000);
                    }

                    // Drop resources loaded by CineWave's default Videasy tab;
                    // only requests created after selecting VidSrc are valid.
                    foreach (IFrame frame in page.Frames)
                    {
                        try { await frame.EvaluateAsync("() => performance.clearResourceTimings()"); }
                        catch { }
                    }

                    captureEnabled = true;
                    bool selected = await ClickControl(page, "VidSrc", startsWith: false);
                    if (!selected)
                    {
                        Console.WriteLine($"VidSrc: CineWave tab not found ({id})");
                        return default;
                    }

                    await Task.Delay(600);
                    await TryStartPlayers(page);
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

    static async Task<bool> ClickControl(IPage page, string label, bool startsWith)
    {
        try
        {
            return await page.EvaluateAsync<bool>(
                @"([label, startsWith]) => {
                    const norm = value => String(value || '').replace(/\s+/g, ' ').trim().toLowerCase();
                    const wanted = norm(label);
                    const matches = Array.from(document.querySelectorAll('body *')).filter(el => {
                        const text = norm(el.textContent);
                        return startsWith ? text.startsWith(wanted) : text === wanted;
                    });
                    if (!matches.length) return false;
                    matches.sort((a, b) => norm(a.textContent).length - norm(b.textContent).length || a.children.length - b.children.length);
                    const leaf = matches[0];
                    const node = leaf.closest('button, [role=""button""], a, [tabindex]') || leaf;
                    node.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
                    node.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
                    node.click();
                    return true;
                }",
                new object[] { label, startsWith }
            );
        }
        catch
        {
            return false;
        }
    }

    static async Task TryStartPlayers(IPage page)
    {
        foreach (IFrame frame in page.Frames.Skip(1))
        {
            try
            {
                await frame.EvaluateAsync(
                    @"() => {
                        const selectors = '[aria-label*=""play"" i], .play-button, .vjs-big-play-button, button:has(svg), video';
                        Array.from(document.querySelectorAll(selectors)).slice(0, 5).forEach(node => {
                            if (node.tagName === 'VIDEO') node.play().catch(() => {});
                            else node.click();
                        });
                    }"
                );
            }
            catch { }
        }
    }
}
