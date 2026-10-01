using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace SexTb;

public static class SexTbTo
{
    public static string SiteHost = "https://sextb.net";

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
            baseUrl = host + "/search/" + HttpUtility.UrlEncode(search.Trim());
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string cat = c.Trim();
            baseUrl = cat.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? cat.TrimEnd('/')
                : host + "/" + cat.Trim().Trim('/');
        }
        else
        {
            // Trang chu khong co phim (chi nav) -> home = censored moi nhat.
            baseUrl = host + "/genre/censored";
        }

        return pg > 1 ? baseUrl.TrimEnd('/') + "/pg-" + pg : baseUrl;
    }

    // Card: card__cover > img[data-src|src + alt] + card__title > a[href=/v/slug].
    // Card: <a href="/slug"><img.tray-item-thumbnail data-src + alt>
    // + <div.tray-item-title>ten</div></a>. Slug ngan (/hoip-005), khong
    // prefix — loai nav (/genre, /studio, /actress, /user, /search).
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match card in Regex.Matches(html,
            "<a\\s[^>]*href=\"(/[^\"?#]+)\"[^>]*>([\\s\\S]{0,2500}?)</a\\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string href = card.Groups[1].Value.Trim();
            if (href.Length < 4 || href.Contains("tray-item-thumbnail"))
                continue;
            if (href.StartsWith("/genre/") || href.StartsWith("/studio/") ||
                href.StartsWith("/actress/") || href.StartsWith("/user/") ||
                href.StartsWith("/search") || href.StartsWith("/feed") ||
                href.StartsWith("/images/") || href == "/")
                continue;

            string block = card.Groups[2].Value;
            if (block.IndexOf("tray-item-thumbnail", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            string uri = href.StartsWith("http") ? href : SiteHost + href;
            if (!seen.Add(uri))
                continue;

            string name = "";
            var title = Regex.Match(block,
                @"tray-item-title[^>]*>(.*?)</div\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (title.Success)
                name = Clean(HttpUtility.HtmlDecode(
                    Regex.Replace(title.Groups[1].Value, "<[^>]+>", " ")));

            if (string.IsNullOrEmpty(name))
            {
                var alt = Regex.Match(block, @"<img\b[^>]*\balt\s*=\s*[""']([^""']{3,300})[""']",
                    RegexOptions.IgnoreCase);
                if (alt.Success)
                    name = Clean(HttpUtility.HtmlDecode(alt.Groups[1].Value));
            }

            if (string.IsNullOrEmpty(name))
                continue;

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
                video = "sextb/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "sextb",
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
    // Taxonomy: genre tu nav home (/genre/x), studio tu /list-studios
    // (168, 1 trang), label tu /list-labels (150, 1 trang). Prefix rieng
    // tung loai; ten label/studio lay tu text, genre suy tu slug.
    public static List<(string name, string path)> Taxonomies(
        string html, string kind, int top = int.MaxValue)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string prefix = kind == "studio" ? "/studio/"
            : kind == "label" ? "/label/" : "/genre/";

        foreach (Match m in Regex.Matches(html,
            "<a\\s[^>]*href=\"(" + Regex.Escape(prefix) + "[^\"?#]+)\"[^>]*>([\\s\\S]{1,120}?)</a\\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = Clean(m.Groups[1].Value);
            string name = Clean(HttpUtility.HtmlDecode(
                Regex.Replace(m.Groups[2].Value, "<[^>]+>", " ")));
            name = Regex.Replace(name, @"\s+", " ").Trim();

            if (slug.Length == 0 || !seen.Add(slug))
                continue;

            if (string.IsNullOrEmpty(name))
                name = PrettySlug(slug.Split('/').Last());

            if (kind == "genre")
                name = PrettySlug(slug.Split('/').Last());

            res.Add((name.Replace(':', '-'), slug.Trim('/')));
        }

        if (res.Count > top)
            res.RemoveRange(top, res.Count - top);

        return res;
    }

    static string PrettySlug(string slug)
    {
        // bo duoi hash: amateur-9yvovjqr -> amateur; censored giu nguyen.
        string s = Regex.Replace(slug, @"-[a-z0-9]{6,}$", "");
        s = s.Replace('-', ' ').Trim();
        if (s.Length == 0)
            return slug;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
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
                apiHost + "/" + m.Groups[1].Value + "?referer=sextb.net",
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

        foreach (var key in new[] { "hls4", "hls3", "hls2" })
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

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host, IReadOnlyList<(string name, string path)> cats = null,
        IReadOnlyList<(string name, string path)> studios = null,
        IReadOnlyList<(string name, string path)> labels = null)
    {
        var genreMenu = new List<Shared.Models.SISI.Base.MenuItem>();
        if (cats != null)
        {
            foreach (var c in cats.Take(300))
                genreMenu.Add(new(c.name, host + "/sextb?c=" + c.path));
        }

        var labelMenu = new List<Shared.Models.SISI.Base.MenuItem>();
        if (labels != null)
        {
            foreach (var l in labels.Take(300))
                labelMenu.Add(new(l.name, host + "/sextb?c=" + l.path));
        }

        var studioMenu = new List<Shared.Models.SISI.Base.MenuItem>();
        if (studios != null)
        {
            foreach (var s in studios)
                studioMenu.Add(new(s.name, host + "/sextb?c=" + s.path));
        }

        var root = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/sextb"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/sextb"
            },
        };

        if (genreMenu.Count > 0)
            root.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genreMenu
            });

        if (labelMenu.Count > 0)
            root.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Nhãn",
                playlist_url = "submenu",
                submenu = labelMenu
            });

        if (studioMenu.Count > 0)
            root.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Hãng phim",
                playlist_url = "submenu",
                submenu = studioMenu
            });

        return root;
    }
}
