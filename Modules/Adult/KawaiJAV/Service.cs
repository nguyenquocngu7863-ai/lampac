using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace KawaiJAV;

public static class KawaiJAVTo
{
    public static string SiteHost = "https://kawai-jav.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri ==========
    // Danh sách: /en/most-view.html?page=N
    // Search: /en/search.html?q=<term>&page=N
    // Category: /en/categories/<id>.<slug>.html?page=N
    // Star: /en/pornstars/<id>/<slug>.html?page=N
    // Phim chi tiết: /en/watch/<id>/<slug>.html
    public static string Uri(string host, string search, string c, string sort, int pg)
    {
        host = SiteHost;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = System.Uri.EscapeDataString(search.Trim());
            return pg > 1 ? host + "/en/search.html?q=" + q + "&page=" + pg : host + "/en/search.html?q=" + q;
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return pg > 1 ? raw.TrimEnd('/') + (raw.Contains("?") ? "&page=" + pg : "?page=" + pg) : raw;

            // Tag: /kawaijav?c=tag/<keyword> -> /en/search.html?keyword=<keyword>
            if (raw.StartsWith("tag/", StringComparison.OrdinalIgnoreCase))
            {
                string keyword = HttpUtility.UrlDecode(raw.Substring(4)).Trim();
                string tagUrl = host + "/en/search.html?keyword=" + HttpUtility.UrlEncode(keyword);
                if (pg > 1) tagUrl += "&page=" + pg;
                string sortVal = NormalizeSort(sort);
                if (!string.IsNullOrEmpty(sortVal)) tagUrl += "&sort=" + sortVal;
                return tagUrl;
            }

            // Category: /en/categories/<id>.<slug>.html?page=N&sort=views
            if (raw.StartsWith("categories/", StringComparison.OrdinalIgnoreCase))
            {
                string catUrl = host + "/en/" + raw;
                if (!catUrl.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) catUrl += ".html";
                if (pg > 1) catUrl += "?page=" + pg;
                string sortVal = NormalizeSort(sort);
                if (!string.IsNullOrEmpty(sortVal)) catUrl += (catUrl.Contains("?") ? "&" : "?") + "sort=" + sortVal;
                return catUrl;
            }

            string baseUrl = host + "/" + raw.TrimStart('/');
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            string url = baseUrl + (baseUrl.Contains("?") ? "&page=" + pg : "?page=" + pg);
            string s = NormalizeSort(sort);
            if (!string.IsNullOrEmpty(s) && !url.Contains("sort="))
                url += "&sort=" + s;
            return url;
        }

        string homeUrl = pg > 1 ? host + "/en/most-view.html?page=" + pg : host + "/en/most-view.html";
        string hs = NormalizeSort(sort);
        if (!string.IsNullOrEmpty(hs) && !homeUrl.Contains("sort="))
            homeUrl += "&sort=" + hs;
        return homeUrl;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"\?page=(\d+)"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
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
        if (!parsed.Host.EndsWith("kawai-jav.com", StringComparison.OrdinalIgnoreCase)) return null;
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
    // Card: <a href="/en/watch/<id>/<slug>.html"><img data-src="..." alt="..."/>
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a[^>]*\bhref\s*=\s*""([^""]*/watch/\d+/[^""]*)""[^>]*>(?:\s*<img[^>]*?\bdata-src\s*=\s*""([^""]*)""[^>]*?\balt\s*=\s*""([^""]*)""[^>]*>)?",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string pageUrl = NormalizePageUrl(m.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[3].Value));
            if (string.IsNullOrEmpty(name)) continue;
            string poster = NormalizeMediaUrl(m.Groups[2].Value);
            if (!string.IsNullOrEmpty(poster) && poster.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                poster = "https:" + poster.Substring(5);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "kawaijav", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Video: parse videoData từ trang chi tiết ==========
    public static (string hd, string sd) ParseVideoData(string html)
    {
        string hd = null, sd = null;
        if (string.IsNullOrEmpty(html)) return (null, null);

        var m = Regex.Match(html, @"videoData\s*=\s*(\{.*?\})\s*;", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success)
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                if (doc.RootElement.TryGetProperty("videoFile", out var vf))
                {
                    if (vf.TryGetProperty("HD", out var hdEl)) hd = NormalizeMediaUrl(hdEl.GetString());
                    if (vf.TryGetProperty("SD", out var sdEl)) sd = NormalizeMediaUrl(sdEl.GetString());
                }
                if (string.IsNullOrEmpty(hd) && doc.RootElement.TryGetProperty("videoHD", out var hdEl2))
                    hd = NormalizeMediaUrl(hdEl2.GetString());
                if (string.IsNullOrEmpty(sd) && doc.RootElement.TryGetProperty("videoSD", out var sdEl2))
                    sd = NormalizeMediaUrl(sdEl2.GetString());
            }
            catch { }
        }

        if (string.IsNullOrEmpty(hd))
        {
            var hdM = Regex.Match(html, @"""videoHD""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (hdM.Success) hd = NormalizeMediaUrl(hdM.Groups[1].Value);
        }
        if (string.IsNullOrEmpty(sd))
        {
            var sdM = Regex.Match(html, @"""videoSD""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (sdM.Success) sd = NormalizeMediaUrl(sdM.Groups[1].Value);
        }

        return (hd, sd);
    }

    // ========== Taxonomies: Categories ==========
    public static List<(string slug, string name)> Categories(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""(?:https?://kawai-jav\.com)?/en/categories/([0-9]+\.[a-z0-9\-]+)(?:\.html)?/?""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "categories/" + m.Groups[1].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " "));
            if (string.IsNullOrEmpty(name) || name.Length > 40 || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    // ========== Taxonomies: Tags (đám mây thẻ ở cuối trang) ==========
    // Link: /en/search.html?keyword=<tag>
    public static List<(string slug, string name)> Tags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"href\s*=\s*""(?:https?://kawai-jav\.com)?/en/search\.html\?keyword=([^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string keyword = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " ")).Trim();
            if (string.IsNullOrEmpty(name)) name = keyword;
            if (string.IsNullOrEmpty(name) || name.Length > 40 || !seen.Add(keyword)) continue;
            res.Add((keyword, name));
            if (res.Count >= 60) break;
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
        (c.StartsWith("categories/") || c.StartsWith("pornstars/"));

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
        string root = host + "/kawaijav";
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
        string url(string c, string sort = null) => host + "/kawaijav?c=" + HttpUtility.UrlEncode(c)
            + (string.IsNullOrEmpty(sort) ? "" : "&sort=" + sort);
        var root = new List<MenuItem>(4)
        {
            new MenuItem(){ title = "Bảng xếp hạng", playlist_url = "submenu", submenu = new List<MenuItem>(){
                new("Mới nhất", url("")),
                new("Phổ biến", url("most-view.html", "views")),
            }}
        };
        if (cats != null && cats.Count > 0)
        {
            var gm = new List<MenuItem>(cats.Count);
            foreach (var (slug, name) in cats)
                gm.Add(new MenuItem(string.IsNullOrEmpty(name) ? slug : name, url(slug)));
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = gm });
        }
        if (tags != null && tags.Count > 0)
        {
            var gm = new List<MenuItem>(tags.Count);
            foreach (var (keyword, name) in tags)
                gm.Add(new MenuItem(name, url("tag/" + keyword)));
            root.Add(new MenuItem() { title = "Thẻ", playlist_url = "submenu", submenu = gm });
        }
        return root;
    }
}
