using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace AnySex;

public static class AnySexTo
{
    public static string SiteHost = "https://anysex.com";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // ===== URL =====
    // path (c) = "videos/new" | "videos/best" | "videos/most-viewed" | "videos/top-rated"
    //            | "videos/4k" | "videos/categories/<slug>" | "tags/<slug>"
    //            | "models/<slug>" | "channels/<slug>"
    // Phan trang cua site luon la PATH + N/:  /videos/new/2/ , /tags/x/3/?sort=views
    // Rieng search: /search/2/?q=... (page nam TRUOC query)
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "?q=" + HttpUtility.UrlEncode(search.Trim());
            if (!string.IsNullOrWhiteSpace(sort))
                q += "&sort=" + sort.Trim();
            return host + "/search/" + (pg > 1 ? pg + "/" : "") + q;
        }

        string path = string.IsNullOrWhiteSpace(c) ? "videos/new" : c.Trim('/');
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            string url = path.TrimEnd('/');
            string qs = pg > 1 ? "/" + pg : "";
            if (!string.IsNullOrWhiteSpace(sort))
                qs += "/?sort=" + sort.Trim();
            else if (pg > 1)
                qs += "/";
            return url + qs;
        }

        string sfx = string.IsNullOrWhiteSpace(sort) ? "" : "?sort=" + sort.Trim();
        return host + "/" + path + "/" + (pg > 1 ? pg + "/" : "") + sfx;
    }

    // ===== danh sach video =====
    // Card: <div class="item " data-video-id="396870" ...> <a href="/video/396870/slug/"
    //       class="popfire"> <div class="img"><img ... data-preview="...mp4" data-jpg="..."/>
    //       <div class="duration">18:51</div></div> <strong class="title">TEN</strong> </a>
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string route, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();

        foreach (string raw in html.Split(new[] { "<div class=\"item " }, StringSplitOptions.None))
        {
            string b = raw.Length > 3500 ? raw.Substring(0, 3500) : raw;

            var idm = Regex.Match(b, "data-video-id=\"(\\d+)\"");
            if (!idm.Success)
                continue;

            var hm = Regex.Match(b, "href=\"(https?://[^\"]*/video/[^\"]+)\"");
            if (!hm.Success)
                continue;
            string href = HttpUtility.HtmlDecode(hm.Groups[1].Value);

            // site tra link theo ngon ngu/geo (ru./de./es.anysex.com) -> dua ve 1 host
            href = Regex.Replace(href, "^https?://[a-z]{2}\\.anysex\\.com/", SiteHost + "/",
                RegexOptions.IgnoreCase);

            if (!seen.Add(href))
                continue;

            var tm = Regex.Match(b, "class=\"title\">\\s*([^<]{2,300}?)\\s*</strong>");
            if (!tm.Success)
                continue;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var pm = Regex.Match(b, "data-jpg=\"([^\"]+)\"");
            if (!pm.Success)
                pm = Regex.Match(b, "<img[^>]+src=\"([^\"]+)\"");
            if (pm.Success)
            {
                poster = HttpUtility.HtmlDecode(pm.Groups[1].Value);
                if (poster.StartsWith("//"))
                    poster = "https:" + poster;
            }

            string preview = "";
            var vm = Regex.Match(b, "data-preview=\"([^\"]+)\"");
            if (vm.Success)
                preview = HttpUtility.HtmlDecode(vm.Groups[1].Value);

            string dur = "";
            var dm = Regex.Match(b, "class=\"duration\">\\s*([^<]{1,20}?)\\s*</div>");
            if (dm.Success)
                dur = HttpUtility.HtmlDecode(dm.Groups[1].Value.Trim());

            string quality = "";
            var qm = Regex.Match(b, "class=\"is-hd[^\"]*\">\\s*([^<]{1,10}?)\\s*</span>");
            if (qm.Success)
                quality = qm.Groups[1].Value.Trim();

            var item = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = dur,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "anysex",
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

    // So trang tu khoi pagination: <li class="last"><a href=".../16994/">16994</a>
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 1;

        int i = html.IndexOf("class=\"pagination\"", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return 1;

        string slice = html.Substring(i, Math.Min(6000, html.Length - i));

        var last = Regex.Match(slice, "class=\"last\"><a href=\"[^\"]*/(\\d+)/");
        if (last.Success && int.TryParse(last.Groups[1].Value, out int lp) && lp > 1)
            return lp;

        int max = 1;
        foreach (Match m in Regex.Matches(slice, "href=\"[^\"]*/(\\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max)
                max = n;

        return max;
    }

    // ===== stream =====
    // <source id="video_source_4" src="https://anysex.com/get_file/.../396870_1080.mp4/?br=2340"
    //         type="video/mp4" title="1080p">
    public static Dictionary<string, string> StreamLinks(string html)
    {
        var qualitys = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(html))
            return qualitys;

        foreach (Match m in Regex.Matches(html, "<source[^>]+>", RegexOptions.IgnoreCase))
        {
            string tag = m.Value;
            var sm = Regex.Match(tag, "src=\"(https?://[^\"]+)\"");
            if (!sm.Success)
                continue;

            string url = HttpUtility.HtmlDecode(sm.Groups[1].Value);
            if (url.Contains(".m3u8"))
                continue;

            string label = "MP4";
            var tm = Regex.Match(tag, "title=\"([^\"]+)\"");
            if (tm.Success)
                label = tm.Groups[1].Value.Trim();

            if (!qualitys.ContainsKey(label))
                qualitys[label] = url;
        }

        return qualitys;
    }

    // ===== menu: dong 2 "Sap xep" theo CONTEXT =====
    public static readonly (string name, string sort)[] SortsCategory =
    {
        ("Hot", ""),
        ("Mới nhất", "new"),
        ("Xem nhiều", "views"),
        ("Đánh giá cao", "top"),
    };

    public static readonly (string name, string sort)[] SortsLatest =
    {
        ("Mới nhất", ""),
        ("Xem nhiều", "views"),
        ("Đánh giá cao", "top"),
    };

    public static readonly (string name, string sort)[] SortsSearch =
    {
        ("Liên quan nhất", ""),
        ("Mới nhất", "new"),
        ("Xem nhiều", "views"),
        ("Đánh giá cao", "top"),
    };

    // null = context khong sort duoc -> AN HAN dong 2 (khong thay muc khac)
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return SortsSearch;

        if (string.IsNullOrWhiteSpace(c))
            return null;

        string cc = c.Trim('/');

        if (cc.StartsWith("videos/categories/", StringComparison.OrdinalIgnoreCase))
            return SortsCategory;

        if (cc.StartsWith("tags/", StringComparison.OrdinalIgnoreCase)
            || cc.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            || cc.StartsWith("channels/", StringComparison.OrdinalIgnoreCase))
            return SortsLatest;

        // videos/new | videos/best | videos/most-viewed | videos/top-rated | videos/4k
        return null;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null)
            return null;

        if (string.IsNullOrWhiteSpace(sort))
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
        if (!string.IsNullOrWhiteSpace(c)
            && c.Trim('/').StartsWith("videos/categories/", StringComparison.OrdinalIgnoreCase))
            return "hot";
        return "mới nhất";
    }

    // HEAD: dong 1 + 2, phu thuoc search/sort/c -> dung lai moi request
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/anysex";

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
        List<(string name, string slug)> categories,
        List<(string name, string slug)> channels)
    {
        host = host.TrimEnd('/');

        var lists = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Mới nhất", host + "/anysex?c=videos/new"),
            new("Hay nhất", host + "/anysex?c=videos/best"),
            new("Xem nhiều nhất", host + "/anysex?c=videos/most-viewed"),
            new("Đánh giá cao", host + "/anysex?c=videos/top-rated"),
            new("4K", host + "/anysex?c=videos/4k"),
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
                        x.name, host + "/anysex?c=" + HttpUtility.UrlEncode("videos/categories/" + x.slug)
                    )).ToList()
            });
        }

        if (channels != null && channels.Count > 0)
        {
            // 40 trang /channels (~1240 muc) -> chi lay 300 muc dau cho 1 submenu
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Kênh",
                playlist_url = "submenu",
                submenu = channels.Take(300).Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, host + "/anysex?c=" + HttpUtility.UrlEncode("channels/" + x.slug)
                    )).ToList()
            });
        }

        return menu;
    }

    // ===== taxonomy =====
    // /videos/categories/: <a data-id="51" title="Japanese" href=".../videos/categories/japanese/">
    public static List<(string name, string slug)> CategoryList(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a[^>]+href=\"https://anysex\\.com/videos/categories/([a-z0-9\\-]+)/\"[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value;
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            string name = "";
            var tm = Regex.Match(m.Value, "title=\"([^\"]+)\"");
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());

            if (string.IsNullOrEmpty(name))
            {
                var nm = Regex.Match(m.Groups[2].Value, "class=\"title\">\\s*([^<]{1,80}?)\\s*</strong>");
                if (nm.Success)
                    name = HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim());
            }

            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((name, slug));
        }

        return list;
    }

    // /channels/: <a class="item" href=".../channels/slug/" title="Name">
    public static List<(string name, string slug)> ChannelList(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a[^>]+href=\"https://anysex\\.com/channels/([a-z0-9\\-]+)/\"[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value;
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            string name = "";
            var tm = Regex.Match(m.Value, "title=\"([^\"]+)\"");
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());

            if (string.IsNullOrEmpty(name))
            {
                var nm = Regex.Match(m.Groups[2].Value, "class=\"title\">\\s*([^<]{1,80}?)\\s*</strong>");
                if (nm.Success)
                    name = HttpUtility.HtmlDecode(nm.Groups[1].Value.Trim());
            }

            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((name, slug));
        }

        return list;
    }

    public static int ChannelPages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html, "/channels/(\\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;

        return max > 10 ? 10 : max;
    }

    // ===== popup long-press =====
    // Khoi <div class="video-tags ..."> gom model (/models/), category
    // (/videos/categories/), tag (/tags/). Cat o swiper-button-more de khong
    // lan sang block "Related Videos".
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        int i = html.IndexOf("class=\"video-tags", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return list;

        int end = html.IndexOf("swiper-button-more", i, StringComparison.OrdinalIgnoreCase);
        string block = end > i
            ? html.Substring(i, end - i)
            : html.Substring(i, Math.Min(12000, html.Length - i));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(block,
            "<a[^>]+href=\"https://anysex\\.com/(models|videos/categories|tags)/([a-z0-9\\-]+)/\"[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string kind = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "models" => "model",
                "tags" => "tag",
                _ => "category"
            };
            string slug = m.Groups[2].Value;

            string inner = Regex.Replace(m.Groups[3].Value,
                "<span class=\"count\">.*?</span>", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            inner = Regex.Replace(inner, "<[^>]+>", " ");
            string name = HttpUtility.HtmlDecode(Regex.Replace(inner, "\\s+", " ").Trim());
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
            && html.Contains("list_videos_custom_related_sphinx_videos_items", StringComparison.OrdinalIgnoreCase);
    }

    // "Video lien quan" nam san trong trang detail -> khong fetch them
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(string route, string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        int i = html.IndexOf("list_videos_custom_related_sphinx_videos_items", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        return Playlist(route, html.Substring(i));
    }
}
