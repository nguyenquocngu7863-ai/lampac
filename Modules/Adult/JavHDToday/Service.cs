using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace JavHDToday;

public sealed class JavHDTodayServer
{
    public string Label { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string Kind { get; set; } = "";
}

public static class JavHDTodayTo
{
    public static string SiteHost = "https://javhd.today";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    public static string Uri(
        string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        // Tim kiem: /search/video/?s=<q>[&o=<sort>]&ajax=1[&page=N].
        // `c` dang "o=<sort>" khi user chon sort o dong 2 (MenuHead ghep).
        if (!string.IsNullOrWhiteSpace(search))
        {
            string o = NormalizeSort(SearchO(c));
            string u = host + "/search/video/?s="
                + HttpUtility.UrlEncode(search.Trim())
                + (string.IsNullOrEmpty(o) ? "" : "&o=" + o)
                + "&ajax=1";
            return pg > 1 ? u + "&page=" + pg : u;
        }

        // Home (c rong) = /recent/ + browse_videos.
        if (string.IsNullOrWhiteSpace(c))
            return host + "/recent/?ajax=browse_videos&page=" + Math.Max(1, pg);

        // Tach sort o segment cuoi: <base>/<sort>/
        string raw = c.Trim().Trim('/');
        int qi = raw.IndexOf('?');
        if (qi >= 0) raw = raw.Substring(0, qi);
        raw = raw.Trim('/');
        string low = raw.ToLowerInvariant();

        // 7 list toan cuc + popular goc: browse_videos.
        if (IsGlobalList(low))
        {
            string gbu = host + "/" + low + "/";
            return gbu + "?ajax=browse_videos&page=" + Math.Max(1, pg);
        }

        string basePath = BasePath(raw);
        string last = low.Equals(basePath.ToLowerInvariant(),
            StringComparison.Ordinal) ? ""
            : low.Substring(basePath.Length).Trim('/');
        string sort = NormalizeSort(last);

        // Home sort: /<sort>/?ajax=browse_videos (do live 2026-10-07:
        // /watched/ + browse_videos ra 28 item, /recent/watched/ chet).
        if (basePath.Equals("recent", StringComparison.OrdinalIgnoreCase))
        {
            string b = string.IsNullOrEmpty(sort) ? "recent" : sort;
            return host + "/" + b + "/?ajax=browse_videos&page=" + Math.Max(1, pg);
        }

        // Genre <slug>[/<sort>]/: sort THAT dang PATH (do live 2026-10-07:
        // /big-tits/popular/?ajax=1 doi that, con ?ajax=1&sort=popular
        // tra ve y nhu base). Trang 2: /<slug>/<sort>/recent/<N>/?ajax=1.
        string gbase = basePath.Trim('/') + "/"
            + (string.IsNullOrEmpty(sort) ? "" : sort + "/");
        string gurl = host + "/" + gbase;
        if (pg > 1)
            return gurl + "recent/" + pg + "/?ajax=1";
        return gurl + "?ajax=1";
    }

    // 7 list toan cuc + popular goc chay ?ajax=browse_videos.
    static bool IsGlobalList(string path)
    {
        string q = path.Trim('/').ToLowerInvariant();
        return q == "recent" || q == "releaseday"
            || q == "popular/today" || q == "popular/week"
            || q == "popular/month" || q == "popular/year"
            || q == "popular";
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string json)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(json))
            return playlists;

        string html = json;
        // JSON dung: {"status":..,"html":"...","pagination":..}. Dung
        // JsonDocument de lay "html" (giai escape chuan, ke ca \/ -> /).
        // Regex + Regex.Unescape cu VO HIEU LUC tu khi site escape "/" :
        // href thanh \/369778\/...\/ va <\/li> lam regex </li> khong khop
        // -> 0 item -> 503 (bug 2026-10-07).
        bool extracted = false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("html", out var he)
                && he.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                html = he.GetString();
                extracted = true;
            }
        }
        catch { }
        if (!extracted)
        {
            try
            {
                var m = Regex.Match(json, "\"html\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                if (m.Success)
                    html = Regex.Unescape(m.Groups[1].Value);
            }
            catch { }
            // Regex.Unescape khong chac giai \/ -> / tren moi runtime.
            if (html.Contains("\\/"))
                html = html.Replace("\\/", "/");
        }

        var seen = new HashSet<string>();
        foreach (Match cm in Regex.Matches(html, "<li id=\"video-(\\d+)\">(.*?)</li>", RegexOptions.Singleline))
        {
            string b = cm.Groups[2].Value;
            if (b.Length > 3000)
                b = b.Substring(0, 3000);

            var hm = Regex.Match(b, "<a href=\"(/\\d+/[^\\\"]+/)\"[^>]*title=\"([^\"]{3,300})\"");
            if (!hm.Success)
                hm = Regex.Match(b, "<a href=\"(/\\d+/[^\\\"]+/)\"");
            if (!hm.Success)
                continue;
            string href = SiteHost + hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            string name = hm.Groups.Count > 2 ? HttpUtility.HtmlDecode(hm.Groups[2].Value.Trim()) : ("Video " + cm.Groups[1].Value);
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, "<img[^>]+src=\"(https?://[^\"]+)\"");
            if (im.Success)
                poster = im.Groups[1].Value;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javhdtoday",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // Server Turbo (turbovid, cung ho voi JavGuru TV / JavTsunami
    // Turbo): player co `data-hash="<master.m3u8>"`, du phong .m3u8
    // absolute, cuoi cung `var urlPlay = '...mp4'`.
    // UU TIEN m3u8 truoc — video HLS co ca urlPlay se bi chon nham.
    public static List<string> StreamUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string u)
        {
            u = HttpUtility.HtmlDecode((u ?? "").Trim());
            if (u.StartsWith("//"))
                u = "https:" + u;
            if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && seen.Add(u))
                urls.Add(u);
        }

        foreach (Match m in Regex.Matches(html,
            "data-hash=\"(https?://[^\"]+)\"", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);

        if (urls.Count == 0)
        {
            foreach (Match m in Regex.Matches(html,
                "(https?://[^\"'\\s<>\\\\]+\\.m3u8[^\"'\\s<>\\\\]*)",
                RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
        }

        if (urls.Count == 0)
        {
            var mp4 = Regex.Match(html,
                "urlPlay\\s*=\\s*'([^']+\\.(?:mp4|m4v))'",
                RegexOptions.IgnoreCase);
            if (mp4.Success)
                Add(mp4.Groups[1].Value);
        }

        return urls;
    }

    public static bool IsDirectMp4(string url)
        => !string.IsNullOrEmpty(url)
            && url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    // Thu tu uu tien server (0 = tot nhat). Ran nhau voi JavGuru/JavTsunami:
    // DoodStream ben nhat, Turbo nhanh nhung gateway hay 520/chap chon,
    // Cloudwish/Mycloudz packer HLS on dinh, StreamBeast la ma `#...` vo
    // dung, server la thi khong dua vao menu.
    // DoodStream NHAT DINH truoc Turbo: log user truoc day (`srv=Vide0 n=1`
    // phat OK trong khi turbo/hicherri `n=0`) — ben hon nhanh.
    public static int Rank(string label)
    {
        if (string.IsNullOrEmpty(label))
            return 9;

        if (Has(label, "Dood"))
            return 0;
        if (Has(label, "Turbo"))
            return 1;
        if (Has(label, "Cloudwish"))
            return 2;
        if (Has(label, "Mycloudz"))
            return 3;
        if (Has(label, "Myserver"))
            return 4;
        if (Has(label, "Topserver"))
            return 5;
        if (Has(label, "Maxcloud"))
            return 6;
        if (Has(label, "Bpserver"))
            return 7;

        return 8;
    }

    // Server nao da co cach resolve thi moi dua vao menu. StreamBeast
    // (`streambeast.upn.one/#...`, khong path media) va host la khac thi
    // khong xu ly — bam vao bao loi con hon treo 12s.
    public static bool IsSupported(string label)
    {
        if (string.IsNullOrEmpty(label))
            return false;

        return Has(label, "Dood") || Has(label, "Turbo")
            || Has(label, "Cloudwish") || Has(label, "Mycloudz")
            || IsJavhdzButton(label);
    }

    // 4 nut javhdz tren trang detail (data-name tren <button>).
    // Upnshare (streambeast `#...`) khong resolve duoc nen loai.
    public static bool IsJavhdzButton(string label)
    {
        if (string.IsNullOrEmpty(label))
            return false;

        return Has(label, "Myserver") || Has(label, "Topserver")
            || Has(label, "Maxcloud") || Has(label, "Bpserver");
    }

    static bool Has(string s, string v)
        => s.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool IsDood(string embedUrl)
        => !string.IsNullOrEmpty(embedUrl)
            && embedUrl.IndexOf("dooood.com",
                StringComparison.OrdinalIgnoreCase) >= 0;

    public static bool IsCloud(string embedUrl)
        => !string.IsNullOrEmpty(embedUrl)
            && (embedUrl.IndexOf("cloudwish",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || embedUrl.IndexOf("mycloudz",
                    StringComparison.OrdinalIgnoreCase) >= 0);

    public static bool IsJavhdz(string embedUrl)
        => !string.IsNullOrEmpty(embedUrl)
            && (Has(embedUrl, "javhdz")
                || Has(embedUrl, "savedvids"));

    public static string Label(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return "";

        if (Has(embedUrl, "dooood.com"))
            return "DoodStream";
        if (Has(embedUrl, "turbovid"))
            return "Turbo";
        if (Has(embedUrl, "cloudwish"))
            return "Cloudwish";
        if (Has(embedUrl, "mycloudz"))
            return "Mycloudz";

        return "";
    }

    // Danh sach server tu trang detail, sap xep theo Rank. Lay ca
    // `data-embed` (base64 url don) va `data-embeds` (base64 json array
    // cua embed du phong javhdz) — chi giu host da biet resolve.
    public static List<JavHDTodayServer> Servers(string html)
    {
        var list = new List<JavHDTodayServer>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string u, string name = null)
        {
            if (string.IsNullOrEmpty(u) || !seen.Add(u))
                return;

            string label = string.IsNullOrEmpty(name)
                ? Label(u) : name;
            if (string.IsNullOrEmpty(label) || !IsSupported(label))
                return;

            list.Add(new JavHDTodayServer()
            {
                Label = label,
                PageUrl = u,
                Kind = ServerKind(label)
            });
        }

        foreach (Match m in Regex.Matches(html, "data-embed=\"([^\"]+)\""))
        {
            try
            {
                string u = Encoding.UTF8.GetString(
                    Convert.FromBase64String(m.Groups[1].Value)).Trim();
                Add(u, null);
            }
            catch { }
        }

        // 4 nut javhdz (Myserver/Topserver/Maxcloud/Bpserver): moi nut
        // giu mot array mirror (embed.php, embed_server7/2/5.php) —
        // lay URL dau tien, nhan theo data-name tren nut.
        foreach (Match b in Regex.Matches(html,
            "<button[^>]+class=\"button_choice_server\"[^>]*>",
            RegexOptions.IgnoreCase))
        {
            string tag = b.Value;
            var nm = Regex.Match(tag, "data-name=\"([^\"]+)\"",
                RegexOptions.IgnoreCase);
            var em = Regex.Match(tag, "data-embeds=\"([^\"]+)\"");
            if (!nm.Success || !em.Success)
                continue;

            string name = nm.Groups[1].Value.Trim();
            if (!IsJavhdzButton(name))
                continue;

            try
            {
                string arr = Encoding.UTF8.GetString(
                    Convert.FromBase64String(em.Groups[1].Value));
                var u = Regex.Match(arr, "\"(https?://[^\"\\\\]+)\"");
                if (u.Success)
                    Add(u.Groups[1].Value.Replace("\\/", "/"), name);
            }
            catch { }
        }

        foreach (Match m in Regex.Matches(html, "data-embeds=\"([^\"]+)\""))
        {
            try
            {
                string arr = Encoding.UTF8.GetString(
                    Convert.FromBase64String(m.Groups[1].Value));
                foreach (Match u in Regex.Matches(arr,
                    "\"(https?://[^\"\\\\]+)\""))
                    Add(u.Groups[1].Value.Replace("\\/", "/"));
            }
            catch { }
        }

        return list.OrderBy(x => Rank(x.Label)).ThenBy(x => x.Label).ToList();
    }

    // ================= DANG PHAT (mp4 / hls) =================
    // Giong JavGuru/JavTsunami: app chi ep hls.js khi URL co duoi `.m3u8`.
    public const string KindMp4 = "mp4";
    public const string KindHls = "hls";

    // Turbo la DA DANG (phim thi data-hash HLS, phim thi chi
    // urlPlay mp4) nen phai mo player do moi phim. Dood=mp4,
    // Cloud/javhdz=HLS biet ngay tu host. Probe 2 lan x 3s,
    // vua deadline 4s cua /vidosik.
    public static async Task<string> ServerKindAsync(
        string pageUrl, int maxTime = 3, long deadline = 0)
    {
        if (IsDood(pageUrl))
            return KindMp4;

        if (IsCloud(pageUrl) || IsJavhdz(pageUrl))
            return KindHls;

        string player = await CurlGetRetry(
            pageUrl, SiteHost + "/", null, 2, maxTime, deadline);
        if (string.IsNullOrEmpty(player))
            return "";

        foreach (string media in StreamUrls(player))
            return IsDirectMp4(media) ? KindMp4 : KindHls;

        return "";
    }

    // Biet ngay tu host, khong can fetch. DoodStream=mp4, con lai=HLS.
    // Turbo thuc ra DA DANG nen chi dung tam o Servers(); /vidosik do
    // lai bang ServerKindAsync o tren.
    public static string ServerKind(string label)
    {
        if (string.IsNullOrEmpty(label))
            return "";

        if (Has(label, "Dood"))
            return KindMp4;

        if (Has(label, "Turbo") || Has(label, "Cloudwish")
            || Has(label, "Mycloudz") || IsJavhdzButton(label))
            return KindHls;

        return "";
    }

    // Embed javhdz (Myserver/Topserver/Maxcloud/Bpserver): trang
    // Plyr co `var FIRST = {"playlist":"/tb_playlist.php?t=..."}`.
    // Ghep voi host embed -> playlist media (1 level, segment
    // absolute tren googleusercontent, tien to PNG do hls.js cat).
    public static string JavhdzPlaylist(string html, string embedUrl)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html,
            "\"playlist\"\\s*:\\s*\"([^\"]+)\"");
        if (!m.Success)
            return null;

        string p = m.Groups[1].Value.Trim();
        if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return p;

        try
        {
            var u = new Uri(embedUrl);
            return u.GetLeftPart(UriPartial.Authority) + p;
        }
        catch
        {
            return null;
        }
    }
    // Cloudwish / Mycloudz: player nap code bang PACKER base36 (giong
    // StreamHG cua JavTsunami/JavGuru-SB). Giai xong moi thay
    //   var links={"hls4":"...","hls3":"...","hls2":"..."}
    // hls4 la path relative /stream/... tren chinh host player, hls3 la
    // master.txt, hls2 la master.m3u8 co token. Uu tien hls4>hls3>hls2
    // nhu player that.
    public static List<string> CloudMasters(string html, string playerHost)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(html))
            return res;

        string code = Unpack(html);
        if (string.IsNullOrEmpty(code))
            return res;

        var lm = Regex.Match(code, @"var\s+links\s*=\s*\{([^;]+)\}",
            RegexOptions.IgnoreCase);
        if (!lm.Success)
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in new[] { "hls4", "hls3", "hls2" })
        {
            var km = Regex.Match(lm.Groups[1].Value,
                "\"" + key + "\":\"([^\"]+)\"");
            if (!km.Success)
                continue;

            string u = HttpUtility.HtmlDecode(km.Groups[1].Value.Trim());
            if (u.StartsWith("//"))
                u = "https:" + u;
            else if (u.StartsWith("/")
                && !string.IsNullOrEmpty(playerHost))
                u = playerHost.TrimEnd('/') + u;

            if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen.Add(u))
                res.Add(u);
        }

        return res;
    }

    // Giai eval(p,a,c,k,e,d) kieu Dean Edwards. Thay token theo thu tu
    // GIAM DAN, quy doi tu so nguyen (ToBase), dung MatchEvaluator de
    // token co '$' khong bi hieu la group reference.
    public static string Unpack(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html,
            @"eval\(function\(p,a,c,k,e,d\)\{[^}]*\}"
            + @"\('([\s\S]*?)',(\d+),(\d+),"
            + @"'([\s\S]*?)'\.split\('\|'\)\)\)",
            RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string p = m.Groups[1].Value;
        if (!int.TryParse(m.Groups[2].Value, out int a)
            || !int.TryParse(m.Groups[3].Value, out int c))
            return null;

        var k = m.Groups[4].Value.Split('|');

        for (int i = c - 1; i >= 0; i--)
        {
            if (i >= k.Length || string.IsNullOrEmpty(k[i]))
                continue;

            p = Regex.Replace(p, @"\b" + ToBase(i, a) + @"\b",
                (Match _) => k[i]);
        }

        return p;
    }

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

    // ================= DOOD (DoodStream) =================
    //
    // Embed `dooood.com/e/<id>` 301 sang host doi theo lan tai
    // (dooood.com -> playmogo.com -> ...). API nam tren host CUOI:
    //   GET https://<host-cuoi>/pass_md5/<hash>/<id>?referer=javhd.today
    // tra ve THANG URL mp4 dang text thuan (khong phai JSON).
    //
    // CDN CHI phuc vu khi co CA HAI: URL co query string (`?token&expiry`)
    // VA request co Referer khop host embed. Thieu mot la 302 sang
    // `*.dood.video` (sinkhole 127.0.0.1 ca public DNS) — chet.
    public static async Task<(string url, string referer)> DoodSourceAsync(
        string embedUrl, int maxTime = 6, long deadline = 0)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return (null, null);

        var got = await CurlGetUrl(embedUrl, SiteHost + "/", maxTime);
        if (string.IsNullOrEmpty(got.body))
            return (null, null);

        var m = Regex.Match(got.body,
            @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
        if (!m.Success)
            return (null, null);

        string baseUrl = "";
        if (!string.IsNullOrEmpty(got.finalUrl))
        {
            int at = got.finalUrl.IndexOf("/e/",
                StringComparison.OrdinalIgnoreCase);
            if (at > 8)
                baseUrl = got.finalUrl[..at];
        }

        if (string.IsNullOrEmpty(baseUrl))
            return (null, null);

        string api =
            $"{baseUrl}/{m.Groups[1].Value}?referer=javhd.today";
        string src = await CurlGetRetry(api, SiteHost + "/",
            null, 4, maxTime, deadline);
        src = (src ?? "").Trim();

        if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        string token = m.Groups[1].Value.Split('/')[^1];
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return (src + (src.Contains('?') ? "&" : "?")
            + $"token={token}&expiry={expiry}", baseUrl);
    }

    // Master 2 LEVEL cua Turbo (giong JavGuru TV / JavTsunami Turbo):
    // tach o server, tra playlist media cua variant cao nhat de app
    // khong phai doi level (hls.js bao "Found no media in msn N").
    public static async Task<List<(string url, string tag)>> MasterVariants(
        string master, int max = 3, int maxTime = 7, long deadline = 0,
        string referer = null)
    {
        var res = new List<(string url, string tag)>();
        if (string.IsNullOrEmpty(master))
            return res;

        string body = await CurlGetRetry(master, referer,
            "#EXTM3U", 3, maxTime, deadline);
        if (string.IsNullOrEmpty(body))
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
            "#EXT-X-STREAM-INF:([^\\r\\n]*)\\r?\\n\\s*(\\S+)",
            RegexOptions.IgnoreCase))
        {
            var px = Regex.Match(m.Groups[1].Value,
                @"RESOLUTION=\d+x(\d+)", RegexOptions.IgnoreCase);
            string u = m.Groups[2].Value.Trim();
            if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && dir != null)
                u = dir + u.TrimStart('/');
            found.Add((px.Success ? int.Parse(px.Groups[1].Value) : 0, u));
        }

        if (found.Count == 0)
            return res;

        foreach (var x in found.OrderByDescending(x => x.px).Take(max))
            res.Add((x.url, x.px > 0 ? x.px + "p" : ""));

        return res;
    }

    static void PrepCurlEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("LD_PRELOAD");
        psi.Environment.Remove("LD_LIBRARY_PATH");

        string curl = "/data/data/com.termux/files/usr/bin/curl";
        psi.FileName = File.Exists(curl) ? curl : "curl";
    }

    public static async Task<string> CurlGet(
        string url, string referer, int maxTime = 25, bool http2 = true)
        => (await CurlRun(url, referer, maxTime, http2, false)).body;

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
            {
                psi.ArgumentList.Add("-w");
                psi.ArgumentList.Add("\n@@FINAL@@%{url_effective}");
            }

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

                return (stdout[..at],
                    stdout[(at + sep.Length)..].Trim());
            }
        }
        catch
        {
            return (null, null);
        }
    }

    public static async Task<string> CurlGetRetry(
        string url, string referer, string marker,
        int attempts = 3, int maxTime = 25, long deadline = 0)
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

            bool h2 = (i % 2 == 0);
            string body = await CurlGet(url, referer, maxTime, h2);
            if (!string.IsNullOrWhiteSpace(body)
                && (string.IsNullOrEmpty(marker) || body.Contains(marker)))
                return body;
        }

        return null;
    }

    // 8 sort THAT cua dropdown tren moi trang list (do truc tiep
    // HTML trang genre 2026-10-07). DANG URL: <base>/<sort>/ + ajax:
    //   genre `<slug>/<sort>/?ajax=1`  (big-tits/watched, rated... OK)
    //   home  `<sort>/?ajax=browse_videos` (watched OK; /recent/watched/ chet)
    // Rieng `popular` goc KHONG phai sort — no la base rieng
    // (/popular/today|week|month|year + browse_videos).
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất",      ""),
        ("Phổ biến",      "popular"),
        ("Ngày phát hành","releaseday"),
        ("Đánh giá cao",  "rated"),
        ("Bình luận",     "discussed"),
        ("Tải nhiều",     "downloaded"),
        ("Dài nhất",      "longest"),
        ("Xem nhiều",     "watched"),
    };

    static readonly string[] SortWhitelist =
        { "popular", "releaseday", "rated", "discussed", "downloaded", "longest", "watched" };

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    // Sort AP DUOC: home (c rong = /recent/), genre `<slug>/`, search
    // (?s=&o=). 5 list toan cuc (/popular/today.../releaseday) la BASE
    // rieng, khong phai sort — o nhom "Bang xep hang".
    public static (string name, string sort)[] SortsFor(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search)) return SearchSorts;
        if (string.IsNullOrWhiteSpace(c)) return Sorts;   // home
        string path = c.Trim().Trim('/').ToLowerInvariant();
        if (IsGlobalList(path)) return null;              // base rieng
        if (path == "recent") return Sorts;
        // <base>/<sort>/: tach base truoc (big-tits/watched -> big-tits)
        string bseg = BasePath(path);
        if (bseg.Equals("recent", StringComparison.OrdinalIgnoreCase)) return Sorts;
        if (!bseg.Contains("/")) return Sorts;            // genre <slug>
        return null;
    }

    // Search sort that (?s=&o=, do live 2026-10-07: recent/popular/rated
    // deu doi list so voi relevance).
    static readonly (string name, string sort)[] SearchSorts =
    {
        ("Liên quan",  ""),
        ("Mới nhất",   "recent"),
        ("Phổ biến",   "popular"),
        ("Đánh giá cao","rated"),
        ("Bình luận",  "discussed"),
        ("Tải nhiều",  "downloaded"),
    };

    // Sort hien tai: voi genre/home doc tu segment cuoi cua `c`;
    // voi search doc tu `o` (truyen qua `c` dang "o=<x>" hoac query).
    public static string CurrentSort(string search, string c)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return NormalizeSort(SearchO(c)) ?? "";
        if (string.IsNullOrWhiteSpace(c)) return "";
        string path = c.Trim().Trim('/').ToLowerInvariant();
        int slash = path.LastIndexOf('/');
        string last = slash >= 0 ? path.Substring(slash + 1) : path;
        if (last == "recent") return "";
        return NormalizeSort(last) ?? "";
    }

    static string SearchO(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return null;
        var m = Regex.Match("&" + c.TrimStart('?'), @"[?&]o=([^&]*)",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string SortLabel(string sort)
    {
        if (string.IsNullOrEmpty(sort)) return "Mới nhất";
        foreach (var (name, s) in Sorts)
            if (s == sort) return name;
        foreach (var (name, s) in SearchSorts)
            if (s == sort) return name;
        return sort;
    }

    // Ghep sort vao `c` hien tai (giu base). Genre/home: <base>/<sort>/;
    // mac dinh (sort rong): ve base goc. Search: truyen "o=<sort>".
    public static string WithSort(string search, string c, string sort)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return string.IsNullOrEmpty(sort) ? "" : "o=" + sort;
        string basePath = BasePath(c);
        return string.IsNullOrEmpty(sort) ? basePath : basePath + "/" + sort;
    }

    // Base path cua `c` (bo segment sort cuoi neu co).
    public static string BasePath(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return "recent";
        string path = c.Trim().Trim('/');
        // bo query thua (search o=)
        int q = path.IndexOf('?');
        if (q >= 0) path = path.Substring(0, q);
        q = path.IndexOf('&');
        if (q >= 0) path = path.Substring(0, q);
        path = path.Trim('/');
        if (path.Length == 0) return "recent";
        string low = path.ToLowerInvariant();
        if (low == "recent") return "recent";
        int slash = path.LastIndexOf('/');
        string last = (slash >= 0 ? path.Substring(slash + 1) : path).ToLowerInvariant();
        if (NormalizeSort(last) != null)
            path = slash >= 0 ? path.Substring(0, slash) : "recent";
        path = path.Trim('/');
        return path.Length == 0 ? "recent" : path;
    }

    // ===== head menu: dong 1 + 2, dung moi request (re) =====
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/javhdtoday";
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
        if (opts == null || opts.Length == 0) return res;
        string cur = CurrentSort(search, c);
        var sub = new List<Shared.Models.SISI.Base.MenuItem>(opts.Length);
        foreach (var (name, s) in opts)
        {
            string link;
            if (!string.IsNullOrWhiteSpace(search))
                link = root + "?search=" + HttpUtility.UrlEncode(search)
                    + (string.IsNullOrEmpty(s) ? "" : "&c=" + HttpUtility.UrlEncode("o=" + s));
            else
                link = root + "?c=" + HttpUtility.UrlEncode(WithSort(search, c, s));
            sub.Add(new Shared.Models.SISI.Base.MenuItem() { title = name, playlist_url = link });
        }
        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = "Sắp xếp: " + SortLabel(cur),
            playlist_url = "submenu",
            submenu = sub
        });
        return res;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host, List<(string name, string path)> genres,
        List<(string name, string query)> studios)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/javhdtoday?c=" + c.Trim('/');

        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            // Dong 3: 5 list TOAN CUC (day la "list", khong phai "sort").
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Bảng xếp hạng",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", url("recent/")),
                    new("Phổ biến hôm nay", url("popular/today/")),
                    new("Phổ biến tuần", url("popular/week/")),
                    new("Phổ biến tháng", url("popular/month/")),
                    new("Ngày phát hành", url("releaseday/")),
                }
            }
        };

        // Site khong co index studio (/channels/ la trang
        // stub) — hang phim nam trong dropdown nav, link dang
        // search (?s=<ten hang>).
        if (genres != null && genres.Count > 0)
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem()
                    {
                        title = x.name,
                        playlist_url = host + "/javhdtoday?c=" + x.path
                    }).ToList()
            });

        if (studios != null && studios.Count > 0)
            menu.Add(new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Hãng phim",
                playlist_url = "submenu",
                submenu = studios.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem()
                    {
                        title = x.name,
                        playlist_url = host + "/javhdtoday?search="
                            + HttpUtility.UrlEncode(x.query)
                    }).ToList()
            });

        return menu;
    }

    // The loai tu /categories/: card <li id="category-N"> co ten
    // + so phim. Loc slug tube lien ket (partner site), giu
    // the loai that.
    public static List<(int count, string name, string path)> CatList(
        string html)
    {
        var res = new List<(int, string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        foreach (Match m in Regex.Matches(html,
            "<li id=\"category-\\d+\">(.*?)</li>",
            RegexOptions.Singleline))
        {
            string b = m.Groups[1].Value;
            var hm = Regex.Match(b, "<a href=\"/([a-z0-9\\-]+)/\"");
            var nm = Regex.Match(b, "category-title\">([^<]+)<");
            if (!hm.Success || !nm.Success)
                continue;

            string slug = hm.Groups[1].Value;
            if (TubeSlug(slug))
                continue;

            int cnt = 0;
            var cm = Regex.Match(b, "([\\d,]+)\\s*</div>\\s*</a>");
            if (cm.Success)
                int.TryParse(cm.Groups[1].Value.Replace(",", ""),
                    out cnt);

            res.Add((cnt, HttpUtility.HtmlDecode(
                nm.Groups[1].Value.Trim()), slug + "/"));
        }

        return res;
    }

    // Top the loai theo so phim (nhu Tags cua JavGuru).
    public static List<(string name, string path)> CatTop(
        List<(int count, string name, string path)> cats,
        int max = 50)
        => cats.OrderByDescending(x => x.count).Take(max)
            .Select(x => (x.name, x.path)).ToList();

    // Hang phim tu dropdown "Studios" tren nav (moi trang deu
    // co): link /search/video/?s=<ten hang>. Query tra theo
    // TEN HIEN (site moc nham vai query, vd Glory Quest).
    public static List<(string name, string query)> StudioList(
        string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        int at = html.IndexOf("> Studios<",
            StringComparison.Ordinal);
        if (at < 0)
            return res;

        string blk = html.Substring(at,
            Math.Min(6000, html.Length - at));
        var seen = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(blk,
            "<a href=\"https?://javhd\\.today/search/video/"
            + "\\?s=([^\"]+)\">(?:<i[^>]*></i>\\s*)?([^<]+)</a>"))
        {
            string name = HttpUtility.HtmlDecode(
                m.Groups[2].Value.Trim());
            if (name.Length == 0 || !seen.Add(name))
                continue;

            if (name.IndexOf("All Studios",
                StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            res.Add((name, name));
        }

        return res;
    }

    static bool TubeSlug(string slug)
    {
        switch (slug)
        {
            case "categories":
            case "7mmtv":
            case "avgle":
            case "bestjav":
            case "jable":
            case "javbangers":
            case "javct":
            case "javfinder":
            case "javgg":
            case "javhub":
            case "javlibrary":
            case "javmost":
            case "javtiful":
            case "javtrailers":
            case "javtube":
            case "jav-guru":
            case "jav-porn":
            case "missav":
            case "njav":
            case "pornhub":
            case "sextb":
            case "xnxx":
            case "xvideos":
            case "xhamster":
                return true;
            default:
                return false;
        }
    }
}
