using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Youporn;

public static class YoupornTo
{
    public static string SiteHost = "https://www.youporn.com";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // ===== SORT =====
    // Home: /browse/time/ (moi nhat) · /most_viewed/ · /top_rated/ · /browse/duration/
    // Category/tag/pornstar: hau to path .../views/ /rating/ /time/ /duration/
    public static readonly (string name, string key)[] Sorts =
    {
        ("Mới nhất", "time"),
        ("Xem nhiều", "views"),
        ("Đánh giá cao", "rating"),
        ("Dài nhất", "duration")
    };

    public static string ClampSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return null;

        foreach (var o in Sorts)
            if (o.key == sort.Trim().ToLowerInvariant())
                return o.key;

        return null;
    }

    public static string SortLabel(string sort)
    {
        foreach (var o in Sorts)
            if (o.key == sort)
                return o.name.ToLowerInvariant();

        return "mới nhất";
    }

    static string HomePath(string sort)
    {
        switch (sort)
        {
            case "views": return "/most_viewed/";
            case "rating": return "/top_rated/";
            case "duration": return "/browse/duration/";
            default: return "/browse/time/";
        }
    }

    // ===== URL =====
    // search  /search/?query=<kw>&page=<pg>
    // list    /{c}/?page=<pg>   (c = "category/bigtits", "porntags/butt", "pornstar/<slug>")
    // sort    them hau to vao path cua c
    public static string ListUrl(string host, string search, string c, string sort, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');
        if (pg < 1)
            pg = 1;

        if (!string.IsNullOrWhiteSpace(search))
            return $"{host}/search/?query={HttpUtility.UrlEncode(search.Trim())}&page={pg}";

        if (string.IsNullOrWhiteSpace(c))
            return host + HomePath(sort) + "?page=" + pg;

        // Category/tag/pornstar: hau to sort (/views/, /time/...) da do la NO-OP
        // (tra ve y nguyen thu tu mac dinh) -> khong bao gio ghep sort vao path.
        return host + "/" + c.Trim().Trim('/') + "/?page=" + pg;
    }

    public static string WatchUrl(string host, string videoId)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        return $"{host.TrimEnd('/')}/watch/{videoId}/";
    }

    public static string CategoriesUrl(string host)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        return host.TrimEnd('/') + "/categories/";
    }

    // ===== LIST =====
    // Card: <article class="video-box ..." data-video-id="..." aria-label="<title>">
    //   poster   <img ... data-src="https://fi1-ph.ypncdn.com/...jpg">
    //   preview  data-mediabook="https://ev-ph.ypncdn.com/..._fb.mp4?..."
    //   time     <div class="video-duration ..."><span> 09:02 </span>
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var items = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return items;

        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html, "<article class=\"video-box[\\s\\S]*?</article>"))
        {
            string b = m.Value;

            var idm = Regex.Match(b, "data-video-id=\"(\\d+)\"");
            if (!idm.Success)
                continue;

            string id = idm.Groups[1].Value;
            if (!seen.Add(id))
                continue;

            string name = null;
            var tm = Regex.Match(b, "aria-label=\"([^\"]+)\"");
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value);
            else
            {
                var t2 = Regex.Match(b, "<img[^>]*alt=\"([^\"]+)\"");
                if (t2.Success)
                    name = HttpUtility.HtmlDecode(t2.Groups[1].Value);
            }

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var pm = Regex.Match(b, "data-(?:src|poster)=\"(https?://[^\"]+)\"");
            string picture = pm.Success ? HttpUtility.HtmlDecode(pm.Groups[1].Value) : null;

            string dur = "";
            var dm = Regex.Match(b, "class=\"video-duration[^\"]*\"[\\s\\S]{0,300}?<span[^>]*>\\s*([\\d:]{4,8})");
            if (dm.Success)
                dur = dm.Groups[1].Value;

            string href = "/watch/" + id + "/";

            var item = new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name.Trim(),
                picture = picture,
                time = dur,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "youporn",
                    href = href,
                    image = picture
                }
            };

            var vm = Regex.Match(b, "data-mediabook=\"([^\"]+)\"");
            if (vm.Success)
                item.preview = HttpUtility.HtmlDecode(vm.Groups[1].Value);

            items.Add(item);
        }

        return items;
    }

    // Trang phan trang chi hien 4 so + mui ten Next -> lay so lon nhat + 1
    // (moi trang luon con it nhat 1 trang ke tiep; khong bi ket o trang 4)
    public static int TotalPages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 1;

        int max = 0;
        foreach (Match m in Regex.Matches(html, "data-page-number=\"(\\d+)\""))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max)
                max = n;
        }

        return max > 0 ? max + 1 : 1;
    }

    // ===== TAXONOMY =====
    // /categories/ -> <a href="/category/<slug>/" class="categoryBox ...">
    //                   <p class="tm_category_title">Big Dick <span ...>
    public static List<(string name, string slug)> Categories(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html,
            "<a href=\"/category/([^\"/]+)/\"[\\s\\S]{0,700}?tm_category_title\">([^<]+)"))
        {
            string slug = m.Groups[1].Value;
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (slug.Length == 0 || name.Length == 0 || !seen.Add(slug))
                continue;

            res.Add((name, slug));
        }

        return res;
    }

    // ===== DETAIL / POPUP =====
    // Khoi class="videoTags" cua trang watch:
    //   <a class="... categories-tags ..." href="/category/asian/">Asian</a>
    //   <a class="... bubble-porntag ..." href="/porntags/butt/">butt</a>
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        int i = html.IndexOf("class=\"videoTags\"", StringComparison.Ordinal);
        if (i < 0)
            return res;

        string blk = html.Substring(i, Math.Min(15000, html.Length - i));
        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(blk, "href=\"/(category|porntags)/([^\"/]+)/\"[^>]*>([^<]{1,60})<"))
        {
            string kind = m.Groups[1].Value == "category" ? "category" : "tag";
            string slug = m.Groups[2].Value;
            string name = HttpUtility.HtmlDecode(m.Groups[3].Value).Trim();

            if (name.Length == 0 || !seen.Add(kind + ":" + slug))
                continue;

            res.Add((kind, slug, name));
        }

        return res;
    }

    public static string VideoTitle(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, "\"@type\":\\s*\"VideoObject\"[\\s\\S]{0,400}?\"name\":\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (m.Success)
            return JsonUnescape(m.Groups[1].Value);

        var h1 = Regex.Match(html, "<h1 class=\"videoTitle[^\"]*\"[^>]*>([\\s\\S]{0,200}?)</h1>");
        return h1.Success ? HttpUtility.HtmlDecode(Regex.Replace(h1.Groups[1].Value, "<[^>]+>", "")).Trim() : null;
    }

    // ===== STREAM =====
    // watch page: "mediaDefinitions":[{"format":"hls","videoUrl":"/media/hls/?s=<token>"},
    //                                  {"format":"mp4","videoUrl":"/media/mp4/?s=<token>"}]
    // -> goi tiep /media/<kind>/?s=... -> [{quality, videoUrl: <cdn mp4/m3u8 co hash>}]
    public static (string mp4, string hls) StreamMetas(string html)
    {
        if (string.IsNullOrEmpty(html))
            return (null, null);

        var m = Regex.Match(html, "\"mediaDefinitions\":(\\[[^\\]]*\\])");
        if (!m.Success)
            return (null, null);

        string json = m.Groups[1].Value.Replace("\\/", "/");
        string mp4 = null, hls = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return (null, null);

            foreach (var d in doc.RootElement.EnumerateArray())
            {
                string fmt = Str(d, "format");
                string url = Str(d, "videoUrl");
                if (string.IsNullOrEmpty(url))
                    continue;

                if (fmt == "mp4")
                    mp4 = url;
                else if (fmt == "hls")
                    hls = url;
            }
        }
        catch { }

        return (mp4, hls);
    }

    public static Dictionary<string, string> Qualities(string json)
    {
        var res = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(json))
            return res;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return res;

            foreach (var q in doc.RootElement.EnumerateArray())
            {
                string url = Str(q, "videoUrl");
                if (string.IsNullOrEmpty(url))
                    continue;

                string label = Str(q, "quality");
                string key = string.IsNullOrEmpty(label) ? "HD" : label + "p";

                int n = 2;
                while (res.ContainsKey(key))
                    key = (string.IsNullOrEmpty(label) ? "HD" : label + "p") + "-" + n++;

                res[key] = url;
            }
        }
        catch { }

        return res;
    }

    // ===== MENU =====
    public static List<MenuItem> MenuHead(string host, string search, string c, string sort)
    {
        host = host.TrimEnd('/');
        string root = host + "/youporn";

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
                q += (q.Length == 0 ? "" : "&") + "sort=" + HttpUtility.UrlEncode(s);

            return q.Length == 0 ? root : root + "?" + q;
        }

        var res = new List<MenuItem>(2)
        {
            new MenuItem()
            {
                title = "Tìm kiếm", search_on = "search_on", playlist_url = root
            }
        };

        // Sort CHI dung o trang chu (4 trang /browse/time/, /most_viewed/, /top_rated/,
        // /browse/duration/ deu khac thu tu — da do). /search/ va category/tag deu la
        // NO-OP (da do: /category/bigtits/time/ tra dung id nhu trang goc) -> bo dong.
        if (string.IsNullOrWhiteSpace(search) && string.IsNullOrWhiteSpace(c))
        {
            var sub = new List<MenuItem>(Sorts.Length);
            foreach (var o in Sorts)
                sub.Add(new MenuItem(o.name, link(o.key)));

            res.Add(new MenuItem()
            {
                title = $"Sắp xếp: {SortLabel(sort)}",
                playlist_url = "submenu",
                submenu = sub
            });
        }

        return res;
    }

    public static List<MenuItem> Menu(string host, List<(string name, string slug)> categories)
    {
        host = host.TrimEnd('/');
        if (categories == null || categories.Count == 0)
            return new List<MenuItem>();

        var sub = new List<MenuItem>(categories.Count);
        foreach (var c in categories)
            sub.Add(new MenuItem(c.name, host + "/youporn?c=" + HttpUtility.UrlEncode("category/" + c.slug)));

        return new List<MenuItem>(1)
        {
            new MenuItem()
            {
                title = "Thể loại", playlist_url = "submenu", submenu = sub
            }
        };
    }

    static string JsonUnescape(string s)
    {
        try
        {
            using var doc = JsonDocument.Parse("\"" + s + "\"");
            return doc.RootElement.GetString();
        }
        catch
        {
            return HttpUtility.HtmlDecode(s);
        }
    }

    static string Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()?.Replace("\\/", "/"),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }
}
