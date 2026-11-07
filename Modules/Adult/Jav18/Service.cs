using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Jav18;

public static class Jav18To
{
    public static string SiteHost = "https://18jav.tv";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    // The nao goc cua card video trong list. Moi the bat dau bang 1 div nay,
    // cac div ben trong deu co the lon hon -> tach theo marker, khong theo
    // </div> (se cat ngan).
    const string CardMark = "<div class=\"col-6 col-sm-4 col-lg-3\">";

    // KVS block id cho phan trang AJAX (do truc tiep 2026-11-07):
    //   list chung (latest-updates/hot/categories/tags/models) -> list_videos_common_videos_list
    //   search                                                -> list_videos_videos_list_search_result
    public static string BlockCommon = "list_videos_common_videos_list";

    public static string BlockSearch = "list_videos_videos_list_search_result";

    // Trang /models (index nữ diễn viên) khác block — không dùng cho list
    // video nên không cần khai báo ở đây.

    public static bool IsLatest(string c)
        => string.IsNullOrWhiteSpace(c) || c.Trim().Equals("latest-updates", StringComparison.OrdinalIgnoreCase);

    public static bool IsHot(string c)
        => !string.IsNullOrWhiteSpace(c) && c.Trim().Equals("hot", StringComparison.OrdinalIgnoreCase);

    // categories/<slug>, tags/<slug>, models/<slug> đều dùng chung 1 block
    // list_videos_common_videos_list và có đủ 3 kiểu sort.
    public static bool IsTaxonomy(string c)
    {
        if (string.IsNullOrWhiteSpace(c))
            return false;

        var seg = c.Trim('/').Split('/');
        if (seg.Length < 2)
            return false;

        string k = seg[0].ToLowerInvariant();
        return k == "categories" || k == "tags" || k == "models";
    }

    // Phân trang KVS KHÔNG theo đường dẫn (khác Jable) mà theo AJAX:
    //   <base>/?mode=async&function=get_block&block_id=<block>&sort_by=<s>&from=<pg>
    // search: from_videos=<pg>&from_albums=<pg> (+ q=).
    // Trang 1 mặc định (sort = sort mặc định của context) dùng URL thường +
    // trailing slash (đã đo 200); pg>1 hoặc sort khác mặc định -> AJAX
    // (đã đo từng trường hợp, xem ../ADULT-MODULE notes + capture).
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (pg < 1)
            pg = 1;

        string path;

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ?q=... trả 404 — bắt buộc dạng đường dẫn /search/<q>/
            path = "/search/" + HttpUtility.UrlEncode(search.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string p = c.Trim();
            if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // c là URL đầy đủ (bookmark cũ): cắt scheme+host, giữ path
                int i = p.IndexOf("://", StringComparison.Ordinal);
                int j = p.IndexOf('/', i + 3);
                p = j > 0 ? p.Substring(j) : "/";
            }
            path = "/" + p.Trim('/');
        }
        else
        {
            path = "/latest-updates";
        }

        // BAT BUOC trailing slash. Khong co "/" cuoi -> 404 NotFound
        // (Lampac tra 404, khong phai bi chan).
        string u = host + path.TrimEnd('/') + "/";

        string dflt = DefaultSort(search, c);
        bool needAjax = pg > 1 || (!string.IsNullOrEmpty(sort) && sort != dflt);

        if (needAjax)
        {
            string s = string.IsNullOrEmpty(sort) ? "" : sort;
            if (!string.IsNullOrWhiteSpace(search))
            {
                u += "?mode=async&function=get_block&block_id=" + BlockSearch
                    + "&q=" + HttpUtility.UrlEncode(search.Trim())
                    + "&sort_by=" + s
                    + "&from_videos=" + pg + "&from_albums=" + pg;
            }
            else
            {
                u += "?mode=async&function=get_block&block_id=" + BlockCommon
                    + "&sort_by=" + s + "&from=" + pg;
            }
        }

        return u;
    }

    // Sort mặc định của từng context (trang 1 không sort = sort này):
    //   search  -> "" (最高相關 / liên quan nhất)
    //   /hot    -> views
    //   còn lại -> release_at
    public static string DefaultSort(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return "";
        if (IsHot(c))
            return "views";
        return "release_at";
    }

    // CÁC KIỂU SẮP XẾP thật của site — sort-nav KVS, đo trực tiếp 2026-11-07:
    //   /categories/<slug>, /tags/<slug>, /models/<slug>:
    //     release_at (最近更新), views (最多觀看), stars (近期最佳) — đủ 3
    //   /search/<q>/: "" (最高相關), release_at, views, stars — đủ 3
    //   /latest-updates: chỉ release_at; /hot: chỉ views -> ẨN dòng 2
    public static readonly string[] SortWhitelist =
    {
        "release_at", "views", "stars",
    };

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return null;
        sort = sort.Trim().ToLowerInvariant();
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    public static readonly (string name, string sort)[] TaxSorts =
    {
        ("Mặc định",    "release_at"),
        ("Xem nhiều",   "views"),
        ("Đánh giá cao","stars"),
    };

    public static readonly (string name, string sort)[] SearchSorts =
    {
        ("Liên quan nhất",""),
        ("Mới nhất",    "release_at"),
        ("Xem nhiều",   "views"),
        ("Đánh giá cao","stars"),
    };

    // Tap sort ÁP DỤNG được cho context. null = site không sort được -> ẩn dòng 2.
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return SearchSorts;
        if (IsLatest(c) || IsHot(c))
            return null;
        if (IsTaxonomy(c))
            return TaxSorts;
        return null;
    }

    // Gop sort ve dung tap cua context TRƯOC khi vao cache key + Uri.
    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null)
            return null;

        sort = NormalizeSort(sort);
        string dflt = opts[0].sort;
        if (string.IsNullOrEmpty(sort))
            return dflt;

        foreach (var o in opts)
            if (o.sort == sort)
                return sort;

        return dflt;
    }

    public static string SortLabel(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts != null)
            foreach (var o in opts)
                if (o.sort == sort)
                    return o.name;

        return "mặc định";
    }

    // HEAD: dong 1 + 2, phu thuoc search/sort/c -> dung lai moi request.
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/jav18";
        var res = new List<MenuItem>(2)
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };

        var opts = SortsFor(search, c);
        if (opts == null || opts.Length == 0)
            return res;

        var sub = new List<MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
        {
            // Dang o search thi giu search; dang o c thi giu c, chi doi sort.
            string q;
            if (!string.IsNullOrWhiteSpace(search))
                q = "search=" + HttpUtility.UrlEncode(search);
            else
                q = "c=" + c.Trim('/');
            if (!string.IsNullOrEmpty(s))
                q += "&sort=" + s;

            sub.Add(new MenuItem(name, root + "?" + q));
        }

        res.Add(new MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort, search, c)}",
            playlist_url = "submenu",
            submenu = sub
        });
        return res;
    }

    // <div class="col-6 col-sm-4 col-lg-3">
    //   <a href="https://18jav.tv/videos/<slug>">
    //     <img class="lazyload" data-src="https://cdn.18jav.tv/storage/<slug>/320_180.jpg">
    //     <span class="label">1:28:15</span>
    //   </a>
    //   <h6 class="title"><a href="...">TEN</a></h6>
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

            var href = Regex.Match(block,
                @"<a\b[^>]*\bhref\s*=\s*[""']([^""']*?/videos/[^""']+)[""']",
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

            // Poster: cdn.18jav.tv/storage/<slug>/320_180.jpg (lazyload)
            string poster = "";
            var img = Regex.Match(block,
                @"<img\b[^>]*\bdata-src\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (img.Success)
            {
                poster = img.Groups[1].Value.Trim();
                if (poster.StartsWith("//"))
                    poster = "https:" + poster;
            }

            // Thời lượng: <div class="absolute-bottom-right"><span class="label">1:28:15</span>
            string time = "";
            var dur = Regex.Match(block,
                @"<div\b[^>]*\babsolute-bottom-right\b[^>]*>\s*<span\b[^>]*>([^<]{3,12})</span\s*>",
                RegexOptions.IgnoreCase);
            if (dur.Success)
                time = Clean(dur.Groups[1].Value);

            list.Add(new PlaylistItem()
            {
                video = "jav18/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                name = name,
                picture = poster,
                time = time,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "18jav",
                    href = uri,
                    image = poster
                }
            });
        }

        return list;
    }

    // var hlsUrl = "https://s8.hshdkshd.com/hls/<token>/<ts>/storage/<id>/<md5>.m3u8";
    // Host/token đổi mỗi lần load -> phải lấy NGUYÊN văn trang, không tự
    // dựng URL. Playlist là MEDIA playlist (AES-128, key URI tương đối) ->
    // Hls.js tự xử lý.
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

    // Phân trang KVS: data-parameters="sort_by:release_at;from:1417"
    // search: from_videos+from_albums:N -> lấy MAX = tổng số trang.
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html, @"from[A-Za-z_+]*:(\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max)
                max = n;
        }
        return max;
    }

    // Trang detail có section "推薦 / 猜你喜歡" (video liên quan). Card thật
    // col-6 col-sm-4 col-lg-3; card quảng cáo col-lg-12 -> bị loại bởi CardMark.
    public static List<PlaylistItem> Related(string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<PlaylistItem>();

        int i = html.IndexOf("推薦", StringComparison.Ordinal);
        if (i < 0)
            return new List<PlaylistItem>();

        return Playlist(html.Substring(i));
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return Regex.Replace(s.Trim(), @"\s+", " ");
    }

    // /categories/ : <a href="https://18jav.tv/categories/<slug>"> ... <h4>TEN</h4><span>N 部影片</span>
    // /tags/       : <a class="tag text-light" href="https://18jav.tv/tags/<slug>">TEN</a>
    // /models      : <a href="https://18jav.tv/models/<slug>"> ... <h6 class="title">TEN</h6><span>N 部影片</span>
    // href KHÔNG có trailing slash, slug có thể IN HOA (/tags/Cosplay).
    public static List<(string name, string path)> Categories(string html)
        => Slugs(html, "categories");

    public static List<(string name, string path)> Tags(string html)
        => Slugs(html, "tags");

    public static List<(string name, string path)> Models(string html)
        => Slugs(html, "models");

    static List<(string name, string path)> Slugs(string html, string kind)
    {
        var res = new List<(string name, string path)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var re = new Regex(@"href\s*=\s*[""']https://18jav\.tv/" + kind + @"/([a-zA-Z0-9-]+)[""']",
            RegexOptions.IgnoreCase);

        foreach (Match m in re.Matches(html))
        {
            string slug = m.Groups[1].Value.Trim();
            if (slug.Length == 0)
                continue;
            if (!seen.Add(slug))
                continue;

            // Tên nằm sau href trong cùng <a> (h4 ở /categories/, text trực
            // tiếp ở /tags/, h6 ở /models). Window 600, cắt </a> rồi mới strip.
            // Match dừng ở dấu " đóng attribute -> ký tự '>' của thẻ <a>
            // còn nằm đầu tail, phải bỏ (nếu không tên bị dính "> ").
            int start = m.Index + m.Length;
            if (start < html.Length && html[start] == '>')
                start++;

            string tail = html.Substring(start,
                Math.Min(600, html.Length - start));
            int cut = tail.IndexOf("</a", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
                tail = tail.Substring(0, cut);

            // categories / models: sau tên là <span>...N 部影片</span> — bỏ
            if (kind == "categories" || kind == "models")
            {
                int sp = tail.IndexOf("<span", StringComparison.OrdinalIgnoreCase);
                if (sp >= 0)
                    tail = tail.Substring(0, sp);
            }

            int lt = tail.LastIndexOf('<');
            if (lt >= 0 && tail.IndexOf('>', lt) < 0)
                tail = tail.Substring(0, lt);

            string name = Clean(HttpUtility.HtmlDecode(Regex.Replace(tail, "<[^>]+>", " ")));
            if (name.Length == 0)
                continue;

            res.Add((name, kind + "/" + slug));
        }

        return res;
    }

    // Detail: nữ diễn viên / thể loại / từ khoá của 1 video (cho popup
    // nhấn giữ). Thứ thuộc tính trong <a> không cố định nên dùng lookahead.
    //   <a class="model" href="https://18jav.tv/models/amateur"><span ... title="素人">素</span></a>
    //   <a href="https://18jav.tv/categories/amateur" class="cat">素人自拍</a>
    //   <a href="https://18jav.tv/tags/girl">少女</a>   (KHÔNG có class)
    // Đầu trang detail có tag cloud TOÀN SITE (113 thẻ, class="tag
    // text-light") -> phải giới hạn parse trong vùng meta của video:
    // từ <div class="models"> / <h5 class="tags"> đến hết mục 推薦.
    public static List<(string kind, string slug, string name)> Info(string html)
    {
        var res = new List<(string kind, string slug, string name)>();
        if (string.IsNullOrEmpty(html))
            return res;

        int im = html.IndexOf("<div class=\"models\"", StringComparison.Ordinal);
        int it = html.IndexOf("<h5 class=\"tags", StringComparison.Ordinal);
        int start = -1;
        if (im >= 0)
            start = im;
        if (it >= 0 && (start < 0 || it < start))
            start = it;
        if (start < 0)
            return res;

        int end = html.IndexOf("推薦", start, StringComparison.Ordinal);
        if (end < 0)
            end = html.Length;

        string meta = html.Substring(start, Math.Min(end, html.Length) - start);

        Scan(res, meta, "model", "models", @"\bmodel\b");
        Scan(res, meta, "cat", "categories", @"\bcat\b");
        Scan(res, meta, "tag", "tags", "", requireClass: false);
        return res;
    }

    static void Scan(List<(string kind, string slug, string name)> res, string html,
        string kind, string dir, string cls, bool requireClass = true)
    {
        string classLook = requireClass
            ? @"(?=[^>]*\bclass\s*=\s*[""'][^""']*" + cls + @"[^""']*[""'])"
            : "";

        var re = new Regex(
            @"<a\b(?=[^>]*\bhref\s*=\s*[""']https://18jav\.tv/" + dir + @"/([a-z0-9-]+)[""'])" +
            classLook +
            @"[^>]*>(.*?)</a\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match m in re.Matches(html))
        {
            string slug = m.Groups[1].Value.Trim();
            if (slug.Length == 0)
                continue;

            string inner = m.Groups[2].Value;
            // model: <span title="素人">素</span> -> ưu tiên title
            string name = Clean(HttpUtility.HtmlDecode(Regex.Replace(inner, "<[^>]+>", " ")));
            var t = Regex.Match(inner, @"\btitle\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (t.Success)
                name = Clean(HttpUtility.HtmlDecode(t.Groups[1].Value));

            if (name.Length == 0)
                continue;

            res.Add((kind, slug, name));
        }
    }

    // FALLBACK TĨNH — /categories/ (14 mục) và /tags/ (113 mục) là trang
    // tĩnh, nhưng request lỗi thì app đã cache response đầu cả phiên (bị
    // menu rút gọn, phải restart app mới ra). Fetch lỗi/hết giờ -> dùng 2
    // bảng này, vẫn đầy đủ.
    public static readonly IReadOnlyList<(string name, string path)> FallbackCats =
        new (string, string)[]
        {
            ("中文字幕", "chinese-subtitle"),
            ("無碼解放", "uncensored"),
            ("台灣AV", "taiwan-av"),
            ("日本AV", "jav"),
            ("制服誘惑", "uniform"),
            ("角色劇情", "roleplay"),
            ("絲襪", "pantyhose"),
            ("直接開啪", "sex-only"),
            ("主奴調教", "bdsm"),
            ("多P群交", "groupsex"),
            ("進犯", "intrusion"),
            ("男友視角", "pov"),
            ("流出", "hixxen-cam"),
            ("女同歡愉", "lesbian"),
        };

    public static readonly IReadOnlyList<(string name, string path)> FallbackTags =
        new (string, string)[]
        {
            ("婚紗", "wedding-dress"),
            ("水着", "swimsuit"),
            ("吊帶襪", "stockings"),
            ("運動裝", "sportswear"),
            ("校服", "school-uniform"),
            ("絲襪", "pantyhose"),
            ("女僕", "maid"),
            ("過膝襪", "knee-socks"),
            ("和服", "kimono"),
            ("獸耳", "kemonomimi"),
            ("眼鏡娘", "glasses"),
            ("肉絲", "flesh-toned-pantyhose"),
            ("漁網", "fishnets"),
            ("旗袍", "cheongsam"),
            ("兔女郎", "bunny-girl"),
            ("黑絲", "black-pantyhose"),
            ("Cosplay", "Cosplay"),
            ("長身", "tall"),
            ("紋身", "tattoo"),
            ("黑肉", "suntan"),
            ("貧乳", "small-tits"),
            ("短髮", "short-hair"),
            ("熟女", "mature-woman"),
            ("嬌小", "dainty"),
            ("白虎", "hairless-pussy"),
            ("少女", "girl"),
            ("軟體", "flexible-body"),
            ("巨乳", "big-tits"),
            ("美腿", "beautiful-leg"),
            ("美尻", "beautiful-butt"),
            ("乳交", "tit-wank"),
            ("潮吹", "squirting"),
            ("痙攣", "spasms"),
            ("接吻", "kiss"),
            ("腳交", "footjob"),
            ("顏射", "facial"),
            ("深喉", "deep-throat"),
            ("口爆", "cum-in-mouth"),
            ("中出", "creampie"),
            ("口交", "blowjob"),
            ("肛交", "anal-sex"),
            ("調教", "tune"),
            ("刑具", "torture"),
            ("泡姬", "soapland"),
            ("進犯", "intrusion"),
            ("瞬間插入", "quickie"),
            ("放尿", "piss"),
            ("露出", "outdoor"),
            ("按摩", "massage"),
            ("男M", "masochism-guy"),
            ("凌辱", "insult"),
            ("多P", "gangbang"),
            ("集團進犯", "gang-intrusion"),
            ("泥醉", "crapulence"),
            ("痴女", "chizyo"),
            ("痴漢", "chikan"),
            ("母乳", "breast-milk"),
            ("綑綁", "bondage"),
            ("3P", "3p"),
            ("一日十回", "10-times-a-day"),
            ("童貞", "virginity"),
            ("醜男", "ugly-man"),
            ("時間停止", "time-stop"),
            ("誘惑", "temptation"),
            ("夫目前犯", "sex-beside-husband"),
            ("下雨天", "rainy-day"),
            ("NTR", "ntr"),
            ("媚藥", "love-potion"),
            ("流出", "hixxen-cam"),
            ("親屬", "kinship"),
            ("催眠", "hypnosis"),
            ("巨漢", "giant"),
            ("黑人", "black"),
            ("復仇", "avenge"),
            ("年齡差", "age-difference"),
            ("出軌", "affair"),
            ("人妻", "wife"),
            ("未亡人", "widow"),
            ("球隊經理", "team-manager"),
            ("老師", "teacher"),
            ("風俗娘", "club-hostess-and-sex-worker"),
            ("家庭教師", "private-teacher"),
            ("OL", "ol"),
            ("護士", "nurse"),
            ("偶像", "idol"),
            ("家政婦", "housewife"),
            ("逃犯", "fugitive"),
            ("空姐", "flight-attendant"),
            ("主播", "female-anchor"),
            ("醫生", "doctor"),
            ("搜查官", "detective"),
            ("情侶", "couple"),
            ("電車", "tram"),
            ("廁所", "toilet"),
            ("泳池", "swimming-pool"),
            ("便利店", "store"),
            ("學校", "school"),
            ("監獄", "prison"),
            ("魔鏡號", "magic-mirror"),
            ("圖書館", "library"),
            ("溫泉", "hot-spring"),
            ("健身房", "gym-room"),
            ("處女", "first-night"),
            ("汽車", "car"),
            ("洗浴場", "bathing-place"),
            ("錄像", "video-recording"),
            ("綜藝", "variety-show"),
            ("感謝祭", "thanksgiving"),
            ("4小時以上", "more-than-4-hours"),
            ("節日主題", "festival"),
            ("處女作/引退作", "debut-retires"),
            ("麻豆", "madou"),
            ("FC2", "fc2"),
        };

    // Bảng dịch Trung -> Việt cho tên thể loại / từ khoá (menu + popup).
    // Tên không có trong bảng (vd tên diễn viên) giữ nguyên.
    static readonly Dictionary<string, string> Trans = new(StringComparer.Ordinal)
    {
        ["中文字幕"] = "Phụ đề Trung",
        ["無碼解放"] = "Không che",
        ["台灣AV"] = "AV Đài Loan",
        ["日本AV"] = "AV Nhật Bản",
        ["制服誘惑"] = "Đồng phục",
        ["角色劇情"] = "Nhập vai",
        ["絲襪"] = "Tất lưới",
        ["直接開啪"] = "Quan hệ luôn",
        ["主奴調教"] = "BDSM",
        ["多P群交"] = "Quần giao",
        ["進犯"] = "Xâm phạm",
        ["男友視角"] = "Góc POV",
        ["流出"] = "Rò rỉ",
        ["女同歡愉"] = "Lesbian",
        ["素人自拍"] = "Tự quay nghiệp dư",
        ["婚紗"] = "Váy cưới",
        ["水着"] = "Đồ bơi",
        ["吊帶襪"] = "Tất giữ",
        ["運動裝"] = "Đồ thể thao",
        ["校服"] = "Đồng phục học sinh",
        ["女僕"] = "Hầu gái",
        ["過膝襪"] = "Tất qua gối",
        ["和服"] = "Kimono",
        ["獸耳"] = "Tai thú",
        ["眼鏡娘"] = "Gái đeo kính",
        ["肉絲"] = "Tất màu da",
        ["漁網"] = "Lưới cá",
        ["旗袍"] = "Xường xám",
        ["兔女郎"] = "Girl thỏ",
        ["黑絲"] = "Tất đen",
        ["Cosplay"] = "Cosplay",
        ["長身"] = "Người cao",
        ["紋身"] = "Xăm hình",
        ["黑肉"] = "Da ngăm",
        ["貧乳"] = "Ngực nhỏ",
        ["短髮"] = "Tóc ngắn",
        ["熟女"] = "MILF",
        ["嬌小"] = "Nhỏ nhắn",
        ["白虎"] = "Không lông",
        ["少女"] = "Thiếu nữ",
        ["軟體"] = "Dẻo dai",
        ["巨乳"] = "Ngực lớn",
        ["美腿"] = "Chân đẹp",
        ["美尻"] = "Mông đẹp",
        ["乳交"] = "Cạ ngực",
        ["潮吹"] = "Phun nước",
        ["痙攣"] = "Co giật",
        ["接吻"] = "Hôn",
        ["腳交"] = "Footjob",
        ["顏射"] = "Bắn mặt",
        ["深喉"] = "Deep throat",
        ["口爆"] = "Bắn miệng",
        ["中出"] = "Xuất trong",
        ["口交"] = "Bú",
        ["肛交"] = "Hậu môn",
        ["調教"] = "Huấn luyện",
        ["刑具"] = "Đồ tra tấn",
        ["泡姬"] = "Soapland",
        ["瞬間插入"] = "Quickie",
        ["放尿"] = "Đái",
        ["露出"] = "Lộ hàng",
        ["按摩"] = "Massage",
        ["男M"] = "Nam M",
        ["凌辱"] = "Nhục hình",
        ["多P"] = "Đa P",
        ["集團進犯"] = "Nhóm xâm phạm",
        ["泥醉"] = "Say xỉn",
        ["痴女"] = "Nữ dâm",
        ["痴漢"] = "Chikan",
        ["母乳"] = "Sữa mẹ",
        ["綑綁"] = "Trói",
        ["3P"] = "3P",
        ["一日十回"] = "10 lần/ngày",
        ["童貞"] = "Trai tân",
        ["醜男"] = "Trai xấu",
        ["時間停止"] = "Dừng thời gian",
        ["誘惑"] = "Cám dỗ",
        ["夫目前犯"] = "Trước mặt chồng",
        ["下雨天"] = "Ngày mưa",
        ["NTR"] = "NTR",
        ["媚藥"] = "Thuốc kích dục",
        ["親屬"] = "Người thân",
        ["催眠"] = "Thôi miên",
        ["巨漢"] = "Gã khổng lồ",
        ["黑人"] = "Da đen",
        ["復仇"] = "Trả thù",
        ["年齡差"] = "Chênh lệch tuổi",
        ["出軌"] = "Ngoại tình",
        ["人妻"] = "Vợ người ta",
        ["未亡人"] = "Góa phụ",
        ["球隊經理"] = "Quản lý đội",
        ["老師"] = "Cô giáo",
        ["風俗娘"] = "Nhân viên phong tục",
        ["家庭教師"] = "Gia sư",
        ["OL"] = "OL",
        ["護士"] = "Y tá",
        ["偶像"] = "Thần tượng",
        ["家政婦"] = "Người giúp việc",
        ["逃犯"] = "Tội phạm trốn",
        ["空姐"] = "Tiếp viên hàng không",
        ["主播"] = "MC nữ",
        ["醫生"] = "Bác sĩ",
        ["搜查官"] = "Điều tra viên",
        ["情侶"] = "Cặp đôi",
        ["電車"] = "Tàu điện",
        ["廁所"] = "Nhà vệ sinh",
        ["泳池"] = "Hồ bơi",
        ["便利店"] = "Cửa hàng tiện lợi",
        ["學校"] = "Trường học",
        ["監獄"] = "Nhà tù",
        ["魔鏡號"] = "Gương ma thuật",
        ["圖書館"] = "Thư viện",
        ["溫泉"] = "Suối nước nóng",
        ["健身房"] = "Phòng gym",
        ["處女"] = "Trinh nữ",
        ["汽車"] = "Ô tô",
        ["洗浴場"] = "Nhà tắm",
        ["錄像"] = "Quay lén",
        ["綜藝"] = "Show giải trí",
        ["感謝祭"] = "Lễ tạ ơn",
        ["4小時以上"] = "Trên 4 giờ",
        ["節日主題"] = "Chủ đề lễ hội",
        ["處女作/引退作"] = "Ra mắt / giải nghệ",
        ["麻豆"] = "Madou",
        ["FC2"] = "FC2",
    };

    public static string Translate(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return Trans.TryGetValue(name, out var v) ? v : name;
    }
    // BASE: dong 3+ — list toàn cục + taxonomy, KHONG phu thuoc
    // search/sort/c -> cache dung 1 lan.
    public static List<MenuItem> Menu(
        string host,
        IReadOnlyList<(string name, string path)> cats = null,
        IReadOnlyList<(string name, string path)> tags = null,
        IReadOnlyList<(string name, string path)> models = null)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/jav18?c=" + c.Trim('/');

        // Dong 3: list TOAN CUC (day la "list", khong phai "sort" cua list
        // dang mo -> khong duoc lan vao dong 2).
        var root = new List<MenuItem>()
        {
            new MenuItem() { title = "Danh sách", playlist_url = "submenu", submenu = new List<MenuItem>()
            {
                new("Mới nhất", url("latest-updates")),
                new("Hot", url("hot")),
            }}
        };

        if (cats != null && cats.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var c in cats.Take(60))
                sub.Add(new MenuItem(Translate(c.name), url(c.path)));
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = sub });
        }

        if (tags != null && tags.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var t in tags.Take(130))
                sub.Add(new MenuItem(Translate(t.name), url(t.path)));
            root.Add(new MenuItem() { title = "Từ khoá", playlist_url = "submenu", submenu = sub });
        }

        if (models != null && models.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var m in models.Take(60))
                sub.Add(new MenuItem(m.name, url(m.path)));
            root.Add(new MenuItem() { title = "Nữ diễn viên", playlist_url = "submenu", submenu = sub });
        }

        return root;
    }
}
