using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace ClipHot;

public static class ClipHotTo
{
    public static string SiteHost = "https://cliphotfb.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri (WordPress) ==========
    public static string Uri(string host, string search, string c, string sort, int pg)
    {
        // Ép buộc dùng site host thật, tránh init.host bị ghi đè bằng route module
        host = SiteHost.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = System.Uri.EscapeDataString(search.Trim());
            return pg > 1 ? host + "/search/" + q + "/trang/" + pg + "/" : host + "/search/" + q + "/";
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return pg > 1 ? raw.TrimEnd('/') + "/trang/" + pg + "/" : raw;
            string baseUrl = host + "/" + raw.TrimStart('/');
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            return pg > 1 ? baseUrl + "trang/" + pg + "/" : baseUrl;
        }

        return pg > 1 ? host + "/trang/" + pg + "/" : host + "/";
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"/trang/(\d+)"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        // Kiểm tra link rel=next
        if (Regex.IsMatch(html, @"rel\s*=\s*[""']next[""']", RegexOptions.IgnoreCase))
            max = Math.Max(max, 2);
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
        if (!parsed.Host.EndsWith("cliphotfb.com", StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static string HostOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return null;
        return parsed.GetLeftPart(UriPartial.Authority);
    }

    // ========== Playlist ==========
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<li\b[^>]*\bclass\s*=\s*""[^""]*\bitem-movie\b[^""]*""[^>]*>\s*<a\b[^>]*\btitle\s*=\s*""([^""]+)""[^>]*\bhref\s*=\s*""([^""]+\.html[^""]*)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[1].Value));
            string pageUrl = NormalizePageUrl(m.Groups[2].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            if (string.IsNullOrEmpty(name)) continue;
            int endLi = html.IndexOf("</li>", m.Index, StringComparison.OrdinalIgnoreCase);
            int len = endLi > 0 ? Math.Min(2000, endLi - m.Index) : 2000;
            string snippet = html.Substring(m.Index, len);
            string poster = null;
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-original\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success) pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-src\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success) pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "cliphot", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Video ID từ trang chi tiết ==========
    public static string VideoId(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"data-video-id\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string ServerId(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"data-server-id\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "1";
    }

    // ========== Taxonomies ==========
    public static List<(string slug, string name)> Categories(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""(https?://cliphotfb\.com)?/phim-sex-([a-z\-]+)/""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "phim-sex-" + m.Groups[2].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[3].Value), "<[^>]+>", " "));
            if (string.IsNullOrEmpty(name) || name.Length > 40 || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    public static List<(string slug, string name)> Tags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""(https?://cliphotfb\.com)?/tag/([a-z0-9\-]+)/?""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "tag/" + m.Groups[2].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[3].Value), "<[^>]+>", " "));
            name = Regex.Replace(name, @"\s*\(\d+\)\s*$", "").Trim();
            if (string.IsNullOrEmpty(name) || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    // ========== Sort ==========
    public static readonly (string name, string sort)[] TaxSorts =
    {
        ("Theo ngày", ""),
        ("Theo lượt xem", "views"),
    };

    public static bool IsTaxonomy(string c) =>
        !string.IsNullOrWhiteSpace(c) &&
        (c.StartsWith("phim-sex-") || c.StartsWith("tag/"));

    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;
        if (string.IsNullOrWhiteSpace(c)) return null;
        if (IsTaxonomy(c)) return TaxSorts;
        return null;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null) return null;
        sort = NormalizeSort(sort);
        if (string.IsNullOrEmpty(sort)) return null;
        foreach (var o in opts) if (o.sort == sort) return sort;
        return null;
    }

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        return sort == "views" ? "views" : null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "ngày"
        : sort == "views" ? "lượt xem" : sort;

    // ===== head của menu =====
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/cliphot";
        var res = new List<MenuItem>(2)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
        var opts = SortsFor(search, c);
        if (opts == null || opts.Length == 0) return res;
        var sub = new List<MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
            sub.Add(new MenuItem(name, root + "?c=" + HttpUtility.UrlEncode(c)
                + (string.IsNullOrEmpty(s) ? "" : "&sort=" + s)));
        res.Add(new MenuItem(){ title = $"Sắp xếp: {SortLabel(sort)}", playlist_url = "submenu", submenu = sub });
        return res;
    }

    // ===== base của menu =====
    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats, List<(string slug, string name)> tags)
    {
        host = host.TrimEnd('/');
        string url(string c, string sort = null) => host + "/cliphot?c=" + HttpUtility.UrlEncode(c)
            + (string.IsNullOrEmpty(sort) ? "" : "&sort=" + sort);
        var root = new List<MenuItem>(4)
        {
            new MenuItem(){ title = "Bảng xếp hạng", playlist_url = "submenu", submenu = new List<MenuItem>(){
                new("Mới nhất", url("")),
                new("Phổ biến", url("phim-sex-viet-nam")),
            }}
        };
        List<MenuItem> TaxMenu(List<(string slug, string name)> items)
        {
            var gm = new List<MenuItem>(items.Count);
            foreach (var (slug, name) in items)
                gm.Add(new MenuItem(string.IsNullOrEmpty(name) ? slug : name, url(slug)));
            return gm;
        }
        if (cats != null && cats.Count > 0)
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = TaxMenu(cats) });
        if (tags != null && tags.Count > 0)
            root.Add(new MenuItem() { title = "Tag", playlist_url = "submenu", submenu = TaxMenu(tags) });
        return root;
    }
}
