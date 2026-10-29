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

namespace Viet69Life;

public static class Viet69LifeTo
{
    public static string SiteHost = "https://viet69z.life";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    // ========== Uri (WordPress) ==========
    // Home: / (default latest) ; /page/N/  +  ?filter=latest|most-viewed|popular|random
    // Category: /{slug}/ ; /{slug}/page/N/  +  ?filter=...
    // Search: /?s=kw ; /page/N/?s=kw
    public static string Uri(string search, string c, string sort, int pg)
    {
        string host = SiteHost;
        sort = NormalizeSort(sort);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "s=" + System.Uri.EscapeDataString(search.Trim());
            return pg > 1 ? host + "/page/" + pg + "/?" + q : host + "/?" + q;
        }

        string baseUrl;
        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            baseUrl = host + "/" + raw;
            if (pg > 1) baseUrl = baseUrl.TrimEnd('/') + "/page/" + pg + "/";
        }
        else
        {
            baseUrl = pg > 1 ? host + "/page/" + pg + "/" : host + "/";
        }

        if (!string.IsNullOrEmpty(sort))
            return baseUrl + (baseUrl.Contains('?') ? "&" : "?") + "filter=" + sort;
        return baseUrl;
    }

    public static readonly string[] SortWhitelist = { "latest", "most-viewed", "popular", "random" };
    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    // ========== So trang ==========
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"/page/(\d+)"))
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
        if (!parsed.Host.EndsWith("viet69z.life", StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return WebUtility.HtmlDecode(s).Trim();
    }

    // ========== Playlist: <div class="video-block"> ... <a class="thumb" href=".html"> + <img data-src> + <span class="duration"> + <a class="infos" title> ==========
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a\b[^>]*\bclass\s*=\s*""[^""]*\bthumb\b[^""]*""[^>]*\bhref\s*=\s*""([^""]*\.html)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string pageUrl = NormalizePageUrl(m.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;

            string snippet = html.Substring(m.Index, Math.Min(3000, html.Length - m.Index));

            string name = null;
            var tm = Regex.Match(snippet, @"<a\b[^>]*\binfos\b[^>]*\btitle\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
            if (tm.Success) name = Clean(tm.Groups[1].Value);
            if (string.IsNullOrEmpty(name))
            {
                var sm = Regex.Match(snippet, @"<span\b[^>]*\bclass\s*=\s*""title""[^>]*>([^<]*)</span>", RegexOptions.IgnoreCase);
                if (sm.Success) name = Clean(sm.Groups[1].Value);
            }
            if (string.IsNullOrEmpty(name)) continue;

            string poster = null;
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-src\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);

            string duration = null;
            var dm = Regex.Match(snippet, @"<span\b[^>]*\bclass\s*=\s*""duration""[^>]*>([^<]*)</span>", RegexOptions.IgnoreCase);
            if (dm.Success) duration = dm.Groups[1].Value.Trim();

            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                time = duration,
                json = true,
                bookmark = new Bookmark() { site = "viet69life", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Servers: <button class="server" data-t="iframe" data-id="URL">LABEL</button> ==========
    public static List<(string label, string link)> Servers(string html)
    {
        var res = new List<(string label, string link)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<button\b[^>]*\bclass\s*=\s*""[^""]*\bserver\b[^""]*""[^>]*\bdata-id\s*=\s*""([^""]*)""[^>]*>(.*?)</button>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string link = m.Groups[1].Value.Trim();
            string label = Clean(Regex.Replace(m.Groups[2].Value, "<[^>]+>", " "));
            if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(label) || !seen.Add(label + "|" + link)) continue;
            res.Add((label, link));
        }
        return res;
    }

    // ========== Stream: iframe(data-id) -> jwplayer sources[0].file (.m3u8) ==========
    // Nguồn ghi URL có slash escape (`https:\/\/`) -> phải unescape trước khi regex.
    // Config dạng "file":"https://..." (key file trong nháy, rồi hai chấm).
    public static string JwplayerMaster(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        string src = html.Replace("\\/", "/");
        var m = Regex.Match(src, @"file[""']:\s*[""'](https?://[^""']+?\.m3u8)[^""']*[""']", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string v = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
        return v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : null;
    }

    // ========== Nav categories (menu-item > nav-link, exclude .html videos) ==========
    public static List<(string slug, string name)> NavCategories(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<li\b[^>]*\bclass\s*=\s*""[^""]*\bmenu-item\b[^""]*""[^>]*>\s*<a\b[^>]*\bhref\s*=\s*""([^""]*)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string href = m.Groups[1].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " "));
            if (href.StartsWith("/")) href = SiteHost + href;
            if (!href.StartsWith(SiteHost, StringComparison.OrdinalIgnoreCase)) continue;
            if (href.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;
            string path = href.Substring(SiteHost.Length).Trim('/');
            if (string.IsNullOrEmpty(path) || path.Length > 60 || !seen.Add(path)) continue;
            res.Add((path, name));
        }
        return res;
    }
}
