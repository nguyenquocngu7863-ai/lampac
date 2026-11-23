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
            // Form site: action=/en/search.html, input name="keyword".
            // `?q=` bi site BO QUA (tra ve nguyen trang chu) -> phai la keyword=.
            string q = System.Uri.EscapeDataString(search.Trim());
            return Paged(host + "/en/search.html?keyword=" + q, pg);
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
                return Paged(host + "/en/search.html?keyword=" + HttpUtility.UrlEncode(keyword), pg);
            }

            // Category: /en/categories/<id>.<slug>.html
            if (raw.StartsWith("categories/", StringComparison.OrdinalIgnoreCase))
            {
                string catUrl = host + "/en/" + raw;
                if (!catUrl.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) catUrl += ".html";
                return Paged(catUrl, pg);
            }

            string baseUrl = host + "/" + raw.TrimStart('/');
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            return Paged(baseUrl, pg);
        }

        // Home: site KHONG co bang "moi nhat" phan trang (trang chu la trang tong hop
        // khong co pagination) -> dung 3 bang xep hang phan trang cua site.
        return Paged(RankingPath(host, sort), pg);
    }

    // Site khong he co tham so `?sort=` (da quet home/detail/category/most-view: 0 ket qua).
    // Sap xep duoc chon bang CHINH DUONG DAN. Them ?sort= vao la chet.
    // Do 2026-11-23: `/en/video.html` -> 404, nen khong co bang "moi nhat" phan trang.
    public static string RankingPath(string host, string sort)
    {
        switch (NormalizeSort(sort))
        {
            case "top-rated": return host + "/en/top-rate.html";
            default: return host + "/en/most-view.html";
        }
    }

    static string Paged(string url, int pg)
    {
        if (pg <= 1) return url;
        return url + (url.Contains('?') ? "&" : "?") + "page=" + pg;
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

    // ========== POPUP (long-press) ==========
    // Trang chi tiet: khong co nhan "dien vien" rieng. Trong tab info (giua
    // "Channel:" va "Added on:") co 3 nhom:
    //   Channel:   /en/channel/<id>/<slug>
    //   Tags:      /en/search.html?keyword=<kw>
    //   Categories:/en/categories/<id>.<slug>.html
    // Chi lay trong doan nay: nav mega-menu cung tro /en/categories/ va /en/pornstars/
    // nhung do la toan bo danh muc, khong phai cua phim.
    public static List<(string kind, string path, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrWhiteSpace(html)) return list;

        int from = html.IndexOf("Channel:", StringComparison.OrdinalIgnoreCase);
        int to = html.IndexOf("Added on:", StringComparison.OrdinalIgnoreCase);
        if (from < 0 || to <= from) return list;

        string seg = html.Substring(from, to - from);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(seg,
            @"<a\b[^>]*\bhref\s*=\s*""([^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string kind, path, name;

            if (m.Groups[1].Value.Contains("/en/categories/", StringComparison.OrdinalIgnoreCase))
            {
                path = PathOf(m.Groups[1].Value);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("/en/categories/", StringComparison.OrdinalIgnoreCase))
                    continue;
                kind = "cat";
            }
            else if (m.Groups[1].Value.Contains("/en/channel/", StringComparison.OrdinalIgnoreCase))
            {
                path = PathOf(m.Groups[1].Value);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("/en/channel/", StringComparison.OrdinalIgnoreCase))
                    continue;
                kind = "channel";
            }
            else if (m.Groups[1].Value.Contains("keyword=", StringComparison.OrdinalIgnoreCase))
            {
                // Tag: giu nguyen keyword (khong dung path) vi route la ?c=tag/<keyword>
                string kw = System.Web.HttpUtility.UrlDecode(
                    (Regex.Match(m.Groups[1].Value, @"keyword=([^&]+)", RegexOptions.IgnoreCase).Groups[1].Value)).Trim();
                if (string.IsNullOrEmpty(kw)) continue;
                path = "tag/" + kw;
                kind = "tag";
            }
            else continue;

            if (!seen.Add(kind + "/" + path)) continue;

            name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " "));
            if (string.IsNullOrEmpty(name)) continue;

            list.Add((kind, path, name));
        }

        return list;
    }

    // `https://kawai-jav.com/en/categories/17.sod-stars.html` -> `/en/categories/17.sod-stars.html`
    public static string PathOf(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;

        string p = href.Trim();
        int scheme = p.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            int slash = p.IndexOf('/', scheme + 3);
            if (slash < 0) return null;
            p = p.Substring(slash);
        }

        if (!p.StartsWith("/")) return null;

        int cut = p.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) p = p.Substring(0, cut);

        p = p.TrimEnd('/');
        return p.Length > 1 ? p : null;
    }

    // URI do client gui vao: chi nhan host site + path hop le (chong SSRF).
    public static string SafeDetailUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        string url = uri.Trim();
        if (url.StartsWith("//")) url = "https:" + url;
        else if (url.StartsWith("/")) url = SiteHost + url;
        else if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = SiteHost + "/" + url.Trim('/');

        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != System.Uri.UriSchemeHttp && u.Scheme != System.Uri.UriSchemeHttps) return null;
        if (!u.Host.EndsWith("kawai-jav.com", StringComparison.OrdinalIgnoreCase)) return null;
        if (!u.AbsolutePath.StartsWith("/en/watch/", StringComparison.OrdinalIgnoreCase)) return null;

        return u.GetLeftPart(global::System.UriPartial.Path);
    }

    // "Related Videos" nam sau <h2> do; truoc no co 2 link /en/watch/ (share/self)
    // -> phai cat tu heading, khong thi list se lan ca chinh phim dang xem.
    public static List<PlaylistItem> Related(string route, string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return new List<PlaylistItem>();

        int i = html.IndexOf("Related Videos", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return new List<PlaylistItem>();

        return Playlist(route, html.Substring(i));
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
    // Truoc day co TaxSorts + IsTaxonomy (sort cho category/tag). Do 2026-11-23:
    // site khong ho tro sort tren category/tag (khong tham so, khong path khac) ->
    // da bo, chi con sort cho danh sach goc.

    // 3 bang xep hang phan trang cua site. Sort = null nghia la "Pho bien".
    // `premium-hd.html` la trang nang cap (0 item, doi dang nhap) -> khong phai danh sach.
    public static readonly (string name, string sort)[] HomeSorts =
    {
        ("Phổ biến", "views"),
        ("Đánh giá cao", "top-rated"),
    };

    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;
        // Category/tag: site khong co sort (khong tham so, khong path khac) -> khong hien dong sort.
        if (!string.IsNullOrWhiteSpace(c)) return null;
        return HomeSorts;
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
        switch (sort.Trim().ToLowerInvariant())
        {
            case "views": case "most-view": case "popular": return "views";
            case "top-rated": case "top-rate": case "rating": return "top-rated";
            default: return null;
        }
    }

    public static string SortLabel(string sort)
    {
        switch (NormalizeSort(sort))
        {
            case "top-rated": return "đánh giá cao";
            default: return "phổ biến";
        }
    }

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
        {
            // Home: khong co `?c=`, sort nam o &sort=. Taxonomy: giu ?c=.
            string u = string.IsNullOrWhiteSpace(c)
                ? root + "?sort=" + s
                : root + "?c=" + HttpUtility.UrlEncode(c) + "&sort=" + s;
            sub.Add(new MenuItem(name, u));
        }
        res.Add(new MenuItem(){ title = $"Sắp xếp: {SortLabel(sort)}", playlist_url = "submenu", submenu = sub });
        return res;
    }

    // ===== base của menu =====
    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats, List<(string slug, string name)> tags)
    {
        host = host.TrimEnd('/');
        string url(string c, string sort = null) => host + "/kawaijav?c=" + HttpUtility.UrlEncode(c)
            + (string.IsNullOrEmpty(sort) ? "" : "&sort=" + sort);
        // "Bang xep hang" = chon bang xep hang cua site (sort -> path, khong phai ?sort=).
        var rank = new List<MenuItem>(HomeSorts.Length);
        foreach (var (name, s) in HomeSorts)
            rank.Add(new MenuItem(name, host + "/kawaijav?sort=" + s));
        var root = new List<MenuItem>(4)
        {
            new MenuItem(){ title = "Bảng xếp hạng", playlist_url = "submenu", submenu = rank }
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
