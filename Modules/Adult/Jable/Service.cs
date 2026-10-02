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

    // CAC KIỂU SẮP XẾP của site jable (mục "Sắp xếp" dòng 2 trong menu).
    // `c` = path site, `sort` = ?sort_by= của web (null = không).
    public static readonly (string name, string c, string sort)[] Sorts =
    {
        ("Mới nhất",        "latest-updates", null),
        ("Mới phát hành",  "new-release",    null),
        ("Hot hôm nay",    "hot", "video_viewed_today"),
        ("Hot tuần này",   "hot", "video_viewed_week"),
        ("Hot tháng này",  "hot", "video_viewed_month"),
        ("Hot tất cả",     "hot", "video_viewed"),
    };

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

            string tail = html.Substring(m.Index + m.Length,
                Math.Min(200, html.Length - m.Index - m.Length));
            int cut = tail.IndexOf("</a", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
                tail = tail.Substring(0, cut);

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

    // CÔNG THỨC MENU SISI (áp dụng cho mọi module — xem skill
    // lampac-adult-module mục 9g):
    //   dòng 1 : Tìm kiếm  (search_on, được phép ở root)
    //   dòng 2 : Sắp xếp   (submenu chứa các kiểu xếp hạng của site)
    //   dòng 3+: taxonomy   (Từ khoá / Thể loại / Kênh / Hãng…)
    // Mọi mục điều hướng phải nằm trong submenu — mục root không
    // submenu thì app bấm vào chỉ mở lại menu (chết im, mục 9c).
    public static List<MenuItem> Menu(
        string host,
        IReadOnlyList<(string name, string path)> cats = null,
        IReadOnlyList<(string name, string path)> tags = null)
    {
        // QUERY `?c=` — app phan trang bang query `?pg=N`, dung duong dan
        // se lam trang 2 tra ve trang 1 (lap noi dung, muc 9d).
        var root = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/jable"
            }
        };

        // DÒNG 2 — Sắp xếp. Mỗi kiểu là 1 path site (+ ?sort_by= nếu có).
        var sortSub = new List<MenuItem>();
        foreach (var s in Sorts)
        {
            string url = host + "/jable?c=" + s.c
                + (string.IsNullOrEmpty(s.sort) ? "" : "&sort=" + s.sort);
            sortSub.Add(new MenuItem(s.name, url));
        }
        root.Add(new MenuItem() { title = "Sắp xếp", playlist_url = "submenu", submenu = sortSub });

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