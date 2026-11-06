using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace VietSexBlog;

public static class VietSexBlogTo
{
    public static string SiteHost = "https://vietsexhd.blog";

    // Phai trung UA ma proxy cua Lampac ghi de (Http.UserAgent) — URL
    // googlevideo tra ve bi rang buoc voi UA luc mo trang embed.
    public static string ChromeUA = Shared.Services.Http.UserAgent;

    // Nav that cua site (div.main-menu, 26 muc) duoc parse tu chinh trang list
    // nen khong ton them request. Menu() doc lai de khong lech slug khi site doi.
    static volatile List<(string name, List<(string slug, string name)> items)> _nav;

    public static string Uri(string host, string search, string sort, string c, string t, int pg)
    {
        var url = new System.Text.StringBuilder();
        url.Append(SiteHost);

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("/?search=");
            url.Append(HttpUtility.UrlEncode(search));
            if (pg > 1)
            {
                url.Append("&page=");
                url.Append(pg);
            }
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append("/");
            url.Append(c);
            if (pg > 1)
            {
                url.Append("?page=");
                url.Append(pg);
            }
        }
        else
        {
            url.Append("/danh-sach/phim-moi");
            if (pg > 1)
            {
                url.Append("?page=");
                url.Append(pg);
            }
        }

        return url.ToString();
    }

    // Anh: site bo data-original (lazy-load) tu 2026-11, gio tra thang trong src
    // (va co the la data-src o vai block). Thu lan luot ca 3 thuoc tinh.
    static string Poster(string block)
    {
        foreach (string attr in new[] { "data-original", "data-src", "src" })
        {
            var m = Regex.Match(block, attr + "=\"([^\"]+)\"");
            if (!m.Success)
                continue;

            string u = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
            if (u.Length == 0 || u.StartsWith("data:"))
                continue;

            if (u.StartsWith("//"))
                return "https:" + u;

            return u.StartsWith("/") ? SiteHost + u : u;
        }

        return "";
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html, System.Func<Shared.Models.SISI.Base.PlaylistItem, Shared.Models.SISI.Base.PlaylistItem> onplaylist = null)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();

        if (_nav == null || _nav.Count == 0)
        {
            var nav = Taxonomies(html);
            if (nav.Count > 0)
                _nav = nav;
        }

        var blocks = Regex.Matches(html, "<a\\s+class=\"m-block movie-item\".*?</a>", RegexOptions.Singleline);
        foreach (Match block in blocks)
        {
            string b = block.Value;

            var hm = Regex.Match(b, "href=\"(https?://[^\"]+/phim/[A-Za-z0-9._%-]+)\"");
            var tm = Regex.Match(b, "title=\"([^\"]+)\"");
            if (!hm.Success || !tm.Success) continue;

            string href = hm.Groups[1].Value;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value);
            string poster = Poster(b);

            string label = "";
            var labelMatch = Regex.Match(b, "<div\\s+class=\"label\">([^<]+)</div>");
            if (labelMatch.Success)
                label = HttpUtility.HtmlDecode(labelMatch.Groups[1].Value).Trim();

            var pl = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "vietsexblog",
                    href = href,
                    image = poster
                }
            };

            if (!string.IsNullOrEmpty(label))
                pl.quality = label;

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }

    // ========== Info popup (long-press) ==========
    // Trang detail co 3 dong #extras: "Diễn viên" (thuong rong — site khong
    // co taxonomy dien vien), "Thể loại" (/the-loai/), "Từ khóa" (/tu-khoa/).
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        int start = html.IndexOf("id=\"extras\"", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return res;

        int end = html.IndexOf("header-list-index", start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            end = Math.Min(html.Length, start + 6000);
        string block = html[start..end];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match a in Regex.Matches(block,
            @"href=""[^""]*/(the-loai|tu-khoa)/([^""#?]+)""[^>]*>([\s\S]*?)</a>",
            RegexOptions.IgnoreCase))
        {
            string kind = a.Groups[1].Value.ToLowerInvariant();
            string slug = kind + "/" + a.Groups[2].Value.Trim();
            if (slug.Length <= kind.Length + 1 || !seen.Add(slug))
                continue;

            string name = CleanTag(a.Value);
            if (string.IsNullOrEmpty(name))
            {
                var tit = Regex.Match(a.Value, @"title=""([^""]+)""", RegexOptions.IgnoreCase);
                if (tit.Success)
                    name = HttpUtility.HtmlDecode(tit.Groups[1].Value).Trim();
            }
            if (string.IsNullOrEmpty(name))
                name = a.Groups[2].Value;

            res.Add((kind, a.Groups[2].Value.Trim(), name));
        }
        return res;
    }

    static string CleanTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return "";
        var m = Regex.Match(tag, @">([^<]*)</a>\s*$");
        if (!m.Success) return "";
        // Client tach subtitle bang ':' -> ten co dau ':' se bi cat.
        return HttpUtility.HtmlDecode(m.Groups[1].Value).Trim().Replace(':', '-');
    }

    // Khoi "Có thể bạn thích?" (10 card m-block movie-item) o cuoi trang detail.
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(
        string route, string html)
    {
        if (string.IsNullOrEmpty(html))
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        int i = html.IndexOf("Có thể bạn thích?", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            i = html.IndexOf("header-list-index", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<Shared.Models.SISI.Base.PlaylistItem>();

        return Playlist(route, html[i..]);
    }

    // Chi nhan URL thuoc vietsexhd.blog / x.vietsex.blog (chong SSRF).
    // Site doi domain lieu tuc (x.vietsex.blog -> vietsexhd.blog 2026-11-06).
    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = HttpUtility.HtmlDecode(value.Trim());
        int frag = value.IndexOf('#');
        if (frag >= 0) value = value[..frag];
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            value = SiteHost + "/" + value.Trim('/');
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return null;
        if (parsed.Host.Equals("vietsexhd.blog", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.Equals("x.vietsex.blog", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".vietsex.blog", StringComparison.OrdinalIgnoreCase))
            return value;
        return null;
    }

    public static Dictionary<string, string> StreamLinks(string html)
    {
        var links = new Dictionary<string, string>();

        var servers = Regex.Matches(html, "data-link=\"([^\"]+)\"\\s+data-type=\"([a-zA-Z0-9]+)\"");
        int n = 0;
        foreach (Match m in servers)
        {
            n++;
            string link = HttpUtility.HtmlDecode(m.Groups[1].Value);
            string type = m.Groups[2].Value;
            if (link.StartsWith("/"))
                link = SiteHost + link;
            if (!link.StartsWith("http"))
                continue;
            string label = servers.Count > 1 ? ("Server " + n + " " + type) : type;
            if (!links.ContainsKey(label))
                links.TryAdd(label, link);
        }

        return links;
    }

    public static bool IsBlogger(string url)
        => !string.IsNullOrEmpty(url)
        && (url.Contains("blogger.com/video.g") || url.Contains("blogger.com/video?token="));

    // Trang embed Blogger nhung san URL googlevideo (itag 18 = 360p,
    // itag 22 = 720p). Topsex dung cung cong thuc nay.
    public static Dictionary<string, string> ExtractMp4s(string embedHtml)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(embedHtml))
            return res;

        string html = embedHtml
            .Replace("\\u003d", "=")
            .Replace("\\u0026", "&")
            .Replace("&amp;", "&");

        int at = 0;
        while (true)
        {
            int idx = html.IndexOf("googlevideo.com/videoplayback", at, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                break;

            at = idx + 20;

            int start = html.LastIndexOf("https://", idx, StringComparison.Ordinal);
            if (start < 0)
                continue;

            int end = html.Length;
            foreach (char stop in new[] { '"', '\'', '<', ' ', '\\', '\n' })
            {
                int i = html.IndexOf(stop, idx);
                if (i > 0 && i < end)
                    end = i;
            }

            string url = html[start..end].TrimEnd('&', ';');
            url = Regex.Replace(url, "&quot;?$", "");
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;

            var im = Regex.Match(url, "[?&]itag=(\\d+)");
            string key = im.Success
                ? im.Groups[1].Value switch { "22" => "720p", "18" => "360p", var t => "itag " + t }
                : "MP4";

            if (!res.ContainsKey(key))
                res.Add(key, url);
        }

        return res;
    }

    // div.main-menu > nav.menu > ul > li > a  — 26 muc: "/" (Trang Chu),
    // "/the-loai/{slug}" (21), "/quoc-gia/{slug}" (4). Tach nhom theo tien to
    // path de khoi hardcode danh muc (site doi slug lien tuc).
    public static List<(string name, List<(string slug, string name)> items)> Taxonomies(string html)
    {
        var groups = new List<(string, List<(string, string)>)>();

        if (string.IsNullOrEmpty(html))
            return groups;

        int start = html.IndexOf("<div class=\"main-menu\">", StringComparison.Ordinal);
        if (start < 0)
            return groups;

        int end = html.IndexOf("</nav>", start, StringComparison.Ordinal);
        string box = end > start ? html[start..end] : html[start..];

        var theloai = new List<(string, string)>();
        var quocgia = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(box,
            "<a href=\"(?<href>[^\"]+)\"[^>]*>\\s*(?<name>[^<]+?)\\s*</a>", RegexOptions.Singleline))
        {
            string href = m.Groups["href"].Value.Trim();
            string name = HttpUtility.HtmlDecode(m.Groups["name"].Value.Trim());

            if (name.Length == 0)
                continue;

            bool isCat = href.StartsWith("/the-loai/");
            if (!isCat && !href.StartsWith("/quoc-gia/"))
                continue;

            // Giu nguyen tien to path: "the-loai/jav" va "quoc-gia/chau-au" la
            // 2 trang khac nhau, c= chi nhan dung chuoi nay.
            string slug = href.Trim('/');
            if (slug.Length == 0 || !seen.Add(slug))
                continue;

            // Client tach subtitle bang dau ':' — ten co dau se bi cat.
            var item = (slug, name.Replace(':', '-'));
            if (isCat) theloai.Add(item); else quocgia.Add(item);
        }

        if (theloai.Count > 0)
            groups.Add(("Thể loại", theloai));

        if (quocgia.Count > 0)
            groups.Add(("Quốc gia", quocgia));

        return groups;
    }

    // Nav lay tu trang list that bai (mang loi) — dung slug dang song tren site.
    public static readonly List<(string name, List<(string slug, string name)> items)> FallbackGroups =
        new List<(string, List<(string, string)>)>
        {
            ("Thể loại", new List<(string, string)>()
            {
                ("the-loai/phim-sex-viet-nam", "Phim sex Việt Nam"),
                ("the-loai/jav", "JAV"),
                ("the-loai/phim-sex-jav", "Phim Sex JAV"),
                ("the-loai/jav-hd", "Jav HD"),
                ("the-loai/vietsub", "Phim sex Vietsub"),
                ("the-loai/phim-sex-viet-sub", "Phim Sex Việt Sub"),
                ("the-loai/khong-che", "AV Không che"),
                ("the-loai/phim-sex-khong-che", "Phim Sex Không Che"),
                ("the-loai/censored", "Censored"),
                ("the-loai/sex-hd", "Sex HD"),
                ("the-loai/phim-sex-hd", "Phim Sex HD"),
                ("the-loai/phim-sex-moi", "Phim Sex Mới"),
                ("the-loai/phim-sex-trung-quoc", "Phim Sex Trung Quốc"),
                ("the-loai/phim-sex-au-my", "Phim Sex Âu Mỹ"),
                ("the-loai/phim-sex-chau-au", "Phim Sex Châu Âu"),
                ("the-loai/chau-au", "Mỹ - Châu Âu"),
                ("the-loai/sex-chau-au", "Sex Châu Âu"),
                ("the-loai/phim-sex-bao-dam", "Phim Sex Bạo Dâm"),
                ("the-loai/phim-sex-hiep-dam", "Phim Sex Hiếp Dâm"),
                ("the-loai/phim-sex-loan-luan", "Phim Sex Loạn Luân"),
                ("the-loai/vlxx", "VLXX")
            }),
            ("Quốc gia", new List<(string, string)>()
            {
                ("quoc-gia/viet-nam", "Việt Nam"),
                ("quoc-gia/au-my", "Âu Mỹ"),
                ("quoc-gia/chau-au", "Châu Âu"),
                ("quoc-gia/han-quoc", "Hàn Quốc")
            })
        };

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        string url = host + "/vietsexblog";

        // Chi 1 muc flat o dong dau la "Tim kiem" (search_on); moi category
        // PHAI nam trong submenu group (2 tang) — muc flat category la loi
        // "menu mot tang" (MODULE-GUIDE §5.3). Nen "Phim moi" duoc gom vao
        // nhom "Danh sach" thay vi dua thang len dong dau.
        var menu = new List<Shared.Models.SISI.Base.MenuItem>(4)
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = url
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh sách",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new Shared.Models.SISI.Base.MenuItem(
                        "Mới nhất", url + "?c=danh-sach/phim-moi")
                }
            }
        };

        var groups = _nav;
        if (groups == null || groups.Count == 0)
            groups = FallbackGroups;

        foreach (var (name, items) in groups)
        {
            if (items == null || items.Count == 0)
                continue;

            var sub = new List<Shared.Models.SISI.Base.MenuItem>(items.Count);
            foreach (var (slug, title) in items)
            {
                if (string.IsNullOrEmpty(slug))
                    continue;

                sub.Add(new Shared.Models.SISI.Base.MenuItem(
                    string.IsNullOrEmpty(title) ? slug : title,
                    url + "?c=" + slug));
            }

            if (sub.Count == 0)
                continue;

            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = name,
                playlist_url = "submenu",
                submenu = sub
            });
        }

        return menu;
    }
}
