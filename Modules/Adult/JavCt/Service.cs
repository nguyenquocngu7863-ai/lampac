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
            baseUrl = host + "/amateur";
        }

        return pg > 1 ? baseUrl.TrimEnd('/') + "/pg-" + pg : baseUrl;
    }

    // Card: card__cover > img[data-src|src + alt] + card__title > a[href=/v/slug].
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match card in Regex.Matches(html, @"card__cover(.*?)card__content",
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
    public static List<(string name, string path)> Taxonomies(string html, string kind)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

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

            res.Add((name.Replace(':', '-'), (kind == "studio" ? "studio/" : "category/") + slug));
        }

        return res;
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

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host, IReadOnlyList<(string name, string path)> cats = null,
        IReadOnlyList<(string name, string path)> studios = null)
    {
        var genreMenu = new List<Shared.Models.SISI.Base.MenuItem>();
        if (cats != null)
        {
            foreach (var c in cats.Take(300))
                genreMenu.Add(new(c.name, host + "/javct?c=" + c.path));
        }

        var studioMenu = new List<Shared.Models.SISI.Base.MenuItem>();
        if (studios != null)
        {
            // 1479 studio 1 trang — chi lay 100 dau, hoi user neu muon full.
            foreach (var s in studios.Take(100))
                studioMenu.Add(new(s.name, host + "/javct?c=" + s.path));
        }

        var root = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/javct"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javct"
            },
        };

        if (genreMenu.Count > 0)
            root.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genreMenu
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
