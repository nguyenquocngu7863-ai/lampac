using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace JavMit;

public static class JavMitTo
{
    public static string SiteHost = "https://javmit.com";
    public const string BasePath = "/en";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // Trang chu:        /en/            -> /en/page/N/
    // Danh sach:        /en/video/      -> /en/video/page/N/?sort=...
    // The loai:         /en/category/x/ -> /en/category/x/page/N/
    // The:              /en/tag/x/      -> /en/tag/x/page/N/
    // Dien vien:        /en/actor/x/    -> /en/actor/x/page/N/
    // Tim kiem:         /en/?s=q&rawx_scope=all -> /en/page/N/?s=q&rawx_scope=all
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = HttpUtility.UrlEncode(search.Trim());
            return (pg > 1 ? host + BasePath + "/page/" + pg + "/" : host + BasePath + "/")
                + "?s=" + q + "&rawx_scope=all";
        }

        // /en/page/N/ bi bo qua (tra ve y trang 1) -> dung /en/video/ cho ca
        // trang chu lan danh sach, phan trang that.
        if (string.IsNullOrWhiteSpace(c) || c.Trim('/') == "video")
        {
            string url = host + BasePath + "/video/";
            if (pg > 1)
                url += "page/" + pg + "/";
            if (!string.IsNullOrEmpty(sort))
                url += "?sort=" + sort;
            return url;
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string path = c.Trim('/');
            string url = host + BasePath + "/" + path + "/";
            if (pg > 1)
                url += "page/" + pg + "/";
            return url;
        }

        return host + BasePath + "/video/";
    }

    // Card trang chu: <article class="list-video"> ... <a class="lv-thumb" href=...>
    // Card listing : <article class="v-card">    ... <a class="vc-thumb-wrap" href=...>
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string route, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<article class=\"(?:list-video|v-card)\"[^>]*>(.*?)</article>",
            RegexOptions.Singleline))
        {
            string b = m.Groups[1].Value;

            var hm = Regex.Match(b,
                "<a\\s+href=\"(https://javmit\\.com/en/[^\"]+)\"\\s+class=\"(?:lv-thumb|vc-thumb-wrap)\"");
            if (!hm.Success)
                continue;

            string href = hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            var tm = Regex.Match(b, "class=\"(?:lv-title|vc-title)\">([^<]+)<");
            if (!tm.Success)
                continue;

            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b,
                "<img[^>]+src=\"([^\"]+)\"[^>]*class=\"(?:lv-thumb-img|vc-thumb-img)\"");
            if (!im.Success)
                im = Regex.Match(b,
                    "<img[^>]+class=\"(?:lv-thumb-img|vc-thumb-img)\"[^>]*src=\"([^\"]+)\"");
            if (im.Success)
                poster = HttpUtility.HtmlDecode(im.Groups[1].Value);

            string dur = null;
            var dm = Regex.Match(b, "class=\"(?:lv-dur|vc-dur)\">([^<]+)<");
            if (dm.Success)
                dur = HttpUtility.HtmlDecode(dm.Groups[1].Value.Trim());

            string quality = null;
            var qm = Regex.Match(b, "rawx-unc-badge[^>]*>([^<]+)<");
            if (qm.Success)
            {
                string badge = HttpUtility.HtmlDecode(qm.Groups[1].Value.Trim());
                if (!string.IsNullOrEmpty(badge))
                    quality = badge == "無修正" ? "UNCENSORED" : badge;
            }

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = dur,
                quality = quality,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark
                {
                    site = "javmit",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // Trang detail: <button class="rawx-quality-btn" data-src="..." data-label="AUTO|1080P|480P">
    public static List<(string label, string url)> StreamLinks(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html, "<button[^>]*rawx-quality-btn[^>]*>", RegexOptions.Singleline))
        {
            string tag = m.Value;

            var sm = Regex.Match(tag, "data-src=\"([^\"]+)\"");
            if (!sm.Success)
                continue;

            string url = HttpUtility.HtmlDecode(sm.Groups[1].Value);
            if (!url.StartsWith("http"))
                continue;

            var lm = Regex.Match(tag, "data-label=\"([^\"]+)\"");
            string label = lm.Success ? lm.Groups[1].Value.Trim() : "HLS";
            if (string.IsNullOrEmpty(label))
                label = "HLS";

            string key = label;
            int n = 2;
            while (!seen.Add(key))
                key = label + " " + (n++);

            list.Add((key, url));
        }

        if (list.Count == 0)
        {
            var om = Regex.Match(html, "<meta[^>]+property=\"og:video\"[^>]+content=\"([^\"]+)\"");
            if (om.Success)
            {
                string url = HttpUtility.HtmlDecode(om.Groups[1].Value);
                if (url.Contains(".m3u8") || url.Contains(".mp4"))
                    list.Add(("AUTO", url));
            }
        }

        return list;
    }

    public static string Poster(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";

        var m = Regex.Match(html,
            "<img[^>]+src=\"(https://javmit\\.com/wp-content/uploads/[^\"]+cover-javmit\\.jpg)\"");
        if (!m.Success)
            m = Regex.Match(html, "<meta[^>]+property=\"og:image\"[^>]+content=\"([^\"]+)\"");
        if (!m.Success)
            return "";

        return HttpUtility.HtmlDecode(m.Groups[1].Value);
    }

    // Chip the loai / the / dien vien tren trang detail: <a class="v-tag ... rawx-topic-chip" href="...">
    public static List<(string kind, string path, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a[^>]*class=\"[^\"]*v-tag[^\"]*\"[^>]*>[^<]*</a>",
            RegexOptions.Singleline))
        {
            string tag = m.Value;

            var hm = Regex.Match(tag, "href=\"https://javmit\\.com/en/([^\"]+)\"");
            if (!hm.Success)
                continue;

            string path = hm.Groups[1].Value.Trim('/');
            string name = HttpUtility.HtmlDecode(Regex.Replace(tag, "<[^>]+>", " ")).Trim();
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name))
                continue;

            string kind;
            if (path.StartsWith("tag/", StringComparison.OrdinalIgnoreCase))
                kind = "tag";
            else if (path.StartsWith("actor/", StringComparison.OrdinalIgnoreCase))
                kind = "actor";
            else if (path.StartsWith("category/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("uncensored", StringComparison.OrdinalIgnoreCase))
                kind = "category";
            else
                continue;

            if (!seen.Add(kind + "/" + path))
                continue;

            list.Add((kind, path, name));
        }

        return list;
    }

    // Khoi "RELATED VIDEOS": <div class="related-item"> ... <a class="ri-thumb" href=...>
    public static List<(string href, string title, string poster, string dur)> Related(string html)
    {
        var list = new List<(string, string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<div class=\"related-item\">(.*?)</div>\\s*</div>",
            RegexOptions.Singleline))
        {
            string b = m.Groups[1].Value;

            var hm = Regex.Match(b, "<a href=\"(https://javmit\\.com/en/[^\"]+)\" class=\"ri-thumb\"");
            if (!hm.Success)
                continue;

            string href = hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            string title = "";
            var tm = Regex.Match(b, "class=\"ri-title\">([^<]+)<");
            if (tm.Success)
                title = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(title))
            {
                var am = Regex.Match(b, "<img[^>]+alt=\"([^\"]+)\"");
                if (am.Success)
                    title = HttpUtility.HtmlDecode(am.Groups[1].Value.Trim());
            }
            if (string.IsNullOrEmpty(title))
                continue;

            string poster = "";
            var im = Regex.Match(b, "<img[^>]+src=\"([^\"]+)\"[^>]*class=\"ri-thumb-img\"");
            if (!im.Success)
                im = Regex.Match(b, "<img[^>]+class=\"ri-thumb-img\"[^>]*src=\"([^\"]+)\"");
            if (im.Success)
                poster = HttpUtility.HtmlDecode(im.Groups[1].Value);

            string dur = null;
            var dm = Regex.Match(b, "class=\"ri-dur\">([^<]+)<");
            if (dm.Success)
                dur = HttpUtility.HtmlDecode(dm.Groups[1].Value.Trim());

            list.Add((href, title, poster, dur));
        }

        return list;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html, "/page/(\\d+)/"))
        {
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;
        }

        return max;
    }

    // /en/categories/: <a href=".../en/<path>/" class="cc-header"><div class="cc-name">NAME</div>
    // <path> co the la "category/x" hoac "uncensored", "uncensored/fc2"...
    public static List<(string name, string slug)> Categories(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a href=\"https://javmit\\.com/en/([^\"]+)\" class=\"cc-header\">(.*?)</a>",
            RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim('/');
            if (string.IsNullOrEmpty(slug))
                continue;

            var nm = Regex.Match(m.Groups[2].Value, "class=\"cc-name\">([^<]+)<");
            string name = nm.Success ? HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim()) : slug;
            if (string.IsNullOrEmpty(name))
                name = slug;
            if (seen.Add(slug))
                list.Add((name, slug));
        }

        return list;
    }

    // /en/tags/: <a href=".../tag/slug/" class="tc-tag sz5"> NAME </a>
    public static List<(string name, string slug, int weight)> Tags(string html)
    {
        var list = new List<(string, string, int)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a href=\"https://javmit\\.com/en/tag/([^\"/]+)/\" class=\"tc-tag sz(\\d)\">([^<]+)</a>",
            RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value;
            if (!int.TryParse(m.Groups[2].Value, out int weight))
                weight = 1;

            string name = HttpUtility.HtmlDecode(m.Groups[3].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            if (seen.Add(slug))
                list.Add((name, slug, weight));
        }

        return list;
    }

    // /en/actors/: <a href=".../actor/slug/" class="costar-card actors-hub-card"> ... NAME ... N VIDEOS
    public static List<(string name, string slug, int count)> Actors(string html)
    {
        var list = new List<(string, string, int)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a href=\"https://javmit\\.com/en/actor/([^\"/]+)/\" class=\"costar-card actors-hub-card\">(.*?)</a>",
            RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value;
            string b = m.Groups[2].Value;

            var nm = Regex.Match(b, "class=\"costar-name actors-hub-name\">([^<]+)<");
            string name = nm.Success ? HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim()) : slug;
            if (string.IsNullOrEmpty(name))
                name = slug;

            int count = 0;
            var cm = Regex.Match(b, "class=\"costar-count actors-hub-count\">([\\d,]+)");
            if (cm.Success)
                int.TryParse(cm.Groups[1].Value.Replace(",", ""), out count);

            if (seen.Add(slug))
                list.Add((name, slug, count));
        }

        return list;
    }

    // ===== sort — CHI co o /en/video/ (da do: category/tag/actor bo qua ?sort=) =====
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất", "date"),
        ("Xem nhiều", "views"),
        ("A-Z", "title"),
    };

    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return null;
        // Trang chu va c=video deu la /en/video/ -> sort duoc.
        if (string.IsNullOrWhiteSpace(c))
            return Sorts;
        if (c.Trim('/') != "video")
            return null;
        return Sorts;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null || string.IsNullOrWhiteSpace(sort))
            return null;

        sort = sort.Trim().ToLowerInvariant();
        foreach (var o in opts)
            if (o.sort == sort)
                return sort;
        return null;
    }

    public static string SortLabel(string sort) =>
        sort == "views" ? "xem nhiều"
        : sort == "title" ? "A-Z"
        : "mới nhất";

    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/javmit";

        string link(string s)
        {
            string q;
            if (!string.IsNullOrWhiteSpace(search))
                q = "search=" + HttpUtility.UrlEncode(search);
            else if (!string.IsNullOrWhiteSpace(c))
                q = "c=" + HttpUtility.UrlEncode(c);
            else
                q = "";
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
        if (opts == null || opts.Length == 0)
            return res;

        var sub = new List<Shared.Models.SISI.Base.MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
            sub.Add(new Shared.Models.SISI.Base.MenuItem(name, link(s)));

        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = "Sắp xếp: " + SortLabel(sort),
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host,
        List<(string name, string slug)> categories = null,
        List<(string name, string slug, int weight)> tags = null,
        List<(string name, string slug, int count)> actors = null)
    {
        host = host.TrimEnd('/');
        string root = host + "/javmit";

        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh sách",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới cập nhật", root),
                    new("Không che", root + "?c=uncensored"),
                }
            }
        };

        if (categories != null && categories.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = categories.Select(c =>
                    new Shared.Models.SISI.Base.MenuItem(
                        c.name, root + "?c=" + HttpUtility.UrlEncode(c.slug))).ToList()
            });
        }

        if (tags != null && tags.Count > 0)
            menu.AddRange(Buckets(root, "Thẻ", tags.Select(t => (t.name, "tag/" + t.slug, t.weight)).ToList()));

        if (actors != null && actors.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Diễn viên",
                playlist_url = "submenu",
                submenu = actors.Select(a =>
                    new Shared.Models.SISI.Base.MenuItem(
                        a.name, root + "?c=actor/" + HttpUtility.UrlEncode(a.slug))).ToList()
            });
        }

        return menu;
    }

    public const int MaxPerBucket = 300;

    static List<Shared.Models.SISI.Base.MenuItem> Buckets(
        string root, string title, List<(string name, string path, int weight)> all)
    {
        var res = new List<Shared.Models.SISI.Base.MenuItem>();
        if (all == null || all.Count == 0)
            return res;

        var sorted = all
            .OrderByDescending(x => x.weight)
            .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < sorted.Count; i += MaxPerBucket)
        {
            int n = Math.Min(MaxPerBucket, sorted.Count - i);
            var sub = sorted.Skip(i).Take(n).Select(x =>
                new Shared.Models.SISI.Base.MenuItem(
                    x.name, root + "?c=" + HttpUtility.UrlEncode(x.path))).ToList();

            string name = i == 0
                ? title + " phổ biến"
                : title + " " + (i + 1) + "-" + (i + n);

            res.Add(new Shared.Models.SISI.Base.MenuItem(name, "submenu") { submenu = sub });
        }

        return res;
    }
}
