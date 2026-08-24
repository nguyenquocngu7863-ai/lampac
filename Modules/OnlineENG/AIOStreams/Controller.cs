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

namespace AIOStreams;

public sealed class AIOStreamsController : BaseOnlineController<ModuleConf>
{
    public AIOStreamsController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/aiostreams")]
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
        string stream_source,
        int serial = 0,
        short s = -1,
        short e = -1,
        bool play = false,
        bool rjson = false
    )
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (!init.streams)
            return OnError("AIOStreams stream resource is disabled", 404);

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

        if (isSeries)
            return await EpisodeResponse(addonId, title, original_title, s, e, play);

        string type = "movie";
        List<AIOStreamItem> allStreams = await GetStreams(type, addonId);
        if (allStreams.Count == 0)
            return OnError("No direct HTTP streams returned by AIOStreams", 502);

        List<AIOStreamItem> streams = string.IsNullOrWhiteSpace(stream_source)
            ? allStreams
            : allStreams
                .Where(i => SourceGroupName(i).Equals(stream_source, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (streams.Count == 0)
            return OnError("No streams for the selected AIOStreams source", 404);

        if (play)
            return RedirectToPlay(BuildVideoEndpoint(streams[0]));

        VoiceTpl sourceFilter = BuildSourceFilter(
            allStreams,
            addonId,
            title,
            original_title,
            stream_source
        );

        return ContentTpl(BuildMovieTemplate(streams, title, original_title, sourceFilter));
    }

    /// <summary>
    /// Exposes AIOStreams' standard subtitle resource to the generic Lampa
    /// subtitle plug-in. The private manifest remains server-side; only the
    /// normal subtitle records are returned to the client.
    /// </summary>
    [HttpGet, Staticache(manually: true)]
    [Route("lite/aiostreams/subtitles")]
    public async Task<ActionResult> Subtitles(
        string stremio_id,
        string id,
        string imdb_id,
        long tmdb_id,
        string source,
        int serial = 0,
        short s = -1,
        short e = -1
    )
    {
        if (await IsRequestBlocked(rch: false, rch_check: false))
            return badInitMsg;

        if (!init.subtitles)
            return ContentTo("{\"subtitles\":[]}", "application/json; charset=utf-8");

        if (!IsManifestUrl(init.manifest))
            return OnError("AIOStreams manifest URL is missing or invalid", 503);

        string addonId = ResolveStremioId(
            stremio_id,
            id,
            imdb_id,
            tmdb_id,
            source
        );

        if (string.IsNullOrWhiteSpace(addonId))
            return ContentTo("{\"subtitles\":[]}", "application/json; charset=utf-8");

        string type = serial == 1 ? "series" : "movie";
        string contentId = type == "series" && s > 0 && e > 0
            ? $"{addonId}:{s}:{e}"
            : addonId;

        JObject root = await GetAddonJson("subtitles", type, contentId);
        var result = new JObject
        {
            ["subtitles"] = new JArray()
        };

        if (root?["subtitles"] is JArray subtitles)
        {
            var safe = (JArray)result["subtitles"];
            foreach (JToken token in subtitles)
            {
                if (token is not JObject subtitle)
                    continue;

                string url = subtitle.Value<string>("url");
                if (!IsHttpUrl(url))
                    continue;

                safe.Add((JObject)subtitle.DeepClone());
            }
        }

        return ContentTo(result.ToString(Formatting.None), "application/json; charset=utf-8");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/aiostreams/video")]
    // Keep the original media extension visible to Lampa. MKV is used by the
    // client plug-in as the opt-in GStreamer hook; m3u8/mp4 lets Lampa select
    // its normal HLS/native player instead of treating a redirect as unknown.
    [Route("lite/aiostreams/file.mkv")]
    [Route("lite/aiostreams/file.m3u8")]
    [Route("lite/aiostreams/file.mp4")]
    public async Task<ActionResult> Video(string u, string h, bool play = true)
    {
        if (await IsRequestBlocked(rch: false, rch_check: false))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(u))
            return OnError("Missing AIOStreams stream URL", 400);

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
            return OnError("Invalid AIOStreams stream URL", 400);

        List<HeadersModel> headers = DecodeHeaders(h);
        string output = HostStreamProxy(streamUrl, headers);

        if (string.IsNullOrWhiteSpace(output) ||
            (!IsHttpUrl(output) && !output.Contains("/proxy/", StringComparison.OrdinalIgnoreCase)))
        {
            return OnError("Unable to prepare AIOStreams stream", 502);
        }

        // The normal /video endpoint keeps HLS/MP4 direct. The /file.mkv
        // alias intentionally retains the MKV suffix before this redirect, so
        // gst.js can send a selected MKV through GStreamer when that plug-in
        // is enabled. With gst disabled, the same endpoint simply redirects
        // to the source/proxy for VLC/direct playback.
        return RedirectToPlay(output);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/aiostreams/episode")]
    public async Task<ActionResult> Episode(
        string stremio_id,
        string title,
        string original_title,
        short s,
        short e,
        bool play = false
    )
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (!init.streams)
            return OnError("AIOStreams stream resource is disabled", 404);

        if (string.IsNullOrWhiteSpace(stremio_id))
            return OnError("Missing Stremio series id", 400);

        return await EpisodeResponse(stremio_id, title, original_title, s, e, play);
    }

    async Task<ActionResult> EpisodeResponse(
        string addonId,
        string title,
        string original_title,
        short season,
        short episode,
        bool play
    )
    {
        if (season <= 0 || episode <= 0)
            return OnError("Stremio episode requires season and episode", 400);

        List<AIOStreamItem> streams = await GetStreams(
            "series",
            $"{addonId}:{season}:{episode}"
        );

        if (streams.Count == 0)
            return OnError("No direct HTTP streams returned by AIOStreams", 502);

        var video = BuildVideoResponse(
            streams,
            title,
            original_title,
            season,
            episode
        );

        if (play)
            return RedirectToPlay(video.firstLink);

        return ContentTo(video.json);
    }

    async Task<ActionResult> Seasons(
        string addonId,
        string title,
        string original_title,
        long tmdb_id
    )
    {
        long tvId = ResolveTmdbId(addonId, tmdb_id);
        if (tvId <= 0 && !IsImdbId(addonId))
            return OnError("Series season list requires a TMDB or IMDb id", 400);

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
        if ((tvId <= 0 && !IsImdbId(addonId)) || season <= 0)
            return OnError("Series episode list requires a TMDB or IMDb id and season", 400);

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

            string link = BuildEpisodeUrl(
                addonId,
                title,
                original_title,
                season,
                (short)number
            );

            string streamLink = BuildEpisodeUrl(
                addonId,
                title,
                original_title,
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
        string cacheKey = $"aiostreams:tmdb:{path}";

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

        JObject tmdb = tvId > 0
            ? await GetTmdb($"tv/{tvId}")
            : null;
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

        JObject tmdb = tvId > 0
            ? await GetTmdb($"tv/{tvId}/season/{season}")
            : null;
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
            $"aiostreams:cinemeta:{addonId}",
            TimeSpan.FromHours(6),
            () => Http.Get<JObject>(uri, timeoutSeconds: 8, proxy: proxy)
        );
    }

    static bool IsManifestUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
            uri.Scheme is "http" or "https";
    }

    async Task<JObject> GetAddonJson(string resource, string type, string addonId)
    {
        if (!IsManifestUrl(init.manifest) ||
            !Uri.TryCreate(init.manifest, UriKind.Absolute, out Uri manifestUri))
        {
            Serilog.Log.Warning("AIOStreams manifest URL is invalid");
            return null;
        }

        string relativePath = $"{resource}/{type}/{Uri.EscapeDataString(addonId)}.json";
        string endpoint = new Uri(manifestUri, relativePath).AbsoluteUri;
        string cacheKey = $"aiostreams:{resource}:{init.manifest}:{type}:{addonId}";
        int timeoutSeconds = Math.Clamp(init.timeoutSeconds, 5, 120);
        int cacheSeconds = Math.Clamp(init.cacheSeconds, 15, 900);

        return await InvokeCache(
            cacheKey,
            TimeSpan.FromSeconds(cacheSeconds),
            () => Http.Get<JObject>(
                endpoint,
                timeoutSeconds: timeoutSeconds,
                proxy: proxy
            )
        );
    }

    async Task<List<AIOStreamItem>> GetStreams(string type, string addonId)
    {
        var result = new List<AIOStreamItem>();
        JObject root = await GetAddonJson("stream", type, addonId);

        if (root?["streams"] is not JArray streams)
            return result;

        int maxStreams = Math.Clamp(init.maxStreams, 1, 200);

        foreach (JToken token in streams)
        {
            if (result.Count >= maxStreams || token is not JObject stream)
                break;

            AIOStreamItem item = ReadStream(stream);
            if (item != null)
                result.Add(item);
        }

        return result;
    }

    static AIOStreamItem ReadStream(JObject stream)
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
        string rawTitle = stream.Value<string>("title") ?? string.Empty;
        string rawDescription = stream.Value<string>("description") ?? string.Empty;
        JObject behaviorHints = stream["behaviorHints"] as JObject;
        string fileName = behaviorHints?.Value<string>("filename") ??
            behaviorHints?.Value<string>("fileName") ?? string.Empty;
        string title = Compact(
            string.IsNullOrWhiteSpace(rawTitle) ? rawDescription : rawTitle
        );
        string metadata = $"{name} {rawTitle} {rawDescription} {fileName}";
        string quality = FindQuality(metadata);
        string format = DetectFormat(rawUrl, metadata);

        // File hosts often return an opaque download path while Stremio marks
        // the item as notWebReady. Prefer the file player for that case so
        // Lampa does not try to load an extensionless redirect as a generic
        // HTML5 video. Explicit m3u8/mp4 markers above still win.
        bool notWebReady = behaviorHints?.Value<bool>("notWebReady") == true;
        if (format == null && notWebReady)
            format = "mkv";

        return new AIOStreamItem(
            rawUrl,
            name,
            title,
            quality,
            format,
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
        List<AIOStreamItem> streams,
        string title,
        string original_title,
        VoiceTpl sourceFilter
    )
    {
        var tpl = new MovieTpl(title, original_title, sourceFilter, streams.Count);
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Keep every returned file as a card. The source buttons above the
        // list provide grouping/filtering, while same-resolution releases are
        // not collapsed into a single quality entry.
        foreach (AIOStreamItem stream in streams)
        {
            string source = SourceGroupName(stream);
            string label = source;
            if (!string.IsNullOrWhiteSpace(stream.Quality))
                label += $" • {stream.Quality}";

            labels.TryGetValue(label, out int count);
            count++;
            labels[label] = count;
            if (count > 1)
                label += $" #{count}";

            tpl.Append(
                label,
                BuildVideoEndpoint(stream),
                "play",
                quality: stream.Quality,
                details: Compact(stream.Title)
            );
        }

        return tpl;
    }

    VoiceTpl BuildSourceFilter(
        List<AIOStreamItem> streams,
        string addonId,
        string title,
        string original_title,
        string selectedSource
    )
    {
        var groups = new List<string>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AIOStreamItem stream in streams)
        {
            string source = SourceGroupName(stream);
            if (known.Add(source))
                groups.Add(source);
        }

        if (groups.Count == 0)
            return null;

        var filter = new VoiceTpl(groups.Count);
        for (int index = 0; index < groups.Count; index++)
        {
            string source = groups[index];
            bool active = string.IsNullOrWhiteSpace(selectedSource)
                ? index == 0
                : source.Equals(selectedSource, StringComparison.OrdinalIgnoreCase);

            filter.Append(
                source,
                active,
                BuildIndexUrl(
                    addonId,
                    title,
                    original_title,
                    serial: 0,
                    streamSource: source
                )
            );
        }

        return filter;
    }

    (string json, string firstLink) BuildVideoResponse(
        List<AIOStreamItem> streams,
        string title,
        string original_title,
        short season,
        short episode
    )
    {
        var quality = new StreamQualityTpl();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AIOStreamItem stream in streams)
        {
            string key = stream.Quality ?? "auto";
            string source = SourceGroupName(stream);
            if (!string.IsNullOrWhiteSpace(source))
                key += $" • {source}";

            // The quality picker ("Chọn link AIOStreams") only shows the dict
            // key, so carry the full release title there. Without it every
            // entry from one source looks identical ("2160p • Sootio #2").
            string details = Compact(stream.Title) ?? Compact(stream.Name);
            if (!string.IsNullOrWhiteSpace(details))
                key += $" • {details}";

            if (!keys.Add(key))
            {
                key = $"{key} #{keys.Count + 1}";
                while (!keys.Add(key))
                    key += "#";
            }

            quality.Append(BuildVideoEndpoint(stream, selectLink: true), key);
        }

        StreamQualityDto first = quality.Firts();
        string name = title ?? original_title ?? "AIOStreams";
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

    string BuildVideoEndpoint(AIOStreamItem stream, bool selectLink = false)
    {
        string route = stream.Format switch
        {
            "mkv" => "file.mkv",
            "m3u8" => "file.m3u8",
            "mp4" => "file.mp4",
            _ => "video"
        };

        string endpoint = $"{host}/lite/aiostreams/{route}?u={HttpUtility.UrlEncode(EncryptQuery(stream.Url))}";

        if (stream.Headers != null && stream.Headers.Count > 0)
        {
            string json = JsonConvert.SerializeObject(stream.Headers);
            endpoint += $"&h={HttpUtility.UrlEncode(EncryptQuery(json))}";
        }

        if (selectLink)
            endpoint += "&aiostreams_select=1";

        return accsArgs(endpoint + "&play=true");
    }

    string BuildEpisodeUrl(
        string addonId,
        string title,
        string original_title,
        short season,
        short episode,
        bool play = false
    )
    {
        string query =
            $"stremio_id={HttpUtility.UrlEncode(addonId)}" +
            $"&title={HttpUtility.UrlEncode(title)}" +
            $"&original_title={HttpUtility.UrlEncode(original_title)}" +
            $"&s={season}&e={episode}";

        if (play)
            query += "&play=true";

        return accsArgs($"{host}/lite/aiostreams/episode?{query}");
    }

    string BuildIndexUrl(
        string addonId,
        string title,
        string original_title,
        int serial,
        int season = -1,
        int episode = -1,
        string streamSource = null,
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
        if (!string.IsNullOrWhiteSpace(streamSource))
            query += $"&stream_source={HttpUtility.UrlEncode(streamSource)}";
        if (play)
            query += "&play=true";

        return accsArgs($"{host}/lite/aiostreams?{query}");
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
                value.StartsWith("tmdb:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("kitsu:", StringComparison.OrdinalIgnoreCase))
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

    static bool IsImdbId(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.StartsWith("tt", StringComparison.OrdinalIgnoreCase);
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

    static string DetectFormat(string url, string metadata)
    {
        string value;
        try
        {
            value = Uri.UnescapeDataString($"{url} {metadata}");
        }
        catch
        {
            value = $"{url} {metadata}";
        }

        if (Regex.IsMatch(value, @"\.mkv(?:$|[?#&\s])|\bmatroska\b", RegexOptions.IgnoreCase))
            return "mkv";

        if (Regex.IsMatch(value, @"\.m3u8(?:$|[?#&\s])|\bm3u8\b|\bHLS(?:\s+Stream)?\b", RegexOptions.IgnoreCase))
            return "m3u8";

        if (Regex.IsMatch(value, @"\.mp4(?:$|[?#&\s])|\bmp4\b", RegexOptions.IgnoreCase))
            return "mp4";

        return null;
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

    static string SourceGroupName(AIOStreamItem stream)
    {
        // AIOStreams puts the extractor/source in the final title line:
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
            return "AIOStreams";

        Match quality = Regex.Match(
            name,
            @"(?<!\d)(2160|1440|1080|720|576|480|360|240|144)p?(?!\d)",
            RegexOptions.IgnoreCase
        );

        if (!quality.Success)
            quality = Regex.Match(name, @"\b(?:4k|uhd)\b", RegexOptions.IgnoreCase);

        if (quality.Success)
            name = name[..quality.Index];

        name = Regex.Replace(name, @"[\s(\[{•|·\-]+$", "").Trim();
        return string.IsNullOrWhiteSpace(name) ? "AIOStreams" : name;
    }

    static string Compact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string result = Regex.Replace(value, @"\s+", " ").Trim();
        return result.Length > 180 ? result[..180] : result;
    }
}
