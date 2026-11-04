using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Vietmup;

public static class VietmupTo
{
    public static string SiteHost = "https://vietmup.lol";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36";

    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        // Search
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "q=" + HttpUtility.UrlEncode(search);
            if (pg > 1) q += "&page=" + pg;
            return host + "/search?" + q;
        }

        // Category
        if (!string.IsNullOrEmpty(c))
        {
            string url = c.StartsWith("http") ? c : host + "/category/" + c.Trim('/');
            if (pg > 1) url += (url.Contains("?") ? "&" : "?") + "page=" + pg;
            if (!string.IsNullOrWhiteSpace(sort))
                url += (url.Contains("?") ? "&" : "?") + "sort=" + sort.Trim();
            return url;
        }

        // Home
        string basePath = host + "/";
        string qs = "";
        if (pg > 1) qs += "page=" + pg;
        if (!string.IsNullOrWhiteSpace(sort))
            qs += (qs == "" ? "" : "&") + "sort=" + sort.Trim();
        if (qs != "")
            basePath += "?" + qs;
        return basePath;
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();
        foreach (string raw in html.Split(new[] { "<div class=\"group\">" }, StringSplitOptions.None).Skip(1))
        {
            string b = raw.Length > 3000 ? raw.Substring(0, 3000) : raw;

            var hm = Regex.Match(b, "<a href=\"(/watch/[^\"]+)\"");
            if (!hm.Success)
                continue;
            string href = hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            var tm = Regex.Match(b, "alt=\"([^\"]{4,200})\"");
            if (!tm.Success)
                continue;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, "src=\"(https://cdn\\d*\\.vietmup\\.lol/[^\"]+)\"");
            if (im.Success)
            {
                poster = im.Groups[1].Value;
                if (poster.StartsWith("/"))
                    poster = SiteHost + poster;
            }

            var item = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "vietmup",
                    href = href,
                    image = poster
                }
            };

            playlists.Add(item);
        }

        return playlists;
    }

    // Player page: extract /api/stream?t=... from data-server-url
    public static string StreamToken(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, "data-server-type=\"m3u8\"[^>]*data-server-url=\"(/api/stream\\?t=[^\"]+)\"");
        if (m.Success)
            return HttpUtility.HtmlDecode(m.Groups[1].Value);

        return null;
    }

    // Server Abyss: data-server-url là trang player riêng (player.abyssplayer.com/<slug>)
    public static string AbyssUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, "data-server-type=\"abyss\"[^>]*data-server-url=\"(https://player\\.abyssplayer\\.com/[^\"]+)\"");
        if (m.Success)
            return HttpUtility.HtmlDecode(m.Groups[1].Value);

        return null;
    }

    // Category list từ home page (fallback hardcode)
    public static List<(string name, string slug)> Categories(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, "href=\"/category/([^\"]+)\"[^>]*>([^<]{2,60})<"))
        {
            string slug = m.Groups[1].Value.Trim('/');
            string name = m.Groups[2].Value.Trim().TrimStart('#');
            if (string.IsNullOrEmpty(name))
                name = slug;
            if (seen.Add(slug))
                list.Add((name, slug));
        }

        return list;
    }

    // ===== MENU =====
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/vietmup";
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
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };

        // Sort options
        var sorts = new (string name, string sort)[]
        {
            ("Mặc định", ""),
            ("Mới nhất", "latest"),
            ("Xem nhiều nhất", "view_count"),
            ("Hot trong tuần", "view_by_week"),
        };

        var sub = new List<Shared.Models.SISI.Base.MenuItem>(sorts.Length);
        foreach (var (name, s) in sorts)
            sub.Add(new Shared.Models.SISI.Base.MenuItem(name, link(s)));

        string sortLabel = string.IsNullOrEmpty(sort) ? "mặc định"
            : sort == "latest" ? "mới nhất"
            : sort == "view_count" ? "xem nhiều nhất"
            : sort == "view_by_week" ? "hot trong tuần" : sort;

        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {sortLabel}",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host, List<(string name, string slug)> categories = null)
    {
        var menu = new List<Shared.Models.SISI.Base.MenuItem>();

        if (categories != null && categories.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = categories.Select(cat =>
                    new Shared.Models.SISI.Base.MenuItem(
                        cat.name,
                        host + "/vietmup?c=" + cat.slug
                    )).ToList()
            });
        }

        return menu;
    }
}
