using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace OpenDirectory;

/// <summary>
/// Read-only adapter for the configured Open Directory host. It deliberately
/// resolves exact title/year folders and never performs a fuzzy site-wide
/// search, so a missing match is safer than returning the wrong movie.
/// </summary>
public sealed class OpenDirectoryController : BaseOnlineController<ModuleConf>
{
    static readonly Regex AnchorRegex = new(
        "<a\\b[^>]*?href\\s*=\\s*(?:\"(?<href>[^\"]*)\"|'(?<href>[^']*)')[^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled
    );

    static readonly Regex SeasonRegex = new(
        "(?:season|series|mùa|mua|s)[ ._-]*0*(?<season>\\d{1,3})(?:$|[^0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    static readonly Regex SeasonEpisodeRegex = new(
        "(?:^|[^a-z])s0*(?<season>\\d{1,3})[ ._-]*e0*(?<episode>\\d{1,3})(?:$|[^0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    static readonly Regex NumberedEpisodeRegex = new(
        "(?:^|[^a-z])e0*(?<episode>\\d{1,3})(?:$|[^0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    static readonly Regex OneByOneEpisodeRegex = new(
        "(?:^|[^0-9])(?<season>\\d{1,3})x0*(?<episode>\\d{1,3})(?:$|[^0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    static readonly Regex QualityRegex = new(
        "(?<!\\d)(?<quality>2160|1440|1080|720|576|480|360|240|144)p?(?!\\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    public OpenDirectoryController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/opendirectory")]
    public async Task<ActionResult> Index(
        string title,
        string original_title,
        short year,
        int serial = 0,
        string dir = null,
        short s = -1,
        short e = -1,
        bool play = false,
        bool rjson = false
    )
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        bool isSeries = serial == 1;
        DirectoryPage page = await ResolveFolder(dir, title, original_title, year, isSeries);
        if (page == null)
            return OnError(
                "Open Directory không tìm thấy thư mục chính xác theo tên và năm; không dùng tìm mơ hồ để tránh nhầm phim.",
                404
            );

        if (!isSeries)
            return RenderMovie(page, title, original_title, play);

        if (s <= 0)
            return await RenderSeasons(page, title, original_title);

        DirectoryPage seasonPage = await ResolveSeasonPage(page, s);
        if (seasonPage == null)
            return OnError($"Open Directory không có Season {s}", 404);

        if (e <= 0)
            return RenderEpisodes(seasonPage, title, original_title, s);

        return await RenderEpisode(seasonPage, title, original_title, s, e, play);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/opendirectory/episode")]
    public async Task<ActionResult> Episode(
        string dir,
        string title,
        string original_title,
        short s,
        short e,
        bool play = false
    )
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        string seasonUrl = DecodeDirectory(dir);
        if (!IsAllowedDirectoryUrl(seasonUrl))
            return OnError("Open Directory season URL không hợp lệ", 400);

        DirectoryPage page = await LoadPage(seasonUrl);
        if (page == null)
            return OnError("Open Directory không tải được thư mục Season", 502);

        return await RenderEpisode(page, title, original_title, s, e, play);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/opendirectory/video")]
    [Route("lite/opendirectory/file.mkv")]
    [Route("lite/opendirectory/file.mp4")]
    [Route("lite/opendirectory/file.m3u8")]
    [Route("lite/opendirectory/file.webm")]
    [Route("lite/opendirectory/file.avi")]
    [Route("lite/opendirectory/file.m2ts")]
    public async Task<ActionResult> Video(string u, bool play = true)
    {
        if (await IsRequestBlocked(rch: false, rch_check: false))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(u))
            return OnError("Thiếu Open Directory URL", 400);

        string source;
        try
        {
            source = DecryptQuery(u);
        }
        catch
        {
            source = null;
        }

        if (!IsAllowedMediaUrl(source))
            return OnError("Open Directory media URL không hợp lệ", 400);

        string output = HostStreamProxy(source);
        if (string.IsNullOrWhiteSpace(output) ||
            (!IsHttpUrl(output) && !output.Contains("/proxy/", StringComparison.OrdinalIgnoreCase)))
        {
            return OnError("Không chuẩn bị được Open Directory stream", 502);
        }

        return RedirectToPlay(output);
    }

    ActionResult RenderMovie(
        DirectoryPage page,
        string title,
        string original_title,
        bool play
    )
    {
        List<OpenDirectoryMedia> media = page.Entries
            .Where(i => !i.IsDirectory)
            .Select(ToMedia)
            .Where(i => i != null)
            .Take(MaxFiles())
            .ToList();

        if (media.Count == 0)
            return OnError("Open Directory không có file media trong thư mục phim", 404);

        if (play)
            return RedirectToPlay(BuildVideoEndpoint(media[0]));

        var tpl = new MovieTpl(title, original_title, media.Count);
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (OpenDirectoryMedia item in media)
        {
            string label = DisplayName(item);
            labels.TryGetValue(label, out int count);
            count++;
            labels[label] = count;
            if (count > 1)
                label += $" #{count}";

            tpl.Append(
                label,
                BuildVideoEndpoint(item),
                "play",
                quality: item.Quality,
                details: item.Name
            );
        }

        return ContentTpl(tpl);
    }

    async Task<ActionResult> RenderSeasons(
        DirectoryPage showPage,
        string title,
        string original_title
    )
    {
        List<(int Number, string Url)> seasons = await GetSeasons(showPage);
        if (seasons.Count == 0)
            return OnError("Open Directory không tìm thấy thư mục Season", 404);

        var tpl = new SeasonTpl(seasons.Count);
        foreach (var season in seasons)
        {
            tpl.Append(
                $"Season {season.Number}",
                BuildIndexUrl(showPage.Url, title, original_title, season.Number),
                season.Number
            );
        }

        return ContentTpl(tpl);
    }

    ActionResult RenderEpisodes(
        DirectoryPage seasonPage,
        string title,
        string original_title,
        short season
    )
    {
        var episodes = seasonPage.Entries
            .Where(i => !i.IsDirectory)
            .Select(i => new { Media = ToMedia(i), Number = ParseEpisode(i.Name, season) })
            .Where(i => i.Media != null && i.Number > 0 && i.Number <= short.MaxValue)
            .GroupBy(i => i.Number)
            .OrderBy(i => i.Key)
            .ToList();

        if (episodes.Count == 0)
            return OnError($"Open Directory không tìm thấy tập nào trong Season {season}", 404);

        var tpl = new EpisodeTpl(episodes.Count);
        foreach (var group in episodes)
        {
            short episode = (short)group.Key;
            string name = $"Episode {episode:00}";
            string link = BuildEpisodeUrl(seasonPage.Url, title, original_title, season, episode);
            string streamLink = BuildEpisodeUrl(seasonPage.Url, title, original_title, season, episode, play: true);

            tpl.Append(
                name,
                title ?? original_title ?? "Open Directory",
                season,
                episode,
                link,
                "call",
                streamlink: streamLink
            );
        }

        return ContentTpl(tpl);
    }

    async Task<ActionResult> RenderEpisode(
        DirectoryPage seasonPage,
        string title,
        string original_title,
        short season,
        short episode,
        bool play
    )
    {
        List<OpenDirectoryMedia> media = seasonPage.Entries
            .Where(i => !i.IsDirectory && ParseEpisode(i.Name, season) == episode)
            .Select(ToMedia)
            .Where(i => i != null)
            .Take(MaxFiles())
            .ToList();

        if (media.Count == 0)
            return OnError($"Open Directory không tìm thấy S{season:00}E{episode:00}", 404);

        var quality = new StreamQualityTpl(media.Count);
        foreach (OpenDirectoryMedia item in media)
        {
            string label = DisplayName(item);
            quality.Append(BuildVideoEndpoint(item, selectLink: true), label);
        }

        StreamQualityDto first = quality.Firts();
        string name = title ?? original_title ?? "Open Directory";
        name += $" S{season:00}E{episode:00}";

        if (play)
            return RedirectToPlay(first.link);

        string json = VideoTpl.ToJson(
            "play",
            first.link,
            name,
            streamquality: quality,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        );

        return ContentTo(json);
    }

    async Task<DirectoryPage> ResolveFolder(
        string encryptedDirectory,
        string title,
        string original_title,
        short year,
        bool isSeries
    )
    {
        string supplied = DecodeDirectory(encryptedDirectory);
        if (IsAllowedDirectoryUrl(supplied))
            return await LoadPage(supplied);

        List<string> names = TitleCandidates(title, original_title);
        string[] roots = isSeries
            ? new[] { "tvs", "asiandrama", "kdrama" }
            : new[] { "movies" };

        foreach (string root in roots)
        {
            foreach (string name in names)
            {
                foreach (string folderName in FolderNameCandidates(name, year, isSeries))
                {
                    string url = BuildDirectoryUrl(root, folderName);
                    DirectoryPage page = await LoadPage(url);
                    if (page != null)
                        return page;
                }
            }
        }

        return null;
    }

    async Task<DirectoryPage> ResolveSeasonPage(DirectoryPage showPage, short season)
    {
        foreach ((int Number, string Url) item in await GetSeasons(showPage))
        {
            if (item.Number == season)
                return await LoadPage(item.Url) ?? new DirectoryPage(item.Url, new List<OpenDirectoryEntry>());
        }

        // A few directories put files directly under the show folder. Keep the
        // exact folder rather than guessing a different show.
        if (showPage.Entries.Any(i => !i.IsDirectory && ParseEpisode(i.Name, season) > 0))
            return showPage;

        return null;
    }

    async Task<List<(int Number, string Url)>> GetSeasons(DirectoryPage showPage)
    {
        var result = new List<(int Number, string Url)>();
        var seen = new HashSet<int>();

        foreach (OpenDirectoryEntry entry in showPage.Entries.Where(i => i.IsDirectory))
        {
            Match match = SeasonRegex.Match(entry.Name ?? string.Empty);
            if (match.Success &&
                int.TryParse(match.Groups["season"].Value, out int number) &&
                number > 0 && seen.Add(number))
            {
                result.Add((number, entry.Url));
            }
        }

        if (result.Count > 0)
            return result.OrderBy(i => i.Number).ToList();

        var inferred = new HashSet<int>();
        foreach (OpenDirectoryEntry entry in showPage.Entries.Where(i => !i.IsDirectory))
        {
            Match match = SeasonEpisodeRegex.Match(entry.Name ?? string.Empty);
            if (match.Success && int.TryParse(match.Groups["season"].Value, out int number) && number > 0)
                inferred.Add(number);
        }

        return inferred.OrderBy(i => i).Select(i => (i, showPage.Url)).ToList();
    }

    async Task<DirectoryPage> LoadPage(string url)
    {
        if (!IsAllowedDirectoryUrl(url))
            return null;

        string html = await InvokeCache(
            $"opendirectory:page:{url}",
            TimeSpan.FromMinutes(10),
            () => Http.Get(
                url,
                timeoutSeconds: TimeoutSeconds(),
                proxy: proxy
            )
        );

        if (!LooksLikeDirectoryPage(html))
            return null;

        return new DirectoryPage(url, ParseEntries(url, html));
    }

    static bool LooksLikeDirectoryPage(string html)
    {
        return !string.IsNullOrWhiteSpace(html) &&
            (Regex.IsMatch(html, "<title>\\s*Index of", RegexOptions.IgnoreCase) ||
             html.Contains("Parent Directory", StringComparison.OrdinalIgnoreCase));
    }

    List<OpenDirectoryEntry> ParseEntries(string pageUrl, string html)
    {
        var result = new List<OpenDirectoryEntry>();
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri baseUri))
            return result;

        int limit = MaxDirectoryEntries();
        foreach (Match match in AnchorRegex.Matches(html))
        {
            string href = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim();
            string text = StripTags(WebUtility.HtmlDecode(match.Groups["text"].Value));
            if (string.IsNullOrWhiteSpace(href) ||
                href.StartsWith("#", StringComparison.Ordinal) ||
                href.Equals("../", StringComparison.Ordinal) ||
                text.StartsWith("Parent Directory", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(baseUri, href, out Uri target) ||
                !IsAllowedDirectoryUrl(target.AbsoluteUri))
            {
                continue;
            }

            bool isDirectory = href.EndsWith("/", StringComparison.Ordinal) ||
                text.Contains("Directory", StringComparison.OrdinalIgnoreCase);

            if (!isDirectory && !IsMediaUrl(target.AbsoluteUri))
                continue;

            string name = string.IsNullOrWhiteSpace(text)
                ? Uri.UnescapeDataString(target.Segments.LastOrDefault() ?? string.Empty).TrimEnd('/')
                : text.Trim();

            result.Add(new OpenDirectoryEntry(name, target.AbsoluteUri, isDirectory));
            if (result.Count >= limit)
                break;
        }

        return result;
    }

    OpenDirectoryMedia ToMedia(OpenDirectoryEntry entry)
    {
        if (entry == null || entry.IsDirectory || !IsMediaUrl(entry.Url))
            return null;

        string name = string.IsNullOrWhiteSpace(entry.Name)
            ? Uri.UnescapeDataString(new Uri(entry.Url).Segments.Last())
            : entry.Name;
        string format = DetectFormat(entry.Url);
        return new OpenDirectoryMedia(name, entry.Url, format, FindQuality(name));
    }

    string BuildVideoEndpoint(OpenDirectoryMedia media, bool selectLink = false)
    {
        // Direct links: Lampa nhận link gốc của host, playback không chạm vào
        // Lampac. Marker chọn file đi trong fragment - fragment không bao giờ
        // được gửi lên origin, và gst.js gỡ nó sau khi người dùng chọn link.
        if (init.directLinks)
            return selectLink ? media.Url + "#opendirectory_select=1" : media.Url;

        string route = media.Format switch
        {
            "mkv" => "file.mkv",
            "mp4" => "file.mp4",
            "m3u8" => "file.m3u8",
            "webm" => "file.webm",
            "avi" => "file.avi",
            "m2ts" => "file.m2ts",
            _ => "video"
        };

        string endpoint = $"{host}/lite/opendirectory/{route}?u={HttpUtility.UrlEncode(EncryptQuery(media.Url))}";
        if (selectLink)
            endpoint += "&opendirectory_select=1";

        return accsArgs(endpoint + "&play=true");
    }

    string BuildIndexUrl(string folder, string title, string original_title, int season)
    {
        string query =
            $"title={HttpUtility.UrlEncode(title)}" +
            $"&original_title={HttpUtility.UrlEncode(original_title)}" +
            "&serial=1" +
            $"&s={season}" +
            $"&dir={HttpUtility.UrlEncode(EncryptQuery(folder))}";
        return accsArgs($"{host}/lite/opendirectory?{query}");
    }

    string BuildEpisodeUrl(
        string folder,
        string title,
        string original_title,
        short season,
        short episode,
        bool play = false
    )
    {
        string query =
            $"title={HttpUtility.UrlEncode(title)}" +
            $"&original_title={HttpUtility.UrlEncode(original_title)}" +
            $"&s={season}&e={episode}" +
            $"&dir={HttpUtility.UrlEncode(EncryptQuery(folder))}";
        if (play)
            query += "&play=true";

        return accsArgs($"{host}/lite/opendirectory/episode?{query}");
    }

    string DecodeDirectory(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return null;

        try
        {
            return DecryptQuery(encrypted);
        }
        catch
        {
            return null;
        }
    }

    bool IsAllowedDirectoryUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
            !IsHttpUrl(uri.AbsoluteUri) ||
            !Uri.TryCreate(DirectoryHost(), UriKind.Absolute, out Uri root) ||
            !uri.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        return path.StartsWith("/movies/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/tvs/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/asiandrama/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/kdrama/", StringComparison.OrdinalIgnoreCase);
    }

    bool IsAllowedMediaUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
            !IsHttpUrl(uri.AbsoluteUri) ||
            !Uri.TryCreate(DirectoryHost(), UriKind.Absolute, out Uri root) ||
            !uri.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        return path.StartsWith("/movies/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/tvs/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/asiandrama/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/kdrama/", StringComparison.OrdinalIgnoreCase);
    }

    string BuildDirectoryUrl(string root, string folder)
    {
        string baseUrl = DirectoryHost().TrimEnd('/');
        return $"{baseUrl}/{root}/{Uri.EscapeDataString(folder.Trim('/'))}/";
    }

    string DirectoryHost()
        => string.IsNullOrWhiteSpace(init.directoryHost)
            ? "https://a.111477.xyz"
            : init.directoryHost.TrimEnd('/');

    int TimeoutSeconds()
        => Math.Clamp(init.timeoutSeconds, 5, 60);

    int MaxFiles()
        => Math.Clamp(init.maxFiles, 1, 100);

    int MaxDirectoryEntries()
        => Math.Clamp(init.maxDirectoryEntries, 100, 10000);

    static List<string> TitleCandidates(string title, string original_title)
    {
        var result = new List<string>();
        foreach (string value in new[] { title, original_title })
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            string clean = Regex.Replace(value.Trim(), @"\s+", " ");
            clean = Regex.Replace(clean, @"\s*\(\d{4}\)\s*$", string.Empty).Trim();
            // Open Directory names are often sanitized for filesystem
            // compatibility: `Mad Max: Fury Road` is stored as
            // `Mad Max Fury Road`. Prefer that deterministic variant when
            // punctuation is present, but do not do a broad partial-title
            // search that could select another film.
            string filesystemSafe = Regex.Replace(clean, "[<>:\"/\\\\|?*]+", " ");
            filesystemSafe = Regex.Replace(filesystemSafe, @"\s+", " ").Trim();
            AddCandidate(filesystemSafe, result);
            AddCandidate(clean, result);
        }

        return result;

        static void AddCandidate(string value, List<string> values)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                !values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value);
            }
        }
    }

    static IEnumerable<string> FolderNameCandidates(string title, short year, bool isSeries)
    {
        if (isSeries)
        {
            yield return title;
            if (year > 0)
                yield return $"{title} ({year})";
            yield break;
        }

        if (year > 0)
            yield return $"{title} ({year})";
        yield return title;
    }

    static int ParseEpisode(string name, short requestedSeason)
    {
        string value = name ?? string.Empty;
        Match match = SeasonEpisodeRegex.Match(value);
        if (match.Success &&
            int.TryParse(match.Groups["season"].Value, out int season) &&
            int.TryParse(match.Groups["episode"].Value, out int episode))
        {
            return season == requestedSeason ? episode : -1;
        }

        match = OneByOneEpisodeRegex.Match(value);
        if (match.Success &&
            int.TryParse(match.Groups["season"].Value, out season) &&
            int.TryParse(match.Groups["episode"].Value, out episode))
        {
            return season == requestedSeason ? episode : -1;
        }

        match = NumberedEpisodeRegex.Match(value);
        return match.Success && int.TryParse(match.Groups["episode"].Value, out episode)
            ? episode
            : -1;
    }

    static string DetectFormat(string url)
    {
        string path = url ?? string.Empty;
        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch { }

        path = path.Split('?', '#')[0];
        if (path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase)) return "mkv";
        if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return "mp4";
        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) return "m3u8";
        if (path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)) return "webm";
        if (path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)) return "avi";
        if (path.EndsWith(".m2ts", StringComparison.OrdinalIgnoreCase)) return "m2ts";
        return null;
    }

    static bool IsMediaUrl(string url)
        => DetectFormat(url) != null;

    static string FindQuality(string value)
    {
        Match match = QualityRegex.Match(value ?? string.Empty);
        if (match.Success)
            return $"{match.Groups["quality"].Value}p";

        return Regex.IsMatch(value ?? string.Empty, @"\b(?:4k|uhd)\b", RegexOptions.IgnoreCase)
            ? "2160p"
            : null;
    }

    static string DisplayName(OpenDirectoryMedia media)
    {
        string name = Regex.Replace(media.Name ?? "Open Directory", @"\s+", " ").Trim();
        string extension = "." + media.Format;
        if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            name = name[..^extension.Length];

        return string.IsNullOrWhiteSpace(media.Quality)
            ? name
            : $"{media.Quality} • {name}";
    }

    static string StripTags(string value)
        => Regex.Replace(value ?? string.Empty, "<[^>]+>", string.Empty).Trim();

    static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
            uri.Scheme is "http" or "https";
    }

    sealed record DirectoryPage(string Url, List<OpenDirectoryEntry> Entries);
}
