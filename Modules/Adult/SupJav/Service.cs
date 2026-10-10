using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    // Home = Popular (mac dinh = Day). Moi nhat = latest home (/).
    // QUAN TRONG: phan trang PHAI duoc ap dung cho home. Truoc day `pg` bi bo qua
    // khi `c` rong (`return host + "/popular"`), nen app cuon toi trang N nao cung
    // tra cung 24 phim dau -> home lap vo han. Do la nguyen nhan "home van lap".
    //
    // `sort` phai di SAU /page/N/ moi dung (đo trên site):
    //   /popular/page/2/?sort=week  ✅  (khac trang 1, giu tab active "Week")
    //   /popular/?sort=week&page=2  ❌  -> /popular/2?sort=week = trang 1
    public static string Uri(string host, string search, string c, string sort, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            // /?s=term, trang N: /page/N/?s=term  (search khong co sort)
            string q = "s=" + System.Uri.EscapeDataString(search.Trim());
            return pg > 1 ? host + "/page/" + pg + "/?" + q : host + "/?" + q;
        }
        string url;
        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            if (raw == "__latest")
                url = pg > 1 ? host + "/page/" + pg + "/" : host + "/";
            else if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = pg > 1 ? raw.TrimEnd('/') + "/page/" + pg + "/" : raw;
            else
            {
                string baseUrl = host + "/" + raw;
                url = pg > 1 ? baseUrl.TrimEnd('/') + "/page/" + pg + "/" : baseUrl;
            }
        }
        else
            url = pg > 1 ? host + "/popular/page/" + pg + "/" : host + "/popular";

        string s = NormalizeSort(sort);
        if (string.IsNullOrEmpty(s)) return url;
        return url + (url.Contains('?') ? "&" : "?") + "sort=" + s;
    }

    // Cac gia tri sort MA SITE CO THAT (do tu <div class="sort">, khong bịa).
    // Whitelist — khong cho phep sort tu query len URL (tranh SSRF/injection).
    public static readonly string[] SortWhitelist = { "week", "month", "views" };
    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    // ========== So trang ==========
    // 1 = chi co 1 trang (khoa nut cuon de app khong keo ra trang rong/loi)
    // 0 = khong doc duoc (giu infinite nhu cu)
    // N = so trang that, lay tu <div class="pagination"> -> /page/N> lon nhat
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        int i = html.IndexOf("class=\"pagination\"", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return 1;                       // khong co pagination: / (trang chu) la 1 trang
        string slice = html.Substring(i, Math.Min(4000, html.Length - i));
        int max = 1;
        foreach (Match m in Regex.Matches(slice, @"/page/(\d+)"))
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

    // ========== LUC (LuluStream): packer -> jwplayer setup sources[0].file ==========
    // Gateway tra thang trang player (*.tnmr.org), link nam trong P.A.C.K.E.R:
    //   jwplayer("...").setup({ sources: [{ file: "https://<host>/hls2/.../master.m3u8?t=..&s=..&e=..&f=.." }] })
    // Token nam trong query -> GIU NGUYEN URL, khong strip.
    public static string LuluMaster(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        string src = Unpack(html) ?? html;
        var m = Regex.Match(src, @"file\s*:\s*[""'](https?://[^""']+?\.m3u8[^""']*)[""']", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string v = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
        return v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : null;
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
    // Trang embed co `<link rel="canonical">` / og:url tro ve dung URL day du
    // (`/e/<id>/<slug>`) — dung lam Referer cho API `get_video`.
    public static string StreamTapeEmbedUrl(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"<link[^>]*rel\s*=\s*""canonical""[^>]*href\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (!m.Success)
            // og:url: StreamTape dung `name=` (khong phai `property=`)
            m = Regex.Match(html, @"<meta[^>]*(?:name|property)\s*=\s*""og:url""[^>]*content\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string u = m.Groups[1].Value.Trim();
        if (u.StartsWith("//")) return "https:" + u;
        if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return u;
        return "https://streamtape.com/" + u.TrimStart('/');
    }

    public static bool HasRobotLink(string html)
        => !string.IsNullOrEmpty(html)
           && html.IndexOf("robotlink", StringComparison.OrdinalIgnoreCase) >= 0;

    // Chi lay URL CUOI cua chuoi 302, KHONG tai ve noi dung: file mp4 cua
    // StreamTape 1GB+ nen dung `-r 0-0` de curl chi yeu 1 byte roi doc
    // `url_effective`. BAT BUOC cho ST: API `get_video` tra 302 sang CDN,
    // `VerifyLinkAsync` (HEAD) se tra 302 chu phai media nen phai follow
    // truoc roi redirect thang URL CDN. (Giong PubJav CurlFinalUrl.)
    static void PrepCurlEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("LD_PRELOAD");
        psi.Environment.Remove("LD_LIBRARY_PATH");
        string curl = "/data/data/com.termux/files/usr/bin/curl";
        psi.FileName = System.IO.File.Exists(curl) ? curl : "curl";
    }

    public static async Task<string> CurlFinalUrl(string url, string referer, int maxTime = 10)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            PrepCurlEnv(psi);
            psi.ArgumentList.Add("-sL");
            psi.ArgumentList.Add("--http1.1");
            psi.ArgumentList.Add("--compressed");
            psi.ArgumentList.Add("--connect-timeout");
            psi.ArgumentList.Add("8");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add(maxTime.ToString());
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add(ChromeUA);
            if (!string.IsNullOrEmpty(referer))
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(referer);
            }
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add("0-0");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("/dev/null");
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add("%{url_effective}");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using var p = Process.Start(psi);
            if (p == null) return null;
            string stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0) return null;
            string eff = stdout.Trim();
            if (eff.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return eff;
            return url;
        }
        catch { return null; }
    }

    // ========== VOE: quyet chuoi redirect TAI SERVER ==========
    // Chrome bi chan `net::ERR_BLOCKED_BY_CLIENT` khi di theo redirect
    // `voe.sx` -> host VOE (2026-10: teresapoliticallearn.com) -- cu the la
    // chi chan CAC NAVIGATION DI THEO REDIRECT, con vao truc tiep host do thi
    // hoan toan binh thuong (da do: goto 200, jwplayer tra master.m3u8 song).
    // Nen quyet chuoi bang curl + GET truoc roi dua URL cuoi cho Chrome mo
    // truc tiep. Tra ve null thi se dung lai tai finalGateway (cu~ troi Chrome
    // tu, thua ca chuoi do chan).
    public static async Task<string> VoeEmbedUrlAsync(string finalGateway, string referer, int timeoutSeconds = 8)
    {
        if (string.IsNullOrEmpty(finalGateway)) return null;
        try
        {
            string url = await CurlFinalUrl(finalGateway, referer, timeoutSeconds);
            if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = finalGateway;
            for (int i = 0; i < 4; i++)
            {
                string html = await GetHtmlAsync(url, referer, timeoutSeconds, null, 1);
                if (string.IsNullOrEmpty(html)) break;
                var m = Regex.Match(html,
                    @"window\.location\.href\s*=\s*['""]([^'""]+)['""]",
                    RegexOptions.IgnoreCase);
                if (!m.Success) break;
                string next = m.Groups[1].Value.Trim();
                if (next.StartsWith("//")) next = "https:" + next;
                else if (!next.StartsWith("http", StringComparison.OrdinalIgnoreCase)) break;
                if (string.Equals(next, url, StringComparison.OrdinalIgnoreCase)) break;
                url = next;
            }
            return url;
        }
        catch { return finalGateway; }
    }

    public static string StreamTapeId(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl)) return null;
        var m = Regex.Match(embedUrl, @"streamtape\.com/e/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // StreamTape trang embed co HAI token trong cung mot `id/expires/ip`:
    //   <div id="robotlink">...&token=AAA</div>   -> token CU, API tra {"status":500}
    //   script gan lai robotlink  ...&token=BBB     -> token MOI, 302 sang CDN .mp4
    // Script chay SAU nen chi token gan lai moi chay duoc -> phai lay match CUOI.
    // Do la ly do ST fail o SupJav (da do 500 o ca 3 host) — KHONG phai IP.
    public static string StreamTapeMp4(string embedHtml)
    {
        if (string.IsNullOrEmpty(embedHtml)) return null;

        // 1) uu tien token do script gan lai (regex PubJav da dung)
        string query = null;
        foreach (Match m in Regex.Matches(embedHtml,
            @"robotlink.{0,4}\.innerHTML\s*=\s*'[^']*get_video\?'\s*\+\s*\('([^']+)'",
            RegexOptions.IgnoreCase))
            query = m.Groups[1].Value;

        // 2) fallback: ghep tay tu bieu thuc `'A' + ('B').substring(...)`
        if (string.IsNullOrEmpty(query))
        {
            foreach (Match m in Regex.Matches(embedHtml,
                @"robotlink'\)\.innerHTML\s*=\s*'([^']*)'\s*\+\s*\('([^']*)'\)((?:\s*\.substring\(\d+\)\s*)*)",
                RegexOptions.IgnoreCase))
            {
                // `.substring(n)` CHI ap cho chuoi trong ngoac (nhom 2), KHONG ap
                // cho ca bieu thuc: '//streamta' + ('xcdpe.com/..').substring(2)
                //                  .substring(1)  ->  //streamtape.com/..
                string tail = m.Groups[2].Value;
                foreach (Match s in Regex.Matches(m.Groups[3].Value ?? "", @"substring\((\d+)\)"))
                {
                    int n;
                    if (int.TryParse(s.Groups[1].Value, out n) && n >= 0 && n <= tail.Length)
                        tail = tail.Substring(n);
                }
                query = m.Groups[1].Value + tail;
            }
        }

        // 3) cuoi cung moi lay div (token cu, thuong chet nhung van giong regex)
        if (string.IsNullOrEmpty(query))
        {
            var div = Regex.Match(embedHtml, @"id\s*=\s*""robotlink""[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            if (div.Success) query = div.Groups[1].Value;
        }

        if (string.IsNullOrEmpty(query)) return null;

        string path = query.Trim();
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

    // ===== Dòng 2 "Sắp xếp" — MỤC ĐÍCH: sort CHO LIST ĐANG MỞ =====
    // Sai lầm cũ (đã sửa 2026-10-06): từng làm dòng 2 thành danh sách xếp hạng
    // TỔNG ("Mới nhất / Phổ biến (Ngày/Tuần/Tháng)") -> mở category nào nó vẫn
    // hiện list tổng, không sort gì cả. Mục đích đúng = **sort đúng cái đang xem**.
    //
    // Pattern chuẩn (EpornerTo.Menu): menu sinh trong CHÍNH request list nên
    // server biết `c` hiện tại -> row 2 GIỮ NGUYÊN `c`, chỉ đổi `sort`.
    // Vẫn đúng 2 tầng (sisi.js không hỗ trợ 3 tầng: mục cấp 2 có submenu ->
    // playlist_url=="submenu" -> màn trống).
    //
    // Sort thật của site — soi <div class="sort"> từng trang (đo 2026-10-06):
    //   /category/* , /tag/* (kể cả /category/maker/*) -> Date (mặc định) | Views
    //   /popular                                       -> Day (mặc định) | Week | Month
    //   / (mới nhất) và ?s= (search)                   -> KHÔNG có sort
    //     (đã đo: `?s=SSIS&sort=views` == `?s=SSIS`, ID y hệt cả trang 1 & 2)
    // Đừng bịa sort cho context site không có.
    public static readonly (string name, string sort)[] TaxSorts =
    {
        ("Theo ngày",     ""),
        ("Theo lượt xem", "views"),
    };
    public static readonly (string name, string sort)[] PopSorts =
    {
        ("Theo ngày",  ""),
        ("Theo tuần",  "week"),
        ("Theo tháng", "month"),
    };

    public static bool IsTaxonomy(string c) =>
        !string.IsNullOrWhiteSpace(c) &&
        (c.StartsWith("category/") || c.StartsWith("tag/"));

    // Tập sort ÁP ĐƯỢC cho context này. null = site không sort được ở đây.
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;   // ?s=  -> không có sort
        if (string.IsNullOrWhiteSpace(c)) return null;
        if (c == "popular") return PopSorts;
        if (IsTaxonomy(c)) return TaxSorts;
        return null;                                           // __latest
    }

    // Gộp sort về đúng tập của context — `sort=week` khi đang ở category vô nghĩa.
    public static string ClampSort(string sort, string search, string c)
    {
        var opts = SortsFor(search, c);
        if (opts == null) return null;
        sort = NormalizeSort(sort);
        if (string.IsNullOrEmpty(sort)) return null;
        foreach (var o in opts) if (o.sort == sort) return sort;
        return null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "ngày"
        : sort == "views" ? "lượt xem"
        : sort == "week" ? "tuần"
        : sort == "month" ? "tháng" : sort;

    // ===== head của menu: phụ thuộc search/sort/c -> dựng lại mỗi request (rẻ) =====
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/supjav";
        var res = new List<MenuItem>(2)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
        var opts = SortsFor(search, c);
        // Context không sort được (`?s=` đo 2026-10-06: sort=views|week ==
        // không sort, cả trang 2 cũng trùng; `c=__latest`) -> KHÔNG hiện dòng 2.
        // Row 2 chết (bấm gì cũng không đổi) còn tệ hơn row 2 vắng.
        if (opts == null || opts.Length == 0) return res;
        var sub = new List<MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
            sub.Add(new MenuItem(name, root + "?c=" + HttpUtility.UrlEncode(c)
                + (string.IsNullOrEmpty(s) ? "" : "&sort=" + s)));
        res.Add(new MenuItem(){ title = $"Sắp xếp: {SortLabel(sort)}", playlist_url = "submenu", submenu = sub });
        return res;
    }

    // ===== base của menu: KHÔNG phụ thuộc search/sort/c -> cache đúng 1 lần =====
    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats, List<(string slug, string name)> makers, List<(string slug, string name)> tags)
    {
        host = host.TrimEnd('/');
        string url(string c, string sort = null) => host + "/supjav?c=" + HttpUtility.UrlEncode(c)
            + (string.IsNullOrEmpty(sort) ? "" : "&sort=" + sort);
        var root = new List<MenuItem>(6)
        {
            // 2 list TOÀN CỤC (đây là "list", không phải "sort" -> không được lẫn vào dòng 2)
            new MenuItem(){ title = "Bảng xếp hạng", playlist_url = "submenu", submenu = new List<MenuItem>(){
                new("Mới nhất", url("__latest")),
                new("Phổ biến",  url("popular")),
            }}
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
                // Cho 2 vong reload cua VOE host: lan 1 luu permanentToken vao
                // localStorage roi reload, lan 2 moi hien player va phat m3u8.
                // 20s de du; khong du thi se tra rong.
                for (int i = 0; i < 25; i++)
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
                    await Task.Delay(800);
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
