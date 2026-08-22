using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace WebStreamr;

public sealed class WebStreamrController : BaseOnlineController<ModuleConf>
{
    public WebStreamrController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/webstreamr")]
    public async Task<ActionResult> Index(
        string stremio_id,
        string id,
        string imdb_id,
        long tmdb_id,
        long kinopoisk_id,
        string title,
        string original_title,
        string original_language,
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

        string addonId = ResolveStremioId(
            stremio_id,
            id,
            imdb_id,
            tmdb_id,
            source
        );

        if (string.IsNullOrWhiteSpace(addonId))
            return OnError("Stremio requires an IMDb or TMDB id", 400);

        bool isSeries = serial == 1;

        if (isSeries && s <= 0)
        {
            return await Seasons(
                addonId,
                title,
                original_title,
                tmdb_id
            );
        }

        if (isSeries && e <= 0)
        {
            return await Episodes(
                addonId,
                title,
                original_title,
                tmdb_id,
                s
            );
        }

        string type = isSeries ? "series" : "movie";
        string requestId = isSeries
            ? $"{addonId}:{s}:{e}"
            : addonId;

        List<WebStreamItem> streams = await GetStreams(type, requestId);
        if (streams.Count == 0)
            return OnError("No direct HTTP streams returned by WebStreamr", 502);

        if (isSeries)
        {
            var video = BuildVideoResponse(
                streams,
                title,
                original_title,
                s,
                e
            );

            if (play)
                return RedirectToPlay(video.firstLink);

            return ContentTo(video.json);
        }

        if (play)
            return RedirectToPlay(BuildVideoEndpoint(streams[0]));

        return ContentTpl(BuildMovieTemplate(streams, title, original_title));
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/webstreamr/video")]
    // The .mkv alias is intentional: the client GStreamer plug-in uses the
    // visible extension to decide whether an MKV should be transcoded.
    [Route("lite/webstreamr/file.mkv")]
    public async Task<ActionResult> Video(string u, string h, bool play = true)
    {
        if (await IsRequestBlocked(rch: false, rch_check: false))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(u))
            return OnError("Missing WebStreamr stream URL", 400);

        string streamUrl;
        try
        {
            streamUrl = DecryptQuery(u);
        }
        catch
        {
            streamUrl = null;
        }

        if (!IsHttpUrl(streamUrl))
            return OnError("Invalid WebStreamr stream URL", 400);

        List<HeadersModel> headers = DecodeHeaders(h);
        string output = HostStreamProxy(streamUrl, headers);

        if (string.IsNullOrWhiteSpace(output) ||
            (!IsHttpUrl(output) && !output.Contains("/proxy/", StringComparison.OrdinalIgnoreCase)))
        {
            return OnError("Unable to prepare WebStreamr stream", 502);
        }

        // The normal /video endpoint keeps HLS/MP4 direct. The /file.mkv
        // alias intentionally retains the MKV suffix before this redirect, so
        // gst.js can send a selected MKV through GStreamer when that plug-in
        // is enabled. With gst disabled, the same endpoint simply redirects
        // to the source/proxy for VLC/direct playback.
        return RedirectToPlay(output);
    }

    async Task<ActionResult> Seasons(
        string addonId,
        string title,
        string original_title,
        long tmdb_id
    )
    {
        long tvId = ResolveTmdbId(addonId, tmdb_id);
        if (tvId <= 0)
            return OnError("Series season list requires a TMDB id", 400);

        List<(int Number, int EpisodeCount)> seasons = await GetSeasonRows(addonId, tvId);
        if (seasons.Count == 0)
            return OnError("Unable to load series seasons", 502);

        var tpl = new SeasonTpl(seasons.Count);

        foreach (var seasonInfo in seasons)
        {
            tpl.Append(
                $"Season {seasonInfo.Number}",
                BuildIndexUrl(addonId, title, original_title, serial: 1, season: seasonInfo.Number),
                seasonInfo.Number
            );
        }

        return ContentTpl(tpl);
    }

    async Task<ActionResult> Episodes(
        string addonId,
        string title,
        string original_title,
        long tmdb_id,
        short season
    )
    {
        long tvId = ResolveTmdbId(addonId, tmdb_id);
        if (tvId <= 0 || season <= 0)
            return OnError("Series episode list requires a TMDB id and season", 400);

        List<(int Number, string Name)> episodes = await GetEpisodeRows(addonId, tvId, season);
        if (episodes.Count == 0)
            return OnError("Unable to load series episodes", 502);

        var tpl = new EpisodeTpl(episodes.Count);

        foreach (var episodeInfo in episodes)
        {
            int number = episodeInfo.Number;
            if (number <= 0 || number > short.MaxValue)
                continue;

            string episodeName = episodeInfo.Name;
            string name = string.IsNullOrWhiteSpace(episodeName)
                ? $"Episode {number}"
                : $"{number}. {episodeName}";

            string link = BuildIndexUrl(
                addonId,
                title,
                original_title,
                serial: 1,
                season,
                (short)number
            );

            string streamLink = BuildIndexUrl(
                addonId,
                title,
                original_title,
                serial: 1,
                season,
                (short)number,
                play: true
            );

            tpl.Append(
                name,
                title ?? original_title,
                season,
                (short)number,
                link,
                "call",
                streamlink: streamLink
            );
        }

        return ContentTpl(tpl);
    }

    async Task<JObject> GetTmdb(string path)
    {
        string apiKey = CoreInit.conf?.cub?.api_key;
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        string uri = $"https://api.themoviedb.org/3/{path}?api_key={HttpUtility.UrlEncode(apiKey)}&language=en-US";
        string cacheKey = $"webstreamr:tmdb:{path}";

        return await InvokeCache(
            cacheKey,
            TimeSpan.FromHours(6),
            () => Http.Get<JObject>(
                uri,
                timeoutSeconds: 8,
                proxy: proxy
            )
        );
    }

    async Task<List<(int Number, int EpisodeCount)>> GetSeasonRows(string addonId, long tvId)
    {
        var result = new List<(int Number, int EpisodeCount)>();

        JObject tmdb = await GetTmdb($"tv/{tvId}");
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

        // Some installations do not have a TMDB API key. Cinemeta is only a
        // metadata fallback; stream URLs still come exclusively from the
        // configured Stremio add-on.
        JObject cinemeta = await GetCinemeta(addonId);
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

    async Task<List<(int Number, string Name)>> GetEpisodeRows(
        string addonId,
        long tvId,
        short season
    )
    {
        var result = new List<(int Number, string Name)>();

        JObject tmdb = await GetTmdb($"tv/{tvId}/season/{season}");
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

        JObject cinemeta = await GetCinemeta(addonId);
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

    async Task<JObject> GetCinemeta(string addonId)
    {
        if (string.IsNullOrWhiteSpace(addonId) ||
            !addonId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string uri = $"https://v3-cinemeta.strem.io/meta/series/{Uri.EscapeDataString(addonId)}.json";
        return await InvokeCache(
            $"webstreamr:cinemeta:{addonId}",
            TimeSpan.FromHours(6),
            () => Http.Get<JObject>(uri, timeoutSeconds: 8, proxy: proxy)
        );
    }

    async Task<List<WebStreamItem>> GetStreams(string type, string addonId)
    {
        var result = new List<WebStreamItem>();

        if (!Uri.TryCreate(init.manifest, UriKind.Absolute, out Uri manifestUri) ||
            manifestUri.Scheme is not ("http" or "https"))
        {
            Serilog.Log.Warning("WebStreamr manifest URL is invalid: {Manifest}", init.manifest);
            return result;
        }

        string relativePath = $"stream/{type}/{Uri.EscapeDataString(addonId)}.json";
        string endpoint = new Uri(manifestUri, relativePath).AbsoluteUri;
        string cacheKey = $"webstreamr:streams:{init.manifest}:{type}:{addonId}";
        int timeoutSeconds = Math.Clamp(init.timeoutSeconds, 5, 120);

        JObject root = await InvokeCache(
            cacheKey,
            TimeSpan.FromMinutes(5),
            () => Http.Get<JObject>(
                endpoint,
                timeoutSeconds: timeoutSeconds,
                proxy: proxy
            )
        );

        if (root?["streams"] is not JArray streams)
            return result;

        int maxStreams = Math.Clamp(init.maxStreams, 1, 200);

        foreach (JToken token in streams)
        {
            if (result.Count >= maxStreams || token is not JObject stream)
                break;

            WebStreamItem item = ReadStream(stream);
            if (item != null)
                result.Add(item);
        }

        return result;
    }

    static WebStreamItem ReadStream(JObject stream)
    {
        string rawUrl = stream.Value<string>("url");
        if (string.IsNullOrWhiteSpace(rawUrl))
            return null;

        var headers = ReadHeaders(stream);
        int separator = rawUrl.IndexOf('|');
        if (separator > 0)
        {
            AddPipeHeaders(rawUrl[(separator + 1)..], headers);
            rawUrl = rawUrl[..separator];
        }

        rawUrl = rawUrl.Trim();
        if (!IsHttpUrl(rawUrl))
            return null;

        string name = Compact(stream.Value<string>("name"));
        string title = Compact(stream.Value<string>("title"));
        string quality = FindQuality($"{name} {title} {stream.Value<string>("description")}");

        return new WebStreamItem(
            rawUrl,
            name,
            title,
            quality,
            headers.Count > 0 ? headers : null
        );
    }

    static List<HeadersModel> ReadHeaders(JObject stream)
    {
        var result = new List<HeadersModel>();
        JObject behaviorHints = stream["behaviorHints"] as JObject;
        JObject proxyHeaders = behaviorHints?["proxyHeaders"] as JObject;
        JObject requestHeaders = proxyHeaders?["request"] as JObject;

        if (requestHeaders == null)
            requestHeaders = stream["headers"] as JObject;

        if (requestHeaders == null)
            return result;

        foreach (JProperty property in requestHeaders.Properties())
        {
            if (!AllowedHeader(property.Name))
                continue;

            string value = property.Value.Type == JTokenType.String
                ? property.Value.Value<string>()
                : null;

            if (!string.IsNullOrWhiteSpace(value))
                result.Add(new HeadersModel(property.Name, value));
        }

        return result;
    }

    static void AddPipeHeaders(string value, List<HeadersModel> headers)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var query = HttpUtility.ParseQueryString(value);
        foreach (string key in query)
        {
            if (!AllowedHeader(key))
                continue;

            string headerValue = query[key];
            if (!string.IsNullOrWhiteSpace(headerValue) &&
                headers.All(i => !i.name.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                headers.Add(new HeadersModel(key, headerValue));
            }
        }
    }

    static bool AllowedHeader(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string normalized = name.ToLowerInvariant();
        return normalized is not ("host" or "connection" or "content-length" or "accept-encoding" or "range");
    }

    List<HeadersModel> DecodeHeaders(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return new List<HeadersModel>();

        try
        {
            string json = DecryptQuery(encrypted);
            return JsonConvert.DeserializeObject<List<HeadersModel>>(json)
                ?? new List<HeadersModel>();
        }
        catch
        {
            return new List<HeadersModel>();
        }
    }

    MovieTpl BuildMovieTemplate(
        List<WebStreamItem> streams,
        string title,
        string original_title
    )
    {
        var tpl = new MovieTpl(title, original_title, streams.Count);
        var groups = new List<(string Name, List<WebStreamItem> Streams)>();
        var groupIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // One Stremio response may contain many quality links from the same
        // extractor. Keep one Lampac card per source and put its resolutions
        // into the quality selector instead of rendering 20 duplicate cards.
        foreach (WebStreamItem stream in streams)
        {
            string name = SourceGroupName(stream);

            if (!groupIndexes.TryGetValue(name, out int groupIndex))
            {
                groupIndex = groups.Count;
                groupIndexes[name] = groupIndex;
                groups.Add((name, new List<WebStreamItem>()));
            }

            groups[groupIndex].Streams.Add(stream);
        }

        foreach (var group in groups)
        {
            var quality = new StreamQualityTpl(group.Streams.Count);
            var qualityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (WebStreamItem stream in group.Streams)
            {
                string key = stream.Quality ?? "auto";
                if (!qualityKeys.Add(key))
                    key = $"{key} ({qualityKeys.Count + 1})";

                quality.Append(BuildVideoEndpoint(stream), key);
            }

            StreamQualityDto first = quality.Firts();
            if (first == null)
                continue;

            tpl.Append(
                group.Name,
                first.link,
                "play",
                streamquality: quality
            );
        }

        return tpl;
    }

    (string json, string firstLink) BuildVideoResponse(
        List<WebStreamItem> streams,
        string title,
        string original_title,
        short season,
        short episode
    )
    {
        var quality = new StreamQualityTpl();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (WebStreamItem stream in streams)
        {
            string key = stream.Quality ?? "auto";
            if (!keys.Add(key))
            {
                string source = Compact(stream.Name);
                key = string.IsNullOrWhiteSpace(source)
                    ? $"{key}-{keys.Count + 1}"
                    : $"{key} {source}";

                if (!keys.Add(key))
                    key = $"{key}-{keys.Count + 1}";
            }

            quality.Append(BuildVideoEndpoint(stream), key);
        }

        StreamQualityDto first = quality.Firts();
        string name = title ?? original_title ?? "WebStreamr";
        if (season > 0 && episode > 0)
            name += $" S{season:00}E{episode:00}";

        string json = VideoTpl.ToJson(
            "play",
            first.link,
            name,
            streamquality: quality,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        );

        return (json, first.link);
    }

    string BuildVideoEndpoint(WebStreamItem stream)
    {
        string route = IsMkvStream(stream) && IsGStreamerEnabled()
            ? "file.mkv"
            : "video";

        string endpoint = $"{host}/lite/webstreamr/{route}?u={HttpUtility.UrlEncode(EncryptQuery(stream.Url))}";

        if (stream.Headers != null && stream.Headers.Count > 0)
        {
            string json = JsonConvert.SerializeObject(stream.Headers);
            endpoint += $"&h={HttpUtility.UrlEncode(EncryptQuery(json))}";
        }

        return accsArgs(endpoint + "&play=true");
    }

    string BuildIndexUrl(
        string addonId,
        string title,
        string original_title,
        int serial,
        int season = -1,
        int episode = -1,
        bool play = false
    )
    {
        string query =
            $"stremio_id={HttpUtility.UrlEncode(addonId)}" +
            $"&title={HttpUtility.UrlEncode(title)}" +
            $"&original_title={HttpUtility.UrlEncode(original_title)}" +
            $"&serial={serial}";

        if (season > 0)
            query += $"&s={season}";
        if (episode > 0)
            query += $"&e={episode}";
        if (play)
            query += "&play=true";

        return accsArgs($"{host}/lite/webstreamr?{query}");
    }

    static string ResolveStremioId(
        string stremioId,
        string id,
        string imdbId,
        long tmdbId,
        string source
    )
    {
        foreach (string candidate in new[] { stremioId, imdbId, id })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string value = candidate.Trim();
            if (value.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        if (tmdbId > 0)
            return $"tmdb:{tmdbId}";

        if (source is "tmdb" or "cub" &&
            long.TryParse(id, out long numericId) &&
            numericId > 0)
        {
            return $"tmdb:{numericId}";
        }

        return null;
    }

    static long ResolveTmdbId(string addonId, long tmdbId)
    {
        if (tmdbId > 0)
            return tmdbId;

        if (!string.IsNullOrWhiteSpace(addonId) &&
            addonId.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(addonId[5..], out long parsed))
        {
            return parsed;
        }

        return 0;
    }

    static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
            uri.Scheme is "http" or "https";
    }

    static bool IsMkvUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
            uri.AbsolutePath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsMkvStream(WebStreamItem stream)
    {
        return IsMkvUrl(stream.Url) ||
            Regex.IsMatch(
                $"{stream.Name} {stream.Title}",
                @"\b(?:mkv|matroska)\b",
                RegexOptions.IgnoreCase
            );
    }

    static bool IsGStreamerEnabled()
    {
        try
        {
            JToken gst = CoreInit.CurrentConf?["gst"] ?? CoreInit.CurrentConf?["GStreamer"];
            JToken enable = (gst as JObject)?["enable"];

            if (enable == null)
                return false;

            if (enable.Type == JTokenType.Boolean)
                return enable.Value<bool>();

            return bool.TryParse(enable.ToString(), out bool result) && result;
        }
        catch
        {
            return false;
        }
    }

    static string FindQuality(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        Match match = Regex.Match(
            value,
            @"(?<!\d)(2160|1440|1080|720|576|480|360|240|144)p?(?!\d)",
            RegexOptions.IgnoreCase
        );

        if (match.Success)
            return $"{match.Groups[1].Value}p";

        return Regex.IsMatch(value, @"\b(?:4k|uhd)\b", RegexOptions.IgnoreCase)
            ? "2160p"
            : null;
    }

    static string SourceGroupName(WebStreamItem stream)
    {
        // WebStreamrMBG puts the extractor/source in the final title line:
        // `🔗 Extractor from Source`. Prefer the source name so FSL, PixelDrain,
        // and similar links for one provider are grouped together.
        string title = Compact(stream.Title);
        Match sourceMatch = Regex.Match(
            title ?? string.Empty,
            @"(?:^|\s)🔗\s*(?<extractor>.+?)(?:\s+from\s+(?<source>[^\r\n]+))?$",
            RegexOptions.IgnoreCase
        );

        if (sourceMatch.Success)
        {
            string source = Compact(
                sourceMatch.Groups["source"].Success
                    ? sourceMatch.Groups["source"].Value
                    : sourceMatch.Groups["extractor"].Value
            );

            if (!string.IsNullOrWhiteSpace(source))
                return source;
        }

        string name = Compact(stream.Name);
        if (string.IsNullOrWhiteSpace(name))
            return "WebStreamr";

        Match quality = Regex.Match(
            name,
            @"(?<!\d)(2160|1440|1080|720|576|480|360|240|144)p?(?!\d)",
            RegexOptions.IgnoreCase
        );

        if (!quality.Success)
            quality = Regex.Match(name, @"\b(?:4k|uhd)\b", RegexOptions.IgnoreCase);

        if (quality.Success)
            name = name[..quality.Index];

        name = Regex.Replace(name, @"\s*[•|·\-]+\s*$", "").Trim();
        return string.IsNullOrWhiteSpace(name) ? "WebStreamr" : name;
    }

    static string Compact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string result = Regex.Replace(value, @"\s+", " ").Trim();
        return result.Length > 180 ? result[..180] : result;
    }
}
