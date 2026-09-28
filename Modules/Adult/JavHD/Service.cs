using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace JavHD;

public sealed class JavHDServer
{
    public string Label { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string Kind { get; set; } = "";
}

public static class JavHDTo
{
    public static string SiteHost = "https://javhd.today";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    public static string Uri(string host, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        string path = "recent/";
        if (!string.IsNullOrEmpty(c))
        {
            c = c.Trim('/');
            path = c.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? c : c;
            if (!path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                path = path.Trim('/') + "/";
        }

        string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path.TrimEnd('/') + "/" : host + "/" + path;
        return url + "?ajax=browse_videos&page=" + Math.Max(1, pg);
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string json)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(json))
            return playlists;

        string html = json;
        try
        {
            var m = Regex.Match(json, "\"html\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (m.Success)
                html = Regex.Unescape(m.Groups[1].Value);
        }
        catch { }

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
                    site = "javhd",
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
            || Has(label, "Cloudwish") || Has(label, "Mycloudz");
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
    public static List<JavHDServer> Servers(string html)
    {
        var list = new List<JavHDServer>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string u)
        {
            if (string.IsNullOrEmpty(u) || !seen.Add(u))
                return;

            string label = Label(u);
            if (string.IsNullOrEmpty(label) || !IsSupported(label))
                return;

            list.Add(new JavHDServer()
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
                Add(u);
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
    // Cloud=HLS biet ngay tu host.
    public static async Task<string> ServerKindAsync(
        string pageUrl, int maxTime = 4, long deadline = 0)
    {
        if (IsDood(pageUrl))
            return KindMp4;

        if (IsCloud(pageUrl))
            return KindHls;

        string player = await CurlGetRetry(
            pageUrl, SiteHost + "/", null, 3, maxTime, deadline);
        if (string.IsNullOrEmpty(player))
            return "";

        foreach (string media in StreamUrls(player))
            return IsDirectMp4(media) ? KindMp4 : KindHls;

        return "";
    }

    // Biet ngay tu host, khong can fetch. DoodStream=mp4, 3 con lai=HLS.
    // Turbo thuc ra DA DANG nen chi dung tam o Servers(); /vidosik do
    // lai bang ServerKindAsync o tren.
    public static string ServerKind(string label)
    {
        if (string.IsNullOrEmpty(label))
            return "";

        if (Has(label, "Dood"))
            return KindMp4;

        if (Has(label, "Turbo") || Has(label, "Cloudwish")
            || Has(label, "Mycloudz"))
            return KindHls;

        return "";
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

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        var cats = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Mới nhất", host + "/javhd?c=recent/"),
            new("Phổ biến hôm nay", host + "/javhd?c=popular/today/"),
            new("Phổ biến tuần", host + "/javhd?c=popular/week/"),
            new("Phổ biến tháng", host + "/javhd?c=popular/month/"),
            new("Ngày phát hành", host + "/javhd?c=releaseday/"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javhd"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = cats
            }
        };
    }
}
