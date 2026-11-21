using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace JavCc;

public static class JavCcTo
{
    public static string SiteHost = "https://javcc.me";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // ===== URL builder =====
    // Site: WordPress + Retrotube. Phan trang luon la /page/N (khong phai ?paged=).
    //   home            -> /
    //   category        -> /category/<slug>
    //   actor           -> /actor/<slug>
    //   studio          -> /studios/<slug>
    //   tag             -> /tag/<slug>
    //   search          -> /?s=<q>
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        string page = pg > 1 ? "/page/" + pg : "";

        if (!string.IsNullOrWhiteSpace(search))
            return host + "/?s=" + HttpUtility.UrlEncode(search.Trim())
                + (pg > 1 ? "&pg=" + pg : "");

        if (!string.IsNullOrWhiteSpace(c))
        {
            string path = c.Trim('/');

            // c co the la path day du (category/censored) hoac URL
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return path + page;

            return host + "/" + path + page;
        }

        return host + "/" + page;
    }

    // ===== list card =====
    // <article data-post-id="..." class="loop-video thumb-block ...">
    //   <a href="https://javcc.me/dsod-142" title="...">
    //     <div class="post-thumbnail">
    //       <div class="post-thumbnail-container"><img data-src="https://pic.../x.jpg" alt="..."></div>
    //       <span class="views"><i class="fa fa-eye"></i> 325</span>
    //       <span class="duration"><i class="fa fa-clock-o"></i>02:50:00</span>
    //     </div>
    //     <header class="entry-header"><span>TITLE</span></header>
    //   </a>
    // </article>
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();

        foreach (string raw in html.Split(new[] { "<article " }, StringSplitOptions.None))
        {
            string b = raw.Length > 6000 ? raw.Substring(0, 6000) : raw;

            // chi lay card phim that (co data-post-id + href detail)
            if (!b.Contains("data-post-id="))
                continue;

            var hm = Regex.Match(b, @"<a\s+href=""(https?://[^""]*javcc\.me/[^""]+)""");
            if (!hm.Success)
                continue;

            string href = hm.Groups[1].Value;
            // bo qua actor/studio/tag/category/page link
            if (Regex.IsMatch(href, @"javcc\.me/(actor|studios|studios|tag|category|page)/"))
                continue;
            if (href.Contains("/page/"))
                continue;
            if (!seen.Add(href))
                continue;

            // ten: uu tien <span> trong entry-header, fallback title attr
            string name = null;
            var tm = Regex.Match(b, @"<header class=""entry-header"">\s*<span>([^<]{2,400})</span>");
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
            {
                var at = Regex.Match(b, @"<a\s+href=""[^""]+""\s+title=""([^""]{2,400})""");
                if (at.Success)
                    name = HttpUtility.HtmlDecode(at.Groups[1].Value.Trim());
            }
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, @"data-src=""([^""]+)""");
            if (im.Success)
            {
                poster = im.Groups[1].Value;
                if (poster.StartsWith("//"))
                    poster = "https:" + poster;
                else if (poster.StartsWith("/"))
                    poster = SiteHost + poster;
            }

            // time = duration site in (mm:ss / hh:mm:ss). Khong co -> null.
            string time = null;
            var dm = Regex.Match(b, @"<span class=""duration"">(?:<i[^>]*></i>)?\s*([0-9:]{4,12})\s*</span>");
            if (dm.Success)
                time = dm.Groups[1].Value.Trim();

            // quality: site khong in badge chat luong tren card -> null (khong bia)
            var item = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = time,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javcc",
                    href = href,
                    image = poster
                }
            };

            playlists.Add(item);
        }

        return playlists;
    }

    // ===== so trang =====
    // <div class="pagination"><ul><li><a href=".../page/6655" class="inactive">6655</a>
    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html, @"/page/(\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;
        }

        return max;
    }

    // ===== player =====
    // Trang detail KHONG co <iframe>/<source>. Player nam trong:
    //   <script data-rocketlazyloadscript='data:text/javascript;base64,<B64>'>
    //   -> var defaultUrl = "https://x.videplay.us/x/<id>";
    // Trang embed x.videplay.us chua:
    //   function realembed(){ ... src="https://api.videplay.us/o/v-master.m3u8/<TOKEN>" ... }
    // => lay thang master m3u8 tu embed, khong can giai AES.
    public static string EmbedUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        // 1) base64 trong data-rocketlazyloadscript
        var m = Regex.Match(html,
            @"data-rocketlazyloadscript='data:text/javascript;base64,([A-Za-z0-9+/=]+)'");
        if (m.Success)
        {
            try
            {
                string js = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(m.Groups[1].Value));

                var u = Regex.Match(js, @"defaultUrl\s*=\s*""([^""]+)""");
                if (u.Success)
                    return u.Groups[1].Value.Trim();
            }
            catch { }
        }

        // 2) fallback: co the site doi sang nhung thang
        var d = Regex.Match(html, @"https?://[a-z0-9.\-]*videplay\.[a-z]+/x/[A-Za-z0-9]+");
        if (d.Success)
            return d.Value;

        return null;
    }

    // master m3u8 nam san trong embed (iframe src)
    public static string MasterFromEmbed(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html,
            @"https?://[a-z0-9.\-]*videplay\.[a-z]+/o/v-master\.m3u8/[A-Za-z0-9+/=]+");
        if (m.Success)
            return m.Value;

        // fallback: co the la /o/<...>.m3u8
        var m2 = Regex.Match(html,
            @"https?://[a-z0-9.\-]*videplay\.[a-z]+/o/[^""'\s]+\.m3u8[^""'\s]*");
        return m2.Success ? m2.Value : null;
    }

    // ===== popup chips =====
    // <div id="video-actors"><i class="fa fa-star"></i> Pornstars:
    //   <a href="https://javcc.me/actor/<slug>" title="NAME">NAME</a></div>
    // <div id="video-actors"><i class="fa fa-film"></i> Studios:
    //   <a href="https://javcc.me/studios/<slug>" title="NAME">NAME</a></div>
    // <div class="tags"><div class="tags-list">
    //   <a href=".../category/<slug>" class="label"><i class="fa fa-folder-open"></i>NAME</a>
    //   <a href=".../tag/<slug>" class="label"><i class="fa fa-tag"></i>NAME</a>
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>();

        void Add(string kind, string slug, string name)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(name))
                return;
            if (!seen.Add(kind + ":" + slug))
                return;
            list.Add((kind, slug, HttpUtility.HtmlDecode(name.Trim())));
        }

        // actor
        foreach (Match m in Regex.Matches(html,
            @"href=""https?://[^""]*javcc\.me/actor/([^""]+)""[^>]*>([^<]{1,80})<"))
            Add("actor", m.Groups[1].Value, m.Groups[2].Value);

        // studio
        foreach (Match m in Regex.Matches(html,
            @"href=""https?://[^""]*javcc\.me/studios/([^""]+)""[^>]*>([^<]{1,80})<"))
            Add("studio", m.Groups[1].Value, m.Groups[2].Value);

        // category (icon folder)
        foreach (Match m in Regex.Matches(html,
            @"href=""https?://[^""]*javcc\.me/category/([^""]+)""[^>]*class=""label""[^>]*>(?:<i[^>]*></i>)?([^<]{1,80})<"))
            Add("category", m.Groups[1].Value, m.Groups[2].Value);

        // tag (icon tag)
        foreach (Match m in Regex.Matches(html,
            @"href=""https?://[^""]*javcc\.me/tag/([^""]+)""[^>]*class=""label""[^>]*>(?:<i[^>]*></i>)?([^<]{1,80})<"))
            Add("tag", m.Groups[1].Value, m.Groups[2].Value);

        return list;
    }

    // "Video lien quan" — block cuoi trang detail
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(string uri, string html)
    {
        return Playlist(uri, html);
    }

    public static List<(string name, string slug, int count)> Taxonomy(string json)
    {
        var list = new List<(string, string, int)>();
        if (string.IsNullOrEmpty(json))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                string slug = item.TryGetProperty("slug", out var s) ? s.GetString() : null;
                int count = item.TryGetProperty("count", out var c) && c.TryGetInt32(out int value) ? value : 0;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(slug))
                    list.Add((HttpUtility.HtmlDecode(name), slug, count));
            }
        }
        catch { }

        return list;
    }

    // ===== menu =====
    // Site KHONG co sort (khong tham so sort nao trong HTML, da kiem ca home/category/search)
    // -> KHONG co dong 2 "Sap xep". Dung luat: khong lam con hon lam sai.
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host,
        List<(string name, string slug, int count)> categories = null,
        List<(string name, string slug, int count)> tags = null)
    {
        host = host.TrimEnd('/');
        string root = host + "/javcc";

        static List<Shared.Models.SISI.Base.MenuItem> Items(
            string root, string kind, List<(string name, string slug, int count)> source)
        {
            return source.OrderByDescending(x => x.count)
                .ThenBy(x => x.name, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .Select(x => new Shared.Models.SISI.Base.MenuItem(
                    x.name, root + "?c=" + HttpUtility.UrlEncode(kind + "/" + x.slug)))
                .ToList();
        }

        categories ??= new List<(string, string, int)>();
        tags ??= new List<(string, string, int)>();

        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm", search_on = "search_on", playlist_url = root
            }
        };

        var categoryItems = Items(root, "category", categories);
        categoryItems.Insert(0, new("Mới nhất", root));
        menu.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = "Thể loại",
            playlist_url = "submenu",
            submenu = categoryItems
        });

        var tagItems = Items(root, "tag", tags);
        if (tagItems.Count > 0)
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thẻ",
                playlist_url = "submenu",
                submenu = tagItems
            });

        return menu;
    }
}
