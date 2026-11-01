using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace XViet;

public static class XVietTo
{
    public static string SiteHost = "https://xviet.live";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri ==========
    // Home: / (trang 1), /page/N (N>1)
    // Category: /the-loai/<slug> + /the-loai/<slug>/page/N
    // Search + tag: /search/<slug> + /search/<slug>/page/N  (canonical giong nhau)
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        string baseUrl;
        if (!string.IsNullOrWhiteSpace(search))
            baseUrl = host + "/search/" + Slugify(search);
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                baseUrl = raw.TrimEnd('/');
            else
                baseUrl = host + "/" + raw;
        }
        else
            baseUrl = host;
        if (pg > 1) baseUrl += "/page/" + pg;
        return baseUrl;
    }

    public static string Slugify(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[àáạảãâầấậẩẫăằắặẳẵ]", "a");
        s = Regex.Replace(s, @"[èéẹẻẽêềếệểễ]", "e");
        s = Regex.Replace(s, @"[ìíịỉĩ]", "i");
        s = Regex.Replace(s, @"[òóọỏõôồốộổỗơờớợởỡ]", "o");
        s = Regex.Replace(s, @"[ùúụủũưừứựửữ]", "u");
        s = Regex.Replace(s, @"[ỳýỵỷỹ]", "y");
        s = s.Replace("đ", "d");
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"[^a-z0-9\-]", "");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int max = 1;
        bool found = false;
        foreach (Match m in Regex.Matches(html, @"/page/(\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
            found = true;
        }
        return found ? max : 1;
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
        if (!parsed.Host.EndsWith("xviet.live", StringComparison.OrdinalIgnoreCase))
            return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    // ========== Playlist: <article id="post-N" class="entry-video..."> ==========
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<article\b[^>]*\bid\s*=\s*""post-\d+""[^>]*>(.*?)</article>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string block = m.Groups[1].Value;
            var am = Regex.Match(block, @"<a\b[^>]*\bhref\s*=\s*""([^""]+)""[^>]*>", RegexOptions.IgnoreCase);
            if (!am.Success) continue;
            string pageUrl = NormalizePageUrl(am.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string name = "";
            var tm = Regex.Match(block, @"\btitle\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (tm.Success) name = Clean(HttpUtility.HtmlDecode(tm.Groups[1].Value));
            if (string.IsNullOrEmpty(name))
            {
                var hm = Regex.Match(block, @"<h2\b[^>]*>\s*<a\b[^>]*>([^<]+)</a>", RegexOptions.IgnoreCase);
                if (hm.Success) name = Clean(HttpUtility.HtmlDecode(hm.Groups[1].Value));
            }
            if (string.IsNullOrEmpty(name)) continue;
            string poster = null;
            var pm = Regex.Match(block, @"<img\b[^>]*\bsrc\s*=\s*""([^""\s]+)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "xviet", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Detail: window.VB_PLAY = { endpoint, s1, s2 } ==========
    public static List<(string server, string token)> VideoTokens(string html)
    {
        var res = new List<(string server, string token)>();
        if (string.IsNullOrEmpty(html)) return res;
        var m = Regex.Match(html, @"window\.VB_PLAY\s*=\s*\{(.*?)\}", RegexOptions.Singleline);
        if (!m.Success) return res;
        string body = m.Groups[1].Value;
        var s1 = Regex.Match(body, @"\bs1\s*:\s*""([^""]*)""");
        var s2 = Regex.Match(body, @"\bs2\s*:\s*""([^""]*)""");
        if (s1.Success && !string.IsNullOrEmpty(s1.Groups[1].Value)) res.Add(("1", s1.Groups[1].Value));
        if (s2.Success && !string.IsNullOrEmpty(s2.Groups[1].Value)) res.Add(("2", s2.Groups[1].Value));
        return res;
    }

    public static string PlayUrl(string token)
        => SiteHost + "/play/?token=" + System.Uri.EscapeDataString(token ?? "");

    // ========== Play page: const url = "..."; const playType = "embed" ==========
    public static (string url, string playType) ParsePlayUrl(string html)
    {
        if (string.IsNullOrEmpty(html)) return (null, null);
        var um = Regex.Match(html, @"(?:const|let|var)\s+url\s*=\s*""([^""]+)""");
        var pt = Regex.Match(html, @"(?:const|let|var)\s+playType\s*=\s*""([^""]*)""");
        string url = um.Success ? WebUtility.HtmlDecode(um.Groups[1].Value).Replace("\\/", "/") : null;
        if (!string.IsNullOrEmpty(url) && url.StartsWith("//")) url = "https:" + url;
        return (url, pt.Success ? pt.Groups[1].Value : "");
    }

    // ========== Embed: jwplayer sources [{file,type,label}] ==========
        // ========== Embed: nhiều kiểu player ==========
    // - play.kbj.lol (jwplayer): { "file":"...", "label":"..." }
    // - vscloud.club (Nid Player): window.PLAYER_CONFIG = { "source":"https://...m3u8" }
    // ========== Embed: nhiều kiểu player ==========
    // - play.kbj.lol (jwplayer): { "file":"...", "label":"..." }
    // - vscloud.club (Nid Player): window.PLAYER_CONFIG = { "source":"https://...m3u8" }
    public static List<(string file, string label)> ParseEmbedSources(string html)
    {
        var res = new List<(string file, string label)>();
        if (string.IsNullOrEmpty(html)) return res;

        // 1) jwplayer style: { "file":"...", "label":"..." }
        foreach (Match m in Regex.Matches(html, @"{\s*""file""\s*:\s*""([^""]+)""([^}]*)\}", RegexOptions.Singleline))
        {
            string file = WebUtility.HtmlDecode(m.Groups[1].Value).Replace("\\/", "/");
            if (file.StartsWith("enc.", StringComparison.OrdinalIgnoreCase)) file = DecodeEnc(file);
            if (string.IsNullOrEmpty(file)) continue;
            if (file.StartsWith("//")) file = "https:" + file;
            if (!file.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            var lm = Regex.Match(m.Groups[2].Value, @"""label""\s*:\s*""([^""]*)""");
            res.Add((file, lm.Success ? lm.Groups[1].Value : ""));
        }

        // 2) Nid Player (vscloud.club): window.PLAYER_CONFIG = { "source":"https://...m3u8" }
        foreach (Match m in Regex.Matches(html, @"PLAYER_CONFIG\s*=\s*(\{.*?\})", RegexOptions.Singleline))
        {
            var sm = Regex.Match(m.Groups[1].Value, @"""source""\s*:\s*""([^""]+)""");
            if (!sm.Success) continue;
            string src = WebUtility.HtmlDecode(sm.Groups[1].Value).Replace("\\/", "/");
            if (src.StartsWith("//")) src = "https:" + src;
            if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            res.Add((src, "auto"));
        }

        return res;
    }


    // Giong _0x_dec cua embed: enc.<hex> -> dao chuoi -> hex -> XOR key
    static string DecodeEnc(string a)
    {
        if (string.IsNullOrEmpty(a) || !a.StartsWith("enc.", StringComparison.OrdinalIgnoreCase)) return a;
        string s = a.Substring(4).Replace(".", "");
        char[] arr = s.ToCharArray();
        Array.Reverse(arr);
        string e = new string(arr);
        var sb = new StringBuilder();
        for (int g = 0; g + 1 < e.Length; g += 2)
        {
            if (int.TryParse(e.Substring(g, 2), System.Globalization.NumberStyles.HexNumber, null, out int h))
                sb.Append((char)h);
        }
        string f = sb.ToString();
        const string key = "o_X1_2Gj";
        var outSb = new StringBuilder();
        for (int j = 0; j < f.Length; j++)
            outSb.Append((char)(f[j] ^ key[j % key.Length]));
        return outSb.ToString();
    }

    // ========== Menu ==========
    public static List<MenuItem> MenuHead(string host, string search)
    {
        host = host.TrimEnd('/');
        string root = host + "/xviet";
        return new List<MenuItem>(1)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
    }

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> hotTags)
    {
        host = host.TrimEnd('/');
        string root = host + "/xviet";
        string url(string c) => root + "?c=" + HttpUtility.UrlEncode(c);
        var rootList = new List<MenuItem>(2)
        {
            new MenuItem(){ title = "Danh sách", playlist_url = "submenu", submenu = new List<MenuItem>(){
                new("Mới nhất", root),
                new("Check hàng", url("the-loai/check-hang")),
                new("ClipHot", url("the-loai/cliphot")),
                new("Cưỡi ngựa", url("the-loai/cuoi-ngua")),
                new("Doggy", url("the-loai/doggy")),
                new("HeoVl", url("the-loai/heovl")),
                new("Mobiblog", url("the-loai/mobiblog")),
                new("Phevkl", url("the-loai/phevkl")),
                new("Sinh viên", url("the-loai/sinh-vien")),
                new("Teen", url("the-loai/teen")),
            }},
        };
        if (hotTags != null && hotTags.Count > 0)
        {
            if (hotTags.Count > 300)
            {
                int n = 0;
                for (int i = 0; i < hotTags.Count; i += 300)
                {
                    var chunk = hotTags.Skip(i).Take(300)
                        .Select(x => new MenuItem(x.name, root + "?c=" + HttpUtility.UrlEncode("search/" + x.slug))).ToList();
                    n++;
                    rootList.Add(new MenuItem() { title = $"Tag hot ({n})", playlist_url = "submenu", submenu = chunk });
                }
            }
            else rootList.Add(new MenuItem()
            {
                title = "Tag hot",
                playlist_url = "submenu",
                submenu = hotTags.Select(x => new MenuItem(x.name, root + "?c=" + HttpUtility.UrlEncode("search/" + x.slug))).ToList()
            });
        }
        return rootList;
    }

    public static List<(string slug, string name)> HotTags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""/search/([a-z0-9\-]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = Clean(m.Groups[1].Value);
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " "));
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name)) continue;
            if (name.Length > 40 || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    // ========== Fetch helper ==========
    public static async Task<string> GetHtmlAsync(string url, string referer, int timeoutSeconds = 25, System.Net.WebProxy proxy = null, int httpversion = 1)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", ChromeUA),
            ("Referer", referer ?? SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        string html = null;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, httpversion: httpversion); } catch { }
        if (!string.IsNullOrEmpty(html) && html.Length > 2000) return html;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, proxy: proxy, httpversion: httpversion); } catch { return null; }
        if (string.IsNullOrEmpty(html)) return null;
        return html;
    }
}
