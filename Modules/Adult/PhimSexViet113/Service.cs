using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace PhimSexViet113;

public static class PhimSexViet113To
{
    public static string SiteHost = "https://phimsexviet113.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri ==========
    // Home: / (trang 1), /trang/N/ (N>1).
    // Category: /phim-sex-viet-nam/ + /phim-sex-viet-nam/trang/2/
    //   5 chuyen muc: phim-sex-viet-nam / nhat-ban / trung-quoc / chau-au / vietsub. Home = Sex Viet Nam.
    // Search: /search/<slug>/ (slugify tieng Viet). Search khong co phan trang (trang 2 tra 0 item).
    // Tag: /tag/<slug>/ (tag list lay o footer "THE HOT" va trang chi tiet).
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = Slugify(search);
            return host + "/search/" + slug + "/";
        }
        string baseUrl;
        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                baseUrl = raw.TrimEnd('/');
            else
                baseUrl = host + "/" + raw;
        }
        else
            baseUrl = host;
        if (pg > 1)
            baseUrl += "/trang/" + pg + "/";
        else if (!baseUrl.EndsWith("/"))
            baseUrl += "/";
        return baseUrl;
    }

    // Giong media.js slugify() cua site: bo dau tieng Viet, thay space = "-"
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

    // ========== Pages ==========
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int max = 1;
        bool found = false;
        foreach (Match m in Regex.Matches(html, @"/trang/(\d+)/?"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
            found = true;
        }
        if (!found) return 1;
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
        if (!parsed.Host.EndsWith("phimsexviet113.com", StringComparison.OrdinalIgnoreCase))
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

    // ========== Playlist: <li class="item-movie"><a title href="*.html">... ==========
    // Anh lazyload: data-original truoc, src la gif 1px. Label VN/JP/... + time.
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<li\b[^>]*\bclass\s*=\s*""[^""]*\bitem-movie\b[^""]*""[^>]*>\s*<a\b[^>]*\bhref\s*=\s*""([^""]+\.html[^""]*)""[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string pageUrl = NormalizePageUrl(m.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string aTag = m.Value;
            var tm = Regex.Match(aTag, @"\btitle\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            string name = tm.Success ? Clean(HttpUtility.HtmlDecode(tm.Groups[1].Value)) : "";
            if (string.IsNullOrEmpty(name)) continue;
            string snippet = html.Substring(m.Index, Math.Min(3000, html.Length - m.Index));
            string poster = null;
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-original\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""\s]+\.(?:jpe?g|png|webp)[^""]*)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            var lm = Regex.Match(snippet, @"<div\b[^>]*\bclass\s*=\s*""label""[^>]*>([^<]*)<", RegexOptions.IgnoreCase);
            string label = lm.Success ? Clean(lm.Groups[1].Value) : "";
            var qm = Regex.Match(snippet, @"<div\b[^>]*\bclass\s*=\s*""title-movie""[^>]*>([^<]*)<", RegexOptions.IgnoreCase);
            if (string.IsNullOrEmpty(name) && qm.Success) name = Clean(HttpUtility.HtmlDecode(qm.Groups[1].Value));
            if (string.IsNullOrEmpty(name)) continue;
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = string.IsNullOrEmpty(label) ? name : label + " " + name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "phimsexviet113", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Detail: #video[data-video-id] + .video-server[data-server-id] ==========
    public static string VideoId(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"id\s*=\s*""video""[^>]*\bdata-video-id\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
        if (!m.Success)
            m = Regex.Match(html, @"\bdata-video-id\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static List<(string id, string num)> Servers(string html)
    {
        var res = new List<(string id, string num)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"class\s*=\s*""[^""]*\bvideo-server\b[^""]*""[^>]*\bdata-server-id\s*=\s*""(\d+)""",
            RegexOptions.IgnoreCase))
        {
            string id = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
            res.Add((id, id));
        }
        return res;
    }

    // ========== POST /api/player {id, server} -> {status,data:{type,sources}} ==========
    public static string PlayerBody(string videoId, string serverId)
        => "id=" + System.Uri.EscapeDataString(videoId ?? "")
         + "&server=" + System.Uri.EscapeDataString(serverId ?? "1");

    // Tra (link, referer). jwplayer -> sources[0].file (m3u8). iframe -> source.
    public static (string link, string referer) ParsePlayerJson(string json, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("status", out var st)
                || !string.Equals(st.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                return (null, null);
            if (!root.TryGetProperty("data", out var data)) return (null, null);
            string type = data.TryGetProperty("type", out var tp) ? (tp.GetString() ?? "") : "";
            if (string.Equals(type, "iframe", StringComparison.OrdinalIgnoreCase))
            {
                if (data.TryGetProperty("source", out var src))
                {
                    string u = src.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(u))
                    {
                        if (u.StartsWith("//")) u = "https:" + u;
                        if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            return (u, pageUrl);
                    }
                }
                return (null, null);
            }
            if (data.TryGetProperty("sources", out var sources)
                && sources.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sources.EnumerateArray())
                {
                    if (!s.TryGetProperty("file", out var f)) continue;
                    string u = f.GetString()?.Trim();
                    if (string.IsNullOrEmpty(u)) continue;
                    u = WebUtility.HtmlDecode(u).Replace("\\/", "/");
                    if (u.StartsWith("//")) u = "https:" + u;
                    if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        return (u, pageUrl);
                }
            }
            return (null, null);
        }
        catch { return (null, null); }
    }

    // ========== Menu (chuan 2 tang, §9a) ==========
    public static List<MenuItem> MenuHead(string host, string search)
    {
        host = host.TrimEnd('/');
        string root = host;
        return new List<MenuItem>(1)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
    }

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> hotTags)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "?c=" + HttpUtility.UrlEncode(c);
        var root = new List<MenuItem>(3)
        {
            // Moi muc root phai co submenu (muc tran bam vao chi hien lai bo loc).
            new MenuItem(){ title = "Danh sách", playlist_url = "submenu", submenu = new List<MenuItem>(){
                new("Mới nhất", host),
                new("Sex Việt Nam", url("phim-sex-viet-nam")),
                new("Sex Nhật Bản", url("phim-sex-nhat-ban")),
                new("Sex Trung Quốc", url("phim-sex-trung-quoc")),
                new("Sex Châu Âu", url("phim-sex-chau-au")),
                new("Vietsub", url("phim-sex-vietsub")),
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
                        .Select(x => new MenuItem(x.name, host + "?c=" + HttpUtility.UrlEncode("tag/" + x.slug))).ToList();
                    n++;
                    root.Add(new MenuItem() { title = $"Tag hot ({n})", playlist_url = "submenu", submenu = chunk });
                }
            }
            else root.Add(new MenuItem()
            {
                title = "Tag hot",
                playlist_url = "submenu",
                submenu = hotTags.Select(x => new MenuItem(x.name, host + "?c=" + HttpUtility.UrlEncode("tag/" + x.slug))).ToList()
            });
        }
        return root;
    }

    // THE HOT: <a href="/tag/<slug>/">Name</a> o footer. Dedup + strip FEFF.
    public static List<(string slug, string name)> HotTags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""/tag/([a-z0-9\-]+)/?""[^>]*>(.*?)</a>",
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

    // ========== Fetch helpers: direct-first + retry (site khong CF) ==========
    public static async Task<string> GetHtmlAsync(string url, string referer, int timeoutSeconds = 25, System.Net.WebProxy proxy = null, int httpversion = 1)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", ChromeUA),
            ("Referer", referer ?? SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        string html = null;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, httpversion: httpversion); } catch { }
        if (!string.IsNullOrEmpty(html) && html.Length > 5000) return html;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, proxy: proxy, httpversion: httpversion); } catch { return null; }
        if (string.IsNullOrEmpty(html)) return null;
        return html;
    }

    public static async Task<string> PostPlayerAsync(string pageUrl, string videoId, string serverId, System.Net.WebProxy proxy = null, int httpversion = 1, int timeoutSeconds = 15)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["id"] = videoId,
                ["server"] = serverId
            });
            var headers = HeadersModel.Init(
                ("User-Agent", ChromeUA),
                ("Referer", pageUrl),
                ("Origin", SiteHost),
                ("Accept", "application/json, text/plain, */*"),
                ("X-Requested-With", "XMLHttpRequest")
            );
            return await Http.Post(new Uri(new Uri(SiteHost), "/api/player").ToString(), content,
                timeoutSeconds: timeoutSeconds, headers: headers,
                proxy: proxy, httpversion: httpversion, disposeData: true);
        }
        catch { return null; }
    }
}
