using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace SexVietTuyChon;

public static class SexVietTuyChonTo
{
    // 2026-11-24: sexviettuyenchon.com da 301 -> dongsexviet.com.
    // POST /api/player tren host cu tra 404 (redirect bien POST thanh GET)
    // => phai goi API tren origin that cua trang detail.
    public const string SiteHost = "https://dongsexviet.com";

    // origin (scheme://host) cua mot URL bat ky trong site
    public static string Origin(string url)
    {
        if (string.IsNullOrEmpty(url))
            return SiteHost;
        if (System.Uri.TryCreate(url, System.UriKind.Absolute, out var u))
            return u.Scheme + "://" + u.Host;
        return SiteHost;
    }

    public const string ChromeUA =
        "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    // ===== DANH MUC (menu 2 tang, lay tu nav /list-menu cua site) =====
    public static readonly (string name, string slug)[] Categories =
    {
        ("Phim Sex Việt Nam", "phim-sex-viet-nam"),
        ("Phim Sex Nhật Bản", "phim-sex-nhat-ban"),
        ("Phim Sex Trung Quốc", "phim-sex-trung-quoc"),
        ("Phim Sex Châu Âu", "phim-sex-chau-au"),
        ("Phim Sex Vietsub", "phim-sex-vietsub"),
    };

    // search: form GET -> slugify bỏ dấu tiếng Việt, lowercase, space -> '-'
    // (xem assets/js/media.js: slugify($("#keyword").val()).toLowerCase())
    static string SlugifyVi(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim().ToLowerInvariant();
        string[][] map =
        {
            new[] { "a", "àáạảãâầấậẩẫăằắặẳẵ" },
            new[] { "e", "èéẹẻẽêềếệểễ" },
            new[] { "i", "ìíịỉĩ" },
            new[] { "o", "òóọỏõôồốộổỗơờớợởỡ" },
            new[] { "u", "ùúụủũưừứựửữ" },
            new[] { "y", "ỳýỵỷỹ" },
            new[] { "d", "đ" },
        };
        foreach (var pair in map)
            foreach (char ch in pair[1])
                s = s.Replace(ch.ToString(), pair[0]);
        s = Regex.Replace(s, @"[^a-z0-9\s-]", " ");
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    // / (home)          -> /                | /trang/2/
    // /phim-sex-viet-nam/ -> /phim-sex-viet-nam/trang/2/
    // /search/<slug>/   -> /search/<slug>/trang/2/
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = SlugifyVi(search);
            if (string.IsNullOrEmpty(slug))
                slug = HttpUtility.UrlEncode(search.Trim());

            return host + "/search/" + slug + "/"
                + (pg > 1 ? "trang/" + pg + "/" : "");
        }

        if (!string.IsNullOrEmpty(c))
        {
            c = c.Trim('/');
            return host + "/" + c + "/"
                + (pg > 1 ? "trang/" + pg + "/" : "");
        }

        return host + "/"
            + (pg > 1 ? "trang/" + pg + "/" : "");
    }

    // ===== list card =====
    // <li class="item-movie">
    //   <a title="..." href="https://.../<slug>.html">
    //     <img ... data-original="https://domain115.com/.../hinh.jpg" alt="...">
    //     <div class="label">VN|VIETSUB</div>
    //     <div class="title-movie">TEN</div>
    //   </a>
    // </li>
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(
        string uri, string html)
    {
        var list = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in html.Split(
            new[] { "<li class=\"item-movie\"" }, StringSplitOptions.None))
        {
            var hm = Regex.Match(raw,
                "<a\\s+title=\"([^\"]*)\"\\s+href=\"(https?://[^\"]+\\.html)\"");
            string href, title;
            if (hm.Success)
            {
                title = hm.Groups[1].Value;
                href = hm.Groups[2].Value;
            }
            else
            {
                var hm2 = Regex.Match(raw, "href=\"(https?://[^\"]+\\.html)\"");
                if (!hm2.Success)
                    continue;
                href = hm2.Groups[1].Value;
                title = null;
            }

            if (!href.StartsWith("http"))
                continue;
            if (!seen.Add(href))
                continue;

            // ten: uu tien title="" cua the <a>, sau do .title-movie
            if (string.IsNullOrWhiteSpace(title))
            {
                var tm = Regex.Match(raw,
                    "<div class=\"title-movie\">([^<]*)</div>");
                title = tm.Success ? tm.Groups[1].Value : null;
            }
            if (string.IsNullOrWhiteSpace(title))
                continue;
            title = HttpUtility.HtmlDecode(title.Trim());

            string picture = "";
            var im = Regex.Match(raw, "data-original=\"([^\"]+)\"");
            if (im.Success)
                picture = im.Groups[1].Value;
            if (!string.IsNullOrEmpty(picture) && picture.StartsWith("/"))
                picture = SiteHost + picture;

            // slot `time` = MOT nhan site in tren card (VN / VIETSUB)
            string label = null;
            var lm = Regex.Match(raw, "<div class=\"label\">([^<]*)</div>");
            if (lm.Success)
                label = HttpUtility.HtmlDecode(lm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(label))
                label = null;

            list.Add(new Shared.Models.SISI.Base.PlaylistItem
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = title,
                picture = picture,
                time = label,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark
                {
                    site = "sexviettuyenchon",
                    href = href,
                    image = picture
                }
            });
        }

        return list;
    }

    // tong so trang: lay max N trong /trang/N/
    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 1;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"/trang/(\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;
        return max;
    }

    // ===== detail: video id + danh sach server =====
    // <div id="video" data-video-id="4482">
    // <li class="video-server bt_active" data-server-id="1">...<i..></i> 1</li>
    public static string VideoId(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var m = Regex.Match(html,
            "id=\"video\"[^>]*?data-video-id=\"(\\d+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static List<string> Servers(string html)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(html))
            return list;
        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, "data-server-id=\"(\\d+)\""))
            if (seen.Add(m.Groups[1].Value))
                list.Add(m.Groups[1].Value);
        if (list.Count == 0)
            list.Add("1");
        return list;
    }

    // ===== /api/player (POST id + server) =====
    // jwplayer: {"status":"success","data":{"type":"jwplayer",
    //            "sources":[{"file":"https://.../output.m3u8?cdn=tt","type":"hls"}]}}
    // iframe:   {"data":{"type":"iframe","source":"https://..."}}
    public static string PlayerUrl(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        var tm = Regex.Match(json, "\"type\"\\s*:\\s*\"([^\"]+)\"");
        string type = tm.Success ? tm.Groups[1].Value : "";

        if (type == "iframe")
        {
            var sm = Regex.Match(json, "\"source\"\\s*:\\s*\"([^\"]+)\"");
            return sm.Success ? Unescape(sm.Groups[1].Value) : null;
        }

        // jwplayer (mac dinh): lay file dau tien
        var fm = Regex.Match(json, "\"file\"\\s*:\\s*\"([^\"]+)\"");
        if (!fm.Success)
            return null;
        return Unescape(fm.Groups[1].Value);
    }

    static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Replace("\\/", "/").Replace("\\u0026", "&");
    }

    // ===== popup (long-press): tags + category tu detail =====
// Khoi <div id="extras"> trong <article>: chuoi <a href=".../tag/<slug>/" title="..">
// roi 1 the category <a href=".../phim-sex-*/" title="..">.
// group: "category" | "tag" ; cpath = path sau host (dung cho ?c=<cpath>)
public static List<(string group, string name, string cpath)> Chips(string html)
{
    var list = new List<(string, string, string)>();
    if (string.IsNullOrEmpty(html))
        return list;

    int i = html.IndexOf("id=\"extras\"", StringComparison.OrdinalIgnoreCase);
    string block = i >= 0 ? html.Substring(i) : html;
    if (i >= 0)
    {
        int end = block.IndexOf("</div>", StringComparison.Ordinal);
        if (end > 0)
            block = block.Substring(0, end);
    }

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (Match m in Regex.Matches(block,
        "<a href=\"" + Regex.Escape(SiteHost) + "/([^\"]+)/\" title=\"([^\"]*)\""))
    {
        string cpath = m.Groups[1].Value.Trim();
        string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
        if (string.IsNullOrEmpty(cpath) || string.IsNullOrEmpty(name))
            continue;

        string group;
        if (cpath.StartsWith("tag/", StringComparison.OrdinalIgnoreCase))
            group = "tag";
        else if (cpath.StartsWith("phim-sex", StringComparison.OrdinalIgnoreCase))
            group = "category";
        else
            continue;

        if (seen.Add(group + "/" + cpath))
            list.Add((group, name, cpath));
    }

    return list;
}

// ===== menu (2 tang, khong co sort) =====
    // Site khong co sap xep -> BO HANG dong 2 (khong lam no-op).
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        host = host.TrimEnd('/');
        string root = host + "/sexviettuyenchon";

        var res = new List<Shared.Models.SISI.Base.MenuItem>
        {
            new()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };

        var sub = new List<Shared.Models.SISI.Base.MenuItem>(Categories.Length);
        foreach (var (name, slug) in Categories)
            sub.Add(new Shared.Models.SISI.Base.MenuItem(
                name, root + "?c=" + HttpUtility.UrlEncode(slug)));

        res.Add(new Shared.Models.SISI.Base.MenuItem
        {
            title = "Thể loại",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }
}