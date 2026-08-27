using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mapple4K;

public sealed class Mapple4KController : BaseENGController
{
    sealed record Candidate(string Url, string Label, List<HeadersModel> Headers);

    public Mapple4KController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/mapple4k")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/mapple4k/video")]
    [Route("lite/mapple4k/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;
        if (id <= 0)
            return OnError();

        List<Candidate> candidates = await ResolveAll(id, s, e);
        if (candidates.Count == 0)
            return OnError("Mapple không trả stream", 502);

        var qualities = new StreamQualityTpl(candidates.Count);
        foreach (Candidate candidate in candidates)
            qualities.Append(HostStreamProxy(candidate.Url, candidate.Headers), candidate.Label);

        if (qualities.IsEmpty)
            return OnError("Mapple không chuẩn bị được stream", 502);

        var first = qualities.Firts();
        if (play)
            return RedirectToPlay(first.link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            "Mapple 4K",
            streamquality: qualities,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        ));
    }

    async Task<List<Candidate>> ResolveAll(long tmdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string memKey = $"mapple4k:playback-api-v3:{mediaType}:{tmdbId}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out List<Candidate> cached))
            return cached;

        var result = new List<Candidate>(1);

        // Current Mapple uses requestToken + /api/playback-init + optional
        // proof-of-work, followed by /api/stream. Let its own page perform that
        // moving protocol and intercept only the final stream response.
        Candidate browserCandidate = await ResolveBrowser(tmdbId, season, episode);
        if (browserCandidate != null)
            result.Add(browserCandidate);

        if (result.Count > 0)
        {
            hybridCache.Set(memKey, result, cacheTime(15));
            Console.WriteLine($"Mapple4K: {result.Count} streams ({mediaType}:{tmdbId}) [{string.Join(", ", result.Select(i => i.Label))}]");
            proxyManager?.Success();
        }
        else
        {
            Console.WriteLine($"Mapple4K: no stream ({mediaType}:{tmdbId})");
            proxyManager?.Refresh();
        }

        return result;
    }

    async Task<Candidate> ResolveBrowser(long tmdbId, short season, short episode)
    {
        string pageUrl = season > 0
            ? $"{init.host}/watch/tv/{tmdbId}-{season}-{episode}?autoPlay=true"
            : $"{init.host}/watch/movie/{tmdbId}?autoPlay=true";

        try
        {
            using var browser = new PlaywrightBrowser(init.priorityBrowser);
            var page = await browser.NewPageAsync(init.plugin, httpHeaders(init)?.ToDictionary(), proxy_data);
            if (page == null)
                return null;

            List<HeadersModel> capturedHeaders = null;
            await page.RouteAsync("**/*", async route =>
            {
                try
                {
                    string url = route.Request.Url;
                    if (url.Contains("/api/stream?", StringComparison.OrdinalIgnoreCase))
                    {
                        var fetched = await route.FetchAsync();
                        string body = await fetched.TextAsync();
                        try
                        {
                            JObject payload = JObject.Parse(body);
                            string streamUrl = payload["data"]?.Value<string>("stream_url")
                                ?? payload.Value<string>("stream_url");
                            if (!string.IsNullOrWhiteSpace(streamUrl))
                            {
                                capturedHeaders = HeadersModel.Init(
                                    ("User-Agent", Http.UserAgent),
                                    ("Referer", init.host + "/"),
                                    ("Origin", init.host)
                                );
                                browser.completionSource.TrySetResult(streamUrl);
                            }
                        }
                        catch { }

                        await route.FulfillAsync(new RouteFulfillOptions
                        {
                            Status = fetched.Status,
                            Body = body,
                            Headers = fetched.Headers
                        });
                        return;
                    }

                    if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                    {
                        capturedHeaders = new List<HeadersModel>();
                        foreach (var header in route.Request.Headers)
                        {
                            if (header.Key.ToLowerInvariant() is "host" or "connection" or "accept-encoding" or "range")
                                continue;
                            capturedHeaders.Add(new HeadersModel(header.Key, header.Value));
                        }

                        browser.completionSource.TrySetResult(url);
                        await route.ContinueAsync();
                        return;
                    }

                    if (browser.IsCompleted || await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                    {
                        if (!browser.IsCompleted)
                            return;
                        await route.AbortAsync();
                        return;
                    }

                    await route.ContinueAsync();
                }
                catch { }
            });

            PlaywrightBase.GotoAsync(page, pageUrl);
            await Task.Delay(3000);
            foreach (IFrame frame in page.Frames)
            {
                try
                {
                    await frame.EvaluateAsync(
                        @"() => {
                            const selectors = '[aria-label*=""play"" i], [data-action*=""play"" i], [class*=""play"" i], .vjs-big-play-button, button:has(svg), video';
                            Array.from(document.querySelectorAll(selectors)).slice(0, 6).forEach(node => {
                                if (node.tagName === 'VIDEO') node.play().catch(() => {});
                                else node.click();
                            });
                        }"
                    );
                }
                catch { }
            }

            string stream = await browser.WaitPageResult(20);
            if (stream == null)
            {
                foreach (IFrame frame in page.Frames)
                {
                    try
                    {
                        string[] resources = await frame.EvaluateAsync<string[]>(
                            "() => performance.getEntriesByType('resource').map(item => item.name)"
                        );
                        stream = resources?.FirstOrDefault(url =>
                            url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase));
                        if (stream != null)
                        {
                            capturedHeaders = HeadersModel.Init(
                                ("User-Agent", Http.UserAgent),
                                ("Referer", frame.Url)
                            );
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (string.IsNullOrWhiteSpace(stream))
                return null;

            capturedHeaders ??= HeadersModel.Init(
                ("User-Agent", Http.UserAgent),
                ("Referer", init.host + "/")
            );
            Console.WriteLine($"Mapple4K: browser HLS resolved ({tmdbId})");
            return new Candidate(stream, "01. Mapple browser · 4K/Auto", capturedHeaders);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Mapple browser fallback failed for {TmdbId}", tmdbId);
            return null;
        }
    }

}
