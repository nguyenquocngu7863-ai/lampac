using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using Shared.Services.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace VsMov;

/// <summary>
/// VsMov adapter for vsmov.com JSON API. Vietnamese movie source with
/// free API, CDN streaming, and rich metadata.
/// </summary>
public class VsMovController : BaseOnlineController
{
    public VsMovController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vsmov")]
    public async Task<ActionResult> Index(
        string title,
        string original_title,
        short year,
        string slug = null,
        int t = -1,
        short s = -1,
        bool rjson = false,
        bool similar = false)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(slug))
        {
            string query = string.IsNullOrWhiteSpace(original_title) ? title : original_title;
            if (string.IsNullOrWhiteSpace(query))
                return OnError();

            var search = await Search(query);

            if ((!search.IsSuccess || search.Value.Count == 0) &&
                !string.IsNullOrWhiteSpace(title) &&
                !string.Equals(query, title, StringComparison.OrdinalIgnoreCase))
            {
                search = await Search(title);
            }

            if (!search.IsSuccess)
                return ContentTpl(search, () => new SimilarTpl());

            if (!similar)
            {
                VsMovie exact = FindExact(search.Value, title, original_title, year);
                if (exact != null)
                    return LocalRedirect(DetailLink(exact.slug, title, original_title, year, rjson));

                if (search.Value.Count == 1)
                    return LocalRedirect(DetailLink(search.Value[0].slug, title, original_title, year, rjson));
            }

            return ContentTpl(search, () => SearchTemplate(search.Value, title, original_title, year, rjson));
        }

        var detail = await Detail(slug);
        if (!detail.IsSuccess)
            return ContentTpl(detail, () => new MovieTpl(title, original_title));

        return ContentTpl(detail, () => RenderDetail(detail.Value, slug, title, original_title, t, s, rjson));
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vsmov-search")]
    public async Task<ActionResult> SpiderSearch(string title, bool rjson = false)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OnError();

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var search = await Search(title);
        return ContentTpl(search, () => SearchTemplate(search.Value, title, null, 0, rjson));
    }

    #region Video

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vsmov/video")]
    [Route("lite/vsmov/video.m3u8")]
    public async Task<ActionResult> Video(string uri, string title, bool play = false)
    {
        string source = DecryptQuery(uri);
        if (string.IsNullOrWhiteSpace(source) || !IsHttpUrl(source))
            return OnError("uri");

        if (await IsRequestBlocked(rch: true, rch_check: !play))
            return badInitMsg;

        var headers = httpHeaders(init.host, init.headers_stream);
        string stream = HostStreamProxy(source, headers: headers);
        if (string.IsNullOrWhiteSpace(stream))
            return OnError("stream", refresh_proxy: true);

        if (play)
            return RedirectToPlay(stream);

        return ContentTo(VideoTpl.ToJson(
            "play",
            stream,
            title ?? "auto",
            vast: init.vast,
            headers: headers,
            httpContext: HttpContext
        ));
    }

    #endregion

    #region API and templates

    async Task<CacheResult<List<VsMovie>>> Search(string query)
    {
        string cacheKey = $"vsmov:search:{query}";

        return await InvokeCacheResult<List<VsMovie>>(cacheKey, TimeSpan.FromHours(2), textJson: true, onget: async e =>
        {
            string url = $"{ApiBase}/api/tim-kiem?keyword={HttpUtility.UrlEncode(query)}&limit=32";
            // VSMOV accepts a plain direct JSON request. Do not route this public API
            // through a user CORS/RCH/proxy configuration: those relays commonly return
            // a 503 even though vsmov.com itself is healthy.
            var root = await Http.Get<VsSearchResponse>(
                url,
                timeoutSeconds: 30,
                headers: ApiHeaders,
                useDefaultHeaders: false,
                textJson: true
            );
            var items = root?.items;

            if (items == null || items.Count == 0)
                return e.Fail("results", refresh_proxy: true);

            return e.Success(items.Where(i => !string.IsNullOrWhiteSpace(i.slug)).ToList());
        });
    }

    async Task<CacheResult<VsDetailResponse>> Detail(string slug)
    {
        string cacheKey = $"vsmov:detail:{slug}";

        return await InvokeCacheResult<VsDetailResponse>(cacheKey, TimeSpan.FromHours(2), textJson: true, onget: async e =>
        {
            string url = $"{ApiBase}/api/phim/{HttpUtility.UrlEncode(slug)}";
            var root = await Http.Get<VsDetailResponse>(
                url,
                timeoutSeconds: 30,
                headers: ApiHeaders,
                useDefaultHeaders: false,
                textJson: true
            );

            if (root?.movie == null || root.episodes == null || root.episodes.Count == 0)
                return e.Fail("detail", refresh_proxy: true);

            return e.Success(root);
        });
    }

    static readonly IReadOnlyList<HeadersModel> ApiHeaders = HeadersModel.Init(
        ("accept", "application/json"),
        ("referer", "https://vsmov.com/")
    );

    string ApiBase
        => (init?.apihost ?? init?.host ?? "https://vsmov.com").TrimEnd('/');

    SimilarTpl SearchTemplate(
        IEnumerable<VsMovie> movies,
        string title,
        string originalTitle,
        short year,
        bool rjson)
    {
        var list = movies?.Where(i => i != null && !string.IsNullOrWhiteSpace(i.slug)).ToList()
            ?? new List<VsMovie>();
        var tpl = new SimilarTpl(list.Count);
        string encodedTitle = HttpUtility.UrlEncode(title);
        string encodedOriginal = HttpUtility.UrlEncode(originalTitle);

        foreach (var movie in list)
        {
            string displayName = movie.name;
            if (!string.IsNullOrWhiteSpace(movie.origin_name) &&
                !string.Equals(movie.name, movie.origin_name, StringComparison.OrdinalIgnoreCase))
                displayName += " / " + movie.origin_name;

            string details = movie.lang;
            if (!string.IsNullOrWhiteSpace(movie.quality))
                details = string.IsNullOrWhiteSpace(details) ? movie.quality : details + " · " + movie.quality;

            string link = $"{host}/lite/vsmov?slug={HttpUtility.UrlEncode(movie.slug)}" +
                          $"&title={encodedTitle}&original_title={encodedOriginal}&year={year}&rjson={rjson}";

            tpl.Append(
                displayName,
                movie.year > 0 ? movie.year.ToString() : string.Empty,
                details,
                link,
                PosterApi.Size(movie.poster_url ?? movie.thumb_url)
            );
        }

        return tpl;
    }

    ITplResult RenderDetail(
        VsDetailResponse detail,
        string slug,
        string title,
        string originalTitle,
        int translation,
        short season,
        bool rjson)
    {
        if (detail?.movie == null || detail.episodes == null)
            return default;

        string displayTitle = title ?? detail.movie.name ?? originalTitle;
        bool isSeries = string.Equals(detail.movie.type, "series", StringComparison.OrdinalIgnoreCase)
            || detail.episodes.Any(i => (i?.server_data?.Count ?? 0) > 1 || i?.server_data?.Any(e => EpisodeNumber(e) > 1) == true);

        if (!isSeries)
            return RenderMovie(detail, displayTitle, originalTitle, slug);

        return RenderSeries(detail, displayTitle, originalTitle, slug, translation, season, rjson);
    }

    MovieTpl RenderMovie(VsDetailResponse detail, string title, string originalTitle, string slug)
    {
        var servers = detail.episodes
            .Where(i => i?.server_data?.Any(e => !string.IsNullOrWhiteSpace(StreamUrl(e))) == true)
            .ToList();
        var tpl = new MovieTpl(title, originalTitle, servers.Count);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var server in servers)
        {
            var episode = server.server_data.FirstOrDefault(e => !string.IsNullOrWhiteSpace(StreamUrl(e)));
            string source = StreamUrl(episode);
            if (string.IsNullOrWhiteSpace(source))
                continue;

            string name = string.IsNullOrWhiteSpace(server.server_name)
                ? "VsMov"
                : server.server_name.Trim();
            if (!usedNames.Add(name))
                name += " #" + (usedNames.Count + 1);

            tpl.Append(
                name,
                VideoLink(source, title, slug),
                "call",
                vast: init.vast
            );
        }

        return tpl;
    }

    ITplResult RenderSeries(
        VsDetailResponse detail,
        string title,
        string originalTitle,
        string slug,
        int translation,
        short season,
        bool rjson)
    {
        var seasons = GetSeasons(detail);
        if (season == -1 && seasons.Count > 1)
        {
            var tpl = new SeasonTpl();
            string encodedTitle = HttpUtility.UrlEncode(title);
            string encodedOriginal = HttpUtility.UrlEncode(originalTitle);

            foreach (int item in seasons)
            {
                tpl.Append(
                    $"{item} сезон",
                    $"{host}/lite/vsmov?slug={HttpUtility.UrlEncode(slug)}&title={encodedTitle}" +
                    $"&original_title={encodedOriginal}&s={item}&rjson={rjson}",
                    item
                );
            }

            return tpl;
        }

        if (season == -1)
            season = (short)seasons.FirstOrDefault(1);

        var candidates = detail.episodes
            .Select((server, index) => new
            {
                Server = server,
                Index = index,
                Episodes = EpisodesForSeason(server, season)
            })
            .Where(i => i.Episodes.Count > 0)
            .ToList();

        if (candidates.Count == 0)
            return default;

        var selected = candidates.FirstOrDefault(i => i.Index == translation) ?? candidates[0];
        var vtpl = new VoiceTpl(candidates.Count);
        string encodedTitle2 = HttpUtility.UrlEncode(title);
        string encodedOriginal2 = HttpUtility.UrlEncode(originalTitle);

        foreach (var candidate in candidates)
        {
            string voiceName = string.IsNullOrWhiteSpace(candidate.Server.server_name)
                ? "Server " + (candidate.Index + 1)
                : candidate.Server.server_name;

            vtpl.Append(
                voiceName,
                candidate.Index == selected.Index,
                $"{host}/lite/vsmov?slug={HttpUtility.UrlEncode(slug)}&title={encodedTitle2}" +
                $"&original_title={encodedOriginal2}&s={season}&t={candidate.Index}&rjson={rjson}"
            );
        }

        var etpl = new EpisodeTpl(vtpl, selected.Episodes.Count);
        int fallbackEpisode = 1;

        foreach (var episode in selected.Episodes)
        {
            string source = StreamUrl(episode);
            if (string.IsNullOrWhiteSpace(source))
                continue;

            int number = EpisodeNumber(episode);
            if (number < 1)
                number = fallbackEpisode;
            fallbackEpisode = Math.Max(fallbackEpisode + 1, number + 1);

            string link = VideoLink(source, title, slug);
            etpl.Append(
                string.IsNullOrWhiteSpace(episode.name) ? $"Tập {number}" : episode.name,
                title ?? originalTitle,
                season,
                (short)Math.Min(number, short.MaxValue),
                link,
                "call",
                streamlink: link + "&play=true",
                vast: init.vast
            );
        }

        return etpl;
    }

    string VideoLink(string source, string title, string slug)
    {
        return accsArgs(
            $"{host}/lite/vsmov/video?uri={EncryptQuery(source)}" +
            $"&title={HttpUtility.UrlEncode(title)}&slug={HttpUtility.UrlEncode(slug)}"
        );
    }

    List<int> GetSeasons(VsDetailResponse detail)
    {
        var seasons = new HashSet<int>();
        foreach (var server in detail?.episodes ?? new List<VsEpisodeServer>())
        {
            foreach (var episode in server?.server_data ?? new List<VsEpisode>())
            {
                int value = EpisodeSeason(episode);
                if (value > 0) seasons.Add(value);
            }
        }

        if (seasons.Count == 0) seasons.Add(1);
        return seasons.OrderBy(i => i).ToList();
    }

    List<VsEpisode> EpisodesForSeason(VsEpisodeServer server, short season)
    {
        var episodes = server?.server_data ?? new List<VsEpisode>();
        if (episodes.Count == 0) return new List<VsEpisode>();

        bool hasExplicitSeason = episodes.Any(i => i != null && i.season_number > 0);
        if (hasExplicitSeason)
            episodes = episodes.Where(i => EpisodeSeason(i) == season).ToList();
        else if (season != 1)
            episodes = new List<VsEpisode>();

        return episodes
            .Where(i => !string.IsNullOrWhiteSpace(StreamUrl(i)))
            .OrderBy(EpisodeNumber)
            .ToList();
    }

    int EpisodeSeason(VsEpisode episode)
    {
        if (episode == null) return 1;
        if (episode.season_number > 0) return episode.season_number;

        Match match = Regex.Match(episode.name ?? string.Empty, @"(?:season|s|mùa)\s*0*(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : 1;
    }

    int EpisodeNumber(VsEpisode episode)
    {
        if (episode == null) return 0;
        if (episode.episode_number > 0) return episode.episode_number;

        string name = episode.name ?? string.Empty;
        Match match = Regex.Match(name, @"(?:episode|ep|tập|tap|series|ser|e)\s*[-._ ]*0*(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success) match = Regex.Match(name, @"(?:^|\s)0*(\d+)\s*$");
        return match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : 0;
    }

    string StreamUrl(VsEpisode episode)
    {
        if (episode == null) return null;

        string link = episode.link_m3u8;
        if (string.IsNullOrWhiteSpace(link))
            link = EmbedM3u8(episode.link_embed);

        if (string.IsNullOrWhiteSpace(link)) return null;
        if (link.StartsWith("//", StringComparison.Ordinal)) link = "https:" + link;
        return IsHttpUrl(link) ? link : null;
    }

    static string EmbedM3u8(string embed)
    {
        if (string.IsNullOrWhiteSpace(embed)) return null;

        try
        {
            // VsMov pattern: https://v1.streamvsmov.com/video/{hash} → stream/{hash}/master.m3u8
            if (embed.Contains("streamvsmov.com/video/", StringComparison.OrdinalIgnoreCase))
            {
                string hash = embed.Split('/').LastOrDefault();
                if (!string.IsNullOrWhiteSpace(hash))
                    return $"https://v1.streamvsmov.com/stream/{hash}/master.m3u8";
            }

            var uri = new Uri(embed, UriKind.Absolute);
            string value = HttpUtility.ParseQueryString(uri.Query).Get("url");
            return !string.IsNullOrWhiteSpace(value) && value.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
                ? HttpUtility.UrlDecode(value)
                : null;
        }
        catch
        {
            return null;
        }
    }

    static bool IsHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
           && !string.IsNullOrWhiteSpace(uri.Host);

    static VsMovie FindExact(IEnumerable<VsMovie> items, string title, string originalTitle, short year)
    {
        string normalizedTitle = SearchNameTo.Convert(title, string.Empty);
        string normalizedOriginal = SearchNameTo.Convert(originalTitle, string.Empty);

        foreach (var item in items ?? Enumerable.Empty<VsMovie>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.slug)) continue;
            bool yearMatches = year == 0 || item.year == 0 || Math.Abs(item.year - year) <= 1;
            if (!yearMatches) continue;

            if ((!string.IsNullOrEmpty(normalizedTitle) && SearchNameTo.Equals(item.name, normalizedTitle)) ||
                (!string.IsNullOrEmpty(normalizedTitle) && SearchNameTo.Equals(item.origin_name, normalizedTitle)) ||
                (!string.IsNullOrEmpty(normalizedOriginal) && SearchNameTo.Equals(item.name, normalizedOriginal)) ||
                (!string.IsNullOrEmpty(normalizedOriginal) && SearchNameTo.Equals(item.origin_name, normalizedOriginal)))
                return item;
        }

        return null;
    }

    string DetailLink(string slug, string title, string originalTitle, short year, bool rjson)
    {
        return accsArgs(
            $"/lite/vsmov?slug={HttpUtility.UrlEncode(slug)}&title={HttpUtility.UrlEncode(title)}" +
            $"&original_title={HttpUtility.UrlEncode(originalTitle)}&year={year}&rjson={rjson}"
        );
    }

    #endregion
}
