using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace PubJav;

public static class PubJavTo
{
    public static readonly string SiteHost = "https://pubjav.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    public static string PlayerApi => SiteHost + "/ajax/player";

    // CHI DANH SACH DANH MUC, dung khi fetch `/genres` that bai (mang loi).
    // Trang that co 315 genre + 312 studio — dung 16 dong nay la THIEU 19 lan.
    // Binh thuong danh sach day du duoc dung (`Taxonomies` + `TaxonomiesAsync`).
    public static readonly (string slug, string name)[] FallbackGenres =
    {
        ("reducing-mosaic", "Reducing Mosaic"),
        ("uncensored-leaked", "Uncensored Leaked"),
        ("amateur", "Amateur"),
        ("anal", "Anal"),
        ("av-idol", "AV Idol"),
        ("beautiful-girl", "Beautiful Girl"),
        ("beautiful-pussy", "Beautiful Pussy"),
        ("big-asses", "Big Asses"),
        ("big-tits", "Big Tits"),
        ("blowjob", "Blowjob"),
        ("bondage", "Bondage"),
        ("bukkake", "Bukkake"),
        ("cheating-wife", "Cheating Wife"),
        ("cosplay", "Cosplay"),
        ("creampie", "Creampie"),
        ("cumshot", "Cumshot"),
    };

    // ================= URL LIST =================

    // Trang chu = /movies. Phan trang:
    //   /movies            -> /movies/pg-N
    //   /genre/<slug>      -> /genre/<slug>/pg-N
    //   /movies?genre=X    -> /movies?genre=X&pg=N   (query co san thi
    //                         phan trang nam trong query, khong phai /pg-N)
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host))
            host = SiteHost;

        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = Slug(search);

            // Ten Nhat/co ky tu dac bi `Slug` bo het -> slug rong. Giu
            // nguyen term de site tra 404 (0 phim, app hien "khong co ket
            // qua"); KHONG fallback `/movies` vi se do ra 12400 trang
            // phim moi cho mot cum tim kiem khong co ket qua.
            if (string.IsNullOrEmpty(slug))
                slug = System.Uri.EscapeDataString(WebUtility.HtmlDecode(search).Trim().ToLowerInvariant());

            return host + "/search/" + slug + (pg > 1 ? $"/pg-{pg}" : "");
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim();
            int at = raw.IndexOf('?');
            string path = (at >= 0 ? raw[..at] : raw).Trim('/');
            string query = at >= 0 ? raw[(at + 1)..].Trim('&') : "";

            if (string.IsNullOrEmpty(path))
                return host + "/movies";

            if (pg <= 1)
                return string.IsNullOrEmpty(query)
                    ? $"{host}/{path}"
                    : $"{host}/{path}?{query}";

            return string.IsNullOrEmpty(query)
                ? $"{host}/{path}/pg-{pg}"
                : $"{host}/{path}?{query}&pg={pg}";
        }

        return host + "/movies" + (pg > 1 ? $"/pg-{pg}" : "");
    }

    // Site kiem tra duong dan tim kiem theo slug: "Hello World" ->
    // /search/hello-world. Phai bo khoang trang va ky tu la.
    public static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        value = WebUtility.HtmlDecode(value).Trim().ToLowerInvariant();
        value = Regex.Replace(value, @"[^a-z0-9\s-]", "");
        value = Regex.Replace(value, @"[\s_]+", "-");
        return Regex.Replace(value, @"-+", "-").Trim('-');
    }

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim());
        int fragment = value.IndexOf('#');
        if (fragment >= 0)
            value = value[..fragment];

        if (value.StartsWith("//"))
            value = "https:" + value;
        else if (value.StartsWith("/"))
            value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            value = SiteHost + "/" + value;

        // `System.Uri` — không dùng `Uri` trần: trong class này có static
        // method Uri(host, search, c, pg) nên `Uri` trần sẽ trỏ vào method
        // đó và CS0119.
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return null;

        if (!parsed.Host.Equals("pubjav.com", StringComparison.OrdinalIgnoreCase) &&
            !parsed.Host.EndsWith(".pubjav.com", StringComparison.OrdinalIgnoreCase))
            return null;

        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//"))
            value = "https:" + value;

        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? value
            : null;
    }

    // ================= LIST =================

    // <div class="ml-item"> ... <a href="/play/<code>" title="...">
    //   <img data-original="<poster>" alt="<ten>">
    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<PlaylistItem>();
        if (string.IsNullOrWhiteSpace(html))
            return playlists;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Chia theo INDEX cua the `<div class="ml-item">` thay vi dung lookahead:
        // block cuoi cung khong co item sau nen regex lookahead lam mat 1 phim
        // (23/24). Item cuoi de han du phan trang, nhung ta chi boc href/title/
        // img nen phan trang thua khong gay nhieu.
        var starts = Regex.Matches(html,
            @"<div\b[^>]*\bclass\s*=\s*[""'][^""']*\bml-item\b[^""']*[""'][^>]*>",
            RegexOptions.IgnoreCase);

        for (int i = 0; i < starts.Count; i++)
        {
            int from = starts[i].Index;
            int to = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
            string block = html[from..to];

            var href = Regex.Match(block,
                @"<a\b[^>]*\bhref\s*=\s*[""'](/play/[a-z0-9._-]+)[""']",
                RegexOptions.IgnoreCase);
            if (!href.Success)
                continue;

            string pageUrl = NormalizePageUrl(href.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl))
                continue;

            var title = Regex.Match(block,
                @"<a\b[^>]*\bclass\s*=\s*[""'][^""']*\bml-mask\b[^""']*[""'][^>]*" +
                @"\btitle\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            if (!title.Success)
                title = Regex.Match(block, @"<img\b[^>]*\balt\s*=\s*[""']([^""']+)[""']",
                    RegexOptions.IgnoreCase);

            string name = title.Success
                ? WebUtility.HtmlDecode(title.Groups[1].Value).Trim()
                : "";
            if (string.IsNullOrEmpty(name))
                continue;

            // `<span class="mli-code">SNOS-306-RM</span>` — ma phim. Ten
            // phim tren site chi co tieng Nhat nen ghep `code` vao dau
            // de app hien thi/ tim duoc (kieu `FNS-258 <ten>` cua MissAV).
            string code = Code(block, href.Groups[1].Value);
            if (!string.IsNullOrEmpty(code) &&
                !name.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                name = code + " " + name;

            string poster = GetPoster(block);

            playlists.Add(new PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "pubjav",
                    href = pageUrl,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // Ma phim: `<span class="mli-code">` chua `SNOS-306-RM`; `<a>` chi co
    // slug `/play/snos-306-rm` nen fallback lay slug roi UPPER (site cung
    // hien thi `SNOS-306-RM`).
    static string Code(string block, string href)
    {
        var match = Regex.Match(block,
            @"class\s*=\s*[""'][^""']*\bmli-code\b[^""']*[""'][^>]*>\s*([^<]+)<",
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            string value = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            if (!string.IsNullOrEmpty(value))
                return value;
        }

        var slug = Regex.Match(href ?? "",
            @"/play/(?<code>[a-z0-9._-]+)", RegexOptions.IgnoreCase);

        return slug.Success ? slug.Groups["code"].Value.ToUpperInvariant() : null;
    }

    static string GetPoster(string block)
    {
        var match = Regex.Match(block,
            @"<img\b[^>]*\bdata-original\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(block,
                @"<img\b[^>]*\bdata-src\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(block, @"<img\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);

        string poster = NormalizeMediaUrl(match.Success ? match.Groups[1].Value : null);
        if (!string.IsNullOrEmpty(poster) &&
            poster.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        return poster;
    }

    // ================= DETAIL =================

    // Trang detail nhung 3 bien JS: filmId, window.__pt, window.__pk.
    // __pt la token MOT LAN DUNG — moi lan goi /ajax/player phai lay pt moi
    // (hoac noi tiep bang next_pt tra ve). Dung lai pt cu => 403.
    public static (string filmId, string pt, string pk) Tokens(string html)
    {
        if (string.IsNullOrEmpty(html))
            return (null, null, null);

        var id = Regex.Match(html, @"\bfilmId\s*=\s*(\d+)\s*;");
        var pt = Regex.Match(html, @"window\.__pt\s*=\s*""([a-f0-9]+)""");
        var pk = Regex.Match(html, @"window\.__pk\s*=\s*""([a-f0-9]+)""");

        if (!id.Success || !pt.Success || !pk.Success)
            return (null, null, null);

        return (id.Groups[1].Value, pt.Groups[1].Value, pk.Groups[1].Value);
    }

    // Nut server: <button class="switch-source" data-source="<filmId>"
    //   data-id="<episode>"><i ...></i> TB</button>
    // Phim cu co 1 nguon/server (label tran "PM"); phim 2 nguon thi label
    // "F4 #A" (bug 2026-10-06: regex cu doi "<" ngay sau label nen miss het).
    // Tra FULL label -> episode, giu thu tu xuat hien tren trang.
    public static Dictionary<string, string> Servers(string html, string filmId)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(filmId))
            return res;

        var pattern = @"data-source\s*=\s*[""" + filmId + @"""][^>]*" +
                      @"data-id\s*=\s*[""'](\d+)[""'][^>]*>\s*<i[^>]*>\s*</i>\s*([^<]+?)\s*<";

        foreach (Match match in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
        {
            string label = match.Groups[2].Value.Trim();
            if (label.Length == 0 || res.ContainsKey(label))
                continue;

            res[label] = match.Groups[1].Value;
        }

        return res;
    }

    // /ajax/player tra { player_enc, next_pt, next_pk }.
    // player_enc = base64, XOR voi __pk moi giai duoc HTML <iframe>.
    public static (string html, string nextPt, string nextPk) DecryptResponse(string json, string pk)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, null, null);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null, null);

            return (XorDecrypt(GetString(root, "player_enc"), pk),
                    GetString(root, "next_pt"),
                    GetString(root, "next_pk"));
        }
        catch
        {
            return (null, null, null);
        }
    }

    static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static string XorDecrypt(string encoded, string key)
    {
        if (string.IsNullOrEmpty(encoded) || string.IsNullOrEmpty(key))
            return null;

        try
        {
            byte[] data = Convert.FromBase64String(encoded);
            var sb = new StringBuilder(data.Length);
            for (int i = 0; i < data.Length; i++)
                sb.Append((char)(data[i] ^ key[i % key.Length]));

            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    public static string IframeUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var match = Regex.Match(html,
            @"<iframe\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);

        return NormalizeMediaUrl(match.Success ? match.Groups[1].Value : null);
    }

    // ================= SERVER =================

    // Thu tu uu tien do DO DUOC tren may nay, khong do ten host:
    //   F4 (f4stream.com)  HLS, verify 2026-10-06 phat ngon ca 2 nhanh
    //                      #A/#B phim ofje-138-rm. XEP #1
    //   FL (ryderjet.com)  StreamHG  hls4 ~1050KB/s
    //   SW (hglink.to)     StreamHG  hls4 ~1560KB/s
    //   ST (strtape.cloud) Streamtape mp4 1.17GB, 206
    //   DD (playmogo.com)  DoodStream mp4 878MB, 206
    //   PM (playmate.to)   HLS 1080p .txt, tung 1.15MB/s nhung 2026-10-06
    //                      bi bop CDN sieu cham -> XEP SAU DD (van giu vi
    //                      chat luong 1080p, lam fallback duoc)
    //   US (player.upn.one)   master qua `cfNative` 4/4 OK, NHUNG segment
    //                      tren CDN `*.gamezonehub.shop` bi Cloudflare chan
    //                      IP may chu (522/504/403 lien tuc). XEP CUOI: co
    //                      code dung nhung hls.js se treo o server nay.
    //   PP (stb.strp2p.com)   cung ho upn, them id da bi xoa (404 het)
    //   TB (turbonewvid.com) host CHET — DNS tro ve 192.64.119.234
    //     (namecheap parking), khong dung duoc
    //
    // Server list DOI THEO TUNG PHIM: spsf-31 co 7 nut, ndra-025-rm chi
    // 5 va co `PM` thay cho `PP`. Code phai loc theo phim, khong hardcode.
    public static int Priority(string label)
    {
        // Label co the kem nguon ("F4 #A") -> lay token dau ("F4").
        // Label tran ("PM") van khop nhu cu.
        string key = label ?? "";
        var m = Regex.Match(key, @"^([A-Za-z0-9]+)");
        key = m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
        switch (key)
        {
            case "F4": return 0;
            case "FL": return 1;
            case "SW": return 2;
            case "ST": return 3;
            case "DD": return 4;
            case "PM": return 5;
            case "US": return 6;
            case "PP": return 7;
            default: return 99;
        }
    }

    public static bool IsSupported(string label) => Priority(label) < 99;

    // Suy kind tu LABEL (khong can fetch iframe): dung cho /vidosik liet ke
    // nhanh §11c2. Moi label map toi 1 host co dinh:
    //   F4 -> f4stream.com (hls)   FL/SW -> StreamHG (hls)
    //   ST -> strtape.cloud (mp4)  DD -> playmogo.com (mp4, Dood)
    //   PM -> playmate.to (hls)    US/PP -> upn (hls)
    public static string KindByLabel(string label)
    {
        string key = label ?? "";
        var m = Regex.Match(key, @"^([A-Za-z0-9]+)");
        key = m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
        switch (key)
        {
            case "F4":
            case "FL":
            case "SW":
            case "PM":
            case "US":
            case "PP":
                return "hls";
            case "ST":
            case "DD":
            case "TB":
                return "mp4";
            default:
                return "hls";
        }
    }

    // Host cua iframe -> dang link. Dung de chon route .m3u8 hay .mp4
    // (app Lampa chon player theo DUOI url, mp4 di qua .m3u8 se loi
    // "no EXTM3U delimiter").
    public static string Kind(string iframe)
    {
        if (string.IsNullOrEmpty(iframe))
            return null;

        if (iframe.IndexOf("ryderjet.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
            iframe.IndexOf("hglink.to", StringComparison.OrdinalIgnoreCase) >= 0 ||
            iframe.IndexOf("vibuxer.com", StringComparison.OrdinalIgnoreCase) >= 0)
            return "hls";

        // F4 (f4s.top / f4scdn.com / f4stream.com): m3u8 nhung resolve rieng
        // (data-api -> JSON url), KHONG phai packer StreamHg — xem StreamAsync.
        if (iframe.IndexOf("f4s.top", StringComparison.OrdinalIgnoreCase) >= 0 ||
            iframe.IndexOf("f4scdn.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
            iframe.IndexOf("f4stream.com", StringComparison.OrdinalIgnoreCase) >= 0)
            return "hls";

        if (iframe.IndexOf("strtape.cloud", StringComparison.OrdinalIgnoreCase) >= 0 ||
            iframe.IndexOf("streamtape", StringComparison.OrdinalIgnoreCase) >= 0)
            return "mp4";

        if (iframe.IndexOf("dood", StringComparison.OrdinalIgnoreCase) >= 0 ||
            iframe.IndexOf("playmogo", StringComparison.OrdinalIgnoreCase) >= 0)
            return "mp4";

        if (IsUpn(iframe) || IsPlaymate(iframe))
            return "hls";

        return null;
    }

    public static bool IsF4(string iframe) =>
        !string.IsNullOrEmpty(iframe) &&
        (iframe.IndexOf("f4s.top", StringComparison.OrdinalIgnoreCase) >= 0 ||
         iframe.IndexOf("f4scdn.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
         iframe.IndexOf("f4stream.com", StringComparison.OrdinalIgnoreCase) >= 0);

    // ========== F4 (f4s.top / f4scdn.com / f4stream.com) ==========
    // Cong thuc goc tu SexTb (F4SourceAsync, da verify ben do):
    //   GET embed -> data-api="/api/play/<uuid>" -> GET api (host f4stream.com
    //   neu relative) -> JSON {"url":"/v/<token>"} -> m3u8 (200 #EXTM3U).
    // F4 sinh token theo IP -> direct (CurlGet, giong DoodSourceAsync).
    // Verified 2026-10-06: f4s.top/e/dv1wes3h (nut F4 #A phim ofje-138-rm).
    public static async Task<string> F4SourceAsync(string embedUrl, string referer,
        int timeoutSeconds = 10)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return null;

        string html = await CurlGet(embedUrl, referer ?? SiteHost + "/",
            Math.Max(6, timeoutSeconds));
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"data-api\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;
        string api = m.Groups[1].Value;
        if (api.StartsWith("/"))
            api = "https://f4stream.com" + api;

        string json = await CurlGet(api, embedUrl, Math.Max(6, timeoutSeconds));
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("url", out var u) &&
                u.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                string v = u.GetString().Replace("\\/", "/");
                if (v.StartsWith("/"))
                    v = "https://f4stream.com" + v;
                return v.StartsWith("http") ? v : null;
            }
        }
        catch { }
        return null;
    }

    // hglink.to/e/<id> la trang CSR (main.js obfuscate, body 452 byte).
    // Trang player that cua no la vibuxer.com/e/<id> — cung ho StreamHG,
    // cung `var links` va cung token trong duong dan. Do chuc nang cua
    // app hien ra la chinh no nen doi host la duoc.
    public static string StreamHgUrl(string iframe)
    {
        if (string.IsNullOrEmpty(iframe))
            return null;

        if (iframe.IndexOf("hglink.to", StringComparison.OrdinalIgnoreCase) >= 0)
            return Regex.Replace(iframe, @"//hglink\.to/", "//vibuxer.com/",
                RegexOptions.IgnoreCase);

        return iframe;
    }

    // ================= UPNSHARE (US + PP) =================

    // Ho player upn: `player.upn.one/#<id>` va `stb.strp2p.com/#<id>` —
    // hai host cung mot bundle (Vimeo/CDN, jwplayer).
    //
    // Id nam SAU DAU `#` nen app khong lay duoc, nhung player tu goi
    // API cua chinh host:
    //     GET <host>/api/v1/video?id=<id>
    // -> payload la CHUOI HEX, giai AES-128-CBC (WebCrypto) roi
    //    TextDecoder => JSON.
    //
    // Key/IV deu tu sinh trong bundle tu `location.protocol` +
    // `location.hash`. Da tinh lai cho protocol "https:" va hash bat
    // dau la "#" (35) — hai cai nay khong doi theo phim nen hardcode
    // duoc, va ket qua giai dung JSON (17 khoa) cho ca `glxfig`.
    //
    //   key = "kiemtienmua911ca"
    //   iv  = "1234567890oiuytr"
    public const string UpnKey = "kiemtienmua911ca";
    public const string UpnIv = "1234567890oiuytr";

    public static bool IsUpn(string iframe)
    {
        if (string.IsNullOrEmpty(iframe))
            return false;

        return iframe.IndexOf("player.upn.one", StringComparison.OrdinalIgnoreCase) >= 0 ||
               iframe.IndexOf("strp2p.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
               iframe.IndexOf("upn.one", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // `.../#<id>` -> `<id>`. Kiem tra do dai > 1 cho dung regex `U()`
    // cua player (tra null neu id 0 hoac 1 ky tu).
    public static string UpnId(string iframe)
    {
        if (string.IsNullOrEmpty(iframe))
            return null;

        int at = iframe.IndexOf('#');
        if (at < 0)
            return null;

        string id = iframe[(at + 1)..].Split('&')[0].Trim('/');
        return id.Length > 1 ? id : null;
    }

    public static string UpnApi(string iframe, string id)
        => HostOf(iframe) + "/api/v1/video?id=" + HttpUtility.UrlEncode(id);

    public static string UpnDecrypt(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        hex = hex.Trim();
        if (hex.Length % 2 != 0 || Regex.IsMatch(hex, @"[^0-9a-fA-F]"))
            return null;

        var data = new byte[hex.Length / 2];
        for (int i = 0; i < data.Length; i++)
            data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

        try
        {
            using var aes = Aes.Create();
            aes.KeySize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes(UpnKey);
            aes.IV = Encoding.UTF8.GetBytes(UpnIv);

            using var transform = aes.CreateDecryptor();
            var plain = transform.TransformFinalBlock(data, 0, data.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    // JSON tra ve 3 URL master, thu tu uu tien do DO DUOC:
    //   cfNative  player.upn.one/v4/pl/<cfDomain>/…/master.*.m3u8?k=&kx=
    //             -> 200 4/4 lan. KHONG can cookie (k nam san trong query).
    //             Relay cua player, nen no khong bi Cloudflare chan.
    //   source    In-House host IP (94.131.217.x) — o doi moi lan resolve,
    //             gan nhu het luc da do 0/4. Giu lai phong khi host song lai.
    //   cf        CDN truc tiep `cf-master.*.txt` — 403 Cloudflare 0/4.
    //
    // Segment (.woff2, noi dung la fMP4) chi lay duoc tu CDN domain trong
    // `cfNative`; CDN do hay 522/403 khi request lien tuc tu cung IP nen
    // server phai PROBE master truoc, tra ve cai nao that su tra `#EXTM3U`.
    public static List<string> UpnSources(string json)
    {
        var res = new List<string>();
        var get = (string prop) =>
        {
            var m = Regex.Match(json,
                "\"" + prop + "\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Replace("\\/", "/") : null;
        };

        foreach (string prop in new[] { "cfNative", "source", "cf" })
        {
            string v = get(prop);
            if (!string.IsNullOrEmpty(v) && v.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !res.Contains(v))
                res.Add(v);
        }

        return res;
    }

    // ================= PLAYMATE (PM) =================

    // `playmate.to/embed/<id>` la jwplayer + CryptoJS + pako, KHONG
    // phai player upn. Khong co `/api/v1`:
    //     POST <host>/api/s   {"c":"<id>","d":"<device>"}
    // -> JSON, `sx` = master `.txt` (HLS 1080p, segment `.css` nhung
    // noi dung la MPEG-TS that — magic 0x47).
    // `d` la device cua player; server khong kiem tra nghiem nen
    // "desktop" chay duoc tren moi UA.
    public static bool IsPlaymate(string iframe)
        => !string.IsNullOrEmpty(iframe) &&
           iframe.IndexOf("playmate.to", StringComparison.OrdinalIgnoreCase) >= 0;

    public static string PlaymateId(string iframe)
    {
        if (string.IsNullOrEmpty(iframe))
            return null;

        var m = Regex.Match(iframe, @"/embed/([A-Za-z0-9_\-]+)",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string PlaymateApi(string iframe, string id)
        => HostOf(iframe) + "/api/s";

    public static string PlaymateSource(string json)
    {
        var m = Regex.Match(json, "\"sx\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Replace("\\/", "/") : null;
    }

    // ================= STREAMHG (FL + SW) =================

    // StreamHG giu link trong packer P.A.C.K.E.R + `var links={"hls4":..,..}`.
    // Player that uu tien hls4 > hls3 > hls2. Upstream chon loc: phim nay
    // hls4 nhanh 1.5MB/s, phim ky hls4 1KB/s con hls2 205KB/s — nen
    // phai probe ca 3 master, lay link dau tien co variant.
    public static string Unpack(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html,
            @"eval\(function\(p,a,c,k,e,d\)\{[^}]*\}\('([\s\S]*?)',(\d+),(\d+),'([\s\S]*?)'\.split\('\|'\)\)\)",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string p = m.Groups[1].Value;
        if (!int.TryParse(m.Groups[2].Value, out int a) ||
            !int.TryParse(m.Groups[3].Value, out int c))
            return null;

        var k = m.Groups[4].Value.Split('|');

        // Lap index lon xuoi: ket qua vong sau la du lieu cho vong sau
        for (int i = c - 1; i >= 0; i--)
        {
            if (i >= k.Length || string.IsNullOrEmpty(k[i]))
                continue;

            // MatchEvaluator de '$' trong k[i] khong bi hieu la bien the
            p = Regex.Replace(p, @"\b" + ToBase(i, a) + @"\b", _ => k[i]);
        }

        return p;
    }

    // Do lai so nguyen o he `b` bang chu cai (a=36 -> 13 ra "d").
    // KHONG shortcut "value < b -> value.ToString()": chi dung cho b <= 10.
    static string ToBase(int value, int b)
    {
        if (value == 0)
            return "0";

        var sb = new StringBuilder();
        while (value > 0)
        {
            int d = value % b;
            sb.Insert(0, (char)(d < 10 ? '0' + d : 'a' + d - 10));
            value /= b;
        }

        return sb.ToString();
    }

    public static List<string> StreamHgMasters(string html, string playerUrl)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(html))
            return res;

        string src = Unpack(html) ?? html;

        var m = Regex.Match(src, @"var\s+links\s*=\s*\{([^}]{0,2000})\}",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return res;

        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match x in Regex.Matches(m.Groups[1].Value,
            @"""(hls\d)""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase))
            kv[x.Groups[1].Value] = WebUtility.HtmlDecode(x.Groups[2].Value.Trim());

        string baseHost = HostOf(playerUrl);

        foreach (string key in new[] { "hls4", "hls3", "hls2" })
        {
            if (!kv.TryGetValue(key, out string v) || string.IsNullOrEmpty(v))
                continue;

            // hls4 la path relative (/stream/...) tro ve chinh host player
            if (v.StartsWith("/") && !string.IsNullOrEmpty(baseHost))
                v = baseHost.TrimEnd('/') + v;

            if (v.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !res.Contains(v))
                res.Add(v);
        }

        return res;
    }

    public static string HostOf(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return null;

        return parsed.GetLeftPart(UriPartial.Authority);
    }

    // ================= STREAMTAPE (ST) =================

    // Trang Streamtape co HAI link giong nhau:
    //   <div id="robotlink">...&token=A</div>  -> token GIA, API tra 500
    //   script gan lai #robotlink     token=B  -> 302 sang CDN .mp4
    // Script chay sau nen chi token gan lai moi chay duoc — lay match CUOI.
    public static string StreamtapeUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        string query = null;
        foreach (Match m in Regex.Matches(html,
            @"robotlink.{0,4}\.innerHTML\s*=\s*'[^']*get_video\?'\s*\+\s*\('([^']+)'",
            RegexOptions.IgnoreCase))
            query = m.Groups[1].Value;

        if (string.IsNullOrEmpty(query))
        {
            var div = Regex.Match(html, @"id=""robotlink""[^>]*>([^<]+)<",
                RegexOptions.IgnoreCase);
            if (div.Success)
                query = div.Groups[1].Value;
        }

        if (string.IsNullOrEmpty(query))
            return null;

        // Literal co tien to ngau nhien ("xcdid=.."), cat tu `id=` di
        var tail = Regex.Match(query, @"id=[A-Za-z0-9_-]+&expires=\d+[^']*");
        if (!tail.Success)
            return null;

        return "https://strtape.cloud/get_video?" + tail.Value;
    }

    // ================= DOODSTREAM (DD) =================

    // Cong thuc DoodStream (dung lai cho site bat ky):
    //   1) GET <embed>          -> `pass_md5/<hash>/<token>`
    //   2) GET <hostAPI>/pass_md5/..?referer=<domain>
    //                              -> TEXT THUAN: URL .mp4 tren CDN
    //   3)them `?token&expiry` va phai co Referer DUNG host API
    // Token trong duong dan phai LAY LAI o moi lan goi (hash doi theo phim).
    public static async Task<(string url, string referer)> DoodSourceAsync(
        string embedUrl, string referer, int maxTime = 6)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return (null, null);

        var got = await CurlGetUrl(embedUrl, referer, maxTime);
        var m = Regex.Match(got.body ?? "", @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
        if (!m.Success)
            return (null, null);

        string api = null;
        if (!string.IsNullOrEmpty(got.finalUrl))
        {
            int at = got.finalUrl.IndexOf("/e/", StringComparison.OrdinalIgnoreCase);
            if (at > 8)
                api = got.finalUrl[..at];
        }

        if (string.IsNullOrEmpty(api))
            api = HostOf(embedUrl);

        // API hay tra chuoi rong/"RELOAD" -> tai lai embed roi thu lai
        string src = null;
        for (int i = 0; i < 3 && string.IsNullOrEmpty(src); i++)
        {
            if (i > 0)
            {
                await Task.Delay(250 * i);
                got = await CurlGetUrl(embedUrl, referer, maxTime);
                m = Regex.Match(got.body ?? "", @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
                if (!m.Success)
                    return (null, null);
            }

            src = await CurlGetRetry($"{api}/{m.Groups[1].Value}?referer=pubjav.com",
                api + "/", null, 1, maxTime);
            src = (src ?? "").Trim();
            if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                src = null;
        }

        if (string.IsNullOrEmpty(src))
            return (null, null);

        string token = m.Groups[1].Value.Split('/')[^1];
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var res = (src + (src.Contains('?') ? "&" : "?") +
                   $"token={token}&expiry={expiry}", api);

        // KHONG in URL: no mang token cua CDN. Chi do do dai.
        Console.WriteLine($"PubJav DD: ok len={res.Item1.Length} referer={api}");
        return res;
    }

    // ================= UPNSHARE RESOLVE (US + PP) =================

    // Giai iframe `player.upn.one/#<id>` hoac `stb.strp2p.com/#<id>` ra
    // master HLS. Referer BAT BUOC = host player (CDN tra 403 thieu
    // Referer nen do phai trung, khong gan `pubjav.com`).
    public static async Task<(string url, string referer)> UpnResolveAsync(
        string iframe, int maxTime = 12)
    {
        if (!IsUpn(iframe))
            return (null, null);

        string id = UpnId(iframe);
        if (string.IsNullOrEmpty(id))
            return (null, null);

        string referer = HostOf(iframe) + "/";
        string body = await CurlGetRetry(UpnApi(iframe, id), referer, null, 2, maxTime);
        if (string.IsNullOrWhiteSpace(body))
            return (null, null);

        string hex = body.Trim().Trim('"');
        string json = UpnDecrypt(hex);
        if (string.IsNullOrEmpty(json))
            return (null, null);

        // Master nao that su con song thi moi giu — `source` (host IP) va
        // `cf` (CDN) hay chet, chi `cfNative` on dinh.
        var candidates = UpnSources(json);
        for (int i = 0; i < candidates.Count; i++)
        {
            string master = await CurlGetRetry(candidates[i], referer,
                "#EXTM3U", 1, Math.Min(maxTime, 8));

            if (string.IsNullOrEmpty(master))
                continue;

            Console.WriteLine($"PubJav US: ok id={id} src[{i}]={HostOf(candidates[i])}");
            return (candidates[i], referer);
        }

        Console.WriteLine($"PubJav US: fail id={id} n={candidates.Count}");
        return (null, null);
    }

    // ================= PLAYMATE RESOLVE (PM) =================

    // Giai iframe `playmate.to/embed/<id>` -> master `.txt`.
    // Master/variant/segment deu URL RELATIVE va KHONG can Referer (da
    // do 200 voi ca 3) nen tra thang master cho app tu noi tiep.
    public static async Task<(string url, string referer)> PlaymateResolveAsync(
        string iframe, int maxTime = 12)
    {
        if (!IsPlaymate(iframe))
            return (null, null);

        string id = PlaymateId(iframe);
        if (string.IsNullOrEmpty(id))
            return (null, null);

        string referer = HostOf(iframe) + "/";
        string body = await CurlPostJson(PlaymateApi(iframe, id), referer,
            "{\"c\":\"" + id + "\",\"d\":\"desktop\"}", maxTime);

        if (string.IsNullOrWhiteSpace(body))
            return (null, null);

        string source = PlaymateSource(body);
        if (string.IsNullOrEmpty(source))
            return (null, null);

        Console.WriteLine($"PubJav PM: ok id={id} host={HostOf(source)}");
        return (source, referer);
    }

    // ================= CURL =================

    static void PrepCurlEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("LD_PRELOAD");
        psi.Environment.Remove("LD_LIBRARY_PATH");

        string curl = "/data/data/com.termux/files/usr/bin/curl";
        psi.FileName = System.IO.File.Exists(curl) ? curl : "curl";
    }

    static ProcessStartInfo CurlArgs(string url, string referer, int maxTime, bool http2)
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
        psi.ArgumentList.Add(http2 ? "--http2" : "--http1.1");
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

        return psi;
    }

    public static async Task<string> CurlGet(string url, string referer,
        int maxTime = 20, bool http2 = true)
    {
        try
        {
            var psi = CurlArgs(url, referer, maxTime, http2);
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using var p = Process.Start(psi);
            if (p == null)
                return null;

            string stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }

    // Can `url_effective` = URL CUOI sau khi da follow het redirect.
    // DoodStream luon 301 sang host khac, dung host ban dau thi fail.
    public static async Task<(string body, string finalUrl)> CurlGetUrl(
        string url, string referer, int maxTime = 20)
    {
        try
        {
            var psi = CurlArgs(url, referer, maxTime, true);
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add("\n%{url_effective}");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using var p = Process.Start(psi);
            if (p == null)
                return (null, null);

            string stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
                return (null, null);

            int at = stdout.LastIndexOf("\nhttp", StringComparison.Ordinal);
            if (at < 0)
                return (stdout, url);

            return (stdout[..at], stdout[(at + 1)..].Trim());
        }
        catch
        {
            return (null, null);
        }
    }

    // Chi lay URL CUOI cua chuoi 302, KHONG tai ve noi dung. File mp4 cua
    // Streamtape/DoodStream da 1GB+ nen dung `-r 0-0` de curl chi yeu 1 byte
    // roi doc `url_effective`.
    public static async Task<string> CurlFinalUrl(string url, string referer,
        int maxTime = 12)
    {
        try
        {
            var psi = CurlArgs(url, referer, maxTime, true);
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add("0-0");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("/dev/null");
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add("%{url_effective}");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using var p = Process.Start(psi);
            if (p == null)
                return null;

            string stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
                return null;

            string final = stdout.Trim();
            return final.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? final
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string> CurlGetRetry(string url, string referer,
        string marker, int attempts = 2, int maxTime = 20)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (i > 0)
                await Task.Delay(300 * i);

            string body = await CurlGet(url, referer, maxTime);
            if (!string.IsNullOrWhiteSpace(body) &&
                (string.IsNullOrEmpty(marker) || body.Contains(marker)))
                return body;

            // mot so host StreamHG treo voi --http2, http1.1 thi 200 ngay
            if (i == attempts - 1)
                body = await CurlGet(url, referer, maxTime, false);
            else
                continue;

            if (!string.IsNullOrWhiteSpace(body) &&
                (string.IsNullOrEmpty(marker) || body.Contains(marker)))
                return body;
        }

        return null;
    }

    // POST JSON cho ho player upn (`/api/v1/video`) va playmate (`/api/s`).
    // Hai API deu tra 200 ma khong can cookie; chi can UA + Referer dung.
    public static async Task<string> CurlPostJson(string url, string referer,
        string json, int maxTime = 12)
    {
        try
        {
            var psi = CurlArgs(url, referer, maxTime, true);
            psi.ArgumentList.Add("-H");
            psi.ArgumentList.Add("Content-Type: application/json");
            psi.ArgumentList.Add("--data-raw");
            psi.ArgumentList.Add(json);
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using var p = Process.Start(psi);
            if (p == null)
                return null;

            string stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }

    // Master HLS phai co `#EXT-X-STREAM-INF` (nhac variant) — master rong
    // hoac trang loi 502/CDN chet deu khong phaii.
    public static async Task<string> FirstWorkingMaster(List<string> masters, int maxTime = 8)
    {
        if (masters == null || masters.Count == 0)
            return null;

        var checks = new List<Task<string>>();
        foreach (string master in masters)
            checks.Add(CurlGetRetry(master, null, "#EXT-X-STREAM-INF", 1, maxTime));

        string[] results = await Task.WhenAll(checks);
        for (int i = 0; i < results.Length; i++)
            if (!string.IsNullOrWhiteSpace(results[i]))
                return masters[i];

        return null;
    }

    public static bool IsHls(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        int query = value.IndexOfAny(new[] { '?', '#' });
        string path = query >= 0 ? value[..query] : value;
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMedia(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        int query = value.IndexOfAny(new[] { '?', '#' });
        string path = query >= 0 ? value[..query] : value;
        return IsHls(value) ||
               path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
    }

    // Link mp4 (ST/DD) la file 800MB-1.2GB nen KHONG tai ve. Chi curl
    // 1KB dau roi so magic byte:
    //   00 00 00 ?? 66 74 79 70 = mp4 (ftyp o offset 4)
    //   1A 45 DF A3              = matroska/webm
    //   47                       = MPEG-TS
    // Server nao tra `{"status":500,"msg":"Sorry, error on our side!"}`
    // (Content-Type: text/html) se bi lo, de app fallback sang server
    // mp4 con lai thay vi mo mot link chet.
    public static async Task<bool> ProbeMediaAsync(string url, string referer,
        int maxTime = 6)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        try
        {
            var psi = CurlArgs(url, referer, maxTime, true);
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add("0-1023");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using var p = Process.Start(psi);
            if (p == null)
                return false;

            var buf = new byte[16];
            int read = await p.StandardOutput.BaseStream.ReadAsync(buf, 0, buf.Length);
            await p.WaitForExitAsync();
            if (read < 8)
                return false;

            bool mp4 = buf[4] == 0x66 && buf[5] == 0x74 && buf[6] == 0x79 && buf[7] == 0x70;
            bool mkv = buf[0] == 0x1A && buf[1] == 0x45 && buf[2] == 0xDF && buf[3] == 0xA3;
            bool ts = buf[0] == 0x47;

            return mp4 || mkv || ts;
        }
        catch
        {
            return false;
        }
    }

    // ================= MENU =================

    // Trang `/genres` va `/studios` liet ke TOAN BO danh muc trong MOT trang
    // (khong phan trang — da do: khong co `class="pagination"`). Moi muc nam
    // trong `page_list`:
    //   <h3 class="list_title"><a href="<url>" title="<ten that>">
    //
    // `href` KHAC NHAU giua hai trang:
    //   /genres  -> `https://pubjav.com/genre/creampie`  (URL tuyet doi)
    //   /studios -> `/studio/madonna`                    (path tuong doi)
    // Nen regex bo qua phan dau href va chi giu lai path sau `prefix`.
    //
    // PHẢI cat `page_list` truoc: menu dieu huong cua site cung chua
    // `/genre/...` va `/studio/...`, nam trong `<body>` truoc `page_list`.

    // FEFF/ZWSP/ZWNJ/ZWJ/LRM/RLM/BOM — dinh dang nhin thay. Site lam rac
    // 2 muc: `genre/milf<FEFF>` va `genre/<FEFF>sexy-legs` deu 404 tren
    // chinh site, nhung ban cat sach thi ra trang that (`/genre/milf`,
    // `/genre/sexy-legs` deu 200). Bo ky tu an => khuc phai lai duoc dung.
    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };

    static string CleanTaxonomy(string value)
        => value == null ? null : new string(value.Where(c => !Hidden.Contains(c)).ToArray()).Trim();

    public static List<(string slug, string name)> Taxonomies(string html, string prefix)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(prefix))
            return res;

        int start = html.IndexOf("class=\"page_list\"", StringComparison.Ordinal);
        if (start < 0)
            return res;

        int stop = html.IndexOf("class=\"pagination\"", start, StringComparison.Ordinal);
        var box = stop > start ? html[start..stop] : html[start..];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Nhom `slug` CHI giu phan SAU prefix: `href` cua /genres la
        // `https://pubjav.com/genre/creampie`, cua /studios la `/studio/madonna`
        // — `[^\"]*?` non-greedy bo qua phan dau, con `<slug>` = `creampie`.
        // (Neu giu ca `genre/creampie` roi `TaxonomyMenu` them `prefix` nua
        // se ra `c=genre%2fgenre%2fcreampie`.)
        var pattern = "<h3 class=\"list_title\">\\s*<a href=\"[^\"]*?" +
                      Regex.Escape(prefix) + "(?<slug>[^\"]+)\"\\s+title=\"(?<name>[^\"]*)\"";

        foreach (Match match in Regex.Matches(box, pattern, RegexOptions.IgnoreCase))
        {
            string slug = CleanTaxonomy(WebUtility.HtmlDecode(match.Groups["slug"].Value));
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            string name = CleanTaxonomy(WebUtility.HtmlDecode(match.Groups["name"].Value));

            // Client SISI tach `title` bang dau `:` (subtitle) — tên studio
            // có dau `:` se bi cat cụt, thay bang `-`.
            res.Add((slug, name.Replace(':', '-').Replace('|', '-')));
        }

        return res;
    }

    // QUY TẮC MENU: dung 2 tang.
    //   tang 1 = ten nhom ("Thể loại", "Studio")
    //   tang 2 = TOAN BO muc tinh duoc, bam vao ra thang list phim
    // Client SISI (`module/SISI/plugins/sisi.js`) chi hien MOT TANG submenu:
    // chon muc cap 2 day `b.playlist_url` thang vao `Lampa.Activity.push`.
    // Muc cap 2 co `submenu` rieng se bi day `playlist_url == "submenu"` =>
    // man hinh trong. Nen KHONG chia 300+ muc theo chu cai (thanh 3 tang).
    static List<MenuItem> TaxonomyMenu(string host, string prefix,
        List<(string slug, string name)> items)
    {
        var res = new List<MenuItem>(items?.Count ?? 0);

        foreach (var (slug, name) in items ?? new List<(string, string)>())
        {
            if (string.IsNullOrEmpty(slug))
                continue;

            res.Add(new MenuItem(
                string.IsNullOrEmpty(name) ? slug : name,
                host + "/pubjav?c=" + HttpUtility.UrlEncode(prefix + slug)));
        }

        return res;
    }

    // Sort thật của site (từ <select name="sort"> của form myFilter,
    // CO MAT tren ca genre/*, studio/*, search tra ve 0 item):
    // desc | asc | release | viewed | liked | favorite
    //
    // QUAN TRONG (do that 2026-10-07 tren genre/beautiful-pussy):
    // form GET toi /genre/<slug> voi DAY DU 4 query (genre, quality,
    // year, sort). Chi gui `?sort=viewed` thi server BO QUA sort, tra ve
    // y nhu base (sequence 24/24 trung).
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới cập nhật",   "desc"),
        ("Cũ nhất",        "asc"),
        ("Ngày phát hành", "release"),
        ("Xem nhiều",      "viewed"),
        ("Nhiều like",     "liked"),
        ("Nhiều yêu thích","favorite"),
    };

    static readonly string[] SortWhitelist =
        { "desc", "asc", "release", "viewed", "liked", "favorite" };

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    // Sort AP DUOC: genre/* + studio/* (ghep fullquery) VA movies
    // (home/movies). search KHONG co <select> sort -> null.
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return null;
        if (string.IsNullOrWhiteSpace(c)) return Sorts;   // home = /movies
        string path = c.Trim().TrimStart('/');
        int at = path.IndexOf('?');
        if (at >= 0) path = path.Substring(0, at);
        path = path.Trim('/').ToLowerInvariant();
        if (path.StartsWith("movies")) return Sorts;
        if (path.StartsWith("genre/")) return Sorts;
        if (path.StartsWith("studio/")) return Sorts;
        return null;
    }

    // (raw, path, query): tach `c` thanh path + query de ghep sort.
    static (string path, string query) SplitC(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return ("movies", "");
        string raw = c.Trim();
        int at = raw.IndexOf('?');
        string path = (at >= 0 ? raw.Substring(0, at) : raw).Trim('/');
        string query = at >= 0 ? raw.Substring(at + 1).Trim('&') : "";
        if (string.IsNullOrEmpty(path)) path = "movies";
        return (path, query);
    }

    static string QVal(string query, string key)
    {
        var m = Regex.Match("&" + query, @"[?&]" + key + @"=([^&]*)",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // Ghep sort vao `c` hien tai: giu nguyen genre/quality/year dang loc,
    // chi doi sort. Trang genre/studio ghep DAY DU 4 query (form myFilter),
    // thieu la site bo qua sort (do that 2026-10-07).
    public static string WithSort(string c, string sort)
    {
        var (path, query) = SplitC(c);
        string genre = QVal(query, "genre") ?? "all";
        string quality = QVal(query, "quality") ?? "all";
        string year = QVal(query, "year") ?? "all";
        return $"{path}?genre={genre}&quality={quality}&year={year}&sort={sort}";
    }

    // Ghep year vao `c` hien tai: giu sort dang chon, doi year.
    public static string WithYear(string c, string sort, string year)
    {
        var (path, query) = SplitC(c);
        string genre = QVal(query, "genre") ?? "all";
        string quality = QVal(query, "quality") ?? "all";
        return $"{path}?genre={genre}&quality={quality}&year={year}&sort={sort}";
    }

    // Sort hien tai doc tu `c`; year hien tai doc tu `c`.
    public static string CurrentSort(string c)
    {
        var (_, query) = SplitC(c);
        return NormalizeSort(QVal(query, "sort")) ?? "desc";
    }

    public static string CurrentYear(string c)
    {
        var (_, query) = SplitC(c);
        string y = QVal(query, "year");
        if (string.IsNullOrEmpty(y)) return "all";
        if (y.Equals("all", StringComparison.OrdinalIgnoreCase)) return "all";
        return Array.IndexOf(Years, y) >= 0 ? y : "all";
    }

    public static string SortLabel(string sort)
    {
        foreach (var (name, s) in Sorts)
            if (s == sort) return name;
        return "Mới cập nhật";
    }

    // ===== head menu: phụ thuộc search/c -> dựng lại mỗi request (rẻ) =====
    // Dong 2 "Sắp xếp" + dong 2b "Năm": 2 trong 4 o filter lay vao menu
    // theo yeu cau user 2026-10-07 (Genre/Quality bo qua: Genre = chinh
    // menu The loai, Quality chi co HD/SD).
    public static List<MenuItem> MenuHead(string host, string search, string c)
    {
        host = host.TrimEnd('/');
        var res = new List<MenuItem>(3)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/pubjav" }
        };
        var opts = SortsFor(search, c);
        if (opts == null) return res;   // search -> bo dong 2
        string curSort = CurrentSort(c);
        string curYear = CurrentYear(c);
        var sub = new List<MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
            sub.Add(new MenuItem(name, host + "/pubjav?c=" + HttpUtility.UrlEncode(WithSort(c, s))));
        res.Add(new MenuItem()
        {
            title = "Sắp xếp: " + SortLabel(curSort),
            playlist_url = "submenu",
            submenu = sub
        });
        var ysub = new List<MenuItem>(Years.Length + 1)
        {
            new MenuItem("Tất cả", host + "/pubjav?c=" + HttpUtility.UrlEncode(WithYear(c, curSort, "all")))
        };
        foreach (string y in Years)
            ysub.Add(new MenuItem(y, host + "/pubjav?c=" + HttpUtility.UrlEncode(WithYear(c, curSort, y))));
        res.Add(new MenuItem()
        {
            title = "Năm: " + (curYear == "all" ? "Tất cả" : curYear),
            playlist_url = "submenu",
            submenu = ysub
        });
        return res;
    }

    // Danh muc year lay tu `<select name="year">` cua form.
    public static readonly string[] Years =
    {
        "2026", "2025", "2024", "2023", "2022", "2021", "2020", "2019",
        "2018", "2017", "2016"
    };

    public static List<MenuItem> Menu(string host,
        List<(string slug, string name)> genres,
        List<(string slug, string name)> studios)
    {
        host = host.TrimEnd('/');

        // `c` = path (+ query) cua site; module `Uri()` tach `?` roi
        // them `&pg=N` — dung cach phan trang cua chinh site.
        string url(string c) => host + "/pubjav?c=" + HttpUtility.UrlEncode(c);

        var root = new List<MenuItem>(6)
        {
        };

        // Dong 3: the loai. `/genres` co `uncensored`/`amateur` nhung
        // THIEU `censored` (form dropdown moi co) nen them `Censored` vao
        // dau. `Uncensored`/`Amateur` da co san trong danh sach nen khong
        // them lai de tranh trung.
        var genreMenu = new List<MenuItem>()
        {
            new("Censored", url("movies?genre=censored&quality=all&year=all&sort=desc"))
        };

        var fetched = TaxonomyMenu(host, "genre/", genres);
        if (fetched.Count > 0)
            genreMenu.AddRange(fetched);
        else
        {
            foreach (var (slug, name) in FallbackGenres)
                genreMenu.Add(new MenuItem(name, host + "/pubjav?c=genre/" + slug));
        }

        root.Add(new MenuItem()
        {
            title = "Thể loại",
            playlist_url = "submenu",
            submenu = genreMenu
        });

        // Dong 4: hang phim
        var studioMenu = TaxonomyMenu(host, "studio/", studios);
        if (studioMenu.Count > 0)
            root.Add(new MenuItem()
            {
                title = "Hãng phim",
                playlist_url = "submenu",
                submenu = studioMenu
            });

        // Dong 5: chat luong
        root.Add(new MenuItem()
        {
            title = "Chất lượng",
            playlist_url = "submenu",
            submenu = new List<MenuItem>()
            {
                new("HD", url("movies?genre=all&quality=hd&year=all&sort=desc")),
                new("SD", url("movies?genre=all&quality=sd&year=all&sort=desc"))
            }
        });

        // Dong 6/Menu base: bo nhom "Năm" tinh — year da la dong dong
        // trong head (WithYear, giu sort hien tai). Tranh trung lap.

        return root;
    }
}
