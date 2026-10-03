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
    // Site doi subdomain giua `x.javmoi.blog` va `z.javmoi.blog` (do lai
    // 2026-10-03):
    //   x. = host chu nhung chuong — SSL rought 2/3 lan do, chi 1/3 tra 200
    //   z. = on dinh 3/3 200, noi dung trang cung 74.486B
    // => SiteHost = x (theo y site), AltHost = z lam FALLBACK TU DONG o
    // FetchHtmlAsync: 2 duong chay song, ben nao co noi dung thi lay nen
    // gan khong bao gio tra 0 phim.
    // Regex ben duoi van KHONG hardcode subdomain.
    public static string SiteHost = "https://x.javmoi.blog";
    public static string AltHost = "https://z.javmoi.blog";

    // Dao subdomain cua site: x -> z, z -> x. Tra null neu khong phai host
    // nay (khong sua link host khac).
    public static string SwapHost(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        if (url.StartsWith(SiteHost, StringComparison.OrdinalIgnoreCase))
            return AltHost + url.Substring(SiteHost.Length);

        if (url.StartsWith(AltHost, StringComparison.OrdinalIgnoreCase))
            return SiteHost + url.Substring(AltHost.Length);

        return null;
    }

    // Goc `scheme://authority` cua mot URL. Poster/link lay theo chinh URL
    // do chu khong lay theo SiteHost, de van dung khi dang dung host fallback.
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

    public static string ChromeUA = "Mozilla/5.0 (Linux; "
        + "Android 13) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/152.0.0.0 Mobile Safari/537.36";

    // Trang chu/phan trang: /danh-sach/phim-moi?page=N
    // The loai: /the-loai/<slug>   Quoc gia: /quoc-gia/<slug>
    // Tim kiem: /?search=<query>  (form action="/" name="search")
    // `c = "moi-nhat"` = phim moi (danh sach `phim-moi`) — chi danh cho MENU,
    // vi app chi bam duoc muc co submenu (skill 9c) nen "Moi nhat" phai co
    // URL rieng de no khac `/javmoi` tran; giong giu no ra `/danh-sach/phim-moi`.
    public const string ListNew = "moi-nhat";

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

        // `moi-nhat` VA rong deu ra `/danh-sach/phim-moi`.
        if (string.IsNullOrEmpty(c) || string.Equals(c, ListNew,
            StringComparison.OrdinalIgnoreCase))
        {
            return host + "/danh-sach/phim-moi"
                + (pg > 1 ? "?page=" + pg : "");
        }

        return host + "/" + c.Trim('/') + "/"
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

            // Origin lay tu chinh href -> poster luon dung host da phuc vu
            // trang nay (x hoac z), khong bi loi anh khi dang fallback.
            string origin = Origin(href);

            string poster = "";
            var im = Regex.Match(tail,
                "data-original=\"(/storage/images/[^\"]+)\"");
            if (im.Success)
                poster = origin + im.Groups[1].Value;
            else
            {
                var bg = Regex.Match(tail,
                    "background-image:url\\('(/storage/images/[^']+)'\\)");
                if (bg.Success)
                    poster = origin + bg.Groups[1].Value;
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

    // `baseHost` = Origin cua trang chi tiet da lay duoc stream — cho phep
    // playlist dung host that (khong nhat la SiteHost) khi fallback dang bat.
    public static string PlaylistUrl(string path, string baseHost = null)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        return (string.IsNullOrEmpty(baseHost) ? SiteHost : baseHost) + path;
    }

    // Danh muc trang chu xuat hien o HAI cho (do 2026-10-03):
    //   1. `main-menu`  — 11 muc, ten nam trong text:  <a href="/the-loai/sexhd"> Phim SexHD </a>
    //   2. `footer`     — them 13 muc, ten nam trong `title`:
    //                     <a class="link-x" href="/the-loai/vung-trom" title="Vụng trộm">Vụng trộm</a>
    // Truoc do chi cat `main-menu` nen menu thieu 13/24 muc (chi 11).
    // -> Quet CA trang, lay ten tu `title` neu co, khong thi tu text.
    // Link phim la `/phim/...` nen khong bao gio trung danh muc.
    static readonly Regex _navRe = new Regex(
        "<a\\s+[^>]*href=\"(?<path>/(?:the-loai|quoc-gia)/[a-z0-9\\-]+)\"[^>]*>"
        + "\\s*(?<name>[^<]{1,60}?)\\s*</a>", RegexOptions.IgnoreCase);

    public static List<(string name, string path)> NavList(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in _navRe.Matches(html))
        {
            // Bo dau '/' cuoi neu site co bien the — `/the-loai/x` va
            // `/the-loai/x/` la cung danh muc, chi giu mot.
            string path = m.Groups["path"].Value.TrimEnd('/');
            if (!seen.Add(path))
                continue;

            string name = WebUtility.HtmlDecode(m.Groups["name"].Value.Trim());

            // Text rong -> lay `title` cua chinh the `<a>` do.
            if (string.IsNullOrEmpty(name))
            {
                var ti = Regex.Match(m.Value, "title=\"([^\"]+)\"",
                    RegexOptions.IgnoreCase);
                if (ti.Success)
                    name = WebUtility.HtmlDecode(ti.Groups[1].Value.Trim());
            }

            if (string.IsNullOrEmpty(name))
                continue;

            list.Add((name, path));
        }

        return list;
    }

    // Cong thuc menu 9g — 2 tang, KHONG muc root nao thieu submenu (skill 9c):
    //   1. Tim kiem   — search_on (duoc phep o root)
    //   2. Sap xep    — submenu; site JavMoi KHONG co sort gi (chi mot danh
    //                   sach `phim-moi`) nen chi 1 dong "Moi nhat".
    //   3. The loai   — submenu 24 danh muc tu trang chu + footer.
    // Truoc do "Moi nhat" nam TRAN root, `playlist_url` trung dung `/javmoi`
    // => app khong bam duoc (chi muc co `submenu` moi push `playlist_url`).
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
            }
        };

        // Dong 2 — Sắp xếp. `?c=` (skill 9d): app gắn `?pg=N` bang query.
        var sortSub = new List<MenuItem>()
        {
            new MenuItem("Mới nhất",
                host + "/javmoi?c=" + JavMoiTo.ListNew)
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
                    new MenuItem(x.name, host + "/javmoi?c="
                        + HttpUtility.UrlEncode(x.path))).ToList()
            });
        }

        return menu;
    }
}
