using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace SexVietDam;

public static class SvdTo
{
    public static string SiteHost = "https://sexvietdam.blog";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // Site dung de "sexvietdam.blog" trong query config cua player.
    public static string Domain = "sexvietdam.blog";

    // SSR that, khong can Chrome. Moi trang 24 phim, KHONG co sort ->
    // chi co ?page=N (da do: ?page=2/3 -> 0 phim trung trang truoc).
    //   /                      trang chu
    //   /the-loai/<slug>       16 the loai
    //   /tag/<slug>            2 tu khoa (xnxx, xvideos)
    //   /tim-kiem?k=<q>        tim kiem (k rong -> 404, khong phai loi)
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (pg < 1)
            pg = 1;

        string u, q = null;

        if (!string.IsNullOrWhiteSpace(search))
        {
            u = host + "/tim-kiem";
            q = "k=" + HttpUtility.UrlEncode(search.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string p = c.Trim();
            u = p.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? p
                : host + "/" + p.Trim('/');
        }
        else
        {
            u = host + "/";
        }

        if (pg > 1)
            q = (q == null ? "" : q + "&") + "page=" + pg;

        if (q != null)
            u += (u.Contains("?") ? "&" : "?") + q;

        return u;
    }

    // Trang dung Alpine.js: khung danh sach la <template> voi :href/:title
    // (khong co href that) nen regex bat `href="http.../phim-sex/..."` tu loai
    // bo het template, chi con 24 the that.
    //
    //   <a href="https://sexvietdam.blog/phim-sex/<slug>"
    //      title="TIEU DE"
    //      class="item__thumbnail">
    //     <img src="https://sexvietdam.blog/resize/800/...">
    //     <div class="item__labels"><span>Gái Xinh</span></div>
    //   </a>
    static readonly Regex ReCard = new Regex(
        "<a\\s+href=\"(?<url>https?://[^\"]*/phim-sex/[^\"]+)\"(?<mid>[\\s\\S]{0,220}?)class=\"item__thumbnail\"\\s*>(?<body>[\\s\\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<PlaylistItem> Playlist(string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReCard.Matches(html))
        {
            string uri = m.Groups["url"].Value.Trim();
            if (!seen.Add(uri))
                continue;

            // Tieu de: attribute `title` cua the <a> (khong ton tai trong template).
            string name = "";
            var mt = Regex.Match(m.Groups["mid"].Value, "title\\s*=\\s*\"([^\"]*)\"",
                RegexOptions.IgnoreCase);
            if (mt.Success)
                name = Clean(HttpUtility.HtmlDecode(mt.Groups[1].Value));

            if (name.Length == 0)
            {
                var h4 = Regex.Match(m.Groups["body"].Value, "<h4[^>]*>([\\s\\S]*?)</h4>",
                    RegexOptions.IgnoreCase);
                if (h4.Success)
                    name = Clean(HttpUtility.HtmlDecode(Regex.Replace(h4.Groups[1].Value, "<[^>]+>", " ")));
            }

            if (name.Length == 0)
                continue;

            // Anh the: `src=` thuc, khong phai `:src=` / `data-src=`.
            string poster = "";
            var mi = Regex.Match(m.Groups["body"].Value, "(?<![:\\w-])src\\s*=\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);
            if (mi.Success)
                poster = Abs(mi.Groups[1].Value);

            // Site KHONG co duration/luot xem -> lay nhan tren the
            // (Gái Xinh / Vú To ...) de hien badge.
            string time = "";
            var ml = Regex.Match(m.Groups["body"].Value,
                "item__labels\"[\\s\\S]{0,200}?<span[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
            if (ml.Success)
                time = Clean(ml.Groups[1].Value);

            list.Add(new PlaylistItem()
            {
                video = "sexvietdam/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                name = name,
                picture = poster,
                time = time,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "sexvietdam",
                    href = uri,
                    image = poster
                }
            });
        }

        return list;
    }

    // NAV trong trang chu day du 16 the loai + 2 tu khoa. KHONG co trang index
    // (/the-loai, /tag deu 404) nen lay chu tu nav, khong lay trang rieng.
    public static List<(string name, string path)> Genres(string html)
        => Slugs(html, "the-loai");

    public static List<(string name, string path)> Tags(string html)
        => Slugs(html, "tag");

    static List<(string name, string path)> Slugs(string html, string kind)
    {
        var res = new List<(string name, string path)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var re = new Regex("href=\"(?:https?://[^\"/]+)?/" + kind + "/([a-z0-9-]+)\"[^>]*>\\s*([^<]{1,80}?)\\s*</a>",
            RegexOptions.IgnoreCase);

        foreach (Match m in re.Matches(html))
        {
            string slug = m.Groups[1].Value;
            if (!seen.Add(slug))
                continue;

            string name = Clean(HttpUtility.HtmlDecode(m.Groups[2].Value));
            if (name.Length == 0)
                continue;

            res.Add((name, kind + "/" + slug));
        }

        return res;
    }

    // Fallback tinh: fetch loi -> van tra menu day du (khong bao gio rut gon).
    // Ten lay dung theo nav hien tai cua site.
    public static readonly IReadOnlyList<(string name, string path)> FallbackGenres =
        new (string, string)[]
        {
            ("Tự Quay",          "the-loai/tu-quay"),
            ("Tự Sướng",         "the-loai/tu-suong"),
            ("Vú To",            "the-loai/vu-to"),
            ("Gái Xinh",         "the-loai/gai-xinh"),
            ("Học Sinh",         "the-loai/hoc-sinh"),
            ("Hiếp Dâm",         "the-loai/hiep-dam"),
            ("Viet69",           "the-loai/viet69"),
            ("Mông Đẹp",         "the-loai/mong-dep"),
            ("Vụng Trộm",        "the-loai/vung-trom"),
            ("Tập Thể",          "the-loai/tap-the"),
            ("Mông To",          "the-loai/mong-to"),
            ("Tư Thế 69",        "the-loai/tu-the-69"),
            ("Bú Lồn",           "the-loai/bu-lon"),
            ("Dáng Chuẩn",       "the-loai/nguoi-mau-dang-chuan"),
            ("Bắn Tinh Đầy Lồn", "the-loai/ban-tinh-day-lon"),
            ("Bú cu",            "the-loai/bu-cu"),
        };

    public static readonly IReadOnlyList<(string name, string path)> FallbackTags =
        new (string, string)[]
        {
            ("Xnxx",   "tag/xnxx"),
            ("Xvideos", "tag/xvideos"),
        };

    // CONG THUC MENU SISI (skill lampac-adult-module muc 9g):
    //   dong 1 : Tìm kiếm (search_on, duoc phep o root)
    //   dong 2+: taxonomy — moi muc goc PHAI co submenu, muc goc khong
    //            submenu = chet (muc 9c).
    // Site KHONG co kieu xep hang -> KHONG dong "Sắp xếp".
    public static List<MenuItem> Menu(
        string host,
        IReadOnlyList<(string name, string path)> genres = null,
        IReadOnlyList<(string name, string path)> tags = null)
    {
        var root = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/sexvietdam"
            }
        };

        if (genres != null && genres.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var g in genres)
                sub.Add(new MenuItem(g.name, host + "/sexvietdam?c=" + g.path));
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = sub });
        }

        if (tags != null && tags.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var t in tags)
                sub.Add(new MenuItem(t.name, host + "/sexvietdam?c=" + t.path));
            root.Add(new MenuItem() { title = "Từ khoá", playlist_url = "submenu", submenu = sub });
        }

        root.Add(new MenuItem()
        {
            title = "Clip Mới",
            playlist_url = "submenu",
            submenu = new List<MenuItem>() { new MenuItem("Mới Nhất", host + "/sexvietdam") }
        });

        return root;
    }

    // 3 nút server tren trang chi tiet. LUU Y: `data-source` dung TRUOC
    // `class` trong the <button> -> khong duoc ghep theo thu tu attribute.
    //   [1] StreamQQ Plan VIP       -> e.streamforester.name (360p+720p)
    //   [2] StreamQQ Plan VIP Alias -> vcast.name           (360p)
    //   [3] PlayHQ                  -> CDN chet (NXDOMAIN), bo
    public static List<(string name, string src)> Servers(string html)
    {
        var res = new List<(string name, string src)>();
        if (string.IsNullOrEmpty(html))
            return res;

        foreach (Match m in Regex.Matches(html, "<button\\b[^>]*set-player-source[^>]*>",
            RegexOptions.IgnoreCase))
        {
            string tag = m.Value;

            string src = Attr(tag, "data-source");
            if (string.IsNullOrEmpty(src))
                continue;

            string name = Attr(tag, "data-cdn-name");
            if (string.IsNullOrEmpty(name))
                name = "Server " + (res.Count + 1);

            res.Add((name, src));
        }

        return res;
    }

    // Doc 1 attribute trong the <button> (khong ghep theo thu tu).
    static string Attr(string tag, string key)
    {
        var m = Regex.Match(tag, key + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? HttpUtility.HtmlDecode(m.Groups[1].Value).Trim() : null;
    }

    // vcast: window.videoData = {"title":...,
    //   "sources":[{"label":"HLS","file":"/videos/<id>/master.m3u8"}]}
    // wogplayer: POST /videos/<id>/config?d=<domain> ->
    //   {"sources":[{"label":"HLS","file":"https://.../master.m3u8?d&e&s"}]}
    // Cung mot chu `"file":"..."` cho ca hai.
    public static string FileUrl(string jsonOrHtml)
    {
        if (string.IsNullOrEmpty(jsonOrHtml))
            return null;

        var m = Regex.Match(jsonOrHtml, "\"file\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    // Lay id video tu trang player: /videos/<id>/play  hoac /play/v1/<id>
    public static string VideoId(string url)
    {
        var m = Regex.Match(url ?? "", "/videos/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups[1].Value;

        m = Regex.Match(url ?? "", "/play/v1/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string Abs(string u)
    {
        if (string.IsNullOrEmpty(u))
            return "";

        if (u.StartsWith("//"))
            return "https:" + u;
        if (u.StartsWith("/"))
            return SiteHost + u;
        return u;
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return Regex.Replace(s.Trim(), @"\s+", " ");
    }
}
