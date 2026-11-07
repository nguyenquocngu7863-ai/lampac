using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace SakuraSins;

public static class SakuraSinsTo
{
    public static string SiteHost = "https://sakurasins.com";

    public static string ChromeUA =
        "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";

    // ===== URL =====
    // Feed (home / category / search) load-more qua API:
    //   GET /api/videos.php?sort=&cat=&q=&page=N  -> JSON {ok, html, next_page, has_more}
    // Studio / Performer / Collection la trang server-side phan trang ?page=N.
    public static bool IsApi(string search, string c)
    {
        return !string.IsNullOrWhiteSpace(search)
            || string.IsNullOrWhiteSpace(c)
            || c.StartsWith("cat/");
    }

    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        string q = "page=" + pg;

        if (!string.IsNullOrWhiteSpace(search))
        {
            q = "q=" + HttpUtility.UrlEncode(search) + "&" + q;
            if (!string.IsNullOrWhiteSpace(sort))
                q += "&sort=" + sort;
            return host + "/api/videos.php?" + q;
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            if (c.StartsWith("cat/"))
            {
                q = "cat=" + HttpUtility.UrlEncode(c.Substring(4)) + "&" + q;
                if (!string.IsNullOrWhiteSpace(sort))
                    q += "&sort=" + sort;
                return host + "/api/videos.php?" + q;
            }
            if (c.StartsWith("collection/"))
                return host + "/collection/" + c.Substring(11);
            if (c.StartsWith("studio/"))
                return host + "/studio/" + c.Substring(7) + (pg > 1 ? "?page=" + pg : "");
            if (c.StartsWith("performer/"))
                return host + "/performer/" + c.Substring(10) + (pg > 1 ? "?page=" + pg : "");
        }

        if (!string.IsNullOrWhiteSpace(sort))
            q += "&sort=" + sort;
        return host + "/api/videos.php?" + q;
    }

    // ===== API feed JSON -> (fragment html, has_more) =====
    public static (string html, bool hasMore) FeedJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return ("", false);

        var hmm = Regex.Match(json, @"""html""\s*:\s*""(.*?)""\s*,\s*""(?:count|next_page|has_more)""", RegexOptions.Singleline);
        string frag = hmm.Success ? Regex.Unescape(hmm.Groups[1].Value) : "";

        var hm = Regex.Match(json, @"""has_more""\s*:\s*(true|false)");
        bool more = hm.Success && hm.Groups[1].Value == "true";

        return (frag, more);
    }

    // ===== card vcard -> PlaylistItem =====
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in html.Split(new[] { "<article class=\"vcard" }, StringSplitOptions.None))
        {
            string b = raw.Length > 4000 ? raw.Substring(0, 4000) : raw;

            var hm = Regex.Match(b, @"<a class=""vcard__thumb""[^>]*href=""(/watch/[^""]+)""");
            if (!hm.Success)
                continue;
            string href = hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            var tm = Regex.Match(b, @"<a class=""vcard__title""[^>]*>([^<]{4,300})</a>");
            if (!tm.Success)
                continue;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, @"<img[^>]*src=""([^""]+)""");
            if (im.Success)
            {
                poster = im.Groups[1].Value;
                if (poster.StartsWith("/"))
                    poster = SiteHost + poster;
            }

            string dur = "";
            var dm = Regex.Match(b, @"<span class=""vcard__dur"">([^<]+)</span>");
            if (dm.Success)
                dur = HttpUtility.HtmlDecode(dm.Groups[1].Value.Trim());

            list.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = dur,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "sakurasins",
                    href = SiteHost + href,
                    image = poster
                }
            });
        }

        return list;
    }

    // ===== video lien quan (section related-videos, cung cau truc vcard) =====
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(string route, string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        int i = html.IndexOf("related-videos", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        string block = html.Substring(i, Math.Min(80000, html.Length - i));
        return Playlist(route, block);
    }

    // ===== trang phan trang server-side: "Page X of N" -> total pages =====
    public static int TotalPages(string html, int pg)
    {
        if (string.IsNullOrEmpty(html))
            return 1;

        var m = Regex.Match(html, @"pager__cur[^>]*>\s*Page\s+\d+\s+of\s+(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int t) && t > 0)
            return t;

        return pg;
    }

    // ===== taxonomy =====
    // /categories: <a class="catcard" href="/?cat=asian-anal" ...> ... <div class="catcard__name">Asian Anal</div>
    public static List<(string name, string slug)> Categories(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            "<a class=\"catcard\" href=\"/\\?cat=([^\"]+)\"[^>]*>(.*?)</a>",
            RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim();
            if (!seen.Add(slug))
                continue;

            var nm = Regex.Match(m.Groups[2].Value, "catcard__name\">([^<]{1,80})</div>");
            string name = nm.Success
                ? HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim())
                : slug;
            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((name, slug));
        }

        return list;
    }

    // /studios, /performers: <a class="pfcard" href="/studio/slug"> ... <div class="pfcard__name">NAME</div>
    static List<(string name, string slug)> Pfcards(string html, string kind, int cap)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            "<a class=\"pfcard\" href=\"/" + kind + "/([^\"]+)\"[^>]*>(.*?)</a>",
            RegexOptions.Singleline))
        {
            if (list.Count >= cap)
                break;

            string slug = m.Groups[1].Value.Trim();
            if (!seen.Add(slug))
                continue;

            var nm = Regex.Match(m.Groups[2].Value, "pfcard__name\">([^<]{1,80})</div>");
            string name = nm.Success
                ? HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim())
                : slug;
            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((name, slug));
        }

        return list;
    }

    public static List<(string name, string slug)> Studios(string html)
        => Pfcards(html, "studio", 300);

    public static List<(string name, string slug)> Performers(string html)
        => Pfcards(html, "performer", 300);

    // /collections: <a class="clcard" href="/collection/slug"> ... <div class="clcard__t">NAME</div>
    public static List<(string name, string slug)> Collections(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            "<a class=\"clcard\" href=\"/collection/([^\"]+)\"[^>]*>(.*?)</a>",
            RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim();
            if (!seen.Add(slug))
                continue;

            var nm = Regex.Match(m.Groups[2].Value, "clcard__t\">([^<]{1,80})</div>");
            string name = nm.Success
                ? HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim())
                : slug;
            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((name, slug));
        }

        return list;
    }

    // ===== sort =====
    // Sort tabs tren web: recent (mac dinh), views, rating, longest.
    // DO (2026-11-06): /?sort=views va /?cat=x&sort=views DOI LIST (total+sequence khac),
    // search/studio/performer/collection khong xac nhan sort -> khong hien dong 2.
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mặc định",       ""),
        ("Xem nhiều",      "views"),
        ("Top đánh giá",   "rating"),
        ("Dài nhất",       "longest"),
    };

    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return null;
        if (string.IsNullOrWhiteSpace(c) || c.StartsWith("cat/"))
            return Sorts;
        return null;
    }

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return null;
        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in Sorts)
            if (s == sort)
                return s;
        return null;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null)
            return null;
        sort = NormalizeSort(sort);
        if (string.IsNullOrEmpty(sort))
            return null;
        foreach (var o in opts)
            if (o.sort == sort)
                return sort;
        return null;
    }

    public static string SortLabel(string sort) => sort switch
    {
        "views" => "xem nhiều",
        "rating" => "top đánh giá",
        "longest" => "dài nhất",
        _ => "mặc định"
    };

    // ===== head menu (phu thuoc search/sort/c, dung lai moi request) =====
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/sakurasins";
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
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    // ===== base menu (khong phu thuoc context, cache 1 lan) =====
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host,
        List<(string name, string slug)> cats,
        List<(string name, string slug)> studios,
        List<(string name, string slug)> performers,
        List<(string name, string slug)> collections)
    {
        host = host.TrimEnd('/');
        string root = host + "/sakurasins";

        var menu = new List<Shared.Models.SISI.Base.MenuItem>();

        if (cats != null && cats.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = cats.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, root + "?c=cat/" + x.slug)).ToList()
            });
        }

        if (studios != null && studios.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Studio",
                playlist_url = "submenu",
                submenu = studios.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, root + "?c=studio/" + x.slug)).ToList()
            });
        }

        if (performers != null && performers.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Diễn viên",
                playlist_url = "submenu",
                submenu = performers.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, root + "?c=performer/" + x.slug)).ToList()
            });
        }

        if (collections != null && collections.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Bộ sưu tập",
                playlist_url = "submenu",
                submenu = collections.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, root + "?c=collection/" + x.slug)).ToList()
            });
        }

        return menu;
    }

    // ===== popup (long-press): chip tu trang detail =====
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // danh muc: <a class="cat-chip" href="/?cat=slug">NAME</a>
        foreach (Match m in Regex.Matches(html,
            "<a class=\"cat-chip\" href=\"/\\?cat=([^\"]+)\"[^>]*>([^<]+)</a>"))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name))
                continue;
            if (!seen.Add("category/" + slug))
                continue;
            list.Add(("category", slug, name));
        }

        // studio: <a href="/studio/slug"><strong>NAME</strong></a>
        foreach (Match m in Regex.Matches(html,
            "<a href=\"/studio/([^\"]+)\"[^>]*><strong>([^<]+)</strong></a>"))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name))
                continue;
            if (!seen.Add("studio/" + slug))
                continue;
            list.Add(("studio", slug, name));
        }

        return list;
    }
}
