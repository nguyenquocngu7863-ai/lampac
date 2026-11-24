using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace JavBangers;

/// <summary>
/// javbangers.com — KVS/Porntrex ("javwhores" theme), do 2026-11-24 tren site that.
///   list    : /latest-updates/[pg]/   /top-rated/   /most-popular/
///   the loai: /categories/&lt;slug&gt;/[pg]/      (index /categories/)
///   dien vien:/models/&lt;slug&gt;/[pg]/          (index /models/)
///   nguoi dang:/members/&lt;id&gt;/[pg]/           (index /members/ — page chi co 10 video)
///   tim kiem: /search/?q=&lt;kw&gt;
///   sort    : ?sort_by=video_viewed|rating|duration  (DO: an o category/model/search; KHONG an o
///             /latest-updates/ va /members/&lt;id&gt;/ — xem bang do trong README)
///   card    : &lt;div class="video-item"&gt; &lt;a class="thumb" href="/video/&lt;id&gt;/&lt;slug&gt;" title="TEN"&gt;
///             &lt;img class="cover lazy-load" data-original="..."&gt; ...
///             &lt;div class="durations"&gt;&lt;i class="fa fa-clock-o"&gt;&lt;/i&gt; 34:09&lt;/div&gt;
///   player  : trang /video/... tra **404 cho guest** ==> lay stream tu GET /embed/&lt;id&gt;
///             flashvars.video_url = "https://.../get_file/4/...&lt;id&gt;.mp4/?v-acctoken=...&amp;embed=true"
///             (video_alt_url = 720p nhung la REDIRECT ve trang detail 404)
/// </summary>
public static class JbTo
{
    public const string SiteHost = "https://www.javbangers.com";

    public const string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    static string Base(string host)
        => (string.IsNullOrWhiteSpace(host) ? SiteHost : host).TrimEnd('/');

    static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        s = HttpUtility.HtmlDecode(s).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    static string Match1(string s, string pattern)
    {
        var m = Regex.Match(s, pattern, RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    #region URL
    // c = "" (latest-updates) | "top-rated" | "most-popular" | "categories/<slug>" | "models/<slug>" | "members/<id>"
    public static string ListUrl(string host, string c, int pg)
    {
        if (pg < 1) pg = 1;
        c = (c ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(c)) c = "latest-updates";

        string u = Base(host) + "/" + c + "/";
        if (pg > 1) u += pg + "/";
        return u;
    }

    public static string SearchUrl(string host, string kw, string sort, int pg)
    {
        string u = Base(host) + "/search/?q=" + HttpUtility.UrlEncode((kw ?? "").Trim());
        if (!string.IsNullOrEmpty(sort)) u += "&sort_by=" + HttpUtility.UrlEncode(sort);
        if (pg > 1) u += "&page=" + pg;
        return u;
    }

    public static string MenuUrl(string host, string c)
        => $"{Base(host)}/javbangers?c={HttpUtility.UrlEncode(c)}";
    #endregion

    #region playlist
    // card: div.video-item ... moi card co 2 link cung 1 video (thumb + ten) -> dedupe theo id
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var res = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string part in Regex.Split(html, @"class=""video-item"))
        {
            var hm = Regex.Match(part, @"href=""https?://[^""/]+/video/(\d+)/[^""]*""");
            if (!hm.Success) continue;

            string id = hm.Groups[1].Value;
            if (!seen.Add(id)) continue;

            string name = Clean(Match1(part, @"\stitle=""([^""]+)"""));
            string pic = Match1(part, @"<img[^>]*\sdata-original=""([^""]+)""");
            if (string.IsNullOrEmpty(pic))
                pic = Match1(part, @"<img[^>]*\ssrc=""(https?://[^""]+)""");

            string dur = Clean(Match1(part, @"fa-clock-o""></i>\s*([\d:]+)"));
            string views = Clean(Match1(part, @"class=""viewsthumb"">\s*([^<]+)</div>"));

            res.Add(new PlaylistItem()
            {
                name = name,
                video = $"{route}?uri={HttpUtility.UrlEncode(Base(null) + "/video/" + id + "/")}",
                picture = Abs(pic),
                time = string.IsNullOrEmpty(dur) ? views : dur,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "javbangers",
                    href = $"{SiteHost}/video/{id}/",
                    image = Abs(pic)
                }
            });
        }

        return res;
    }
    #endregion

    #region SORT — DO 2026-11-24 (skill lampac-sort)
    // BANG DO (so SEQUENCE id + sha1 + kiem chung thu tu bang chinh du lieu the hien tren card):
    //  /latest-updates/          base f08c292c ; +duration / +rating / +video_viewed / +most_favourited
    //                            = f08c292c y het  => path nay BO QUA sort_by -> KHONG co dong 2
    //  /members/13/              base 0c741a14 ; +duration = 0c741a14 y het => KHONG co dong 2
    //  /categories/creampie/     base 4b5a2d07
    //      +duration    73f82883  -> duration GIAM DAN that (740:35 > 506:56 > 482:09 > 458:12 ...)
    //      +video_viewed (views GIAM DAN that: 566 131 > 293 693 > 247 065 > 230 904 ...)
    //      +rating        (100% > 100% > ... hang so dau trang, dung chieu giam)
    //  /models/yi-yuna/          base 9fb20b0b ; +duration 2edcf9a6 -> duration giam dan (29:14 > 22:39 > 19:38)
    //  /search/?q=japan          base 535ab69b ; +duration 8a32933d -> DOI thu tu
    // => sort SONG o: categories/*, models/*, search. CHET o: latest-updates, top-rated/most-popular, members/*.
    public static readonly (string id, string name)[] Sorts =
    {
        ("", "Mới nhất"),
        ("video_viewed", "Xem nhiều"),
        ("rating", "Đánh giá cao"),
        ("duration", "Dài nhất")
    };

    static readonly (string id, string name)[] NoSort = { ("", "Mới nhất") };

    public static (string id, string name)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return Sorts;

        string p = (c ?? "").Trim().Trim('/');
        if (p.StartsWith("categories/", StringComparison.OrdinalIgnoreCase)) return Sorts;
        if (p.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) return Sorts;
        // latest-updates / top-rated / most-popular / members/* : site bo qua sort_by (da do)
        return NoSort;
    }

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return "";
        sort = sort.Trim().ToLowerInvariant();
        foreach (var s in Sorts) if (s.id == sort) return sort;
        return null;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        var ok = SortsFor(search, c);
        string n = NormalizeSort(sort);
        if (n != null)
            foreach (var s in ok) if (s.id == n) return n;
        return ok[0].id;
    }

    public static string SortLabel(string sort)
    {
        foreach (var s in Sorts) if (s.id == (sort ?? "")) return s.name;
        return "Mới nhất";
    }

    // giu nguyen context (search / c), luon ve trang 1
    public static string SortUrl(string host, string search, string c, string sort)
    {
        string q = !string.IsNullOrWhiteSpace(search)
            ? "search=" + HttpUtility.UrlEncode(search)
            : !string.IsNullOrWhiteSpace(c) ? "c=" + HttpUtility.UrlEncode(c.Trim().Trim('/')) : "";

        if (!string.IsNullOrEmpty(sort))
            q = (q.Length == 0 ? "" : q + "&") + "sort=" + HttpUtility.UrlEncode(sort);

        return $"{Base(host)}/javbangers" + (q.Length == 0 ? "" : "?" + q);
    }
    #endregion

    #region menu
    // <a class="item" href=".../categories/<slug>/" title="Name"> ... <p class="title">Name</p>
    public static List<(string path, string name)> CategoryIndex(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(html,
            @"href=""https?://[^""/]+/(categories/[a-z0-9\-]+)/""[^>]*title=""([^""]{1,40})"""))
        {
            string path = m.Groups[1].Value;
            string name = Clean(m.Groups[2].Value);
            if (string.IsNullOrEmpty(name) || !seen.Add(path)) continue;
            res.Add((path, name));
        }
        return res;
    }

    public static List<(string path, string name)> FallbackCats = new List<(string, string)>()
    {
        ("categories/creampie", "Creampie"),
        ("categories/censored", "Censored"),
        ("categories/hardcore", "Hardcore"),
        ("categories/lesbian", "Lesbian"),
        ("categories/milf", "Milf"),
        ("categories/busty", "Busty"),
        ("categories/hentai", "Hentai"),
        ("categories/bukkake", "Bukkake"),
        ("categories/bondage", "Bondage"),
        ("categories/threesome", "Threesome"),
        ("categories/orgy", "Orgy"),
        ("categories/hairy", "Hairy"),
        ("categories/outdoor", "Outdoor"),
        ("categories/masturbate", "Masturbate"),
        ("categories/deep-throat", "Deep Throat")
    };

    // 4 dong: Tim kiem / Sap xep: <dang mo> / Danh sach / The loai
    public static List<MenuItem> Menu(string host, List<(string path, string name)> cats,
        string search, string c, string sort)
    {
        if (cats == null || cats.Count == 0) cats = FallbackCats;

        string cur = (c ?? "").Trim().Trim('/');
        var res = new List<MenuItem>()
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = $"{Base(host)}/javbangers" }
        };

        // dong 2 — chi context nao do duoc sort that
        var ok = SortsFor(search, c);
        if (ok.Length > 1)
        {
            res.Add(new MenuItem()
            {
                title = "Sắp xếp: " + SortLabel(sort),
                playlist_url = "submenu",
                submenu = ok.Select(s => new MenuItem(s.name, SortUrl(host, search, c, s.id))).ToList()
            });
        }

        // dong 3 — listing (doi TAP phim, KHONG phai sort)
        if (string.IsNullOrWhiteSpace(search))
        {
            res.Add(new MenuItem()
            {
                title = "Danh sách",
                playlist_url = "submenu",
                submenu = new List<MenuItem>()
                {
                    new MenuItem("Mới nhất", MenuUrl(host, "latest-updates")),
                    new MenuItem("Top đánh giá", MenuUrl(host, "top-rated")),
                    new MenuItem("Xem nhiều", MenuUrl(host, "most-popular"))
                }
            });
        }

        // dong 4 — taxonomy
        res.Add(new MenuItem()
        {
            title = "Thể loại",
            playlist_url = "submenu",
            submenu = cats.Select(x => new MenuItem(x.name, MenuUrl(host, x.path))).ToList()
        });

        return res;
    }
    #endregion

    #region detail / stream
    public static string VideoId(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        uri = uri.Trim();

        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return null;
            if (!u.Host.EndsWith("javbangers.com", StringComparison.OrdinalIgnoreCase)) return null;
            uri = u.AbsolutePath;
        }

        var m = Regex.Match(uri, @"^/video/(\d+)/");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string EmbedUrl(string host, string id)
        => $"{Base(host)}/embed/{id}";

    // flashvars.video_url: 'https://.../get_file/4/.../412813.mp4/?v-acctoken=...&embed=true'
    public static string StreamFromEmbed(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        string url = Match1(html, @"video_url:\s*'([^']+)'");
        if (string.IsNullOrEmpty(url)) return null;

        url = HttpUtility.HtmlDecode(url.Trim());
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : null;
    }

    // flashvars: video_categories / video_models / video_tags = ten cach nhau dau phay
    public static List<string> Names(string html, string key)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(html)) return res;

        string raw = Match1(html, key + @":\s*'([^']*)'");
        if (string.IsNullOrEmpty(raw)) return res;

        foreach (string s in raw.Split(','))
        {
            string n = Clean(s);
            if (string.IsNullOrEmpty(n)) continue;
            if (!res.Contains(n)) res.Add(n);
        }
        return res;
    }

    // index /categories/ va /models/ : ten -> path (dung de gan chip)
    public static Dictionary<string, string> NameMap(string html, string section)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(html)) return map;

        foreach (Match m in Regex.Matches(html,
            @"href=""https?://[^""/]+/(" + section + @"/[^""/]+)/""[^>]*title=""([^""]{1,60})"""))
        {
            string path = m.Groups[1].Value;
            string name = Clean(m.Groups[2].Value);
            if (string.IsNullOrEmpty(name) || map.ContainsKey(name)) continue;
            map[name] = path;
        }
        return map;
    }
    #endregion

    #region helpers
    public static string Abs(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        url = url.Trim();
        if (url.StartsWith("//")) return "https:" + url;
        if (url.StartsWith("/")) return SiteHost + url;
        return url;
    }

    // phan trang: data-parameters="sort_by:post_date;from:6011" (ajax) + link /.../N/
    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html)) return 0;

        int max = 0;
        // ajax pagination: sort_by:post_date;from:6011  |  search: from_videos+from_albums:02
        foreach (Match m in Regex.Matches(html, @"from[A-Za-z_\+]*:(\d+)"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;

        if (max > 0) return max;

        foreach (Match m in Regex.Matches(html, @"/(?:latest-updates|top-rated|most-popular|categories/[a-z0-9\-]+|models/[a-z0-9\-]+|members/\d+)/(\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;

        return max;
    }

    public static bool IsBlocked(string html)
    {
        if (string.IsNullOrEmpty(html) || html.Length < 1000) return true;
        if (html.Contains("Just a moment") || html.Contains("Enable JavaScript and cookies")) return true;
        return false;
    }
    #endregion
}
