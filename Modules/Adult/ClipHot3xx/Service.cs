using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace ClipHot3xx;

public static class ClipHot3xxTo
{
    public static string SiteHost = "https://cliphot3xx.com";
    // BẮT BUỘC = `Http.UserAgent` (Chrome/146) — URL googlevideo bi rang buoc voi
    // UA dung khi goi batchexecute (dong cong thuc F14, xem module Mbbg).
    public static string ChromeUA = Http.UserAgent;

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri (WordPress-like) ==========
    public static string Uri(string host, string search, string c, string sort, int pg)
    {
        // Force real site host
        host = SiteHost.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = System.Uri.EscapeDataString(search.Trim());
            string url = host + "/search?q=" + q;
            if (pg > 1) url += "&page=" + pg;
            return url;
        }
        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            // Tag handling (prefix "tag/")
            if (raw.StartsWith("tag/", StringComparison.OrdinalIgnoreCase))
            {
                string tagUrl = host + "/" + raw;
                return pg > 1 ? tagUrl + "?page=" + pg : tagUrl;
            }
            // Category handling
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return pg > 1 ? raw.TrimEnd('/') + "?page=" + pg : raw;
            string categoryUrl = host + "/category/" + raw;
            return pg > 1 ? categoryUrl + "?page=" + pg : categoryUrl;
        }
        // Home page
        return pg > 1 ? host + "/?page=" + pg : host + "/";
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"[?&]page=(\d+)", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        }
        // Check rel=next
        if (Regex.IsMatch(html, @"rel\s*=\s*[""']next[""']", RegexOptions.IgnoreCase))
            max = Math.Max(max, 2);
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
        if (!parsed.Host.EndsWith("cliphot3xx.com", StringComparison.OrdinalIgnoreCase)) return null;
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

    // ========== Playlist ==========
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a\s+[^>]*href\s*=\s*""(https?://cliphot3xx\.com/video/\d+[^""]*)""[^>]*title\s*=\s*""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string pageUrl = NormalizePageUrl(m.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[2].Value));
            if (string.IsNullOrEmpty(name)) continue;
            // Extract poster from surrounding img tag
            int endA = html.IndexOf("</a>", m.Index, StringComparison.OrdinalIgnoreCase);
            int len = endA > 0 ? Math.Min(2000, endA - m.Index) : 2000;
            string snippet = html.Substring(m.Index, len);
            string poster = null;
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-original\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success) pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-src\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success) pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "cliphot3xx", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Embed Url extraction from detail page ==========
    public static string EmbedUrl(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var m = Regex.Match(html, @"""embedUrl""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (m.Success) return WebUtility.HtmlDecode(m.Groups[1].Value);
        return null;
    }

    // ========== Token extraction from blogger embed URL ==========
    // https://www.blogger.com/video.g?token=<TOKEN>&origin=  -> <TOKEN>
    public static string BloggerToken(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var m = Regex.Match(url, @"[?&]token=([^&]+)", RegexOptions.IgnoreCase);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    public static string BatchUrl =
        "https://www.blogger.com/_/BloggerVideoPlayerUi/data/batchexecute"
        + "?rpcids=WcwnYd&source-path=%2Fvideo.g&hl=en-US&rt=c";

    public static string BatchBody(string token)
    {
        string inner = "[\"" + token + "\",null,0]";
        string fReq = "[[[\"WcwnYd\"," + JsonSerializer.Serialize(inner)
            + ",null,\"generic\"]]]";
        return "f.req=" + HttpUtility.UrlEncode(fReq) + "&";
    }

    // Parse batchexecute response -> googlevideo URLs
    // Tra ve XSSI:
    //   )]}'
    //   <len>
    //   [["wrb.fr","WcwnYd","<json dang string>"], … ]
    public static Dictionary<string, string> BloggerFormats(string resp)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(resp)) return res;
        try
        {
            int p = resp.IndexOf(")]}'", StringComparison.Ordinal);
            string body = p >= 0 ? resp.Substring(p + 4) : resp;
            int s = body.IndexOf('[');
            if (s < 0) return res;
            body = body.Substring(s);

            var rd = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(body));
            using var root = JsonDocument.ParseValue(ref rd);
            if (root.RootElement.ValueKind != JsonValueKind.Array || root.RootElement.GetArrayLength() == 0)
                return res;

            string inner = root.RootElement[0][2].GetString();
            if (string.IsNullOrEmpty(inner)) return res;

            var rd2 = new System.Text.Json.Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(inner));
            using var doc = JsonDocument.ParseValue(ref rd2);

            var fmts = doc.RootElement[2];
            foreach (var it in fmts.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Array || it.GetArrayLength() < 2) continue;
                string url = it[0].GetString();
                int itag = it[1][0].GetInt32();
                if (string.IsNullOrEmpty(url)) continue;
                string label = itag == 22 ? "720p" : itag == 18 ? "360p" : "itag " + itag;
                res[label] = url;
            }
        }
        catch { }
        return res;
    }

    // ========== HLS URL extraction from player page ==========
    // JWPlayer config: sources: [{ file: "https:\/\/cliphot3xx.com\/hls\/...m3u8", type: 'hls' }]
    public static string HlsUrl(string playerHtml)
    {
        if (string.IsNullOrEmpty(playerHtml)) return null;
        var m = Regex.Match(playerHtml,
            @"file\s*:\s*""(https??:?\\?/\\?/[^""]*?\.m3u8[^""]*)""",
            RegexOptions.IgnoreCase);
        if (m.Success)
        {
            // Remove backslash escapes: \/ -> /
            return m.Groups[1].Value.Replace("\\/", "/");
        }
        return null;
    }

    // ========== Taxonomies ==========
    public static List<(string slug, string name)> Categories(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a[^>]*href\s*=\s*""(https?://cliphot3xx\.com)?/category/([a-z0-9\-]+)/?""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = m.Groups[2].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[3].Value), "<[^>]+>", " "));
            if (string.IsNullOrEmpty(name) || name.Length > 40 || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    public static List<(string slug, string name)> Tags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a[^>]*href\s*=\s*""(https?://cliphot3xx\.com)?/tag/([a-z0-9\-]+)/?""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "tag/" + m.Groups[2].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[3].Value), "<[^>]+>", " "));
            name = name.Replace("#", "").Trim();
            if (string.IsNullOrEmpty(name) || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    // ========== Menu construction ==========
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/cliphot3xx";
        var res = new List<MenuItem>(2)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
        // No sorting submenu for now
        return res;
    }

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats, List<(string slug, string name)> tags)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/cliphot3xx?c=" + HttpUtility.UrlEncode(c);
        var root = new List<MenuItem>(4)
        {
            new MenuItem(){ title = "Mới nhất", playlist_url = host + "/cliphot3xx" }
        };
        List<MenuItem> TaxMenu(List<(string slug, string name)> items)
        {
            var gm = new List<MenuItem>(items.Count);
            foreach (var (slug, name) in items)
                gm.Add(new MenuItem(string.IsNullOrEmpty(name) ? slug : name, url(slug)));
            return gm;
        }
        if (cats != null && cats.Count > 0)
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = TaxMenu(cats) });
        if (tags != null && tags.Count > 0)
            root.Add(new MenuItem() { title = "Tag", playlist_url = "submenu", submenu = TaxMenu(tags) });
        return root;
    }
}
