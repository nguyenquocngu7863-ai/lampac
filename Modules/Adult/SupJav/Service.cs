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
    // Home = Popular (list tron 24, lay het roi ngung). Moi nhat = latest home (/).
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
            if (raw == "__latest")
                return pg > 1 ? host + "/page/" + pg + "/" : host + "/";
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return pg > 1 ? raw.TrimEnd('/') + "/page/" + pg + "/" : raw;
            string baseUrl = host + "/" + raw;
            return pg > 1 ? baseUrl.TrimEnd('/') + "/page/" + pg + "/" : baseUrl;
        }
        return host + "/popular";
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
            // anh lazyload: data-original / data-src truoc, src thuong sau
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-original\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(snippet, @"<img\b[^>]*\bdata-src\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!pm.Success)
                pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
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
        // Thu tu hls2 truoc: hls3 tro toi host crystalhavenstudios.shop da bi
        // Cloudflare chan (403 "Website Access Blocked") va host khac (404) --
        // hls2 tren cdn-centaurus.com moi la bien con song (2026-10).
        foreach (string key in new[] { "hls2", "hls3", "hls4" })
        {
            if (!kv.TryGetValue(key, out string v) || string.IsNullOrEmpty(v)) continue;
            if (v.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !res.Contains(v)) res.Add(v);
        }
        return res;
    }

    // ========== Gateway shell: trang playbutton trung gian (can di ?l= lay session truoc) ==========
    public static bool IsGatewayShell(string html)
    {
        if (string.IsNullOrEmpty(html)) return true;
        if (html.Contains("start_player") || html.Contains("var OLID")) return true;
        if (html.Length < 6000 && !html.Contains("jwplayer") && !html.Contains("var links") && !html.Contains("robotlink"))
            return true;
        return false;
    }

    // ========== VAS (Vidara): 302 -> https://<host>/e/<filecode> -> POST /api/stream -> streaming_url ==========
    public static (string apiHost, string filecode) VasTarget(string location)
    {
        if (string.IsNullOrEmpty(location)) return (null, null);
        var m = Regex.Match(location, @"(https?://[^/]+)/e/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (!m.Success) return (null, null);
        return (m.Groups[1].Value, m.Groups[2].Value);
    }

    public static string VasStreamingUrl(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var m = Regex.Match(json, @"""streaming_url""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string v = m.Groups[1].Value.Replace("\\/", "/");
        return v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : null;
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

    // ========== Taxonomies ==========
    // categories: /category/<slug>/ (nav home) ; makers: /category/maker/<slug> (trang /maker)
    public static List<(string slug, string name)> Taxonomies(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""(https?://supjav\.com)?/category/(?!maker/)([a-z0-9\-]+)/?""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "category/" + m.Groups[2].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[3].Value), "<[^>]+>", " "));
            if (string.IsNullOrEmpty(name) || name.Length > 40 || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    public static List<(string slug, string name)> Makers(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""(https?://supjav\.com)?/category/maker/([a-z0-9\-]+)/?""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "category/maker/" + m.Groups[2].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[3].Value), "<[^>]+>", " "));
            name = Regex.Replace(name, @"\s*\(\d+\)\s*$", "").Trim();
            if (string.IsNullOrEmpty(name) || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    public static List<(string slug, string name)> Tags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""(https?://supjav\.com)?/tag/([a-z0-9\-]+)/?""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "tag/" + m.Groups[2].Value.Trim();
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[3].Value), "<[^>]+>", " "));
            name = Regex.Replace(name, @"\s*\(\d+\)\s*$", "").Trim();
            if (string.IsNullOrEmpty(name) || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats, List<(string slug, string name)> makers, List<(string slug, string name)> tags)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/supjav?c=" + HttpUtility.UrlEncode(c);
        var root = new List<MenuItem>(5)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/supjav" }
        };
        root.Add(new MenuItem() { title = "Sắp xếp", playlist_url = "submenu", submenu = new List<MenuItem>(){
            new("Mới nhất", url("__latest")),
            new("Phổ biến", url("popular")),
        }});
        List<MenuItem> TaxMenu(List<(string slug, string name)> items)
        {
            var gm = new List<MenuItem>(items.Count);
            foreach (var (slug, name) in items)
                gm.Add(new MenuItem(string.IsNullOrEmpty(name) ? slug : name, url(slug)));
            return gm;
        }
        if (cats != null && cats.Count > 0)
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = TaxMenu(cats) });
        if (makers != null && makers.Count > 0)
        {
            // makers da sort=quantity: chia khoi tuan tu giu thu tu (Top 1-300...), khong bucket A-Z
            if (makers.Count > 300)
            {
                int n = 0;
                for (int i = 0; i < makers.Count; i += 300)
                {
                    var chunk = makers.Skip(i).Take(300).Select(x => new MenuItem(x.name, url(x.slug))).ToList();
                    n++;
                    root.Add(new MenuItem() { title = $"Hãng phim Top {i + 1}–{i + chunk.Count}", playlist_url = "submenu", submenu = chunk });
                }
            }
            else root.Add(new MenuItem() { title = "Hãng phim", playlist_url = "submenu", submenu = TaxMenu(makers) });
        }
        if (tags != null && tags.Count > 0)
        {
            if (tags.Count > 300)
            {
                foreach (var b in DirBuckets(host, "Genre", tags, 300))
                    root.Add(b);
            }
            else root.Add(new MenuItem() { title = "Genre", playlist_url = "submenu", submenu = TaxMenu(tags) });
        }
        return root;
    }

    public const int MaxPerBucket = 300;
    public static List<MenuItem> DirBuckets(string host, string title, IReadOnlyList<(string slug, string name)> all, int maxPer = MaxPerBucket)
    {
        var res = new List<MenuItem>();
        if (all == null || all.Count == 0) return res;
        var groups = new Dictionary<char, List<(string slug, string name)>>();
        foreach (var it in all)
        {
            if (string.IsNullOrWhiteSpace(it.name)) continue;
            char c = char.ToUpperInvariant(it.name.Trim()[0]);
            if (c < 'A' || c > 'Z') c = '#';
            if (!groups.TryGetValue(c, out var lst)) { lst = new List<(string, string)>(); groups[c] = lst; }
            lst.Add(it);
        }
        var keys = groups.Keys.OrderBy(k => k == '#' ? 0 : 20 + (k - 'A')).ToList();
        var flat = new List<(char key, string name, string path)>(all.Count);
        foreach (var k in keys) foreach (var it in groups[k]) flat.Add((k, it.name, it.slug));
        var from = new List<char>(); var to = new List<char>(); var subs = new List<List<MenuItem>>();
        for (int i = 0; i < flat.Count; i += maxPer)
        {
            int n = Math.Min(maxPer, flat.Count - i);
            from.Add(flat[i].key); to.Add(flat[i + n - 1].key);
            subs.Add(flat.Skip(i).Take(n).Select(x => new MenuItem(x.name, host.TrimEnd('/') + "/supjav?c=" + HttpUtility.UrlEncode(x.path))).ToList());
        }
        for (int k = 0; k < from.Count; k++)
            res.Add(new MenuItem(title + " " + from[k] + (to[k] == from[k] ? "" : "–" + to[k]), "submenu") { submenu = subs[k] });
        return res;
    }

    // ========== VOE/VAS qua Chrome (port JavGuru VoSourceAsync) ==========
    // Trang final (supjav.php?l=..&c=..) redirect bang JS localStorage (VOE)
    // hoac jwplayer + pako/crypto (VAS): curl khong ra link. Mo that bang
    // Playwright, bat network .m3u8/.mp4 + doc jwplayer playlist.
    // CHI goi o /video (ton 4-12s), khong goi o /vidosik.
    public static bool IsVoeLabel(string label)
        => !string.IsNullOrEmpty(label) &&
            (label.IndexOf("VOE", StringComparison.OrdinalIgnoreCase) >= 0 ||
             label.IndexOf("VAS", StringComparison.OrdinalIgnoreCase) >= 0);

    public static async Task<string> VoeSourceAsync(string pageUrl, string referer, int timeoutMs = 12000)
    {
        if (string.IsNullOrEmpty(pageUrl)) return null;
        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync("SupJav",
                    new Dictionary<string, string>
                    {
                        ["User-Agent"] = ChromeUA,
                        ["Referer"] = referer ?? (SiteHost + "/")
                    }, keepopen: false);
                if (page == null) return null;
                string got = null;
                page.Request += (_, req) =>
                {
                    try
                    {
                        string u = req.Url;
                        if (got != null) return;
                        bool media = u.Contains(".m3u8") || u.Contains(".mp4");
                        bool ad = u.IndexOf("ima3.js", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  u.IndexOf("ads", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (media && !ad) got = u;
                    }
                    catch { }
                };
                await page.GotoAsync(pageUrl, new Microsoft.Playwright.PageGotoOptions
                {
                    Timeout = timeoutMs,
                    WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded
                });
                string file = null;
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        file = await page.EvaluateAsync<string>(@"() => {
                            try {
                                var p = jwplayer('a');
                                var pl = p && p.getPlaylist ? p.getPlaylist() : null;
                                var s = pl && pl[0] && pl[0].sources ? pl[0].sources : null;
                                return (s && s[0] && s[0].file) ? s[0].file : null;
                            } catch (e) { return null; }
                        }");
                    }
                    catch { }
                    if (!string.IsNullOrEmpty(file) || got != null) break;
                    await Task.Delay(700);
                }
                try { await page.CloseAsync(); } catch { }
                string best = !string.IsNullOrEmpty(file) ? file : got;
                return string.IsNullOrEmpty(best) ? null : best;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SupJav: voe loi {ex.Message}");
            return null;
        }
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
