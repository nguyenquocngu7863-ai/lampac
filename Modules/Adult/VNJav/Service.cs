using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace VNJav;

public static class VNJavTo
{
    public static string SiteHost = "https://vnjav.net";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // ---- Uri: home / category / tag / actor / search + phân trang + sort ----
    // c = "danh-muc/<slug>" | "the/<slug>" | "dien-vien/<slug>" | "videos"
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        string path;
        if (!string.IsNullOrWhiteSpace(search))
        {
            // search thật: /tim-kiem?q=<kw>  (/videos?q= bị site bỏ qua — đã đo)
            path = "/tim-kiem?q=" + HttpUtility.UrlEncode(search.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string cc = c.Trim('/');
            path = "/" + (cc.StartsWith("http") ? "" : cc);
        }
        else
        {
            path = "/videos";
        }

        var q = new List<string>();
        if (pg > 1) q.Add("page=" + pg);
        if (!string.IsNullOrWhiteSpace(sort)) q.Add("sort=" + sort.Trim());

        return host + path + (q.Count == 0 ? "" : (path.Contains("?") ? "&" : "?") + string.Join("&", q));
    }

    // ---- Parse list SSR (Next.js) ----
    // Card: <a ... aria-label="<title>" ... href="/video/<slug>"> <img src="<poster>"> ...
    //       <span class="card-badge ...">Vietsub</span> ... <h3 ...><a ...>TITLE</a></h3>
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string route, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a[^>]*aria-label=\"([^\"]+)\"[^>]*href=\"(/video/[^\"]+)\"[^>]*>\\s*<img[^>]*src=\"([^\"]+)\"",
            RegexOptions.IgnoreCase))
        {
            string href = m.Groups[2].Value;
            if (!seen.Add(href))
                continue;

            string title = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(title))
                continue;
            if (title.Length > 150) title = title.Substring(0, 147) + "...";

            string poster = HttpUtility.HtmlDecode(m.Groups[3].Value.Trim());
            if (poster.StartsWith("//")) poster = "https:" + poster;
            else if (poster.StartsWith("/")) poster = SiteHost + poster;

            // 1 nhãn site in trên card (badge "Vietsub") -> slot time.
            // Site KHÔNG in duration/quality trên card list -> quality = null (không bịa).
            string badge = "";
            var bm = Regex.Match(html.Substring(Math.Max(0, m.Index - 64), Math.Min(1200, html.Length - Math.Max(0, m.Index - 64))),
                "card-badge[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            if (bm.Success) badge = HttpUtility.HtmlDecode(bm.Groups[1].Value.Trim());

            string detail = SiteHost + href;
            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                name = title,
                video = route + "?uri=" + HttpUtility.UrlEncode(detail),
                picture = poster,
                time = string.IsNullOrEmpty(badge) ? null : badge,
                quality = null,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "vnjav",
                    href = detail,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // ---- Detail RSC: sources[] -> (label, embedUrl). Chỉ lấy 2 server đã giải ----
    // Bản server-side (Http.BaseGet) nhúng RSC ESCAPE 2 LỚP: \"label\":\"Server 1\",\"embedUrl\":\"...\"
    // Bản Chrome dump không escape: "label":"Server 1","embedUrl":"..."
    // -> bắt cả 2 dạng bằng 1 regex có optional backslash trước mỗi nháy.
    public static (string loadvid, string streamtape) Servers(string html)
    {
        string loadvid = null, streamtape = null;
        if (string.IsNullOrEmpty(html)) return (null, null);

        foreach (Match m in Regex.Matches(html,
            @"\\?""label\\?"":\\?""[^""\\]+\\?"",\\?""embedUrl\\?"":\\?""([^""\\]+)\\?""",
            RegexOptions.IgnoreCase))
        {
            string u = Regex.Unescape(m.Groups[1].Value);
            if (u.Contains("loadvid.com") && loadvid == null) loadvid = u;
            else if (u.Contains("streamtape.") && streamtape == null) streamtape = u;
        }
        return (loadvid, streamtape);
    }

    // ---- loadvid embed: lấy csrf-token + videoToken + videoHash ----
    public static (string csrf, string token, string hash) LoadVidConfig(string html)
    {
        if (string.IsNullOrEmpty(html)) return (null, null, null);
        var csrf = Regex.Match(html, "<meta name=\"csrf-token\" content=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        var tok = Regex.Match(html, "videoToken:\\s*'([^']+)'", RegexOptions.IgnoreCase);
        var hash = Regex.Match(html, "videoHash:\\s*'([^']+)'", RegexOptions.IgnoreCase);
        return (csrf.Success ? csrf.Groups[1].Value : null,
                tok.Success ? tok.Groups[1].Value : null,
                hash.Success ? hash.Groups[1].Value : null);
    }

    // ---- StreamTape F17c: bóc token ở NHÁNH SCRIPT (div #robotlink là token cũ chết) ----
    // document.getElementById('robotlink').innerHTML = '//stre'+ ('xcdamtape.com/get_video?...').substring(2).substring(1);
    public static string StreamTapeGetVideo(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        foreach (Match m in Regex.Matches(html,
            @"getElementById\(['""]robotlink['""]\)\.innerHTML\s*=\s*'([^']*)'\s*\+\s*\('([^']*)'\)((?:\s*\.substring\(\d+\)\s*)*)",
            RegexOptions.IgnoreCase))
        {
            string tail = m.Groups[2].Value;
            foreach (Match s in Regex.Matches(m.Groups[3].Value ?? "", @"substring\((\d+)\)"))
                if (int.TryParse(s.Groups[1].Value, out int n) && n >= 0 && n <= tail.Length)
                    tail = tail.Substring(n);

            string raw = m.Groups[1].Value + tail;   // "//streamtape.com/get_video?...&token=-"
            if (!raw.Contains("get_video")) continue;
            if (raw.StartsWith("//")) return "https:" + raw;
            if (raw.StartsWith("/")) return "https://streamtape.com" + raw;
            return raw;
        }
        return null;
    }

    // ---- taxonomy động: parse <a href="/the/<slug>">TÊN</a> / /dien-vien/... ----
    public static List<(string name, string slug)> Taxonomy(string html, string prefix)
    {
        var list = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(html)) return list;

        foreach (Match m in Regex.Matches(html,
            "href=\"/" + Regex.Escape(prefix) + "/([^\"?/]+)\"[^>]*>\\s*(?:<[^>]+>\\s*)*<span[^>]*>([^<]+)</span>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name) || !seen.Add(slug))
                continue;
            list.Add((name, slug));
        }
        return list;
    }

    // ---- sort thật (đo: ?sort=views đổi first-item, latest = mặc định) ----
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất", ""),
        ("Xem nhiều", "views"),
        ("Bình luận nhiều", "comments"),
        ("Được thích nhiều", "likes"),
        ("Cũ nhất", "oldest"),
    };

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in Sorts) if (s == sort) return s;
        return null;
    }

    public static string SortLabel(string sort) => string.IsNullOrEmpty(sort) ? "mới nhất"
        : sort == "views" ? "xem nhiều"
        : sort == "comments" ? "bình luận nhiều"
        : sort == "likes" ? "được thích nhiều"
        : sort == "oldest" ? "cũ nhất" : sort;

    // ---- menu head: Tìm kiếm + Sắp xếp (giữ search/c) ----
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/vnjav";
        string Link(string s)
        {
            var q = new List<string>();
            if (!string.IsNullOrWhiteSpace(search)) q.Add("search=" + HttpUtility.UrlEncode(search));
            else if (!string.IsNullOrWhiteSpace(c)) q.Add("c=" + HttpUtility.UrlEncode(c));
            if (!string.IsNullOrEmpty(s)) q.Add("sort=" + s);
            return q.Count == 0 ? root : root + "?" + string.Join("&", q);
        }

        var res = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm", search_on = "search_on", playlist_url = root
            }
        };

        var sub = new List<Shared.Models.SISI.Base.MenuItem>();
        foreach (var (name, s) in Sorts) sub.Add(new Shared.Models.SISI.Base.MenuItem(name, Link(s)));
        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });
        return res;
    }

    // ---- menu base: danh mục + thẻ + diễn viên (động) ----
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host,
        List<(string name, string slug)> tags = null,
        List<(string name, string slug)> actors = null)
    {
        host = host.TrimEnd('/');
        Func<string, string> cat = slug => host + "/vnjav?c=" + HttpUtility.UrlEncode("danh-muc/" + slug);

        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh mục", playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Vietsub", cat("vietsub")),
                    new("Không che", cat("khong-che")),
                    new("LTX Decensored", cat("ltx-decensored")),
                }
            }
        };

        if (tags != null && tags.Count > 0)
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thẻ", playlist_url = "submenu",
                submenu = tags.Select(t => new Shared.Models.SISI.Base.MenuItem(
                    t.name, host + "/vnjav?c=" + HttpUtility.UrlEncode("the/" + t.slug))).ToList()
            });

        if (actors != null && actors.Count > 0)
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Diễn viên", playlist_url = "submenu",
                submenu = actors.Select(a => new Shared.Models.SISI.Base.MenuItem(
                    a.name, host + "/vnjav?c=" + HttpUtility.UrlEncode("dien-vien/" + a.slug))).ToList()
            });

        return menu;
    }

    // ---- POPUP /genres: chip lấy ĐỘNG từ 1 lần fetch detail ----
    // Detail SSR in chip dạng: <a ... href="/the/ban-trong">Bắn trong</a>, /dien-vien/..., /danh-muc/...
    // RSC nhúng bản escape: \"href\":\"/the/ban-trong\" ... \"children\":\"Bắn trong\"
    // -> bắt cả 2 dạng, lấy slug + TÊN HIỂN THỊ có sẵn (không hardcode, không map taxonomy).
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // kind khớp đúng prefix Uri(): cat -> danh-muc, tag -> the, actor -> dien-vien
        foreach (var (kind, prefix) in new[] {
            ("cat", "danh-muc"), ("tag", "the"), ("actor", "dien-vien") })
        {
            foreach (Match m in Regex.Matches(html,
                // optional backslash trước mỗi nháy (bản RSC escape 2 lớp)
                @"\\?""href\\?"":\\?""(?:/|https?://vnjav\.net/)" + Regex.Escape(prefix) + @"/([^""\\/?#]+)\\?""[^}]*?\\?""children\\?"":\\?""([^""\\]+)\\?""|href=""/" + Regex.Escape(prefix) + @"/([^""?/#]+)""[^>]*>([^<]+)<",
                RegexOptions.IgnoreCase))
            {
                string slug = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value;
                string name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Value;
                if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name)) continue;
                name = HttpUtility.HtmlDecode(name.Trim());
                if (name.Length > 40) name = name.Substring(0, 37) + "...";
                if (!seen.Add(kind + "/" + slug)) continue;
                list.Add((kind, slug.Trim(), name));
            }
        }
        return list;
    }

    // ---- POPUP: khối "Video liên quan" ở <aside> (5 card, KHÔNG gồm phim đang xem) ----
    // Tái dùng Playlist() vì card related có đúng format aria-label + href + <img src>.
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(string route, string html, string currentUri = null)
    {
        if (string.IsNullOrEmpty(html))
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        int i = html.IndexOf("Video liên quan", StringComparison.OrdinalIgnoreCase);
        if (i < 0) i = html.IndexOf("related", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        string block = html.Substring(i, Math.Min(60000, html.Length - i));
        var list = Playlist(route, block);

        // Bỏ phim đang xem nếu site lỡ nhét nó vào khối gợi ý.
        if (!string.IsNullOrEmpty(currentUri))
        {
            string slug = currentUri.TrimEnd('/');
            int k = slug.LastIndexOf('/');
            if (k >= 0) slug = slug.Substring(k + 1);
            if (!string.IsNullOrEmpty(slug))
                list = list.Where(p => !(p.video ?? "").Contains("/" + slug + "?") &&
                                       !(p.video ?? "").Contains(slug + "%2f")).ToList();
        }
        return list;
    }
}
