using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace KissJav;

/// <summary>
/// kissjav.li — theme rieng (khong WordPress), HTML server-side. Do 2026-11-24.
///   list    : /latest-updates/[page]/        /most-popular/[page]/?sort_by=video_viewed
///   the loai: /categories/&lt;slug&gt;/[page]/
///   tim kiem: /search/?q=&lt;kw&gt;
///   sort    : ?sort_by=video_viewed|rating|duration|most_commented|most_favourited
///   detail  : /video/&lt;id&gt;/&lt;slug&gt;/
///   player  : flashvars.video_url = base64("https://kissjav.li/get_file/11/.../&lt;id&gt;.mp4/?br=1014")
///   card    : &lt;div class="thumb thumb_rel item"&gt;&lt;a href="/video/&lt;id&gt;/&lt;slug&gt;/" title="TEN"&gt;
///               &lt;img class="lazy-load" data-original="//assets.../1.jpg"&gt;
///               &lt;div class="time"&gt;0:23&lt;/div&gt;
/// </summary>
public static class KissJavTo
{
    public const string SiteHost = "https://kissjav.li";

    public const string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    static string Base(string host)
        => (string.IsNullOrWhiteSpace(host) ? SiteHost : host).TrimEnd('/');

    static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        s = HttpUtility.HtmlDecode(s).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    static string Match1(string s, string pattern)
    {
        var m = Regex.Match(s, pattern, RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    #region URL
    // c = "latest-updates" | "most-popular" | "categories/korean-porn" | ...
    public static string ListUrl(string host, string c, string sort, int pg)
    {
        if (pg < 1) pg = 1;
        c = (c ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(c)) c = "latest-updates";

        string u = Base(host) + "/" + c + "/";
        if (pg > 1) u += pg + "/";

        if (!string.IsNullOrEmpty(sort))
            u += "?sort_by=" + HttpUtility.UrlEncode(sort);

        return u;
    }

    public static string SearchUrl(string host, string kw, int pg)
    {
        string u = Base(host) + "/search/?q=" + HttpUtility.UrlEncode((kw ?? "").Trim());
        if (pg > 1) u += "&page=" + pg;
        return u;
    }

    public static string MenuUrl(string host, string c, string sort = null)
        => $"{Base(host)}/kissjav?c={HttpUtility.UrlEncode(c)}" +
           (string.IsNullOrEmpty(sort) ? "" : "&sort=" + HttpUtility.UrlEncode(sort));
    #endregion

    #region playlist
    // Moi card bat dau bang `thumb thumb_rel item` (list + related deu dung markup nay).
    static List<PlaylistItem> Cards(string route, string html)
    {
        var res = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string part in Regex.Split(html, @"class=""thumb thumb_rel item"))
        {
            var hm = Regex.Match(part, @"href=""(https?://[^""/]+/video/\d+/[^""]*?)""");
            if (!hm.Success) continue;

            string href = hm.Groups[1].Value;
            if (!seen.Add(href)) continue;

            string name = Clean(Match1(part, @"\stitle=""([^""]+)"""));
            if (string.IsNullOrEmpty(name))
                name = Clean(Match1(part, @"<img[^>]*\salt=""([^""]*)"""));
            if (string.IsNullOrEmpty(name))
                name = Regex.Replace(href.TrimEnd('/').Split('/')[^1], "-", " ");

            string pic = Match1(part, @"<img[^>]*\sdata-original=""([^""]+)""");
            if (string.IsNullOrEmpty(pic))
                pic = Match1(part, @"<img[^>]*\sdata-webp=""([^""]+)""");

            string time = Clean(Match1(part, @"<div class=""time"">\s*([^<]+)</div>"));

            res.Add(new PlaylistItem()
            {
                name = name,
                video = $"{route}?uri={HttpUtility.UrlEncode(href)}",
                picture = Abs(pic),
                time = time,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "kissjav",
                    href = href,
                    image = Abs(pic)
                }
            });
        }

        return res;
    }

    public static List<PlaylistItem> Playlist(string route, string html)
        => Cards(route, html);
    #endregion

    #region menu
    public static List<(string path, string name)> Nav(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(html,
            @"href=""https?://[^""/]+/((?:latest-updates|most-popular|categories/[a-z0-9\-]+))/?""[^>]*>\s*([^<]{1,40})\s*<"))
        {
            string path = m.Groups[1].Value;
            string name = Clean(m.Groups[2].Value);
            if (string.IsNullOrEmpty(name)) continue;
            if (!seen.Add(path)) continue;
            res.Add((path, name));
        }
        return res;
    }

    public static List<(string path, string name)> FallbackNav = new List<(string, string)>()
    {
        ("latest-updates", "Mới nhất"),
        ("most-popular", "Xem nhiều"),
        ("categories/korean-porn", "Korean Porn"),
        ("categories/korean-bj", "Korean BJ"),
        ("categories/jav", "Uncensored"),
        ("categories/fc2ppv", "FC2PPV"),
        ("categories/myfans", "MyFans"),
        ("categories/vip", "KVIP"),
        ("categories/jvip", "JVIP"),
        ("categories/voyeur-jp", "Voyeur")
    };

    static readonly (string sort, string label)[] Sorts =
    {
        ("", "Mới nhất"),
        ("video_viewed", "Xem nhiều"),
        ("rating", "Đánh giá cao"),
        ("duration", "Dài nhất"),
        ("most_commented", "Nhiều bình luận"),
        ("most_favourited", "Nhiều yêu thích")
    };

    public static List<MenuItem> Menu(string host, List<(string path, string name)> nav, string c, string sort)
    {
        if (nav == null || nav.Count == 0) nav = FallbackNav;

        string cur = (c ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(cur)) cur = "latest-updates";

        // Muc dang mo: ten that cua no + cac muc con la danh muc
        var sections = nav
            .Where(x => !x.path.StartsWith("most-popular", StringComparison.Ordinal))
            .Select(x => new MenuItem(x.name, MenuUrl(host, x.path)))
            .ToList();

        // Sort chi co nghia voi trang danh sach (khong phai search)
        var sortMenu = new List<MenuItem>();
        foreach (var (s, label) in Sorts)
            sortMenu.Add(new MenuItem(label, MenuUrl(host, cur, s)));

        return new List<MenuItem>()
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = $"{host}/kissjav" },
            new MenuItem() { title = "Danh mục", playlist_url = "submenu", submenu = sections },
            new MenuItem() { title = "Sắp xếp", playlist_url = "submenu", submenu = sortMenu }
        };
    }
    #endregion

    #region detail / stream
    // flashvars.video_url = base64(url). video_url_hd = "MQ==" (= "1") la sentinel, bo qua.
    public static string PlayUrl(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        string hd = Match1(html, @"video_url_hd:\s*'([^']*)'");
        string main = Match1(html, @"video_url:\s*'([^']*)'");

        foreach (string enc in new[] { hd, main })
        {
            string url = Decode(enc);
            if (url != null) return url;
        }

        return null;
    }

    static string Decode(string b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return null;

        try
        {
            string url = Encoding.UTF8.GetString(Convert.FromBase64String(b64.Trim())).Trim();
            return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : null;
        }
        catch { return null; }
    }

    // The loai RIENG cua phim: <a class="btn gold button" href=".../categories/<slug>/">TEN</a>
    // (nav header cung tro /categories/... nhung KHONG co class nay).
    public static List<(string path, string name)> Chips(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(html,
            @"class=""btn gold button""\s+href=""https?://[^""/]+/(categories/[a-z0-9\-]+)/""[^>]*>\s*([^<]+?)\s*</a>"))
        {
            string path = m.Groups[1].Value;
            string name = Clean(m.Groups[2].Value);
            if (string.IsNullOrEmpty(name)) continue;
            if (!seen.Add(path)) continue;
            res.Add((path, name));
        }
        return res;
    }

    public static string RelatedBlock(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        int i = html.IndexOf("list_videos_related_videos_items", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? null : html.Substring(i);
    }

    public static List<PlaylistItem> RelatedPlaylist(string route, string html)
        => string.IsNullOrEmpty(html) ? new List<PlaylistItem>() : Cards(route, html);

    public static string DetailPath(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        uri = uri.Trim();

        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return null;
            if (!u.Host.EndsWith("kissjav.li", StringComparison.OrdinalIgnoreCase)) return null;
            uri = u.AbsolutePath;
        }

        if (!uri.StartsWith('/')) uri = "/" + uri;
        if (!Regex.IsMatch(uri, @"^/video/\d+/[^/]+/?$", RegexOptions.IgnoreCase))
            return null;

        return uri;
    }
    #endregion

    #region helpers
    public static string Abs(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        url = url.Trim();
        if (url.StartsWith("//")) return "https:" + url;
        if (url.StartsWith("/")) return SiteHost + url;
        return url;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html)) return 0;

        int max = 0;
        foreach (Match m in Regex.Matches(html, @"/(?:latest-updates|most-popular|categories/[a-z0-9\-]+)/(\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;

        return max;
    }

    public static bool IsBlocked(string html)
    {
        if (string.IsNullOrEmpty(html) || html.Length < 1000) return true;
        if (html.Contains("Just a moment") || html.Contains("Enable JavaScript and cookies")) return true;
        return false;
    }
    #endregion
}
