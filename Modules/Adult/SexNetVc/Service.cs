using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace SexNetVc;

public static class NvcTo
{
    public static string SiteHost = "https://sexnetvc.com";

    public const string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // Player StreamQQ nhan `?d=<domain>` trong POST /videos/<id>/config.
    public static string Domain = "sexnetvc.com";

    // SSR thuan (vlxx engine), khong can Chrome. 24 phim / trang.
    //   /                          ?page=N        (417 trang)
    //   /the-loai/<slug>           ?page=N        (vd khong-che: 80 trang)
    //   /tag/<slug>                ?page=N        (vd xnxx: 299 trang)
    //   /tim-kiem/<q>              ?page=N        (form GET /tim-kiem?k= chi tra trang 1
    //                                              -> PHAI dung path da do)
    //   /actresses                 ?page=N        thu muc dien vien (81 trang)
    // KHONG co sort: xem ghi chu trong Menu().
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (pg < 1)
            pg = 1;

        string u;

        if (!string.IsNullOrWhiteSpace(search))
        {
            // EscapeDataString -> %20 (site hieu "jav%20hd"); UrlEncode tra "+" chua do.
            u = host + "/tim-kiem/" + System.Uri.EscapeDataString(search.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string p = c.Trim();
            u = p.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? p
                : host + "/" + p.Trim('/');
        }
        else
        {
            u = host + "/";
        }

        if (pg > 1)
            u += (u.Contains('?') ? "&" : "?") + "page=" + pg;

        return u;
    }

    // ---- card ----------------------------------------------------------
    //
    //  <div class="video-item">
    //      <a  href="https://sexnetvc.com/phim-sex/<slug>"
    //      class="video-item__thumb"
    //       title="TIEU DE">
    //          <div class="video-item__ribbon-wrapper"><span class="ribbon">Không Che</span></div>
    //          <img  src="https://sexnetvc.com/resize/800/..." alt="TIEU DE" />
    //      </a>
    //      <div class="video-item__title"><a ... title="TIEU DE">TIEU DE</a></div>
    //  </div>
    //
    // Thu site IN tren card: tieu de, anh, va ribbon (Khong Che / Uncensored
    // Leak / Vietsub / Trung Quoc ...). KHONG co thoi luong, khong co nhan
    // HD/4K (da do: 0 `duration`, 0 marker chat luong tren 24 card).
    // -> `name` = tieu de, `quality` = null, `time` = ribbon (join " · ").
    // Ribbon khong phai duration nhung day la slot banner cua card — bo di la
    // BO SOT thu site in ra. Cung convention voi SexVietDam (nhan the -> time).
    static readonly Regex ReThumb = new Regex(
        "<a\\b[^>]*class=\"video-item__thumb\"[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in ReThumb.Matches(html))
        {
            string href = Attr(m.Value, "href");
            if (string.IsNullOrEmpty(href) || href.IndexOf("/phim-sex/", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            href = Abs(href);
            if (!seen.Add(href))
                continue;

            string name = Attr(m.Value, "title");

            int end = html.IndexOf("</a>", m.Index, StringComparison.OrdinalIgnoreCase);
            string body = end > m.Index ? html.Substring(m.Index, end - m.Index) : "";

            if (string.IsNullOrEmpty(name))
            {
                var ma = Regex.Match(body, "\\balt\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (ma.Success)
                    name = ma.Groups[1].Value;
            }

            name = Clean(HttpUtility.HtmlDecode(name ?? ""));
            if (name.Length == 0)
                continue;

            string poster = "";
            var mi = Regex.Match(body, "(?<![:\\w-])src\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (mi.Success)
                poster = Abs(mi.Groups[1].Value);

            // Uu tien thoi luong; site nay KHONG in duration (do 24 card: 0
            // `duration`, 0 nhan HD/4K) -> lay MOT nhan ribbon (nhan dau) de
            // card con mot dong badge gon (khong noi 2 nhan lam badge tran).
            string time = "";
            var md = Regex.Match(body,
                "video-item__(?:duration|time)\"[^>]*>\\s*([^<]+)<", RegexOptions.IgnoreCase);
            if (md.Success)
                time = Clean(HttpUtility.HtmlDecode(md.Groups[1].Value));

            if (time.Length == 0)
            {
                var ml = Regex.Match(body,
                    "<span\\s+class=\"ribbon\"[^>]*>([^<]+)</span>", RegexOptions.IgnoreCase);
                if (ml.Success)
                    time = Clean(HttpUtility.HtmlDecode(ml.Groups[1].Value));
            }

            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = time.Length > 0 ? time : null,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "sexnetvc",
                    href = href,
                    image = poster
                }
            });
        }

        return list;
    }

    // /ajax/suggestions/<slug> -> {"data":[{title,url,thumbnail_file_url,...}]}
    public static List<PlaylistItem> Related(string route, string json)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrWhiteSpace(json))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in data.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;

                string url = Str(el, "url");
                string name = Clean(Str(el, "title"));

                if (string.IsNullOrEmpty(url) || name.Length == 0)
                    continue;

                url = Abs(url);
                if (!seen.Add(url))
                    continue;

                string poster = Abs(Str(el, "thumbnail_file_url"));

                list.Add(new PlaylistItem()
                {
                    video = route + "?uri=" + HttpUtility.UrlEncode(url),
                    name = name,
                    picture = poster,
                    json = true,
                    bookmark = new Bookmark()
                    {
                        site = "sexnetvc",
                        href = url,
                        image = poster
                    }
                });
            }
        }
        catch { }

        return list;
    }

    static string Str(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    // ---- chip popup ----------------------------------------------------
    //
    // Chi lay trong khoi "Thể loại:" / "Tags:" cua trang detail. Nav header
    // cung in `class="badge"` nhung la menu, khong phai the cua phim.
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        int start = html.LastIndexOf("Thể loại:", StringComparison.Ordinal);
        if (start < 0)
            return res;

        int end = html.IndexOf("video__suggestions", start, StringComparison.Ordinal);
        string seg = end > start ? html.Substring(start, end - start) : html.Substring(start);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(seg,
            "<a\\b[^>]*href=\"(?:https?://[^\"/]+)?/(?<kind>the-loai|tag)/(?<slug>[^\"?/#]+)\"[^>]*>(?<body>[\\s\\S]*?)</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups["slug"].Value.Trim();
            string name = Clean(HttpUtility.HtmlDecode(Regex.Replace(m.Groups["body"].Value, "<[^>]+>", " ")));

            if (slug.Length == 0 || name.Length == 0)
                continue;

            string kind = m.Groups["kind"].Value.Equals("tag", StringComparison.OrdinalIgnoreCase)
                ? "tag" : "cat";

            if (!seen.Add(kind + "/" + slug))
                continue;

            res.Add((kind, slug, name));
        }

        return res;
    }

    // ---- menu ----------------------------------------------------------
    //
    // /?view=the-loai -> danh muc day du (48 the loai, kem "N phim").
    public static List<(string name, string path)> Genres(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a\\s+href=\"(?:https?://[^\"/]+)?/the-loai/(?<slug>[^\"?#/]+)\"\\s+class=\"video-item__thumb\"[\\s\\S]{0,700}?<div class=\"video-item__title\">\\s*<a[^>]*>(?<name>[^<]{1,80})</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups["slug"].Value.Trim();
            string name = Clean(HttpUtility.HtmlDecode(m.Groups["name"].Value));

            if (slug.Length == 0 || name.Length == 0 || !seen.Add(slug))
                continue;

            res.Add((name, "the-loai/" + slug));
        }

        return res;
    }

    // Nav trang chu: <a href=".../tag/xnxx" class="badge"> <svg/> <span>Xnxx</span> </a>
    public static List<(string name, string path)> Tags(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a\\b[^>]*href=\"(?:https?://[^\"/]+)?/tag/(?<slug>[^\"?#/]+)\"[^>]*>(?<body>[\\s\\S]*?)</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups["slug"].Value.Trim();
            string name = "";

            var mn = Regex.Match(m.Groups["body"].Value, "<span>(?<name>[^<]{1,60})</span>",
                RegexOptions.IgnoreCase);

            if (mn.Success)
                name = Clean(HttpUtility.HtmlDecode(mn.Groups["name"].Value));

            if (slug.Length == 0 || name.Length == 0 || !seen.Add(slug))
                continue;

            res.Add((name, "tag/" + slug));
        }

        return res;
    }

    public static readonly IReadOnlyList<(string name, string path)> FallbackGenres =
        new (string, string)[]
        {
            ("Không Che",  "the-loai/khong-che"),
            ("Việt Nam",   "the-loai/viet-nam"),
            ("Vietsub",    "the-loai/vietsub"),
            ("Trung Quốc", "the-loai/trung-quoc"),
            ("Âu - Mỹ",    "the-loai/au-my"),
            ("JAV HD",     "the-loai/jav-hd"),
            ("Gái Xinh",   "the-loai/gai-xinh"),
            ("Hàn Quốc",   "the-loai/han-quoc"),
        };

    public static readonly IReadOnlyList<(string name, string path)> FallbackTags =
        new (string, string)[]
        {
            ("Xnxx",    "tag/xnxx"),
            ("Xvideos", "tag/xvideos"),
        };

    // DONG 2 "Sap xep" — BO (2026-11-08, do that):
    //   ?sort=views|likes|created_at|id|order=desc|-views deu la SAP XEP TANG
    //   cua CUNG mot tap (80 trang, khong doi) nhung trang 1 ra toan phim cu
    //   (2024) va `order=desc` / `-views` bi site nuot im lang -> khong co
    //   "xem nhieu / moi nhat" that. Site cung khong in UI sap xep nao.
    //   Dua dong 2 chet vao te hon la bo -> khong co sort.
    public static List<MenuItem> Menu(
        string host,
        IReadOnlyList<(string name, string path)> genres = null,
        IReadOnlyList<(string name, string path)> tags = null)
    {
        var root = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/sexnetvc"
            }
        };

        if (genres != null && genres.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var g in genres)
                sub.Add(new MenuItem(g.name, host + "/sexnetvc?c=" + g.path));
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = sub });
        }

        if (tags != null && tags.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var t in tags)
                sub.Add(new MenuItem(t.name, host + "/sexnetvc?c=" + t.path));
            root.Add(new MenuItem() { title = "Từ khoá", playlist_url = "submenu", submenu = sub });
        }

        return root;
    }

    // Tong so trang: pager in `?page=<n>` (trang home dung `?page=`, trang
    // the loai dung `?sort=..&amp;page=`).
    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 0;

        int max = 0;
        foreach (Match m in Regex.Matches(html, "(?:\\?|&amp;|&)page=(\\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max)
                max = n;
        }

        return max;
    }

    // ---- player --------------------------------------------------------
    //
    // Trang detail in 2 nut `set-player-source` (thu tu attribute KHONG on
    // dinh: `data-source` dung truoc `class`):
    //   Server 1  data-cdn-name="StreamQQ Plan VIP"       -> e.streamforester.name
    //   Server 3  data-cdn-name="StreamQQ Plan VIP Alias" -> vcast.name
    public static List<(string name, string src)> Servers(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        foreach (Match m in Regex.Matches(html, "<button\\b[^>]*set-player-source[^>]*>",
            RegexOptions.IgnoreCase))
        {
            string src = Attr(m.Value, "data-source");
            if (string.IsNullOrEmpty(src))
                continue;

            string name = Attr(m.Value, "data-cdn-name");
            if (string.IsNullOrEmpty(name))
                name = "Server " + (res.Count + 1);

            res.Add((name, src));
        }

        return res;
    }

    // vcast: window.videoData.sources[].file
    // wogplayer: POST /videos/<id>/config -> {"sources":[{"file":"..."}]}
    public static string FileUrl(string jsonOrHtml)
    {
        if (string.IsNullOrEmpty(jsonOrHtml))
            return null;

        var m = Regex.Match(jsonOrHtml, "\"file\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    public static string VideoId(string url)
    {
        var m = Regex.Match(url ?? "", "/videos/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups[1].Value;

        m = Regex.Match(url ?? "", "/play/v1/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string Abs(string u)
    {
        if (string.IsNullOrEmpty(u))
            return "";

        u = HttpUtility.HtmlDecode(u);

        if (u.StartsWith("//"))
            return "https:" + u;
        if (u.StartsWith("/"))
            return SiteHost + u;
        return u;
    }

    static string Attr(string tag, string key)
    {
        var m = Regex.Match(tag ?? "", key + "\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return Regex.Replace(s.Trim(), "\\s+", " ");
    }
}
