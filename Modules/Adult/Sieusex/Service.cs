using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Sieusex;

public static class SieusexTo
{
    // ===== DOMAIN XOAY (2026-10-26) =====
    // sieusex.net -> 302 sang biphim.net (path bi mat: sieusex.net/new/2/ -> biphim.net/
    // => dung domain cu se lam phan trang LAP trang 1). Domain hien tai: biphim.net.
    // Regex parse KHONG hardcode host (nav/card/search deu absolute) -> khi domain
    // xoay tiep chi can doi 1 dong SiteHost nay.
    public static string SiteHost = "https://biphim.net";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri (WordPress theme video3x) ==========
    // Da do 2026-10-26:
    //   - trang chu "/" = "/new/1/" (giong het), /new/2..N = phan trang phim moi
    //   - category/tag: /<slug>/ va /<slug>/page/N/
    //   - search THAT la /search/<q>/ (space -> gach ngang), CO phan trang
    //     /search/<q>/page/N/ (da do 2026-10-26 tren biphim.net: page/2,3 ok)
    //     LUU Y: slug phai BO DAU tieng Viet, site 404 neu slug co unicode
    //     (vd /search/ng%C6%A1i/ -> 404, /search/nguoi/ -> 30 item)
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = SearchSlug(search);
            return pg > 1 ? host + "/search/" + slug + "/page/" + pg + "/" : host + "/search/" + slug + "/";
        }
        string raw = (c ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(raw) || raw == "new")
        {
            if (pg > 1) return host + "/new/" + pg + "/";
            return host + "/new/1/";
        }
        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return pg > 1 ? raw.TrimEnd('/') + "/page/" + pg + "/" : raw.TrimEnd('/') + "/";
        // TRAILING SLASH BAT BUOC: thi eu "/" -> trang rong (da do 2026-10-26)
        string baseUrl = host + "/" + raw + "/";
        return pg > 1 ? baseUrl.TrimEnd('/') + "/page/" + pg + "/" : baseUrl;
    }

    // search cua site: "co giao" -> "co-giao". BO DAU tieng Viet (site 404
    // khi slug co unicode), giu ky tu co ban, cac ky tu khac de EscapeDataString.
    public static string SearchSlug(string search)
    {
        string q = RemoveDiacritics(search.Trim()).Replace(' ', '-');
        while (q.Contains("--")) q = q.Replace("--", "-");
        return System.Uri.EscapeDataString(q.Trim('-'));
    }

    // "ngợi" -> "nguoi" (FormD + bo NonSpacingMark)
    public static string RemoveDiacritics(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        string norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (char c in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ========== So trang ==========
    // 1 = chi 1 trang (khoa cuon de app khong keo trang rong)
    // N = so trang that, lay tu <div class="pagenavi"> -> /page/N hoac /new/N lon nhat
    // CHI doc block pagenavi (cat tai </div>) de khong lan vao link footer
    // (footer co /new/112/ -> se dem nham thanh so trang thuc cua search).
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 1;
        int i = html.IndexOf("class=\"pagenavi\"", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return 1;
        int end = html.IndexOf("</div>", i, StringComparison.OrdinalIgnoreCase);
        int len = end > i ? end - i : Math.Min(3000, html.Length - i);
        string slice = html.Substring(i, Math.Min(len, html.Length - i));
        int max = 1;
        foreach (Match m in Regex.Matches(slice, @"/(?:page|new)/(\d+)"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        return max;
    }

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim());
        int frag = value.IndexOf('#');
        if (frag >= 0) value = value[..frag];
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = SiteHost + "/" + value;
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        // KHONG whitelist host: site xoay domain (sieusex.net -> biphim.net), card
        // luon absolute tren domain hien tai -> giu nguyen URL cua site.
        if (parsed.Scheme != "http" && parsed.Scheme != "https") return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static string DetailUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            string u = NormalizePageUrl(uri);
            return (u != null && u.Contains(".html")) ? u : u ?? null;
        }
        string v = uri.Trim().Trim('/');
        if (string.IsNullOrEmpty(v)) return null;
        if (!v.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) v += ".html";
        return SiteHost + "/" + v;
    }

    // ========== Playlist: <div id="video-N" class="video-item"><a title=".." href="..html"><img class="video-image" src=".."/> ==========
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<div\b[^>]*\bid\s*=\s*""video-\d+""[^>]*\bclass\s*=\s*""[^""]*\bvideo-item\b[^""]*""[^>]*>\s*<a\b[^>]*\btitle\s*=\s*""([^""]*)""[^>]*\bhref\s*=\s*""([^""]+\.html[^""]*)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string pageUrl = NormalizePageUrl(m.Groups[2].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[1].Value));
            if (string.IsNullOrEmpty(name)) continue;
            string snippet = html.Substring(m.Index, Math.Min(2000, html.Length - m.Index));
            string poster = null;
            // lazyload: data-original truoc, src thuong sau
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-original\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "sieusex", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Taxonomy tu nav (primary-nav) ==========
    public static List<(string slug, string name)> NavCategories(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        int from = html.IndexOf("id=\"primary-nav\"", StringComparison.OrdinalIgnoreCase);
        if (from < 0) return res;
        int to = html.IndexOf("</ul>", from, StringComparison.OrdinalIgnoreCase);
        if (to < 0 || to - from > 30000) to = Math.Min(from + 30000, html.Length);
        string nav = html[from..to];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // host-agnostic: domain xoay (sieusex.net -> biphim.net)
        foreach (Match m in Regex.Matches(nav,
            @"<a\b[^>]*href\s*=\s*""(?:https?://[^""/]+)?/([a-z0-9\-]{3,})/?""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim().ToLowerInvariant();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " "));
            if (slug.Length < 3 || string.IsNullOrEmpty(name) || name.Length > 40) continue;
            if (slug == "new" || slug.Contains("page") || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    // ========== Top tim kiem (search-history) ==========
    // kind: "search" -> /search/<v>/ ; "tag" -> /tag/<v>/
    public static List<(string kind, string value, string label)> HotSearches(string html)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html)) return res;
        int from = html.IndexOf("search-history", StringComparison.OrdinalIgnoreCase);
        if (from < 0) return res;
        string slice = html.Substring(from, Math.Min(4000, html.Length - from));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(slice,
            @"<a\b[^>]*href\s*=\s*""(?:https?://[^""/]+)?/(search|tag)/([^/""]+)/?""[^>]*>([^<]+)<",
            RegexOptions.IgnoreCase))
        {
            string kind = m.Groups[1].Value.ToLowerInvariant();
            string value = m.Groups[2].Value.Trim();
            string label = Clean(HttpUtility.HtmlDecode(m.Groups[3].Value));
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(label) || !seen.Add(kind + ":" + value)) continue;
            res.Add((kind, value, label));
        }
        return res;
    }

    // ===== Menu (9g) =====
    // Mặc định mở module = list phim moi (c rong -> /new/1/). Khong sort (site
    // khong co sort). Menu: Tim kiem + Danh muc + Top tim kiem.
    public static readonly (string slug, string name)[] FallbackCats =
    {
        ("phim-sex-vietsub", "Sex Phụ Đề"),
        ("phim-sex-khong-che", "Sex Không Che"),
        ("phim-sex-loan-luan", "Sex Loạn Luân"),
        ("phim-sex-vung-trom", "Sex Vụng Trộm"),
        ("phim-sex-viet-nam", "Sex Việt Nam"),
        ("phim-sex-trung-quoc", "Sex Trung Quốc"),
        ("phim-sex-nhat-ban", "Sex Nhật Bản"),
        ("phim-sex-chau-au", "Sex Châu Âu"),
        ("phim-sex-lau-xanh", "Sex Lâu Xanh"),
        ("phim-sex-dit-nhau", "Phim Địt Nhau"),
        ("phim-sex-hoc-sinh", "Sex Học Sinh"),
        ("phim-sex-hiep-dam", "Sex Hiếp Dâm"),
        ("sextop1", "SexTop1"),
        ("vlxx", "VLXX"),
        ("xnxx", "XNXX"),
        ("jav-hd", "JAV"),
        ("phim-xxx", "Phim XXX"),
        ("phim-heo", "Phim Heo"),
    };

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats, List<(string kind, string value, string label)> hot)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/sieusex?c=" + HttpUtility.UrlEncode(c);
        var root = new List<MenuItem>(3);
        if (cats == null || cats.Count == 0)
            cats = FallbackCats.ToList();
        root.Add(new MenuItem()
        {
            title = "Danh mục",
            playlist_url = "submenu",
            submenu = cats.Select(x => new MenuItem(string.IsNullOrEmpty(x.name) ? x.slug : x.name, url(x.slug))).ToList()
        });
        if (hot != null && hot.Count > 0)
        {
            var sub = new List<MenuItem>(hot.Count);
            foreach (var (kind, value, label) in hot)
            {
                if (kind == "tag") sub.Add(new MenuItem(label, url("tag/" + value)));
                else sub.Add(new MenuItem(label, host + "/sieusex?search=" + HttpUtility.UrlEncode(value.Replace('-', ' '))));
            }
            root.Add(new MenuItem() { title = "Top tìm kiếm", playlist_url = "submenu", submenu = sub });
        }
        return root;
    }

    // ========== Detail: post-id + so server ==========
    public static string PostId(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"<div\b[^>]*\bid\s*=\s*""video""[^>]*\bpost-id\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(html, @"\bpost-id\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static int ServerCount(string html)
    {
        if (string.IsNullOrEmpty(html)) return 0;
        int max = 0;
        foreach (Match m in Regex.Matches(html, @"<li\b[^>]*\bid\s*=\s*""server(\d+)""", RegexOptions.IgnoreCase))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        return max;
    }

    // ========== admin-ajax player -> iframe embed ==========
    // POST /wp-admin/admin-ajax.php  body: player_server=<id>&server=<sv>&vast_seen=1&is_mobile=0
    // -> {"player": "... <iframe src=\"https://hidecdn.net/embed/<hash32>?cdn=tt3\"> ..."}
    public static string EmbedFromPlayer(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        // Chuoi JSON thô tu server chua cac escape: \/ \" \u0022 \u003C ... -> hoan bien ve thuan
        var raw = json.Replace("\\/", "/").Replace("\\\"", "\"")
            .Replace("\\u0022", "\"").Replace("\\u003C", "<").Replace("\\u003E", ">");
        var m = Regex.Match(raw,
            @"https?://[^""\s<>]+?/embed/[A-Za-z0-9]+(?:\?[^""\s<>]*)?",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string v = m.Groups[0].Value;
        return v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : null;
    }

    public static string CdnParam(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl)) return null;
        var m = Regex.Match(embedUrl, @"[?&]cdn=([A-Za-z0-9]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string EmbedHash(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl)) return null;
        var m = Regex.Match(embedUrl, @"/embed/([A-Za-z0-9]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string EmbedHost(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl)) return null;
        if (!System.Uri.TryCreate(embedUrl, UriKind.Absolute, out var u)) return null;
        return u.GetLeftPart(UriPartial.Authority);
    }

    // ========== hidecdn: playlist XOR 0x5A ==========
    // Trang embed nhung san serverPlDataMap = {"tt3":"<b64>",...}. Decode:
    // base64 -> XOR tung byte voi 0x5A -> m3u8 VOD 1 level, segment absolute
    // tren TikTok CDN. Fallback: GET /embed/<hash>/pl?c=<cdn> -> {"d":"<b64>"}.
    public static Dictionary<string, string> ServerPlMap(string html)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(html)) return res;
        var m = Regex.Match(html, @"serverPlDataMap\s*=\s*\{([^}]{10,200000})\}", RegexOptions.Singleline);
        if (!m.Success) return res;
        foreach (Match x in Regex.Matches(m.Groups[1].Value, @"""([A-Za-z0-9_\-]+)""\s*:\s*""([A-Za-z0-9+/=]{20,})"""))
            res[x.Groups[1].Value] = x.Groups[2].Value;
        return res;
    }

    public static string PlData(string html, string cdn)
    {
        var map = ServerPlMap(html);
        if (map.Count == 0) return null;
        if (!string.IsNullOrEmpty(cdn) && map.TryGetValue(cdn, out string v)) return v;
        foreach (string k in new[] { "tt3", "tt2", "tt", "tt1" })
            if (map.TryGetValue(k, out string v2)) return v2;
        return null;
    }

    public static string DecodePl(string b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return null;
        try
        {
            byte[] raw = Convert.FromBase64String(b64);
            for (int i = 0; i < raw.Length; i++) raw[i] ^= 0x5A;
            string s = Encoding.UTF8.GetString(raw);
            return s.Contains("#EXTM3U") ? s : null;
        }
        catch { return null; }
    }

    // /embed/<hash>/pl?c=<cdn> -> {"d":"<b64>"}
    public static string PlApiData(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var m = Regex.Match(json, @"""d""\s*:\s*""([A-Za-z0-9+/=]{20,})""");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ========== URL helper ==========
    public static string AbsoluteUrl(string value, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return value;
        if (value.StartsWith("/"))
        {
            string host = EmbedHost(baseUrl);
            return string.IsNullOrEmpty(host) ? null : host + value;
        }
        return null;
    }

    public static string HostOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return null;
        return parsed.GetLeftPart(UriPartial.Authority);
    }
}
