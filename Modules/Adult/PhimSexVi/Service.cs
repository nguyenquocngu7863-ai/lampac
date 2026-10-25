using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace PhimSexVi;
public static class PhimSexViTo
{
    public static string SiteHost = "https://phimsexvi.com";
    public static string EmbedHost = "https://embed.phimsexvi.com";
    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";

    public static string Origin(string url)
    {
        try
        {
            var u = new Uri(url);
            return u.Scheme + "://" + u.Authority;
        }
        catch
        {
            return SiteHost;
        }
    }

    // Trang chu: /  (40 card)
    // The loai: /category/<slug>          + ?page=N  (24 card/trang)
    // Tim search: /search?q=<kw>          + &page=N
    // Hom epage: /videos/new?page=N       (home bo qua ?page)
    public static string Uri(string host, string search, string c, int pg)
    {
        host = string.IsNullOrEmpty(host) ? SiteHost : host;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "/search?q=" + HttpUtility.UrlEncode(search.Trim());
            return pg > 1 ? host + q + "&page=" + pg : host + q;
        }
        if (string.IsNullOrEmpty(c))
            return pg > 1 ? host + "/videos/new?page=" + pg : host + "/";
        return host + "/" + c.Trim('/') + (pg > 1 ? "?page=" + pg : "");
    }

    // Item tren trang chu / the loai:
    //   <article class="video-card">
    //     <a class="video-thumb" href="https://phimsexvi.com/video/<slug>">
    //       <img src="https://phimsexvi.com/upload/anh-bia-phim-sex/<file>.jpg" ...>
    //       <time class="video-badge" ...>1:58:54</time>
    //     <h3 class="video-title"><a href="...">TITLE</a></h3>
    static readonly Regex _itemRe = new Regex(
        "<article[^>]+class=\"video-card\"[^>]*>.*?"
        + "<a[^>]+href=\"(?<href>[^\"]*/video/[^\"]+)\"[^>]*>\\s*"
        + "<img[^>]+src=\"(?<poster>[^\"]+)\"[^>]*>"
        + "(?:.*?<time[^>]+class=\"video-badge\"[^>]*>(?<time>[^<]+)</time>)?"
        + ".*?<h3[^>]*class=\"video-title\"[^>]*>\\s*<a[^>]*>(?<title>[^<]+)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    static string Abs(string url)
        => string.IsNullOrEmpty(url) ? url
         : url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url
         : url.StartsWith("//") ? "https:" + url
         : SiteHost + (url.StartsWith("/") ? url : "/" + url);

    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in _itemRe.Matches(html))
        {
            string href = Abs(m.Groups["href"].Value);
            if (!seen.Add(href))
                continue;
            string title = HttpUtility.HtmlDecode(m.Groups["title"].Value.Trim());
            if (string.IsNullOrEmpty(title))
                continue;
            list.Add(new PlaylistItem
            {
                name = title,
                // route tuong doi: SISI se ghep thanh {host}/phimsexvi/vidosik?uri=<trang chi tiet>.
                // Neu de URL tuyet doi cua site thi bi HostStreamProxy -> /proxy/<hash> -> player
                // nhan HTML va bao "no supported source was found".
                video = string.IsNullOrEmpty(uri) ? href : uri + "?uri=" + HttpUtility.UrlEncode(href),
                // json = true: client goi vidosik roi doc {"HLS": ...}
                // thieu co nay -> player coi URL la media -> "no supported source was found"
                json = true,
                picture = Abs(m.Groups["poster"].Value),
                time = m.Groups["time"].Success
                    ? HttpUtility.HtmlDecode(m.Groups["time"].Value.Trim()) : null,
            });
        }
        return list;
    }

    // Nav the loai tu trang chu:
    //   <a class="submenu-link" href="https://phimsexvi.com/category/<slug>" title="Ten">...
    static readonly Regex _navRe = new Regex(
        "href=\"(?:https?://[^\"'/]+)?/category/(?<slug>[a-z0-9\\-]+)\"[^>]*title=\"(?<name>[^\"]{1,80})\"",
        RegexOptions.IgnoreCase);
    public static List<(string name, string path)> NavList(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in _navRe.Matches(html))
        {
            string slug = m.Groups["slug"].Value;
            if (!seen.Add(slug))
                continue;
            string name = WebUtility.HtmlDecode(m.Groups["name"].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;
            list.Add((name, "/category/" + slug));
        }
        return list;
    }

    // Trang chi tiet /video/<slug> co iframe embed:
    //   <iframe src="https://embed.phimsexvi.com/embed/<id>/p1?...">
    static readonly Regex _embedRe = new Regex(
        "embed\\.phimsexvi\\.com/embed/(?<id>[a-zA-Z0-9]+)/p1",
        RegexOptions.IgnoreCase);
    public static string ExtractVideoId(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var m = _embedRe.Match(html);
        return m.Success ? m.Groups["id"].Value : null;
    }

    // Trang embed chua bien JS:
    //   let m3u8_file_url = '\/phim-sex\/<id>\/cdn.m3u8';
    static readonly Regex _m3u8Re = new Regex(
        "m3u8_file_url\\s*=\\s*[\"\\'](?<path>[^\"\\']+\\.m3u8)[\"\\']",
        RegexOptions.IgnoreCase);
    public static string ExtractM3u8Path(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var m = _m3u8Re.Match(html);
        if (!m.Success) return null;
        // Unescape backslash-slash: \/ -> /
        return m.Groups["path"].Value.Replace("\\/", "/");
    }

    // Cong thuc menu 9g — 2 tang, KHONG muc root nao thieu submenu:
    //   1. Tim kiem — search_on (duoc phep o root)
    //   2. Sap xep — submenu (chi "Moi nhat", site khong co sort khac)
    //   3. The loai — submenu tu trang chu
    public static List<MenuItem> Menu(
        string host,
        List<(string name, string path)> nav = null)
    {
        var menu = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/phimsexvi"
            }
        };
        var sortSub = new List<MenuItem>()
        {
            new MenuItem("Mới nhất",
                host + "/phimsexvi")
        };
        menu.Add(new MenuItem()
        {
            title = "Sắp xếp",
            playlist_url = "submenu",
            submenu = sortSub
        });
        if (nav != null && nav.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = nav.Select(x =>
                    new MenuItem(x.name,
                        host + "/phimsexvi?c=" + HttpUtility.UrlEncode(x.path))).ToList()
            });
        }
        return menu;
    }
}
