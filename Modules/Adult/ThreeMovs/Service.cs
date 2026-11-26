using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace ThreeMovs;

public static class ThreeMovsTo
{
    public static string SiteHost = "https://www.3movs.com";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // ===== URL =====
    // c = "latest-updates" | "top-rated/week" | "most-viewed/month" | "categories/<slug>"
    //     | "tags/<slug>" | "pornstars/<slug>" | (rong = latest-updates)
    // Sap xep + so trang nam SAU path: /categories/anal/most-viewed/month/3/
    // Search: /search_videos/<sort>/?q=...  (page luon = 1, site khong phan trang search)
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string sp = string.IsNullOrWhiteSpace(sort) ? "" : sort.Trim().Trim('/') + "/";
            return host + "/search_videos/" + sp + "?q=" + HttpUtility.UrlEncode(search.Trim());
        }

        string path = string.IsNullOrWhiteSpace(c) ? "latest-updates" : c.Trim().Trim('/');
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            path = path.Substring(path.IndexOf("//", StringComparison.Ordinal) + 2);
        if (path.StartsWith("www.3movs.com/", StringComparison.OrdinalIgnoreCase))
            path = path.Substring("www.3movs.com/".Length);
        path = path.Trim('/');

        string url = host + "/" + path + "/";
        if (!string.IsNullOrWhiteSpace(sort))
            url += sort.Trim().Trim('/') + "/";
        if (pg > 1)
            url += pg + "/";

        return url;
    }

    // ===== danh sach video =====
    // Card: <div class="item thumb"> <a class="wrap_image" href="/videos/469113/slug/" title="TEN">
    //       <img data-src="..320x180/1.jpg" data-webp="..321x181/1.jpg" data-preview="..preview.mp4">
    //       <div class="time">8:00</div></a> <a class="title" href="..">TEN</a>
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string route, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();

        foreach (string raw in html.Split(new[] { "<div class=\"item thumb" }, StringSplitOptions.None))
        {
            string b = raw.Length > 4000 ? raw.Substring(0, 4000) : raw;

            var hm = Regex.Match(b, "href=\"(https?://www\\.3movs\\.com/videos/\\d+/[^\"]*)\"");
            if (!hm.Success)
                continue;
            string href = HttpUtility.HtmlDecode(hm.Groups[1].Value);

            if (!seen.Add(href))
                continue;

            string name = "";
            var tm = Regex.Match(b, "<a class=\"title\"[^>]*title=\"([^\"]+)\"");
            if (!tm.Success)
                tm = Regex.Match(b, "<a class=\"wrap_image\"[^>]*title=\"([^\"]+)\"");
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
            {
                var nm = Regex.Match(b, "<a class=\"title\"[^>]*>\\s*([^<]{2,200}?)\\s*</a>");
                if (nm.Success)
                    name = HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim());
            }
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var pm = Regex.Match(b, "data-webp=\"([^\"]+)\"");
            if (!pm.Success)
                pm = Regex.Match(b, "data-src=\"([^\"]+)\"");
            if (pm.Success)
                poster = HttpUtility.HtmlDecode(pm.Groups[1].Value);

            string preview = "";
            var vm = Regex.Match(b, "data-preview=\"([^\"]+)\"");
            if (vm.Success)
                preview = HttpUtility.HtmlDecode(vm.Groups[1].Value);

            string dur = "";
            var dm = Regex.Match(b, "class=\"time\">\\s*([^<]{1,20}?)\\s*</div>");
            if (dm.Success)
                dur = HttpUtility.HtmlDecode(dm.Groups[1].Value.Trim());

            var item = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = dur,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "threemovs",
                    href = href,
                    image = poster
                }
            };
            if (!string.IsNullOrEmpty(preview))
                item.preview = preview;

            playlists.Add(item);
        }

        return playlists;
    }

    // Khoi phan trang co id rieng: cat truoc "column_aside" de khong lan sang sidebar
    static string PaginationBlock(string html)
    {
        int i = html.IndexOf("list_videos_common_videos_list_pagination", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return "";

        int len = Math.Min(8000, html.Length - i);
        string slice = html.Substring(i, len);

        int end = slice.IndexOf("column_aside", StringComparison.OrdinalIgnoreCase);
        if (end > 0)
            slice = slice.Substring(0, end);

        return slice;
    }

    // <div class="item"><a href="/categories/anal/428/" title="Page 428">428</a>
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 1;

        string slice = PaginationBlock(html);
        if (slice.Length == 0)
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(slice, "href=\"[^\"]*?/(\\d+)/\""))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max)
                max = n;

        return max;
    }

    // ===== stream =====
    // var flashvars = { video_url: '...469113.mp4/?v-acctoken=..', video_url_text: 'High Quality',
    //                   video_alt_url: '...469113_lq.mp4', video_alt_url_text: 'Low Quality' }
    public static Dictionary<string, string> StreamLinks(string html)
    {
        var qualitys = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(html))
            return qualitys;

        var hq = Regex.Match(html, "video_url:\\s*'([^']+)'");
        if (hq.Success)
        {
            string url = HttpUtility.HtmlDecode(hq.Groups[1].Value);
            if (url.StartsWith("//"))
                url = "https:" + url;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                qualitys["HD"] = url;
        }

        var lq = Regex.Match(html, "video_alt_url:\\s*'([^']+)'");
        if (lq.Success)
        {
            string url = HttpUtility.HtmlDecode(lq.Groups[1].Value);
            if (url.StartsWith("//"))
                url = "https:" + url;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !qualitys.ContainsKey("SD"))
                qualitys["SD"] = url;
        }

        // fallback: <source src="...">
        if (qualitys.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, "<source[^>]+src=\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase))
            {
                string url = HttpUtility.HtmlDecode(m.Groups[1].Value);
                if (url.Contains(".m3u8"))
                    continue;
                if (!qualitys.ContainsKey("MP4"))
                    qualitys["MP4"] = url;
            }
        }

        return qualitys;
    }

    // ===== menu: dong 2 "Sap xep" theo CONTEXT =====
    // Sort = doi THU TU cung mot tap: chi categories/tags/pornstars + search moi co.
    // latest-updates / top-rated/* / most-viewed/* la LISTING -> an dong 2.
    public static readonly (string name, string sort)[] SortsCategory =
    {
        ("Mới nhất", ""),
        ("Xem nhiều", "most-viewed/month"),
        ("Đánh giá cao", "top-rated/month"),
        ("Dài nhất", "longest"),
    };

    public static readonly (string name, string sort)[] SortsSearch =
    {
        ("Liên quan nhất", ""),
        ("Mới nhất", "latest-updates"),
        ("Xem nhiều", "most-viewed/month"),
        ("Đánh giá cao", "top-rated/month"),
        ("Dài nhất", "longest"),
    };

    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return SortsSearch;

        if (string.IsNullOrWhiteSpace(c))
            return null;

        string cc = c.Trim().Trim('/');

        if (cc.StartsWith("categories/", StringComparison.OrdinalIgnoreCase)
            || cc.StartsWith("tags/", StringComparison.OrdinalIgnoreCase)
            || cc.StartsWith("pornstars/", StringComparison.OrdinalIgnoreCase)
            || cc.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            return SortsCategory;

        return null;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null || string.IsNullOrWhiteSpace(sort))
            return null;

        string s = sort.Trim().ToLowerInvariant();
        foreach (var o in opts)
            if (o.sort == s && o.sort.Length > 0)
                return o.sort;

        return null;
    }

    public static string SortLabel(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);

        if (opts != null && !string.IsNullOrWhiteSpace(sort))
        {
            foreach (var o in opts)
                if (o.sort == sort)
                    return o.name.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(search))
            return "liên quan nhất";

        return "mới nhất";
    }

    // HEAD: dong 1 + 2 (phu thuoc search/sort/c)
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/threemovs";

        string link(string s)
        {
            string q;
            if (!string.IsNullOrWhiteSpace(search))
                q = "search=" + HttpUtility.UrlEncode(search);
            else if (!string.IsNullOrWhiteSpace(c))
                q = "c=" + HttpUtility.UrlEncode(c);
            else
                q = "";

            if (!string.IsNullOrEmpty(s))
                q += (q.Length == 0 ? "" : "&") + "sort=" + HttpUtility.UrlEncode(s);

            return q.Length == 0 ? root : root + "?" + q;
        }

        var res = new List<Shared.Models.SISI.Base.MenuItem>(2)
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm", search_on = "search_on", playlist_url = root
            }
        };

        var opts = SortsFor(search, c);
        if (opts == null || opts.Length == 0)
            return res;

        var sub = new List<Shared.Models.SISI.Base.MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
            sub.Add(new Shared.Models.SISI.Base.MenuItem(name, link(s)));

        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort, search, c)}",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    // BASE: dong 3+ (khong phu thuoc context) -> cache 1 lan
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host,
        List<(string name, string slug)> categories,
        List<(string name, string slug)> pornstars)
    {
        host = host.TrimEnd('/');

        var lists = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Mới nhất", host + "/threemovs?c=latest-updates"),
            new("Xem nhiều tháng", host + "/threemovs?c=" + HttpUtility.UrlEncode("most-viewed/month")),
            new("Xem nhiều tuần", host + "/threemovs?c=" + HttpUtility.UrlEncode("most-viewed/week")),
            new("Top hôm nay", host + "/threemovs?c=" + HttpUtility.UrlEncode("top-rated/today")),
            new("Top tuần", host + "/threemovs?c=" + HttpUtility.UrlEncode("top-rated/week")),
            new("Top tháng", host + "/threemovs?c=" + HttpUtility.UrlEncode("top-rated/month")),
            new("Top mọi thời", host + "/threemovs?c=" + HttpUtility.UrlEncode("top-rated/all-time")),
            new("Dài nhất", host + "/threemovs?c=longest"),
        };

        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh sách",
                playlist_url = "submenu",
                submenu = lists
            }
        };

        if (categories != null && categories.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = categories.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, host + "/threemovs?c=" + HttpUtility.UrlEncode("categories/" + x.slug)
                    )).ToList()
            });
        }

        if (pornstars != null && pornstars.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Diễn viên",
                playlist_url = "submenu",
                submenu = pornstars.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, host + "/threemovs?c=" + HttpUtility.UrlEncode("pornstars/" + x.slug)
                    )).ToList()
            });
        }

        return menu;
    }

    // ===== taxonomy =====
    static readonly HashSet<string> NotSlug = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "top-rated", "most-viewed", "videos", "videos-rating", "videos-views", "title"
    };

    static List<(string name, string slug)> AnchorList(string html, string kind)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a[^>]+href=\"https?://www\\.3movs\\.com/" + kind + "/([a-z0-9\\-]+)/\"[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value;
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug) || NotSlug.Contains(slug))
                continue;

            string name = "";
            var tm = Regex.Match(m.Value, "title=\"([^\"]+)\"");
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());

            if (string.IsNullOrEmpty(name))
            {
                string inner = Regex.Replace(m.Groups[2].Value, "<span[^>]*>.*?</span>", "",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
                inner = Regex.Replace(inner, "<[^>]+>", " ");
                name = HttpUtility.HtmlDecode(Regex.Replace(inner, "\\s+", " ").Trim());
            }

            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((name, slug));
        }

        return list;
    }

    // /categories/: <div class="thumb_cat item"><a href="/categories/vintage/" title="Vintage">
    public static List<(string name, string slug)> CategoryList(string html)
        => AnchorList(html, "categories");

    // /pornstars/: <a class="th_min" href="/pornstars/angela-white/" title="Angela White">
    public static List<(string name, string slug)> PornstarList(string html)
        => AnchorList(html, "pornstars");

    // ===== popup long-press =====
    // Khoi info cua video: <div class="block"><div class="items_list"> <strong>Tags:</strong>
    //   <a href="/tags/anal-fucking/">Anal Fucking</a> ... roi <strong>Categories:</strong> ...
    // Cat tu <strong>Tags:</strong> (hoac Categories) den khoi comment de khong lay sidebar.
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        int i = html.IndexOf("<strong>Tags:</strong>", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            i = html.IndexOf("<strong>Categories:</strong>", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            i = html.IndexOf("<strong>Models:</strong>", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return list;

        int end = html.IndexOf("video_comments_", i, StringComparison.OrdinalIgnoreCase);
        string block = end > i
            ? html.Substring(i, end - i)
            : html.Substring(i, Math.Min(8000, html.Length - i));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(block,
            "<a[^>]+href=\"https?://www\\.3movs\\.com/(categories|tags|pornstars|models)/([a-z0-9\\-]+)/\"[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string kind = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "tags" => "tag",
                "pornstars" or "models" => "model",
                _ => "category"
            };
            string slug = m.Groups[2].Value;

            string name = HttpUtility.HtmlDecode(Regex.Replace(m.Groups[3].Value, "<[^>]+>", " ")).Trim();
            name = Regex.Replace(name, "\\s+", " ");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(slug))
                continue;

            if (!seen.Add(kind + "/" + slug))
                continue;

            list.Add((kind, slug, name));
        }

        return list;
    }

    public static bool HasRelated(string html)
    {
        return !string.IsNullOrEmpty(html)
            && html.Contains("list_videos_related_videos_items", StringComparison.OrdinalIgnoreCase);
    }

    // "Video lien quan" nam san trong trang detail -> khong fetch them
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(string route, string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        int i = html.IndexOf("list_videos_related_videos_items", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        return Playlist(route, html.Substring(i));
    }
}
