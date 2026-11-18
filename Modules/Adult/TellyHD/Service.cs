using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace TellyHD;

public static class HdTo
{
    public const string SiteHost = "https://tellyhd.at";

    // PHAI trung voi Http.UserAgent cua Lampac: proxy /proxy/ luon ghi de
    // User-Agent bang gia tri do, con token CDN (tnmr.org) lai khoa theo UA +
    // Accept-Language cua request embed -> lech UA la 403.
    public const string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";

    public const string SecChUa = "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"";

    public const string AcceptLanguage = "en-US,en;q=0.9";

    // Site = WordPress + theme Dooplay (WP-Script), SSR that, khong can Chrome.
    //   /movies/page/N/            archive tat ca phim (45/trang, phan trang that)
    //   /genre/<slug>/page/N/      the loai (319)
    //   /release/<year>/page/N/    nam (56)
    //   /quality/<slug>/page/N/    chat luong/ngon ngu (31)
    //   /?s=<q>                    tim kiem (chi 1 trang, markup KHAC archive)
    // Site KHONG co sort -> khong co dong 2 menu.
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (pg < 1)
            pg = 1;

        if (!string.IsNullOrWhiteSpace(search))
        {
            // /?s= tra 9 ket qua, khong co phan trang -> trang 2 = rong.
            if (pg > 1)
                return null;

            return host + "/?s=" + HttpUtility.UrlEncode(search.Trim());
        }

        string path = IsTaxonomy(c) ? c.Trim('/') : "movies";

        return pg > 1
            ? host + "/" + path + "/page/" + pg + "/"
            : host + "/" + path + "/";
    }

    static readonly Regex ReTaxo = new Regex(
        "^(movies|genre|release|quality|cast|director)/[a-z0-9][a-z0-9\\-\\.]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsTaxonomy(string c)
        => !string.IsNullOrWhiteSpace(c) && ReTaxo.IsMatch(c.Trim().Trim('/'));

    // ================= danh sach =================

    // Thu tu attribute khac nhau tuy template:
    //   archive: <article id="post-123" class="item movies">
    //   quality: <article class="item" id="post-123">
    // Bo qua khoi slider trang chu (id="post-featured-...") de khong lap phim.
    static readonly Regex ReItem = new Regex(
        "<article\\b(?=[^>]*\\bid=\"post-\\d+\")(?=[^>]*\\bclass=\"[^\"]*\\bitem\\b)[^>]*>(?<b>[\\s\\S]*?)</article>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex ReItemUrl = new Regex(
        "<a\\s+href=\"(?<u>https?://[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex ReItemImg = new Regex(
        "<img[^>]+src=\"(?<p>[^\"]+)\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex ReItemAlt = new Regex(
        "alt=\"(?<a>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex ReItemQuality = new Regex(
        "class=\"quality\">(?<q>[^<]*)<", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<PlaylistItem> Playlist(string html, string host)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // /movies/ va /movies/page/N/ co slider "Featured" o dau trang (10
        // phim, attribute nguoc thu tu) -> chi parse khoi #archive-content
        // de danh sach khong bi lap / doi thu tu.
        int pivot = html.IndexOf("id=\"archive-content\"", StringComparison.OrdinalIgnoreCase);
        if (pivot > 0)
            html = html.Substring(pivot);

        foreach (Match m in ReItem.Matches(html))
        {
            string body = m.Groups["b"].Value;

            var mu = ReItemUrl.Match(body);
            if (!mu.Success)
                continue;

            string uri = mu.Groups["u"].Value.Trim();
            if (uri.IndexOf("/movies/", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!seen.Add(uri))
                continue;

            string name = "";
            string poster = "";

            var mi = ReItemImg.Match(body);
            if (mi.Success)
            {
                poster = mi.Groups["p"].Value.Trim();

                var ma = ReItemAlt.Match(mi.Value);
                if (ma.Success)
                    name = HttpUtility.HtmlDecode(ma.Groups["a"].Value);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                // h3 co the khong co <a> hoac co class rieng tuy template
                var h3 = Regex.Match(body, "<h3[^>]*>(?<t>[\\s\\S]*?)</h3>", RegexOptions.IgnoreCase);
                if (h3.Success)
                    name = HttpUtility.HtmlDecode(Regex.Replace(h3.Groups["t"].Value, "<[^>]+>", " "));
            }

            name = Clean(name);
            if (name.Length == 0)
                continue;

            string badge = "";
            var mq = ReItemQuality.Match(body);
            if (mq.Success)
                badge = Clean(mq.Groups["q"].Value);

            if (badge.Length == 0)
            {
                var my = Regex.Match(body, "<span>\\s*(?<y>\\d{4})\\s*</span>", RegexOptions.IgnoreCase);
                if (my.Success)
                    badge = my.Groups["y"].Value;
            }

            list.Add(new PlaylistItem()
            {
                video = "tellyhd/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                name = name,
                picture = Abs(poster),
                time = badge,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "tellyhd",
                    href = uri,
                    image = Abs(poster)
                }
            });
        }

        return list;
    }

    // Trang tim kiem dung markup khac: div.result-item > article
    //   <div class="image">...<img src>...<div class="details">
    //     <div class="title"><a href="...">Ten</a></div>
    //     <div class="meta"><span class="year">2025</span></div>
    static readonly Regex ReSearchItem = new Regex(
        "<div class=\"result-item\">(?<b>[\\s\\S]*?)</article>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<PlaylistItem> Search(string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReSearchItem.Matches(html))
        {
            string body = m.Groups["b"].Value;

            var mt = Regex.Match(body,
                "<div class=\"title\">\\s*<a\\s+href=\"(?<u>[^\"]+)\"[^>]*>(?<t>[^<]+)</a>",
                RegexOptions.IgnoreCase);
            if (!mt.Success)
                continue;

            string uri = mt.Groups["u"].Value.Trim();
            if (!seen.Add(uri))
                continue;

            string name = Clean(HttpUtility.HtmlDecode(mt.Groups["t"].Value));
            if (name.Length == 0)
                continue;

            string poster = "";
            var mi = Regex.Match(body, "<img[^>]+src=\"(?<p>[^\"]+)\"", RegexOptions.IgnoreCase);
            if (mi.Success)
                poster = mi.Groups["p"].Value.Trim();

            string badge = "";
            var my = Regex.Match(body, "class=\"year\">(?<y>\\d{4})<", RegexOptions.IgnoreCase);
            if (my.Success)
                badge = my.Groups["y"].Value;

            list.Add(new PlaylistItem()
            {
                video = "tellyhd/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                name = name,
                picture = Abs(poster),
                time = badge,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "tellyhd",
                    href = uri,
                    image = Abs(poster)
                }
            });
        }

        return list;
    }

    // ================= chi tiet / player =================

    public static string Title(string html)
    {
        var m = Regex.Match(html ?? "", "<h1>(?<t>[^<]+)</h1>", RegexOptions.IgnoreCase);
        return m.Success ? Clean(HttpUtility.HtmlDecode(m.Groups["t"].Value)) : null;
    }

    public static string Poster(string html)
    {
        var m = Regex.Match(html ?? "",
            "<div class=\"poster\">\\s*<img\\s+itemprop=\"image\"\\s+src=\"(?<p>[^\"]+)\"",
            RegexOptions.IgnoreCase);
        return m.Success ? Abs(m.Groups["p"].Value.Trim()) : "";
    }

    public static string Description(string html)
    {
        var m = Regex.Match(html ?? "",
            "itemprop=\"description\"[^>]*>(?<d>[\\s\\S]*?)</div>", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string txt = Regex.Replace(m.Groups["d"].Value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        txt = Regex.Replace(txt, "<[^>]+>", " ");
        txt = HttpUtility.HtmlDecode(txt);

        var lines = new List<string>();
        foreach (string ln in txt.Split('\n'))
        {
            string s = Clean(ln);
            if (s.Length > 0)
                lines.Add(s);
        }

        string res = string.Join("\n", lines);
        return res.Length > 600 ? res.Substring(0, 600) : res;
    }

    // The loai cua phim: khoi <div class="sgeneros"> ... rel="tag"
    static readonly Regex ReSgeneros = new Regex(
        "<div class=\"sgeneros\">(?<b>[\\s\\S]{0,3000}?)</div>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex ReGenreChip = new Regex(
        "<a\\s+href=\"(?<u>https?://[^\"]*/genre/(?<s>[^/\"]+)/)\"[^>]*rel=\"tag\"[^>]*>(?<n>[^<]*)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<(string slug, string name)> Chips(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var mb = ReSgeneros.Match(html);
        if (!mb.Success)
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReGenreChip.Matches(mb.Groups["b"].Value))
        {
            string slug = m.Groups["s"].Value.ToLowerInvariant();
            string name = Clean(HttpUtility.HtmlDecode(m.Groups["n"].Value));

            if (name.Length == 0 || !seen.Add(slug))
                continue;

            res.Add((slug, name));
        }

        return res;
    }

    // "Similar titles" — khoi that cua trang chi tiet:
    //   <div class="sbox srelacionados"> <div id="single_relacionados">
    //     <article><a href="..."><img src="..." alt="..." /></a></article>
    static readonly Regex ReSrel = new Regex(
        "<div class=\"sbox srelacionados\">(?<b>[\\s\\S]*?)</div></div>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex ReRelItem = new Regex(
        "<article>\\s*<a\\s+href=\"(?<u>[^\"]+)\"[^>]*>\\s*<img\\s+src=\"(?<p>[^\"]+)\"\\s+alt=\"(?<n>[^\"]*)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<PlaylistItem> Related(string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var mb = ReSrel.Match(html);
        if (!mb.Success)
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReRelItem.Matches(mb.Groups["b"].Value))
        {
            string uri = m.Groups["u"].Value.Trim();
            if (!seen.Add(uri))
                continue;

            string name = Clean(HttpUtility.HtmlDecode(m.Groups["n"].Value));
            if (name.Length == 0)
                continue;

            string poster = Abs(m.Groups["p"].Value.Trim());

            list.Add(new PlaylistItem()
            {
                video = "tellyhd/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "tellyhd", href = uri, image = poster }
            });
        }

        return list;
    }

    // Nut server cua trang chi tiet (Dooplay ajax player):
    //   <li id='player-option-1' class='dooplay_player_option'
    //       data-type='movie' data-post='66728' data-nume='1'>
    //     <i ...></i><span class='title'>Ultra Stream</span>
    public sealed class Server
    {
        public string type;
        public string post;
        public string nume;
        public string name;
    }

    static readonly Regex ReOption = new Regex(
        "<li\\s+id='?player-option-\\d+'?[^>]*dooplay_player_option[^>]*>(?<b>[\\s\\S]{0,300}?)</li>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<Server> Servers(string html)
    {
        var res = new List<Server>();
        if (string.IsNullOrEmpty(html))
            return res;

        foreach (Match m in ReOption.Matches(html))
        {
            string tag = m.Value.Substring(0, m.Value.IndexOf('>') + 1);

            var s = new Server()
            {
                type = Attr(tag, "data-type"),
                post = Attr(tag, "data-post"),
                nume = Attr(tag, "data-nume")
            };

            if (string.IsNullOrEmpty(s.post) || string.IsNullOrEmpty(s.nume))
                continue;

            if (string.IsNullOrEmpty(s.type))
                s.type = "movie";

            var mt = Regex.Match(m.Groups["b"].Value, "<span class='title'>(?<n>[^<]*)</span>",
                RegexOptions.IgnoreCase);
            s.name = mt.Success ? Clean(HttpUtility.HtmlDecode(mt.Groups["n"].Value)) : "";

            if (s.name.Length == 0)
                s.name = "Server " + (res.Count + 1);

            res.Add(s);
        }

        return res;
    }

    static string Attr(string tag, string key)
    {
        var m = Regex.Match(tag, key + "\\s*=\\s*'([^']*)'", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(tag, key + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);

        return m.Success ? HttpUtility.HtmlDecode(m.Groups[1].Value).Trim() : null;
    }

    // admin-ajax tra {"embed_url":"<iframe ... src=\"https:\/\/host\/e\/xxx\" ...>","type":"dtshcode"}
    public static string EmbedUrl(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        var m = Regex.Match(json, "\"embed_url\"\\s*:\\s*\"(?<e>(?:[^\"\\\\]|\\\\.)*)\"");
        if (!m.Success)
            return null;

        string html = m.Groups["e"].Value
            .Replace("\\/", "/")
            .Replace("\\\"", "\"")
            .Replace("\\u0026", "&");

        var mi = Regex.Match(html, "src=\"(?<s>[^\"]+)\"", RegexOptions.IgnoreCase);
        if (mi.Success)
            return HttpUtility.HtmlDecode(mi.Groups["s"].Value.Trim());

        var mi2 = Regex.Match(html, "src='(?<s>[^']+)'", RegexOptions.IgnoreCase);
        return mi2.Success ? HttpUtility.HtmlDecode(mi2.Groups["s"].Value.Trim()) : null;
    }

    // telly.xin/play?v=XXX — player rieng, stream nam trong data-stream-url
    // (JWT tok ~3h, gan voi UA).
    public static string StreamAttr(string html)
    {
        var m = Regex.Match(html ?? "",
            "data-stream-url=\"(?<u>[^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? HttpUtility.HtmlDecode(m.Groups["u"].Value.Trim()) : null;
    }

    // DoodStream: $.get('/pass_md5/<path>/<token>') -> base URL, roi
    // base + 10 ky tu ngau nhien + "?token=<token>&expiry=<ms>"
    public static string DoodPass(string html)
    {
        var m = Regex.Match(html ?? "",
            "\\$\\.get\\('(?<p>[^']*pass_md5[^']*)'", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["p"].Value.Trim() : null;
    }

    // ================= menu =================

    public static List<(string name, string path)> Genres(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "href=\"(?:https?://[^\"/]+)?/genre/(?<s>[a-z0-9\\-]+)/\"[^>]*>\\s*(?<n>[^<]{1,80}?)\\s*</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups["s"].Value.ToLowerInvariant();
            string name = Clean(HttpUtility.HtmlDecode(m.Groups["n"].Value));

            if (name.Length == 0 || !seen.Add(slug))
                continue;

            res.Add((name, "genre/" + slug));
        }

        res.Sort((x, y) => string.Compare(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase));
        return res;
    }

    // sitemap taxonomy: /dtyear-sitemap.xml -> /release/<year>/
    //                   /dtquality-sitemap.xml -> /quality/<slug>/
    public static List<(string name, string path)> FromSitemap(
        string xml, string kind, bool numericFirst, int cap)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(xml))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(xml,
            "<loc>https?://[^<]*/" + kind + "/(?<s>[a-z0-9][a-z0-9\\-\\.]*)/</loc>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups["s"].Value.ToLowerInvariant();
            if (!seen.Add(slug))
                continue;

            res.Add((slug, kind + "/" + slug));
        }

        if (numericFirst)
            res.Sort((x, y) => string.Compare(y.Item1, x.Item1, StringComparison.OrdinalIgnoreCase));
        else
            res.Sort((x, y) => string.Compare(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase));

        if (res.Count > cap)
            res = res.GetRange(0, cap);

        return res;
    }

    public static readonly IReadOnlyList<(string name, string path)> FallbackGenres =
        new (string, string)[]
        {
            ("18+",        "genre/18"),
            ("Hindi",      "genre/hindi"),
            ("Indian",     "genre/indian"),
            ("JAV",        "genre/jav"),
            ("MoodX",      "genre/moodx"),
            ("Uncensored", "genre/uncensored"),
            ("Uncut",      "genre/uncut"),
            ("USA",        "genre/usa"),
            ("Ullu",       "genre/ullu"),
        };

    public static readonly IReadOnlyList<(string name, string path)> FallbackYears =
        new (string, string)[]
        {
            ("2026", "release/2026"),
            ("2025", "release/2025"),
            ("2024", "release/2024"),
            ("2023", "release/2023"),
            ("2022", "release/2022"),
            ("2021", "release/2021"),
            ("2020", "release/2020"),
        };

    public static readonly IReadOnlyList<(string name, string path)> FallbackQualities =
        new (string, string)[]
        {
            ("English",  "quality/english"),
            ("Hindi HD", "quality/hindi-hd"),
            ("Uncensored", "quality/uncensored"),
            ("Jav HD",   "quality/javhd"),
        };

    // Menu SISI dung 2 tang: root chi duoc dat "Tim kiem" (search_on),
    // moi muc dieu huong phai co submenu. Site khong sort -> khong dong 2.
    public static List<MenuItem> Menu(
        string host,
        IReadOnlyList<(string name, string path)> genres = null,
        IReadOnlyList<(string name, string path)> years = null,
        IReadOnlyList<(string name, string path)> qualities = null)
    {
        var root = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/tellyhd"
            }
        };

        root.Add(new MenuItem()
        {
            title = "Phim",
            playlist_url = "submenu",
            submenu = new List<MenuItem>()
            {
                new MenuItem("Mới Nhất", host + "/tellyhd")
            }
        });

        if (genres != null && genres.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var g in genres)
                sub.Add(new MenuItem(g.name, host + "/tellyhd?c=" + HttpUtility.UrlEncode(g.path)));
            root.Add(new MenuItem() { title = "Thể Loại", playlist_url = "submenu", submenu = sub });
        }

        if (years != null && years.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var y in years)
                sub.Add(new MenuItem(y.name, host + "/tellyhd?c=" + HttpUtility.UrlEncode(y.path)));
            root.Add(new MenuItem() { title = "Năm", playlist_url = "submenu", submenu = sub });
        }

        if (qualities != null && qualities.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var q in qualities)
                sub.Add(new MenuItem(q.name, host + "/tellyhd?c=" + HttpUtility.UrlEncode(q.path)));
            root.Add(new MenuItem() { title = "Chất Lượng", playlist_url = "submenu", submenu = sub });
        }

        return root;
    }

    // Chuan hoa URL + chong SSRF: chi nhan trang /movies/ cua chinh site nay.
    public static string PageUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        uri = uri.Trim();

        string u = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : SiteHost + "/" + uri.Trim('/');

        if (!System.Uri.TryCreate(u, System.UriKind.Absolute, out var abs))
            return null;

        if (!string.Equals(abs.Host, new System.Uri(SiteHost).Host, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!abs.AbsolutePath.StartsWith("/movies/", StringComparison.OrdinalIgnoreCase))
            return null;

        return abs.Scheme + "://" + abs.Authority + abs.AbsolutePath;
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
        return Regex.Replace(HttpUtility.HtmlDecode(s).Trim(), "\\s+", " ");
    }
}
