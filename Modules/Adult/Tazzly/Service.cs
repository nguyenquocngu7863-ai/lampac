using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Tazzly;

public static class TazzlyTo
{
    public static readonly string SiteHost = "https://tazzly.com";
    public static readonly string EmbedHost = "https://embed.tazzly.com";
    public static readonly string StaticHost = "https://static.tazzly.com";

    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
    public const int PerPage = 32;

    #region host
    static string Host(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return SiteHost;

        host = host.Trim().TrimEnd('/');
        if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            host = "https://" + host;

        return host;
    }
    #endregion

    #region uri
    // Trang chu phan trang bang JS (goi /api/video/latest) nen HTML bo qua ?page=N.
    // Home phai dung API; search + danh muc moi dung ?page=N cua HTML.
    public static bool IsApi(string search, string c)
        => string.IsNullOrWhiteSpace(search) && string.IsNullOrWhiteSpace(c);

    public static string ApiLatest(string host, int pg)
        => Host(host) + "/api/video/latest?page=" + Math.Max(1, pg).ToString(CultureInfo.InvariantCulture) + "&per_page=" + PerPage.ToString(CultureInfo.InvariantCulture);

    public static string Uri(string host, string search, string c, int pg)
    {
        host = Host(host);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = HttpUtility.UrlEncode(search.Trim());
            string url = host + "/phim-sex-tim-kiem?q=" + q + "&type=video";
            if (pg > 1)
                url += "&page=" + pg.ToString(CultureInfo.InvariantCulture);

            return url;
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string url = c.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? c.Trim()
                : host + "/phim-sex-danh-muc/" + c.Trim().Trim('/');

            if (pg > 1)
                url += (url.Contains("?") ? "&" : "?") + "page=" + pg.ToString(CultureInfo.InvariantCulture);

            return url;
        }

        return host + "/";
    }
    #endregion

    #region url
    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");

        int fragment = value.IndexOf('#');
        if (fragment >= 0)
            value = value.Substring(0, fragment);

        if (value.StartsWith("//"))
            value = "https:" + value;
        else if (value.StartsWith("/"))
            value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            value = SiteHost + "/" + value;

        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return null;

        if (!parsed.Host.Equals("tazzly.com", StringComparison.OrdinalIgnoreCase) &&
            !parsed.Host.EndsWith(".tazzly.com", StringComparison.OrdinalIgnoreCase))
            return null;

        if (parsed.AbsolutePath.IndexOf("/phim-sex/", StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        return value;
    }

    public static string NormalizeCover(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        if (value.StartsWith("//"))
            return "https:" + value;

        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return value;

        return StaticHost + "/" + value.TrimStart('/');
    }
    #endregion

    #region html list
    // The video-card co div long nhau nen khong the Regex "</div>"; cat theo
    // vi tri khoi tiep theo de moi phim chi lay 1 khoi.
    public static List<PlaylistItem> PlaylistFromHtml(string uri, string html, out int totalPages)
    {
        var playlists = new List<PlaylistItem>();
        totalPages = 0;

        if (string.IsNullOrWhiteSpace(html))
            return playlists;

        totalPages = ParseTotalPages(html);

        var starts = new List<int>();
        foreach (Match match in Regex.Matches(html, @"<div\b[^>]*\bclass\s*=\s*[""'][^""']*\bvideo-card\b[^""']*[""'][^>]*>", RegexOptions.IgnoreCase))
            starts.Add(match.Index);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < starts.Count; i++)
        {
            int from = starts[i];
            int to = i + 1 < starts.Count ? starts[i + 1] : Math.Min(html.Length, from + 4000);
            string card = html.Substring(from, to - from);

            var item = BuildItem(uri, card);
            if (item == null)
                continue;

            if (!seen.Add(item.bookmark.href))
                continue;

            playlists.Add(item);
        }

        return playlists;
    }

    static int ParseTotalPages(string html)
    {
        Match match = Regex.Match(html, @"class\s*=\s*[""'][^""']*\btotal-pages[""'][^>]*>\s*(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pages) && pages > 0)
            return pages;

        match = Regex.Match(html, @"data-total\s*=\s*[""'](\d+)[""']", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pages) && pages > 0)
            return pages;

        return 0;
    }

    static PlaylistItem BuildItem(string uri, string card)
    {
        Match hrefMatch = Regex.Match(card, @"<a\b[^>]*\bhref\s*=\s*[""']([^""']*/phim-sex/[^""']+)[""']", RegexOptions.IgnoreCase);
        if (!hrefMatch.Success)
            return null;

        string href = NormalizePageUrl(hrefMatch.Groups[1].Value);
        if (string.IsNullOrEmpty(href))
            return null;

        string name = Attr(card, "video-title", "title");
        if (string.IsNullOrEmpty(name))
        {
            Match text = Regex.Match(card, @"<h3\b[^>]*\bclass\s*=\s*[""'][^""']*\bvideo-title\b[^""']*[""'][^>]*>\s*<a\b[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (text.Success)
                name = WebUtility.HtmlDecode(Regex.Replace(text.Groups[1].Value, "<[^>]+>", "").Trim());
        }

        if (string.IsNullOrEmpty(name))
            return null;

        string poster = Match(card, @"<img\b[^>]*\bclass\s*=\s*[""'][^""']*\bvideo-cover-img\b[^""']*[""'][^>]*\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)
            ?? Match(card, @"<img\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);

        return NewItem(uri, href, name, NormalizeCover(poster), MatchTime(card));
    }

    static string Attr(string block, string className, string attribute)
    {
        Match match = Regex.Match(block, @"<a\b[^>]*\bclass\s*=\s*[""'][^""']*\b" + className + @"\b[^""']*[""'][^>]*\b" + attribute + @"\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (match.Success)
            return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();

        return null;
    }

    static string MatchTime(string card)
    {
        Match match = Regex.Match(card, @"<time\b[^>]*\bclass\s*=\s*[""'][^""']*\bduration-time\b[^""']*[""'][^>]*>([^<]+)</time>", RegexOptions.IgnoreCase);
        if (match.Success)
            return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();

        return null;
    }

    static string Match(string block, string pattern, RegexOptions options)
    {
        Match match = Regex.Match(block, pattern, options);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
    }
    #endregion

    #region json list
    public static List<PlaylistItem> PlaylistFromJson(string uri, string json, out int totalPages)
    {
        var playlists = new List<PlaylistItem>();
        totalPages = 0;

        if (string.IsNullOrWhiteSpace(json))
            return playlists;

        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return playlists;

            if (root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out int pages) && pages > 0)
                totalPages = pages;

            if (!root.TryGetProperty("videos", out var videos) || videos.ValueKind != JsonValueKind.Array)
                return playlists;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var video in videos.EnumerateArray())
            {
                if (video.ValueKind != JsonValueKind.Object)
                    continue;

                string href = NormalizePageUrl(Str(video, "url"));
                if (string.IsNullOrEmpty(href) || !seen.Add(href))
                    continue;

                string name = Str(video, "title");
                if (string.IsNullOrEmpty(name))
                    continue;

                var item = NewItem(uri, href, name, NormalizeCover(Str(video, "cover")), FormatTime(Str(video, "duration")));
                if (item != null)
                    playlists.Add(item);
            }
        }
        catch
        {
        }

        return playlists;
    }

    static string Str(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    static string FormatTime(string seconds)
    {
        if (!int.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total) || total <= 0)
            return null;

        int h = total / 3600;
        int m = total % 3600 / 60;
        int s = total % 60;

        return h > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, s)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", m, s);
    }
    #endregion

    #region item
    static PlaylistItem NewItem(string uri, string href, string name, string poster, string time)
    {
        return new PlaylistItem()
        {
            video = uri + "?uri=" + HttpUtility.UrlEncode(href),
            name = name,
            picture = poster,
            time = time,
            json = true,
            bookmark = new Bookmark()
            {
                site = "tazzly",
                href = href,
                image = poster
            }
        };
    }
    #endregion

    #region player
    // Trang chi tiet chi co 1 iframe tro sang embed host, lay code de tao duong dan m3u8.
    public static string EmbedPlayer(string pageHtml, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageHtml))
            return null;

        Match match = Regex.Match(pageHtml, @"https://embed\.tazzly\.com/embed/([a-zA-Z0-9_-]+)/p(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Value;

        match = Regex.Match(pageHtml, @"/embed/([a-zA-Z0-9_-]+)/p(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
            return EmbedHost + match.Value;

        return null;
    }

    // Trang player noi san m3u8 trong bien JS: let m3u8_file_url = '/phim-sex/<code>/cdn.m3u8';
    public static string M3u8Url(string playerHtml)
    {
        if (string.IsNullOrWhiteSpace(playerHtml))
            return null;

        Match match = Regex.Match(playerHtml, @"m3u8_file_url\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        string path = WebUtility.HtmlDecode(match.Groups[1].Value).Replace("\\/", "/").Trim();
        if (string.IsNullOrEmpty(path))
            return null;

        if (path.StartsWith("//"))
            return "https:" + path;

        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return path;

        if (!path.StartsWith("/"))
            path = "/" + path;

        return EmbedHost + path;
    }
    #endregion

    #region menu
    public static List<MenuItem> Menu(string host)
    {
        host = Host(host);

        return new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/tazzly"
            },
            new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<MenuItem>()
                {
                    new("Mới nhất", host + "/tazzly"),
                    new("Việt Nam", host + "/tazzly?c=phim-sex-viet-nam"),
                    new("Vietsub", host + "/tazzly?c=phim-sex-vietsub"),
                    new("JAV HD", host + "/tazzly?c=javhd"),
                    new("Hentai", host + "/tazzly?c=hentai-sex"),
                    new("Trung Quốc", host + "/tazzly?c=phim-sex-trung-quoc"),
                    new("Âu - Mỹ", host + "/tazzly?c=sex-chau-au")
                }
            }
        };
    }
    #endregion
}
