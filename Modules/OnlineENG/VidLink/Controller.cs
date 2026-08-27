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
using System.Text;
using System.Threading.Tasks;

namespace VidLink;

public class VidLinkController : BaseENGController
{
    const string ApiBase = "https://vidlink.pro/api/b";
    const string EncryptEndpoint = "https://enc-dec.app/api/enc-vidlink";

    public VidLinkController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidlink")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidlink/video")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (id == 0)
            return OnError();

        string embed = $"{init.host}/movie/{id}";
        if (s > 0)
            embed = $"{init.host}/tv/{id}/{s}/{e}";

        var result = await ResolveApi(id, s, e);
        if (result.m3u8 == null)
            result = await black_magic(embed);
        if (result.m3u8 == null)
            return OnError("stream", 502);

        bool directMp4 = result.m3u8.Contains(".mp4", StringComparison.OrdinalIgnoreCase);
        bool remoteRelay = result.m3u8.Contains("noon.mooncase.online/sacdn", StringComparison.OrdinalIgnoreCase);
        string media = directMp4 || remoteRelay
            ? result.m3u8
            : HostStreamProxy(result.m3u8, headers: result.headers);

        if (play)
            return RedirectToPlay(media);

        return ContentTo(VideoTpl.ToJson(
            "play",
            media,
            "English",
            vast: init.vast,
            // VidLink's MP4 CDN failed through generic /proxy/. Let Lampa
            // request it directly with the headers captured by Chromium.
            headers: directMp4 ? result.headers : (remoteRelay ? null : (init.streamproxy ? null : result.headers)),
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        ));
    }


    async Task<(string m3u8, List<HeadersModel> headers)> ResolveApi(long id, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string memKey = $"vidlink:api-standard-h264-v4:{mediaType}:{id}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out (string m3u8, List<HeadersModel> headers) cached))
            return cached;

        try
        {
            // VidLink changed its token protocol: its current player delegates
            // id encryption to enc-dec.app and returns plain JSON from /api/b.
            JObject encrypted = await httpHydra.Get<JObject>(
                $"{EncryptEndpoint}?text={id}",
                addheaders: HeadersModel.Init(
                    ("Accept", "application/json"),
                    ("User-Agent", Http.UserAgent)
                )
            );
            string encryptedId = encrypted?.Value<string>("result");
            if (string.IsNullOrWhiteSpace(encryptedId))
                return default;

            string endpoint = mediaType == "tv"
                ? $"{ApiBase}/tv/{Uri.EscapeDataString(encryptedId)}/{season}/{episode}?multiLang=1"
                : $"{ApiBase}/movie/{Uri.EscapeDataString(encryptedId)}?multiLang=1";

            var requestHeaders = HeadersModel.Init(
                ("Accept", "*/*"),
                ("Accept-Language", "en-US,en;q=0.9"),
                ("Referer", init.host + "/"),
                ("Origin", init.host),
                ("User-Agent", Http.UserAgent),
                // Ask for ordinary browser-compatible H.264 qualities. The
                // WebKit/DASH lane needs signed-cookie MPD handling that Lampa's
                // player does not complete reliably on Android.
                ("X-Playback-Environment", "standard")
            );
            JObject root = await httpHydra.Get<JObject>(endpoint, addheaders: requestHeaders);
            JToken stream = root?["stream"];
            if (stream == null)
                return default;

            JObject streamObject = stream as JObject;
            string playlist = null;
            JObject selectedQuality = null;
            string qualityLabel = null;

            // The standard environment exposes progressive H.264 renditions.
            // Prefer its highest quality over the adaptive HEVC/DASH playlist.
            if (streamObject?["qualities"] is JObject qualities)
            {
                JProperty selected = qualities.Properties()
                    .OrderByDescending(p => ParseQuality(p.Name))
                    .FirstOrDefault(p => p.Value.Type == JTokenType.String
                        ? !string.IsNullOrWhiteSpace(p.Value.Value<string>())
                        : !string.IsNullOrWhiteSpace(p.Value.Value<string>("url")));
                if (selected != null)
                {
                    qualityLabel = selected.Name;
                    playlist = selected.Value.Type == JTokenType.String
                        ? selected.Value.Value<string>()
                        : selected.Value.Value<string>("url");
                    selectedQuality = selected.Value as JObject;
                }
            }

            playlist ??= stream.Type == JTokenType.String
                ? stream.Value<string>()
                : streamObject?.Value<string>("playlist") ?? streamObject?.Value<string>("url");

            if (string.IsNullOrWhiteSpace(playlist) ||
                !Uri.TryCreate(playlist, UriKind.Absolute, out _))
                return default;

            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["User-Agent"] = Http.UserAgent,
                ["Referer"] = init.host + "/",
                ["Origin"] = init.host
            };
            MergeHeaders(merged, streamObject?["playlistHeaders"] as JObject);
            MergeHeaders(merged, streamObject?["headers"] as JObject);
            MergeHeaders(merged, selectedQuality?["headers"] as JObject);

            string delivery = selectedQuality?.Value<string>("type")
                ?? streamObject?.Value<string>("deliveryType")
                ?? streamObject?.Value<string>("type")
                ?? "file";
            bool requiresProxy = selectedQuality?.Value<bool?>("requiresProxy")
                ?? streamObject?.Value<bool?>("requiresProxy")
                ?? false;

            if (requiresProxy && playlist.Contains(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                string relayed = BuildMp4Relay(playlist, merged);
                if (!string.IsNullOrEmpty(relayed))
                    playlist = relayed;
            }
            else if (delivery.Equals("dash", StringComparison.OrdinalIgnoreCase) ||
                     playlist.Contains(".mpd", StringComparison.OrdinalIgnoreCase))
            {
                string relayed = BuildDashRelay(playlist, merged);
                if (!string.IsNullOrEmpty(relayed))
                    playlist = relayed;
            }

            cached = (playlist, HeadersModel.Init(merged));
            hybridCache.Set(memKey, cached, cacheTime(20));
            bool usedRelay = playlist.Contains("noon.mooncase.online", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"VidLink: direct API resolved {delivery} {qualityLabel ?? "auto"}{(usedRelay ? " relay" : "")} ({mediaType}:{id})");
            return cached;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "VidLink direct API failed for {MediaType}:{Id}", mediaType, id);
            return default;
        }
    }

    static string BuildMp4Relay(string mediaUrl, Dictionary<string, string> headers)
    {
        if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out Uri source))
            return null;

        var signedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "auth", "expires", "hash", "key", "sign", "t", "token"
        };
        var query = new List<string>();
        foreach (string item in source.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string key = item.Split('=', 2)[0];
            if (signedKeys.Contains(Uri.UnescapeDataString(key)))
                query.Add(item);
        }

        string serializedHeaders = Uri.EscapeDataString(
            JObject.FromObject(headers).ToString(Newtonsoft.Json.Formatting.None));
        string origin = Uri.EscapeDataString(source.GetLeftPart(UriPartial.Authority));
        query.Add($"headers={serializedHeaders}");
        query.Add($"host={origin}");
        return $"https://noon.mooncase.online/mp{source.AbsolutePath}?{string.Join("&", query)}";
    }

    static string BuildDashRelay(string playlist, Dictionary<string, string> headers)
    {
        if (!Uri.TryCreate(playlist, UriKind.Absolute, out Uri source))
            return null;

        string cookie = headers.FirstOrDefault(i =>
            i.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(cookie))
            return null;

        string signedCookie = Convert.ToBase64String(Encoding.UTF8.GetBytes(cookie))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        string origin = source.GetLeftPart(UriPartial.Authority);
        string query = source.Query.TrimStart('?');
        if (query.Length > 0)
            query += "&";

        query += $"host={Uri.EscapeDataString(origin)}&sc={Uri.EscapeDataString(signedCookie)}";
        return $"https://noon.mooncase.online/sacdn{source.AbsolutePath}?{query}";
    }

    static void MergeHeaders(Dictionary<string, string> target, JObject source)
    {
        if (source == null)
            return;

        foreach (JProperty property in source.Properties())
        {
            string value = property.Value.Value<string>();
            if (!string.IsNullOrWhiteSpace(value))
                target[property.Name] = value;
        }
    }

    static int ParseQuality(string value)
    {
        string digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int quality) ? quality : 0;
    }

    async Task<(string m3u8, List<HeadersModel> headers)> black_magic(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return default;

        try
        {
            string memKey = $"vidlink:hls-first-v3:{uri}";
            if (!hybridCache.TryGetValue(memKey, out (string m3u8, List<HeadersModel> headers) cache))
            {
                using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                {
                    var page = await browser.NewPageAsync(init.plugin, httpHeaders(init)?.ToDictionary(), proxy_data);
                    if (page == null)
                        return default;

                    (string url, List<HeadersModel> headers) mp4Fallback = default;
                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (browser.IsCompleted)
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true, patterCache: "/api/(mercury|venus)$"))
                                return;

                            if (route.Request.Url.Contains("adsco.") || route.Request.Url.Contains("pubtrky.") || route.Request.Url.Contains("clarity."))
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            bool isHls = route.Request.Url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);
                            bool isMp4 = route.Request.Url.Contains(".mp4", StringComparison.OrdinalIgnoreCase);
                            if (isHls || isMp4)
                            {
                                var mediaHeaders = new List<HeadersModel>();
                                foreach (var item in route.Request.Headers)
                                {
                                    if (item.Key.ToLower() is "host" or "accept-encoding" or "connection" or "range")
                                        continue;

                                    mediaHeaders.Add(new HeadersModel(item.Key, item.Value.ToString()));
                                }

                                if (isMp4)
                                {
                                    // Keep the first MP4 only as fallback while
                                    // waiting for a preferred HLS manifest.
                                    if (mp4Fallback.url == null)
                                        mp4Fallback = (route.Request.Url, mediaHeaders);
                                    await route.AbortAsync();
                                    return;
                                }

                                cache.headers = mediaHeaders;
                                PlaywrightBase.ConsoleLog(() => ($"Playwright: SET {route.Request.Url}", cache.headers));
                                browser.completionSource.TrySetResult(route.Request.Url);
                                await route.ContinueAsync();
                                return;
                            }

                            await route.ContinueAsync();
                        }
                        catch (System.Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "VidLink", "id_ejvmtgh5");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, uri);
                    await Task.Delay(2500);

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
                    if (cache.m3u8 == null)
                    {
                        foreach (IFrame frame in page.Frames)
                        {
                            try
                            {
                                string[] resources = await frame.EvaluateAsync<string[]>(
                                    "() => performance.getEntriesByType('resource').map(item => item.name)"
                                );
                                string media = resources?.FirstOrDefault(url =>
                                    url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase));
                                if (media == null)
                                    continue;

                                cache.m3u8 = media;
                                cache.headers = HeadersModel.Init(
                                    ("User-Agent", Http.UserAgent),
                                    ("Referer", frame.Url)
                                );
                                break;
                            }
                            catch { }
                        }
                    }

                    if (cache.m3u8 == null && mp4Fallback.url != null)
                    {
                        cache.m3u8 = mp4Fallback.url;
                        cache.headers = mp4Fallback.headers;
                        Console.WriteLine("VidLink: no HLS; using direct MP4 fallback");
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
