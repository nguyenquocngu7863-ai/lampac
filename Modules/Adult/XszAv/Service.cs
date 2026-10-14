using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace XszAv;

public static class XszAvTo
{
    public static string SiteHost = "https://xsz-av.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    public static string Uri(string host, string search, string c, string sort, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(c)) c = "videos/japanese";
        c = c.Trim().Trim('/');

        string path;
        if (!string.IsNullOrWhiteSpace(search))
            path = "search/videos/" + System.Uri.EscapeDataString(search.Trim());
        else
            path = c;

        var q = new List<string>();
        string ns = NormalizeSort(sort);
        if (!string.IsNullOrEmpty(ns))
            q.Add("o=" + ns);
        if (pg > 1)
            q.Add("page=" + pg);

        string url = host + "/" + path;
        if (q.Count > 0)
            url += "?" + string.Join("&", q);
        return url;
    }

    public static readonly string[] SortWhitelist = { "bw", "mv", "tf", "lg" };
    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        if (sort == "recent" || sort == "mr" || sort == "default") return null;
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    public static (string name, string sort)[] AllSorts =
    {
        ("Mới nhất", ""),
        ("Đang xem", "bw"),
        ("Xem nhiều", "mv"),
        ("Yêu thích", "tf"),
        ("Dài nhất", "lg"),
    };

    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;
        return AllSorts;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        if (SortsFor(search, c) == null) return null;
        sort = NormalizeSort(sort);
        return sort;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mới nhất"
        : sort == "bw" ? "đang xem"
        : sort == "mv" ? "xem nhiều"
        : sort == "tf" ? "yêu thích"
        : sort == "lg" ? "dài nhất" : sort;

    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"[?&]page=(\d+)"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        // qwik json may also expose page count; enough
        return max;
    }

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim());
        int frag = value.IndexOf('#');
        if (frag >= 0) value = value[..frag];
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = SiteHost + "/" + value;
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        if (!parsed.Host.EndsWith("xsz-av.com", StringComparison.OrdinalIgnoreCase))
            return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // anchor with image alt as title
        foreach (Match m in Regex.Matches(html,
            @"<a\b[^>]*\bhref\s*=\s*""(/video/(\d+))""[^>]*>.*?<img\b[^>]*?(?:data-src|src)\s*=\s*""([^""]+)""[^>]*\salt\s*=\s*""([^""]*)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string pageUrl = NormalizePageUrl(m.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[4].Value));
            if (string.IsNullOrEmpty(name))
                name = "Video " + m.Groups[2].Value;
            string poster = NormalizeMediaUrl(m.Groups[3].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "xszav", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    public const string Route = "xszav";
    public static string VideoMark(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"<video\b[^>]*\bsrc\s*=\s*""([^""]+\.m3u8)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // Tags parsed from /tags
    public static List<(string slug, string name)> Tags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a\b[^>]*\bhref\s*=\s*""/tags/([A-Za-z0-9_-]+)""[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[2].Value));
            if (string.IsNullOrEmpty(name) || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    // Menu helpers
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/xszav";
        if (string.IsNullOrWhiteSpace(c)) c = "videos/japanese";
        var res = new List<MenuItem>(2)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
        var opts = SortsFor(search, c);
        if (opts != null)
        {
            var sub = new List<MenuItem>();
            foreach (var (name, s) in opts)
            {
                sub.Add(new MenuItem(name, root + "?c=" + HttpUtility.UrlEncode(string.IsNullOrWhiteSpace(c) ? "videos/japanese" : c)
                    + (string.IsNullOrEmpty(s) ? "" : "&sort=" + s)));
            }
            res.Add(new MenuItem(){ title = $"Sắp xếp: {SortLabel(NormalizeSort(sort))}", playlist_url = "submenu", submenu = sub });
        }
        return res;
    }

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> tags)
    {
        host = host.TrimEnd('/');
        string url(string c, string sort = null) => host + "/xszav?c=" + HttpUtility.UrlEncode(c)
            + (string.IsNullOrEmpty(sort) ? "" : "&sort=" + sort);
        var cats = new List<(string path, string name)>
        {
            ("videos/japanese", "Japanese av"),
            ("videos/uncensored", "Uncensored"),
            ("videos/uncensored-leak", "Uncensored leak"),
            ("videos/amateur", "Amateur"),
            ("videos/chinese", "Chinese"),
            ("videos/korean", "Korean"),
            ("videos/western", "Western"),
            ("videos/hanime", "Hanime"),
        };
        var root = new List<MenuItem>();
        root.Add(new MenuItem(){ title = "Danh mục", playlist_url = "submenu", submenu = cats.Select(x => new MenuItem(x.name, url(x.path))).ToList() });
        if (tags != null && tags.Count > 0)
            root.Add(new MenuItem(){ title = "Tag", playlist_url = "submenu", submenu = tags.Select(x => new MenuItem(string.IsNullOrEmpty(x.name) ? x.slug : x.name, url("tags/" + x.slug))).ToList() });
        return root;
    }
}