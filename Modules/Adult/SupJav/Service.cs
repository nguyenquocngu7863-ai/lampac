using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace SupJav;

public static class SupJavTo
{
    public static string SiteHost = "https://supjav.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
    public const string GatewayHost = "https://lk1.supremejav.com";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri (WordPress) ==========
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            // /?s=term, trang N: /page/N/?s=term
            string q = "s=" + System.Uri.EscapeDataString(search.Trim());
            return pg > 1 ? host + "/page/" + pg + "/?" + q : host + "/?" + q;
        }
        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return pg > 1 ? raw.TrimEnd('/') + "/page/" + pg + "/" : raw;
            string baseUrl = host + "/" + raw;
            return pg > 1 ? baseUrl.TrimEnd('/') + "/page/" + pg + "/" : baseUrl;
        }
        return pg > 1 ? host + "/page/" + pg + "/" : host + "/";
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
        if (!parsed.Host.EndsWith("supjav.com", StringComparison.OrdinalIgnoreCase))
            return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static string HostOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return null;
        return parsed.GetLeftPart(UriPartial.Authority);
    }

    // ========== Playlist: <div class="post"><a href="...html" class="img" title=".."><img src=".." class="thumb"/></a> ==========
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<div\b[^>]*\bclass\s*=\s*""[^""]*\bpost\b[^""]*""[^>]*>\s*<a\b[^>]*\bhref\s*=\s*""([^""]+\.html[^""]*)""[^>]*\btitle\s*=\s*""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string pageUrl = NormalizePageUrl(m.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[2].Value));
            if (string.IsNullOrEmpty(name)) continue;
            string snippet = html.Substring(m.Index, Math.Min(2000, html.Length - m.Index));
            string poster = null;
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "supjav", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Servers: .btn-server[data-link] + label ==========
    public static List<(string label, string link)> Servers(string html)
    {
        var res = new List<(string label, string link)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"class\s*=\s*""[^""]*\bbtn-server\b[^""]*""[^>]*\bdata-link\s*=\s*""([a-f0-9]{32,})""[^>]*>(.*?)<",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string link = m.Groups[1].Value.Trim();
            string label = Clean(Regex.Replace(m.Groups[2].Value, "<[^>]+>", " "));
            if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(label) || !seen.Add(label + "|" + link)) continue;
            if (label.Length > 12) label = label.Substring(0, 12).Trim();
            res.Add((label, link));
        }
        return res;
    }

    // ========== Gateway: lk1.supremejav.com/supjav.php?l=<link> -> ?c=<reversed> ==========
    public static string GatewayUrl(string link) => GatewayHost + "/supjav.php?l=" + link;
    public static string FinalUrl(string link)
    {
        if (string.IsNullOrEmpty(link)) return null;
        char[] rev = link.ToCharArray();
        Array.Reverse(rev);
        return GatewayHost + "/supjav.php?l=" + link + "&c=" + new string(rev);
    }

    // ========== StreamHg unpack (giong SexTb/JavCt) ==========
    public static string Unpack(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"\}\('([\s\S]*?)',(\d+),(\d+),'([\s\S]*?)'\.split\('\|'\)\)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string p = m.Groups[1].Value;
        if (!int.TryParse(m.Groups[2].Value, out int a) || !int.TryParse(m.Groups[3].Value, out int c)) return null;
        var k = m.Groups[4].Value.Split('|');
        for (int i = c - 1; i >= 0; i--)
        {
            if (i >= k.Length || string.IsNullOrEmpty(k[i])) continue;
            p = Regex.Replace(p, @"\b" + ToBase(i, a) + @"\b", _ => k[i]);
        }
        return p;
    }
    static string ToBase(int value, int b)
    {
        if (value == 0) return "0";
        var sb = new System.Text.StringBuilder();
        while (value > 0) { int d = value % b; sb.Insert(0, (char)(d < 10 ? '0' + d : 'a' + d - 10)); value /= b; }
        return sb.ToString();
    }

    public static List<string> StreamHgMasters(string html)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(html)) return res;
        string src = Unpack(html) ?? html;
        var m = Regex.Match(src, @"var\s+links\s*=\s*\{([^}]{0,6000})\}", RegexOptions.IgnoreCase);
        if (!m.Success) return res;
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match x in Regex.Matches(m.Groups[1].Value, @"""(hls\d)""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase))
            kv[x.Groups[1].Value] = HttpUtility.HtmlDecode(x.Groups[2].Value.Trim()).Replace("\\/", "/");
        foreach (string key in new[] { "hls3", "hls2", "hls4" })
        {
            if (!kv.TryGetValue(key, out string v) || string.IsNullOrEmpty(v)) continue;
            if (v.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !res.Contains(v)) res.Add(v);
        }
        return res;
    }

    // ========== StreamTape: /e/ID -> #robotlink ==========
    public static string StreamTapeId(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl)) return null;
        var m = Regex.Match(embedUrl, @"streamtape\.com/e/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string StreamTapeMp4(string embedHtml)
    {
        if (string.IsNullOrEmpty(embedHtml)) return null;
        var m = Regex.Match(embedHtml, @"id\s*=\s*""robotlink""[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string path = m.Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(path)) return null;
        if (path.StartsWith("//")) return "https:" + path;
        // robotlink dang "/streamtape.com/get_video?..." (host nam trong path)
        if (path.StartsWith("/")) path = path.TrimStart('/');
        if (path.StartsWith("streamtape.com/", StringComparison.OrdinalIgnoreCase))
            return "https://" + path;
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;
        return "https://streamtape.com/" + path;
    }

    // ========== Taxonomies: /category/<slug>/ ==========
    public static List<(string slug, string name)> Taxonomies(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""(https?://supjav\.com)?/category/([a-z0-9\-]+)/?""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "category/" + m.Groups[2].Value.Trim();
            string raw = Regex.Replace(m.Groups[3].Value, "<[^>]+>", " ");
            string name = Clean(HttpUtility.HtmlDecode(raw));
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(slug) || !seen.Add(slug)) continue;
            if (name.Length > 40) continue;
            res.Add((slug, name));
        }
        return res;
    }

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/supjav?c=" + HttpUtility.UrlEncode(c);
        var root = new List<MenuItem>(3)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/supjav" }
        };
        if (cats != null && cats.Count > 0)
        {
            var gm = new List<MenuItem>(cats.Count);
            foreach (var (slug, name) in cats)
                gm.Add(new MenuItem(string.IsNullOrEmpty(name) ? slug : name, url(slug)));
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = gm });
        }
        return root;
    }

    // ========== Fetch helpers (direct-first, retry vi CF abort theo dot) ==========
    public static async Task<string> GetHtmlAsync(string url, string referer, int timeoutSeconds = 25, System.Net.WebProxy proxy = null, int httpversion = 1)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", ChromeUA),
            ("Referer", referer ?? SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        string html = null;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, httpversion: httpversion); } catch { }
        if (!string.IsNullOrEmpty(html) && html.Length > 5000 && !html.Contains("Just a moment")) return html;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, proxy: proxy, httpversion: httpversion); } catch { return null; }
        if (string.IsNullOrEmpty(html) || html.Contains("Just a moment")) return null;
        return html;
    }
}
