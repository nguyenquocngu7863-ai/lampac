using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace JavCt;

public static class JavCtTo
{
    public static string SiteHost = "https://javct.net";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    public static string PlayerApi => SiteHost + "/ajax/player";

    static readonly char[] TaxHidden =
        { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Trim(TaxHidden).Trim();
    }

    // Home = /amateur (301 sang /amateur-xxxxxx/, HttpClient tu follow).
    // Search: /search/<q>. Category: path site (/category/x, /studio/x).
    // Phan trang: /pg-N (khong suffix cung 301 dung cho).
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        string baseUrl;
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Search path cua site: /search/<tu1>-<tu2>. Khoang trang PHAI
            // doi thanh '-' — dung '%20' hay '+' deu 404 (da do: "Yui Hatano"
            // -> /search/Yui-Hatano moi ra ket qua, canonical y vay).
            var parts = search.Trim().Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var enc = new List<string>(parts.Length);
            foreach (var p in parts)
                enc.Add(System.Uri.EscapeDataString(p));

            baseUrl = host + "/search/" + string.Join("-", enc);
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string cat = c.Trim();
            baseUrl = cat.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? cat.TrimEnd('/')
                : host + "/" + cat.Trim().Trim('/');
        }
        else
        {
            baseUrl = host + "/amateur";
        }

        return pg > 1 ? baseUrl.TrimEnd('/') + "/pg-" + pg : baseUrl;
    }

    // Sort cua site (WP) la QUERY `?sort=most-viewed|new-releases`
    // (nav site con tro them `most-liked` nhung do la 404 -> khong dung).
    // QUAN TRONG: bang xep hang khong phan trang duoc — moi `/pg-N` deu tra
    // ve cung trang 1 (do 2026-10-02). Caller phai khoa total_pages=1.
    public static string UriSort(string host, string c, string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return null;

        host = host.TrimEnd('/');
        // home: sort tren chinh trang chu (do 2026-10-06: /?sort=most-viewed
        // DOI). Dung ep ve /amateur (sai list: user dang xem home).
        if (string.IsNullOrWhiteSpace(c))
            return host + "/?sort=" + sort.Trim();

        string cat = c.Trim().Trim('/');
        return host + "/" + cat + "?sort=" + sort.Trim();
    }

    // Sort that cua site — do 2026-10-06 qua jina (HTML + dung regex module):
    //   home (n=84, control YEN): most-viewed/new-releases/most-liked DOI het
    //   /amateur /uncensored /censored: most-viewed + new-releases DOI;
    //                                  most-liked RONG (404 nhu comment cu)
    //   category/*, studio/*, search: ca 3 deu CHET sequence -> khong sort duoc
    public static readonly (string name, string sort)[] SortsHome =
    {
        ("Mới nhất",             ""),
        ("Xem nhiều nhất",       "most-viewed"),
        ("Mới phát hành",        "new-releases"),
        ("Được thích nhiều nhất","most-liked"),
    };

    public static readonly (string name, string sort)[] SortsList =
    {
        ("Mới nhất",       ""),
        ("Xem nhiều nhất", "most-viewed"),
        ("Mới phát hành",  "new-releases"),
    };

    // Tap sort AP DUOC cho context nay. null = site khong sort duoc o day
    // -> KHONG hien dong 2.
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;  // search: sort = no-op
        if (string.IsNullOrWhiteSpace(c)) return SortsHome;   // home: ca 3 deu song

        string cc = c.Trim().Trim('/');
        if (cc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            int i = cc.IndexOf("javct.net", StringComparison.OrdinalIgnoreCase);
            cc = i >= 0 ? cc.Substring(i + "javct.net".Length) : cc;
            cc = cc.Trim('/');
        }
        if (cc == "amateur" || cc == "uncensored" || cc == "censored")
            return SortsList;   // most-liked 404 o day -> khong dua vao

        return null;            // category/*, studio/*, ...: sort = no-op
    }

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in SortsHome) if (s == sort) return s;
        foreach (var (_, s) in SortsList) if (s == sort) return s;
        return null;
    }

    // Gop + kiem tra sort co DUOC phep o context nay khong (khong thi ve null)
    public static string ClampSort(string sort, string search, string c)
    {
        sort = NormalizeSort(sort);
        if (string.IsNullOrEmpty(sort)) return null;
        var opts = SortsFor(search, c);
        if (opts == null) return null;
        foreach (var o in opts) if (o.sort == sort) return sort;
        return null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mới nhất"
        : sort == "most-viewed" ? "xem nhiều nhất"
        : sort == "new-releases" ? "mới phát hành"
        : sort == "most-liked" ? "được thích nhiều nhất" : sort;

    // ===== head cua menu: phu thuoc search/sort/c -> dung lai moi request =====
    // Context KHONG sort duoc (category/*, studio/*, search — da do ca 3 deu
    // CHET sequence) -> KHONG hien dong 2.
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/javct";
        string link(string s)
        {
            string q;
            if (!string.IsNullOrWhiteSpace(search)) q = "search=" + HttpUtility.UrlEncode(search);
            else if (!string.IsNullOrWhiteSpace(c)) q = "c=" + HttpUtility.UrlEncode(c);
            else q = "";
            if (!string.IsNullOrEmpty(s)) q += (q.Length == 0 ? "" : "&") + "sort=" + s;
            return q.Length == 0 ? root : root + "?" + q;
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

        var opts = SortsFor(search, c);
        if (opts == null || opts.Length == 0)
            return res;

        var sub = new List<Shared.Models.SISI.Base.MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
            sub.Add(new(name, link(s)));

        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });
        return res;
    }

    // -----------------------------------------------------------------------------
    // Helpers to parse the detail page
    // -----------------------------------------------------------------------------
    /// <summary>
    /// Extracts the filmId (data‑source), `__pt` and `__pk` values from the
    /// detail page.  Returns (null, null, null) if the page does not contain
    /// the expected data.
    /// </summary>
    public static (string filmId, string pt, string pk) GetDetailTokens(string page)
    {
        if (string.IsNullOrEmpty(page))
            return (null, null, null);

        var ds = Regex.Match(page, @"data-source\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        var pt = Regex.Match(page, @"window\.__pt\s*=\s*[""']([^""']+)[""']");
        var pk = Regex.Match(page, @"window\.__pk\s*=\s*[""']([^""']+)[""']");

        string filmId = ds.Success ? ds.Groups[1].Value : null;
        string ptVal  = pt.Success ? pt.Groups[1].Value : null;
        string pkVal  = pk.Success ? pk.Groups[1].Value : null;

        return (filmId, ptVal, pkVal);
    }

    /// <summary>
    /// Returns the list of available servers from the detail page.  The list
    /// is the same as the one used by the original ResolveAsync – 'DD' is
    /// inserted at the front, and duplicate labels are renumbered.
    /// </summary>
    public static List<(string label, string episode)> GetDetailServers(string page)
    {
        if (string.IsNullOrEmpty(page))
            return null;

        var ds = Regex.Match(page, @"data-source\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!ds.Success)
            return null;

        var servers = new List<(string label, string episode)>();

        foreach (Match b in Regex.Matches(page,
            @"<button\b[^>]*\bdata-id\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</button\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string label = Regex.Replace(b.Groups[2].Value, "<[^>]+>", " ").Trim();
            if (label.Length > 12)
                label = label.Substring(0, 12);
            if (string.IsNullOrEmpty(label))
                label = "S" + (servers.Count + 1);

            string key = label;
            int dup = 2;
            while (servers.Exists(s => s.label == key))
                key = label + " " + (dup++);
            servers.Add((key, b.Groups[1].Value));
        }

        // Player mac dinh (fakeplayer playbox): episode = filmId, khong co data-id.
        // Do live 2026-10-09: DD chet site-wide ("We are updating", ca phim
        // moi lan cu) — F4 (f4s.top) thay the. Giu DD o CUOI (phim nao con
        // song thi van bam duoc), nut that (F4/FL/US/PM) len truoc de app
        // autoplay khong vot phai nut chet.
        for (int i = 0; i < servers.Count; i++)
        {
            if (string.Equals(servers[i].label, "DD", StringComparison.OrdinalIgnoreCase))
            {
                int n = 2;
                string nk = "DD " + (n++);
                while (servers.Exists(s => s.label == nk))
                    nk = "DD " + (n++);
                servers[i] = (nk, servers[i].episode);
            }
        }
        servers.Add(("DD", ds.Groups[1].Value));

        return servers;
    }

    // Card: card__cover > img[data-src|src + alt] + card__title > a[href=/v/slug].
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Block card keo dai toi het span card__category (chua code vang).
        foreach (Match card in Regex.Matches(html, @"card__cover([\s\S]*?card__category[\s\S]*?</span\s*>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string block = card.Groups[1].Value;

            var href = Regex.Match(block, @"<a\b[^>]*\bhref\s*=\s*[""']([^""']+/v/[^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!href.Success)
                continue;

            string uri = href.Groups[1].Value.Trim();
            if (uri.StartsWith("//"))
                uri = "https:" + uri;
            else if (uri.StartsWith("/"))
                uri = SiteHost + uri;
            if (!seen.Add(uri))
                continue;

            string name = "";
            var title = Regex.Match(block,
                @"card__title[^>]*>\s*<a\b[^>]*>(.*?)</a\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (title.Success)
                name = Clean(HttpUtility.HtmlDecode(
                    Regex.Replace(title.Groups[1].Value, "<[^>]+>", " ")));

            if (string.IsNullOrEmpty(name))
            {
                var alt = Regex.Match(block, @"<img\b[^>]*\balt\s*=\s*[""']([^""']{3,200})[""']",
                    RegexOptions.IgnoreCase);
                if (alt.Success)
                    name = Clean(HttpUtility.HtmlDecode(alt.Groups[1].Value));
            }

            if (string.IsNullOrEmpty(name))
                continue;

            // Code vang duoi tieu de (card__category): "FC2PPV-4981624".
            // De code TRUOC tieu de: app cat cuoi khi dai, code sau se mat.
            var code = Regex.Match(block,
                @"card__category[^>]*>\s*<a\b[^>]*>([^<]{2,40})</a\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (code.Success)
            {
                string c = Clean(HttpUtility.HtmlDecode(code.Groups[1].Value));
                if (c.Length > 0 && name.IndexOf(c, StringComparison.OrdinalIgnoreCase) < 0)
                    name = "[" + c + "] " + name;
            }

            string poster = "";
            var img = Regex.Match(block,
                @"<img\b[^>]*\bdata-src\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!img.Success)
                img = Regex.Match(block, @"<img\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""']",
                    RegexOptions.IgnoreCase);
            if (img.Success)
            {
                poster = img.Groups[1].Value.Trim();
                if (poster.StartsWith("//"))
                    poster = "https:" + poster;
                if (poster.Contains("default-cover"))
                    poster = "";
            }

            list.Add(new PlaylistItem()
            {
                video = "javct/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "javct",
                    href = uri,
                    image = poster
                }
            });
        }

        return list;
    }

    // /categories: <a href=".../category/slug">. /studios: /studio/slug.
    // Moi muc studio co <span>(so phim)</span> ke sau — sap theo so phim
    // giam dan (1479 hang, menu chi lay top). Category khong co so thi giu
    // nguyen thu tu trang.
    public static List<(string name, string path)> Taxonomies(
        string html, string kind, int top = int.MaxValue)
    {
        var res = new List<(string name, string path, int count)>();
        if (string.IsNullOrEmpty(html))
            return new List<(string, string)>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string prefix = kind == "studio" ? "/studio/" : "/category/";

        foreach (Match m in Regex.Matches(html,
            "<a\\s[^>]*href=\"[^\"]*?" + Regex.Escape(prefix) + "([^\"/]+)\"[^>]*>([^<]{1,60})</a\\s*>",
            RegexOptions.IgnoreCase))
        {
            string slug = Clean(m.Groups[1].Value);
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[2].Value));
            if (slug.Length == 0 || name.Length == 0 || !seen.Add(slug))
                continue;

            int count = 0;
            string tail = html.Substring(m.Index + m.Length,
                Math.Min(200, html.Length - m.Index - m.Length));
            var cm = Regex.Match(tail, @"\(\s*(\d+)\s*\)");
            if (cm.Success)
                int.TryParse(cm.Groups[1].Value, out count);

            res.Add((name.Replace(':', '-'), (kind == "studio" ? "studio/" : "category/") + slug, count));
        }

        if (res.Exists(r => r.count > 0))
            res.Sort((x, y) => y.count.CompareTo(x.count));

        if (res.Count > top)
            res.RemoveRange(top, res.Count - top);

        var out_ = new List<(string, string)>(res.Count);
        foreach (var r in res)
            out_.Add((r.name, r.path));

        return out_;
    }

    // POST /ajax/player {episode, filmId, pt} -> {player_enc xor __pk | player}.
    // xorDecrypt JS: base64 -> XOR voi key lap lai.
    public static string XorDecrypt(string encoded, string key)
    {
        if (string.IsNullOrEmpty(encoded))
            return "";

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(encoded);
        }
        catch
        {
            return "";
        }

        if (string.IsNullOrEmpty(key))
            return System.Text.Encoding.UTF8.GetString(decoded);

        var chars = new char[decoded.Length];
        for (int i = 0; i < decoded.Length; i++)
            chars[i] = (char)(decoded[i] ^ key[i % key.Length]);

        return new string(chars);
    }

    public static string EmbedUrl(string playerHtml)
    {
        if (string.IsNullOrEmpty(playerHtml))
            return null;

        var m = Regex.Match(playerHtml, @"<iframe\b[^>]+\bsrc\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string src = m.Groups[1].Value.Trim();
        if (src.StartsWith("//"))
            src = "https:" + src;

        return src.StartsWith("http") ? src : null;
    }

    // DoodStream (F5 skill lampac-deobfuscate): embed -> pass_md5 -> mp4.
    // Token DUNG 1 LAN / het han nhanh: phai GET embed roi goi API lien
    // trong cung resolve, khong cache token. Tra ve (url, referer host API).
    public static async Task<(string url, string referer)> DoodSourceAsync(
        string embedUrl, int timeoutSeconds = 10)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return (null, null);

        string body;
        try
        {
            body = await Http.Get(
                embedUrl,
                timeoutSeconds: timeoutSeconds,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", SiteHost + "/")));
        }
        catch
        {
            return (null, null);
        }

        var m = Regex.Match(body ?? "", @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
        if (!m.Success)
            return (null, null);

        string apiHost = SiteHost;
        try
        {
            var u = new System.Uri(embedUrl);
            int at = u.AbsoluteUri.IndexOf("/e/", StringComparison.OrdinalIgnoreCase);
            if (at > 8)
                apiHost = u.AbsoluteUri.Substring(0, at);
        }
        catch { }

        string src;
        try
        {
            src = await Http.Get(
                apiHost + "/" + m.Groups[1].Value + "?referer=javct.net",
                timeoutSeconds: timeoutSeconds,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", apiHost + "/")));
        }
        catch
        {
            return (null, null);
        }

        src = (src ?? "").Trim();
        if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        string token = m.Groups[1].Value.Split('/').Last();
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return (src + (src.Contains('?') ? "&" : "?")
            + $"token={token}&expiry={expiry}", apiHost + "/");
    }

    // StreamHG clone (ryderjet...): packer base36 giai ra
    // `links={"hls4"|"hls3"|"hls2"}`, uu tien hls4>hls3>hls2 (F1).
    public static string Unpack(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html,
            @"eval\(function\(p,a,c,k,e,d\)\{[^}]*\}\('([\s\S]*?)',(\d+),(\d+),'([\s\S]*?)'\.split\('\|'\)\)",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string p = m.Groups[1].Value;
        if (!int.TryParse(m.Groups[2].Value, out int a) ||
            !int.TryParse(m.Groups[3].Value, out int c))
            return null;

        var k = m.Groups[4].Value.Split('|');

        for (int i = c - 1; i >= 0; i--)
        {
            if (i >= k.Length || string.IsNullOrEmpty(k[i]))
                continue;
            p = Regex.Replace(p, @"\b" + ToBase(i, a) + @"\b", _ => k[i]);
        }

        return p;
    }

    static string ToBase(int value, int b)
    {
        if (value == 0)
            return "0";
        var sb = new System.Text.StringBuilder();
        while (value > 0)
        {
            int d = value % b;
            sb.Insert(0, (char)(d < 10 ? '0' + d : 'a' + d - 10));
            value /= b;
        }
        return sb.ToString();
    }

    public static string StreamHgMaster(string embedHtml, string referer)
    {
        string plain = Unpack(embedHtml);
        if (string.IsNullOrEmpty(plain))
            return null;

        // uu tien hls3 (master.txt) > hls2 (cdn token) > hls4 (/stream/ can cookie).
        // hls4 khong token, phu thuoc cookie + IP -> hay 403/timeout.
        foreach (var key in new[] { "hls3", "hls2", "hls4" })
        {
            var m = Regex.Match(plain, "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);
            if (!m.Success)
                continue;

            string url = m.Groups[1].Value.Replace("\\/", "/");
            if (url.StartsWith("//"))
                url = "https:" + url;
            if (url.StartsWith("http"))
                return url;
        }

        return null;
    }

    // UPN/PP (F2 skill lampac-deobfuscate): iframe `player.upn.one/#id`
    // -> GET /api/v1/video?id= -> hex -> AES-128-CBC -> JSON 17 khoa,
    // lay cfNative (master signed, 200 khong can cookie).
    public const string UpnKey = "kiemtienmua911ca";
    public const string UpnIv = "1234567890oiuytr";

    public static string UpnDecrypt(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        hex = hex.Trim().Trim('"');
        if (hex.Length % 2 != 0)
            return null;

        var data = new byte[hex.Length / 2];
        for (int i = 0; i < data.Length; i++)
        {
            if (!System.Uri.IsHexDigit(hex[i * 2]) || !System.Uri.IsHexDigit(hex[i * 2 + 1]))
                return null;
            data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        try
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.KeySize = 128;
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;
            aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
            aes.Key = System.Text.Encoding.UTF8.GetBytes(UpnKey);
            aes.IV = System.Text.Encoding.UTF8.GetBytes(UpnIv);
            using var tr = aes.CreateDecryptor();
            return System.Text.Encoding.UTF8.GetString(
                tr.TransformFinalBlock(data, 0, data.Length));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(string url, string referer)> UpnSourceAsync(
        string embedUrl, int timeoutSeconds = 10)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return (null, null);

        int hash = embedUrl.IndexOf('#');
        if (hash < 0)
            return (null, null);

        string id = embedUrl.Substring(hash + 1).Trim().Trim('/');
        int amp = id.IndexOf('&');
        if (amp >= 0)
            id = id.Substring(0, amp);
        if (id.Length < 2)
            return (null, null);

        string host;
        try
        {
            host = new System.Uri(embedUrl).GetLeftPart(System.UriPartial.Authority);
        }
        catch
        {
            return (null, null);
        }

        string hex;
        try
        {
            hex = await Http.Get(
                host + "/api/v1/video?id=" + System.Uri.EscapeDataString(id),
                timeoutSeconds: timeoutSeconds,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", host + "/")));
        }
        catch
        {
            return (null, null);
        }

        string json = UpnDecrypt(hex);
        if (string.IsNullOrEmpty(json))
            return (null, null);

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("cfNative", out var cf) &&
                cf.ValueKind == System.Text.Json.JsonValueKind.String &&
                cf.GetString().StartsWith("http"))
                return (cf.GetString(), host + "/");
        }
        catch { }

        return (null, null);
    }

    // Playmate (F4 skill lampac-deobfuscate): iframe `playmate.to/embed/id`
    // -> POST /api/s {"c":id,"d":"desktop"} -> field "sx" = master .txt.
    public static async Task<string> PlaymateSourceAsync(
        string embedUrl, int timeoutSeconds = 10)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return null;

        string id;
        try
        {
            id = new System.Uri(embedUrl).AbsolutePath.Trim('/').Split('/').Last();
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(id))
            return null;

        string host;
        try
        {
            host = new System.Uri(embedUrl).GetLeftPart(System.UriPartial.Authority);
        }
        catch
        {
            return null;
        }

        try
        {
            string json = await Http.Post(
                host + "/api/s",
                new System.Net.Http.StringContent(
                    "{\"c\":\"" + id + "\",\"d\":\"desktop\"}",
                    System.Text.Encoding.UTF8, "application/json"),
                timeoutSeconds: timeoutSeconds,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", host + "/")));

            var m = Regex.Match(json ?? "", "\"sx\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;

            string src = m.Groups[1].Value.Replace("\\/", "/");
            return src.StartsWith("http") ? src : null;
        }
        catch
        {
            return null;
        }
    }

    // F4 (f4s.top/f4stream, thay DD tu ~2026-10): iframe `f4s.top/e/id`
    // -> GET embed lay data-api `/api/play/<uuid>` -> GET api (host = embed
    // authority) -> JSON {url:"/v/<token>", type:"hls", expires_in:300}.
    // Tra master m3u8 (302 sang CDN). Token song 5 phut -> resolve o /video
    // (lazy), cache pack NGAN (4 phut).
    public static async Task<string> F4SourceAsync(
        string embedUrl, int timeoutSeconds = 10)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return null;

        string host;
        try
        {
            host = new System.Uri(embedUrl).GetLeftPart(System.UriPartial.Authority);
        }
        catch
        {
            return null;
        }

        string embedHtml = null;
        try
        {
            embedHtml = await Http.Get(
                embedUrl,
                timeoutSeconds: timeoutSeconds,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", SiteHost + "/")));
        }
        catch { }
        if (string.IsNullOrEmpty(embedHtml))
            return null;

        var am = Regex.Match(embedHtml, @"data-api\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!am.Success)
            return null;

        string api = am.Groups[1].Value.Trim().Replace("&quot;", "").Replace("&#x27;", "");
        if (api.StartsWith("/"))
            api = host + api;
        else if (!api.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        string json;
        try
        {
            json = await Http.Get(
                api,
                timeoutSeconds: timeoutSeconds,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", embedUrl)));
        }
        catch
        {
            return null;
        }

        var um = Regex.Match(json ?? "", @"""url""\s*:\s*""([^""]+)""",
            RegexOptions.IgnoreCase);
        if (!um.Success)
            return null;

        string src = um.Groups[1].Value.Replace("\\/", "/");
        if (src.StartsWith("/"))
            src = host + src;
        return src.StartsWith("http") ? src : null;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host, IReadOnlyList<(string name, string path)> cats = null,
        IReadOnlyList<(string name, string path)> studios = null)
    {
        // Lay HET, khong cat top N — chi CHIA nho de moi submenu <= 300.
        var genreMenu = new List<Shared.Models.SISI.Base.MenuItem>();
        if (cats != null)
        {
            foreach (var c in cats)
                genreMenu.Add(new(c.name, host + "/javct?c=" + c.path));
        }

        // 1479 hang -> 5 dong `Hang phim #–B ... M–P` thay vi 1 submenu
        // 1479 muc (client SISI chi 1 tang nen KHONG tach them tang).
        var studioRows = DirBuckets(host, "Hãng phim", studios);

        // base: KHONG phu thuoc search/sort/c -> cache dung 1 lan.
        // Dong 1 (Tim kiem) + dong 2 (Sap xep) do MenuHead dung rieng moi
        // request — khong cache cung taxonomy.
        var root = new List<Shared.Models.SISI.Base.MenuItem>();

        if (genreMenu.Count > 0)
            root.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genreMenu
            });

        root.AddRange(studioRows);

        return root;
    }

    // MOI NHOM = 1 muc TANG 1, submenu la cac muc trong nhom do.
    // Client SISI chi hien MOT tang submenu nen KHONG tach
    // `Hang phim -> A -> Madonna` (3 tang = muc chet).
    public const int MaxPerBucket = 300;

    public static List<Shared.Models.SISI.Base.MenuItem> DirBuckets(
        string host, string title,
        IReadOnlyList<(string name, string path)> all, int maxPer = MaxPerBucket)
    {
        var res = new List<Shared.Models.SISI.Base.MenuItem>();
        if (all == null || all.Count == 0)
            return res;

        // Gom theo KY TU DAU cua ten. Ky tu khong phai A-Z (so, `#`)
        // gom vao nhom `#` — khong in ky tu la ra ten muc (client tach
        // subtitle bang `:` nen ky ten la se bi cat).
        var groups = new Dictionary<char, List<(string name, string path)>>();

        foreach (var it in all)
        {
            if (string.IsNullOrWhiteSpace(it.name))
                continue;

            char c = char.ToUpperInvariant(it.name.Trim()[0]);
            if (c < 'A' || c > 'Z')
                c = '#';

            if (!groups.TryGetValue(c, out var lst))
            {
                lst = new List<(string, string)>();
                groups[c] = lst;
            }

            lst.Add(it);
        }

        // `#` truoc, roi so 0-9, roi A-Z.
        var keys = groups.Keys.OrderBy(CharRank).ToList();

        // Flatten roi cat chunk. Nhom vuot `maxPer` se bi cat giua: phan
        // du lot sang chunk sau, KHONG bo muc nao.
        var flat = new List<(char key, string name, string path)>(all.Count);
        foreach (var k in keys)
            foreach (var it in groups[k])
                flat.Add((k, it.name, it.path));

        var from = new List<char>();
        var to = new List<char>();
        var subs = new List<List<Shared.Models.SISI.Base.MenuItem>>();

        for (int i = 0; i < flat.Count; i += maxPer)
        {
            int n = Math.Min(maxPer, flat.Count - i);

            from.Add(flat[i].key);
            to.Add(flat[i + n - 1].key);
            subs.Add(flat.Skip(i).Take(n).Select(x =>
                new Shared.Models.SISI.Base.MenuItem(
                    x.name, host + "/javct?c=" + x.path)).ToList());
        }

        // Chunk cat giua mot chu cai (from == to) thi them `(1/2)`,`(2/2)`
        // — khong thi menu hien hai dong trung ten.
        for (int k = 0; k < from.Count; k++)
        {
            string name = title + " " + from[k]
                + (to[k] == from[k] ? "" : "–" + to[k]);

            if (to[k] == from[k])
            {
                int parts = from.Count(c => c == from[k]);
                if (parts > 1)
                {
                    int nth = 1;
                    for (int q = 0; q < k; q++)
                        if (from[q] == from[k])
                            nth++;

                    name += $" ({nth}/{parts})";
                }
            }

            res.Add(new Shared.Models.SISI.Base.MenuItem(name, "submenu")
            { submenu = subs[k] });
        }

        return res;
    }

    // `#` < 0 < 1 < ... < 9 < A < ... < Z. So PHAI xep TANG so (cua chu
    // so) chu khong phai tat ca so cung hang — neu khong ten muc ra
    // `Hang phim 9–7` theo thu tu site.
    static int CharRank(char c)
    {
        if (c == '#')
            return 0;
        if (c >= '0' && c <= '9')
            return 1 + (c - '0');
        if (c >= 'A' && c <= 'Z')
            return 20 + (c - 'A');
        return 100;
    }
}
