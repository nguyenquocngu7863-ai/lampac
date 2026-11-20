using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using Shared.Models.SISI.Base;

namespace Gapedk;

public static class GapedkTo
{
    public static string SiteHost = "https://www.gapedk.com";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // ===== URL =====
    // home      : /            | /page/N/
    // the loai  : /genre/<slug>/ | /genre/<slug>/page/N/
    // hang      : /director/<slug>/ | .../page/N/
    // dien vien : /pornstar/<slug>/ | .../page/N/
    // search    : /index.php?s=<q>   (site KHONG phan trang search)
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
            return host + "/index.php?s=" + HttpUtility.UrlEncode(search.Trim());

        if (!string.IsNullOrEmpty(c))
        {
            string path = c.Trim().Trim('/');
            return host + "/" + path + (pg > 1 ? "/page/" + pg : "") + "/";
        }

        return host + "/" + (pg > 1 ? "page/" + pg + "/" : "");
    }

    // ===== playlist =====
    // <div class="cont_peliculas">
    //   <div class="peliculas_box" style="position:relative">
    //     <a href="URL" ><img src="IMG" alt="TEN" title="TEN"></a><br>
    //     <div class="titulope">TEN</div>
    //     <div class="pelinfo">...genre...</div>
    // Card trang detail (khoi RELATED) dung "global_box", cung hinh dang.
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var playlists = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html,
            "<div class=\"(?:peliculas|global)_box\"[^>]*>\\s*<a\\s+href=\"([^\"]+)\"\\s*><img\\s+src=\"([^\"]+)\"",
            RegexOptions.Singleline))
        {
            string href = m.Groups[1].Value.Trim();
            if (href.StartsWith("/"))
                href = SiteHost + href;
            if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seen.Add(href))
                continue;

            string picture = m.Groups[2].Value.Trim();
            if (picture.StartsWith("//"))
                picture = "https:" + picture;
            else if (picture.StartsWith("/"))
                picture = SiteHost + picture;

            int win = Math.Min(1500, html.Length - m.Index);
            var tm = Regex.Match(html.Substring(m.Index, win),
                "<div class=\"titulope\">([^<]{1,300})</div>");
            if (!tm.Success)
                continue;

            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            playlists.Add(new PlaylistItem
            {
                name = name,
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                picture = picture,
                json = true,
                bookmark = new Bookmark
                {
                    site = "gapedk",
                    href = href,
                    image = picture
                }
            });
        }

        return playlists;
    }

    // <a href=".../genre/milf/page/225/" class="last" title="Last">225</a>
    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html, "/page/(\\d+)/"))
        {
            if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)
                && p > max)
                max = p;
        }

        return max > 5000 ? 5000 : max;
    }

    // ===== taxonomy (genres / studios) lay dong tu trang chu =====
    // Nav:  <li class="menu-item"><a href="/genre/<slug>/" class="item-link">Ten</a></li>
    // Card: <a href="https://www.gapedk.com/genre/<slug>/" rel="category tag">Ten</a>
    public static List<(string slug, string name)> Taxonomy(string html, string kind)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string slug, string name)
        {
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name))
                return;
            if (nameOf.ContainsKey(slug))
                return;
            nameOf[slug] = name;
            list.Add((slug, name));
        }

        // nav (genre + director)
        foreach (Match m in Regex.Matches(html,
            "<a\\s+href=\"/" + kind + "/([^\"\\s/]+)/?\"\\s+class=\"item-link\">([^<]{1,120})</a>",
            RegexOptions.IgnoreCase))
        {
            Add(m.Groups[1].Value.Trim(), HttpUtility.HtmlDecode(m.Groups[2].Value.Trim()));
        }

        // the tag tren card (chi genre moi co)
        if (kind == "genre")
        {
            foreach (Match m in Regex.Matches(html,
                "href=\"(?:https?://[^\"/]+)?/genre/([^\"\\s/]+)/?\"\\s+rel=\"category tag\">([^<]{1,120})</a>",
                RegexOptions.IgnoreCase))
            {
                Add(m.Groups[1].Value.Trim(), HttpUtility.HtmlDecode(m.Groups[2].Value.Trim()));
            }
        }

        return list;
    }

    // ===== MENU (khong co sort tren site -> khong co dong 2) =====
    public static List<MenuItem> Menu(
        string host,
        List<(string slug, string name)> genres,
        List<(string slug, string name)> studios)
    {
        host = host.TrimEnd('/');

        var menu = new List<MenuItem>
        {
            new MenuItem
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/gapedk"
            }
        };

        if (genres != null && genres.Count > 0)
        {
            menu.Add(new MenuItem
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres.ConvertAll(g => new MenuItem(
                    g.name,
                    host + "/gapedk?c=" + HttpUtility.UrlEncode("genre/" + g.slug)))
            });
        }

        if (studios != null && studios.Count > 0)
        {
            menu.Add(new MenuItem
            {
                title = "Hãng phim",
                playlist_url = "submenu",
                submenu = studios.ConvertAll(s => new MenuItem(
                    s.name,
                    host + "/gapedk?c=" + HttpUtility.UrlEncode("director/" + s.slug)))
            });
        }

        return menu;
    }

    // ===== detail =====
    public static string Title(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var m = Regex.Match(html, "<div class=\"titlex\">([^<]{1,300})</div>");
        return m.Success ? HttpUtility.HtmlDecode(m.Groups[1].Value.Trim()) : null;
    }

    public static string Poster(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var m = Regex.Match(html, "class=\"poster-movie\">.*?<img[^>]+src=\"([^\"]+)\"",
            RegexOptions.Singleline);
        if (!m.Success)
            return null;
        string p = m.Groups[1].Value.Trim();
        if (p.StartsWith("//"))
            p = "https:" + p;
        return p;
    }

    // Links trong 1 khoi detail: peligen (genre), pelinfoe (studio), pelinfop (dien vien)
    public static List<(string slug, string name)> Links(string html, string kind, string blockClass)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var b = Regex.Match(html,
            "<div class=\"" + blockClass + "\"[^>]*>(.*?)</div>",
            RegexOptions.Singleline);
        if (!b.Success)
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(b.Groups[1].Value,
            "href=\"(?:https?://[^\"/]+)?/" + kind + "/([^\"\\s/]+)/?\"[^>]*>([^<]{1,200})</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name))
                continue;
            if (!seen.Add(slug))
                continue;
            list.Add((slug, name));
        }

        return list;
    }

    // ===== player =====
    // <div id="tab1" class="tab_content"><script>doo("iqsdvs2wegox")</script></div>
    // ham doo/mix/... dinh nghia trong
    // holedk.com/wp-content/themes/HoleRed/reprodk1.js
    public static List<(string fn, string id)> Players(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        foreach (Match m in Regex.Matches(html,
            "<div id=\"tab\\d+\" class=\"tab_content\"><script>([a-z]+)\\(\"([^\"]+)\"\\)</script>"))
        {
            list.Add((m.Groups[1].Value, m.Groups[2].Value));
        }

        return list;
    }

    // Chi DoodStream: MXDrop (mxdrop.to) resolve duoc URL nhung CDN
    // mxcontent.net chan TLS/UA cua .NET (403 nginx) -> server khong phat duoc
    // qua /proxy nen an khoi /vidosik.
    public static string PlayerLabel(string fn) => fn switch
    {
        "doo" => "DoodStream",
        _ => null
    };

    public static string EmbedUrl(string fn, string id) => fn switch
    {
        "doo" => "https://playmogo.com/e/" + id + "/",
        _ => null
    };

    // ===== DoodStream (playmogo.com) =====
    // $.get('/pass_md5/<hash>/<token>', function(data){ ... })   -> data = base URL
    // src = data + makePlay(); makePlay = 10 ky tu ngau nhien + "?token=<tok>&expiry=" + Date.now()
    public static string DoodPath(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var m = Regex.Match(html, "get\\(\\s*'(/pass_md5/[^']+)'");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string DoodToken(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var m = Regex.Match(html, "\\?token=([A-Za-z0-9]+)&expiry=");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string DoodSource(string passUrl, string token)
    {
        if (string.IsNullOrEmpty(passUrl) || string.IsNullOrEmpty(token))
            return null;

        string rnd = "";
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        for (int i = 0; i < 10; i++)
            rnd += chars[Random.Shared.Next(chars.Length)];

        long expiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return passUrl.Trim() + rnd + "?token=" + token + "&expiry=" + expiry.ToString(CultureInfo.InvariantCulture);
    }
}
