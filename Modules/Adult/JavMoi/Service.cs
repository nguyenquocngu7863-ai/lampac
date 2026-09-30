using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace JavMoi;

public static class JavMoiTo
{
    // `x.javmoi.blog` da chet (tra 000), `z.javmoi.blog` moi la host song.
    // Site doi subdomain nen regex ben duoi KHONG hardcode `x.`/`z.`.
    public static string SiteHost = "https://z.javmoi.blog";

    public static string ChromeUA = "Mozilla/5.0 (Linux; "
        + "Android 13) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/152.0.0.0 Mobile Safari/537.36";

    // Trang chu/phan trang: /danh-sach/phim-moi?page=N
    // The loai: /the-loai/<slug>   Quoc gia: /quoc-gia/<slug>
    // Tim kiem: /?search=<query>  (form action="/" name="search")
    public static string Uri(string host, string search, string c, int pg)
    {
        host = string.IsNullOrEmpty(host) ? SiteHost : host;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "?search=" + HttpUtility.UrlEncode(search.Trim());
            return pg > 1
                ? host + "/danh-sach/phim-moi" + q + "&page=" + pg
                : host + "/" + q;
        }

        if (!string.IsNullOrEmpty(c))
            return host + "/" + c.Trim('/') + "/"
                + (pg > 1 ? "?page=" + pg : "");

        return host + "/danh-sach/phim-moi"
            + (pg > 1 ? "?page=" + pg : "");
    }

    // Poster la lazyload:
    //   <div data-original="/storage/images/<slug>/thumb.webp">
    //   fallback <div style="background-image:url('/storage/images/...')">
    // => KHONG duoc regex <img src=...>, nho do ma khong co poster.
    // KHONG hardcode `x.` hay `z.` — site doi subdomain giua `x.javmoi.blog`
    // va `z.javmoi.blog`, hardcode se tra 0 phim ngay. Chi khop duong dan
    // `/phim/` la duoc, domain lay tu chinh href.
    static readonly Regex _itemRe = new Regex(
        "<a class=\"m-block movie-item\" "
        + "href=\"(https?://[a-z0-9.-]*javmoi\\.[a-z]+/phim/[^\"]+)\" "
        + "title=\"([^\"]*)\"",
        RegexOptions.IgnoreCase);

    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in _itemRe.Matches(html))
        {
            string href = m.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            // Poster nam trong <a> sau the mo -> cat doan ngan phia truoc
            int at = m.Index;
            int len = Math.Min(1200, html.Length - at);
            string tail = html.Substring(at, len);

            string poster = "";
            var im = Regex.Match(tail,
                "data-original=\"(/storage/images/[^\"]+)\"");
            if (im.Success)
                poster = SiteHost + im.Groups[1].Value;
            else
            {
                var bg = Regex.Match(tail,
                    "background-image:url\\('(/storage/images/[^']+)'\\)");
                if (bg.Success)
                    poster = SiteHost + bg.Groups[1].Value;
            }

            list.Add(new PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "javmoi",
                    href = href,
                    image = poster
                }
            });
        }

        return list;
    }

    // Trang phim: <a class="server" data-link="/storage/m3u8/<slug>/<x>.m3u8"
    //             data-type="m3u8" data-id="12538">
    public static string StreamPath(string detailHtml)
    {
        if (string.IsNullOrEmpty(detailHtml))
            return null;

        var m = Regex.Match(detailHtml,
            "class=\"server\"[^>]*data-link=\"(/storage/m3u8/[^\"]+\\.m3u8)\"");
        if (!m.Success)
            return null;

        return m.Groups[1].Value;
    }

    // Site chia nhieu kieu playlist:
    //   main.m3u8   -> host v16-ad.ap4r.com, segment 404 (CHET, ca trong
    //                  nuoc va nuoc ngoai) -> thu hai loai duoi
    //   index.m3u8  -> p16-oec-sg.ibyteimg.com, TS bi boc header PNG
    //   master.m3u8 -> ibyteimg.com, TS thuan (0x47 o byte 0)
    // => doi main sang master/index, va lot PNG cho moi host ibyteimg.
    public static string AltPaths(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (!path.EndsWith("/main.m3u8", StringComparison.OrdinalIgnoreCase))
            return null;

        return path.Substring(0, path.Length - "main.m3u8".Length)
            + "master.m3u8";
    }

    public static string PlaylistUrl(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        return SiteHost + path;
    }

    // Nav trang chu: <li><a href="/the-loai/sexhd"> Phim SexHD </a></li>
    public static List<(string name, string path)> NavList(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var ci = StringComparison.OrdinalIgnoreCase;

        int start = html.IndexOf("class=\"main-menu\"", ci);
        if (start < 0)
            start = 0;
        else
        {
            int end = html.IndexOf("</nav>", start, ci);
            if (end > 0)
                html = html.Substring(start, end - start);
            else
                html = html.Substring(start);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a href=\"(/the-loai/[a-z0-9\\-]+|/quoc-gia/[a-z0-9\\-]+)\"[^>]*>"
            + "\\s*([^<]{1,50}?)\\s*</a>", RegexOptions.IgnoreCase))
        {
            string path = m.Groups[1].Value;
            if (!seen.Add(path))
                continue;

            string name = WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            list.Add((name, path));
        }

        return list;
    }

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
                playlist_url = host + "/javmoi"
            },
            new MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javmoi"
            }
        };

        if (nav != null && nav.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = nav.Select(x =>
                    new MenuItem(x.name, host + "/javmoi?c="
                        + HttpUtility.UrlEncode(x.path))).ToList()
            });
        }

        return menu;
    }
}
