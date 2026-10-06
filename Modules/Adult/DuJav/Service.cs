using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace DuJav;

public static class DuJavTo
{
    public static string SiteHost = "https://d.dujav.com";
    const string Lang = "/vi";

    public const string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        var tail = new List<string>(2);
        if (pg > 1) tail.Add("page=" + pg);
        if (!string.IsNullOrWhiteSpace(sort)) tail.Add("sort=" + sort.Trim());
        string tailStr(List<string> t, string first)
        {
            if (t.Count == 0) return "";
            return first + string.Join("&", t);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string u = host + Lang + "/search?q=" + System.Uri.EscapeDataString(search.Trim());
            return u + tailStr(tail, "&");
        }

        string path;
        if (string.IsNullOrWhiteSpace(c))
        {
            path = Lang;
        }
        else
        {
            string cc = c.Trim();
            if (cc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try { cc = new System.Uri(cc).AbsolutePath; } catch { }
            }
            // cat bo prefix ngon ngu neu co (/vi/censored -> censored)
            cc = Regex.Replace(cc, @"^/[a-z]{2}(/|$)", "/").Trim('/');
            path = Lang + "/" + cc;
        }

        return host + path + tailStr(tail, "?");
    }

    // Card: <a ... data-video-id="118114" href="/vi/uncen-leak/slug"><article>
    //         <img src="/images/..." alt="Ten phim"> ... (khong co <a> long nhau)
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match card in Regex.Matches(html,
            @"<a\b[^>]*\bdata-video-id=""\d+""[^>]*\bhref=""([^""]+)""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase))
        {
            string href = card.Groups[1].Value.Trim();
            if (href.StartsWith("//"))
                href = "https:" + href;
            else if (href.StartsWith("/"))
                href = SiteHost + href;
            else if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seen.Add(href))
                continue;

            string inner = card.Groups[2].Value;

            string poster = null;
            var pm = Regex.Match(inner, @"<img\b[^>]*\bsrc=""([^""]+)""", RegexOptions.IgnoreCase);
            if (pm.Success)
            {
                poster = pm.Groups[1].Value.Trim();
                if (poster.StartsWith("//"))
                    poster = "https:" + poster;
                else if (poster.StartsWith("/"))
                    poster = SiteHost + poster;
            }

            string name = "";
            var nm = Regex.Match(inner, @"<img\b[^>]*\balt=""([^""]*)""", RegexOptions.IgnoreCase);
            if (nm.Success)
                name = HttpUtility.HtmlDecode(nm.Groups[1].Value).Trim();
            if (string.IsNullOrEmpty(name))
            {
                try { name = System.Uri.UnescapeDataString(href.TrimEnd('/').Split('/')[^1]).Replace("-", " "); }
                catch { name = href; }
            }

            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "dujav",
                    href = href,
                    image = poster
                }
            });
        }

        return list;
    }

    // Taxonomy tu JSON-LD BreadcrumbList (vi du trang /vi/topics, /vi/stars):
    //   {"@type":"ListItem","position":1,"name":"hd","url":".../vi/topics/hd"}
    // Parse THEO BLOCK (thu tu field name/url khong on dinh) roi trich rieng.
    // kind = "topics" | "stars" -> path tuong doi "topics/hd", "stars/ai-sayama".
    // Ten topic tren site la slug ("sailor-suit") -> doi sang Title Case cho
    // de doc; ten dien vien da dep thi giu nguyen.
    public static List<(string name, string path)> Taxonomies(string html, string kind)
    {
        var res = new List<(string name, string path)>();
        if (string.IsNullOrEmpty(html))
            return res;

        string prefix = Lang + "/" + kind + "/";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match b in Regex.Matches(html,
            @"\{[^{}]*?""@type"":""ListItem""[^{}]*?\}",
            RegexOptions.IgnoreCase))
        {
            string block = b.Groups[0].Value;
            var nm = Regex.Match(block, @"""name"":""([^""]+)""");
            var um = Regex.Match(block, @"""url"":""([^""]+)""");
            if (!nm.Success || !um.Success)
                continue;

            int i = um.Groups[1].Value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                continue;
            string slug = um.Groups[1].Value.Substring(i + prefix.Length).Trim('/');
            if (string.IsNullOrEmpty(slug) || slug.Contains("/") || !seen.Add(slug))
                continue;

            string name;
            try { name = Regex.Unescape(nm.Groups[1].Value).Trim(); }
            catch { name = nm.Groups[1].Value.Trim(); }
            if (!name.Contains(" ") && name.Contains("-"))
            {
                // slug -> "Sailor Suit" (khong che ten, chi doi dau -)
                var words = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
                for (int w = 0; w < words.Length; w++)
                    if (words[w].Length > 0)
                        words[w] = char.ToUpperInvariant(words[w][0]) + words[w].Substring(1);
                name = string.Join(" ", words);
            }
            if (string.IsNullOrEmpty(name))
                name = slug;

            res.Add((name, kind + "/" + slug));
        }

        return res;
    }

    // Player 3 buoc (do 2026-10-06):
    //   trang phim -> <iframe src="/watch/<slug>">
    //   GET /api/player/<slug> (+Referer trang phim) -> {"url":"/watch/t/<token>"}
    //   GET /watch/t/<token> -> jwplayer + https://...m3u8 (token dung lai duoc,
    //   m3u8 khong doi Referer)
    public static async Task<string> ResolveM3U8(string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(pageUrl))
            return null;

        string page;
        try
        {
            page = await Http.Get(pageUrl, timeoutSeconds: 12,
                headers: HeadersModel.Init(("User-Agent", ChromeUA)));
        }
        catch { return null; }

        var f = Regex.Match(page ?? "", @"<iframe\b[^>]*\bsrc=""(/watch/[^""]+)""",
            RegexOptions.IgnoreCase);
        if (!f.Success)
            return null;
        string wslug = f.Groups[1].Value.Substring("/watch/".Length);

        string api;
        try
        {
            api = await Http.Get(SiteHost + "/api/player/" + wslug, timeoutSeconds: 12,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", pageUrl)));
        }
        catch { return null; }

        var tok = Regex.Match(api ?? "", @"""url"":""([^""]+)""");
        if (!tok.Success)
            return null;

        string tokPage;
        try
        {
            tokPage = await Http.Get(SiteHost + tok.Groups[1].Value, timeoutSeconds: 12,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", pageUrl)));
        }
        catch { return null; }

        var m = Regex.Match(tokPage ?? "", @"(https?:)?//[^""\s]+\.m3u8[^""\s]*");
        if (!m.Success)
            return null;

        string u = m.Groups[0].Value;
        if (u.StartsWith("//"))
            u = "https:" + u;
        return u;
    }

    // Sort that cua site — do 2026-10-06 truc tiep (so ca set + sequence):
    //   home + category (censored, topics/hd, trending) + actor (n=12):
    //     updated_at/today_view/weekly_view/monthly_view/total_view DOI het
    //   popular: chi updated_at + published_at song (4 sort view = TRUNG vi
    //     trang nay von da xep theo view); published_at = "Ngày phát hành"
    //   search: tat ca TRUNG -> khong sort duoc
    public static readonly (string name, string sort)[] SortsHome =
    {
        ("Mặc định",            ""),
        ("Cập nhật gần đây",    "updated_at"),
        ("Lượt xem hằng ngày",  "today_view"),
        ("Lượt xem hằng tuần",  "weekly_view"),
        ("Lượt xem hằng tháng", "monthly_view"),
        ("Lượt xem hằng năm",   "total_view"),
    };

    public static readonly (string name, string sort)[] SortsPopular =
    {
        ("Mặc định",         ""),
        ("Cập nhật gần đây", "updated_at"),
        ("Ngày phát hành",   "published_at"),
    };

    // Tap sort AP DUOC cho context nay. null = site khong sort duoc o day
    // (search; trang index stars/topics khong co video) -> KHONG hien dong 2.
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;
        if (string.IsNullOrWhiteSpace(c)) return SortsHome;

        string cc = c.Trim();
        if (cc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            try { cc = new System.Uri(cc).AbsolutePath; } catch { }
        }
        cc = Regex.Replace(cc, @"^/[a-z]{2}(/|$)", "/").Trim('/');

        if (cc == "popular") return SortsPopular;
        if (cc == "stars" || cc == "topics") return null;  // trang index: khong co video
        return SortsHome;
    }

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in SortsHome) if (s == sort) return s;
        foreach (var (_, s) in SortsPopular) if (s == sort) return s;
        return null;
    }

    // Gop + kiem tra sort co DUOC phep o context nay khong (khong thi ve null)
    public static string ClampSort(string sort, string search, string c)
    {
        sort = NormalizeSort(sort);
        if (string.IsNullOrEmpty(sort)) return null;
        var opts = SortsFor(search, c);
        if (opts == null) return null;
        foreach (var o in opts) if (o.sort == sort) return sort;
        return null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mặc định"
        : sort == "updated_at" ? "cập nhật gần đây"
        : sort == "today_view" ? "lượt xem hằng ngày"
        : sort == "weekly_view" ? "lượt xem hằng tuần"
        : sort == "monthly_view" ? "lượt xem hằng tháng"
        : sort == "total_view" ? "lượt xem hằng năm"
        : sort == "published_at" ? "ngày phát hành" : sort;

    // ===== head cua menu: phu thuoc search/sort/c -> dung lai moi request =====
    // Context KHONG sort duoc (search — da do tat ca TRUNG) -> KHONG hien dong 2.
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/dujav";
        string link(string s)
        {
            string q;
            if (!string.IsNullOrWhiteSpace(search)) q = "search=" + HttpUtility.UrlEncode(search);
            else if (!string.IsNullOrWhiteSpace(c)) q = "c=" + HttpUtility.UrlEncode(c);
            else q = "";
            if (!string.IsNullOrEmpty(s)) q += (q.Length == 0 ? "" : "&") + "sort=" + s;
            return q.Length == 0 ? root : root + "?" + q;
        }

        var res = new List<Shared.Models.SISI.Base.MenuItem>(2)
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };

        var opts = SortsFor(search, c);
        if (opts == null || opts.Length == 0)
            return res;

        var sub = new List<Shared.Models.SISI.Base.MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
            sub.Add(new(name, link(s)));

        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });
        return res;
    }

    // ===== base: KHONG phu thuoc search/sort/c -> cache dung 1 lan =====
    // KHONG co nhom Dien vien: site co ~38k dien vien, lay vai chuc nguoi
    // top vao menu vua thieu vua kho chiu -> bo han (2026-10-06).
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host, IReadOnlyList<(string name, string path)> topics)
    {
        host = host.TrimEnd('/');
        string cat(string c) => host + "/dujav?c=" + HttpUtility.UrlEncode(c);

        var root = new List<Shared.Models.SISI.Base.MenuItem>();

        // Dong 3: list toan cuc (URL rieng, khong phai sort) — xem 9g #3
        root.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = "Danh sách",
            playlist_url = "submenu",
            submenu = new List<Shared.Models.SISI.Base.MenuItem>()
            {
                new("Thịnh hành", cat("trending")),
                new("Phổ biến", cat("popular")),
            }
        });

        root.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = "Kiểm duyệt",
            playlist_url = "submenu",
            submenu = new List<Shared.Models.SISI.Base.MenuItem>()
            {
                new("Có che", cat("censored")),
                new("Không che", cat("uncensored")),
                new("Rò rỉ không che", cat("uncen-leak")),
                new("Nghiệp dư", cat("amateur")),
                new("Trung Quốc", cat("chinese-av")),
                new("Có phụ đề", cat("subtitle")),
            }
        });

        if (topics != null && topics.Count > 0)
        {
            // ~500 topic -> chia bucket theo chu cai (moi submenu <= 300,
            // client SISI chi 1 tang)
            if (topics.Count > 300)
                root.AddRange(DirBuckets(host, "Thể loại", topics));
            else
            {
                var sub = new List<Shared.Models.SISI.Base.MenuItem>(topics.Count);
                foreach (var (name, path) in topics)
                    sub.Add(new(name, cat(path)));
                root.Add(new Shared.Models.SISI.Base.MenuItem()
                {
                    title = "Thể loại",
                    playlist_url = "submenu",
                    submenu = sub
                });
            }
        }

        return root;
    }

    public const int MaxPerBucket = 300;

    // Chia taxonomy lon thanh nhieu submenu theo chu cai (moi cai <= 300).
    // Copy tu JavCt (da verify tren app): gom theo ky tu dau, flatten roi cat
    // chunk; chunk cat giua mot chu cai thi them (1/2),(2/2).
    public static List<Shared.Models.SISI.Base.MenuItem> DirBuckets(
        string host, string title,
        IReadOnlyList<(string name, string path)> all, int maxPer = MaxPerBucket)
    {
        host = host.TrimEnd('/');
        var res = new List<Shared.Models.SISI.Base.MenuItem>();
        if (all == null || all.Count == 0)
            return res;

        var groups = new Dictionary<char, List<(string name, string path)>>();
        foreach (var it in all)
        {
            if (string.IsNullOrWhiteSpace(it.name))
                continue;
            char c = char.ToUpperInvariant(it.name.Trim()[0]);
            if (c < 'A' || c > 'Z')
                c = '#';
            if (!groups.TryGetValue(c, out var lst))
            {
                lst = new List<(string, string)>();
                groups[c] = lst;
            }
            lst.Add(it);
        }

        var keys = groups.Keys.OrderBy(CharRank).ToList();

        var flat = new List<(char key, string name, string path)>(all.Count);
        foreach (var k in keys)
            foreach (var it in groups[k])
                flat.Add((k, it.name, it.path));

        var from = new List<char>();
        var to = new List<char>();
        var subs = new List<List<Shared.Models.SISI.Base.MenuItem>>();
        for (int i = 0; i < flat.Count; i += maxPer)
        {
            int n = Math.Min(maxPer, flat.Count - i);
            from.Add(flat[i].key);
            to.Add(flat[i + n - 1].key);
            subs.Add(flat.Skip(i).Take(n).Select(x =>
                new Shared.Models.SISI.Base.MenuItem(
                    x.name, host + "/dujav?c=" + HttpUtility.UrlEncode(x.path))).ToList());
        }

        for (int k = 0; k < from.Count; k++)
        {
            string name = title + " " + from[k]
                + (to[k] == from[k] ? "" : "–" + to[k]);
            if (to[k] == from[k])
            {
                int parts = from.Count(c => c == from[k]);
                if (parts > 1)
                {
                    int nth = 1;
                    for (int q = 0; q < k; q++)
                        if (from[q] == from[k])
                            nth++;
                    name += $" ({nth}/{parts})";
                }
            }
            res.Add(new Shared.Models.SISI.Base.MenuItem(name, "submenu")
            { submenu = subs[k] });
        }

        return res;
    }

    // `#` < 0 < 1 < ... < 9 < A < ... < Z
    static int CharRank(char c)
    {
        if (c == '#') return -1;
        if (c >= '0' && c <= '9') return c;
        return 1000 + c;
    }
}
