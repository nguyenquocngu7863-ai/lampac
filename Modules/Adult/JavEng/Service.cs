using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace JavEng;

public static class JavEngTo
{
    public static string SiteHost = "https://javeng.tv";
    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // WordPress/Dooplay phan trang bang duong dan /page/N/ (khong phai ?page=N).
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = HttpUtility.UrlEncode(search);
            return host + "/?s=" + slug + (pg > 1 ? "&paged=" + pg : "");
        }

        // Trang chu cua javeng.tv chinh la archive "jav-eng-sub" trang 1,
        // va phan trang that nam o /jav-eng-sub/page/N/ (khong phai /page/N/).
        const string HomeCat = "jav-eng-sub";

        string path = "/" + HomeCat + "/";
        if (!string.IsNullOrEmpty(c))
        {
            c = c.Trim().Trim('/');
            if (c.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return NormalizePageUrl(c, pg);
            path = "/" + c + "/";
        }

        if (pg > 1)
            path += "page/" + pg + "/";

        return host + path;
    }

    public static string NormalizePageUrl(string url, int pg = 1)
    {
        if (string.IsNullOrWhiteSpace(url))
            return SiteHost;

        url = HttpUtility.HtmlDecode(url.Trim());
        if (url.StartsWith("//"))
            url = "https:" + url;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = SiteHost + (url.StartsWith("/") ? url : "/" + url);

        url = url.TrimEnd('/');

        if (pg > 1)
        {
            if (Regex.IsMatch(url, @"/page/\d+$", RegexOptions.IgnoreCase))
                url = Regex.Replace(url, @"/page/\d+$", "");
            url += "/page/" + pg;
        }

        return url + "/";
    }

    // DooPlay co 2 layout khac nhau:
    //   archive/home : danh sach nam trong #archive-content (truoc do co slider #slider-movies)
    //   genre        : danh sach nam trong <div class="items full">
    // Ca hai deu bi lap lai khi phan trang, nen phai cat ve dung vung danh sach.
    public static string ListHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        int start = html.IndexOf("id=\"archive-content\"", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            start = html.IndexOf("class=\"items full", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return html;

        var tail = html[start..];
        foreach (string stop in new[] {
            "class=\"sidebar", "class=\"fixed-sidebar", "id=\"sidebar",
            "id=\"darkfooter", "<footer", "class=\"pagination", "id=\"comments" })
        {
            int at = tail.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
            if (at > 0)
            {
                tail = tail[..at];
                break;
            }
        }

        return tail;
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in ListHtml(html).Split(new[] { "<article" }, StringSplitOptions.None))
        {
            if (raw.Length < 120)
                continue;

            // DooPlay markup doi chieu giua 2 kieu:
            //   home      : <h3><a href="...">Title</a></h3>
            //   category  : <h3 class="title">Title</h3>
            var hrefMatch = Regex.Match(raw, "<a[^>]+href=\"(https?://[^\"]+/jav-eng-sub/[^\"]+/)\"", RegexOptions.IgnoreCase);
            if (!hrefMatch.Success)
                hrefMatch = Regex.Match(raw, "<a[^>]+href=\"(/[a-z0-9\\-]+/[^\"]+/)\"", RegexOptions.IgnoreCase);
            if (!hrefMatch.Success)
                continue;

            string href = hrefMatch.Groups[1].Value;
            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                href = SiteHost + href;
            href = NormalizePageUrl(href);

            if (!seen.Add(href))
                continue;

            string title = "";
            var titleMatch = Regex.Match(raw, "<h3[^>]*>(?:<a[^>]*>)?([^<]{4,700}?)(?:</a>)?</h3>", RegexOptions.IgnoreCase);
            if (titleMatch.Success)
                title = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());

            string poster = "";
            var imgMatch = Regex.Match(raw, "<img[^>]+(?:data-src|src)=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (imgMatch.Success)
            {
                poster = imgMatch.Groups[1].Value;
                if (poster.StartsWith("//"))
                    poster = "https:" + poster;
            }

            if (string.IsNullOrEmpty(title) && imgMatch.Success)
            {
                var altMatch = Regex.Match(raw, "alt=\"([^\"]{4,700})\"", RegexOptions.IgnoreCase);
                if (altMatch.Success)
                    title = HttpUtility.HtmlDecode(altMatch.Groups[1].Value.Trim());
            }

            if (string.IsNullOrEmpty(title))
                continue;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "/?uri=" + HttpUtility.UrlEncode(href),
                name = title,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javeng",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    public sealed class PlayerOption
    {
        public string Post { get; set; } = "";
        public string Type { get; set; } = "movie";
        public string Nume { get; set; } = "1";
        public string Label { get; set; } = "";
    }

    public static string PlayerApiUrl(string post, string type, string nume)
    {
        if (string.IsNullOrWhiteSpace(type))
            type = "movie";
        if (string.IsNullOrWhiteSpace(nume))
            nume = "1";
        return SiteHost + "/wp-json/dooplayer/v2/" + post.Trim() + "/" + type.Trim().Trim('/') + "/" + nume.Trim();
    }

    public static List<PlayerOption> PlayerOptions(string html)
    {
        var options = new List<PlayerOption>();
        if (string.IsNullOrEmpty(html))
            return options;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, "<li[^>]+class=['\"][^'\"]*dooplay_player_option[^'\"]*['\"][^>]*>", RegexOptions.IgnoreCase))
        {
            string tag = m.Value;
            var post = Regex.Match(tag, "data-post\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
            var type = Regex.Match(tag, "data-type\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
            var nume = Regex.Match(tag, "data-nume\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);
            if (!post.Success || !nume.Success)
                continue;

            string tail = html.Substring(m.Index, Math.Min(html.Length - m.Index, 2000));
            var server = Regex.Match(tail, "<span[^>]+class=['\"][^'\"]*server[^'\"]*['\"][^>]*>([^<]{1,80})</span>", RegexOptions.IgnoreCase);
            string label = server.Success ? HttpUtility.HtmlDecode(server.Groups[1].Value.Trim()) : "Server " + nume.Groups[1].Value.Trim();
            string key = post.Groups[1].Value + "/" + nume.Groups[1].Value;
            if (!seen.Add(key))
                continue;

            options.Add(new PlayerOption
            {
                Post = post.Groups[1].Value.Trim(),
                Type = type.Success ? type.Groups[1].Value.Trim() : "movie",
                Nume = nume.Groups[1].Value.Trim(),
                Label = label
            });
        }

        return options;
    }

    public static string PlayerEmbed(string json)
    {
        if (string.IsNullOrEmpty(json))
            return "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return "";
            if (!doc.RootElement.TryGetProperty("embed_url", out var prop))
                return "";
            string embed = prop.ValueKind == System.Text.Json.JsonValueKind.String ? prop.GetString() : "";
            if (string.IsNullOrWhiteSpace(embed))
                return "";
            embed = HttpUtility.HtmlDecode(embed.Trim()).Replace("\\/", "/");
            if (embed.StartsWith("//"))
                embed = "https:" + embed;
            if (!embed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return "";
            return embed;
        }
        catch
        {
            return "";
        }
    }

    public static string NormalizeMediaUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";
        url = HttpUtility.HtmlDecode(url.Trim()).Replace("\\/", "/");
        if (url.StartsWith("//"))
            url = "https:" + url;
        if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var u))
            return "";
        if (!u.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !u.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return "";
        if (!string.IsNullOrEmpty(u.UserInfo))
            return "";
        return u.ToString();
    }

    public static bool IsHls(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;
        string clean = url.Split(new[] { '?', '#' })[0];
        return clean.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            || clean.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("/m3u8/");
    }

    public static bool IsMedia(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;
        string clean = url.Split(new[] { '?', '#' })[0].ToLowerInvariant();
        return clean.EndsWith(".m3u8") || clean.EndsWith(".m3u") || clean.EndsWith(".mp4") || clean.EndsWith(".m4v") || clean.EndsWith(".webm");
    }

    public static List<string> JwplayerFiles(string text)
    {
        var media = new List<string>();
        if (string.IsNullOrEmpty(text))
            return media;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string url)
        {
            url = NormalizeMediaUrl(url);
            if (IsMedia(url) && seen.Add(url))
                media.Add(url);
        }

        foreach (Match m in Regex.Matches(text, "\"file\"\\s*:\\s*\"(https?:[^\"\\\\]+)\"", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(text, "(https?://[^\\s\"'<>]+?\\.m3u8[^\\s\"'<>]*)", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(text, "(https?://[^\\s\"'<>]+?\\.mp4[^\\s\"'<>]*)", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);

        return media;
    }

    public static List<string> StaticMedia(string html)
    {
        var media = new List<string>();
        if (string.IsNullOrEmpty(html))
            return media;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string url)
        {
            url = NormalizeMediaUrl(url);
            if (IsMedia(url) && seen.Add(url))
                media.Add(url);
        }

        foreach (Match m in Regex.Matches(html, "<source[^>]+src=\"([^\"]+)\"", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(html, "<video[^>]+src=\"([^\"]+)\"", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(html, "\"file\"\\s*:\\s*\"(https?:[^\"\\\\]+)\"", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(html, "(https?://[^\\s\"'<>]+?\\.m3u8[^\\s\"'<>]*)", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(html, "(https?://[^\\s\"'<>]+?\\.mp4[^\\s\"'<>]*)", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);

        media.Sort((a, b) =>
        {
            bool ah = IsHls(a);
            bool bh = IsHls(b);
            if (ah == bh)
                return 0;
            return ah ? -1 : 1;
        });
        return media;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        // Lưu ý: trang 1 của /genre/uncensored/ trùng gần hết /jav-eng-sub/
        // chỉ vì các phim mới nhất đều thuộc nhóm này, KHÔNG phải taxonomy lỗi.
        // Kiểm chứng: main p2 ∩ uncensored p1 = 1, main p1 ∩ uncensored p2 = 0.
        var genres = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("JAV mới", host + "/javeng?c=jav-eng-sub"),
            new("Vietsub", host + "/javeng?c=genre/eng-sub"),
            new("Censored", host + "/javeng?c=genre/censored"),
            new("Uncensored", host + "/javeng?c=genre/uncensored"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/javeng"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javeng"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres
            }
        };
    }
}
