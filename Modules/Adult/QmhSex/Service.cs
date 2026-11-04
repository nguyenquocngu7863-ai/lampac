using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace QmhSex;

public static class QmhSexTo
{
    public const string SiteHost = "https://qmhsex.shop";

    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";

    // ===== SORT (đo 2026-11-04) =====
    // Site là WordPress: ?orderby=title|modified|comment_count|rand (+&order=asc cho title).
    // Đo thật: TRANG CHỦ BỎ QUA orderby (3 giá trị cho cùng thứ tự) -> chỉ bật sort
    // khi đang ở category / tag / search.
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất", ""),
        ("A → Z", "title_asc"),
        ("Z → A", "title_desc"),
        ("Mới sửa", "modified"),
        ("Nhiều bình luận", "comment_count"),
        ("Ngẫu nhiên", "rand"),
    };

    public static bool SupportSort(string search, string c)
        => !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(c);

    public static string ClampSort(string sort, string search, string c)
    {
        if (!SupportSort(search, c))
            return null;
        if (string.IsNullOrWhiteSpace(sort))
            return null;

        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in Sorts)
            if (s == sort)
                return s;

        return null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mới nhất"
        : sort == "title_asc" ? "A → Z"
        : sort == "title_desc" ? "Z → A"
        : sort == "modified" ? "mới sửa"
        : sort == "comment_count" ? "nhiều bình luận"
        : sort == "rand" ? "ngẫu nhiên"
        : sort;

    static string SortQuery(string sort) => sort switch
    {
        "title_asc" => "orderby=title&order=asc",
        "title_desc" => "orderby=title",
        "modified" => "orderby=modified",
        "comment_count" => "orderby=comment_count",
        "rand" => "orderby=rand",
        _ => "",
    };

    // ===== URL =====
    // c = "cat/<slug>" (danh mục) | "tag/<slug>" (thẻ)
    // search -> /?s=kw ; phân trang -> /page/N (đặt TRƯỚC query)
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');
        if (pg < 1)
            pg = 1;

        string path = host + "/";
        string query = "";

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = "s=" + HttpUtility.UrlEncode(search.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string cc = c.Trim('/');
            if (cc.StartsWith("cat/", StringComparison.OrdinalIgnoreCase))
                path = host + "/" + cc.Substring(4);
            else if (cc.StartsWith("tag/", StringComparison.OrdinalIgnoreCase))
                path = host + "/tag/" + cc.Substring(4);
            else
                path = host + "/" + cc;
        }

        if (pg > 1)
            path = path.TrimEnd('/') + "/page/" + pg;

        string sq = SortQuery(sort);
        if (!string.IsNullOrEmpty(sq))
            query = string.IsNullOrEmpty(query) ? sq : query + "&" + sq;

        return path + (string.IsNullOrEmpty(query) ? "" : "?" + query);
    }

    // ===== LIST =====
    // <article data-video-uid="1" data-post-id="15273" ...>
    //   <a href="https://qmhsex.shop/15273" title="Tên phim">
    //     <div class="post-thumbnail"><div class="post-thumbnail-container">
    //       <img ... data-src="https://.../anh.jpg" alt="Tên phim">
    static readonly Regex ArticleRx = new Regex(
        "<article[^>]*data-post-id=\"(?<id>\\d+)\"[^>]*>\\s*<a href=\"(?<url>[^\"]+)\" title=\"(?<title>[^\"]*)\"",
        RegexOptions.IgnoreCase);

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string route, string html)
        => ParseArticles(route, html);

    static List<Shared.Models.SISI.Base.PlaylistItem> ParseArticles(string route, string html)
    {
        var list = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in ArticleRx.Matches(html))
        {
            string url = HttpUtility.HtmlDecode(m.Groups["url"].Value.Trim());
            if (string.IsNullOrEmpty(url))
                continue;
            if (!url.StartsWith("http"))
                url = SiteHost + "/" + url.TrimStart('/');
            if (!seen.Add(url))
                continue;

            string title = HttpUtility.HtmlDecode(m.Groups["title"].Value.Trim());
            if (string.IsNullOrEmpty(title))
                title = "QmhSex";

            string poster = "";
            string window = html.Substring(m.Index, Math.Min(1500, html.Length - m.Index));
            var im = Regex.Match(window, "data-src=\"(?<img>[^\"]+)\"", RegexOptions.IgnoreCase);
            if (im.Success)
                poster = HttpUtility.HtmlDecode(im.Groups["img"].Value.Trim());
            else
            {
                im = Regex.Match(window, "<img[^>]*src=\"(?<img>[^\"]+)\"", RegexOptions.IgnoreCase);
                if (im.Success)
                    poster = HttpUtility.HtmlDecode(im.Groups["img"].Value.Trim());
            }

            list.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                name = title,
                video = route + "?uri=" + HttpUtility.UrlEncode(url),
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "qmhsex",
                    href = url,
                    image = poster
                }
            });
        }

        return list;
    }

    // ===== PHIM LIÊN QUAN (popup) =====
    // Trang detail:
    //   <div class="under-video-block"><h2 class="widget-title">Gợi ý cho bạn</h2>
    //     <div><article ...> x14</div></div>
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(string route, string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        int i = html.IndexOf("under-video-block", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        string block = html.Substring(i, Math.Min(60000, html.Length - i));
        int end = block.IndexOf("<div class=\"clear\"", StringComparison.OrdinalIgnoreCase);
        if (end > 0)
            block = block.Substring(0, end);

        return ParseArticles(route, block);
    }

    // Số trang: <div class="pagination"> ... <a href=".../page/140">Last</a>
    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 0;

        int i = html.IndexOf("<div class=\"pagination\"", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return 0;

        string block = html.Substring(i, Math.Min(4000, html.Length - i));
        int end = block.IndexOf("</div>", StringComparison.OrdinalIgnoreCase);
        if (end > 0)
            block = block.Substring(0, end);

        int max = 1;
        foreach (Match m in Regex.Matches(block, "page/(\\d+)"))
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;

        return max;
    }

    // ===== PLAYER =====
    // Trang detail có:
    //   var sources=["https://s63.xvideo.lat/embed/xxx.html"];
    //   <button id="server1" class="video-server bt_normal">VIP 1</button>
    // Trả về: JSON array (đúng format browser gửi) + danh sách server.
    public static (string sourcesJson, List<int> servers) PlayerInfo(string html)
    {
        var servers = new List<int>();
        foreach (Match m in Regex.Matches(html ?? "", "id=\"server(\\d+)\"", RegexOptions.IgnoreCase))
            if (int.TryParse(m.Groups[1].Value, out int sid) && !servers.Contains(sid))
                servers.Add(sid);
        if (servers.Count == 0)
            servers.Add(1);

        string sourcesJson = null;
        var sm = Regex.Match(html ?? "", @"var\s+sources\s*=\s*(\[[^\]]*\])");
        if (sm.Success)
        {
            var urls = new List<string>();
            foreach (Match u in Regex.Matches(sm.Groups[1].Value.Replace("\\/", "/"),
                "\"(?<u>https?://[^\"]+)\""))
            {
                string url = u.Groups["u"].Value;
                if (!urls.Contains(url))
                    urls.Add(url);
            }
            if (urls.Count > 0)
                sourcesJson = "[" + string.Join(",", urls.Select(x => "\"" + x + "\"")) + "]";
        }

        return (sourcesJson, servers);
    }

    // JSON của admin-ajax: {"success":true,"data":{"sources":{"file":"https://go1.../x/xxx.html","type":"iframe"}}}
    public static List<string> EmbedFiles(string json)
    {
        var files = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
            return files;

        try
        {
            using var doc = JsonDocument.Parse(json.Trim('\uFEFF', ' ', '\t', '\r', '\n'));
            Walk(doc.RootElement, files);
        }
        catch { }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return files.Where(x => seen.Add(x)).ToList();
    }

    static void Walk(JsonElement el, List<string> files)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String && p.NameEquals("file"))
                {
                    string s = p.Value.GetString();
                    if (!string.IsNullOrEmpty(s) && s.StartsWith("http"))
                        files.Add(s);
                }
                else
                {
                    Walk(p.Value, files);
                }
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in el.EnumerateArray())
                Walk(i, files);
        }
    }

    // Trang embed (go1.qooglevideo.shop) -> jwplayer:
    //   sources: [{"file":"https:\/\/stream.bytecdn.cfd\/qmhz\/ttk\/1\/xxx.m3u8","type":"mp4","label":"DP 0"}, ...]
    public static List<(string label, string url)> Streams(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        string text = html.Replace("\\/", "/");
        int n = 0;
        foreach (Match m in Regex.Matches(text,
            "\"file\"\\s*:\\s*\"(?<file>https?://[^\"]+)\"(?<rest>[^{}]*)"))
        {
            string file = m.Groups["file"].Value;
            if (!file.Contains(".m3u8") && !file.Contains(".mp4"))
                continue;

            string label = "";
            var lm = Regex.Match(m.Groups["rest"].Value,
                "\"label\"\\s*:\\s*\"(?<label>[^\"]*)\"");
            if (lm.Success)
                label = lm.Groups["label"].Value.Trim();

            if (string.IsNullOrEmpty(label))
                label = "HLS " + (++n);

            res.Add((label, file));
        }

        return res;
    }

    // ===== CHIP: danh muc + the cua video (popup) =====
    // <article id="post-15271" class="post-15271 post type-post ... hentry
    //          category-clip-sex category-phim-sex-chau-a ...
    //          tag-clip-sex-telegram tag-phe69 ...">
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var m = Regex.Match(html, "<article id=\"post-\\d+\" class=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (!m.Success)
            return list;

        foreach (Match c in Regex.Matches(m.Groups[1].Value, @"\b(category|tag)-([a-z0-9\-]+)\b",
            RegexOptions.IgnoreCase))
        {
            string slug = c.Groups[2].Value.Trim('-');
            if (string.IsNullOrEmpty(slug))
                continue;

            string kind = c.Groups[1].Value.ToLowerInvariant() == "category" ? "cat" : "tag";
            list.Add((kind, slug, PrettySlug(slug)));
        }

        return list;
    }

    // Ten hien thi lay san tu trang: nav danh muc + "Tu khoa" (tag cloud)
    public static Dictionary<string, string> PageNames(string html)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(html))
            return map;

        foreach (Match m in Regex.Matches(html,
            "<a[^>]+href=\"https://qmhsex\\.shop/tag/(?<s>[a-z0-9\\-]+)\"[^>]*>(?<n>[^<]{1,80})</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups["s"].Value;
            string name = HttpUtility.HtmlDecode(m.Groups["n"].Value.Trim());
            if (!string.IsNullOrEmpty(slug) && name.Length > 0 && !map.ContainsKey("tag/" + slug))
                map["tag/" + slug] = name;
        }

        foreach (Match m in Regex.Matches(html,
            "<a[^>]+href=\"https://qmhsex\\.shop/(?!tag/)(?<s>[a-z0-9][a-z0-9\\-]*)\"[^>]*>(?<n>[^<]{1,80})</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups["s"].Value;
            if (slug.All(char.IsDigit))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups["n"].Value.Trim());
            if (name.Length > 0 && !map.ContainsKey("cat/" + slug))
                map["cat/" + slug] = name;
        }

        return map;
    }

    public static string PrettySlug(string slug)
    {
        if (string.IsNullOrEmpty(slug))
            return "";
        return HttpUtility.HtmlDecode(slug.Replace('-', ' '));
    }

    // ===== MENU =====
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/qmhsex";

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

        var res = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };

        if (!SupportSort(search, c))
            return res;

        var sub = new List<Shared.Models.SISI.Base.MenuItem>();
        foreach (var (name, s) in Sorts)
            sub.Add(new Shared.Models.SISI.Base.MenuItem(name, link(s)));

        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host,
        List<(string name, string slug, int count)> cats,
        List<(string name, string slug, int count)> tags)
    {
        host = host.TrimEnd('/');
        var menu = new List<Shared.Models.SISI.Base.MenuItem>();

        if (cats != null && cats.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = cats.Select(x => new Shared.Models.SISI.Base.MenuItem(
                    TaxTitle(x),
                    host + "/qmhsex?c=cat/" + x.slug)).ToList()
            });
        }

        if (tags != null && tags.Count > 0)
        {
            // chi top 100 theo so luong (the cua tung phim da co trong chip popup)
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thẻ",
                playlist_url = "submenu",
                submenu = tags.Take(100).Select(x => new Shared.Models.SISI.Base.MenuItem(
                    TaxTitle(x),
                    host + "/qmhsex?c=tag/" + x.slug)).ToList()
            });
        }

        return menu;
    }

    static string TaxTitle((string name, string slug, int count) x)
        => x.count > 0 ? $"{x.name} ({x.count})" : x.name;

    // Ten danh muc / the lay tu MENU da cache (menu lay tu WP REST nen ten dep hon):
    //   menu item: title = "Clip sex (98)", playlist_url = ".../qmhsex?c=cat/clip-sex"
    // -> map "cat/clip-sex" -> "Clip sex"
    public static Dictionary<string, string> NamesFromMenu(List<Shared.Models.SISI.Base.MenuItem> menu)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (menu == null)
            return map;

        foreach (var row in menu)
        {
            if (row.submenu == null)
                continue;

            foreach (var item in row.submenu)
            {
                string url = item.playlist_url ?? "";
                int i = url.IndexOf("?c=");
                if (i < 0)
                    continue;

                string val = url.Substring(i + 3);
                int amp = val.IndexOf('&');
                if (amp > 0)
                    val = val.Substring(0, amp);

                int slash = val.IndexOf('/');
                if (slash < 0)
                    continue;

                string title = item.title ?? "";
                int p = title.LastIndexOf(" (");
                if (p > 0 && title.EndsWith(")"))
                    title = title.Substring(0, p);

                if (title.Length > 0)
                    map[val.Substring(0, slash) + "/" + val.Substring(slash + 1)] = title;
            }
        }

        return map;
    }

    // WP REST: /wp-json/wp/v2/categories|tags -> [{id,name,slug,count}, ...]
    public static List<(string name, string slug, int count)> Taxonomy(string json)
    {
        var list = new List<(string, string, int)>();
        if (string.IsNullOrWhiteSpace(json))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(json.Trim('\uFEFF', ' ', '\t', '\r', '\n'));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;

                string slug = GetStr(el, "slug");
                if (string.IsNullOrEmpty(slug))
                    continue;

                string name = GetStr(el, "name");
                if (string.IsNullOrEmpty(name))
                    name = slug;

                list.Add((name, slug, GetInt(el, "count")));
            }
        }
        catch { }

        return list;
    }

    static string GetStr(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    static int GetInt(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt32(out int i) ? i : 0;
        return 0;
    }
}
