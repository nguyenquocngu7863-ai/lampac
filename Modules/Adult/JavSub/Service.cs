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

namespace JavSub;

public static class JavSubTo
{
    public static string SiteHost = "https://javsub.blog";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri (Laravel blog, theme sex4a) ==========
    // Home: / + ?page=N (293 trang, do 2026-10-09).
    // Category/tag: /the-loai/<slug>, /tag/<slug> + ?page=N.
    // Search: /search?k=<tu> (form site: action=/search, input name=k).
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "k=" + System.Uri.EscapeDataString(search.Trim());
            return pg > 1 ? host + "/search?" + q + "&page=" + pg : host + "/search?" + q;
        }
        string baseUrl;
        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            baseUrl = raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? raw.TrimEnd('/') : host + "/" + raw;
        }
        else baseUrl = host + "/";
        if (pg <= 1) return baseUrl;
        return baseUrl + (baseUrl.Contains('?') ? "&" : "?") + "page=" + pg;
    }

    // ========== Sort: SITE KHONG CO (do live 2026-10-09) ==========
    // Home/nav/footer/detail khong co control sort, khong co query sort/order
    // trong HTML -> KHONG hien dong 2 (row chet con te hon row vang, nhu SupJav).
    public static string NormalizeSort(string sort) => null;
    public static (string name, string sort)[] SortsFor(string search, string c) => null;
    public static string ClampSort(string sort, string search, string c) => null;
    public static string SortLabel(string sort) => "mới nhất";

    // ========== So trang: <nav class="paginator"> -> ?page=N lon nhat ==========
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int i = html.IndexOf("paginator", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return 1;
        string slice = html.Substring(i, Math.Min(4000, html.Length - i));
        int max = 1;
        foreach (Match m in Regex.Matches(slice, @"[?&]page=(\d+)"))
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
        if (!parsed.Host.EndsWith("javsub.blog", StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    // ========== Playlist: .item > a[href=/phim-sex/slug][title] + img ==========
    // Trang taxonomy dung chung .item nhung link /the-loai/ -> regex chi nhan
    // /phim-sex/ nen khong lan.
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<div\b[^>]*\bclass\s*=\s*""item""[^>]*>\s*<a\b[^>]*\bhref\s*=\s*""([^""]+/phim-sex/[^""]*)""[^>]*\btitle\s*=\s*""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string pageUrl = NormalizePageUrl(m.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[2].Value));
            if (string.IsNullOrEmpty(name)) continue;
            string snippet = html.Substring(m.Index, Math.Min(1500, html.Length - m.Index));
            string poster = null;
            var pm = Regex.Match(snippet, @"<img\b[^>]*\bsrc\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "javsub", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Taxonomy: /?view=the-loai (42 muc, do 2026-10-09) ==========
    // <h3>Ten</h3> + <small>N phim</small>, sap theo so phim giam dan (giong site).
    public static List<(string slug, string name)> Taxonomies(string html)
    {
        var res = new List<(string slug, string name, int count)>();
        if (string.IsNullOrEmpty(html)) return new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"href\s*=\s*""[^""]*/the-loai/([a-z0-9\-/]+)""[^>]*>\s*<h3>([^<]{1,60})</h3>\s*<small[^>]*>(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = "the-loai/" + Clean(m.Groups[1].Value).Trim('/');
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[2].Value)).Replace(':', '-');
            if (string.IsNullOrEmpty(name) || !seen.Add(slug)) continue;
            int.TryParse(m.Groups[3].Value, out int count);
            if (count <= 0) continue;
            res.Add((slug, name, count));
        }
        res.Sort((a, b) => b.count.CompareTo(a.count));
        var out_ = new List<(string, string)>(res.Count);
        foreach (var r in res) out_.Add((r.slug, r.name));
        return out_;
    }

    // Fallback tinh khi fetch taxonomy loi (10 the-loai + 2 tag tren nav site).
    public static readonly (string slug, string name)[] FallbackTax =
    {
        ("the-loai/khong-che", "Không Che"), ("the-loai/vietsub", "Vietsub"),
        ("the-loai/phim-sex-hd", "Phim Sex HD"), ("the-loai/uncensored-leak", "Uncensored Leak"),
        ("the-loai/vu-to", "Vú To"), ("the-loai/mong-to", "Mông To"),
        ("the-loai/doggy", "Doggy"), ("the-loai/bu-cu", "Bú Cu"),
        ("the-loai/tap-the", "Tập Thể"), ("the-loai/vlxx", "VLXX"),
        ("tag/xvideos", "Xvideos"), ("tag/xnxx", "XNXX"),
    };

    // ========== Servers: button.set-player-source[data-source] ==========
    // Vi du: data-source="https://e.streamforester.name/videos/<id>/play?..." (Server 1),
    // https://vcast.name/videos/<id>/play?... (Server 2). Cung id, khac host.
    public static List<(string label, string link)> Servers(string html)
    {
        var res = new List<(string label, string link)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<button\b[^>]*\bdata-source\s*=\s*""([^""]+)""[^>]*>(.*?)</button\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string link = WebUtility.HtmlDecode(m.Groups[1].Value.Trim());
            string label = Clean(Regex.Replace(m.Groups[2].Value, "<[^>]+>", " "));
            if (string.IsNullOrEmpty(link) || !link.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(label)) label = "Server " + (res.Count + 1);
            if (label.Length > 12) label = label.Substring(0, 12).Trim();
            if (!seen.Add(label + "|" + link)) continue;
            res.Add((label, link));
        }
        return res;
    }

    // Player JW: POST <embed-host>/videos/<id>/config?d= (body rong, JSON) ->
    // {"sources":[{"file":"...m3u8|mp4"}],...}. JS goc (do 2026-10-09):
    // fetch(`/videos/${id}/config?d=${domain}`, {method:"POST", headers:json}).
    public static string ConfigUrl(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl)) return null;
        string noQ = embedUrl.Split('?')[0].TrimEnd('/');
        if (!noQ.EndsWith("/play", StringComparison.OrdinalIgnoreCase)) return null;
        return noQ[..^"/play".Length] + "/config?d=";
    }

    public static string SourceFile(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var m = Regex.Match(json, @"""file""\s*:\s*""([^""]+\.(m3u8|mp4)[^""]*)""", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string v = m.Groups[1].Value.Replace("\\/", "/");
        return v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : null;
    }

    // Embed kieu vcast (alias): videoData co san sources (khong can POST config):
    // videoData = {...,"sources":[{"label":"HLS",...,"file":"/videos/<id>/master.m3u8"}],...};
    public static string EmbedInlineSource(string embedHtml, string embedUrl)
    {
        if (string.IsNullOrEmpty(embedHtml) || string.IsNullOrEmpty(embedUrl)) return null;
        var m = Regex.Match(embedHtml, @"""sources""\s*:\s*\[\s*\{[^}]*?""file""\s*:\s*""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return null;
        string src = m.Groups[1].Value.Replace("\\/", "/").Trim();
        if (src.StartsWith("//")) return "https:" + src;
        if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return src;
        if (src.StartsWith("/"))
        {
            string host = HostOf(embedUrl);
            if (!string.IsNullOrEmpty(host)) return host + src;
        }
        return null;
    }

    public static string HostOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return null;
        return parsed.GetLeftPart(UriPartial.Authority);
    }

    // ========== Chrome fallback: mo trang embed that, bat response /config ==========
    // POST truc tiep doi khi 404 (do live 2026-10-09: streamforester/vcast tra
    // Not Found voi moi bien the query/body tu server) trong khi JS chay trong
    // browser that van lay duoc sources. Mo embed bang Playwright, nghe response
    // co /config trong URL roi doc file tu JSON; khong thay thi poll jwplayer
    // playlist. CHI goi o /video, cache 10p.
    public static async Task<string> ChromeSourceAsync(string embedUrl, string referer, int timeoutMs = 15000)
    {
        if (string.IsNullOrEmpty(embedUrl)) return null;
        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync("JavSub",
                    new Dictionary<string, string>
                    {
                        ["User-Agent"] = ChromeUA,
                        ["Referer"] = referer ?? (SiteHost + "/")
                    }, keepopen: false);
                if (page == null) return null;
                string got = null;
                page.Response += async (_, resp) =>
                {
                    try
                    {
                        if (got != null) return;
                        string u = resp.Url ?? "";
                        if (!u.Contains("/config")) return;
                        string js = await resp.TextAsync();
                        string f = SourceFile(js ?? "");
                        if (!string.IsNullOrEmpty(f)) got = f;
                    }
                    catch { }
                };
                try
                {
                    await page.GotoAsync(embedUrl, new Microsoft.Playwright.PageGotoOptions
                    {
                        Timeout = timeoutMs,
                        WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded
                    });
                }
                catch { }
                string file = null;
                for (int i = 0; i < 15 && string.IsNullOrEmpty(got); i++)
                {
                    try
                    {
                        file = await page.EvaluateAsync<string>(@"() => {
                            try {
                                var p = jwplayer('player');
                                var pl = p && p.getPlaylist ? p.getPlaylist() : null;
                                var s = pl && pl[0] && pl[0].sources ? pl[0].sources : null;
                                return (s && s[0] && s[0].file) ? s[0].file : null;
                            } catch (e) { return null; }
                        }");
                    }
                    catch { }
                    if (!string.IsNullOrEmpty(file)) break;
                    await Task.Delay(800);
                }
                try { await page.CloseAsync(); } catch { }
                string best = !string.IsNullOrEmpty(got) ? got : file;
                return string.IsNullOrEmpty(best) ? null : best;
            }
        }
        catch { return null; }
    }

    // ===== head: chi co Tim kiem (site khong sort) =====
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        return new List<MenuItem>(1)
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/javsub" }
        };
    }

    // ===== base: The loai (42) + Tag (2 nav; site khong co trang tag index) =====
    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/javsub?c=" + HttpUtility.UrlEncode(c);
        var root = new List<MenuItem>(2);
        var items = (cats != null && cats.Count > 0) ? cats : new List<(string, string)>(FallbackTax);
        var sub = new List<MenuItem>(items.Count);
        foreach (var (slug, name) in items)
            sub.Add(new MenuItem(string.IsNullOrEmpty(name) ? slug : name, url(slug)));
        root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = sub });
        root.Add(new MenuItem()
        {
            title = "Tag",
            playlist_url = "submenu",
            submenu = new List<MenuItem>()
            {
                new("Xvideos", url("tag/xvideos")),
                new("XNXX", url("tag/xnxx")),
            }
        });
        return root;
    }
}
