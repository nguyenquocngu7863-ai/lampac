using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace SexHihi;

/// <summary>
/// sexhihih.blog — WordPress (theme "holy"), HTML server-side (khong CSR).
///   list   : /danh-sach/phim-moi?page=N   (24 phim/trang)
///   the loai: /the-loai/<slug>?page=N
///   quoc gia: /quoc-gia/<slug>?page=N
///   tim kiem: /?search=<kw>&page=N
///   detail : /phim/<slug>
///   player : <a class="server" data-type="m3u8" data-link="/storage/m3u8/<slug>/index.m3u8">
/// </summary>
public static class ShhTo
{
    public static string SiteHost = "https://sexhihih.blog";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // ================= URL =================
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        host = host.TrimEnd('/');

        if (pg < 1)
            pg = 1;

        // Tim kiem: form cua site GET "/" voi tham so `search`
        if (!string.IsNullOrWhiteSpace(search))
        {
            string u = host + "/?search=" + HttpUtility.UrlEncode(search.Trim());
            if (pg > 1)
                u += "&page=" + pg;
            return u;
        }

        // Danh muc: c = "the-loai/jav-hd" hoac "quoc-gia/trung-quoc"
        if (!string.IsNullOrWhiteSpace(c))
        {
            string p = c.Trim().Trim('/');
            string u = p.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? p
                : host + "/" + p;

            if (pg > 1)
                u += (u.Contains("?") ? "&" : "?") + "page=" + pg;

            return u;
        }

        // Trang chu -> dung bang "danh sach phim moi" (co phan trang day du)
        string list = host + "/danh-sach/phim-moi";
        if (pg > 1)
            list += "?page=" + pg;
        return list;
    }

    // ================= Playlist =================
    // Card: <a class="m-block movie-item" href="..." title="...">
    //         <div class="lazyload public-film-item-thumb" data-original="...">
    //         <div class="movie-title-1">...</div>
    // Sidebar "list-top-movie-item" KHONG dung class nay -> khong lot vao.
    static readonly Regex ReCard = new Regex(
        "<a\\b(?<attrs>[^>]*\\bclass\\s*=\\s*\"[^\"]*\\bm-block\\b[^\"]*movie-item[^\"]*\"[^>]*)>(?<body>[\\s\\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReCard.Matches(html))
        {
            string attrs = m.Groups["attrs"].Value;
            string body = m.Groups["body"].Value;

            string pageUrl = Abs(Attr(attrs, "href"));
            if (string.IsNullOrEmpty(pageUrl) || !pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!seen.Add(pageUrl))
                continue;

            string name = Clean(Attr(attrs, "title"));
            if (string.IsNullOrEmpty(name))
            {
                var tm = Regex.Match(body, "<div\\s+class=\"movie-title-1\"[^>]*>([\\s\\S]*?)</div>", RegexOptions.IgnoreCase);
                if (tm.Success)
                    name = Clean(tm.Groups[1].Value);
            }

            if (string.IsNullOrEmpty(name))
                continue;

            string poster = null;
            var pm = Regex.Match(body, "data-original\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(body, "data-src\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(body, "<img\\b[^>]*\\bsrc\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (pm.Success)
                poster = Abs(pm.Groups[1].Value);

            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "sexhihi", href = pageUrl, image = poster }
            });
        }

        return list;
    }

    // ================= Servers (player) =================
    // <a onclick="chooseStreamingServer(this)" class="server" href="javascript:void(0)"
    //    data-id="21340" data-link="/storage/m3u8/<slug>/index.m3u8" data-type="m3u8">1</a>
    static readonly Regex ReServer = new Regex(
        "<a\\b(?<attrs>[^>]*\\bclass\\s*=\\s*\"[^\"]*\\bserver\\b[^\"]*\"[^>]*)>(?<label>[\\s\\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<(string label, string type, string link)> Servers(string html)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReServer.Matches(html))
        {
            string attrs = m.Groups["attrs"].Value;

            string type = (Attr(attrs, "data-type") ?? "").Trim().ToLowerInvariant();
            if (type != "m3u8" && type != "m3u" && type != "mp4")
                continue;

            string link = Abs(Attr(attrs, "data-link"));
            if (string.IsNullOrEmpty(link) || !link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!seen.Add(link))
                continue;

            string label = Clean(m.Groups["label"].Value);
            if (string.IsNullOrEmpty(label))
                label = type.ToUpperInvariant();

            res.Add((label, type, link));
        }

        return res;
    }

    // ================= Taxonomy (menu) =================
    // Nav cua site: <a href="/the-loai/jav-hd">JAV</a> / <a href="/quoc-gia/trung-quoc">...</a>
    static readonly Regex ReTax = new Regex(
        "<a\\b[^>]*\\bhref\\s*=\\s*\"(?<href>[^\"]*?/(?<kind>the-loai|quoc-gia)/(?<slug>[a-z0-9\\-]+))\"[^>]*>(?<label>[\\s\\S]*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<(string kind, string slug, string name)> Taxonomy(string html)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReTax.Matches(html))
        {
            string kind = m.Groups["kind"].Value.ToLowerInvariant();
            string slug = m.Groups["slug"].Value.ToLowerInvariant();
            if (string.IsNullOrEmpty(slug))
                continue;

            string name = Clean(m.Groups["label"].Value);
            if (string.IsNullOrEmpty(name))
                name = slug;

            if (!seen.Add(kind + "/" + slug))
                continue;

            res.Add((kind, slug, name));
        }

        return res;
    }

    // Fallback khi fetch nav that bai (chi 7 the loai + 1 quoc gia tren site)
    public static List<(string kind, string slug, string name)> FallbackTax = new List<(string, string, string)>()
    {
        ("the-loai", "jav-hd", "JAV"),
        ("the-loai", "sextop1", "Sextop1"),
        ("the-loai", "vlxx", "vlxx"),
        ("the-loai", "xnxx", "XNXX"),
        ("the-loai", "viet-nam", "Việt Nam"),
        ("the-loai", "vietsub", "vietsub"),
        ("the-loai", "phim-sex-trung-quoc", "Trung Quốc"),
        ("quoc-gia", "trung-quoc", "Trung Quốc")
    };

    public static List<MenuItem> Menu(string host, List<(string kind, string slug, string name)> tax)
    {
        host = host.TrimEnd('/');
        string root = host + "/sexhihi";

        var genres = new List<MenuItem>();
        var countries = new List<MenuItem>();

        foreach (var t in tax ?? new List<(string, string, string)>())
        {
            string url = root + "?c=" + HttpUtility.UrlEncode(t.kind + "/" + t.slug);
            if (t.kind == "quoc-gia")
                countries.Add(new MenuItem(t.name, url));
            else
                genres.Add(new MenuItem(t.name, url));
        }

        var menu = new List<MenuItem>()
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = root },
            new MenuItem() { title = "Danh sách phim mới", playlist_url = root }
        };

        if (genres.Count > 0)
            menu.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = genres });

        if (countries.Count > 0)
            menu.Add(new MenuItem() { title = "Quốc gia", playlist_url = "submenu", submenu = countries });

        return menu;
    }

    // ================= Phan trang =================
    static readonly Regex RePage = new Regex("\\?page=(\\d+)", RegexOptions.IgnoreCase);

    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 0;

        int max = 0;
        foreach (Match m in RePage.Matches(html))
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;

        return max;
    }

    // ================= Helper =================
    // Cloudflare / trang loi: HTML van co nen phai kiem tra noi dung,
    // con la parse ra 0 phim (page rong) -> app bao loi nham.
    public static bool IsBlocked(string html)
    {
        if (string.IsNullOrEmpty(html))
            return true;

        string h = html.Length > 8000 ? html.Substring(0, 8000).ToLowerInvariant() : html.ToLowerInvariant();

        if (h.Contains("just a moment") ||
            h.Contains("cf-chl") ||
            h.Contains("cf-browser-verification") ||
            h.Contains("checking your browser") ||
            h.Contains("attention required") ||
            h.Contains("enable javascript and cookies") ||
            h.Contains("error 1020") ||
            h.Contains("access denied"))
            return true;

        // Moi trang cua site deu chua ten domain -> trang la khong phai site
        return !h.Contains("sexhihih.blog");
    }

    public static string Abs(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        url = HttpUtility.HtmlDecode(url.Trim());

        if (url.StartsWith("//"))
            return "https:" + url;

        if (url.StartsWith("/"))
            return SiteHost.TrimEnd('/') + url;

        return url;
    }

    static string Attr(string attrs, string name)
    {
        if (string.IsNullOrEmpty(attrs))
            return null;

        var m = Regex.Match(attrs, "(?<![\\w-])" + name + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return null;

        s = Regex.Replace(s, "<[^>]+>", " ");
        s = HttpUtility.HtmlDecode(s);
        s = Regex.Replace(s, "\\s+", " ").Trim();

        return s;
    }
}
