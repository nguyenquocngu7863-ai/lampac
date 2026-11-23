using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
namespace FapClub;
public static class FapClubTo
{
    public const string SiteHost = "https://xxx.fapclub.vip";

    // home: / ; page: /latest-updates/{pg}/ ; category: /categories/{slug}/[{pg}/]
    // search: /search/?q={q} ; search page: /search/{pg}/?q={q}
    // list (dòng 3 "Danh sách", toàn cục): /top-rated/[{pg}/] , /most-popular/[{pg}/]
    public static string Uri(string host, string category, string search, int pg, string list = null)
    {
        string uri;
        if (!string.IsNullOrEmpty(search))
            uri = pg > 1 ? $"{host}/search/{pg}/?q={HttpUtility.UrlEncode(search)}"
                         : $"{host}/search/?q={HttpUtility.UrlEncode(search)}";
        else if (!string.IsNullOrEmpty(category))
            uri = pg > 1 ? $"{host}/categories/{category}/{pg}/"
                         : $"{host}/categories/{category}/";
        else if (IsList(list))
            uri = pg > 1 ? $"{host}/{list}/{pg}/" : $"{host}/{list}/";
        else
            uri = pg > 1 ? $"{host}/latest-updates/{pg}/" : $"{host}/";
        return uri;
    }

    // danh sách toàn cục có thật trên site (đo 2026-11-22: 3 mục, tập phim khác nhau, có phân trang)
    public static readonly (string slug, string label)[] Lists =
    {
        ("latest-updates", "Mới nhất"),
        ("top-rated", "Top đánh giá"),
        ("most-popular", "Phổ biến")
    };

    public static bool IsList(string list)
        => !string.IsNullOrEmpty(list) && Array.Exists(Lists, x => x.slug == list);

    // list lạ từ query param -> null (đừng để lọt vào Uri/cache key)
    public static string ClampList(string list, string search, string category)
        => string.IsNullOrEmpty(search) && string.IsNullOrEmpty(category) && IsList(list) ? list : null;

    public static string ListLabel(string list)
        => Array.Find(Lists, x => x.slug == list).label;

    public static List<PlaylistItem> Playlist(string host, ReadOnlySpan<char> html)
        => Cards(host, html);

    // Danh sách "Related Videos" của trang detail (khối sau id="rell"), cùng shape card với list
    public static List<PlaylistItem> Related(string host, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;
        int idx = html.IndexOf("id=\"rell\"");
        if (idx < 0)
            return null;
        return Cards(host, html.Slice(idx));
    }

    static List<PlaylistItem> Cards(string host, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;
        var cards = Rx.Split("<div class=\"video\">", html, 1);
        if (cards.Count == 0)
            return null;
        var playlists = new List<PlaylistItem>(cards.Count);
        foreach (var card in cards.Rows())
        {
            string href = card.Match("<a href=\"([^\"]+/videos/[^\"]+)\"");
            if (string.IsNullOrEmpty(href))
                continue;
            string title = card.Match("<h2>\\s*<a href=\"[^\"]+\" title=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(title))
                title = card.Match("alt=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            string img = card.Match("<img src=\"([^\"]+)\"");
            string time = card.Match("<div class=\"time\">([^<]+)</div>", trim: true);
            string uri = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : host + (href.StartsWith("/") ? "" : "/") + href;
            uri = uri.Replace(SiteHost, "");
            playlists.Add(new PlaylistItem()
            {
                name = HttpUtility.HtmlDecode(title),
                video = "fapclub/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                picture = img,
                time = time,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "fapclub",
                    href = uri,
                    image = img
                }
            });
        }
        return playlists.Count > 0 ? playlists : null;
    }

    // Chỉ nhận /videos/{id}/{slug}/ — chặn SSRF/host lạ
    public static string DetailUrl(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;
        uri = uri.Trim();
        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!uri.StartsWith(SiteHost + "/", StringComparison.OrdinalIgnoreCase))
                return null;
            uri = uri.Substring(SiteHost.Length);
        }
        if (!uri.StartsWith("/videos/", StringComparison.OrdinalIgnoreCase))
            return null;
        if (uri.IndexOfAny(new[] { '?', '#', ' ', '\r', '\n' }) >= 0)
            return null;
        return uri;
    }

    // data-q = "1080p;hash;FHD 1080p;2027750;1795333475;iCL9...,720p;..."
    // stream  = https://{n}.vstor.top/whpvid/{ts}/{token}/{folder}/{id}/{id}_{quality}.mp4
    // folder  = floor(id/1000)*1000
    //
    // LƯU Ý 720p (đo 2026-11-22): file 720p KHÔNG còn trên CDN — mọi URL 720p trả
    // trang Google (content-type text/html) thay vì mp4. Đo 5 video × 5 node
    // (d1/d2/d3/d7/s2): 1080p/480p/240p = MP4, 720p = DEAD 100%. Metadata 720p
    // vẫn nằm trong data-q nên phải lọc bỏ, nếu không player chọn "auto" là ăn link chết
    // ("Failed to load because no supported source was found").
    public static StreamItem Stream(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;
        string dataQ = Rx.Match(html, "id=\"player\"[^>]*data-q=\"([^\"]+)\"");
        string dataN = Rx.Match(html, "id=\"player\"[^>]*data-n=\"([^\"]+)\"");
        string dataId = Rx.Match(html, "id=\"player\"[^>]*data-id=\"([^\"]+)\"");
        if (string.IsNullOrEmpty(dataQ) || string.IsNullOrEmpty(dataN) || string.IsNullOrEmpty(dataId))
            return null;
        if (!int.TryParse(dataId, out int id) || id <= 0)
            return null;
        string folder = (id / 1000 * 1000).ToString();

        var qualitys = new Dictionary<string, string>();
        var all = new Dictionary<string, string>();
        foreach (string part in dataQ.Replace("&nbsp;", " ").Split(','))
        {
            string[] f = part.Split(';');
            if (f.Length < 6)
                continue;
            string quality = f[0].Trim();
            string ts = f[4].Trim();
            string token = f[5].Trim();
            if (string.IsNullOrEmpty(quality) || string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(token))
                continue;
            string label = quality.Equals("1080p", StringComparison.OrdinalIgnoreCase) ? "1080p" :
                           quality.Equals("720p", StringComparison.OrdinalIgnoreCase) ? "720p" :
                           quality.Equals("480p", StringComparison.OrdinalIgnoreCase) ? "480p" : quality;
            string url = $"https://{dataN}.vstor.top/whpvid/{ts}/{token}/{folder}/{id}/{id}_{quality}.mp4";
            all[label] = url;
            if (label.Equals("720p", StringComparison.OrdinalIgnoreCase))
                continue;                       // CDN không còn file 720p
            qualitys[label] = url;
        }
        // video chỉ có 720p -> vẫn phải dùng (thà có link chết hơn là không có gì)
        if (qualitys.Count == 0)
            qualitys = all;
        if (qualitys.Count == 0)
            return null;
        if (!qualitys.ContainsKey("auto"))
        {
            foreach (string q in new[] { "1080p", "480p", "240p", "720p" })
            {
                if (qualitys.TryGetValue(q, out string u)) { qualitys["auto"] = u; break; }
            }
        }
        return new StreamItem() { qualitys = qualitys };
    }

    // Dòng 3 "Danh sách" — list toàn cục, KHÔNG mang c/search (đổi tập phim, không đổi thứ tự)
    public static List<MenuItem> ListMenu(string host)
    {
        var items = new List<MenuItem>(Lists.Length);
        foreach (var x in Lists)
            items.Add(new MenuItem(x.label, host + "/fapclub?list=" + x.slug));
        return items;
    }

    // Menu: 50 category, lấy trực tiếp từ trang chủ (đều tiếng Anh — đạt luật cứng)
    // URL phải TUYỆT ĐỐI: sisi.js push thẳng b.playlist_url vào Lampa.Activity,
    // URL tương đối sẽ bị fetch sai origin -> bấm menu không ra gì.
    public static List<MenuItem> Categories(string host, ReadOnlySpan<char> html)
    {
        var result = new List<MenuItem>();
        if (html.IsEmpty)
            return result;
        var rx = Rx.Split("href=\"https://xxx.fapclub.vip/categories/", html, 1);
        if (rx.Count == 0)
            return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rx.Rows())
        {
            string slug = row.Match("^([a-z0-9-]+)/\"");
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;
            string title = row.Match("\"[^>]*>([^<]+)</a>");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            result.Add(new MenuItem(HttpUtility.HtmlDecode(title), host + "/fapclub?c=" + slug));
        }
        return result;
    }

    // Chip của CHÍNH video, lấy động từ trang detail:
    //   <div class="vcatsp"><span>Categories:</span> <a href=".../categories/<slug>/" class="video_cat">Tên</a> ...</div>
    public static List<PopupItem> Chips(string host, ReadOnlySpan<char> html)
    {
        var list = new List<PopupItem>();
        if (html.IsEmpty)
            return list;

        int idx = html.IndexOf("class=\"vcatsp\"");
        if (idx < 0)
            return list;

        var block = html.Slice(idx);
        int end = block.IndexOf("</div>");
        if (end > 0)
            block = block.Slice(0, end);

        var rx = Rx.Split("href=\"" + SiteHost + "/categories/", block, 1);
        if (rx.Count == 0)
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rx.Rows())
        {
            string slug = row.Match("^([a-z0-9-]+)/\"");
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;
            string title = row.Match(">([^<]+)</a>");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            list.Add(new PopupItem
            {
                title = HttpUtility.HtmlDecode(title.Trim()),
                url = host + "/fapclub?c=" + slug
            });
        }
        return list;
    }

    // Popup long-press: chip thể loại của video + 1 mục plain "Video liên quan" (?rel=)
    public static PopupResult Popup(string host, string uri, ReadOnlySpan<char> html)
    {
        var groups = new List<PopupGroup>();

        var chips = Chips(host, html);
        if (chips.Count > 0)
            groups.Add(new PopupGroup { title = "Thể loại", items = chips });

        // chỉ thêm khi trang detail thật sự có khối "Related Videos" (tránh link chết)
        if (!string.IsNullOrEmpty(uri) && html.IndexOf("id=\"rell\"") >= 0)
        {
            groups.Add(new PopupGroup
            {
                title = "Liên quan",
                items = new List<PopupItem>
                {
                    new PopupItem
                    {
                        title = "Video liên quan",
                        plain = true,
                        url = host + "/fapclub?rel=" + HttpUtility.UrlEncode(uri)
                    }
                }
            });
        }

        return groups.Count > 0 ? new PopupResult { groups = groups } : null;
    }

    public class PopupResult
    {
        public object[] items { get; set; } = new object[0];
        public List<PopupGroup> groups { get; set; }
    }
    public class PopupGroup
    {
        public string title { get; set; }
        public List<PopupItem> items { get; set; }
    }
    public class PopupItem
    {
        public string title { get; set; }
        public string url { get; set; }
        public bool plain { get; set; }
        public bool play { get; set; }
    }
}
