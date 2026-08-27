using Microsoft.AspNetCore.Mvc;
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
    const string HdHubHost = "https://hdhub.thevolecitor.qzz.io";
    const string HdHubConfig = "eyJ0b3Jib3giOiJ1bnNldCIsInF1YWxpdGllcyI6IjIxNjBwLDEwODBwLDcyMHAiLCJzb3J0IjoiZGVzYyJ9";

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
            "CineWave · tất cả server",
            accsArgs($"{host}/lite/cinewave/video?tmdb_id={tmdbId}&imdb_id={HttpUtility.UrlEncode(imdb_id)}"),
            "call",
            quality: "2160p",
            details: "4K/1080p/720p • direct + embed fallback"
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

    // ═══════════════════════════ Direct catalog + embed fallback ═══════════════════════════

    async Task<List<CineWaveCandidate>> ResolveCandidates(long tmdbId, string imdbId, short season, short episode)
    {
        imdbId = await ResolveImdbId(tmdbId, imdbId, season > 0);
        string streamId = season > 0 && episode > 0
            ? $"{imdbId}:{season}:{episode}"
            : imdbId;
        string mediaType = season > 0 ? "series" : "movie";
        string memKey = $"cinewave:all:{mediaType}:{streamId}";

        if (hybridCache.TryGetValue(memKey, out List<CineWaveCandidate> cached))
            return cached;

        var candidates = new List<CineWaveCandidate>(40);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(imdbId) && imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string apiUrl = $"{HdHubHost}/{HdHubConfig}/stream/{mediaType}/{HttpUtility.UrlEncode(streamId)}.json";
                var apiHeaders = HeadersModel.Init(
                    ("Referer", SiteHost() + "/"),
                    ("Accept", "application/json"),
                    ("User-Agent", Http.UserAgent)
                );
                JObject root = await httpHydra.Get<JObject>(apiUrl, addheaders: apiHeaders);

                if (root?["streams"] is JArray streams)
                {
                    int index = 0;
                    foreach (JToken item in streams)
                    {
                        string providerName = item.Value<string>("name") ?? string.Empty;
                        string description = item.Value<string>("description") ?? item.Value<string>("title") ?? string.Empty;
                        string combined = providerName + " " + description;

                        if (Regex.IsMatch(combined, "donat|discord", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                            continue;

                        string url = item.Value<string>("url") ?? item.Value<string>("externalUrl");
                        if (!IsHttpUrl(url) || !seen.Add(url))
                            continue;

                        index++;
                        string quality = QualityLabel(combined);
                        string provider = Regex.Replace(providerName, @"\s+", " ").Trim();
                        if (string.IsNullOrWhiteSpace(provider))
                            provider = "CineWave";
                        if (provider.Length > 24)
                            provider = provider[..24];

                        long size = item["behaviorHints"]?.Value<long?>("videoSize") ?? 0;
                        string sizeLabel = SizeLabel(size);
                        string label = $"{index:00}. {quality} · {provider}";
                        if (sizeLabel != null)
                            label += $" · {sizeLabel}";

                        var streamHeaders = HeadersModel.Init(
                            ("Referer", SiteHost() + "/"),
                            ("User-Agent", Http.UserAgent),
                            ("Accept", "*/*")
                        );
                        candidates.Add(new CineWaveCandidate(url, label, streamHeaders));
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "CineWave direct catalog failed for {StreamId}", streamId);
            }
        }

        // Use the browser-sniffed Server Hub only when the direct catalog has
        // no usable result; otherwise it adds a 20-second Chromium pass to
        // every request merely to duplicate one of the available servers.
        if (candidates.Count == 0)
        {
            CineWaveStream embed = await ResolveStream(tmdbId, season, episode);
            if (embed != null && seen.Add(embed.Url))
                candidates.Add(new CineWaveCandidate(embed.Url, "01. Embed fallback", embed.Headers));
        }

        if (candidates.Count > 0)
        {
            hybridCache.Set(memKey, candidates, TimeSpan.FromSeconds(Math.Clamp(init.cacheSeconds, 60, 3600)));
            Console.WriteLine($"CineWave: {candidates.Count} streams ({mediaType}:{streamId})");
        }

        return candidates;
    }

    async Task<string> ResolveImdbId(long tmdbId, string imdbId, bool series)
    {
        if (!string.IsNullOrWhiteSpace(imdbId) && imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            return imdbId;

        JObject external = await GetTmdb($"{(series ? "tv" : "movie")}/{tmdbId}/external_ids");
        return external?.Value<string>("imdb_id");
    }

    static string QualityLabel(string value)
    {
        Match match = Regex.Match(value ?? string.Empty, @"(?:2160p|4K|1080p|720p|480p)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return "Auto";

        return match.Value.Equals("2160p", StringComparison.OrdinalIgnoreCase) ? "4K" : match.Value.ToUpperInvariant();
    }

    static string SizeLabel(long bytes)
    {
        if (bytes <= 0)
            return null;

        double gb = bytes / 1024d / 1024d / 1024d;
        return gb >= 1 ? $"{gb:0.##} GB" : $"{bytes / 1024d / 1024d:0} MB";
    }

    // ═══════════════════════════ Resolver (headless fallback) ═══════════════════════════

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
        => (string.IsNullOrWhiteSpace(init.siteHost)
            ? "https://watch.cinewave.qzz.io"
            : init.siteHost).TrimEnd('/');

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
