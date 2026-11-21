using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace FreePornVideos;

public static class FreePornVideosTo
{
    public static string SiteHost = "https://www.freepornvideos.xxx";

    // Site da ngon ngu (/vi, /pt, /es...). Module luon di ban /vi.
    public static string Lang = "/vi";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // ===== URL =====
    // c = "" (tat ca) | "latest-updates" | "top-rated" | "most-popular"
    //     | "most-popular/today" | "most-popular/week" | "most-popular/month"
    //     | "categories/<slug>" | "sites/<slug>" | "networks/<slug>"
    //
    // Sort cua site la PATH, khong phai query. Do 2026-11-21 tren /vi/categories/milf/:
    //   (khong hau to)        -> video_viewed  (mac dinh cua category)
    //   /latest-updates/      -> post_date
    //   /top-rated/           -> rating
    //   /most-relevant/       -> ctr
    //   4 ban deu khac thu tu trang 1 (khong trung => sort that).
    // Search cung sort duoc: /vi/search/<q>/latest-updates/ , /top-rated/
    //   (most-popular cua search trung thu tu voi top-rated -> khong dua vao menu).
    // Phan trang luon la path + N/: /vi/categories/milf/top-rated/2/ , /vi/search/q/2/
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        sort = string.IsNullOrWhiteSpace(sort) ? null : sort.Trim().Trim('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string u = host + Lang + "/search/" + SearchSlug(search) + "/";
            if (sort != null)
                u += sort + "/";
            return pg > 1 ? u + pg + "/" : u;
        }

        string path = string.IsNullOrWhiteSpace(c) ? "" : c.Trim('/');

        // "tat ca video": sort chinh la ten list goc (/vi/top-rated/, /vi/most-popular/)
        if (path.Length == 0)
        {
            string list = sort ?? "latest-updates";
            string u = host + Lang + "/" + list + "/";
            return pg > 1 ? u + pg + "/" : u;
        }

        string url = host + Lang + "/" + path + "/";
        if (sort != null)
            url += sort + "/";
        return pg > 1 ? url + pg + "/" : url;
    }

    // "best friend" -> "best-friend" (site dung dau gach ngang cho khoang trang)
    static string SearchSlug(string search)
    {
        string s = Regex.Replace(search.Trim(), @"\s+", "-");
        return HttpUtility.UrlEncode(s).Replace("+", "-");
    }

    // ===== danh sach video =====
    // Card:
    //   <div class="item">
    //     <a href="https://www.freepornvideos.xxx/vi/videos/<id>/<slug>/" title="TEN">
    //       <div class="img thumb__img" data-preview="https://cast.../preview/<id>.mp4">
    //         <img ... data-src="https://img.../1.jpg">
    //         <span class="duration">Video đầy đủ 29:53</span>
    //     <div class="item-info"> <strong class="title"> TEN </strong>
    //       ... <div class="k4"> (badge 4K, chi co o video 4K)
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string route, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();

        foreach (string raw in Regex.Split(html, "<div class=\"item\"(?=[ >])"))
        {
            // KHONG cat ngan block: card co danh sach dien vien dai > 6KB, badge k4 nam CUOI block
            // (cat 6000 se mat badge -> mat quality cua card do)
            string b = raw;

            var hm = Regex.Match(b, "href=\"https?://[^\"]*?/vi/videos/(\\d+)/([^\"]*?)\"");
            if (!hm.Success)
                continue;

            string href = SiteHost + "/vi/videos/" + hm.Groups[1].Value + "/" + hm.Groups[2].Value;
            if (!seen.Add(href))
                continue;

            string name = "";
            var tm = Regex.Match(b, "class=\"title\">\\s*([^<]{2,300}?)\\s*</strong>");
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
            {
                var t2 = Regex.Match(b, "<a href=\"[^\"]+\" title=\"([^\"]{2,300})\"");
                if (t2.Success)
                    name = HttpUtility.HtmlDecode(t2.Groups[1].Value.Trim());
            }
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var pm = Regex.Match(b, "<img[^>]+(?:data-src|src)=\"(https?://[^\"]+)\"");
            if (pm.Success)
                poster = HttpUtility.HtmlDecode(pm.Groups[1].Value);

            string preview = "";
            var vm = Regex.Match(b, "data-preview=\"(https?://[^\"]+)\"");
            if (vm.Success)
                preview = HttpUtility.HtmlDecode(vm.Groups[1].Value);

            // time = duration site in tren card ("Video đầy đủ 29:53" -> "29:53")
            string time = "";
            var dm = Regex.Match(b, "class=\"duration\">([^<]{1,40})<");
            if (dm.Success)
            {
                var d2 = Regex.Match(dm.Groups[1].Value, "\\d{1,2}:\\d{2}(?::\\d{2})?");
                if (d2.Success)
                    time = d2.Value;
            }

            // quality = badge k4 CO ICON cua site (badge k4 rong la card quang cao chen giua
            // 2 card that, khong duoc tinh - khong tu nghi ra "HD")
            string quality = Regex.IsMatch(b, "class=\"k4\">\\s*<span class=\"icon\"") ? "4K" : null;

            var item = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = time,
                quality = quality,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "freepornvideos",
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

    // So trang tu khoi pagination: <li class="last"><a href=".../6179/">Trang cuoi</a>
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 1;

        int i = html.IndexOf("class=\"pagination\"", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return 1;

        string slice = html.Substring(i, Math.Min(8000, html.Length - i));

        var last = Regex.Match(slice, "class=\"last\"><a href=\"[^\"]*/(\\d+)/");
        if (last.Success && int.TryParse(last.Groups[1].Value, out int lp) && lp > 1)
            return lp;

        int max = 1;
        foreach (Match m in Regex.Matches(slice, "href=\"[^\"]*/(\\d+)/\""))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max)
                max = n;

        return max;
    }

    // ===== stream =====
    // Trang detail co san nguon MP4 (khong can ajax/embed):
    //   <source src='https://www.freepornvideos.xxx/vi/get_file/8512/<hash>/.../93823263_2160m.mp4/'
    //           type='video/mp4' label="2160p">
    // -> nhan chat luong = label cua site ("2160p"/"720p"/"480p")
    public static Dictionary<string, string> StreamLinks(string html)
    {
        var qualitys = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(html))
            return qualitys;

        foreach (Match m in Regex.Matches(html, "<source[^>]+>", RegexOptions.IgnoreCase))
        {
            string tag = m.Value;

            var sm = Regex.Match(tag, "src=['\"]([^'\"]+)['\"]");
            if (!sm.Success)
                continue;

            string url = HttpUtility.HtmlDecode(sm.Groups[1].Value);
            if (!url.StartsWith("http"))
                continue;

            string label = "MP4";
            var lm = Regex.Match(tag, "label=['\"]([^'\"]+)['\"]");
            if (!lm.Success)
                lm = Regex.Match(tag, "title=['\"]([^'\"]+)['\"]");
            if (lm.Success && lm.Groups[1].Value.Trim().Length > 0)
                label = lm.Groups[1].Value.Trim();

            if (!qualitys.ContainsKey(label))
                qualitys[label] = url;
        }

        return qualitys;
    }

    // ===== sort (dong 2 "Sap xep: <dang mo>") =====
    public static readonly (string name, string sort)[] SortsAll =
    {
        ("Mới nhất", ""),
        ("Đánh giá cao", "top-rated"),
        ("Phổ biến", "most-popular"),
    };

    public static readonly (string name, string sort)[] SortsCategory =
    {
        ("Mặc định", ""),
        ("Mới nhất", "latest-updates"),
        ("Đánh giá cao", "top-rated"),
        ("Liên quan", "most-relevant"),
    };

    public static readonly (string name, string sort)[] SortsSite =
    {
        ("Mặc định", ""),
        ("Đánh giá cao", "top-rated"),
        ("Phổ biến", "most-popular"),
    };

    public static readonly (string name, string sort)[] SortsSearch =
    {
        ("Liên quan nhất", ""),
        ("Mới nhất", "latest-updates"),
        ("Đánh giá cao", "top-rated"),
    };

    // null = context khong sort duoc -> an han dong 2
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return SortsSearch;

        if (string.IsNullOrWhiteSpace(c))
            return SortsAll;

        string cc = c.Trim('/');

        if (cc.StartsWith("categories/", StringComparison.OrdinalIgnoreCase))
            return SortsCategory;

        if (cc.StartsWith("sites/", StringComparison.OrdinalIgnoreCase)
            || cc.StartsWith("networks/", StringComparison.OrdinalIgnoreCase))
            return SortsSite;

        // most-popular/today|week|month : list co dinh, site khong sort tren do
        return null;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null || string.IsNullOrWhiteSpace(sort))
            return null;

        sort = sort.Trim().ToLowerInvariant();
        foreach (var o in opts)
            if (o.sort == sort && o.sort.Length > 0)
                return o.sort;

        return null;
    }

    public static string SortLabel(string sort, string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(sort))
        {
            foreach (var o in SortsCategory)
                if (o.sort == sort)
                    return o.name.ToLowerInvariant();
            return sort;
        }

        if (!string.IsNullOrWhiteSpace(search))
            return "liên quan nhất";

        if (!string.IsNullOrWhiteSpace(c))
        {
            string cc = c.Trim('/');
            if (cc.StartsWith("categories/", StringComparison.OrdinalIgnoreCase))
                return "mặc định";
            if (cc.StartsWith("sites/", StringComparison.OrdinalIgnoreCase)
                || cc.StartsWith("networks/", StringComparison.OrdinalIgnoreCase))
                return "mặc định";
        }

        return "mới nhất";
    }

    // HEAD: dong 1 + 2, phu thuoc search/sort/c -> dung lai moi request
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/fpv";

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
                q += (q.Length == 0 ? "" : "&") + "sort=" + s;

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
        List<(string name, string slug)> categories = null,
        List<(string name, string slug)> sites = null)
    {
        host = host.TrimEnd('/');

        var lists = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Mới nhất", host + "/fpv?c=latest-updates"),
            new("Đánh giá cao", host + "/fpv?c=top-rated"),
            new("Phổ biến", host + "/fpv?c=most-popular"),
            new("Phổ biến hôm nay", host + "/fpv?c=most-popular/today"),
            new("Phổ biến tuần này", host + "/fpv?c=most-popular/week"),
            new("Phổ biến tháng này", host + "/fpv?c=most-popular/month"),
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

        // 361 the loai -> chia nhom theo chu cai dau cua slug (moi nhom <= 300)
        if (categories != null && categories.Count > 0)
        {
            foreach (var (title, items) in LetterBuckets(host, categories))
            {
                menu.Add(new Shared.Models.SISI.Base.MenuItem()
                {
                    title = "Thể loại " + title,
                    playlist_url = "submenu",
                    submenu = items
                });
            }
        }

        if (sites != null && sites.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Trang web",
                playlist_url = "submenu",
                submenu = sites.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, host + "/fpv?c=" + HttpUtility.UrlEncode("sites/" + x.slug)
                    )).ToList()
            });
        }

        return menu;
    }

    static readonly (string title, char from, char to)[] Buckets =
    {
        ("#–F", '\0', 'f'),
        ("G–L", 'g', 'l'),
        ("M–R", 'm', 'r'),
        ("S–Z", 's', 'z'),
    };

    static List<(string title, List<Shared.Models.SISI.Base.MenuItem> items)> LetterBuckets(
        string host, List<(string name, string slug)> categories)
    {
        var res = new List<(string, List<Shared.Models.SISI.Base.MenuItem>)>();

        foreach (var (title, from, to) in Buckets)
        {
            var items = new List<Shared.Models.SISI.Base.MenuItem>();
            foreach (var (name, slug) in categories)
            {
                char ch = string.IsNullOrEmpty(slug) ? '#' : char.ToLowerInvariant(slug[0]);

                // nhom dau tien = "#" (khong phai a-z) + a..f
                bool inBucket = from == '\0'
                    ? (ch < 'a' || ch > 'z') || ch <= to
                    : ch >= from && ch <= to;
                if (!inBucket)
                    continue;

                items.Add(new Shared.Models.SISI.Base.MenuItem(
                    name, host + "/fpv?c=" + HttpUtility.UrlEncode("categories/" + slug)));
            }

            if (items.Count > 0)
                res.Add((title, items));
        }

        return res;
    }

    // ===== taxonomy =====
    // /vi/categories/ : <a class="item" href=".../vi/categories/18-years-old/" title="18 Tuổi">
    public static List<(string name, string slug)> CategoryList(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a class=\"item\" href=\"https?://[^\"]*?/vi/categories/([^/\"]+)/\"([^>]*)>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value;
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            string name = "";
            var tm = Regex.Match(m.Groups[2].Value, "title=\"([^\"]+)\"");
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());

            if (string.IsNullOrEmpty(name))
                name = HttpUtility.HtmlDecode(Regex.Replace(m.Groups[3].Value, "<[^>]+>", " ").Trim());

            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((Regex.Replace(name, "\\s+", " "), slug));
        }

        return list;
    }

    // /vi/sites/ : moi sponsor la 1 khoi
    //   <div class="headline"><h2>BLACKED</h2>
    //     <a class="more" href=".../vi/sites/blacked/"><span>Xem Tất Cả 855 Video</span></a>
    public static List<(string name, string slug)> SiteList(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        int i = html.IndexOf("class=\"list-sponsors\"", StringComparison.OrdinalIgnoreCase);
        string block = i < 0 ? html : html.Substring(i);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(block,
            "<h2>([^<]{1,80})</h2>\\s*<a class=\"more\" href=\"https?://[^\"]*?/vi/sites/([^/\"]+)/\"",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string name = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
            string slug = m.Groups[2].Value;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            list.Add((name, slug));
        }

        return list;
    }

    public static int SitePages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html, "/vi/sites/(\\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;

        return max > 10 ? 10 : max;
    }

    // ===== popup long-press =====
    // Khoi class="block-details": <a class="btn_sponsor"    href=".../vi/sites/<slug>/">Ten</a>
    //                             <a class="btn_sponsor_group" href=".../vi/networks/<slug>/">Ten</a>
    //                             <a class="btn_tag"       href=".../vi/categories/<slug>/">Ten</a>
    //                             <a class="btn_model"     href=".../vi/models/<slug>/">Ten</a>
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        int i = html.IndexOf("class=\"block-details\"", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return list;

        int end = html.IndexOf("id=\"list_videos_related", i, StringComparison.OrdinalIgnoreCase);
        string block = end > i
            ? html.Substring(i, end - i)
            : html.Substring(i, Math.Min(30000, html.Length - i));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(block,
            "<a[^>]*class=\"btn_([a-z_]+)\"[^>]*href=\"[^\"]*?/vi/(sites|networks|categories|models)/([^/\"]+)/\"[^>]*>([^<]{1,80})<",
            RegexOptions.IgnoreCase))
        {
            string kind = m.Groups[2].Value.ToLowerInvariant() switch
            {
                "sites" => "site",
                "networks" => "network",
                "categories" => "category",
                _ => "model"
            };
            string slug = m.Groups[3].Value;
            string name = HttpUtility.HtmlDecode(Regex.Replace(m.Groups[4].Value, "\\s+", " ").Trim());

            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name))
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