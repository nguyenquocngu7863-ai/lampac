using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace ThichSex;

// ThichSex.org — WordPress (theme phimsex69sexdep), HTML server-side.
// Da do 2026-11-14 (capture_oneshot + curl_cffi chrome124):
//   card : <div id="post-N" class="item cf ..."><div class="thumb">
//          <a class="clip-link" data-id="N" title="TEN" href=".../slug/">
//          <img class="lazy" data-src="...240x180.jpg" alt="TEN">
//          -> site KHONG in duration / badge nao tren card list
//             => time / quality de null (luat 3 truong, khong bi a).
//   phan trang: <span class='pages'>Trang 1 tren 179</span> + /page/N/,
//               category /<slug>/page/N/, tag /tag/<slug>/page/N/,
//               search /page/N/?s=<q> (WordPress ?s=, KHONG phai /search/).
//   sort  : ?orderby=<date|modified|title|rand>&order=<desc|asc> doi THU TU,
//           giu nguyen "Tren 179/39/13" (total khong doi) o home, category,
//           tag, search va ca trang 2 => SORT that (skill lampac-sort).
//           Loai: ?orderby=views -> 1 item (29256B), ?orderby=comment_count
//           doi it/id dau giong home -> khong dua vao whitelist.
//   player: detail <iframe data-src="https://sv2.uptobox-ggcdn.top/player.php?slug=HASH">
//           embed JW Player setup sources:[{"file":"https://sv3.uptobox-ggcdn.top/
//           eee99/HASH.m3u8","type":"hls"}] -> lay truc tiep, khong doan path.
//           Mot so bai cu dung embed.php?url=<id> (xcdnx.cdn-xvideos-xnxx.xyz)
//           nhung JSON-LD van co embedUrl player.php?slug=<post-id>; ca 2 deu
//           di qua PlayerUrl() (uu tien iframe data-src, fallback embedUrl).
public static class ThsTo
{
    public static string SiteHost = "https://thichsex.org";
    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    public const string PlayerHost = "https://sv2.uptobox-ggcdn.top";

    // CF relay (env THICHSEX_RELAY): CDN lh3-ggcontent.top chan TLS .NET bang
    // Cloudflare (403), di qua Worker egress CF thi 200. Tat: THICHSEX_RELAY=''
    public static string RelayBase =
        System.Environment.GetEnvironmentVariable("THICHSEX_RELAY")?.TrimEnd('/');

    public static string Relay(string url)
    {
        if (string.IsNullOrEmpty(RelayBase) || string.IsNullOrEmpty(url)
            || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.StartsWith(RelayBase, StringComparison.OrdinalIgnoreCase))
            return url;

        try
        {
            var u = new Uri(url);
            return $"{RelayBase}/{u.Host}{u.PathAndQuery}";
        }
        catch
        {
            return url;
        }
    }

    // ===== SORT (do that, giu total, doi sequence) =====
    public static readonly (string id, string name)[] Sorts =
    {
        ("",            "Mới nhất"),
        ("date",        "Cũ nhất"),
        ("modified",    "Mới cập nhật"),
        ("title",       "Theo tên A-Z"),
        ("rand",        "Ngẫu nhiên"),
    };

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return "";
        sort = sort.Trim().ToLowerInvariant();
        foreach (var s in Sorts)
            if (s.id == sort) return sort;
        return null;                       // sort la -> null, dung de clamp
    }

    // Sort that o MOI context da do (home / category / tag / search / pg2).
    public static (string id, string name)[] SortsFor(string search, string c) => Sorts;

    public static string ClampSort(string sort, string search, string c)
    {
        string n = NormalizeSort(sort);
        if (n != null)
        {
            foreach (var s in SortsFor(search, c))
                if (s.id == n) return n;
        }
        return "";                         // mac dinh cua site
    }

    public static string SortLabel(string sort)
    {
        foreach (var s in Sorts)
            if (s.id == (sort ?? "")) return s.name;
        return "Mới nhất";
    }

    static string SortQuery(string sort)
    {
        if (string.IsNullOrEmpty(sort)) return "";
        return sort switch
        {
            "date"     => "orderby=date&order=asc",
            "modified" => "orderby=modified&order=desc",
            "title"    => "orderby=title&order=asc",
            "rand"     => "orderby=rand",
            _          => ""
        };
    }

    // ===== URL =====
    // c nhan: "phim-sex-nhat-ban" | "tag/phim-sex-vn" | URL day du cua site.
    // Luon co trailing slash; chi gan /page/N/ khi pg > 1.
    public static string ListUri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host)) host = SiteHost;
        if (pg < 1) pg = 1;
        string sq = SortQuery(sort ?? "");

        // Search WordPress: /page/N/?s=<q> (&orderby=... sau cung).
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = HttpUtility.UrlEncode(search.Trim()).Replace("%20", "+");
            string url = pg > 1
                ? host + "/page/" + pg + "/?s=" + q
                : host + "/?s=" + q;
            return sq.Length == 0 ? url : url + "&" + sq;
        }

        string path;
        if (!string.IsNullOrWhiteSpace(c))
        {
            string v = c.Trim();
            if (v.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var u = new Uri(v);
                path = u.AbsolutePath.TrimEnd('/') + "/";
            }
            else
            {
                // Chặn SSRF: chi nhan path tuong doi sach, khong cho .. / scheme.
                v = v.Trim('/').TrimStart('/');
                if (v.Contains("..") || v.Contains("://") || v.Contains('?') || v.Contains('#'))
                    v = "";
                path = string.IsNullOrEmpty(v) ? "/" : "/" + v + "/";
            }
        }
        else
        {
            path = "/";
        }

        string baseUrl = path == "/" ? host + "/" : host + path;
        if (pg > 1)
            baseUrl += "page/" + pg + "/";

        if (sq.Length == 0)
            return baseUrl;
        return baseUrl + (baseUrl.Contains("?") ? "&" : "?") + sq;
    }

    // Chi nhan URL hoac path thuoc host cua site (chong SSRF cho genres/rel).
    public static string NormalizePageUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        uri = HttpUtility.HtmlDecode(uri.Trim());
        int qm = uri.IndexOfAny(new[] { '?', '#' });
        if (qm >= 0) uri = uri.Substring(0, qm);

        if (!uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            uri = SiteHost + (uri.StartsWith("/") ? "" : "/") + uri;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return null;
        if (!string.Equals(u.Host, "thichsex.org", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(u.Host, "www.thichsex.org", StringComparison.OrdinalIgnoreCase))
            return null;
        if (u.Scheme != "https" && u.Scheme != "http") return null;

        string p = u.AbsolutePath;
        if (p.Length < 2 || p == "/") return null;       // phai la trang bai viet
        return SiteHost + p.TrimEnd('/') + "/";
    }

    static string Abs(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        url = HttpUtility.HtmlDecode(url);
        if (url.StartsWith("//")) return "https:" + url;
        if (url.StartsWith("/")) return SiteHost + url;
        return url;
    }

    // ===== LIST =====
    // Tach theo "<div id=\"post-" de lay dung 1 card, regex ben trong.
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string vidosik, string html)
    {
        var list = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in html.Split(new[] { "<div id=\"post-" }, StringSplitOptions.None))
        {
            string b = raw.Length > 6000 ? raw.Substring(0, 6000) : raw;
            if (!b.Contains("class=\"item")) continue;

            var hm = Regex.Match(b, "class=\"clip-link\"[^>]*href=\"(https://thichsex\\.org/[^\"]+/)\"");
            if (!hm.Success) continue;
            string page = hm.Groups[1].Value;
            if (!seen.Add(page)) continue;

            var tm = Regex.Match(b, "class=\"clip-link\"[^>]*title=\"([^\"]+)\"");
            string name = tm.Success
                ? HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim())
                : Regex.Match(b, "class=\"entry-title\"[^>]*>\\s*<a[^>]*>([^<]+)</a>").Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            string poster = "";
            var im = Regex.Match(b, "class=\"lazy\"\\s+data-src=\"([^\"]+)\"");
            if (!im.Success)
                im = Regex.Match(b, "<img[^>]*data-src=\"([^\"]+)\"");
            if (im.Success) poster = Abs(im.Groups[1].Value);

            list.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                name = name,
                video = vidosik + "?uri=" + HttpUtility.UrlEncode(page),
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "thichsex",
                    href = page,
                    image = poster
                }
            });
        }

        return list;
    }

    // Playlist trong khoi related-posts (dung chung .item, nen cat rieng vung nay).
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(string vidosik, string html)
    {
        if (string.IsNullOrEmpty(html)) return new List<Shared.Models.SISI.Base.PlaylistItem>();
        int i = html.IndexOf("related-posts", StringComparison.Ordinal);
        if (i < 0) return new List<Shared.Models.SISI.Base.PlaylistItem>();
        return Playlist(vidosik, html.Substring(i));
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html)) return 1;
        var m = Regex.Match(html, "class='pages'>Trang\\s*\\d+\\s*tr[eê]n\\s*(\\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(html, "class=\"pages\">Trang\\s*\\d+\\s*tr[eê]n\\s*(\\d+)", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int p) && p > 0)
            return p;
        return 1;
    }

    // ===== PLAYER =====
    // Tra ve NHIEU ung vien (thu lan luot): bai moi dung iframe data-src voi
    // player.php?slug=<hash 32 hex> hoac embed.php?url=<id>; bai cu chi co
    // JSON-LD embedUrl (player.php?slug=<post-id>). Mot so bai embed.php tra
    // sources file rong -> caller phai fallback sang ung vien ke tiep.
    public static List<string> PlayerUrls(string html)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(html)) return list;

        void add(string u)
        {
            if (string.IsNullOrEmpty(u)) return;
            u = HttpUtility.HtmlDecode(u).Replace("\\/", "/");
            if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
            if (!list.Contains(u)) list.Add(u);
        }

        foreach (Match m in Regex.Matches(html,
            "<iframe[^>]*data-src=\"(https?://[^\"]*(?:player|embed)\\.php\\?[^\"]+)\"", RegexOptions.IgnoreCase))
            add(m.Groups[1].Value);

        foreach (Match m in Regex.Matches(html,
            "\"embedUrl\"\\s*:\\s*\"([^\"]*(?:player|embed)\\.php\\?[^\"]+)\"", RegexOptions.IgnoreCase))
            add(m.Groups[1].Value);

        return list;
    }

    public static string PlayerUrl(string html)
        => PlayerUrls(html).FirstOrDefault();

    // embed JW Player: sources: [{"file":"...m3u8","label":"0","type":"hls"}]
    public static string MasterUrl(string embedHtml)
    {
        if (string.IsNullOrEmpty(embedHtml)) return null;
        var m = Regex.Match(embedHtml, "\"file\"\\s*:\\s*\"(https?://[^\"]+\\.m3u8[^\"]*)\"", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string u = Regex.Unescape(m.Groups[1].Value);
        return u.StartsWith("http") ? u : null;
    }

    // ===== TAXONOMY (menu dong 3+, lay dong tu trang chu, co fallback tinh) =====
    public static List<(string name, string slug)> Categories(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            "href=\"https://thichsex\\.org/(phim-sex-[a-z0-9-]+)/\"[^>]*>([^<]{2,60})</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value;
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (!seen.Add(slug)) continue;
            list.Add((name, slug));
        }
        if (list.Count > 0) return list;

        // fallback: sitemap co them 2 muc khong co trong nav
        return FallbackCategories();
    }

    public static List<(string name, string slug)> Tags(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            "class=\"btntag\"\\s+href=\"https://thichsex\\.org/tag/([a-z0-9-]+)/\">([^<]+)</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value;
            if (!seen.Add(slug)) continue;
            list.Add((HttpUtility.HtmlDecode(m.Groups[2].Value.Trim()), slug));
        }
        return list;
    }

    public static List<(string name, string slug)> FallbackCategories() => new()
    {
        ("Sex Trung Quốc", "phim-sex-trung-quoc"),
        ("Sex Gay", "phim-sex-gay"),
        ("Việt Nam", "phim-sex-viet-nam"),
        ("Sex Hàn Quốc", "phim-sex-han-quoc"),
        ("Sex Châu Âu", "phim-sex-chau-au"),
        ("Sex Thái Lan", "phim-sex-thai-lan"),
        ("Sex Nhật Bản", "phim-sex-nhat-ban"),
        ("Sex Les", "phim-sex-les"),
        ("Không Che", "phim-sex-khong-che"),
        ("Vietsub", "phim-sex-vietsub"),
    };

    // menu = head (dong 1 Tim kiem + dong 2 Sap xep, phu thuoc context)
    //        + base (dong 3+ The loai / The, khong phu thuoc context).
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(string host, string search, string c, string sort)
    {
        string root = host + "/thichsex";
        string ctx = !string.IsNullOrWhiteSpace(search)
            ? "search=" + HttpUtility.UrlEncode(search)
            : !string.IsNullOrWhiteSpace(c) ? "c=" + HttpUtility.UrlEncode(c) : "";

        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };

        string link(string s)
        {
            string q = ctx.Length == 0 ? "" : ctx + "&";
            return root + "?" + q + "sort=" + s;
        }

        menu.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = "Sắp xếp: " + SortLabel(sort),
            playlist_url = "submenu",
            submenu = SortsFor(search, c)
                .Select(s => new Shared.Models.SISI.Base.MenuItem(s.name, link(s.id)))
                .ToList()
        });

        return menu;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> MenuBase(string host,
        List<(string name, string slug)> cats, List<(string name, string slug)> tags)
    {
        string root = host + "/thichsex";
        var menu = new List<Shared.Models.SISI.Base.MenuItem>();

        if (cats != null && cats.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = cats.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(x.name, root + "?c=" + HttpUtility.UrlEncode(x.slug))).ToList()
            });
        }

        if (tags != null && tags.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thẻ",
                playlist_url = "submenu",
                submenu = tags.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(x.name, root + "?c=" + HttpUtility.UrlEncode("tag/" + x.slug))).ToList()
            });
        }

        return menu;
    }

    // ===== POPUP CHIPS (1 fetch detail) =====
    // rel="category tag" -> kind "cat"; rel="tag" -> kind "tag".
    // Bo qua link nav/footer (khong co rel) va related-posts (rel="bookmark").
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "href=\"https://thichsex\\.org/(?:tag/|)([a-z0-9-]+)/\"\\s+rel=\"(category tag|tag)\"[^>]*>([^<]+)</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value;
            string kind = m.Groups[2].Value.IndexOf("category", StringComparison.OrdinalIgnoreCase) >= 0 ? "cat" : "tag";
            string name = HttpUtility.HtmlDecode(m.Groups[3].Value.Trim());
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name)) continue;
            if (!seen.Add(kind + "/" + slug)) continue;
            list.Add((kind, slug, name));
        }
        return list;
    }
}
