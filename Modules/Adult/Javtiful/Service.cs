using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Javtiful;

public static class JavtifulTo
{
    public static string SiteHost = "https://javtiful.com";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    static string SlugifyVi(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim().ToLowerInvariant();
        string[][] map = new string[][]
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
        {
            foreach (char ch in pair[1])
                s = s.Replace(ch.ToString(), pair[0]);
        }
        s = Regex.Replace(s, @"[^a-z0-9\s-]", " ");
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        string page = pg > 1 ? "?page=" + pg : "";

        if (!string.IsNullOrWhiteSpace(search))
        {
            // search HOA tung tra 502 -> slugify lowercase luon
            string slug = SlugifyVi(search);
            if (string.IsNullOrEmpty(slug))
                slug = HttpUtility.UrlEncode(search);
            return host + "/vn/search?q=" + slug;
        }

        if (!string.IsNullOrEmpty(c))
        {
            string url = c.StartsWith("http") ? c : host + "/vn/" + c.Trim('/');
            string q = pg > 1 ? (url.Contains("?") ? "&" : "?") + "page=" + pg : "";

            // Sort cua site la QUERY `?sort=popular_week|popular_month|
            // popular_day|popular`, dat sau duong dan co phan trang.
            if (!string.IsNullOrWhiteSpace(sort))
                q += (q.Length == 0 ? "?" : "&") + "sort=" + sort.Trim();

            return url + q;
        }

        // home /vn khong phan trang (?page= bi bo qua) -> dung feed /vn/foryou
        string sq = page;
        if (!string.IsNullOrWhiteSpace(sort))
            sq += (sq.Length == 0 ? "?" : "&") + "sort=" + sort.Trim();

        return host + "/vn/foryou" + sq;
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();
        foreach (string raw in html.Split(new[] { "<article class=\"front-video-card\">" }, StringSplitOptions.None))
        {
            string b = raw.Length > 5000 ? raw.Substring(0, 5000) : raw;

            var hm = Regex.Match(b, @"<a\s+href=""(/vn/video/[^""]+)""\s+class=""front-video-thumb""");
            if (!hm.Success)
                continue;
            string href = SiteHost + hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            var tm = Regex.Match(b, @"class=""front-video-title"">([^<]{4,200})<");
            if (!tm.Success)
                continue;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, @"data-front-lazy-src=""([^""]+)""");
            if (im.Success)
            {
                poster = im.Groups[1].Value;
                if (poster.StartsWith("/"))
                    poster = SiteHost + poster;
            }

            string preview = "";
            var pv = Regex.Match(b, @"data-front-video-preview-src=""([^""]+)""");
            if (pv.Success)
                preview = pv.Groups[1].Value;

            var item = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javtiful",
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

    // video page -> <source src="...mp4"> direct (fast-stream.jav.si)
    public static List<string> StreamUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, @"<source[^>]*src=""(https?://[^""]+)"""))
        {
            string u = m.Groups[1].Value;
            if (seen.Add(u))
                urls.Add(u);
        }

        return urls;
    }

    public static string VideoPoster(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var m = Regex.Match(html, @"<video[^>]*poster=""([^""]+)""");
        if (!m.Success)
            return "";
        string p = m.Groups[1].Value;
        if (p.StartsWith("/"))
            p = SiteHost + p;
        return p;
    }

    // /vn/channels: card kenh (hieu) 24/ trang, 13 trang = 312 kenh.
    // Card: <a class="front-collection-card-link" href="/vn/channel/<slug>">
    //        ... <strong class="front-collection-title">NAME</strong>
    public static List<(string name, string slug)> ChannelList(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        foreach (Match m in Regex.Matches(html,
            "<a class=\"front-collection-card-link\" "
            + "href=\"/vn/channel/([a-z0-9\\-]+)\">(.*?)</a>",
            RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value;
            if (string.IsNullOrEmpty(slug))
                continue;

            var tm = Regex.Match(m.Groups[2].Value,
                "<strong class=\"front-collection-title\">"
                + "([^<]{1,80})</strong>");
            string name = tm.Success
                ? HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim())
                : slug;
            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((name, slug));
        }

        return list;
    }

    // So trang cua /vn/channels: <a ... href="/vn/channels?page=13">13</a>
    public static int ChannelPages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html,
            "/vn/channels\\?page=(\\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int p)
                && p > max)
                max = p;
        }

        return max > 20 ? 20 : max;
    }

    // ===== DÒNG 2 "Sắp xếp" — sort CHO LIST ĐANG MỞ (chuẩn 9g) =====
    // BẢN CŨ SAI: dòng 2 chứa 2 muc KHONG phai sort
    //   ("Mới nhất" -> ?c=videos, "JAVMost" -> ?c=javmost) va 3 muc
    //   list x sort (?c=videos&sort=...) — ca hai deu lam mat `c` dang mo.
    //   2 muc do chuyen sang dong 3 "Danh sach khac"; cac muc list x sort
    //   bo het (dong 2 da lam duoc viec do).
    //
    // Sort THAT (fetch truc tiep javtiful.com, do bo ID trang 1, 2026-10-06):
    //   /vn/videos /vn/censored /vn/uncensored /vn/reducing-mosaic
    //     -> popular, popular_today, popular_week, popular_month,
    //        most_viewed, most_liked  (6/6 doi list, khong muc nao rong)
    //     LOAI: added_week + added_month -> trung ca trang 1 & 2 (no-op)
    //           added_today  -> chi 1-17 phim, gan 0 se bi 503
    //   /vn/category/* , /vn/channel/* , /vn/javmost
    //     -> popular (10/10 category, 6/6 channel doi list)
    //     LOAI: added_today  -> 12/21 category RONG (503)
    //           added_week   -> 3/21 category RONG (503)
    //           added_month  -> trang 1 TRUNG o 6/10 category + 3/6 channel
    //           popular_week, most_viewed -> link NAV global tro sang
    //              /vn/videos?sort=... -> appended vao day = no-op
    //   /vn/foryou (home), /vn/search?q= -> KHONG sort duoc
    //     (do trang 1 + trang 2, moi gia tri deu y hệt ban khong sort)
    public static readonly string[] SortFullCs =
        { "videos", "censored", "uncensored", "reducing-mosaic" };

    public static readonly (string name, string sort)[] SortsFull =
    {
        ("Mặc định",           ""),
        ("Phổ biến",           "popular"),
        ("Phổ biến hôm nay",   "popular_today"),
        ("Phổ biến tuần này",  "popular_week"),
        ("Phổ biến tháng này", "popular_month"),
        ("Xem nhiều nhất",     "most_viewed"),
        ("Được thích nhiều nhất", "most_liked"),
    };

    public static readonly (string name, string sort)[] SortsPop =
    {
        ("Mặc định", ""),
        ("Phổ biến", "popular"),
    };

    // Tap sort AP DUOC cho context nay. null = site khong sort duoc o day
    // (home / search / c la URL day du / c la trang khac) -> dong 2 chi con
    // muc "Mac dinh" (xem MenuHead).
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;   // /vn/search?q= -> khong sort
        if (string.IsNullOrWhiteSpace(c)) return null;         // /vn/foryou    -> khong sort

        string cc = c;
        if (cc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            int i = cc.IndexOf("/vn/", StringComparison.OrdinalIgnoreCase);
            cc = i >= 0 ? cc.Substring(i + 4) : cc;
        }
        cc = cc.Trim('/');

        foreach (var x in SortFullCs)
            if (cc == x) return SortsFull;

        if (cc.StartsWith("category/") || cc.StartsWith("channel/") || cc == "javmost")
            return SortsPop;

        return null;
    }

    // Gop sort ve dung tap cua context — `sort=popular_week` o category
    // la no-op da do, khong cho phep.
    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in SortsFull) if (s == sort) return s;
        foreach (var (_, s) in SortsPop) if (s == sort) return s;
        return null;
    }

    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null) return null;
        sort = NormalizeSort(sort);
        if (string.IsNullOrEmpty(sort)) return null;
        foreach (var o in opts) if (o.sort == sort) return sort;
        return null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mặc định"
        : sort == "popular" ? "phổ biến"
        : sort == "popular_today" ? "phổ biến hôm nay"
        : sort == "popular_week" ? "phổ biến tuần này"
        : sort == "popular_month" ? "phổ biến tháng này"
        : sort == "most_viewed" ? "xem nhiều nhất"
        : sort == "most_liked" ? "được thích nhiều nhất" : sort;

    // ===== head cua menu: phu thuoc search/sort/c -> dung lai moi request =====
    // Context KHONG sort duoc (home /vn/foryou, /vn/search?q= — da do trang 1
    // + trang 2 moi gia tri deu trung) -> KHONG hien dong 2. Row 2 chet (bam
    // gi cung khong doi) con te hon row 2 vang.
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/javtiful";
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
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });
        return res;
    }

    // ===== base cua menu: KHONG phu thuoc search/sort/c -> cache 1 lan =====
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host,
        List<(string name, string slug)> channels = null)
    {
        var genres = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Ngoại tình", host + "/javtiful?c=category/affair"),
            new("Nghiệp dư", host + "/javtiful?c=category/amateur"),
            new("Gái xinh", host + "/javtiful?c=category/beautiful-girl"),
            new("Ngực to", host + "/javtiful?c=category/big-tits"),
            new("BBW", host + "/javtiful?c=category/bbw"),
            new("AV Trung Quốc", host + "/javtiful?c=category/chinese-av"),
            new("Cosplay", host + "/javtiful?c=category/cosplay"),
            new("Kịch", host + "/javtiful?c=category/drama"),
            new("Nữ sếp", host + "/javtiful?c=category/female-boss"),
            new("Nữ điều tra", host + "/javtiful?c=category/female-investigator"),
            new("Nữ sinh", host + "/javtiful?c=category/female-student"),
            new("Cô giáo", host + "/javtiful?c=category/female-teacher"),
            new("Học sinh", host + "/javtiful?c=category/school-girls"),
            new("Chị dâu", host + "/javtiful?c=category/sister-in-law"),
            new("Giúp việc", host + "/javtiful?c=category/housekeeper"),
            new("Y tá", host + "/javtiful?c=category/nurse"),
            new("Dân văn phòng", host + "/javtiful?c=category/office-lady"),
            new("Thôi miên", host + "/javtiful?c=category/hypnosis"),
            new("Vợ người ta", host + "/javtiful?c=category/married-woman"),
            new("Quý bà", host + "/javtiful?c=category/mature-woman"),
            new("MILF", host + "/javtiful?c=category/milf"),
        };

        // DÒNG 1 + DÒNG 2 do MenuHead() dung rieng moi request (phu thuoc
        // c/search/sort). Day chi con cac nhom dong 3+ (context-free).
        //
        // 2 muc TRUOC DAY o dong 2 khong phai sort:
        //   "Mới nhất" -> ?c=videos (1 list), "JAVMost" -> ?c=javmost (1 list)
        //   => chuyen vao nhom "Danh sach khac" nay cho dung quy tac.
        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh sách khác",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất (tất cả phim)", host + "/javtiful?c=videos"),
                    new("JAVMost", host + "/javtiful?c=javmost"),
                }
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Kiểm duyệt",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Có che", host + "/javtiful?c=censored"),
                    new("Không che", host + "/javtiful?c=uncensored"),
                    new("Giảm mosaic", host + "/javtiful?c=reducing-mosaic"),
                }
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres
            }
        };

        if (channels != null && channels.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Kênh",
                playlist_url = "submenu",
                submenu = channels.Select(ch =>
                    new Shared.Models.SISI.Base.MenuItem(
                        ch.name,
                        host + "/javtiful?c=channel/" + ch.slug
                    )).ToList()
            });
        }

        return menu;
    }
}
