using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace VkMovie;

public class VkMovieController : BaseOnlineController
{
    private static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    private static readonly int client_id = 52461373;
    private static string access_token;
    private static DateTime token_expires;

    public VkMovieController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vkmovie")]
    public async Task<ActionResult> Index(string title, string original_title, short year, byte serial, short s = -1, bool rjson = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (!await EnsureAnonymToken(init, proxy))
            return ShowError("token");

        string localName = SearchNameTo.Convert(title);
        string originalName = SearchNameTo.Convert(original_title);
        if (localName == null && originalName == null)
            return OnError("searchTitle");

        // VK search is heavily localized: querying only the TMDB display title tends to put
        // Russian voice-overs first. Search the original title first, then the localized title,
        // and merge by owner/id. This also catches international uploads whose title contains no
        // Cyrillic at all.
        var queryList = new List<string> { original_title, title };
        if (serial == 1 && s > 0)
        {
            queryList.Add($"{original_title ?? title} season {s}");
            queryList.Add($"{title ?? original_title} сезон {s}");
            queryList.Add($"{original_title ?? title} S{s:00}E");
        }

        var queries = queryList
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string cacheTitle = string.Join("|", queries.Select(query => SearchNameTo.Convert(query)));

    rhubFallback:
        var cache = await InvokeCacheResult<List<CatalogVideo>>(ipkey($"vkmovie:view:v3:{cacheTitle}:{year}:{serial}:{s}"), 20, textJson: true, onget: async e =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);

            string url = $"{init.host}/method/catalog.getVideoSearchWeb2?v=5.264&client_id={client_id}";
            var merged = new Dictionary<string, CatalogVideo>();

            foreach (string query in queries)
            {
                string data = $"screen_ref=search_video_service&input_method=keyboard_search_button&q={HttpUtility.UrlEncode($"{query} {year}")}&access_token={access_token}";
                var root = await httpHydra.Post<Root>(url, data, textJson: true);
                if (root?.response?.catalog_videos == null)
                    continue;

                foreach (var item in root.response.catalog_videos)
                {
                    var video = item?.video;
                    if (video != null)
                        merged[$"{video.owner_id}_{video.id}"] = item;
                }
            }

            if (merged.Count == 0)
                return e.Fail("catalog_videos");

            return e.Success(merged.Values
                .OrderByDescending(i => MatchScore(i.video, originalName, localName, year))
                .ThenByDescending(i => HasAdaptive(i.video?.files))
                .ThenByDescending(i => i.video?.files?.mp4_2160 != null)
                .ThenByDescending(i => i.video?.files?.mp4_1440 != null)
                .ThenByDescending(i => i.video?.files?.mp4_1080 != null)
                .ToList());
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return ContentTpl(cache, () =>
        {
            if (serial == 1)
            {
                var parsed = cache.Value
                    .Select(item => (item, episode: ParseEpisode(item?.video?.title)))
                    .Where(i => i.item?.video?.files != null && i.episode.season > 0 && i.episode.episode > 0)
                    .ToList();

                if (s == -1)
                {
                    var seasons = parsed.Select(i => i.episode.season).Distinct().OrderBy(i => i).ToList();
                    if (seasons.Count == 0)
                        seasons.Add(1);

                    var stpl = new SeasonTpl(seasons.Count);
                    string args = $"title={HttpUtility.UrlEncode(title)}&original_title={HttpUtility.UrlEncode(original_title)}&year={year}&serial=1&rjson={rjson}";
                    foreach (short season in seasons)
                        stpl.Append($"Season {season}", $"{host}/lite/vkmovie?{args}&s={season}", season);
                    return stpl;
                }

                var etpl = new EpisodeTpl(parsed.Count);
                foreach (var entry in parsed.Where(i => i.episode.season == s).OrderBy(i => i.episode.episode))
                {
                    var video = entry.item.video;
                    if (video.duration < 300)
                        continue;

                    var streams = BuildStreams(video.files);
                    if (streams.IsEmpty)
                        continue;

                    var subtitles = BuildSubtitles(video.subtitles);
                    etpl.Append(
                        $"Episode {entry.episode.episode}", title, s, entry.episode.episode,
                        streams.Firts().link,
                        streamquality: streams,
                        subtitles: subtitles,
                        headers: HeadersModel.Init(init.headers),
                        vast: init.vast
                    );
                }

                return etpl;
            }

            var mtpl = new MovieTpl(title, original_title, cache.Value.Count);

            foreach (var item in cache.Value)
            {
                var video = item.video;
                if (video == null || video.files == null)
                    continue;

                string name = SearchNameTo.Convert(video.title);
                int score = MatchScore(video, originalName, localName, year);
                if (name == null || score < 3)
                    continue;

                // Feature films normally exceed 45 minutes. Do not reject words such as
                // "series/season": some VK uploaders use them for movie collections and the old
                // filter discarded otherwise perfect international releases.
                if (video.duration < 2700)
                    continue;

                if (name.Contains("трейлер") || name.Contains("trailer") ||
                    name.Contains("тизер") || name.Contains("teaser") ||
                    name.Contains("обзор"))
                    continue;

                if (!HasAdaptive(video.files) && string.IsNullOrEmpty(video.files.mp4_2160) &&
                    string.IsNullOrEmpty(video.files.mp4_1440) &&
                    string.IsNullOrEmpty(video.files.mp4_1080) &&
                    string.IsNullOrEmpty(video.files.mp4_720))
                    continue;

                var streams = new StreamQualityTpl();

                void append(string url, string quality)
                {
                    if (!string.IsNullOrEmpty(url))
                        streams.Append(HostStreamProxy(url), quality);
                }

                // Adaptive manifests preserve alternate audio renditions. MP4 links flatten the
                // upload to one audio track, so expose HLS first and keep MP4 as a reliable
                // quality fallback for old clients.
                append(video.files.hls_fmp4, "HLS fMP4 • multi-audio");
                append(video.files.hls, "HLS");
                append(video.files.dash_sep, "DASH • multi-audio");
                append(video.files.dash_streams, "DASH streams");
                append(video.files.mp4_2160, "2160p");
                append(video.files.mp4_1440, "1440p");
                append(video.files.mp4_1080, "1080p");
                append(video.files.mp4_720, "720p");
                append(video.files.mp4_480, "480p");
                append(video.files.mp4_360, "360p");
                append(video.files.mp4_240, "240p");
                append(video.files.mp4_144, "144p");

                if (streams.IsEmpty)
                    continue;

                SubtitleTpl subtitles = null;

                if (video.subtitles != null && video.subtitles.Length > 0)
                {
                    var subtitleTpl = new SubtitleTpl(video.subtitles.Length);

                    foreach (var subtitle in video.subtitles)
                    {
                        if (string.IsNullOrEmpty(subtitle?.url))
                            continue;

                        string label = subtitle.manifest_name;
                        if (string.IsNullOrEmpty(label))
                            label = !string.IsNullOrEmpty(subtitle.title) ? subtitle.title : subtitle.lang;

                        subtitleTpl.Append(label, HostStreamProxy(subtitle.url));
                    }

                    if (!subtitleTpl.IsEmpty)
                        subtitles = subtitleTpl;
                }

                mtpl.Append(
                    video.title,
                    streams.Firts().link,
                    streamquality: streams,
                    subtitles: subtitles,
                    headers: HeadersModel.Init(init.headers),
                    vast: init.vast
                );
            }

            return mtpl;
        });
    }

    StreamQualityTpl BuildStreams(VideoFiles files)
    {
        var streams = new StreamQualityTpl();
        void add(string url, string quality)
        {
            if (!string.IsNullOrEmpty(url))
                streams.Append(HostStreamProxy(url), quality);
        }

        // VK Mobile keeps alternate audio in this manifest on supported uploads.
        add(files?.hls_fmp4, "HLS fMP4 • multi-audio");
        add(files?.hls, "HLS");
        add(files?.dash_streams, "DASH streams");
        add(files?.dash_sep, "DASH");
        add(files?.mp4_2160, "2160p");
        add(files?.mp4_1440, "1440p");
        add(files?.mp4_1080, "1080p");
        add(files?.mp4_720, "720p");
        add(files?.mp4_480, "480p");
        add(files?.mp4_360, "360p");
        add(files?.mp4_240, "240p");
        add(files?.mp4_144, "144p");
        return streams;
    }

    SubtitleTpl BuildSubtitles(VideoSubtitle[] source)
    {
        if (source == null || source.Length == 0)
            return null;

        var result = new SubtitleTpl(source.Length);
        foreach (var subtitle in source)
        {
            if (string.IsNullOrEmpty(subtitle?.url))
                continue;
            string label = subtitle.manifest_name ?? subtitle.title ?? subtitle.lang;
            result.Append(label, HostStreamProxy(subtitle.url));
        }
        return result.IsEmpty ? null : result;
    }

    static (short season, short episode) ParseEpisode(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return default;

        foreach (string pattern in new[]
        {
            @"(?i)\bS(?<s>\d{1,2})[ ._-]*E(?<e>\d{1,3})\b",
            @"(?i)\b(?<s>\d{1,2})x(?<e>\d{1,3})\b",
            @"(?i)\bseason\s*(?<s>\d{1,2}).{0,20}?episode\s*(?<e>\d{1,3})\b",
            @"(?i)\b(?<s>\d{1,2})\s*сезон.{0,20}?(?<e>\d{1,3})\s*сер(?:ия|ии|ию)\b",
            @"(?i)\bсезон\s*(?<s>\d{1,2}).{0,20}?сер(?:ия|ии|ию)?\s*(?<e>\d{1,3})\b"
        })
        {
            var match = Regex.Match(title, pattern);
            if (match.Success && short.TryParse(match.Groups["s"].Value, out short season) &&
                short.TryParse(match.Groups["e"].Value, out short episode))
                return (season, episode);
        }

        return default;
    }

    static bool HasAdaptive(VideoFiles files)
        => files != null && (!string.IsNullOrEmpty(files.hls) ||
            !string.IsNullOrEmpty(files.hls_fmp4) ||
            !string.IsNullOrEmpty(files.dash_sep) ||
            !string.IsNullOrEmpty(files.dash_streams));

    static int MatchScore(Video video, string originalName, string localName, short year)
    {
        if (video == null)
            return 0;

        string name = SearchNameTo.Convert(video.title);
        if (string.IsNullOrEmpty(name))
            return 0;

        int score = 0;
        if (!string.IsNullOrEmpty(originalName) && name.Contains(originalName))
            score += 8;
        if (!string.IsNullOrEmpty(localName) && name.Contains(localName))
            score += 5;
        if (name.Contains(year.ToString()))
            score += 3;
        else if (name.Contains((year - 1).ToString()) || name.Contains((year + 1).ToString()))
            score += 1;

        string metadata = $"{video.title} {video.description}".ToLowerInvariant();
        if (metadata.Contains("original") || metadata.Contains("english") ||
            metadata.Contains("multi audio") || metadata.Contains("multi-audio") ||
            metadata.Contains("multiple audio") || metadata.Contains("оригинал"))
            score += 2;
        if (HasAdaptive(video.files))
            score += 1;

        return score;
    }

    async Task<bool> EnsureAnonymToken(BaseSettings init, WebProxy proxy)
    {
        if (!string.IsNullOrEmpty(access_token) && token_expires > DateTime.UtcNow)
            return true;

        var semaphore = new SemaphorManager("vkmovie:anonym_token", TimeSpan.FromSeconds(30));

        try
        {
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return false;

            if (!string.IsNullOrEmpty(access_token) && token_expires > DateTime.UtcNow)
                return true;

            string url = "https://login.vk.com/?act=get_anonym_token";
            string postData = $"client_secret=o557NLIkAErNhakXrQ7A&client_id={client_id}&scopes=audio_anonymous%2Cvideo_anonymous%2Cphotos_anonymous%2Cprofile_anonymous&isApiOauthAnonymEnabled=false&version=1&app_id=6287487";

            JObject root = null;

            try
            {
                root = await httpHydra.Post<JObject>(url, postData);
            }
            catch { }

            if (root == null || !root.ContainsKey("data"))
                return false;

            var data = root["data"];

            string token = data?["access_token"]?.ToString();
            if (string.IsNullOrEmpty(token))
                return false;

            access_token = token;

            long? expires = data?["expires"]?.ToObject<long?>()
                ?? data?["expired_at"]?.ToObject<long?>()
                ?? -1;

            token_expires = expires == -1
                ? DateTime.UtcNow.AddHours(10)
                : DateTimeOffset.FromUnixTimeSeconds(expires.Value).UtcDateTime.AddHours(-4);

            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
