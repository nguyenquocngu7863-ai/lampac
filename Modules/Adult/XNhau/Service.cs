using Shared.Models.SISI.Base;
using System.Net;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace XNhau;

public static class XNhauTo
{
    // host hien tai (ModInit.updateConf gan = conf.host moi lan reload)
    public static string SiteHost = "https://xnhau.free";

    #region Uri
    public static string Uri(string host, string search, string sort, string c, string t, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        // Sort that kieu KVS `?sort_by=` (do live 2026-10-08: ca 6 doi list
        // that tren genre + search; home/tag/page-2 cung vay). Legacy
        // `?sort=clip-sex-moi|hot|hay` map ve sort_by tuong duong.
        string s = NormalizeSort(sort);

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search) && search.Contains("/members/"))
        {
            // Feed nguoi dang: dan link member (https://xnhau.limo/members/16566/)
            // vao o tim kiem — lay thang trang member
            string murl = search.Trim();
            if (murl.StartsWith("/"))
                murl = $"{SiteHost}{murl}";
            url.Clear();
            url.Append(murl.TrimEnd('/'));
            url.Append("/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (!string.IsNullOrWhiteSpace(search) && search.StartsWith("member:"))
        {
            url.Append("members/");
            url.Append(search.Substring(7).Trim().Trim('/'));
            url.Append("/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            string encsearch = HttpUtility.UrlEncode(search);
            url.Append("search/");
            url.Append(encsearch);
            url.Append("/");
            string sq = string.IsNullOrEmpty(s) ? "" : "?sort_by=" + s;
            if (pg > 1)
            {
                // Search phan trang qua AJAX (giong web): ?mode=async&function=get_block&...
                url.Append(sq.Length == 0 ? "?" : sq + "&");
                url.Append("mode=async&function=get_block&block_id=list_videos_videos_list_search_result&q=");
                url.Append(encsearch);
                url.Append("&from_videos=");
                url.Append(pg);
                url.Append("&from_albums=");
                url.Append(pg);
            }
            else if (sq.Length > 0)
                url.Append(sq);
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append("the-loai/");
            url.Append(c);
            url.Append("/");
            string cq = string.IsNullOrEmpty(s) ? "" : "?sort_by=" + s;
            if (pg > 1)
            {
                url.Append(cq.Length == 0 ? "?from=" : cq + "&from=");
                url.Append(pg);
            }
            else if (cq.Length > 0)
                url.Append(cq);
        }
        else if (!string.IsNullOrEmpty(t))
        {
            url.Append("tags/");
            url.Append(t);
            url.Append("/");
            string tq = string.IsNullOrEmpty(s) ? "" : "?sort_by=" + s;
            if (pg > 1)
            {
                url.Append(tq.Length == 0 ? "?from=" : tq + "&from=");
                url.Append(pg);
            }
            else if (tq.Length > 0)
                url.Append(tq);
        }
        else if (!string.IsNullOrEmpty(s))
        {
            // Home + sort: ?sort_by= (thay 3 path rieng clip-sex-moi|hot|hay).
            url.Append("?sort_by=");
            url.Append(s);
            if (pg > 1)
            {
                url.Append("&from=");
                url.Append(pg);
            }
        }
        else if (pg > 1)
        {
            url.Append("clip-sex-moi/?from=");
            url.Append(pg);
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("<div\\s+class=\"item", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups("<a href=\"((?:https?://[^/]+)?/video/[0-9]+/[^\\\"]+)\"[^>]*title=\"([^\\\"]+)\"");

            if (string.IsNullOrWhiteSpace(g[1].Value) || string.IsNullOrWhiteSpace(g[2].Value))
                continue;

            string href = g[1].Value;
            if (href.StartsWith("/"))
                href = $"{SiteHost}{href}";

            var img = row.Groups("data-original=\"([^\"]+)\"");
            string picture = img[1].Value;
            if (string.IsNullOrEmpty(picture))
                picture = row.Match("data-webp=\"([^\"]+)\"");
            if (!string.IsNullOrEmpty(picture) && picture.StartsWith("/"))
                picture = $"{SiteHost}{picture}";

            string time = row.Match("<span class=\"duration\"[^>]*>(.*?)</span>", trim: true);
            if (string.IsNullOrEmpty(time))
                time = row.Match("duration\">([^<]+)<", trim: true);

            string quality = row.Match("<span class=\"hd[^\\\"]*\\\">([^<]+)</span>", trim: true);

            var pl = new PlaylistItem()
            {
                video = $"{uri}?uri={HttpUtility.UrlEncode(href)}",
                name = HttpUtility.HtmlDecode(g[2].Value),
                picture = picture,
                quality = quality,
                time = System.Text.RegularExpressions.Regex.Replace(time ?? "", "<[^>]+>", "").Trim(),
                json = true,
                bookmark = new Bookmark()
                {
                    site = "xnhau",
                    href = href,
                    image = picture
                }
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        // Loại bỏ item trùng theo video URI
        return playlists.DistinctBy(p => p.video).ToList();
    }
    #endregion

    #region Menu
    // ================= MENU =================
    //
    // Cong thuc SISI 3 dong:
    //   dong 1  Tìm kiếm  (search_on)
    //   dong 2  Sắp xếp   (submenu, THEO CONTEXT — MenuHead)
    //   dong 3+ taxonomy   (the loai)
    //
    // Sort that kieu KVS `?sort_by=` (do live 2026-10-08, dropdown sort tren
    // head moi trang list): post_date/video_viewed/rating/duration/
    // most_commented/most_favourited — ca 6 doi list that. Mac dinh (khong
    // sort): home/genre/tag = Mới nhất (post_date), search = Liên quan.
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất",            "post_date"),
        ("Xem nhiều nhất",      "video_viewed"),
        ("Hay nhất",            "rating"),
        ("Dài nhất",            "duration"),
        ("Bình luận nhiều nhất","most_commented"),
        ("Được yêu thích nhất", "most_favourited"),
    };

    static readonly System.Collections.Generic.Dictionary<string, string> SortAlias =
        new System.Collections.Generic.Dictionary<string, string>(
            System.StringComparer.OrdinalIgnoreCase)
        {
            { "clip-sex-moi", "post_date" },
            { "clip-sex-hot", "video_viewed" },
            { "clip-sex-hay", "rating" },
            { "new", "post_date" },
            { "hot", "video_viewed" },
            { "top", "rating" },
        };

    // "" = mac dinh context (Mới nhất cho list, Liên quan cho search).
    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return "";
        sort = sort.Trim();
        if (SortAlias.TryGetValue(sort, out string mapped))
            return mapped;
        foreach (var (_, s) in Sorts)
            if (s.Equals(sort, System.StringComparison.OrdinalIgnoreCase))
                return s;
        return "";
    }

    public static string SortLabel(string search, string sort)
    {
        string s = NormalizeSort(sort);
        if (string.IsNullOrEmpty(s))
            return string.IsNullOrWhiteSpace(search) ? "Mới nhất" : "Liên quan";
        foreach (var (name, v) in Sorts)
            if (v == s) return name;
        return s;
    }

    // Dong 2 theo context: giu search/c/t hien tai, chi doi sort.
    public static List<MenuItem> MenuHead(
        string host, string search, string sort, string c, string t)
    {
        host = host.TrimEnd('/');
        string url = $"{host}/xnhau";
        var res = new List<MenuItem>(2)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = url,
            }
        };
        string link(string s)
        {
            var q = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
                q.Add("search=" + HttpUtility.UrlEncode(search));
            if (!string.IsNullOrWhiteSpace(c))
                q.Add("c=" + HttpUtility.UrlEncode(c));
            if (!string.IsNullOrWhiteSpace(t))
                q.Add("t=" + HttpUtility.UrlEncode(t));
            string ns = NormalizeSort(s);
            if (!string.IsNullOrEmpty(ns))
                q.Add("sort=" + ns);
            return q.Count == 0 ? url : url + "?" + string.Join("&", q);
        }
        var sub = new List<MenuItem>(Sorts.Length + 1);
        foreach (var (name, s) in Sorts)
            sub.Add(new(name, link(s)));
        sub.Add(new("Trang chủ", url));
        res.Add(new MenuItem()
        {
            title = "Sắp xếp: " + SortLabel(search, sort),
            playlist_url = "submenu",
            submenu = sub
        });
        return res;
    }
    //
    // Client SISI chi hien MOT TANG submenu, nen moi nhom taxonomy la
    // 1 muc tang 1. `/tags/` co 14634 tag nen KHONG dua vao menu (ke ca
    // khi fetch duoc thi app se treo); chi lay `/the-loai/`.
    //
    // `groups` = 3 nhom tren trang `/the-loai/`:
    //   "Sex Châu Á" 9 | "Thể loại cụ thể" 59 | "Phim Sex" 4
    // Day la BASE menu (dong 3+); dong 1+2 do MenuHead giu theo context.
    public static List<MenuItem> Menu(string host,
        List<(string name, List<(string slug, string name)> items)> groups = null)
    {
        string url = $"{host}/xnhau";

        string cat(string slug) => $"{url}?c={HttpUtility.UrlEncode(slug)}";

        var menu = new List<MenuItem>(5);

        // Dong 3+ — taxonomy. Client chi 1 tang nen moi nhom 1 muc.
        foreach (var (name, items) in groups ?? FallbackGroups)
        {
            if (items == null || items.Count == 0)
                continue;

            var sub = new List<MenuItem>(items.Count);
            foreach (var (slug, title) in items)
            {
                if (string.IsNullOrEmpty(slug))
                    continue;

                // Client tach subtitle bang dau `:` — ten co dau se bi cat.
                sub.Add(new MenuItem(
                    string.IsNullOrEmpty(title) ? slug : title.Replace(':', '-'),
                    cat(slug)));
            }

            if (sub.Count > 0)
                menu.Add(new MenuItem()
                {
                    title = name,
                    playlist_url = "submenu",
                    submenu = sub
                });
        }

        return menu;
    }

    // Trang `/the-loai/`: 3 nhom trong sidebar,
    //   <div class="headline"><h2><a href=".../?group=clip-sex">Sex Châu Á</a>
    //   </h2></div><ul class="list">
    //     <li><a href=".../the-loai/vietnam/">Việt Nam<span class="rating">127952</span></a></li>
    // Dung `group=` de tach tung nhom (regex phai khong `.*?` lam non-greedy
    // lan sang nhom sau).
    public static List<(string name, List<(string slug, string name)> items)> Taxonomies(string html)
    {
        var groups = new List<(string, List<(string, string)>)>();

        if (string.IsNullOrEmpty(html))
            return groups;

        int start = html.IndexOf("<div class=\"main-content\">", StringComparison.Ordinal);
        if (start < 0)
            return groups;

        string box = html[start..];

        var seenGroup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rx = System.Text.RegularExpressions.Regex.Matches(box,
            "<div class=\"headline\">\\s*<h2><a[^>]*>([^<]+)</a>\\s*</h2>\\s*</div>\\s*"
            + "<ul class=\"list\">(.*?)</ul>",
            System.Text.RegularExpressions.RegexOptions.Singleline
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match m in rx)
        {
            string name = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name) || !seenGroup.Add(name))
                continue;

            var items = new List<(string, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (System.Text.RegularExpressions.Match c in
                System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value,
                    "href=\"[^\"]*?/the-loai/(?<slug>[a-z0-9\\-]+)/\"[^>]*>"
                    + "(?<name>[^<]*?)\\s*<span class=\"rating\">",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                string slug = WebUtility.HtmlDecode(c.Groups["slug"].Value).Trim();
                if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                    continue;

                items.Add((slug, WebUtility.HtmlDecode(
                    c.Groups["name"].Value).Trim()));
            }

            if (items.Count > 0)
                groups.Add((name, items));
        }

        return groups;
    }

    // Fetch `/the-loai/` that bai (mang loi) — 3 nhom, 72 muc.
    public static readonly List<(string name, List<(string slug, string name)> items)> FallbackGroups =
        new List<(string, List<(string, string)>)>
        {
            ("Sex Châu Á", new List<(string, string)>()
            {
                ("vietnam", "Việt Nam"), ("han-quoc", "Hàn Quốc"),
                ("trung-quoc", "Trung quốc"), ("thai-lan", "Thái lan"),
                ("indonesia", "Indonesia"), ("malaysia", "Malaysia"),
                ("philippines", "Philippines"), ("nhat-ban", "Nhật Bản"),
                ("an-do", "Ả Rập"),
            }),
            ("Thể loại cụ thể", new List<(string, string)>()
            {
                ("gai-xinh", "Gái Xinh"), ("gai-teen", "Gái Teen"),
                ("thu-dam", "Thủ dâm"), ("hoc-sinh", "Học sinh"),
                ("sinh-vien", "Sinh viên"), ("tu-quay", "Tự quay"),
                ("quay-len", "Quay lén"), ("xuat-trong", "Xuất trong"),
                ("nhan-tran", "Nhân trần"), ("loan-luan", "Loạn luân"),
                ("doggy", "Doggy"), ("vu-bu", "Vú bự"),
                ("blowjob-bu-cu", "Bú cu"), ("bu-lon-vet-mang", "Bú Lồn"),
                ("dit-bu-mong-to", "Đít bự"), ("anal-lo-dit", "Anal"),
                ("deepthroat-bu-cu-lut-can", "Deepthroat"),
                ("footjob-thu-dam-bang-chan", "Footjob"),
                ("handjob-hj-quay-tay", "Handjob"),
                ("kissing", "Hôn"),
                ("bdsm-bao-dam", "BDSM"), ("phim-cap-3", "Phim cap 3"),
                ("rau-fwb", "FWB"), ("may-bay-mbbg", "Máy bay"),
                ("vo-chong", "Vợ chồng"), ("cap-doi-nguoi-yeu", "Cặp đôi"),
                ("ngoai-tinh-vung-trom", "Ngoại tình"),
                ("khong-long", "Không lông"),
                ("ram-long-lon-nhieu-long", "Rậm lông"),
                ("cu-bu-cac-to", "Cu bự"),
                ("chubby-mum-mim", "Chubby"),
                ("loli-minh-day", "Loli/Mình dây"),
                ("gai-goi", "Gái gọi"),
                ("sugar-baby-sgbb", "Sugar baby"),
                ("sex-au-my", "Âu Mỹ"),
                ("lezzie-lesbian", "Lesbian"),
                ("sexy-thu-dong-vat", "Thú đồng vật"),
                ("lesbian-dong-tinh-nu", "Lesbian"),
                ("bisexual-song-tinh", "Bisexual"),
                ("gay-dong-tinh-nam", "Gay"),
                ("ladyboy-chuyen-gioi", "Ladyboy"),
                ("phim-sex", "Phim Sex"),
            }),
            ("Phim Sex", new List<(string, string)>()
            {
                ("phim-sex-nhat-ban-jav", "JAV Nhật Bản"),
                ("phim-sex-han-quoc", "Phim sex Hàn Quốc"),
                ("sex-ai", "AI"), ("hentai", "Hentai"),
            }),
        };
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        uri = uri.Trim();

        if (System.Text.RegularExpressions.Regex.IsMatch(uri, @"^[0-9]+$"))
            return uri;

        if (uri.StartsWith("/"))
            return $"{SiteHost}{uri}";

        return uri;
    }

    public static Dictionary<string, string> StreamLinks(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        string s = html.ToString();
        var stream_links = new Dictionary<string, string>(3);

        // Cách 1: video_url: '...' (xNhau player)
        var m480 = System.Text.RegularExpressions.Regex.Match(s, @"video_url:\s*'([^']+)'");
        var t480 = System.Text.RegularExpressions.Regex.Match(s, @"video_url_text:\s*'([^']+)'");
        if (m480.Success && m480.Groups[1].Value.StartsWith("http"))
            stream_links.TryAdd(t480.Success ? t480.Groups[1].Value : "480p", m480.Groups[1].Value);

        var m720 = System.Text.RegularExpressions.Regex.Match(s, @"video_alt_url:\s*'(https?://[^']+)'");
        var t720 = System.Text.RegularExpressions.Regex.Match(s, @"video_alt_url_text:\s*'([^']+)'");
        if (m720.Success)
            stream_links.TryAdd(t720.Success ? t720.Groups[1].Value : "720p", m720.Groups[1].Value);

        // Fallback: parse iframe src / source src (nếu video_url không tìm thấy)
        if (stream_links.Count == 0)
        {
            var iframe = System.Text.RegularExpressions.Regex.Match(s, "<iframe[^>]+src=[\"']([^\"']+)[\"']");
            if (iframe.Success)
                stream_links.TryAdd("stream", iframe.Groups[1].Value);
            else
            {
                var source = System.Text.RegularExpressions.Regex.Match(s, "<source[^>]+src=[\"']([^\"']+)[\"']");
                if (source.Success)
                    stream_links.TryAdd("stream", source.Groups[1].Value);
            }
        }

        return stream_links.OrderByDescending(kv => StreamQualityRank(kv.Key + " " + kv.Value))
            .ToDictionary(k => k.Key, v => v.Value);
    }
    #endregion

    #region StreamQualityRank
    static int StreamQualityRank(string s)
    {
        string l = s.ToLowerInvariant();
        if (l.Contains("2160") || l.Contains("4k"))
            return 1050;
        if (l.Contains("1080"))
            return 1080;
        if (l.Contains("720"))
            return 720;
        if (l.Contains("480"))
            return 480;
        if (l.Contains("360"))
            return 360;
        return 0;
    }
    #endregion
}
