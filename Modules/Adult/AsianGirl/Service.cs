using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace AsianGirl;

public static class AsianGirlTo
{
    public static string SiteHost = "https://asiangirl.porn";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // 24 card/trang — search phan trang bang offset `from_videos` (0, 24, 48...).
    public const int PageSize = 24;

    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string url = host + "/search/?q=" + HttpUtility.UrlEncode(search.Trim());
            if (pg > 1)
                url += "&from_videos=" + ((pg - 1) * PageSize);
            return url;
        }

        // home = /new/ (trang chu goc tron nhieu section -> trung item)
        string path = string.IsNullOrWhiteSpace(c) ? "/new/" : "/" + c.Trim('/') + "/";

        string q = "";
        if (pg > 1)
            q = "from=" + pg;
        if (!string.IsNullOrWhiteSpace(sort))
            q += (q.Length > 0 ? "&" : "") + "sort_by=" + sort;

        return host + path + (q.Length > 0 ? "?" + q : "");
    }

    // ===== danh sach =====
    // Card: <div class="item card-video col-1"> ... <a href="https://asiangirl.porn/v/<id>/">
    // The QC chen giua dung cung class nhung href ra host khac -> loc bang host.
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(
        string route, string host, string html, string skipHref = null)
    {
        var list = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        string site = string.IsNullOrEmpty(host) ? SiteHost : host.TrimEnd('/');
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in html.Split(new[] { "class=\"item card-video" }, StringSplitOptions.None))
        {
            string b = raw.Length > 3000 ? raw.Substring(0, 3000) : raw;

            var hm = Regex.Match(b, "href=\"((?:https?://[^\"/]+)?/v/[^\"?#]+)\"");
            if (!hm.Success)
                continue;

            string href = hm.Groups[1].Value;
            if (href.StartsWith("/"))
                href = site + href;

            if (!href.StartsWith(site + "/v/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (skipHref != null && href.TrimEnd('/') == skipHref.TrimEnd('/'))
                continue;

            if (!seen.Add(href))
                continue;

            var tm = Regex.Match(b, "class=\"card-video__title\">(.*?)</a>", RegexOptions.Singleline);
            if (!tm.Success)
                continue;

            string name = HttpUtility.HtmlDecode(Regex.Replace(tm.Groups[1].Value, "<[^>]+>", " "));
            name = Regex.Replace(name, @"\s+", " ").Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, "data-src=\"([^\"]+)\"");
            if (im.Success)
                poster = HttpUtility.HtmlDecode(im.Groups[1].Value);

            string dur = "";
            var dm = Regex.Match(b, "class=\"card-video__duration\">([^<]+)<");
            if (dm.Success)
                dur = dm.Groups[1].Value.Trim();

            string preview = "";
            var pm = Regex.Match(b, "data-preview=\"([^\"]+)\"");
            if (pm.Success)
                preview = HttpUtility.HtmlDecode(pm.Groups[1].Value);

            var item = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = dur,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "asiangirl",
                    href = href,
                    image = poster
                }
            };

            if (!string.IsNullOrEmpty(preview))
                item.preview = preview;

            list.Add(item);
        }

        return list;
    }

    // Trang detail: var stream = 'https://hokcap.ppp.porn/stream/<token>/<exp>/.../<id>.m3u8';
    // Token song ~2 gio -> cache link 5 phut.
    public static string StreamUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"var\s+stream\s*=\s*['""]([^'""]+)['""]");
        if (!m.Success)
            return null;

        string url = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
        return url.StartsWith("http") ? url : null;
    }

    // ===== popup (long-press) =====
    // Detail chi co The loai (khong co dien vien / tag):
    // <div class="carousel carousel--text content-details__meta">
    //   <a href="https://asiangirl.porn/categories/<slug>/">Ten</a>
    public static List<(string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        int i = html.IndexOf("content-details__meta", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return list;

        string block = html.Substring(i, Math.Min(6000, html.Length - i));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(block,
            "<a[^>]+href=\"(?:https?://[^\"/]+)?/categories/([^\"/]+)/\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (slug.Length == 0 || name.Length == 0)
                continue;
            if (!seen.Add(slug))
                continue;
            list.Add((slug, name));
        }

        return list;
    }

    // Danh muc cho menu + popup: <a class="card-cat-v2__link" href="/categories/<slug>/">
    public static List<(string slug, string name)> Categories(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a class=\"card-cat-v2__link\" href=\"(?:https?://[^\"/]+)?/categories/([^\"/]+)/\">(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim();
            if (slug.Length == 0 || !seen.Add(slug))
                continue;

            var tm = Regex.Match(m.Groups[2].Value, "card-cat-v2__title\">([^<]+)<");
            string name = tm.Success
                ? HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim())
                : slug;

            list.Add((slug, name));
        }

        return list;
    }

    // ===== sort — chi 2 context co widget, tap gia tri KHAC nhau =====
    // /hot/    : video_viewed (mac dinh), today, week, month
    // /categories/<slug>/ : post_date (mac dinh), most_favourited,
    //                       video_viewed_month, video_viewed
    // /new/ va /search/ khong co sort.
    public static readonly (string name, string sort)[] SortsHot =
    {
        ("Mặc định",            ""),
        ("Xem nhiều hôm nay",   "video_viewed_today"),
        ("Xem nhiều tuần này",  "video_viewed_week"),
        ("Xem nhiều tháng này", "video_viewed_month"),
        ("Xem nhiều nhất",      "video_viewed"),
    };

    public static readonly (string name, string sort)[] SortsCat =
    {
        ("Mặc định",            ""),
        ("Yêu thích nhất",      "most_favourited"),
        ("Xem nhiều tháng này", "video_viewed_month"),
        ("Xem nhiều nhất",      "video_viewed"),
    };

    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return null;

        if (string.IsNullOrWhiteSpace(c))
            return null;

        string cc = c.Trim('/');
        if (cc == "hot") return SortsHot;
        if (cc.StartsWith("categories/")) return SortsCat;
        return null;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return null;

        var opts = SortsFor(search, c);
        if (opts == null)
            return null;

        foreach (var o in opts)
            if (o.sort == sort.Trim())
                return o.sort;

        return null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mặc định"
        : sort == "video_viewed_today" ? "xem nhiều hôm nay"
        : sort == "video_viewed_week" ? "xem nhiều tuần này"
        : sort == "video_viewed_month" ? "xem nhiều tháng này"
        : sort == "video_viewed" ? "xem nhiều nhất"
        : sort == "most_favourited" ? "yêu thích nhất" : sort;

    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/asiangirl";

        string link(string s)
        {
            string q;
            if (!string.IsNullOrWhiteSpace(search)) q = "search=" + HttpUtility.UrlEncode(search);
            else if (!string.IsNullOrWhiteSpace(c)) q = "c=" + HttpUtility.UrlEncode(c);
            else q = "";
            if (!string.IsNullOrEmpty(s))
                q += (q.Length == 0 ? "" : "&") + "sort=" + s;
            return q.Length == 0 ? root : root + "?" + q;
        }

        var res = new List<Shared.Models.SISI.Base.MenuItem>(2)
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm", search_on = "search_on", playlist_url = root
            }
        };

        var opts = SortsFor(search, c);
        if (opts == null)
            return res;

        var sub = new List<Shared.Models.SISI.Base.MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
            sub.Add(new Shared.Models.SISI.Base.MenuItem(name, link(s)));

        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host, List<(string slug, string name)> cats)
    {
        host = host.TrimEnd('/');

        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh sách",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/asiangirl?c=new"),
                    new("Xem nhiều", host + "/asiangirl?c=hot"),
                }
            }
        };

        if (cats != null && cats.Count > 0)
        {
            var sub = new List<Shared.Models.SISI.Base.MenuItem>(cats.Count);
            foreach (var (slug, name) in cats)
                sub.Add(new Shared.Models.SISI.Base.MenuItem(name, host + "/asiangirl?c=" + HttpUtility.UrlEncode("categories/" + slug)));

            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = sub
            });
        }

        return menu;
    }
}
