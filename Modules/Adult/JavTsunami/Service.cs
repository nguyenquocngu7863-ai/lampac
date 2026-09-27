using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace JavTsunami;

public sealed class JavTsunamiServer
{
    public string Label { get; set; } = "";
    public string PageUrl { get; set; } = "";

    // "mp4" | "hls" | "" (chua probe). Do /vidosik gan va giu trong cache
    // 15 phut de /video biet server nao cung dang voi duoi URL da phat ra.
    public string Kind { get; set; } = "";
}

public sealed class JavTsunamiMeta
{
    public string Name { get; set; } = "";
    public string Poster { get; set; } = "";
    public string Story { get; set; } = "";
    public string Duration { get; set; } = "";
}

public static class JavTsunamiTo
{
    public static string SiteHost = "https://javtsunami.com";
    public static string HicherriHost = "https://hicherri.com";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // Trang danh sach nam trong <div class="videos-list">. Sau do con sidebar
    // ("Related videos" / "Latest") DUNG <article> + <a href="/xxx.html"> y het
    // phim — parse ca trang se ra 28 muc thay vi 20 va 8 muc do la lap giua
    // moi trang (do chung 8/20 khi so pg1 vs pg2 vs pg3). Cat tai
    // class="pagination" la het sach.
    //
    // PHAM TIM: trang chu/category co `<div class="videos-list">` bao list, nhung
    // trang TIM KIEM khong co — ket qua nam ngay sau `<!-- .page-header -->`
    // trong mot `<div>` trần. Neu chi neo vao `videos-list` thi se khui trúng
    // `videos-list` CUA WIDGET SIDEBAR ("New Release") va tra ve nham phim moi
    // nhat, mat het ket qua tim kiem. Vi vay lay `min(videos-list, article dau
    // tien)` cho ca hai kieu trang.
    public static string ListHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        int atList = html.IndexOf("class=\"videos-list\"", StringComparison.OrdinalIgnoreCase);
        int atItem = html.IndexOf("data-video-id=\"video_", StringComparison.OrdinalIgnoreCase);
        int start = atList < 0 ? atItem : atItem < 0 ? atList : Math.Min(atList, atItem);
        if (start < 0)
            return html;

        // `data-video-id` nam GIUA thang <article ...>, cat tu do se cat mat
        // chinh thang mo nen regex `<article[^>]*data-video-id=` khong khop duoc.
        // Lui ve dau <article, chi khi do that su la dau thang (giua hai dau
        // `<article` va `start` chua co dau `>` nao).
        int art = html.LastIndexOf("<article", start, StringComparison.OrdinalIgnoreCase);
        if (art > 0 && html.IndexOf('>', art) > start)
            start = art;

        var tail = html[start..];
        foreach (var stop in new[]
        {
            "class=\"pagination",
            "id=\"sidebar",
            "class=\"sidebar",
            "class=\"widget ",
            "<footer",
            "id=\"comments"
        })
        {
            int at = tail.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
            if (at > 0)
                return tail[..at];
        }

        return tail;
    }

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        // Tim kiem: WP phan trang bang /page/N? s=... (so voi category la
        // /category/x/page/N) — hai kieu khac nhau, khong gop duoc.
        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "?s=" + HttpUtility.UrlEncode(search.Trim());
            return pg > 1
                ? host + "/page/" + pg + q
                : host + "/" + q;
        }

        // Trang chu mac dinh la filter "latest" (khong co ?page=N).
        if (string.IsNullOrWhiteSpace(c) || c.Trim() == "latest")
            return pg > 1
                ? host + "/page/" + pg + "?filter=latest"
                : host + "/?filter=latest";

        // Bo loc sap xep: /?filter=most-viewed|longest|random (khong phai
        // duong dan). Phan trang van la /page/N?filter=... nhu trang chu.
        string cat = c.Trim();
        if (cat.StartsWith("filter/", StringComparison.OrdinalIgnoreCase))
        {
            string f = "?filter=" + cat["filter/".Length..].Trim('/');
            return pg > 1
                ? host + "/page/" + pg + f
                : host + "/" + f;
        }

        if (cat.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return NormalizePageUrl(cat);

        string path = "/" + c.Trim().Trim('/');
        return pg > 1
            ? host + path + "/page/" + pg
            : host + path;
    }

    public static string NormalizePageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return SiteHost + "/";

        url = HttpUtility.HtmlDecode(url.Trim());
        if (url.StartsWith("//"))
            url = "https:" + url;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = SiteHost + (url.StartsWith("/") ? url : "/" + url);

        return url;
    }

    public static bool IsSiteUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;
        return System.Uri.TryCreate(url, System.UriKind.Absolute, out var u)
            && u.Host.EndsWith("javtsunami.com", StringComparison.OrdinalIgnoreCase);
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        html = ListHtml(html);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<article[^>]*data-video-id=\"[^\"]*\"[^>]*>\\s*<a href=\"(https://javtsunami\\.com/[^\"]+\\.html)\" title=\"([^\"]{1,400})\"",
            RegexOptions.IgnoreCase))
        {
            string href = NormalizePageUrl(m.Groups[1].Value);
            if (!IsSiteUrl(href) || !seen.Add(href))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            // <a ...><div class="post-thumbnail-container"><img ... src="...">
            string poster = "";
            int at = html.IndexOf(m.Groups[1].Value, StringComparison.Ordinal);
            if (at > 0)
            {
                var im = Regex.Match(html[at..Math.Min(html.Length, at + 2000)],
                    "<img[^>]+src=\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase);
                if (im.Success)
                    poster = im.Groups[1].Value;
            }

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javtsunami",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // Trang detail dung schema.org VideoObject nen meta la du:
    //   <meta itemprop="name" content="NACT-187" />
    //   <meta itemprop="description" content="..." />   (co 2 cai, lay cai sau)
    //   <meta itemprop="duration" content="P0DT1H56M38S" />
    //   <meta itemprop="thumbnailUrl" content="https://imagerls.com/..." />
    public static JavTsunamiMeta Meta(string html)
    {
        var meta = new JavTsunamiMeta();
        if (string.IsNullOrEmpty(html))
            return meta;

        meta.Name = ItemProp(html, "name");
        meta.Poster = ItemProp(html, "thumbnailUrl");
        meta.Duration = Duration(ItemProp(html, "duration"));
        meta.Story = LastItemProp(html, "description");

        if (string.IsNullOrEmpty(meta.Poster))
            meta.Poster = MetaContent(html, "og:image");

        return meta;
    }

    static string ItemProp(string html, string prop)
    {
        var m = Regex.Match(html,
            "<meta[^>]+itemprop=\"" + prop + "\"[^>]+content=\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        if (m.Success)
            return HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
        return "";
    }

    // description bi lap 2 lan (lan 1 la tom tat ngan) — lay ban sau.
    static string LastItemProp(string html, string prop)
    {
        var all = Regex.Matches(html,
            "<meta[^>]+itemprop=\"" + prop + "\"[^>]+content=\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        if (all.Count == 0)
            return "";
        var last = all[^1];
        return HttpUtility.HtmlDecode(last.Groups[1].Value.Trim());
    }

    static string MetaContent(string html, string name)
    {
        var m = Regex.Match(html,
            "<meta[^>]+property=\"" + name + "\"[^>]+content=\"([^\"]*)\"",
            RegexOptions.IgnoreCase);
        return m.Success ? HttpUtility.HtmlDecode(m.Groups[1].Value.Trim()) : "";
    }

    // ISO-8601 duration "P0DT1H56M38S" -> "1:56:38" (gio > 24 van giu nguyen)
    static string Duration(string iso)
    {
        if (string.IsNullOrEmpty(iso))
            return "";
        if (!iso.StartsWith("P", StringComparison.OrdinalIgnoreCase))
            return iso;

        int h = 0, m = 0, s = 0;
        var hm = Regex.Match(iso, @"(\d+)H", RegexOptions.IgnoreCase);
        if (hm.Success)
            h = int.Parse(hm.Groups[1].Value);
        var mm = Regex.Match(iso, @"(\d+)M", RegexOptions.IgnoreCase);
        if (mm.Success)
            m = int.Parse(mm.Groups[1].Value);
        var sm = Regex.Match(iso, @"(\d+)S", RegexOptions.IgnoreCase);
        if (sm.Success)
            s = int.Parse(sm.Groups[1].Value);

        return h > 0
            ? $"{h}:{m:00}:{s:00}"
            : (m > 0 ? $"{m}:{s:00}" : $"{s:00}");
    }

    // Cac iframe player xep san trong <div class="video-player">. Nhan biet
    // server theo HOST (khong theo thu tu iframe — thu tu HTML doi chuyen giua
    // cac phim), roi sap xep lai theo do tin cay:
    //   Turbo (turbovidhls) > Hicherri > Vide0 > phan con lai
    //
    // Phim `category/jav-uncensored` tren chinh site cung KHONG co iframe
    // turbovidhls (chi con hicherri + vide0) nen phai co du phong.
    public static List<JavTsunamiServer> Servers(string html)
    {
        var list = new List<JavTsunamiServer>();
        if (string.IsNullOrEmpty(html))
            return list;

        int box = html.IndexOf("class=\"video-player\"", StringComparison.OrdinalIgnoreCase);
        string scope = box > 0 ? html[box..] : html;
        int cut = scope.IndexOf("<div class=\"video-tabs", StringComparison.OrdinalIgnoreCase);
        if (cut > 0)
            scope = scope[..cut];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(scope,
            "<iframe[^>]+src=\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase))
        {
            string url = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
            if (url.Length < 12 || !seen.Add(url))
                continue;

            list.Add(new JavTsunamiServer()
            {
                Label = Label(url),
                PageUrl = url
            });
        }

        return list.OrderBy(x => Rank(x.Label)).ThenBy(x => x.Label).ToList();
    }

    // Thu tu uu tien server. 0 = tot nhat.
    // Vide0 dat TRUOC Hicherri: user xac nhan tren app phim cu (khong co Turbo)
    // chi phat duoc bang DoodStream, con host cua StreamHG thi chet lien tuc.
    public static int Rank(string label)
    {
        if (string.IsNullOrEmpty(label))
            return 9;

        if (label.IndexOf("Turbo", StringComparison.OrdinalIgnoreCase) >= 0)
            return 0;
        if (label.IndexOf("Vide0", StringComparison.OrdinalIgnoreCase) >= 0)
            return 1;
        if (label.IndexOf("Hicherri", StringComparison.OrdinalIgnoreCase) >= 0)
            return 2;

        return 8;
    }

    public static bool IsHicherri(string embedUrl)
        => !string.IsNullOrEmpty(embedUrl)
           && embedUrl.IndexOf("hicherri", StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool IsVide0(string embedUrl)
        => !string.IsNullOrEmpty(embedUrl)
           && embedUrl.IndexOf("vide0", StringComparison.OrdinalIgnoreCase) >= 0;

    public static string Label(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return "";
        if (embedUrl.IndexOf("turbovid", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Turbo";
        if (embedUrl.IndexOf("hicherri", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Hicherri";
        if (embedUrl.IndexOf("vide0", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Vide0";
        return "S" + (Math.Abs(embedUrl.GetHashCode()) % 90 + 10);
    }

    // Server Turbo co HAI dang player, xu ly theo cung thu tu voi
    // JavGuruTo.StreamUrls (server TV cua jav.guru cung la turbovid):
    //
    // 1) HLS: <div id="video_player" data-hash="<master.m3u8>">   (uoc 2/3 video)
    // 2) HLS thu: URL .m3u8 absolute bat ky trong trang player
    // 3) MP4: `var urlPlay = 'https://e07.etvp.cc/uploads/<id>.mp4';`  (1/3 video)
    //
    // Thu tu nay quan trong: phai UU TIEN m3u8 truoc, neu khong video HLS co
    // ca urlPlay se bi chon nham MP4.
    public static List<string> StreamUrls(string playerHtml)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(playerHtml))
            return urls;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string u)
        {
            u = HttpUtility.HtmlDecode((u ?? "").Trim());
            if (u.StartsWith("//"))
                u = "https:" + u;
            if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase) && seen.Add(u))
                urls.Add(u);
        }

        foreach (Match m in Regex.Matches(playerHtml, "data-hash=\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);

        if (urls.Count == 0)
        {
            foreach (Match m in Regex.Matches(playerHtml, "(https?://[^\\\"'\\s<>\\\\]+\\.m3u8[^\\\"'\\s<>\\\\]*)", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
        }

        if (urls.Count == 0)
        {
            var mp4 = Regex.Match(playerHtml, "urlPlay\\s*=\\s*'([^']+\\.(?:mp4|m4v))'", RegexOptions.IgnoreCase);
            if (mp4.Success)
                Add(mp4.Groups[1].Value);
        }

        if (urls.Count == 0)
        {
            foreach (Match m in Regex.Matches(playerHtml, "(https?://[^\\\"'\\s<>\\\\]+\\.mp4[^\\\"'\\s<>\\\\]*)", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
        }

        return urls;
    }

    public static bool IsDirectMp4(string url)
        => !string.IsNullOrEmpty(url)
           && url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    // Server nao da co cach resolve. Server la chua xu ly (fapsharing,
    // streamtape...) se khong dua vao menu de tranh bam vao loi.
    public static bool IsSupported(string label)
    {
        if (string.IsNullOrEmpty(label))
            return false;

        return label.IndexOf("Turbo", StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("Hicherri", StringComparison.OrdinalIgnoreCase) >= 0
            || label.IndexOf("Vide0", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ================= DANG PHAT (mp4 / hls) =================
    //
    // App Lampa CHI ep hls.js khi URL khop regex `\.m3u8?(?:$|[?#])`
    // (SISI/plugins/sisi.js -> applyHlsType). Nap MP4 qua route duoi `.m3u8`
    // thi hls.js bao "no EXTM3U delimiter" va khong phat duoc — dung fan
    // "format error" ma user gap. Vi vay URL /vidosik phat ra phai mang duoi
    // dung theo dang thuc that cua nguon, va /video phai uu tien server cung
    // dang khi fallback.
    public const string KindMp4 = "mp4";
    public const string KindHls = "hls";

    // Dung biet ngay tu host, khong can fetch. Rong = phai mo trang player.
    public static string ServerKind(string label)
    {
        if (string.IsNullOrEmpty(label))
            return "";

        if (label.IndexOf("Vide0", StringComparison.OrdinalIgnoreCase) >= 0)
            return KindMp4;   // DoodStream chi phuc vu mp4

        if (label.IndexOf("Hicherri", StringComparison.OrdinalIgnoreCase) >= 0)
            return KindHls;   // StreamHG chi co master.m3u8

        return "";           // Turbo: HLS 2/3 video, MP4 1/3 -> tinh sau
    }

    public static string ServerKind(string label, string pageUrl)
        => string.IsNullOrEmpty(pageUrl) ? ServerKind(label) : ServerKind(pageUrl);

    // Server dang 0 can fetch trang player moi biet. Deadline ngan de
    // /vidosik khong treo (app cho 30s, con phai con phan con lai).
    public static async Task<string> ServerKindAsync(string pageUrl, int maxTime = 4, long deadline = 0)
    {
        if (IsVide0(pageUrl))
            return KindMp4;

        if (IsHicherri(pageUrl))
            return KindHls;

        string player = await CurlGetRetry(pageUrl, SiteHost + "/", null, 3, maxTime, deadline);
        if (string.IsNullOrEmpty(player))
            return "";

        foreach (string media in StreamUrls(player))
            return IsDirectMp4(media) ? KindMp4 : KindHls;

        return "";
    }

    // ================= HICHERRI (StreamHG) =================
    //
    // Trang player nap code bang PACKER roi con obfuscate them:
    //   eval(function(p,a,c,k,e,d){...}('<code>', 36, 498, '<bang token>'))
    // Giai packer xong moi thay
    //   var links={"hls4":"...","hls3":"...","hls2":"..."}
    // va cau hinh jwplayer lay `links.hls4||links.hls3||links.hls2`.
    //
    // Ca 3 tro ve cung mot master HLS nhung o 3 host khac nhau:
    //   hls4  /stream/<srv>/<hash>/<ts>/<file_id>/master.m3u8  (RELATIVE,
    //         tren chinh hicherri.com — nhanh, khong can token)
    //   hls3  host CDN `.cfd` (Cloudflare) + `master.txt`, KHONG token
    //   hls2  host CDN `.cdn-centaurus.com` + `master.m3u8?t=<token>`
    //
    // Host CDN chet ngau nhau (hls4 chet thi hls3, hls3 chet thi hls2) nen
    // KHONG probe tuan tu — probe song song va lay cai nao tra #EXTM3U.
    public static List<string> HicherriMasters(string playerHtml)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(playerHtml))
            return res;

        var m = Regex.Match(playerHtml, PackerRx, RegexOptions.IgnoreCase);
        if (!m.Success)
            return res;

        int radix = 2;
        int count = 0;
        if (!int.TryParse(m.Groups[2].Value, out radix) || radix < 2)
            return res;
        if (!int.TryParse(m.Groups[3].Value, out count) || count < 1)
            return res;

        string[] keys = m.Groups[4].Value.Split('|');
        string code = m.Groups[1].Value;

        // Packer thay TAT CA token theo thu tu GIAM DAN — thu tu nay bat buoc,
        // sai thu tu se ra chuoi sai nghia.
        for (int i = count - 1; i >= 0; i--)
        {
            if (i >= keys.Length || string.IsNullOrEmpty(keys[i]))
                continue;

            string token = ToBase36(i, radix);
            string val = keys[i];
            code = Regex.Replace(code, @"\b" + Regex.Escape(token) + @"\b",
                _ => val.Replace("$", "$$"));
        }

        var lm = Regex.Match(code, @"var\s+links\s*=\s*\{([^;]+)\}", RegexOptions.IgnoreCase);
        if (!lm.Success)
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in new[] { "hls4", "hls3", "hls2" })
        {
            var km = Regex.Match(lm.Groups[1].Value, "\"" + key + "\":\"([^\"]+)\"");
            if (!km.Success)
                continue;

            string u = HttpUtility.HtmlDecode(km.Groups[1].Value.Trim());
            if (u.StartsWith("//"))
                u = "https:" + u;
            else if (u.StartsWith("/"))
                u = HicherriHost + u;

            if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(u))
                res.Add(u);
        }

        return res;
    }

    // Packer cua hicherri ghi `new RegExp('\\b'+...)` — trong file THAT la
    // 3 ky tu: backslash, backslash, 'b'. Regex phai khop `(\\)+` (mot hoac
    // nhieu backslash) chu khong phai `\\b` se khong bao gio khop.
    const string PackerRx = @"eval\(function\(p,a,c,k,e,d\)\{while\(c--\)if\(k\[c\]\)p=p\.replace\(new RegExp\('(?:\\\\)+b'\+c\.toString\(a\)\+'(?:\\\\)+b','g'\),k\[c\]\);return p\}\('([\s\S]*?)',(\d+),(\d+),'([\s\S]*?)'\.split\('\|'\)\)";

    static string ToBase36(int v, int radix)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        if (v == 0)
            return "0";

        var sb = new System.Text.StringBuilder();
        while (v > 0)
        {
            sb.Insert(0, digits[v % radix]);
            v /= radix;
        }

        return sb.ToString();
    }

    // Server Turbo: <iframe src="https://turbovidhls.com/t/6aaef8ebd3808"> ->
    // trang player co <div id="video_player" data-hash="<master.m3u8>">.
    //
    // KHONG du doan path CDN tu id tren URL /t/ — id do KHAC id trong path m3u8
    // (vi du /t/6aaef8ebd3808 -> data3/6aaedb5577e0c/6aaedb5577e0c.m3u8).
    // Chi tin data-hash cua trang player.
    public static string DataHash(string playerHtml)
    {
        if (string.IsNullOrEmpty(playerHtml))
            return null;

        var m = Regex.Match(playerHtml, "data-hash=\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase);
        if (m.Success)
            return HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());

        return null;
    }

    // Master 2 LEVEL: 3 variant 480/720/1080, moi variant tro toi mot playlist
    // media VOD tren gs*.turbosplayer.com (~130KB, 1450 segment), segment o
    // lh3.googleusercontent.com.
    //
    // App Lampa nap MASTER roi hls.js tu chay ABR: moi lan doi level la phai
    // tai them mot playlist con o host khac — chinh tai do hls.js bao
    // "Found no media in msn N of main playlist". Nen tach master o server roi
    // tra playlist media cua variant cao nhat: hls.js nap 1 lan xong.
    public static async Task<List<(string url, string tag)>> MasterVariants(
        string master, int max = 3, int maxTime = 7, long deadline = 0, string referer = null)
    {
        var res = new List<(string url, string tag)>();
        if (string.IsNullOrEmpty(master))
            return res;

        string body = await CurlGetRetry(master, referer, "#EXTM3U", 3, maxTime, deadline);
        if (string.IsNullOrEmpty(body))
            return res;

        return VariantsFrom(master, body, max);
    }

    public static List<(string url, string tag)> VariantsFrom(
        string master, string body, int max = 3)
    {
        var res = new List<(string url, string tag)>();
        if (string.IsNullOrEmpty(master) || string.IsNullOrEmpty(body))
            return res;

        var found = new List<(int px, string url)>();
        string dir = null;
        try
        {
            int at = master.LastIndexOf('/');
            if (at > 8)
                dir = master[..(at + 1)];
        }
        catch { }

        foreach (Match m in Regex.Matches(body,
            "#EXT-X-STREAM-INF:([^\\r\\n]*)\\r?\\n\\s*(\\S+)", RegexOptions.IgnoreCase))
        {
            // Nhan theo CHIEU CAO (1080p) chu khong phai chieu rong (1920p).
            var px = Regex.Match(m.Groups[1].Value, @"RESOLUTION=\d+x(\d+)", RegexOptions.IgnoreCase);
            string u = m.Groups[2].Value.Trim();
            if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase) && dir != null)
                u = dir + u.TrimStart('/');
            found.Add((px.Success ? int.Parse(px.Groups[1].Value) : 0, u));
        }

        if (found.Count == 0)
            return res;

        foreach (var x in found.OrderByDescending(x => x.px).Take(max))
            res.Add((x.url, x.px > 0 ? x.px + "p" : ""));

        return res;
    }

    // ================= VIDE0 (DoodStream) =================
    //
    // iframe `https://vide0.net/e/<id>` tra 301 sang mot host DOI, doi theo
    // lan tai (vide0.net -> playmogo.com -> ...). API DoodStream nam tren
    // CHINH host cuoi do:
    //   GET https://<host-cuoi>/pass_md5/<hash>/<id>?referer=javtsunami.com
    // tra ve THANG URL mp4 dang text thuan (khong phai JSON).
    // Tra (url da kem ?token&expiry, referer can gui) — ca hai deu BAT BUOC,
    // xem ghi chuc ben trong.
    public static async Task<(string url, string referer)> DoodSourceAsync(
        string embedUrl, int maxTime = 6, long deadline = 0)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return (null, null);

        var got = await CurlGetUrl(embedUrl, SiteHost + "/", maxTime, true);
        string body = got.body;
        if (string.IsNullOrEmpty(body))
            return (null, null);

        var m = Regex.Match(body, @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
        if (!m.Success)
            return (null, null);

        // Host cuoi = tien to cua URL sau redirect, cat tai "/e/".
        string baseUrl = "";
        if (!string.IsNullOrEmpty(got.finalUrl))
        {
            int at = got.finalUrl.IndexOf("/e/", StringComparison.OrdinalIgnoreCase);
            if (at > 8)
                baseUrl = got.finalUrl[..at];
        }

        if (string.IsNullOrEmpty(baseUrl))
            return (null, null);

        // -L da di den host cuoi roi, dung chinh host do
        string api = $"{baseUrl}/{m.Groups[1].Value}?referer=javtsunami.com";
        string src = await CurlGetRetry(api, SiteHost + "/", null, 4, maxTime, deadline);
        src = (src ?? "").Trim();

        if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        // CDN CHI phuc vu khi co CA HAI thu sau, thieu mot thu la 302 sang
        // `*.dood.video` — host do tro ve 127.0.0.1 ca public DNS nen chet:
        //   1) URL phai co query string
        //   2) request phai co header Referer khop host embed
        // `makePlay()` ben trang web them `<random10>?token=<tok>&expiry=<unix_ms>`
        // va player dat `src = data + makePlay()`. Chi can MOT query bat ky
        // (token sai/expired van 206) nhung dung y chang cho an toan.
        // Token = phan cuoi cua duong dan pass_md5.
        string token = m.Groups[1].Value.Split('/')[^1];
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string q = $"?token={token}&expiry={expiry}";

        return (src + (src.Contains('?') ? "&" : "") + q, baseUrl);
    }

    // Hicherri: 3 master candidate (hls4/hls3/hls2) o 3 host khac nhau, host
    // nao cung chet ngau nhieu theo tung thoi diem nen PHAI probe SONG SONG —
    // tuan tu se dot het deadline (do treo het --max-time cua host chet).
    //
    //   vong 1: master co tra #EXTM3U khong            (3 host cung luc)
    //   vong 2: playlist media cua variant cao nhat co #EXTINF khong
    //            (hls3 bien the khong co token -> master 200 nhung playlist 403,
    //             nen phai kiem playlist chu duyet)
    //
    // Vong 2 chi 1 lan thu moi host: host vong 1 tra #EXTM3U thi san thong
    // tin server, khong can doi lane http2/http1.1 them nua.
    public static async Task<List<(string url, string tag)>> HicherriVariantsAsync(
        List<string> masters, int maxTime = 3, long deadline = 0)
    {
        var empty = new List<(string url, string tag)>();
        if (masters == null || masters.Count == 0)
            return empty;

        string refHost = HicherriHost + "/";

        // Cho nhieu thu hon: `hls4` nam tren chinh hicherri.com (tokenless,
        // song lai) nen no cham hon `hls3` (CDN `.cfd`) — quy gan 2 lan / 3s
        // thi hay lam no roi sang nhanh CDN, ma nhanh do chi song vai giay
        // roi tra 404. Time 2 vong = 5s + 5s, duoi sub-deadline 12s.
        var got = await Task.WhenAll(masters.Select(x =>
            CurlGetRetry(x, refHost, "#EXTM3U", 3, maxTime, deadline)));

        var plan = new List<string>();
        var vars = new List<List<(string url, string tag)>>();
        for (int i = 0; i < masters.Count; i++)
        {
            if (string.IsNullOrEmpty(got[i]))
            {
                Console.WriteLine($"JavTsunami: hicherri master fail {masters[i]}");
                continue;
            }

            var v = VariantsFrom(masters[i], got[i], 3);
            plan.Add(masters[i]);
            vars.Add(v);
        }

        if (plan.Count == 0)
            return empty;

        var tops = plan.Select((p, i) => vars[i].Count > 0 ? vars[i][0].url : p).ToArray();
        var live = await Task.WhenAll(tops.Select(x =>
            CurlGetRetry(x, refHost, "#EXTINF", 2, maxTime, deadline)));

        for (int i = 0; i < plan.Count; i++)
        {
            if (string.IsNullOrEmpty(live[i]))
            {
                Console.WriteLine($"JavTsunami: hicherri playlist fail {tops[i]}");
                continue;
            }

            Console.WriteLine($"JavTsunami: hicherri ok master[{i}] {plan[i]}");

            if (vars[i].Count > 0)
                return vars[i];

            return new List<(string url, string tag)> { (plan[i], "1080p") };
        }

        return empty;
    }

    static void PrepCurlEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("LD_PRELOAD");
        psi.Environment.Remove("LD_LIBRARY_PATH");

        string curl = "/data/data/com.termux/files/usr/bin/curl";
        psi.FileName = File.Exists(curl) ? curl : "curl";
    }

    public static async Task<string> CurlGet(string url, string referer, int maxTime = 25, bool http2 = true)
        => (await CurlRun(url, referer, maxTime, http2, false)).body;

    // Tra them URL CUOI sau khi -L di het redirect. Can cho vide0.net ->
    // playmogo.com (host doi theo lan tai) de goi API pass_md2 cua DoodStream.
    public static async Task<(string body, string finalUrl)> CurlGetUrl(
        string url, string referer, int maxTime = 25, bool http2 = true)
        => await CurlRun(url, referer, maxTime, http2, true);

    static async Task<(string body, string finalUrl)> CurlRun(
        string url, string referer, int maxTime, bool http2, bool wantFinal)
    {
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
            psi.ArgumentList.Add(http2 ? "--http2" : "--http1.1");
            psi.ArgumentList.Add("--compressed");
            psi.ArgumentList.Add("--connect-timeout");
            psi.ArgumentList.Add("10");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add(maxTime.ToString());
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add(ChromeUA);
            if (!string.IsNullOrEmpty(referer))
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(referer);
            }
            if (wantFinal)
                psi.ArgumentList.Add("-w");
            if (wantFinal)
                psi.ArgumentList.Add("\n@@FINAL@@%{url_effective}");

            psi.ArgumentList.Add(url);

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return (null, null);

                string stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                    return (null, null);

                if (!wantFinal)
                    return (stdout, null);

                const string sep = "\n@@FINAL@@";
                int at = stdout.LastIndexOf(sep, StringComparison.Ordinal);
                if (at < 0)
                    return (stdout, null);

                return (stdout[..at], stdout[(at + sep.Length)..].Trim());
            }
        }
        catch
        {
            return (null, null);
        }
    }

    // deadline (ms epoch cua Environment.TickCount64): client Lampa bo sau 30s
    // nen moi lan fetch bi cat ngan theo thoi gian con lai.
    //
    // turbovidhls.com chap chon (code=000 ca khi shell goi tay), ca http2 va
    // http1.1: probe 12 lan deu co lan chet lane nay song lane kia. Vi vay
    // xen ke http2/http1.1 theo lan thu: lan HTTP/2 treo thi lan HTTP/1.1
    // ngay sau do qua (va nguoc lai), thay vi cho het attempts tren mot lane.
    public static async Task<string> CurlGetRetry(
        string url, string referer, string marker,
        int attempts = 3, int maxTime = 25, long deadline = 0, bool http2 = true)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (deadline > 0)
            {
                long left = deadline - Environment.TickCount64;
                if (left < 2500)
                    return null;
                if (left / 1000 < maxTime)
                    maxTime = (int)(left / 1000);
            }

            if (i > 0)
                await Task.Delay(300 * i);

            bool h2 = (i % 2 == 0) ? http2 : !http2;
            string body = await CurlGet(url, referer, maxTime, h2);
            if (!string.IsNullOrWhiteSpace(body)
                && (string.IsNullOrEmpty(marker) || body.Contains(marker)))
                return body;
        }

        return null;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        var genres = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("JAV Censored", host + "/javtsunami?c=category/jav-censored"),
            new("JAV Uncensored", host + "/javtsunami?c=category/jav-uncensored"),
            new("Chinese AV", host + "/javtsunami?c=category/chinese"),
            new("Phụ đề", host + "/javtsunami?c=tag/jav-eng-sub"),
            new("Mới phát hành", host + "/javtsunami?c=category/new-release"),
            new("Trending", host + "/javtsunami?c=category/trending"),
            new("Nổi bật", host + "/javtsunami?c=category/featured"),
            new("Hot", host + "/javtsunami?c=category/hot-jav"),
            new("Sắp ra mắt", host + "/javtsunami?c=category/upcoming"),
            new("Cosplay", host + "/javtsunami?c=category/cosplay"),
        };

        var views = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Xem nhiều", host + "/javtsunami?c=filter/most-viewed"),
            new("Lâu nhất", host + "/javtsunami?c=filter/longest"),
            new("Ngẫu nhiên", host + "/javtsunami?c=filter/random"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/javtsunami"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javtsunami"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Lọc",
                playlist_url = "submenu",
                submenu = views
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres
            }
        };
    }
}
