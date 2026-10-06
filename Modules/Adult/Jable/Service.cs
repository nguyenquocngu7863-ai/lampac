using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Jable;

public static class JableTo
{
    public static string SiteHost = "https://en.jable.tv";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    // Poster/anh: assets-cdn.jable.tv/contents/videos_screenshots/<id2>/<id>/...
    public static string PosterHost = "https://assets-cdn.jable.tv";

    // The nao goc cua card trong list. Moi the bat dau bang 1 div nay,
    // cac div ben trong deu co the lon hon -> tach theo marker, khong theo
    // </div> (se cat ngan).
    const string CardMark = "<div class=\"col-6 col-sm-4 col-lg-3\">";

    // Trang phim chi co MOT video/phan, phan trang la duong dan khong query:
    //   /latest-updates/2/ , /hot/2/ , /categories/<slug>/2/ ,
    //   /tags/<slug>/2/ , /search/<q>/2/
    // KHONG dung ?page=2 — no echo lai trang 1.
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        string baseUrl;

        if (!string.IsNullOrWhiteSpace(search))
        {
            baseUrl = host + "/search/" + HttpUtility.UrlEncode(search.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string p = c.Trim();
            if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                baseUrl = p.TrimEnd('/');
            else if (p.StartsWith("categories/", StringComparison.OrdinalIgnoreCase)
                  || p.StartsWith("tags/", StringComparison.OrdinalIgnoreCase))
                baseUrl = host + "/" + p.Trim('/');
            else
                baseUrl = host + "/" + p.Trim('/');
        }
        else
        {
            baseUrl = host + "/latest-updates";
        }

        // BAT BUOC trailing slash. Khong co "/" cuoi -> 404 NotFound
        // (Lampac tra 404, khong phai bi chan).
        string u = baseUrl.TrimEnd('/') + (pg > 1 ? "/" + pg : "") + "/";

        // Trang /hot/ co 4 kieu xep hang bang `?sort_by=` (AJAX tab cua
        // web): video_viewed / _month / _week / _today. Phai DAT SAU
        // duong dan co phan trang: /hot/2/?sort_by=...
        if (!string.IsNullOrWhiteSpace(sort))
            u += "?sort_by=" + sort.Trim();

        return u;
    }

    // CAC KIỂU SẮP XẾP that cua site — do truc tiep 2026-10-06 (so SEQUENCE,
    // co control; bai hoc MissAV: cung set nhung dao thu tu van la SONG):
    //   home (latest-updates, c rong) -> sort chet het (video_viewed/today
    //     == base, seq y het) -> AN DONG 2
    //   search (?/search/) -> sort chet (today == base) -> AN DONG 2
    //   hot -> video_viewed/today/month SONG; week == base (chet) -> 3 muc
    //   new-release, categories/*, tags/* -> ca 4 SONG (week==month o cat
    //     nhung ca 2 deu khac base/all/today -> giu ca 2, do site quyet)
    public static readonly string[] SortWhitelist =
    {
        "video_viewed", "video_viewed_today",
        "video_viewed_week", "video_viewed_month",
    };

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    public static readonly (string name, string sort)[] HotSorts =
    {
        ("Hot tất cả",   "video_viewed"),
        ("Hot hôm nay",  "video_viewed_today"),
        ("Hot tháng này","video_viewed_month"),
    };

    public static readonly (string name, string sort)[] TaxSorts =
    {
        ("Mặc định",    ""),
        ("Xem nhiều",   "video_viewed"),
        ("Hot hôm nay", "video_viewed_today"),
        ("Hot tuần này","video_viewed_week"),
        ("Hot tháng này","video_viewed_month"),
    };

    public static bool IsTaxonomy(string c) =>
        !string.IsNullOrWhiteSpace(c) &&
        (c.StartsWith("categories/", StringComparison.OrdinalIgnoreCase) ||
         c.StartsWith("tags/", StringComparison.OrdinalIgnoreCase));

    // Tap sort AP DUOC cho context. null = site khong sort duoc -> an dong 2.
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;
        if (string.IsNullOrWhiteSpace(c)) return null;   // home latest-updates
        if (c.Equals("hot", StringComparison.OrdinalIgnoreCase)) return HotSorts;
        if (c.Equals("new-release", StringComparison.OrdinalIgnoreCase) ||
            IsTaxonomy(c)) return TaxSorts;
        return null;
    }

    // Gop sort ve dung tap cua context TRUOC khi vao cache key + Uri.
    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null) return null;
        sort = NormalizeSort(sort);
        if (string.IsNullOrEmpty(sort))
            return opts.Any(o => string.IsNullOrEmpty(o.sort)) ? "" : opts[0].sort;
        foreach (var o in opts) if (o.sort == sort) return sort;
        return opts.Any(o => string.IsNullOrEmpty(o.sort)) ? "" : opts[0].sort;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mặc định"
        : sort == "video_viewed" ? "xem nhiều"
        : sort == "video_viewed_today" ? "hot hôm nay"
        : sort == "video_viewed_week" ? "hot tuần này"
        : sort == "video_viewed_month" ? "hot tháng này" : sort;

    // HEAD: dong 1 + 2, phu thuoc search/sort/c -> dung lai moi request.
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/jable";
        var res = new List<MenuItem>(2)
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };

        var opts = SortsFor(search, c);
        if (opts == null || opts.Length == 0) return res;

        var sub = new List<MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
        {
            // Dang o home/search thi giu search; dang o c thi giu c, chi doi sort.
            string q;
            if (!string.IsNullOrWhiteSpace(search))
                q = "search=" + HttpUtility.UrlEncode(search);
            else
                q = "c=" + c.Trim('/');
            if (!string.IsNullOrEmpty(s)) q += "&sort=" + s;
            sub.Add(new MenuItem(name, root + "?" + q));
        }
        res.Add(new MenuItem() { title = $"Sắp xếp: {SortLabel(sort)}", playlist_url = "submenu", submenu = sub });
        return res;
    }

    // <h6 class="title"><a href="...videos/slug/">TIEU DE</a></h6>
    // <div class="absolute-bottom-right"><span class="label">2:05:33</span>
    // <img ... data-src=".../62000/62326/320x180/1.jpg" data-preview="...">
    // <span ... data-fav-video-id="62326">
    public static List<PlaylistItem> Playlist(string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var parts = Regex.Split(html, CardMark);
        for (int i = 1; i < parts.Length; i++)
        {
            string block = parts[i];

            var href = Regex.Match(block, @"<a\b[^>]*\bhref\s*=\s*[""']([^""']*?/videos/[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!href.Success)
                continue;

            string uri = href.Groups[1].Value.Trim();
            if (uri.StartsWith("//"))
                uri = "https:" + uri;
            else if (uri.StartsWith("/"))
                uri = SiteHost + uri;

            if (!seen.Add(uri))
                continue;

            var title = Regex.Match(block,
                @"<h6\b[^>]*\bclass\s*=\s*[""'][^""']*\btitle\b[^""']*[""'][^>]*>\s*<a\b[^>]*>(.*?)</a\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!title.Success)
                continue;

            string name = Clean(HttpUtility.HtmlDecode(
                Regex.Replace(title.Groups[1].Value, "<[^>]+>", " ")));
            if (name.Length == 0)
                continue;

            // So video: data-fav-video-id="62326". Dung no de ve poster
            // ban day du (320x180/1.jpg -> <id2>/<id>/preview.jpg).
            string poster = "";
            var vid = Regex.Match(block, @"data-fav-video-id\s*=\s*[""'](\d+)[""']",
                RegexOptions.IgnoreCase);
            if (vid.Success && int.TryParse(vid.Groups[1].Value, out int id))
            {
                int id2 = id / 1000 * 1000;
                poster = PosterHost + "/contents/videos_screenshots/"
                    + id2.ToString(CultureInfo.InvariantCulture) + "/" +
                    id.ToString(CultureInfo.InvariantCulture) + "/preview.jpg";
            }

            if (string.IsNullOrEmpty(poster))
            {
                var img = Regex.Match(block, @"<img\b[^>]*\bdata-src\s*=\s*[""']([^""']+)[""']",
                    RegexOptions.IgnoreCase);
                if (img.Success)
                {
                    poster = img.Groups[1].Value.Trim();
                    if (poster.StartsWith("//"))
                        poster = "https:" + poster;
                }
            }

            string time = "";
            var dur = Regex.Match(block,
                @"<div\b[^>]*\babsolute-bottom-right\b[^>]*>\s*<span\b[^>]*>([^<]{3,12})</span\s*>",
                RegexOptions.IgnoreCase);
            if (dur.Success)
                time = Clean(dur.Groups[1].Value);

            list.Add(new PlaylistItem()
            {
                video = "jable/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                name = name,
                picture = poster,
                time = time,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "jable",
                    href = uri,
                    image = poster
                }
            });
        }

        return list;
    }

    // var hlsUrl = 'https://hot-box-gen.mushroomtrack.com/hls/<token>/<ts>/<id2>/<id>/<id>.m3u8';
    // Host doi random moi lan load (hot-box-gen / home-clone-clear / ...)
    // nen phai lay NGUYEN van phai trang, khong tu dua ten host.
    public static string HlsUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"var\s+hlsUrl\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string url = m.Groups[1].Value.Trim();
        if (url.StartsWith("//"))
            url = "https:" + url;

        return url.StartsWith("http") ? url : null;
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return Regex.Replace(s.Trim(), @"\s+", " ");
    }

    // /categories/ — 12 muc. Link co pagination /categories/2/ (slug la so)
    // nen bo qua slug toan so.
    public static List<(string name, string path)> Categories(string html)
        => Slugs(html, "/categories/");

    // /tags/ la trang index day du (115 muc). /categories/ chi 12 muc va
    // khong co phan trang -> 12 la het, khong the lay them.
    public static List<(string name, string path)> Tags(string html)
        => Slugs(html, "/tags/");

    static List<(string name, string path)> Slugs(string html, string prefix)
    {
        var res = new List<(string name, string path)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var re = new Regex(@"href\s*=\s*[""']https?://en\.jable\.tv" + Regex.Escape(prefix)
            + @"([a-z0-9-]+)/[""'][^>]*>", RegexOptions.IgnoreCase);

        foreach (Match m in re.Matches(html))
        {
            string slug = m.Groups[1].Value.Trim();
            if (slug.Length == 0 || slug.All(char.IsDigit))
                continue;
            if (!seen.Add(slug))
                continue;

            // Ten co the nam sau tag long ben trong <a> (vd <a><span>BDSM...).
            // Window 200 cat ngang tag <span...> -> strip tag khong khop ->
            // rac "BDSM <sp" (bug 2026-10-06). Tang window + cat bo tag do
            // o cuoi truoc khi strip.
            string tail = html.Substring(m.Index + m.Length,
                Math.Min(500, html.Length - m.Index - m.Length));
            int cut = tail.IndexOf("</a", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
                tail = tail.Substring(0, cut);
            int lt = tail.LastIndexOf('<');
            if (lt >= 0 && tail.IndexOf('>', lt) < 0)
                tail = tail.Substring(0, lt);

            string name = Clean(HttpUtility.HtmlDecode(Regex.Replace(tail, "<[^>]+>", " ")));
            if (name.Length == 0)
                continue;

            res.Add((name, prefix.Trim('/') + "/" + slug));
        }

        return res;
    }

    // FALLBACK TINH — `/categories/` va `/tags/` la trang tinh, nhung
    // Jable hay fail SSL nen may khong duoc bao gio tra menu RUT GON
    // (app cache response dau ca phien -> thieu taxonomy = phai restart
    // app moi thay). Fetch loi/het gio -> dung 2 bang nay, van day du.
    public static readonly IReadOnlyList<(string name, string path)> FallbackCats =
        new (string, string)[]
        {
            ("BDSM", "categories/bdsm"),
            ("Sex Only", "categories/sex-only"),
            ("Chinese Subtitle", "categories/chinese-subtitle"),
            ("Insult", "categories/insult"),
            ("Uniform", "categories/uniform"),
            ("Roleplay", "categories/roleplay"),
            ("Private Cam", "categories/private-cam"),
            ("Uncensored", "categories/uncensored"),
            ("POV", "categories/pov"),
            ("Group Sex", "categories/groupsex"),
            ("Pantyhose", "categories/pantyhose"),
            ("Lesbian", "categories/lesbian"),
        };

    public static readonly IReadOnlyList<(string name, string path)> FallbackTags =
        new (string, string)[]
        {
            ("10 times a day", "tags/10-times-a-day"),
            ("3P", "tags/3p"),
            ("Affair", "tags/affair"),
            ("Age difference", "tags/age-difference"),
            ("Anal sex", "tags/anal-sex"),
            ("Avenge", "tags/avenge"),
            ("Bathing place", "tags/bathing-place"),
            ("Beautiful butt", "tags/beautiful-butt"),
            ("Beautiful leg", "tags/beautiful-leg"),
            ("Big tits", "tags/big-tits"),
            ("Black", "tags/black"),
            ("Black pantyhose", "tags/black-pantyhose"),
            ("Blowjob", "tags/blowjob"),
            ("Bondage", "tags/bondage"),
            ("Breast Milk", "tags/breast-milk"),
            ("Bunny girl", "tags/bunny-girl"),
            ("Car", "tags/car"),
            ("Cheongsam", "tags/cheongsam"),
            ("Chikan", "tags/chikan"),
            ("Childhood sweetheart", "tags/childhood"),
            ("Chizyo", "tags/chizyo"),
            ("Couple", "tags/couple"),
            ("Crapulence", "tags/crapulence"),
            ("Creampie", "tags/creampie"),
            ("Cum in mouth", "tags/cum-in-mouth"),
            ("Dainty", "tags/dainty"),
            ("Debut / Retires", "tags/debut-retires"),
            ("Deep throat", "tags/deep-throat"),
            ("Detective", "tags/detective"),
            ("Doctor", "tags/doctor"),
            ("Facial", "tags/facial"),
            ("Female Anchor", "tags/female-anchor"),
            ("Festival", "tags/festival"),
            ("First night", "tags/first-night"),
            ("Fishnets", "tags/fishnets"),
            ("Flesh-toned pantyhose", "tags/flesh-toned-pantyhose"),
            ("Flexible body", "tags/flexible-body"),
            ("Flight attendant", "tags/flight-attendant"),
            ("Footjob", "tags/footjob"),
            ("For women", "tags/for-women"),
            ("Fugitive", "tags/fugitive"),
            ("Gang Intrusion", "tags/gang-intrusion"),
            ("Gangbang", "tags/groupsex"),
            ("Giant", "tags/giant"),
            ("Girl", "tags/girl"),
            ("Glasses", "tags/glasses"),
            ("Gym Room", "tags/gym-room"),
            ("Hairless pussy", "tags/hairless-pussy"),
            ("Hot spring", "tags/hot-spring"),
            ("Housewife", "tags/housewife"),
            ("Hypnosis", "tags/hypnosis"),
            ("Idol", "tags/idol"),
            ("Insult", "tags/insult"),
            ("Intrusion", "tags/intrusion"),
            ("Kemonomimi", "tags/kemonomimi"),
            ("Kimono", "tags/kimono"),
            ("Kinship", "tags/kinship"),
            ("Kiss", "tags/kiss"),
            ("Knee socks", "tags/knee-socks"),
            ("Leakage", "tags/private-cam"),
            ("Library", "tags/library"),
            ("Love potion", "tags/love-potion"),
            ("Magic Mirror", "tags/magic-mirror"),
            ("Maid", "tags/maid"),
            ("Masochism guy", "tags/masochism-guy"),
            ("Massage", "tags/massage"),
            ("Mature woman", "tags/mature-woman"),
            ("Missed last train", "tags/missed-last-train"),
            ("More than 4 hours", "tags/more-than-4-hours"),
            ("NTR", "tags/ntr"),
            ("Nurse", "tags/nurse"),
            ("OL", "tags/ol"),
            ("Outdoor", "tags/outdoor"),
            ("Pantyhose", "tags/pantyhose"),
            ("Piss", "tags/piss"),
            ("Prison", "tags/prison"),
            ("Private teacher", "tags/private-teacher"),
            ("Quickie", "tags/quickie"),
            ("Rainy Day", "tags/rainy-day"),
            ("School", "tags/school"),
            ("School uniform", "tags/school-uniform"),
            ("Sex beside husband", "tags/sex-beside-husband"),
            ("Sex Worker", "tags/club-hostess-and-sex-worker"),
            ("Shoplifting", "tags/shoplifting"),
            ("Short hair", "tags/short-hair"),
            ("Small tits", "tags/small-tits"),
            ("Soapland", "tags/soapland"),
            ("Spasms", "tags/spasms"),
            ("Sportswear", "tags/sportswear"),
            ("Squirting", "tags/squirting"),
            ("Stockings", "tags/stockings"),
            ("Store", "tags/store"),
            ("Suntan", "tags/suntan"),
            ("Swimming pool", "tags/swimming-pool"),
            ("Swimsuit", "tags/swimsuit"),
            ("tall", "tags/tall"),
            ("Tattoo", "tags/tattoo"),
            ("Teacher", "tags/teacher"),
            ("Team Manager", "tags/team-manager"),
            ("Temptation", "tags/temptation"),
            ("Thanksgiving", "tags/thanksgiving"),
            ("Time stop", "tags/time-stop"),
            ("Tit Wank", "tags/tit-wank"),
            ("Toilet", "tags/toilet"),
            ("Torture", "tags/grip"),
            ("Tram", "tags/tram"),
            ("Tune", "tags/tune"),
            ("Ugly man", "tags/ugly-man"),
            ("Variety Show", "tags/variety-show"),
            ("Video recording", "tags/video-recording"),
            ("Virginity", "tags/virginity"),
            ("Wedding dress", "tags/wedding-dress"),
            ("Widow", "tags/widow"),
            ("Wife", "tags/wife"),
        };

    // BASE: dong 3+ — list toan cuc + taxonomy, KHONG phu thuoc
    // search/sort/c -> cache dung 1 lan. `Sorts` cu (name,c,sort) da XOA
    // 2026-10-06: no dan toi list tong co dinh, khong sort list dang mo.
    public static List<MenuItem> Menu(
        string host,
        IReadOnlyList<(string name, string path)> cats = null,
        IReadOnlyList<(string name, string path)> tags = null)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/jable?c=" + c.Trim('/');

        // Dong 3: 3 list TOAN CUC (day la "list", khong phai "sort" cua
        // list dang mo -> khong duoc lan vao dong 2).
        var root = new List<MenuItem>()
        {
            new MenuItem() { title = "Bảng xếp hạng", playlist_url = "submenu", submenu = new List<MenuItem>()
            {
                new("Mới nhất", url("latest-updates")),
                new("Mới phát hành", url("new-release")),
                new("Hot", url("hot")),
            }}
        };

        if (cats != null && cats.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var c in cats.Take(60))
                sub.Add(new MenuItem(c.name, host + "/jable?c=" + c.path));
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = sub });
        }

        if (tags != null && tags.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var t in tags.Take(130))
                sub.Add(new MenuItem(t.name, host + "/jable?c=" + t.path));
            root.Add(new MenuItem() { title = "Từ khoá", playlist_url = "submenu", submenu = sub });
        }

        return root;
    }
}