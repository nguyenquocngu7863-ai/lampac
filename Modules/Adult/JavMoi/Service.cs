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
    // Do lai 2026-11-01: x. CHET HAN (BoringSSL SSL_connect: Connection
    // closed abruptly, 4/4 URL) trong khi z. 200 cho ca trang chu, the loai,
    // quoc gia va ?page=2 => doi SiteHost sang z, x chi con la duong du phong.
    public static string SiteHost = "https://z.javmoi.blog";
    public static string AltHost = "https://x.javmoi.blog";

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
    // Do 2026-11-23: `/danh-sach/phim-moi` phat link TUYET DOI sang host
    // `javchat.blog` (cung noi dung, md5 trang chi tiet y het z.javmoi.blog),
    // trong khi trang chu `/` van phat `z.javmoi.blog`. Regex cu doi cung
    // `javmoi\.` nen list phim-moi ra 0 item (home 503). Chi khop duong dan
    // `/phim/`, host lay tu chinh href.
    static readonly Regex _itemRe = new Regex(
        "<a class=\"m-block movie-item\" "
        + "href=\"(https?://[^\"]+/phim/[^\"]+)\" "
        + "title=\"([^\"]*)\"",
        RegexOptions.IgnoreCase);

    // Phan trang: site dung ?page=N (trang cuoi thay o link "last").
    // Do 2026-11-01: /danh-sach/phim-moi co 864 trang. Truoc day module
    // khong tra total_pages nen app chi xem duoc page 1.
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html, "[?&]page=(\\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max)
                max = n;
        }
        return max;
    }

    public static List<PlaylistItem> Playlist(string uri, string html)
        => Items(uri, html);

    // "Co the ban thich?" = khoi `<ul id="more-movie">` cua trang chi tiet.
    // Khong tim thay khoi -> list rong (popup van co chip).
    static readonly Regex _moreRe = new Regex(
        "<ul[^>]*id=\"more-movie\"[^>]*>(.*?)</ul>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static List<PlaylistItem> PlaylistRelated(string uri, string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<PlaylistItem>();

        var m = _moreRe.Match(html);
        if (!m.Success)
            return new List<PlaylistItem>();

        return Items(uri, m.Groups[1].Value);
    }

    static List<PlaylistItem> Items(string uri, string html)
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
            if (string.IsNullOrEmpty(poster))
            {
                // markup moi: <img src="https://<host>/storage/images/<slug>/thumb.webp">
                var ig = Regex.Match(tail, "src=\"([^\"]*/storage/images/[^\"]+)\"");
                if (ig.Success)
                {
                    string v = ig.Groups[1].Value;
                    poster = v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : origin + v;
                }
            }
            if (string.IsNullOrEmpty(poster))
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

    // ===== POPUP (long-press) =====
    // Trang chi tiet in cac khoi `<div id="extras">`:
    //   <i class="fa-user-tag"></i>&nbsp;Diễn viên:  (thuong RONG)
    //   <i class="fa-bars"></i>&nbsp;Thể loại:      <a href="/the-loai/x" title="...">
    //   <i class="fa-tag"></i>&nbsp;Từ khóa:        <a href="/tu-khoa/x" title="...">
    // Link la TUYET DOI sang javchat.blog du trang phuc vu tu z.javmoi.blog
    // -> chi lay PATH, khong lay host.
    static readonly Regex _extrasRe = new Regex(
        "<div id=\"extras\">(.*?)</div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    static readonly Regex _chipLinkRe = new Regex(
        "<a href=\"([^\"]+)\" title=\"([^\"]*)\"",
        RegexOptions.IgnoreCase);

    // kind: "cat" = /the-loai/…, "tag" = /tu-khoa/…
    public static List<(string kind, string path, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrWhiteSpace(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in _extrasRe.Matches(html))
        {
            foreach (Match a in _chipLinkRe.Matches(m.Groups[1].Value))
            {
                string path = ChipPath(a.Groups[1].Value);
                if (string.IsNullOrEmpty(path))
                    continue;

                string kind = path.StartsWith("/the-loai/", StringComparison.OrdinalIgnoreCase)
                    ? "cat"
                    : path.StartsWith("/tu-khoa/", StringComparison.OrdinalIgnoreCase)
                        ? "tag" : null;

                if (kind == null || !seen.Add(kind + "/" + path))
                    continue;

                string name = WebUtility.HtmlDecode(a.Groups[2].Value.Trim());
                if (string.IsNullOrEmpty(name))
                    continue;

                list.Add((kind, path, name));
            }
        }

        return list;
    }

    // `https://javchat.blog/the-loai/x?y=1` -> `/the-loai/x`
    public static string ChipPath(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;

        string p = href.Trim();

        int scheme = p.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            int slash = p.IndexOf('/', scheme + 3);
            if (slash < 0)
                return null;
            p = p.Substring(slash);
        }

        if (!p.StartsWith("/"))
            return null;

        int cut = p.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0)
            p = p.Substring(0, cut);

        p = p.TrimEnd('/');
        return p.Length > 1 ? p : null;
    }

    // Chi nhan URI thuoc site + dung dang trang phim (chong SSRF).
    public static readonly string[] AllowedHosts = { "javmoi.blog", "javchat.blog" };

    public static string DetailUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string url = uri.Trim();
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = SiteHost + "/" + url.Trim('/');

        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var u))
            return null;

        if (u.Scheme != System.Uri.UriSchemeHttp && u.Scheme != System.Uri.UriSchemeHttps)
            return null;

        bool hostOK = false;
        foreach (string h in AllowedHosts)
        {
            if (u.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
                || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))
            {
                hostOK = true;
                break;
            }
        }

        if (!hostOK || !u.AbsolutePath.StartsWith("/phim/", StringComparison.OrdinalIgnoreCase))
            return null;

        return u.GetLeftPart(System.UriPartial.Path);
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

    static readonly HashSet<string> DeadCats = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "/the-loai/sexhd", "/the-loai/hentai", "/the-loai/tap-the",
        "/the-loai/viet-nam-clip", "/quoc-gia/han-quoc"
    };

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

            // Danh muc CHET tren chinh site (do 2026-11-01: trang 200 nhung
            // 0 phim, chi con widget "top phim" dung chung moi trang):
            //   /the-loai/sexhd  (alias rong cua /the-loai/sex-hd)
            //   /the-loai/hentai, /the-loai/tap-the, /the-loai/viet-nam-clip
            //   /quoc-gia/han-quoc
            // Bo khoi menu, neu khong app bam vao se an 503 "playlists".
            if (DeadCats.Contains(path))
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
