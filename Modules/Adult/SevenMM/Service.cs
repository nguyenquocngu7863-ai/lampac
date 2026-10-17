using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace SevenMM;

public static class SevenMMTo
{
    public const string SiteHost = "https://7mmtv.sx";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";

    public static string Lang(string host)
        => (string.IsNullOrWhiteSpace(host) ? SiteHost : host).TrimEnd('/') + "/en";

    // c = "censored_list/all" | "censored_category/1/Variety" | "uncensored_makersr/37/FC2" ...
    public static string ListUrl(string host, string c, int pg)
    {
        if (pg < 1) pg = 1;
        c = (c ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(c))
            c = "censored_list/all";
        return Lang(host) + "/" + c + "/" + pg + ".html";
    }

    public static string SearchUrl(string host)
        => Lang(host) + "/searchform_search/all/index.html";

    public static string MenuUrl(string host, string c)
        => $"{host}/sevenmm?c={HttpUtility.UrlEncode(c)}";

    public static string StripCount(string title)
        => string.IsNullOrEmpty(title) ? title : Regex.Replace(title, @"\s*\(\d+\)\s*$", "").Trim();

    static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        s = HttpUtility.HtmlDecode(s).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    static string Match1(string s, string pattern)
    {
        var m = Regex.Match(s, pattern, RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    #region playlist
    // Card markup (moi trang list/genre/maker deu giong nhau):
    // <div class='... col-item'>
    //   <figure class='video-preview'><a href=".../_content/<id>/<CODE>.html">
    //     <img class='lazyload' data-src='thumb.webp' alt='TITLE'>
    //     <video ... data-src='preview.mp4'>
    //   <h3 class='video-title'><a href="...">TITLE</a></h3>
    //   <div class='video-channel'>CHANNEL</div> ... <span class='small text-muted'> DATE </span>
    public static List<PlaylistItem> Playlist(string videoRoute, string html)
    {
        var res = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return res;

        foreach (string part in html.Split("col-item"))
        {
            var hm = Regex.Match(part, @"href=""(https?://[^""]*_content/[^""]+?\.html)""");
            if (!hm.Success) continue;
            string href = hm.Groups[1].Value;

            string name = Match1(part, @"<h3 class='video-title'>\s*<a[^>]*>([^<]+)</a>");
            if (string.IsNullOrEmpty(name))
                name = Match1(part, @"<img[^>]*alt='([^']*)'");
            name = Clean(name);
            if (string.IsNullOrEmpty(name))
                name = Regex.Replace(href.Split('/')[^1], @"\.html$", "", RegexOptions.IgnoreCase);

            string pic = Match1(part, @"<img[^>]*data-src='([^']+)'");
            string preview = Match1(part, @"<video[^>]*data-src='([^']+\.mp4)'");
            string time = Match1(part, @"<span class='small text-muted'>\s*([^<]+)</span>");
            string channel = Match1(part, @"class='video-channel'>([^<]*)</div>");

            res.Add(new PlaylistItem()
            {
                name = name,
                video = $"{videoRoute}?uri={HttpUtility.UrlEncode(href)}",
                picture = pic,
                preview = preview,
                time = Clean(time),
                quality = Clean(channel),
                json = true,
                bookmark = new Bookmark()
                {
                    site = "7mmtv",
                    href = href,
                    image = pic
                }
            });
        }

        return res;
    }
    #endregion

    #region stream (vidhide)
    // Trang detail co JSON-LD:  "contentUrl":"https://<vidhide-host>/v/<id>"
    // (nut "Agree TOS" chi la overlay JS — link that nam san trong source).
    public static string EmbedUrl(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        string u = Match1(html, @"""contentUrl""\s*:\s*""(https?://[^""]+)""");
        if (string.IsNullOrEmpty(u))
            u = Match1(html, @"""embedUrl""\s*:\s*""(https?://[^""]+)""");

        if (string.IsNullOrEmpty(u)) return null;
        return u.Replace("\\/", "/").Trim();
    }

    // SP / SV3: embed //7mmtv.sx/assets/js/play/play.php?id=<token> KHONG dung PACKER.
    // No nhung thang master truc tiep trong HTML:
    //   sources = [ {src: 'https://...m3u8',type: 'application/x-mpegURL', size: 480},
    //              {src: 'https://...m3u8',type: 'application/x-mpegURL', size: 720} ];
    // Tra ve (url, size) — lon truoc de app chon ban 720p.
    public static List<(string url, int size)> SpSources(string html)
    {
        var res = new List<(string url, int size)>();
        if (string.IsNullOrEmpty(html)) return res;

        // {src: '<url>',type: 'application/x-mpegURL', size: <n>}
        foreach (Match m in Regex.Matches(html,
            @"\{\s*src\s*:\s*'(?<u>[^']+?)'\s*,\s*type\s*:\s*'application/x-mpegURL'\s*,\s*size\s*:\s*(?<s>\d+)"))
        {
            string u = UnescapeJs(m.Groups["u"].Value);
            if (string.IsNullOrEmpty(u) || !u.Contains("http")) continue;

            int size = int.TryParse(m.Groups["s"].Value, out int sz) ? sz : 0;
            if (res.Any(x => x.url == u)) continue;
            res.Add((u, size));
        }

        if (res.Count > 1)
            res.Sort((a, b) => b.size.CompareTo(a.size));

        return res;
    }

    // "\/" -> "/", "\u0026" -> "&", ...
    public static string UnescapeJs(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return Regex.Replace(s, @"\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString())
                     .Replace("\\/", "/")
                     .Replace("\\u0026", "&")
                     .Replace("&amp;", "&");
    }

    // Embed vidhide/streamhide -> P.A.C.K.E.R -> var links={"hls3":"...","hls2":"..."}
    // baseHost: host dung de ghep link "hls4" dang relative ("/stream/...").
    public static List<string> Masters(string embedHtml, string baseHost = null)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(embedHtml)) return res;

        string plain = Unpack(embedHtml) ?? embedHtml;

        var lm = Regex.Match(plain, @"links\s*=\s*\{([\s\S]*?)\}");
        if (!lm.Success) return res;

        foreach (Match kv in Regex.Matches(lm.Groups[1].Value, @"""(hls\d)""\s*:\s*""([^""]+)"""))
        {
            string u = kv.Groups[2].Value.Replace("\\/", "/").Trim();

            // hls4 cua StreamHG la path relative ("/stream/<token>/...")
            if (u.StartsWith("/") && !string.IsNullOrEmpty(baseHost))
                u = baseHost.TrimEnd('/') + u;

            if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !res.Contains(u))
                res.Add(u);
        }
        return res;
    }

    // P.A.C.K.E.R base36 (skill lampac-deobfuscate F1)
    public static string Unpack(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var m = Regex.Match(html,
            @"eval\(function\(p,a,c,k,e,[a-z]\)[\s\S]*?\}\('([\s\S]*?)',(\d+),(\d+),'([\s\S]*?)'\.split\('\|'\)",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        string p = m.Groups[1].Value;
        if (!int.TryParse(m.Groups[2].Value, out int a) || !int.TryParse(m.Groups[3].Value, out int c))
            return null;

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
        while (value > 0)
        {
            int d = value % b;
            sb.Insert(0, (char)(d < 10 ? '0' + d : 'a' + d - 10));
            value /= b;
        }
        return sb.ToString();
    }
    #endregion

    #region enc (SW / SP)
    // Trang detail nhung mvarr:
    //   mvarr['37_1']=[[iframeId, ENC, iframeTag, 'https://mmsi02.com/e/', '', '></iframe>', '<a download...>']];
    //   mvarr['38_1']=[[iframeId, ENC, iframeTag, 'https://mmvh02.com/v/',  ...]];
    //   mvarr['42_1']=[[iframeId, ENC, iframeTag, '//7mmtv.sx/assets/js/play/play.php?id=', ...]];
    // Map: 37_1=SW (StreamHide), 38_1=VH (VidHide), 42_1=SP.
    // Site tra MOT dau hang mvarr co the 5 hoac 7 phan tu, va THU TU KHONG CO DINH:
//   7 phan: [id, ENC, '<iframe …>', PREFIX, '', '></iframe>', '<a download>']
//   5 phan: [id, ENC, '<iframe …>', PREFIX, '']        (mot bien the)
// Vi vay KHONG duoc lay theo chi so co dinh. Dung regex bat tat ca chuoi trong
// ngoac vu, roi dinh danh theo NOI DUNG:
//   - PREFIX : bat dau bang http:// / https:// / //
//   - ENC    : chi [0-9a-z] va co dau phan cach (ky tu key+97) ben trong
public static Dictionary<string, (string enc, string prefix)> Mvarr(string html)
    {
        var res = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(html)) return res;

        foreach (Match m in Regex.Matches(html,
            @"mvarr\['(\d+_\d+)'\]\s*=\s*\[\s*\[\s*'([^']*)'\s*,\s*'([^']*)'\s*,\s*'([^']*)'\s*,\s*'([^']*)'"))
        {
            string k = m.Groups[1].Value;
            if (res.ContainsKey(k)) continue;
            res[k] = (m.Groups[3].Value, m.Groups[5].Value);
        }
        return res;
    }

    // Inline vars tren trang detail (ten bien do chinh site dat, on dinh qua cac phim):
    //   hadeedg252=17;  -> xor cua decr_sun
    //   hcdeedg252=17;  -> "key" cua decr_sun (chia chuoi)
    //   var argdeqweqweqwe = '687b3731437aa483'; -> AES key  (UTF8, 16 byte)  [DOI THEO TRANG]
    //   var hdddedg252     = '22fcd69f8ae5c786'; -> AES IV   (UTF8, 16 byte)  [DOI THEO TRANG]
    public sealed class EncVars
    {
        public int key;
        public int xor;
        public string aesKey;
        public string aesIv;

        public bool ok => key > 1 && key <= 36
                          && !string.IsNullOrEmpty(aesKey) && !string.IsNullOrEmpty(aesIv)
                          && (aesKey.Length == 16 || aesKey.Length == 24 || aesKey.Length == 32)
                          && aesIv.Length == 16;
    }

    public static EncVars ReadEncVars(string html)
    {
        var v = new EncVars();
        if (string.IsNullOrEmpty(html)) return v;

        v.key = LastInt(html, @"hcdeedg252\s*=\s*(\d+)");
        v.xor = LastInt(html, @"hadeedg252\s*=\s*(\d+)");
        v.aesKey = LastStr(html, @"argdeqweqweqwe\s*=\s*'([^']*)'");
        v.aesIv = LastStr(html, @"hdddedg252\s*=\s*'([^']*)'");
        return v;
    }

    static int LastInt(string html, string pattern)
    {
        int last = 0;
        foreach (Match m in Regex.Matches(html, pattern))
            if (int.TryParse(m.Groups[1].Value, out int n)) last = n;
        return last;
    }

    static string LastStr(string html, string pattern)
    {
        string last = null;
        foreach (Match m in Regex.Matches(html, pattern))
            last = m.Groups[1].Value;
        return last;
    }

    // Buoc 1 (hsdfdg252 trong content_mv_protect_ofb.js):
    //   key = key <= 25 ? key : key % 25;
    //   sep = String.fromCharCode(key + 97);
    //   for each p in enc.split(sep): chr((parseInt(p, key) ^ xor) & 0xff)
    // => chuoi base64.
    public static string DecrSun(string enc, int key, int xor)
    {
        if (string.IsNullOrEmpty(enc) || key < 2 || key > 36) return null;

        if (key > 25) key %= 25;
        if (key < 2) return null;

        char sep = (char)(key + 97);
        var sb = new StringBuilder(enc.Length);

        foreach (string p in enc.Split(sep))
        {
            if (string.IsNullOrEmpty(p)) continue;

            // KHONG dung Convert.ToInt32(p, key): .NET chi ho tro base 2/8/10/16,
            // con site dung base bat ky 2..25 (key doi theo tung phim) -> "Invalid Base.".
            int val = 0;
            foreach (char ch in p)
            {
                int d = ch >= '0' && ch <= '9' ? ch - '0'
                      : ch >= 'a' && ch <= 'z' ? ch - 'a' + 10
                      : ch >= 'A' && ch <= 'Z' ? ch - 'A' + 10 : -1;
                if (d < 0 || d >= key) return null;
                val = val * key + d;
                if (val > 0xffffff) val &= 0xffffff;
            }
            sb.Append((char)((val ^ xor) & 0xff));
        }
        return sb.ToString();
    }

    // Buoc 2 (decr_sun trong content_mv_protect_ofb.js):
    //   CryptoJS.AES.decrypt(base64, Utf8.parse(aesKey),
    //                        { iv: Utf8.parse(aesIv), padding: Pkcs7 })
    // KHONG set mode -> CryptoJS mode MAC DINH = CBC (ten file "...ofb.js" la
    // danh lua; code that dung CBC). Verify 2026-10-17 bang node (offline, 0 request):
    //   d2.html: key=xor=17, ak=687b3731437aa483, iv=22fcd69f8ae5c786
    //     37_1 -> "bk5s6zqsx8ul"   38_1 -> "0dc7tr80trym"
    //     42_1 -> "h0v5fLAKg3sfavCV27zdD2vx9FS7Cd43wInWJ5J9-4ZShTLae0ISyQOI2Xqj44HE"
    //   key=17 tren 4/4 trang da luu; ak/iv DOI THEO TRANG -> luon doc tu HTML, dung hardcode.
    public static string DecryptEnc(string enc, EncVars v)
    {
        if (v == null || !v.ok) return null;

        string b64 = DecrSun(enc, v.key, v.xor);
        if (string.IsNullOrEmpty(b64)) return null;

        byte[] data;
        try { data = Convert.FromBase64String(b64); } catch { return null; }
        if (data.Length == 0 || data.Length % 16 != 0) return null;

        try
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes(v.aesKey);
            aes.IV = Encoding.UTF8.GetBytes(v.aesIv);

            using var dec = aes.CreateDecryptor();
            byte[] plain = dec.TransformFinalBlock(data, 0, data.Length);
            string id = Encoding.UTF8.GetString(plain).Trim();
            return id.Length > 0 ? id : null;
        }
        catch { return null; }
    }

    // DD (DoodStream / playmogo) — JSON-LD contentUrl.
    public static bool IsDood(string url)
        => !string.IsNullOrEmpty(url) &&
           (url.IndexOf("playmogo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            url.IndexOf("dood", StringComparison.OrdinalIgnoreCase) >= 0);

    // "https://mmvh02.com/v/" -> "https://mmvh02.com/" ; "//x/y/" -> "https://x/y/"
    public static string OriginOf(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return null;

        string u = prefix.Trim();
        if (u.StartsWith("//")) u = "https:" + u;

        if (!System.Uri.TryCreate(u, UriKind.Absolute, out var parsed))
            return null;

        return parsed.GetLeftPart(UriPartial.Authority) + "/";
    }
    #endregion

    #region menu
    public static List<(string path, string title, List<(string path, string title)> subs)> Nav(string html)
    {
        var res = new List<(string, string, List<(string, string)>)>();
        if (string.IsNullOrEmpty(html)) return res;

        var used = new List<(int from, int to)>();

        // muc cha co submenu
        foreach (Match m in Regex.Matches(html,
            @"<li class='nav-item toggle'><a[^>]*href=""([^""]+)""[^>]*>([^<]*)</a>[\s\S]{0,320}?<ul class='nav'>([\s\S]*?)</ul>",
            RegexOptions.Singleline))
        {
            var subs = new List<(string, string)>();
            foreach (Match s in Regex.Matches(m.Groups[3].Value, @"href=""([^""]+)""[^>]*>([^<]*)</a>"))
            {
                string sp = PathOf(s.Groups[1].Value);
                if (sp != null) subs.Add((sp, StripCount(Clean(s.Groups[2].Value))));
            }

            string p = PathOf(m.Groups[1].Value);
            if (p == null && subs.Count > 0) p = subs[0].Item1;
            if (p != null)
                res.Add((p, StripCount(Clean(m.Groups[2].Value)), subs));

            used.Add((m.Index, m.Index + m.Length));
        }

        // muc cha khong co submenu (vd "Chinese AV")
        foreach (Match m in Regex.Matches(html, @"<li class='nav-item'><a[^>]*href=""([^""]+)""[^>]*>([^<]*)</a></li>"))
        {
            if (used.Any(u => m.Index >= u.from && m.Index < u.to)) continue;
            string p = PathOf(m.Groups[1].Value);
            if (p == null) continue;
            res.Add((p, StripCount(Clean(m.Groups[2].Value)), new List<(string, string)>()));
        }

        return res;
    }

    // tu href tuyet doi -> duong dan tuong doi trong module (bo "/en/", bo "/<pg>.html")
    static string PathOf(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        int i = href.IndexOf("/en/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;

        string p = href.Substring(i + 4);
        p = Regex.Replace(p, @"/\d+\.html$", "");
        p = p.Trim('/');
        if (p.Length == 0 || p.Contains('#') || !p.Contains('_')) return null;
        return p;
    }

    // trang genre la form checkbox: <input ... name='id[]' value='292' data-labelauty='Gonzo'>
    public static List<(string path, string title)> Genres(string html, string prefix)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        foreach (Match m in Regex.Matches(html,
            @"name='id\[\]'[^>]*value='(\d+)'[^>]*data-labelauty='([^']*)'"))
        {
            string name = Clean(m.Groups[2].Value);
            if (string.IsNullOrEmpty(name)) continue;
            res.Add(($"{prefix}_category/{m.Groups[1].Value}/{Uri.EscapeDataString(name)}", name));
        }
        return res;
    }

    // UNCENSORED: site KHONG co trang index genre (uncensored_categorylist rong, nav chi co maker).
    // Nguon duy nhat = pill Genre tren trang detail: /en/uncensored_category/{id}/{name}/1.html
    public static List<(string id, string name)> UncenGenres(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(html, @"/en/uncensored_category/(\d+)/([^/'""]+)/\d+\.html"))
        {
            if (!seen.Add(m.Groups[1].Value)) continue;
            string name = Clean(m.Groups[2].Value);
            if (string.IsNullOrEmpty(name)) continue;
            res.Add((m.Groups[1].Value, name));
        }
        return res;
    }

    // Link detail trong mot trang list -> lam pool cho harvest genre uncensored.
    public static List<string> DetailUrls(string html)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(html, @"href=""(?:https?://[^""/]+)?(/en/[a-z\-]+_content/[^""]+)"""))
        {
            string u = SiteHost + m.Groups[1].Value;
            if (seen.Add(u)) res.Add(u);
        }
        return res;
    }

    public static List<MenuItem> MenuFallback(string host)
    {
        var sections = new List<MenuItem>()
        {
            new("Giảm mosaic", MenuUrl(host, "reducing-mosaic_list/all")),
            new("Censored", MenuUrl(host, "censored_list/all")),
            new("Uncensored", MenuUrl(host, "uncensored_list/all")),
            new("Amateur", MenuUrl(host, "amateurjav_list/all")),
            new("Chinese AV", MenuUrl(host, "amateur_list/all")),
            new("Live Hàn", MenuUrl(host, "klive_list/all")),
            new("Live Trung", MenuUrl(host, "clive_list/all")),
        };

        return new List<MenuItem>()
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = $"{host}/sevenmm" },
            new MenuItem() { title = "Danh mục", playlist_url = "submenu", submenu = sections }
        };
    }
    #endregion
}
