using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Vietmup;

public static class VietmupTo
{
    public static string SiteHost = "https://vietmup.lol";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36";

    // Ngữ cảnh list của site (đo 2026-11-25):
    //   ""/cat/tag      -> / | /category/<slug> | /tag/<slug>   (param sort)
    //   films           -> /films                                (param sort, ?page=N)
    //   drama           -> /drama                                (param tab: latest|hot|finished)
    //   drama/<slug>    -> không list, là trang series (Controller tự cắt tập)
    //   search          -> /search?q=<kw>                         (1 trang, không sort)
    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        if (!string.IsNullOrWhiteSpace(search))
            return host + "/search?q=" + HttpUtility.UrlEncode(search.Trim());

        string url;
        string sortParam = "sort";

        if (string.IsNullOrEmpty(c))
        {
            url = host + "/";
        }
        else if (c == "films")
        {
            url = host + "/films";
        }
        else if (c == "drama")
        {
            url = host + "/drama";
            sortParam = "tab";
        }
        else if (c.StartsWith("drama/", StringComparison.Ordinal))
        {
            return host + "/" + c.Trim('/');
        }
        else
        {
            int slash = c.IndexOf('/');
            if (slash > 0)
            {
                string kind = c.Substring(0, slash).Trim();
                string slug = c.Substring(slash + 1).Trim('/');
                url = host + "/" + (kind == "tag" ? "tag/" : "category/") + slug;
            }
            else
            {
                url = c.StartsWith("http") ? c : host + "/category/" + c.Trim('/');
            }
        }

        var qs = new List<string>(2);
        if (pg > 1)
            qs.Add("page=" + pg);
        if (!string.IsNullOrWhiteSpace(sort))
            qs.Add(sortParam + "=" + sort.Trim());

        return qs.Count == 0 ? url : url + "?" + string.Join("&", qs);
    }

    // Card list: home (/watch), phim (/films), phim ngắn (/drama) — cùng component
    //   <div class="group..."><a href="/(watch|films|drama)/<slug>">...<img src="cdn..." alt="TÊN">
    // Phim ngắn dùng <h3> thay alt rỗng, nên đọc alt trước rồi mới tới h3.
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();
        foreach (string raw in html.Split(new[] { "<div class=\"group" }, StringSplitOptions.None).Skip(1))
        {
            string b = raw.Length > 4000 ? raw.Substring(0, 4000) : raw;

            var hm = Regex.Match(b, "<a href=\"(/(?:watch|films|drama)/[^\"]+)\"");
            if (!hm.Success)
                continue;
            string href = hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            var tm = Regex.Match(b, "alt=\"([^\"]{4,300})\"");
            if (!tm.Success || string.IsNullOrWhiteSpace(HttpUtility.HtmlDecode(tm.Groups[1].Value)))
                tm = Regex.Match(b, "<h3[^>]*>([\\s\\S]*?)</h3>");
            if (!tm.Success)
                continue;

            string name = HttpUtility.HtmlDecode(
                Regex.Replace(tm.Groups[1].Value, "<[^>]+>", "")).Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, "src=\"(https://cdn\\d*\\.vietmup\\.lol/[^\"]+)\"");
            if (im.Success)
                poster = im.Groups[1].Value;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "vietmup",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // ===== SERVER của trang xem (watch / films) =====
    //   <button data-server-index data-server-type data-server-url>NHÃN</button>
    // Site dùng 4 loại (ghi trong app.js): m3u8 · hls · direct · abyss (+ embed).
    //   m3u8/hls -> /api/stream?t=... (HLS, đã ký, phải qua proxy + referer site)
    //   direct   -> URL file mp4
    //   abyss    -> iframe player.abyssplayer.com/<slug>  (Abyss.cs resolve)
    //   embed    -> iframe bên thứ ba, không resolve được -> bỏ
    public static List<(string label, string kind, string url)> Servers(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<button[^>]*data-server-index=\"[^\"]*\"[^>]*>([^<]*)</button>",
            RegexOptions.IgnoreCase))
        {
            string tag = m.Value;
            string kind = Attr(tag, "data-server-type");
            string url = HttpUtility.HtmlDecode(Attr(tag, "data-server-url"));

            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(url) || url.Contains("${"))
                continue;

            kind = kind.ToLowerInvariant();

            if (kind != "abyss" && url.StartsWith("/", StringComparison.Ordinal))
                url = SiteHost + url;

            if (!seen.Add(kind + "|" + url))
                continue;

            string label = HttpUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim();
            if (string.IsNullOrEmpty(label))
                label = kind == "abyss" ? "Abyss" : "Server " + (list.Count + 1);

            list.Add((label, kind, url));
        }

        return list;
    }

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, name + "=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // Trang tập phim ngắn: <div data-drama-player data-manifest-url="/api/stream?t=..." data-direct-url="">
    public static (string manifest, string direct) DramaManifest(string html)
    {
        if (string.IsNullOrEmpty(html))
            return (null, null);

        string manifest = null, direct = null;

        var mm = Regex.Match(html, "data-manifest-url=\"([^\"]+)\"");
        if (mm.Success && !mm.Groups[1].Value.Contains("${"))
            manifest = HttpUtility.HtmlDecode(mm.Groups[1].Value);

        var dm = Regex.Match(html, "data-direct-url=\"([^\"]+)\"");
        if (dm.Success && !dm.Groups[1].Value.Contains("${"))
            direct = HttpUtility.HtmlDecode(dm.Groups[1].Value);

        if (!string.IsNullOrEmpty(manifest) && manifest.StartsWith("/", StringComparison.Ordinal))
            manifest = SiteHost + manifest;

        if (!string.IsNullOrEmpty(direct) && direct.StartsWith("/", StringComparison.Ordinal))
            direct = SiteHost + direct;

        return (manifest, direct);
    }

    // Trang series /drama/<slug>: danh sách tập <a href="/drama/<slug>/ep-N">
    // Trả (số tập, poster) — Controller dựng item "Tập N".
    public static (List<int> eps, string poster) DramaEpisodes(string html)
    {
        var eps = new List<int>();
        if (string.IsNullOrEmpty(html))
            return (eps, null);

        var seen = new HashSet<int>();
        foreach (Match m in Regex.Matches(html, "/drama/[^\"/]+/ep-(\\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > 0 && seen.Add(n))
                eps.Add(n);
        }
        eps.Sort();

        string poster = null;
        var pm = Regex.Match(html, "<meta property=\"og:image\" content=\"([^\"]+)\"");

        if (!pm.Success)
            pm = Regex.Match(html, "<img src=\"(https://cdn[^\"]+/cover\\.(?:jpg|jpeg|png|webp))\"");

        if (pm.Success)
            poster = HttpUtility.HtmlDecode(pm.Groups[1].Value);

        return (eps, poster);
    }

    // Tên series phim ngắn: <h1>…</h1>, fallback og:title (bỏ hậu tố " | VietMup")
    public static string SeriesName(string html, string slug)
    {
        if (!string.IsNullOrEmpty(html))
        {
            var m = Regex.Match(html, "<h1[^>]*>([\\s\\S]{0,200}?)</h1>");
            if (m.Success)
            {
                string t = HttpUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim();
                if (!string.IsNullOrEmpty(t))
                    return t;
            }

            m = Regex.Match(html, "<meta property=\"og:title\" content=\"([^\"]+)\"");
            if (m.Success)
            {
                string t = HttpUtility.HtmlDecode(m.Groups[1].Value).Split('|')[0].Trim();
                t = Regex.Replace(t, @"\s*-\s*Phim Ngắn.*$", "", RegexOptions.IgnoreCase).Trim();
                if (!string.IsNullOrEmpty(t))
                    return t;
            }
        }

        return PrettyName(null, slug);
    }

    // Category: /category là mục lục đầy đủ (59), home chỉ 12.
    //   - home:   <a href="/category/<slug>" ...>#TÊN</a>
    //   - /category: card ảnh, tên nằm trong <span class="…text-white…">TÊN</span>
    public static List<(string name, string slug)> Categories(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html, "href=\"/category/([^\"/]+)\"[\\s\\S]{0,4000}?</a>"))
        {
            string slug = m.Groups[1].Value.Trim('/');
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            var nm = Regex.Match(m.Value,
                "<span[^>]*class=\"[^\"]*text-white[^\"]*\"[^>]*>([^<]{1,60})</span>");

            if (!nm.Success)
                nm = Regex.Match(m.Value, ">([^<>]{2,60})<");

            string name = nm.Success ? HttpUtility.HtmlDecode(nm.Groups[1].Value).Trim() : slug;
            name = name.TrimStart('#').Trim();

            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((PrettyName(name, slug), slug));
        }

        return list;
    }

    // Danh sách đầy đủ ở trang /tag (115+ tag), dùng cho menu "Thẻ".
    //   <a href="/tag/<slug>" class="...">#tên</a>
    public static List<(string name, string slug)> Tags(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, "href=\"/tag/([^\"/]+)\"[^>]*>([^<]*)<"))
        {
            string slug = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups[2].Value).Trim();
            name = name.TrimStart('#').Trim().Trim('\'', '"', '`').Trim();
            name = Regex.Replace(name, @"\s+", " ");
            if (string.IsNullOrEmpty(name))
                name = slug;

            list.Add((PrettyName(name, slug), slug));
        }

        return list;
    }

    // ===== FOOTER: "Mọi người đang tìm" + "Tags gợi ý" =====
    // Hai khối nằm trong footer mọi trang (đo 2026-11-25), KHÔNG có trong taxonomy/menu chính:
    //   <h3>Mọi người đang tìm</h3><a href="/search?q=<kw>">TỪ KHOÁ</a>   (15)
    //   <h3>Tags gợi ý</h3><a href="/tag/<slug>">tên</a>                  (15)
    static string FooterBlock(string html, string marker, int max = 8000)
    {
        int i = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return null;

        string region = html.Substring(i, Math.Min(max, html.Length - i));
        int end = region.IndexOf("</div>", StringComparison.OrdinalIgnoreCase);
        return end > 0 ? region.Substring(0, end) : region;
    }

    public static List<string> SearchTrends(string html)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(html))
            return list;

        string block = FooterBlock(html, "Mọi người đang tìm");
        if (block == null)
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(block, "href=\"/search\\?q=[^\"]*\"[^>]*>([^<]+)</a>"))
        {
            string kw = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
            if (kw.Length == 0 || !seen.Add(kw))
                continue;
            list.Add(kw);
        }

        return list;
    }

    public static List<(string name, string slug)> SuggestedTags(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        string block = FooterBlock(html, "Tags gợi ý");
        if (block == null)
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(block, "href=\"/tag/([^\"/]+)\"[^>]*>([^<]*)</a>"))
        {
            string slug = m.Groups[1].Value.Trim();
            if (slug.Length == 0 || !seen.Add(slug))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups[2].Value).Trim().TrimStart('#').Trim();
            list.Add((string.IsNullOrEmpty(name) ? (slug.Replace('-', ' ')) : name, slug));
        }

        return list;
    }

    // ===== CHIP của video (popup) =====
    // Trang detail có 1 khối duy nhất:
    //   <div class="flex flex-wrap gap-2 pt-1">
    //     <a href="/category/<slug>">PHIM SEX SINH VIÊN</a>
    //     <a href="/tag/<slug>">#khong-bao</a>
    //   </div>
    // Khối "Thể loại phổ biến" ở footer cũng có /category/ nên phải cắt theo marker
    // "Có thể bạn cũng thích" (hết phần thân video) và ưu tiên đúng container chip.
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        int cut = html.IndexOf("Có thể bạn cũng thích", StringComparison.OrdinalIgnoreCase);
        string region = cut > 0 ? html.Substring(0, cut) : html;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Scan(string block)
        {
            foreach (Match m in Regex.Matches(block, "<a href=\"/(category|tag)/([^\"/]+)\"[^>]*>([^<]*)</a>"))
            {
                string kind = m.Groups[1].Value == "tag" ? "tag" : "cat";
                string slug = m.Groups[2].Value.Trim();
                if (string.IsNullOrEmpty(slug) || !seen.Add(kind + "/" + slug))
                    continue;

                string name = HttpUtility.HtmlDecode(m.Groups[3].Value).Trim().TrimStart('#').Trim();
                list.Add((kind, slug, PrettyName(name, slug)));
            }
        }

        var boxes = Regex.Matches(region, "<div class=\"[^\"]*flex flex-wrap gap-2(?![0-9.])[^\"]*\">([\\s\\S]*?)</div>");
        if (boxes.Count > 0)
        {
            foreach (Match b in boxes)
                Scan(b.Groups[1].Value);
        }

        if (list.Count == 0)
            Scan(region);

        return list;
    }

    // ===== VIDEO LIÊN QUAN (popup) =====
    // Khối "Có thể bạn cũng thích" (mobile + desktop), mỗi item:
    //   <a href="/watch/<slug>" data-related-slug="..."><img src="...jpg" alt="TÊN">...<h3>..</h3></a>
    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistRelated(string route, string html)
    {
        var list = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        int i = -1;
        foreach (string mark in new[] { "Có thể bạn cũng thích", "Phim liên quan" })
        {
            i = html.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
                break;
        }

        if (i < 0)
            return list;

        string region = html.Substring(i, Math.Min(120000, html.Length - i));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(region, "<a href=\"(/(?:watch|films|drama)/[^\"]+)\"[^>]*>([\\s\\S]*?)</a>"))
        {
            string href = m.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            string body = m.Groups[2].Value;

            var tm = Regex.Match(body, "alt=\"([^\"]{4,300})\"");
            if (!tm.Success)
                tm = Regex.Match(body, "<h3[^>]*>([\\s\\S]*?)</h3>");
            if (!tm.Success)
                continue;

            string name = HttpUtility.HtmlDecode(Regex.Replace(tm.Groups[1].Value, "<[^>]+>", "")).Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(body, "src=\"(https://cdn[^\"]+\\.(?:jpg|jpeg|png|webp))\"");
            if (im.Success)
                poster = im.Groups[1].Value;

            list.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "vietmup",
                    href = href,
                    image = poster
                }
            });
        }

        return list;
    }

    // Tên đẹp: slug → chữ thường, tên ALL-CAPS → Title Case (giữ nguyên tên đã đẹp)
    public static string PrettyName(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = (slug ?? "").Replace('-', ' ').Trim();
        if (name.Length == 0)
            return name;

        // Viết tắt ngắn (ASMR, BDSM, LIVE) giữ nguyên; tên ALL-CAPS dài mới Title Case
        bool upper = name == name.ToUpperInvariant() && name.Any(char.IsLetter) && name.Length > 5;
        if (!upper)
            return name;

        string s = name.ToLowerInvariant();
        return Regex.Replace(s, @"(^|\s)(\p{L})", m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
    }

    // ===== MENU =====
    // Bộ sort của site theo TỪNG ngữ cảnh (đo 2026-11-25, xem skill lampac-sort):
    //   home     : Nổi bật(null) · Mới nhất(latest = "gần đây") · Thịnh hành(view_by_week) · Xem nhiều(view_count) · Yêu thích(like_count)
    //   category/tag: Nổi bật(score) · Thịnh hành · Xem nhiều · Yêu thích
    //   films    : Mới nhất(latest) · Thịnh hành · Xem nhiều · Đánh giá cao(rating) · Yêu thích
    //   drama    : Mới cập nhật(latest) · Đang hot(hot) · Trọn bộ(finished)   (param `tab`)
    //   search / drama/<slug>: site KHÔNG có sort -> bỏ hẳn dòng 2
    public static (string param, (string name, string key)[] opts) SortSet(string c, string search)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return (null, null);

        if (!string.IsNullOrEmpty(c) && c.StartsWith("drama/", StringComparison.Ordinal))
            return (null, null);

        if (c == "drama")
        {
            return ("tab", new (string, string)[]
            {
                ("Mới cập nhật", "latest"),
                ("Đang hot", "hot"),
                ("Trọn bộ", "finished"),
            });
        }

        if (c == "films")
        {
            return ("sort", new (string, string)[]
            {
                ("Mới nhất", "latest"),
                ("Thịnh hành", "view_by_week"),
                ("Xem nhiều", "view_count"),
                ("Đánh giá cao", "rating"),
                ("Yêu thích", "like_count"),
            });
        }

        if (!string.IsNullOrEmpty(c))
        {
            return ("sort", new (string, string)[]
            {
                ("Nổi bật", "score"),
                ("Thịnh hành", "view_by_week"),
                ("Xem nhiều", "view_count"),
                ("Yêu thích", "like_count"),
            });
        }

        return ("sort", new (string, string)[]
        {
            ("Nổi bật", ""),
            ("Mới nhất", "latest"),
            ("Thịnh hành", "view_by_week"),
            ("Xem nhiều", "view_count"),
            ("Yêu thích", "like_count"),
        });
    }

    // sort lạ / không thuộc ngữ cảnh -> về mặc định của ngữ cảnh đó
    public static string ClampSort(string c, string search, string sort)
    {
        var (_, opts) = SortSet(c, search);
        if (opts == null || string.IsNullOrEmpty(sort))
            return null;

        foreach (var o in opts)
            if (o.key == sort)
                return sort;

        return null;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/vietmup";

        string ctx()
        {
            if (!string.IsNullOrWhiteSpace(search))
                return "search=" + HttpUtility.UrlEncode(search);
            if (!string.IsNullOrWhiteSpace(c))
                return "c=" + HttpUtility.UrlEncode(c);
            return "";
        }

        var res = new List<Shared.Models.SISI.Base.MenuItem>(2)
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };

        var (param, opts) = SortSet(c, search);
        if (opts == null)
            return res;

        string label = opts[0].name;
        foreach (var o in opts)
        {
            if ((sort ?? "") == o.key)
            {
                label = o.name;
                break;
            }
        }

        var sub = new List<Shared.Models.SISI.Base.MenuItem>(opts.Length);
        foreach (var (name, key) in opts)
        {
            string qs = ctx();
            if (!string.IsNullOrEmpty(key))
                qs += (qs.Length == 0 ? "" : "&") + param + "=" + key;

            sub.Add(new Shared.Models.SISI.Base.MenuItem(name,
                qs.Length == 0 ? root : root + "?" + qs));
        }

        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {label.ToLowerInvariant()}",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host, List<(string name, string slug)> categories = null,
        List<(string name, string slug)> tags = null, List<string> trends = null)
    {
        var menu = new List<Shared.Models.SISI.Base.MenuItem>();

        if (trends != null && trends.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mọi người đang tìm",
                playlist_url = "submenu",
                submenu = trends.Select(kw => new Shared.Models.SISI.Base.MenuItem(
                    kw, host + "/vietmup?search=" + HttpUtility.UrlEncode(kw))).ToList()
            });
        }

        menu.Add(new Shared.Models.SISI.Base.MenuItem("Phim", host + "/vietmup?c=films"));
        menu.Add(new Shared.Models.SISI.Base.MenuItem("Phim ngắn", host + "/vietmup?c=drama"));

        if (categories != null && categories.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = categories.Select(cat =>
                    new Shared.Models.SISI.Base.MenuItem(
                        cat.name,
                        host + "/vietmup?c=cat/" + cat.slug
                    )).ToList()
            });
        }

        if (tags != null && tags.Count > 0)
        {
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thẻ",
                playlist_url = "submenu",
                submenu = tags.Select(t =>
                    new Shared.Models.SISI.Base.MenuItem(
                        t.name,
                        host + "/vietmup?c=tag/" + t.slug
                    )).ToList()
            });
        }

        return menu;
    }
}
