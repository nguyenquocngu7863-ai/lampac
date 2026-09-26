using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Viet69kz;

public static class Viet69kzTo
{
    public static readonly string SiteHost = "https://viet69kz.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    public static string PlayerApi => SiteHost + "/api/player";

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host))
            host = SiteHost;

        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = Slugify(search);
            if (string.IsNullOrEmpty(slug))
                return host + "/";

            return host + "/search/" + slug + "/";
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string url = c.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? c.TrimEnd('/')
                : host + "/" + c.Trim('/');

            if (pg > 1)
                url += "/trang/" + pg;

            return url.EndsWith("/") ? url : url + "/";
        }

        if (pg > 1)
            return host + "/trang/" + pg + "/";

        return host + "/";
    }

    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        value = value.Trim().ToLowerInvariant();
        string[][] map =
        {
            new[] { "a", "àáạảãâầấậẩẫăằắặẳẵ" },
            new[] { "e", "èéẹẻẽêềếệểễ" },
            new[] { "i", "ìíịỉĩ" },
            new[] { "o", "òóọỏõôồốộổỗơờớợởỡ" },
            new[] { "u", "ùúụủũưừứựửữ" },
            new[] { "y", "ỳýỵỷỹ" },
            new[] { "d", "đ" }
        };

        foreach (string[] pair in map)
        {
            foreach (char ch in pair[1])
                value = value.Replace(ch.ToString(), pair[0]);
        }

        value = Regex.Replace(value, @"[^a-z0-9\s-]", "");
        value = Regex.Replace(value, @"[\s_]+", "-");
        return Regex.Replace(value, @"-+", "-").Trim('-');
    }

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim());
        int fragment = value.IndexOf('#');
        if (fragment >= 0)
            value = value.Substring(0, fragment);

        int query = value.IndexOf('?');
        if (query >= 0)
            value = value.Substring(0, query);

        if (value.StartsWith("//"))
            value = "https:" + value;
        else if (value.StartsWith("/"))
            value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            value = SiteHost + "/" + value;

        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return null;

        if (!parsed.Host.Equals("viet69kz.com", StringComparison.OrdinalIgnoreCase) &&
            !parsed.Host.EndsWith(".viet69kz.com", StringComparison.OrdinalIgnoreCase))
            return null;

        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//"))
            value = "https:" + value;
        else if (value.StartsWith("/"))
            value = SiteHost + value;

        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<PlaylistItem>();
        if (string.IsNullOrWhiteSpace(html))
            return playlists;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocks = Regex.Matches(
            html,
            @"<li\b[^>]*\bclass\s*=\s*[""'][^""']*\bitem-movie\b[^""']*[""'][^>]*>.*?</li\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match blockMatch in blocks)
        {
            string block = blockMatch.Value;
            Match hrefMatch = Regex.Match(block, @"<a\b[^>]*\bhref\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!hrefMatch.Success)
                continue;

            string href = NormalizePageUrl(hrefMatch.Groups[1].Value);
            if (string.IsNullOrEmpty(href) || !seen.Add(href))
                continue;

            Match titleMatch = Regex.Match(block, @"<a\b[^>]*\btitle\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!titleMatch.Success)
                titleMatch = Regex.Match(block, @"<div\b[^>]*\bclass\s*=\s*[""'][^""']*\btitle-movie\b[^""']*[""'][^>]*>(.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            string name = titleMatch.Success
                ? WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[1].Value, "<[^>]+>", "").Trim())
                : "";
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = GetPoster(block);

            playlists.Add(new PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "viet69kz",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    static string GetPoster(string block)
    {
        Match match = Regex.Match(block, @"<img\b[^>]*\bdata-original\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(block, @"<img\b[^>]*\bdata-src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(block, @"<img\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);

        string poster = NormalizeMediaUrl(match.Success ? match.Groups[1].Value : null);
        if (!string.IsNullOrEmpty(poster) && poster.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        return poster;
    }

    public static (string id, List<string> servers) VideoData(string html)
    {
        string id = null;
        var servers = new List<string>();
        if (string.IsNullOrWhiteSpace(html))
            return (id, servers);

        Match videoTag = Regex.Match(html, @"<[^>]*\bid\s*=\s*[""']video[""'][^>]*>", RegexOptions.IgnoreCase);
        if (videoTag.Success)
        {
            Match idMatch = Regex.Match(videoTag.Value, @"\bdata-video-id\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (idMatch.Success)
                id = idMatch.Groups[1].Value.Trim();
        }

        if (string.IsNullOrEmpty(id))
        {
            Match idMatch = Regex.Match(html, @"\bdata-video-id\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (idMatch.Success)
                id = idMatch.Groups[1].Value.Trim();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(html, @"\bdata-server-id\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase))
        {
            string server = match.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(server) && seen.Add(server))
                servers.Add(server);
        }

        return (id, servers);
    }

    public static Dictionary<string, string> StreamLinks(string json)
    {
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return links;

        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement data = document.RootElement;
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var dataElement))
                data = dataElement;

            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
            {
                foreach (var source in sources.EnumerateArray())
                {
                    if (source.ValueKind != JsonValueKind.Object)
                        continue;

                    string file = GetString(source, "file");
                    string type = GetString(source, "type");
                    AddLink(links, file, type);
                }
            }

            AddLink(links, GetString(data, "file"), GetString(data, "type"));
            AddLink(links, GetString(data, "url"), GetString(data, "type"));
        }
        catch
        {
        }

        if (links.Count == 0)
        {
            foreach (Match match in Regex.Matches(json, @"""file""\s*:\s*""(https?://[^""]+)""", RegexOptions.IgnoreCase))
                AddLink(links, match.Groups[1].Value, null);
        }

        return links;
    }

    public static string IframeSource(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement data = document.RootElement;
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var dataElement))
                data = dataElement;

            if (data.ValueKind != JsonValueKind.Object)
                return null;

            string type = GetString(data, "type");
            if (!string.Equals(type, "iframe", StringComparison.OrdinalIgnoreCase))
                return null;

            string source = GetString(data, "source");
            if (string.IsNullOrEmpty(source))
                source = GetString(data, "url");
            if (string.IsNullOrEmpty(source))
                source = GetString(data, "embed");

            return NormalizeMediaUrl(source);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsMedia(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string path = value;
        int query = path.IndexOfAny(new[] { '?', '#' });
        if (query >= 0)
            path = path.Substring(0, query);

        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHls(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        int query = value.IndexOfAny(new[] { '?', '#' });
        string path = query >= 0 ? value.Substring(0, query) : value;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
    }

    static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    static void AddLink(Dictionary<string, string> links, string file, string type)
    {
        file = NormalizeMediaUrl(file);
        if (!IsMedia(file))
            return;

        string label = string.IsNullOrWhiteSpace(type) ? (IsHls(file) ? "HLS" : "MP4") : type.Trim();
        string key = label;
        int suffix = 2;
        while (links.ContainsKey(key))
            key = label + " " + suffix++;

        if (!links.ContainsValue(file))
            links[key] = file;
    }

    public static List<MenuItem> Menu(string host)
    {
        host = host.TrimEnd('/');
        return new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/viet69kz"
            },
            new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<MenuItem>()
                {
                    new("Mới nhất", host + "/viet69kz"),
                    new("Việt Nam", host + "/viet69kz?c=phim-sex-viet-nam"),
                    new("Nhật Bản", host + "/viet69kz?c=phim-sex-nhat-ban"),
                    new("Trung Quốc", host + "/viet69kz?c=phim-sex-trung-quoc"),
                    new("Âu - Mỹ", host + "/viet69kz?c=phim-sex-chau-au"),
                    new("Vietsub", host + "/viet69kz?c=phim-sex-vietsub")
                }
            }
        };
    }
}
