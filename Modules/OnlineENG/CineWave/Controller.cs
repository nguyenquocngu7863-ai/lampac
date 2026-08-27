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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace CineWave;

/// <summary>
/// CineWave bridge: https://watch.cinewave.qzz.io
///
/// Play id = base64url( XOR(payload, repeating key "cinewvve") ), padding trimmed.
///   - movie payload:  "movie:{tmdbId}"
///   - tv payload:     "tv-{id}-{season}-{episode}" where every char at
///                     absolute index 2, 10, 18... (i += 8 starting at 2)
///                     of "{id}-{s}-{e}" is XORed with 0x17.
/// Both rules verified against live play URLs (movie ids of 4-7 digits, tv
/// ids of 4-7 digits, 7-digit id sample also masks the episode char).
/// </summary>
public sealed class CineWaveController : BaseOnlineController<ModuleConf>
{
    static readonly string[] PlayerLabels =
    [
        "videasy", "VidFast", "FilmU", "Vares", "VidGod", "VidKing",
        "VixSrc", "VidLink", "VidZee", "VidZee V2", "autoembed", "VidRock",
        "VidSrc", "111movies", "SuperEmbed", "2Embed"
    ];

    sealed record CineWaveCandidate(string Url, string Label, List<HeadersModel> Headers);

    static readonly byte[] XorKey = "cinewvve"u8.ToArray();

    static readonly Regex VideoUrlRegex = new(
        @"\.m3u8(?:[?#]|$)|\.mp4(?:[?#]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    static readonly Regex AdUrlRegex = new(
        @"(doubleclick|googlesyndication|adservice|popads|/ads/|vast\.xml|ping\.gif|silent\.mp4|imasdk)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public CineWaveController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/cinewave")]
    public async Task<ActionResult> Index(
        string id,
        string imdb_id,
        long tmdb_id,
        string title,
        string original_title,
        string source,
        int serial = 0,
        short s = -1,
        short e = -1,
        bool play = false,
        bool rjson = false
    )
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        long tmdbId = ResolveTmdbId(tmdb_id, id, source);
        if (tmdbId <= 0)
            return OnError("CineWave cần TMDB id", 400);

        bool isSeries = serial == 1;

        if (isSeries && s <= 0)
            return await Seasons(tmdbId, imdb_id, title, original_title);

        if (isSeries && e <= 0)
            return await Episodes(tmdbId, imdb_id, title, original_title, s);

        if (isSeries)
            return await EpisodeResponse(tmdbId, imdb_id, title, original_title, s, e, play);

        if (play)
        {
            List<CineWaveCandidate> candidates = await ResolveCandidates(tmdbId, imdb_id, -1, -1);
            if (candidates.Count == 0)
                return OnError("CineWave không tìm thấy stream", 502);

            CineWaveCandidate first = candidates[0];
            return RedirectToPlay(HostStreamProxy(first.Url, first.Headers));
        }

        var tpl = new MovieTpl(title, original_title, 1);
        tpl.Append(
            "CineWave.su · tất cả player",
            accsArgs($"{host}/lite/cinewave/video?tmdb_id={tmdbId}&imdb_id={HttpUtility.UrlEncode(imdb_id)}"),
            "call",
            quality: "4K / HLS",
            details: "Ưu tiên HLS • direct đứng sau"
        );

        return ContentTpl(tpl);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/cinewave/video")]
    public async Task<ActionResult> Video(
        long tmdb_id,
        string imdb_id,
        string title,
        string original_title,
        short s = -1,
        short e = -1,
        bool play = false
    )
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (tmdb_id <= 0)
            return OnError("CineWave cần TMDB id", 400);

        List<CineWaveCandidate> candidates = await ResolveCandidates(tmdb_id, imdb_id, s, e);
        if (candidates.Count == 0)
            return OnError("CineWave không resolve được stream", 502);

        var qualities = new StreamQualityTpl(candidates.Count);
        foreach (CineWaveCandidate candidate in candidates)
            qualities.Append(HostStreamProxy(candidate.Url, candidate.Headers), candidate.Label);

        if (qualities.IsEmpty)
            return OnError("CineWave không chuẩn bị được stream", 502);

        var first = qualities.Firts();
        if (play)
            return RedirectToPlay(first.link);

        string name = title ?? original_title ?? "CineWave";
        if (s > 0 && e > 0)
            name += $" S{s:00}E{e:00}";

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            name,
            streamquality: qualities,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        ));
    }

    async Task<ActionResult> Seasons(long tmdbId, string imdbId, string title, string original_title)
    {
        List<(int Number, int EpisodeCount)> seasons = await GetSeasonRows(tmdbId, imdbId);
        if (seasons.Count == 0)
            return OnError("CineWave không đọc được danh sách season", 502);

        var tpl = new SeasonTpl(seasons.Count);
        foreach (var season in seasons)
        {
            tpl.Append(
                $"Season {season.Number}",
                BuildIndexUrl(tmdbId, imdbId, title, original_title, serial: 1, s: season.Number),
                season.Number
            );
        }

        return ContentTpl(tpl);
    }

    async Task<ActionResult> Episodes(long tmdbId, string imdbId, string title, string original_title, short season)
    {
        List<(int Number, string Name)> episodes = await GetEpisodeRows(tmdbId, imdbId, season);
        if (episodes.Count == 0)
            return OnError($"CineWave không đọc được danh sách tập Season {season}", 502);

        var tpl = new EpisodeTpl(episodes.Count);
        foreach (var ep in episodes)
        {
            if (ep.Number <= 0 || ep.Number > short.MaxValue)
                continue;

            short number = (short)ep.Number;
            string name = string.IsNullOrWhiteSpace(ep.Name)
                ? $"Episode {number:00}"
                : $"{number}. {ep.Name}";

            tpl.Append(
                name,
                title ?? original_title ?? "CineWave",
                season.ToString(),
                number.ToString(),
                BuildIndexUrl(tmdbId, imdbId, title, original_title, serial: 1, s: season, e: number),
                "call",
                streamlink: BuildIndexUrl(tmdbId, imdbId, title, original_title, serial: 1, s: season, e: number, play: true)
            );
        }

        return ContentTpl(tpl);
    }

    async Task<ActionResult> EpisodeResponse(long tmdbId, string imdbId, string title, string original_title, short season, short episode, bool play)
    {
        if (season <= 0 || episode <= 0)
            return OnError("CineWave episode cần season và episode", 400);

        List<CineWaveCandidate> candidates = await ResolveCandidates(tmdbId, imdbId, season, episode);
        if (candidates.Count == 0)
            return OnError("CineWave không resolve được stream", 502);

        var qualities = new StreamQualityTpl(candidates.Count);
        foreach (CineWaveCandidate candidate in candidates)
            qualities.Append(HostStreamProxy(candidate.Url, candidate.Headers), candidate.Label);

        if (qualities.IsEmpty)
            return OnError("CineWave không chuẩn bị được stream", 502);

        var first = qualities.Firts();
        if (play)
            return RedirectToPlay(first.link);

        string name = (title ?? original_title ?? "CineWave") + $" S{season:00}E{episode:00}";
        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            name,
            streamquality: qualities,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        ));
    }

    // ═══════════════════════════ cinewave.su multi-player resolver ═══════════════════════════

    async Task<List<CineWaveCandidate>> ResolveCandidates(long tmdbId, string imdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string memKey = $"cinewave-su:all:{mediaType}:{tmdbId}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out List<CineWaveCandidate> cached))
            return cached;

        var candidates = new List<CineWaveCandidate>(PlayerLabels.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object sync = new();
        string activePlayer = "videasy";
        string pageUrl = season > 0
            ? $"{SiteHost()}/tv/{tmdbId}"
            : $"{SiteHost()}/movie/{tmdbId}";

        try
        {
            using var browser = new PlaywrightBrowser(init.priorityBrowser);
            var page = await browser.NewPageAsync(init.plugin, httpHeaders(init)?.ToDictionary(), proxy_data);
            if (page == null)
                return candidates;

            await page.RouteAsync("**/*", async route =>
            {
                try
                {
                    string url = route.Request.Url;
                    if (VideoUrlRegex.IsMatch(url) && !AdUrlRegex.IsMatch(url))
                    {
                        var headers = new List<HeadersModel>();
                        foreach (var item in route.Request.Headers)
                        {
                            if (!BlockedHeader(item.Key))
                                headers.Add(new HeadersModel(item.Key, item.Value.ToString()));
                        }

                        lock (sync)
                        {
                            if (seen.Add(url))
                                candidates.Add(new CineWaveCandidate(url, activePlayer, headers));
                        }

                        await route.AbortAsync();
                        return;
                    }

                    if (AdUrlRegex.IsMatch(url))
                    {
                        await route.AbortAsync();
                        return;
                    }

                    if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                        return;

                    await route.ContinueAsync();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Debug(ex, "CineWave route failed");
                }
            });

            PlaywrightBase.GotoAsync(page, pageUrl);
            await Task.Delay(3500);

            if (season > 0)
            {
                await ClickPlayerControl(page, $"Season {season}", startsWith: false);
                await Task.Delay(700);
                await ClickPlayerControl(page, $"E{episode} ·", startsWith: true);
                await Task.Delay(1200);
            }

            int perPlayerDelay = Math.Clamp(init.resolveSeconds * 1000 / PlayerLabels.Length, 1500, 2500);
            foreach (string playerLabel in PlayerLabels)
            {
                activePlayer = playerLabel;
                bool clicked = await ClickPlayerControl(page, playerLabel, startsWith: false);
                if (clicked)
                {
                    await Task.Delay(350);
                    await TryStartEmbeddedPlayers(page);
                    await Task.Delay(perPlayerDelay);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "CineWave.su multi-player resolve failed for {TmdbId}", tmdbId);
        }

        List<CineWaveCandidate> ordered;
        lock (sync)
        {
            ordered = candidates
                .OrderBy(i => StreamPriority(i.Url))
                .ThenBy(i => Array.FindIndex(PlayerLabels, p => p.Equals(i.Label, StringComparison.OrdinalIgnoreCase)))
                .Select((item, index) => item with
                {
                    Label = $"{index + 1:00}. {(StreamPriority(item.Url) == 0 ? "HLS" : "Direct")} · {item.Label}"
                })
                .ToList();
        }

        if (ordered.Count > 0)
        {
            hybridCache.Set(memKey, ordered, TimeSpan.FromSeconds(Math.Clamp(init.cacheSeconds, 60, 3600)));
            Console.WriteLine($"CineWave.su: {ordered.Count} streams ({mediaType}:{tmdbId})");
        }
        else
        {
            Console.WriteLine($"CineWave.su: no stream ({mediaType}:{tmdbId})");
            proxyManager?.Refresh();
        }

        return ordered;
    }

    static async Task<bool> ClickPlayerControl(IPage page, string label, bool startsWith)
    {
        try
        {
            return await page.EvaluateAsync<bool>(
                @"([label, startsWith]) => {
                    const norm = value => String(value || '').replace(/\s+/g, ' ').trim().toLowerCase();
                    const wanted = norm(label);
                    const nodes = Array.from(document.querySelectorAll('button, [role=""button""], a, label'));
                    const node = nodes.find(el => {
                        const text = norm(el.textContent);
                        return startsWith ? text.startsWith(wanted) : text === wanted;
                    });
                    if (!node) return false;
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

    static async Task TryStartEmbeddedPlayers(IPage page)
    {
        foreach (IFrame frame in page.Frames.Skip(1))
        {
            try
            {
                await frame.EvaluateAsync(
                    @"() => {
                        const button = document.querySelector('[aria-label*=""play"" i], .play-button, .vjs-big-play-button, button:has(svg)');
                        if (button) button.click();
                        const video = document.querySelector('video');
                        if (video && video.paused) video.play().catch(() => {});
                    }"
                );
            }
            catch
            {
            }
        }
    }

    static int StreamPriority(string url)
    {
        if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (url.Contains(".mp4", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    // ═══════════════════════════ Legacy watch.cinewave fallback helpers ═══════════════════════════

    async Task<CineWaveStream> ResolveStream(long tmdbId, short season, short episode)
    {
        string payload = season > 0 && episode > 0
            ? TvPayload(tmdbId, season, episode)
            : $"movie:{tmdbId}";

        string playUrl = $"{SiteHost()}/play/{EncodePlayId(payload)}";

        try
        {
            string memKey = $"cinewave:resolve:{playUrl}";
            if (!hybridCache.TryGetValue(memKey, out (string file, List<HeadersModel> headers) cache))
            {
                using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                {
                    var page = await browser.NewPageAsync(init.plugin, httpHeaders(init)?.ToDictionary(), proxy_data);
                    if (page == null)
                        return null;

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (cache.file != null || await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true))
                                return;

                            string url = route.Request.Url;
                            if (VideoUrlRegex.IsMatch(url) && !AdUrlRegex.IsMatch(url))
                            {
                                var headers = new List<HeadersModel>();
                                foreach (var item in route.Request.Headers)
                                {
                                    if (BlockedHeader(item.Key))
                                        continue;

                                    headers.Add(new HeadersModel(item.Key, item.Value.ToString()));
                                }

                                cache = (url, headers);
                                PlaywrightBase.ConsoleLog(() => ($"Playwright: SET {url}", (IReadOnlyList<HeadersModel>)headers));
                                browser.SetPageResult(url);
                                await route.AbortAsync();
                                return;
                            }

                            if (browser.IsCompleted || AdUrlRegex.IsMatch(url))
                            {
                                await route.AbortAsync();
                                return;
                            }

                            await route.ContinueAsync();
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "CineWave", "id_z3cw8qmn");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, playUrl);
                    cache.file = await browser.WaitPageResult(Math.Clamp(init.resolveSeconds, 10, 60));
                }

                if (cache.file == null)
                {
                    proxyManager?.Refresh();
                    return null;
                }

                proxyManager?.Success();
                hybridCache.Set(memKey, cache, TimeSpan.FromSeconds(Math.Clamp(init.cacheSeconds, 60, 3600)));
            }

            if (string.IsNullOrWhiteSpace(cache.file))
                return null;

            return new CineWaveStream(cache.file, cache.headers);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "{Class} {CatchId}", "CineWave", "id_p6rn2xqf");
            return null;
        }
    }

    // ═══════════════════════════ Play id codec ═══════════════════════════

    static string EncodePlayId(string payload)
    {
        byte[] data = Encoding.UTF8.GetBytes(payload);
        for (int i = 0; i < data.Length; i++)
            data[i] ^= XorKey[i % XorKey.Length];

        return Convert.ToBase64String(data).TrimEnd('=');
    }

    static string TvPayload(long tmdbId, int season, int episode)
    {
        var sb = new StringBuilder($"{tmdbId}-{season}-{episode}");
        for (int i = 2; i < sb.Length; i += 8)
            sb[i] = (char)(sb[i] ^ 0x17);

        return $"tv-{sb}";
    }

    // ═══════════════════════════ TMDB / Cinemeta ═══════════════════════════

    async Task<JObject> GetTmdb(string path)
    {
        string apiKey = CoreInit.conf?.cub?.api_key;
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        string uri = $"https://api.themoviedb.org/3/{path}?api_key={HttpUtility.UrlEncode(apiKey)}&language=en-US";
        string cacheKey = $"cinewave:tmdb:{path}";

        return await InvokeCache(
            cacheKey,
            TimeSpan.FromHours(6),
            () => Http.Get<JObject>(
                uri,
                timeoutSeconds: Math.Clamp(init.timeoutSeconds, 5, 120),
                proxy: proxy
            )
        );
    }

    async Task<List<(int Number, int EpisodeCount)>> GetSeasonRows(long tmdbId, string imdbId)
    {
        var result = new List<(int Number, int EpisodeCount)>();

        JObject tmdb = await GetTmdb($"tv/{tmdbId}");
        if (tmdb?["seasons"] is JArray seasons)
        {
            foreach (JToken token in seasons)
            {
                int number = token.Value<int?>("season_number") ?? 0;
                int episodeCount = token.Value<int?>("episode_count") ?? 0;
                if (number > 0 && episodeCount > 0)
                    result.Add((number, episodeCount));
            }
        }

        if (result.Count > 0)
            return result;

        JObject cinemeta = await GetCinemeta(imdbId);
        if (cinemeta?["meta"]?["videos"] is not JArray videos)
            return result;

        var counts = new Dictionary<int, int>();
        foreach (JToken token in videos)
        {
            int number = token.Value<int?>("season") ?? 0;
            int episode = token.Value<int?>("episode") ?? 0;
            if (number <= 0 || episode <= 0)
                continue;

            counts[number] = counts.TryGetValue(number, out int count)
                ? Math.Max(count, episode)
                : episode;
        }

        return counts
            .OrderBy(i => i.Key)
            .Select(i => (i.Key, i.Value))
            .ToList();
    }

    async Task<List<(int Number, string Name)>> GetEpisodeRows(long tmdbId, string imdbId, short season)
    {
        var result = new List<(int Number, string Name)>();

        JObject tmdb = await GetTmdb($"tv/{tmdbId}/season/{season}");
        if (tmdb?["episodes"] is JArray episodes)
        {
            foreach (JToken token in episodes)
            {
                int number = token.Value<int?>("episode_number") ?? 0;
                if (number > 0)
                    result.Add((number, token.Value<string>("name")));
            }
        }

        if (result.Count > 0)
            return result;

        JObject cinemeta = await GetCinemeta(imdbId);
        if (cinemeta?["meta"]?["videos"] is not JArray videos)
            return result;

        foreach (JToken token in videos)
        {
            int tokenSeason = token.Value<int?>("season") ?? 0;
            int number = token.Value<int?>("episode") ?? 0;
            if (tokenSeason == season && number > 0)
                result.Add((number, token.Value<string>("name") ?? token.Value<string>("title")));
        }

        return result
            .OrderBy(i => i.Number)
            .ToList();
    }

    async Task<JObject> GetCinemeta(string imdbId)
    {
        if (string.IsNullOrWhiteSpace(imdbId) ||
            !imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string uri = $"https://v3-cinemeta.strem.io/meta/series/{Uri.EscapeDataString(imdbId)}.json";
        return await InvokeCache(
            $"cinewave:cinemeta:{imdbId}",
            TimeSpan.FromHours(6),
            () => Http.Get<JObject>(
                uri,
                timeoutSeconds: Math.Clamp(init.timeoutSeconds, 5, 120),
                proxy: proxy
            )
        );
    }

    // ═══════════════════════════ Helpers ═══════════════════════════

    string BuildIndexUrl(long tmdbId, string imdbId, string title, string original_title, int serial, int s = -1, int e = -1, bool play = false)
    {
        string query =
            $"tmdb_id={tmdbId}" +
            $"&imdb_id={HttpUtility.UrlEncode(imdbId)}" +
            $"&title={HttpUtility.UrlEncode(title)}" +
            $"&original_title={HttpUtility.UrlEncode(original_title)}" +
            $"&serial={serial}";

        if (s > 0)
            query += $"&s={s}";
        if (e > 0)
            query += $"&e={e}";
        if (play)
            query += "&play=true";

        return accsArgs($"{host}/lite/cinewave?{query}");
    }

    string SiteHost()
    {
        string configured = init.siteHost;
        if (string.IsNullOrWhiteSpace(configured) ||
            configured.Contains("watch.cinewave.qzz.io", StringComparison.OrdinalIgnoreCase))
            configured = "https://www.cinewave.su";

        return configured.TrimEnd('/');
    }

    static long ResolveTmdbId(long tmdbId, string id, string source)
    {
        if (tmdbId > 0)
            return tmdbId;

        if ((source is "tmdb" or "cub") &&
            long.TryParse(id, out long numericId) && numericId > 0)
        {
            return numericId;
        }

        return 0;
    }

    static bool BlockedHeader(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        string normalized = name.ToLowerInvariant();
        return normalized is "host" or "connection" or "content-length" or "accept-encoding" or "range";
    }

    static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
            uri.Scheme is "http" or "https";
    }
}
