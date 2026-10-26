using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace Lon69;

public static class Lon69To
{
    public static string SiteHost = "https://Lon69.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    // ========== Uri ==========
    // Sort phải đi sau /page/N/ → ?sort=VALUE hoặc &sort=VALUE
    public static string BuildUri(string host, string search, string c, string sort, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "s=" + Uri.EscapeDataString(search.Trim());
            string base_url = pg > 1 ? $"{host}/page/{pg}/" : $"{host}/";
            return base_url + (base_url.Contains('?') ? "&" : "?") + q
                + (!string.IsNullOrEmpty(sort) ? (base_url.Contains('?') ? "&" : "?") + "sort=" + sort : "");
        }
        if (string.IsNullOrWhiteSpace(c))
        {
            string base_url = pg > 1 ? $"{host}/page/{pg}/" : $"{host}/";
            return base_url + (!string.IsNullOrEmpty(sort) ? (base_url.Contains('?') ? "&" : "?") + "sort=" + sort : "");
        }
        string raw = c.Trim().Trim('/');
        string baseUrl = host + "/" + raw;
        string page_url = pg > 1 ? baseUrl.TrimEnd('/') + "/page/" + pg + "/" : baseUrl;
        return page_url + (!string.IsNullOrEmpty(sort) ? (page_url.Contains('?') ? "&" : "?") + "sort=" + sort : "");
    }

    // ========== Sort ==========
    public static readonly string[] SortWhitelist = { "new", "views", "likes", "rating", "random" };
    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    // Context nào có sort? Home + category + tag + search đều có <select id="crl-sort-select">
    public static (string name, string sort)[] AllSorts => new[]
    {
        ("Mới nhất",  "new"),
        ("Xem nhiều","views"),
        ("Nhiều Like","likes"),
        ("Đánh giá",  "rating"),
        ("Ngẫu nhiên","random"),
    };

    public static string ClampSort(string sort, string search, string c)
    {
        sort = NormalizeSort(sort);
        if (string.IsNullOrEmpty(sort)) return null;
        return AllSorts.Any(o => o.sort == sort) ? sort : null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mới"
        : sort == "views" ? "xem nhiều"
        : sort == "likes" ? "like"
        : sort == "rating" ? "đánh giá"
        : sort == "random" ? "ngẫu nhiên" : sort;

    // ========== Page ==========
    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim());
        int frag = value.IndexOf('#');
        if (frag >= 0) value = value.Substring(0, frag);
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", System.StringComparison.OrdinalIgnoreCase)) value = SiteHost + "/" + value;
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        if (!parsed.Host.EndsWith("lon69.com", System.StringComparison.OrdinalIgnoreCase))
            return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        return value.StartsWith("http", System.StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 1;
        int i = html.IndexOf("class=\"nav-links\"", System.StringComparison.OrdinalIgnoreCase);
        if (i < 0) i = html.IndexOf("class=\"pagination\"", System.StringComparison.OrdinalIgnoreCase);
        if (i < 0) return 1;
        string slice = html.Substring(i, Math.Min(4000, html.Length - i));
        int max = 1;
        foreach (Match m in Regex.Matches(slice, @"/page/(\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        return max > 0 ? max : 1;
    }

    // ========== Playlist ==========
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocks = Regex.Matches(html, @"<article[^>]*class=""[^""]*movie-card[^""]*""[^>]*>([\s\S]*?)</article>", RegexOptions.IgnoreCase);
        foreach (Match bm in blocks)
        {
            string block = bm.Groups[1].Value;
            var aMatch = Regex.Match(block, @"<a[^>]+href=""([^""]+)""", RegexOptions.IgnoreCase);
            if (!aMatch.Success) continue;
            string pageUrl = NormalizePageUrl(aMatch.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            var titleMatch = Regex.Match(block, @"<h2[^>]*>([\s\S]*?)</h2>", RegexOptions.IgnoreCase);
            string name = "";
            if (titleMatch.Success)
                name = WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[1].Value, "<[^>]+>", " ")).Trim();
            if (string.IsNullOrEmpty(name)) continue;
            var imgMatch = Regex.Match(block, @"<img[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase);
            string poster = null;
            if (imgMatch.Success) poster = NormalizeMediaUrl(imgMatch.Groups[1].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "lon69", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Categories ==========
    public static List<(string slug, string name)> Categories(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string href = m.Groups[1].Value;
            string txt = Regex.Replace(WebUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " ").Trim();
            if (href.StartsWith(SiteHost) && href.Contains("/phim-sex-") && href.EndsWith("/") && !seen.Contains(href))
            {
                string slug = href.Replace(SiteHost + "/", "").Trim('/');
                res.Add((slug, txt));
                seen.Add(href);
            }
        }
        return res;
    }

    // ========== NavTags — tags từ menu nav (giới hạn 4 tag) ==========
    public static List<(string slug, string name)> NavTags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]+href=""(https?://lon69\.com/tag/([^""]+)/)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = m.Groups[2].Value.Trim().ToLowerInvariant();
            string txt = Regex.Replace(WebUtility.HtmlDecode(m.Groups[3].Value), "<[^>]+>", " ").Trim();
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(txt) || !seen.Add(slug)) continue;
            res.Add((slug, txt));
        }
        return res;
    }

    // ========== Tags — crawl N trang home để thu thập tag từ CSS class card ==========
    public static List<(string slug, string name)> DiscoverTags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Extract from movie-card classes: tag-slug
        foreach (Match m in Regex.Matches(html, @"tag-([a-z0-9_-]+)", RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(slug) || slug.Length < 2 || !seen.Add(slug)) continue;
            // Skip non-Latin or nonsense tags
            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z]+$")) continue;
            res.Add((slug, slug.Replace("-", " ")));
        }
        return res;
    }

    // ========== MenuHead (sort row) ==========
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/lon69";
        var res = new List<MenuItem>
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "lon69", playlist_url = root }
        };
        // Dòng 2: Sắp xếp — chỉ hiện khi context có sort (home/category/tag/search đều có)
        var sub = new List<MenuItem>(AllSorts.Length);
        foreach (var (name, s) in AllSorts)
            sub.Add(new MenuItem(name, root + "?c=" + HttpUtility.UrlEncode(c)
                + (string.IsNullOrEmpty(s) ? "" : "&sort=" + s)));
        res.Add(new MenuItem(){ title = $"Sắp xếp: {SortLabel(sort)}", playlist_url = "submenu", submenu = sub });
        return res;
    }

    // ========== Menu base (taxonomy) ==========
    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats, List<(string slug, string name)> tags)
    {
        host = host.TrimEnd('/');
        string root = host + "/lon69";
        string url(string c, string sort = null) =>
            root + "?c=" + HttpUtility.UrlEncode(c)
            + (string.IsNullOrEmpty(sort) ? "" : "&sort=" + sort);

        var rootMenu = new List<MenuItem>();

        // Danh mục (category)
        if (cats != null && cats.Count > 0)
        {
            var catSub = cats.Select(x => new MenuItem(x.name, url(x.slug))).ToList();
            rootMenu.Add(new MenuItem(){ title = "Thể loại", playlist_url = "submenu", submenu = catSub });
        }

        // Tags
        if (tags != null && tags.Count > 0)
        {
            var tagSub = tags.Select(x => new MenuItem(x.name, url("tag/" + x.slug))).ToList();
            rootMenu.Add(new MenuItem(){ title = "Thẻ", playlist_url = "submenu", submenu = tagSub });
        }

        return rootMenu;
    }
}
