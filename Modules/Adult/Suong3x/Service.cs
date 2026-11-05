using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Suong3x;

public static class Suong3xTo
{
    public const string SiteHost = "https://phimsexsuong3x.net";

    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36";

    public const int RestPageSize = 20;

    // ===== SORT (đo 2026-11-05) =====
    // Trang HTML KHÔNG có sort (?orderby=views|rand = không đổi thứ tự).
    // WP REST mở -> các sort "modified|title" đi qua REST (orderby=..., order=asc/desc).
    // REST orderby=rand và meta_value_num (views) -> 400, không dùng.
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất", ""),
        ("Mới sửa", "modified"),
        ("A → Z", "title_asc"),
        ("Z → A", "title_desc"),
    };

    public static string ClampSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return "";

        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in Sorts)
            if (s == sort)
                return s;

        return "";
    }

    public static bool IsRestSort(string sort) => !string.IsNullOrEmpty(sort);

    public static string SortLabel(string sort)
    {
        string s = ClampSort(sort);
        foreach (var (name, ss) in Sorts)
            if (ss == s)
                return name.ToLowerInvariant();

        return "mới nhất";
    }

    #region URL
    // c = "cat/<slug>" | "tag/<slug>"
    // HTML: home /page/N/ · category /<slug>/ · tag /tag/<slug>/ · search /?s=<q>
    public static string Uri(string host, string search, string c, int pg)
    {
        host = string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');
        if (pg < 1)
            pg = 1;

        string path;
        string q = "";

        if (!string.IsNullOrWhiteSpace(search))
        {
            path = host + "/";
            q = "?s=" + HttpUtility.UrlEncode(search.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string cc = c.Trim('/');
            int slash = cc.IndexOf('/');
            if (slash > 0)
            {
                string kind = cc.Substring(0, slash).ToLowerInvariant();
                string slug = cc.Substring(slash + 1).Trim('/');
                path = kind == "tag" ? host + "/tag/" + slug + "/" : host + "/" + slug + "/";
            }
            else
            {
                path = host + "/" + cc + "/";
            }
        }
        else
        {
            path = host + "/";
        }

        if (pg > 1)
            path = path.TrimEnd('/') + "/page/" + pg + "/";

        return path + q;
    }

    // REST (mở) — chỉ dùng cho sort modified/title:
    // /wp-json/wp/v2/posts?per_page=&page=&_embed=1&orderby=modified|title&order=asc|desc
    //   &search=<q> | &categories=<id> | &tags=<id>
    public static string RestUri(string host, string search, string c, int pg, string sort, int taxId = 0)
    {
        host = string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');
        if (pg < 1)
            pg = 1;

        var q = new List<string>()
        {
            "per_page=" + RestPageSize,
            "page=" + pg,
            "_embed=1"
        };

        if (sort == "title_asc")
        {
            q.Add("orderby=title");
            q.Add("order=asc");
        }
        else if (sort == "title_desc")
        {
            q.Add("orderby=title");
            q.Add("order=desc");
        }
        else
        {
            q.Add("orderby=modified");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            q.Add("search=" + System.Uri.EscapeDataString(search.Trim()));
        }
        else if (taxId > 0 && !string.IsNullOrWhiteSpace(c))
        {
            int slash = c.IndexOf('/');
            string kind = slash > 0 ? c.Substring(0, slash).ToLowerInvariant() : "";
            q.Add(kind == "tag" ? "tags=" + taxId : "categories=" + taxId);
        }

        return host + "/wp-json/wp/v2/posts?" + string.Join("&", q);
    }

    // slug -> id (khi taxonomy cache chưa có)
    public static string RestTaxUri(string host, string kind, string slug)
    {
        host = string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');
        string tax = kind == "tag" ? "tags" : "categories";
        return host + "/wp-json/wp/v2/" + tax + "?slug=" + System.Uri.EscapeDataString(slug) + "&_fields=id";
    }

    public static string RestCategoriesUri(string host)
    {
        host = string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');
        return host + "/wp-json/wp/v2/categories?per_page=100&_fields=id,name,slug,count";
    }

    public static string RestTagsUri(string host)
    {
        host = string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');
        return host + "/wp-json/wp/v2/tags?per_page=100&orderby=count&order=desc&_fields=id,name,slug,count";
    }
    #endregion

    #region LIST (HTML)
    // <div class="video-block thumbs-rotation" data-post-id="53281" ...>
    //   <a class="thumb" href="https://site/<slug>/<id>/">
    //     <img class="video-img img-fluid" alt="Tên phim" data-src=".../thumb.jpg">
    //     <span class="views-number">51014</span>
    //     <span class="duration">02:00</span>
    //   </a>
    //   <a class="infos" href="..." title="Tên phim"><span class="title">Tên phim</span></a>
    // </div>
    static readonly Regex PostIdRx = new Regex("data-post-id=\"(\\d+)\"");

    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matches = PostIdRx.Matches(html);

        for (int n = 0; n < matches.Count; n++)
        {
            int start = matches[n].Index;
            int end = n + 1 < matches.Count ? matches[n + 1].Index : Math.Min(html.Length, start + 3000);
            string block = html.Substring(start, end - start);

            var hm = Regex.Match(block, "<a class=\"thumb\" href=\"(?<url>[^\"]+)\"");
            if (!hm.Success)
                continue;

            string url = HttpUtility.HtmlDecode(hm.Groups["url"].Value.Trim());
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = SiteHost + "/" + url.TrimStart('/');
            if (!seen.Add(url))
                continue;

            string title = "";
            var tm = Regex.Match(block, "class=\"video-img img-fluid\" alt=\"(?<t>[^\"]*)\"");
            if (tm.Success)
                title = HttpUtility.HtmlDecode(tm.Groups["t"].Value.Trim());
            if (string.IsNullOrEmpty(title))
            {
                tm = Regex.Match(block, "<span class=\"title\">(?<t>[^<]+)</span>");
                if (tm.Success)
                    title = HttpUtility.HtmlDecode(tm.Groups["t"].Value.Trim());
            }
            if (string.IsNullOrEmpty(title))
                title = "Suong3x";

            string thumb = "";
            var im = Regex.Match(block, "class=\"video-img img-fluid\"[^>]*data-src=\"(?<img>[^\"]+)\"");
            if (im.Success)
                thumb = HttpUtility.HtmlDecode(im.Groups["img"].Value.Trim());

            string time = "";
            var dm = Regex.Match(block, "<span class=\"duration\">(?<t>[^<]+)</span>");
            if (dm.Success)
                time = dm.Groups["t"].Value.Trim();

            list.Add(new PlaylistItem()
            {
                name = title,
                video = route + "?uri=" + HttpUtility.UrlEncode(url),
                picture = thumb,
                time = time.Length > 0 ? time : null,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "suong3x",
                    href = url,
                    image = thumb
                }
            });
        }

        return list;
    }

    // "Phim Liên Quan" ở trang detail (cho popup)
    public static List<PlaylistItem> PlaylistRelated(string route, string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<PlaylistItem>();

        int i = html.IndexOf("related-videos", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<PlaylistItem>();

        string block = html.Substring(i, Math.Min(40000, html.Length - i));
        int end = block.IndexOf("</section>", StringComparison.OrdinalIgnoreCase);
        if (end > 0)
            block = block.Substring(0, end);

        return Playlist(route, block);
    }

    // <ul class="pagination ..."> ... <a class="page-link" href=".../page/380/">380</a>
    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 0;

        int i = html.IndexOf("class=\"pagination", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return 0;

        string block = html.Substring(i, Math.Min(4000, html.Length - i));
        int max = 0;
        foreach (Match m in Regex.Matches(block, "page/(\\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;

        return max;
    }
    #endregion

    #region LIST (WP REST — cho sort)
    // [{id, link, title:{rendered}, _embedded:{wp:featuredmedia:[{source_url}]}}, ...]
    // (REST trả link canonical đúng dạng /<slug>/<id>/ như card HTML)
    public static List<PlaylistItem> RestPlaylist(string route, string json)
    {
        var list = new List<PlaylistItem>();
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

                string link = GetStr(el, "link");
                if (string.IsNullOrEmpty(link))
                    continue;

                string title = "";
                if (el.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.Object)
                    title = HttpUtility.HtmlDecode(GetStr(t, "rendered")).Trim();
                if (string.IsNullOrEmpty(title))
                    title = "Suong3x";

                string thumb = "";
                if (el.TryGetProperty("_embedded", out var emb) && emb.ValueKind == JsonValueKind.Object
                    && emb.TryGetProperty("wp:featuredmedia", out var fm) && fm.ValueKind == JsonValueKind.Array
                    && fm.GetArrayLength() > 0 && fm[0].ValueKind == JsonValueKind.Object)
                    thumb = GetStr(fm[0], "source_url");

                list.Add(new PlaylistItem()
                {
                    name = title,
                    video = route + "?uri=" + HttpUtility.UrlEncode(link),
                    picture = thumb,
                    json = true,
                    bookmark = new Bookmark()
                    {
                        site = "suong3x",
                        href = link,
                        image = thumb
                    }
                });
            }
        }
        catch { }

        return list;
    }

    // /wp-json/wp/v2/categories|tags -> [{id,name,slug,count}, ...]
    public static List<(int id, string name, string slug, int count)> Taxonomy(string json)
    {
        var list = new List<(int, string, string, int)>();
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

                list.Add((GetInt(el, "id"), name, slug, GetInt(el, "count")));
            }
        }
        catch { }

        return list;
    }

    // /wp-json/wp/v2/{categories|tags}?slug=..&_fields=id -> [{id}]
    public static int TaxId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(json.Trim('\uFEFF', ' ', '\t', '\r', '\n'));
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                return GetInt(doc.RootElement[0], "id");
        }
        catch { }

        return 0;
    }
    #endregion

    #region DETAIL + PLAYER
    // <meta itemprop="embedURL" content="https://play2.cdn-xvideos-xnxx.xyz/embed4.php?id1=..." />
    public static string EmbedUrl(string html)
    {
        var m = Regex.Match(html ?? "", "itemprop=\"embedURL\"[^>]*content=\"(?<u>[^\"]+)\"");
        if (!m.Success)
            return null;

        string u = HttpUtility.HtmlDecode(m.Groups["u"].Value.Trim());
        return u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : null;
    }

    // Trang embed (JWPlayer): sources: [{"file":"https://sv1.cdn-78-vid.top/<id1>/<tok>.m3u8","label":"0","type":"hls"}]
    // Host embed có Cloudflare: bắt buộc fetch kèm Referer = site.
    public static string StreamFromEmbed(string html)
    {
        var m = Regex.Match(html ?? "", "\"file\"\\s*:\\s*\"(?<u>https?://[^\"]+)\"");
        if (!m.Success)
            return null;

        string u = m.Groups["u"].Value.Replace("\\/", "/");
        return (u.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            ? u : null;
    }
    #endregion

    #region CHIP (popup)
    // Danh mục: <a class="btn btn-black" href="{host}/{slug}/" title="Tên">Tên <span>N</span></a>
    // Thẻ:      <a class="btn btn-grey"  href="{host}/tag/{slug}/" title="Tên">Tên</a>
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        foreach (Match m in Regex.Matches(html,
            "<a class=\"btn btn-black\" href=\"(?<href>[^\"]+?)\"[^>]*title=\"(?<name>[^\"]*)\""))
        {
            string slug = SlugFromPath(m.Groups["href"].Value);
            if (string.IsNullOrEmpty(slug))
                continue;

            list.Add(("cat", slug, NameOr(m, slug)));
        }

        foreach (Match m in Regex.Matches(html,
            "<a class=\"btn btn-grey\" href=\"(?<href>[^\"]*?/tag/(?<slug>[^/\"]+?)/?)\"[^>]*title=\"(?<name>[^\"]*)\""))
        {
            string slug = m.Groups["slug"].Value;
            if (string.IsNullOrEmpty(slug))
                continue;

            list.Add(("tag", slug, NameOr(m, slug)));
        }

        return list;
    }

    static string NameOr(Match m, string slug)
    {
        string name = HttpUtility.HtmlDecode(m.Groups["name"].Value.Trim());
        return string.IsNullOrEmpty(name) ? slug : name;
    }

    // href = "{host}/{slug}/" -> slug (chỉ nhận đúng 1 segment sau host)
    static string SlugFromPath(string url)
    {
        if (string.IsNullOrEmpty(url))
            return "";

        string path = url;
        int i = path.IndexOf('/', 8);
        if (i > 0)
            path = path.Substring(i + 1);

        int q = path.IndexOf('?');
        if (q > 0)
            path = path.Substring(0, q);

        string[] seg = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (seg.Length != 1)
            return "";

        return seg[0];
    }
    #endregion

    #region MENU
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/suong3x";

        string Link(string s)
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

        var res = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };

        var sub = new List<MenuItem>();
        foreach (var (name, s) in Sorts)
            sub.Add(new MenuItem(name, Link(s)));

        res.Add(new MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    public static List<MenuItem> Menu(string host,
        List<(int id, string name, string slug, int count)> cats,
        List<(int id, string name, string slug, int count)> tags)
    {
        host = host.TrimEnd('/');
        var menu = new List<MenuItem>();

        if (cats != null && cats.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = cats.Select(x => new MenuItem(
                    TaxTitle(x),
                    host + "/suong3x?c=cat/" + HttpUtility.UrlEncode(x.slug))).ToList()
            });
        }

        if (tags != null && tags.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Thẻ",
                playlist_url = "submenu",
                submenu = tags.Take(100).Select(x => new MenuItem(
                    TaxTitle(x),
                    host + "/suong3x?c=tag/" + HttpUtility.UrlEncode(x.slug))).ToList()
            });
        }

        return menu;
    }

    static string TaxTitle((int id, string name, string slug, int count) x)
        => x.count > 0 ? $"{x.name} ({x.count})" : x.name;
    #endregion

    #region JSON helpers
    static string GetStr(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    static int GetInt(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt32(out int i) ? i : 0;
        return 0;
    }
    #endregion
}
