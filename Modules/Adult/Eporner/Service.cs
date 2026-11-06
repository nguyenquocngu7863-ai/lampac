using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.HTML;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Eporner;

public static class EpornerTo
{
    #region Sort
    // ĐÃ ĐO 2026-11-06 (xem todo/skill lampac-sort cases.md):
    //  home   : /most-viewed/ /top-rated/ /longest/ /shortest/ — đổi thứ tự, h1 đổi theo sort
    //  cat    : /cat/<c>/SORT-<sort>/<pg>/ — có top-weekly, most-viewed, top-rated, longest.
    //           SORT-shortest và SORT-top-monthly bị site bỏ qua (h1 không đổi) → KHÔNG đưa vào menu.
    //  tag    : /tag/<t>/<pg>/<sort>/ — có top-weekly, most-viewed, top-rated, longest, shortest.
    //           top-monthly no-op (h1 giữ nguyên) → không đưa vào menu.
    //  search : /search/<q>/ KHÔNG sort được ("threesome ni jacky": 20 kết quả → most-viewed còn 1)
    //           ⇒ context search không có dòng "Sắp xếp".
    //  rel    : block tĩnh của trang detail ⇒ không sort.
    static readonly (string id, string name)[] SortsHome =
    {
        ("",            "Mới nhất"),
        ("most-viewed", "Xem nhiều nhất"),
        ("top-rated",   "Đánh giá cao"),
        ("longest",     "Video dài"),
        ("shortest",    "Video ngắn")
    };

    static readonly (string id, string name)[] SortsCat =
    {
        ("",            "Mới nhất"),
        ("top-weekly",  "Top tuần"),
        ("most-viewed", "Xem nhiều nhất"),
        ("top-rated",   "Đánh giá cao"),
        ("longest",     "Video dài")
    };

    static readonly (string id, string name)[] SortsTag =
    {
        ("",            "Mới nhất"),
        ("top-weekly",  "Top tuần"),
        ("most-viewed", "Xem nhiều nhất"),
        ("top-rated",   "Đánh giá cao"),
        ("longest",     "Video dài"),
        ("shortest",    "Video ngắn")
    };

    // null = context này không sort được (đừng dựng dòng 2)
    public static (string id, string name)[] SortsFor(string search, string c, string t)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return null;

        if (!string.IsNullOrWhiteSpace(c))
            return SortsCat;

        if (!string.IsNullOrWhiteSpace(t))
            return SortsTag;

        return SortsHome;
    }

    public static string ClampSort(string sort, string search, string c, string t)
    {
        var ok = SortsFor(search, c, t);
        if (ok == null)
            return "";

        if (!string.IsNullOrWhiteSpace(sort))
        {
            sort = sort.Trim().ToLowerInvariant();
            foreach (var s in ok)
                if (s.id == sort)
                    return sort;
        }

        return "";
    }

    public static string SortLabel(string sort)
    {
        foreach (var s in SortsHome)
            if (s.id == (sort ?? ""))
                return s.name;

        return "Mới nhất";
    }
    #endregion

    #region Uri
    public static string Uri(string host, string search, string sort, string c, int pg, string t = null)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            // search: site bỏ qua sort, chỉ còn trang
            url.Append("search/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/");

            if (pg > 1)
            {
                url.Append(pg);
                url.Append("/");
            }
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append("cat/");
            url.Append(c);
            url.Append("/");

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append("SORT-");
                url.Append(sort);
                url.Append("/");
            }

            if (pg > 1)
            {
                url.Append(pg);
                url.Append("/");
            }
        }
        else if (!string.IsNullOrEmpty(t))
        {
            url.Append("tag/");
            url.Append(t);
            url.Append("/");

            if (pg > 1)
            {
                url.Append(pg);
                url.Append("/");
            }

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append(sort);
                url.Append("/");
            }
        }
        else
        {
            if (pg > 1)
            {
                url.Append(pg);
                url.Append("/");
            }

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append(sort);
                url.Append("/");
            }
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var single = ReadOnlySpan<char>.Empty;

        if (html.Contains("id=\"relateddiv\"", StringComparison.Ordinal))
            single = HtmlSpan.Node(html, "*", "id", "relateddiv", HtmlSpanTargetType.Exact);

        else if (html.Contains("id=\"vidresults\"", StringComparison.Ordinal))
            single = HtmlSpan.Node(html, "*", "id", "vidresults", HtmlSpanTargetType.Exact);

        else if (html.Contains("class=\"toptopbelinset\"", StringComparison.Ordinal))
            single = Rx.Split("class=\"toptopbelinset\"", html)[1].Span;

        else if (html.Contains("class=\"relatedtext\"", StringComparison.Ordinal))
            single = Rx.Split("class=\"relatedtext\"", html)[1].Span;

        if (single.IsEmpty)
            return null;

        var rx = Rx.Split("<div class=\"mb( hdy)?\"", single, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups("<p class=\"mbtit\"><a href=\"/([^\"]+)\">([^<]+)</a>");

            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                string img = row.Match(" data-src=\"([^\"]+)\"");
                if (img == null)
                    img = row.Match("<img src=\"([^\"]+)\"");

                if (img == null)
                    img = string.Empty;

                var pl = new PlaylistItem()
                {
                    name = g[2].Value,
                    video = $"{route}?uri={g[1].Value}",
                    picture = img,
                    preview = Regex.Replace(img, "/[^/]+$", "") + $"/{row.Match("data-id=\"([^\"]+)\"")}-preview.webm",
                    quality = row.Match("<div class=\"mvhdico\"([^>]+)?><span>([^\"<]+)", 2),
                    time = row.Match("<span class=\"mbtim\"([^>]+)?>([^<]+)</span>", 2, trim: true),
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "epr",
                        href = g[1].Value,
                        image = img
                    }
                };

                if (onplaylist != null)
                    pl = onplaylist.Invoke(pl);

                playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion

    #region Chips
    public static List<(string kind, string slug, string name)> Chips(ReadOnlySpan<char> html)
    {
        var list = new List<(string kind, string slug, string name)>(12);

        if (html.IsEmpty)
            return list;

        // chỉ lấy khối thông tin video, tránh danh mục ở nav/menu
        var node = HtmlSpan.Node(html, "*", "id", "video-info-tags", HtmlSpanTargetType.Exact);
        if (node.IsEmpty)
            return list;

        const string pattern = "<li class=\"vit-(category|tag)\"><a href=\"/(?:cat|tag)/([^\"/]+)/\"[^>]*>([^<]+)</a>";

        foreach (var row in Rx.Matches(pattern, node).Rows())
        {
            var g = row.Groups(pattern);

            string slug = g[2].Value;
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            string kind = g[1].Value == "category" ? "cat" : "tag";

            if (list.Any(i => i.kind == kind && i.slug == slug))
                continue;

            string name = HttpUtility.HtmlDecode(g[3].Value).Trim();
            list.Add((kind, slug, string.IsNullOrEmpty(name) ? slug : name));
        }

        return list;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string search, string sort, string c, string t = null, bool allowSort = true)
    {
        host = string.IsNullOrWhiteSpace(host) ? string.Empty : $"{host}/";
        string url = host + "epr";

        var menu = new List<MenuItem>(3)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = url,
            }
        };

        // dòng 2: chỉ dựng khi context này sort được (search/rel = không)
        var sorts = allowSort ? SortsFor(search, c, t) : null;

        if (sorts != null)
        {
            string link(string s)
            {
                string q;

                if (!string.IsNullOrWhiteSpace(search))
                    q = "search=" + HttpUtility.UrlEncode(search);
                else if (!string.IsNullOrWhiteSpace(c))
                    q = "c=" + HttpUtility.UrlEncode(c);
                else if (!string.IsNullOrWhiteSpace(t))
                    q = "t=" + HttpUtility.UrlEncode(t);
                else
                    q = "";

                return q.Length == 0 ? $"{url}?sort={s}" : $"{url}?{q}&sort={s}";
            }

            menu.Add(new MenuItem()
            {
                title = $"Sắp xếp: {SortLabel(sort)}",
                playlist_url = "submenu",
                submenu = System.Linq.Enumerable.ToList(
                    System.Linq.Enumerable.Select(sorts, s => new MenuItem(s.name, link(s.id))))
            });
        }

        #region danh mục (tĩnh, cache 1 lần)
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Eporner_cats_{host}";

        if (!memoryCache.TryGetValue(menuKey, out List<MenuItem> catmenu))
        {
            catmenu = BuildCats(url);

            if (CoreInit.conf.lowMemoryMode == false)
                memoryCache.Set(menuKey, catmenu, TimeSpan.FromDays(1));
        }
        #endregion

        string catTitle = "tất cả";

        if (!string.IsNullOrEmpty(c))
            catTitle = catmenu.FirstOrDefault(i => i.playlist_url.EndsWith($"c={c}"))?.title ?? c;

        menu.Add(new MenuItem()
        {
            title = string.IsNullOrEmpty(t) ? $"Danh mục: {catTitle}" : $"Thẻ: {t}",
            playlist_url = "submenu",
            submenu = catmenu
        });

        return menu;
    }

    static List<MenuItem> BuildCats(string url)
    {
        return new List<MenuItem>(70)
        {
            new("Tất cả", url),
            new("4K UHD", $"{url}?c=4k-porn"),
            new("60 FPS", $"{url}?c=60fps"),
            new("Amateur", $"{url}?c=amateur"),
            new("Anal", $"{url}?c=anal"),
            new("Asian", $"{url}?c=asian"),
            new("ASMR", $"{url}?c=asmr"),
            new("BBW", $"{url}?c=bbw"),
            new("BDSM", $"{url}?c=bdsm"),
            new("Big Ass", $"{url}?c=big-ass"),
            new("Big Dick", $"{url}?c=big-dick"),
            new("Big Tits", $"{url}?c=big-tits"),
            new("Bisexual", $"{url}?c=bisexual"),
            new("Blonde", $"{url}?c=blonde"),
            new("Blowjob", $"{url}?c=blowjob"),
            new("Bondage", $"{url}?c=bondage"),
            new("Brunette", $"{url}?c=brunette"),
            new("Bukkake", $"{url}?c=bukkake"),
            new("Creampie", $"{url}?c=creampie"),
            new("Cumshot", $"{url}?c=cumshot"),
            new("Double Penetration", $"{url}?c=double-penetration"),
            new("Ebony", $"{url}?c=ebony"),
            new("Fat", $"{url}?c=fat"),
            new("Fetish", $"{url}?c=fetish"),
            new("Fisting", $"{url}?c=fisting"),
            new("Footjob", $"{url}?c=footjob"),
            new("For Women", $"{url}?c=for-women"),
            new("Gay", $"{url}?c=gay"),
            new("Group Sex", $"{url}?c=group-sex"),
            new("Handjob", $"{url}?c=handjob"),
            new("Hardcore", $"{url}?c=hardcore"),
            new("Hentai", $"{url}?c=hentai"),
            new("Homemade", $"{url}?c=homemade"),
            new("Hotel", $"{url}?c=hotel"),
            new("Housewives", $"{url}?c=housewives"),
            new("Indian", $"{url}?c=indian"),
            new("Interracial", $"{url}?c=interracial"),
            new("Japanese", $"{url}?c=japanese"),
            new("Latina", $"{url}?c=latina"),
            new("Lesbian", $"{url}?c=lesbians"),
            new("Lingerie", $"{url}?c=lingerie"),
            new("Massage", $"{url}?c=massage"),
            new("Masturbation", $"{url}?c=masturbation"),
            new("Mature", $"{url}?c=mature"),
            new("MILF", $"{url}?c=milf"),
            new("Nurses", $"{url}?c=nurse"),
            new("Office", $"{url}?c=office"),
            new("Older Men", $"{url}?c=old-man"),
            new("Orgy", $"{url}?c=orgy"),
            new("Outdoor", $"{url}?c=outdoor"),
            new("Petite", $"{url}?c=petite"),
            new("Pornstar", $"{url}?c=pornstar"),
            new("POV", $"{url}?c=pov-porn"),
            new("Public", $"{url}?c=public"),
            new("Redhead", $"{url}?c=redhead"),
            new("Shemale", $"{url}?c=shemale"),
            new("Sleep", $"{url}?c=sleep"),
            new("Small Tits", $"{url}?c=small-tits"),
            new("Squirt", $"{url}?c=squirt"),
            new("Striptease", $"{url}?c=striptease"),
            new("Students", $"{url}?c=students"),
            new("Swinger", $"{url}?c=swingers"),
            new("Teen", $"{url}?c=teens"),
            new("Threesome", $"{url}?c=threesome"),
            new("Toys", $"{url}?c=toys"),
            new("Uncategorized", $"{url}?c=uncategorized"),
            new("Uniform", $"{url}?c=uniform"),
            new("Vintage", $"{url}?c=vintage"),
            new("Webcam", $"{url}?c=webcam")
        };
    }
    #endregion

    #region StreamLinks
    async public static Task<StreamItem> StreamLinks(HttpHydra http, string route, string host, string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        string vid = null, hash = null;
        List<PlaylistItem> recomends = null;

        await http.GetSpan($"{host}/{url}", html =>
        {
            vid = Rx.Match(html, "vid ?= ?'([^']+)'");
            hash = Rx.Match(html, "hash ?= ?'([^']+)'");

            if (vid != null && hash != null)
                recomends = Playlist(route, html);
        });

        if (vid == null || hash == null)
            return null;

        var stream_links = new Dictionary<string, string>(5);

        await http.GetSpan($"{host}/xhr/video/{vid}?hash={convertHash(hash)}&domain={Regex.Replace(host, "^https?://", "")}&fallback=false&embed=false&supportedFormats=dash,mp4&_={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}", json =>
        {
            foreach (var row in Rx.Matches("\"src\":( +)?\"(https?://[^/]+/[^\"]+-([0-9]+p).mp4)\",", json).Rows())
            {
                var g = row.Groups("\"src\":( +)?\"(https?://[^/]+/[^\"]+-([0-9]+p).mp4)\",");

                if (!string.IsNullOrEmpty(g[3].Value))
                    stream_links.TryAdd(g[3].Value, g[2].Value);
            }
        });

        return new StreamItem()
        {
            qualitys = stream_links,
            recomends = recomends
        };
    }
    #endregion


    #region convertHash
    static string convertHash(string h)
    {
        var builder = StringBuilderPool.ThreadInstance;

        Base36(h.AsSpan(0, 8), builder);
        Base36(h.AsSpan(8, 8), builder);
        Base36(h.AsSpan(16, 8), builder);
        Base36(h.AsSpan(24, 8), builder);

        return builder.ToString();
    }
    #endregion

    #region Base36
    static void Base36(ReadOnlySpan<char> hex, StringBuilder builder)
    {
        // Парсинг 8 hex-символов без Substring и без аллокаций
        ulong value = ulong.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

        const int Base = 36;
        const string Chars = "0123456789abcdefghijklmnopqrstuvwxyz";

        int start = builder.Length;

        if (value == 0)
        {
            builder.Append('0');
            return;
        }

        while (value > 0)
        {
            builder.Append(Chars[(int)(value % Base)]);
            value /= Base;
        }

        // Разворот добавленного участка (т.к. цифры добавлялись в обратном порядке)
        int i = start;
        int j = builder.Length - 1;
        while (i < j)
        {
            (builder[i], builder[j]) = (builder[j], builder[i]);
            i++;
            j--;
        }
    }
    #endregion
}
