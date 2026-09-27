using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Shared.Models.Base;

namespace JavGuru;

public sealed class JavGuruServer
{
    public string Label { get; set; } = "";
    public string PageUrl { get; set; } = "";
}

public static class JavGuruTo
{
    public static string SiteHost = "https://jav.guru";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // Segment HLS cua server 1 nam o turbosplayer/turboviplay/lh3.googleusercontent.
    // GUI them Referer hoac Origin cua jav.guru thi CDN doi 429 (chan hotlink) —
    // da do: UA bat ky deu 206, chi can co Referer jav.guru la 429. Stream vi
    // vay phai bo qua hoan toan Referer/Origin.
    //
    // NHUNG server JK (maxstream.org) lai can Referer cua chinh no. Chung mot
    // bong header cho moi link thi hoac 429 hoac 403 — phai chon theo server.
    public static IReadOnlyList<HeadersModel> StreamHeaders(string label = null)
    {
        if (!string.IsNullOrEmpty(label) && label.IndexOf("JK", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return HeadersModel.Init(
                ("Accept", "*/*"),
                ("User-Agent", ChromeUA),
                ("Referer", "https://maxstream.org/")
            );
        }

        return HeadersModel.Init(
            ("Accept", "*/*"),
            ("User-Agent", ChromeUA)
        );
    }

    // Referer di kem khi fetch playlist cua server do (rỗng = không gửi).
    public static string ServerReferer(string label)
    {
        if (!string.IsNullOrEmpty(label) && label.IndexOf("JK", StringComparison.OrdinalIgnoreCase) >= 0)
            return "https://maxstream.org/";

        return null;
    }

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            return host + "/?s=" + HttpUtility.UrlEncode(search.Trim()) + (pg > 1 ? "&paged=" + pg : "");
        }

        string path;
        if (!string.IsNullOrWhiteSpace(c))
        {
            if (c.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return NormalizePageUrl(c, pg);

            path = "/" + c.Trim().Trim('/') + "/";
        }
        else
        {
            path = "/";
        }

        // WP: trang 1 khong gan /page/1/, phan trang nam o /page/N/
        if (pg > 1)
            path = path.TrimEnd('/') + "/page/" + pg + "/";

        return host + path;
    }

    public static string NormalizePageUrl(string url, int pg = 1)
    {
        if (string.IsNullOrWhiteSpace(url))
            return SiteHost + "/";

        url = HttpUtility.HtmlDecode(url.Trim());
        if (url.StartsWith("//"))
            url = "https:" + url;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = SiteHost + (url.StartsWith("/") ? url : "/" + url);

        url = url.TrimEnd('/');

        if (pg > 1)
        {
            if (Regex.IsMatch(url, @"/page/\d+$", RegexOptions.IgnoreCase))
                url = Regex.Replace(url, @"/page/\d+$", "");
            url += "/page/" + pg;
        }

        return url + "/";
    }

    public static bool IsSiteUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;
        return Uri_(url, out var host) && host.EndsWith("jav.guru", StringComparison.OrdinalIgnoreCase);
    }

    static bool Uri_(string url, out string host)
    {
        if (System.Uri.TryCreate(url, System.UriKind.Absolute, out var u))
        {
            host = u.Host;
            return true;
        }
        host = null;
        return false;
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in html.Split(new[] { "<div class=\"inside-article\">" }, StringSplitOptions.None))
        {
            if (raw.Length < 200)
                continue;

            string block = raw.Length > 6000 ? raw[..6000] : raw;

            var hm = Regex.Match(block, "<a href=\"(https://jav\\.guru/\\d{4,}/[a-z0-9\\-]+/)\"", RegexOptions.IgnoreCase);
            if (!hm.Success)
                continue;

            string href = NormalizePageUrl(hm.Groups[1].Value);
            if (!IsSiteUrl(href) || !seen.Add(href))
                continue;

            string name = "";
            var tm = Regex.Match(block, "<a href=\"https://jav\\.guru/[^\\\"]+\" title=\"([^\\\"]{4,400})\"", RegexOptions.IgnoreCase);
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
            {
                var im = Regex.Match(block, "<img[^>]+alt=\"([^\\\"]{4,400})\"", RegexOptions.IgnoreCase);
                if (im.Success)
                    name = HttpUtility.HtmlDecode(im.Groups[1].Value.Trim());
            }
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var pm = Regex.Match(block, "<img[^>]+src=\"(https?://[^\\\"]+)\"", RegexOptions.IgnoreCase);
            if (pm.Success)
                poster = pm.Groups[1].Value;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javguru",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // Nut player: <a class="wp-btn-iframe__shortcode" data-localize="qkaxnccmfn">STREAM TV</a>
    // Payload luon di kem: var qkaxnccmfn = { ..., "iframe_url":"<base64>" };
    public static List<JavGuruServer> Servers(string html)
    {
        var list = new List<JavGuruServer>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html, "data-localize=\"([a-z0-9]+)\"[^>]*>\\s*([^<]{2,20})</a>", RegexOptions.IgnoreCase))
        {
            string token = m.Groups[1].Value;
            string label = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(label))
                continue;

            string page = VarIframeUrl(html, token);
            if (string.IsNullOrEmpty(page) || !page.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seen.Add(page))
                continue;

            list.Add(new JavGuruServer() { Label = label, PageUrl = page });
        }

        return list;
    }

    static string VarIframeUrl(string html, string token)
    {
        int at = html.IndexOf("var " + token + " =", StringComparison.Ordinal);
        if (at < 0)
            return null;

        int end = html.IndexOf("};", at, StringComparison.Ordinal);
        if (end < 0)
            return null;

        string block = html[at..(end + 2)];
        var m = Regex.Match(block, "\"iframe_url\"\\s*:\\s*\"([A-Za-z0-9+/=]+)\"");
        if (!m.Success)
            return null;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)).Trim();
        }
        catch
        {
            return null;
        }
    }

    // iframe_url co dang https://jav.guru/searcho/?ud=<token>&bg=...
    // Trang do ghep iframe con bang JS: base + '?' + rtype + 'r=' + reverse(p1+p2+p3).
    //
    // Ghi chu: token trong `ud` CHINH la chuoi p1+p2+p3 (khong dao), nen `ur` =
    // dao nguoc `ud`. Ten tham so la <rtype><d|r>: ud -> rtype 'u' -> ur; server
    // khac dung td/ed/hd/od -> rtype tuong ung 't'/'e'/'h'/'o'.
    //
    // Uu diem: tinh thang `ur` tu iframe_url, KHONG can tai trang /searcho/ o giua.
    // Trang do chang 0.5-8s (va hay 520) trong khi `ur` lay luoc 0ms.
    public static string GatewayUrl(string iframeUrl)
    {
        if (string.IsNullOrEmpty(iframeUrl))
            return null;

        var m = Regex.Match(iframeUrl, @"[?&]([a-z])d=([0-9a-z]+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string rtype = m.Groups[1].Value;
        char[] rev = m.Groups[2].Value.ToCharArray();
        Array.Reverse(rev);

        int q = iframeUrl.IndexOf('?');
        string path = q > 0 ? iframeUrl[..q] : iframeUrl;

        return path + "?" + rtype + "r=" + new string(rev);
    }

    // Master cua server 1 (turbo) la playlist 2 LEVEL: 3 variant 480/720/1080
    // (284 byte), moi variant tro toi mot playlist media VOD tren
    // gs01/gs12.turbosplayer.com (91KB, 1011 segment), segment o lh3.googleusercontent.com.
    //
    // app Lampa nap MASTER roi hls.js tu chay ABR: moi lan doi level la phai tai
    // them mot playlist con o host khac — chinh tai do hls.js bao
    //   "Found no media in msn N of main playlist ...m3u8"
    // (doc playlist ra duoc, co EXTINF, nhung app nhin khong thay).
    // Nen tach master ngay o server roi tra playlist media cua tung variant:
    // hls.js nap 1 lan xong, khong co level nao de doi.
    //
    // KHONG tai thu playlist con de kiem tra #EXTINF. Master da la nguon su
    // that, viec tai them 3 playlist ~90KB chi de kiem tra lai thu rut mat
    // ~2s va hay fail khi host gs* cham/chap chon (gap code=000) — roi ruc master
    // tho lai, app lai gap loi cu. Khong co gi de kiem tra thi lay luon variant.
    public static async Task<List<(string url, string tag)>> MasterVariants(string master, int max = 3, int maxTime = 7, long deadline = 0, string referer = null)
    {
        var res = new List<(string url, string tag)>();
        if (string.IsNullOrEmpty(master))
            return res;

        // Master chi 284 byte: cung timeout ngan + nhieu lan thu nhu buoc tren.
        // Them fallback http1.1 cho javclan /stream (treo voi http2).
        string html = await CurlGetRetryBoth(master, referer, "#EXT-X-STREAM-INF", 2, 6, deadline);
        if (string.IsNullOrEmpty(html))
            return res;

        var found = new List<(int px, string url)>();

        // Base de noi variant relative (StreamHG/SB viet index-f1-... khong
        // domain): thu muc chua master.
        string dir = null;
        try
        {
            int at = master.LastIndexOf('/');
            if (at > 8)
                dir = master[..(at + 1)];
        }
        catch { }

        foreach (Match m in Regex.Matches(html, "#EXT-X-STREAM-INF:([^\\r\\n]*)\\r?\\n\\s*(\\S+)", RegexOptions.IgnoreCase))
        {
            // Nhan theo CHIEU CAO (1080p) chu khong phai chieu rong (1920p):
            // RESOLUTION=1920x1080 -> nhan "1080p" de hien thi quen thuoc.
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

    // Server 1 (turbo): /searcho/?ur=<token> -> 302 -> emturbovid.com/t/<id>
    // -> trang player co data-hash="<master.m3u8>".
    //
    // KHONG du doan path CDN. Id tren URL /t/ KHAC id trong path m3u8:
    //   /t/6ab6dfae3d2c8  ->  cdn.turboviplay.com/data3/6ab6cef31bf74/6ab6cef31bf74.m3u8
    // nen pattern data{N}/{id}/... doan tu id /t/ luon 404 (da probe data1..6),
    // va do do toi ket luan nham "video bi xoa" trong khi video van xem duoc.
    // Chi tin data-hash cua trang player.
    // maxstream.org (STREAM JK) nhung link phat trong JS dang P.A.C.K.E.R:
    //   eval(function(p,a,c,k,e,d){while(c--)if(k[c])p=p.replace(
    //       new RegExp('\\b'+c.toString(a)+'\\b','g'),k[c]);return p}('...',36,119,'a|b|c'.split('|')))
    // Token viet o HE A (a=36): '3a'=118, '2z'=107. Phai giai nen roi moi
    // regex ra duoc URL — regex tren HTML thoi se khong thay gi.
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
        if (!int.TryParse(m.Groups[2].Value, out int a) || !int.TryParse(m.Groups[3].Value, out int c))
            return null;

        var k = m.Groups[4].Value.Split('|');

        // lap tu index lon xuoi 0: ket qua vong sau la du lieu cho vong sau
        for (int i = c - 1; i >= 0; i--)
        {
            if (i >= k.Length || string.IsNullOrEmpty(k[i]))
                continue;

            // dung MatchEvaluator de khien '$' trong k[i] khong bi hieu la bien the
            p = Regex.Replace(p, @"\b" + ToBase(i, a) + @"\b", (Match _) => k[i]);
        }

        return p;
    }

    // Do lai so nguyen o he `b` bang chu cai (a=36 -> 13 ra "d", 118 ra "3a").
    //
    // KHONG duoc shortcut "value < b -> value.ToString()": chi dung khi b <= 10.
    // Voi b=36, 13 phai ra "d" — neu tra ve "13" thi token 1 chu cai khong bao
    // gio khop \b...\b, giai nen ra "d://s1.c.b" thay vi "https://s1.maxstream.org"
    // va regex tim .m3u8 khong match.
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

    // Link phat cua playerjs: new Playerjs({ ..., file:"<...m3u8?...>" })
    public static string PlayerJsFile(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        string src = Unpack(html) ?? html;

        var m = Regex.Match(src, "file\\s*:\\s*[\"'](https?://[^\"']+?\\.m3u8[^\"']*)[\"']", RegexOptions.IgnoreCase);
        if (m.Success)
            return HttpUtility.HtmlDecode(m.Groups[1].Value);

        foreach (Match x in Regex.Matches(src, "https?://[^\"'\\s<>]+\\.m3u8[^\"'\\s<>]*", RegexOptions.IgnoreCase))
            return HttpUtility.HtmlDecode(x.Value);

        return null;
    }

    // StreamHG (STREAM SB, host javclan.com): player khong in file truc tiep
    // ma dat link trong `var links={"hls4":"...","hls3":"...","hls2":"..."}`
    // (ca packer + JSON deu base64/giai nen giong nhau). Thu tu uu tien giong
    // player: hls4 > hls3 > hls2. hls4 la path relative (/stream/...) tro ve
    // chinh host player — phai noi base. Upstream chon loc: phim thi hls2/3
    // 502 chi hls4 song, phim thi nguoc lai, nen Streams() probe tung link.
    public static List<string> SbLinks(string src, string playerHost)
    {
        var res = new List<string>();
        if (string.IsNullOrEmpty(src))
            return res;

        var m = Regex.Match(src, "var\\s+links\\s*=\\s*\\{([^}]{0,2000})\\}", RegexOptions.IgnoreCase);
        if (!m.Success)
            return res;

        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match x in Regex.Matches(m.Groups[1].Value, "\"(hls\\d)\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
            kv[x.Groups[1].Value] = HttpUtility.HtmlDecode(x.Groups[2].Value.Trim());

        foreach (string k in new[] { "hls4", "hls3", "hls2" })
        {
            if (!kv.TryGetValue(k, out string v) || string.IsNullOrEmpty(v))
                continue;
            if (v.StartsWith("/") && !string.IsNullOrEmpty(playerHost))
                v = playerHost.TrimEnd('/') + v;
            if (v.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                res.Add(v);
        }

        return res;
    }

    public static async Task<List<(string url, string tag)>> Streams(string gateway, string referer, long deadline = 0, string serverReferer = null)
    {
        var res = new List<(string url, string tag)>();
        if (string.IsNullOrEmpty(gateway))
            return res;

        // Video da bi upstream xoa tra trang "Video Unavailable" (1.8KB, khong
        // data-hash/m3u8/urlPlay): dung ngay, khong retry vo ich.
        //
        // Do buoc nay hay treo: do chay 6 lan thi 4 treo, mot treo an het
        // --max-time (8s/lan => 17s/2 lan roi hien budget het). Lan thanh
        // cong chi mat 1.1-1.7s, nen dung timeout NGAN va THU NHIEU hon la
        // dung timeout dai: 4 x 4s + 1.8s nghi = 17.8s, xac suat that bai
        // ~6% thay vi 50%.
        // SB (javclan) chon loc theo phim + IP: luc hls2/3 502, luc gateway
        // /searcho treo 17s. Cho SB budget rong hon (6x6s) thay vi 4x4s.
        bool isSbGw = IsSbGateway(gateway);
        string player = await CurlGetRetry(gateway, referer, null, isSbGw ? 6 : 4, isSbGw ? 6 : 4, deadline);
        if (IsDeadPlayer(player))
            return res;

        // ROLLBACK ve dung nhu luc 1080p chay on (truoc khi lam JK):
        // chi player P.A.C.K.E.R (maxstream/JK) moi di duong giai-nen.
        // Turbo (TV) va server khac giu nguyen duong data-hash/m3u8 nhu cu —
        // de PlayerJsFile chay truoc se vot nham URL m3u8 dau tien trong
        // trang player thay vi data-hash (master).
        string packed = Unpack(player);
        bool isSb = isSbGw;
        // SB (StreamHG): uu tien var links (hls4>hls3>hls2) truoc, vi
        // PlayerJsFile vot bua URL .m3u8 absolute dau tien (hls2) trong khi
        // upstream hay 502 hls2/3 chi con hls4 song. Server khac giu nguyen
        // duong cu de khong vot nham master.
        string jsFile = null;
        List<string> sbLinks = null;
        string sbHost = null;
        if (isSb && packed != null)
        {
            sbHost = await SbResolveHost(gateway, referer, deadline);
            sbLinks = SbLinks(packed, sbHost);
        }
        if (sbLinks == null || sbLinks.Count == 0)
            jsFile = packed != null ? PlayerJsFile(packed) : null;
        Console.WriteLine($"JavGuru: player len={player?.Length ?? 0} packed={packed != null} jsFile={(string.IsNullOrEmpty(jsFile) ? "null" : "ok")} sbLinks={sbLinks?.Count ?? 0}");

        if (!string.IsNullOrEmpty(jsFile))
        {
            res.Add((jsFile, ""));
        }
        else if (sbLinks != null && sbLinks.Count > 0)
        {
            foreach (string u in sbLinks)
                res.Add((u, ""));
        }
        else
        {
            if (res.Count == 0)
            {
                foreach (string u in StreamUrls(player))
                    res.Add((u, ""));
            }
        }

        // SB co nhieu link (hls4/3/2) ma upstream chon loc (phim thi hls2/3
        // 502 chi hls4 song): probe tung link, lay link dau tien co variant.
        if (res.Count > 1 && isSb)
        {
            foreach (var cand in res)
            {
                var variants = await MasterVariants(cand.url, deadline: deadline, referer: serverReferer);
                if (variants.Count > 0)
                    return variants;
            }
            return new List<(string url, string tag)>();
        }

        // Master 2 level -> tach o server, tra playlist media tung chat luong
        // de app khong phai doi level (xem MasterVariants).
        if (res.Count == 1)
        {
            var variants = await MasterVariants(res[0].url, deadline: deadline, referer: serverReferer);
            if (variants.Count > 0)
                return variants;
        }

        return res;
    }

    // SB di qua gateway /searcho (rtype x: ?xd= trong iframe_url, ?xr= sau khi
    // dao). Nhan dien de bat probe multi-link; TV/JK/LU giu nguyen duong cu.
    static bool IsSbGateway(string gateway)
        => !string.IsNullOrEmpty(gateway)
           && Regex.IsMatch(gateway, @"[?&]x[dr]=[0-9a-z]+", RegexOptions.IgnoreCase);

    // Host that cua trang player SB (javclan.com/...): gateway /searcho chi
    // 302 toi player, nen xin redirect URL (khong theo) roi lay host. 1
    // request nhe ~0.4s, co deadline.
    static async Task<string> SbResolveHost(string gateway, string referer, long deadline = 0)
    {
        try
        {
            if (deadline > 0 && deadline - Environment.TickCount64 < 4000)
                return null;

            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            PrepCurlEnv(psi);
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("/dev/null");
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add("%{redirect_url}");
            psi.ArgumentList.Add("--connect-timeout");
            psi.ArgumentList.Add("5");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add("8");
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add(ChromeUA);
            if (!string.IsNullOrEmpty(referer))
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(referer);
            }
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(gateway);

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return null;
                string redir = (await p.StandardOutput.ReadToEndAsync()).Trim();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0 || string.IsNullOrEmpty(redir))
                    return null;

                var u = new Uri(redir.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? redir : "https:" + redir);
                return u.GetLeftPart(UriPartial.Authority);
            }
        }
        catch
        {
            return null;
        }
    }

    // Trang player bao video khong ton tai (1.8KB "Video Unavailable / This
    // video has been removes or do not exist"): khong co stream nao de cho.
    static bool IsDeadPlayer(string html)
        => !string.IsNullOrEmpty(html)
           && (html.Contains("Video Unavailable", StringComparison.OrdinalIgnoreCase)
               || html.Contains("do not exist", StringComparison.OrdinalIgnoreCase));

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
            if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase) && seen.Add(u))
                urls.Add(u);
        }

        foreach (Match m in Regex.Matches(html, "data-hash=\"(https?://[^\\\"]+)\"", RegexOptions.IgnoreCase))
            Add(m.Groups[1].Value);

        if (urls.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, "(https?://[^\\\"'\\s<>\\\\]+\\.m3u8[^\\\"'\\s<>\\\\]*)", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
        }

        // Mot so video khong co HLS ma la MP4: player in
        // var urlPlay = 'https://e0X.etvp.cc/uploads/<id>.mp4';
        if (urls.Count == 0)
        {
            var mp4 = Regex.Match(html, "urlPlay\\s*=\\s*'([^']+\\.(?:mp4|m4v))'", RegexOptions.IgnoreCase);
            if (mp4.Success)
                Add(mp4.Groups[1].Value);
        }

        // master (nhieu bien the) dat dau, variant VOD sau
        urls.Sort((x, y) =>
        {
            int sx = x.Contains("master") ? 0 : 1;
            int sy = y.Contains("master") ? 0 : 1;
            if (sx != sy)
                return sx.CompareTo(sy);
            return string.Compare(y, x, StringComparison.Ordinal);
        });

        return urls;
    }

    static void PrepCurlEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("LD_PRELOAD");
        psi.Environment.Remove("LD_LIBRARY_PATH");

        string curl = "/data/data/com.termux/files/usr/bin/curl";
        psi.FileName = System.IO.File.Exists(curl) ? curl : "curl";
    }

    public static async Task<string> CurlGet(string url, string referer, int maxTime = 25, bool http2 = true)
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
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return null;
                string stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return p.ExitCode == 0 ? stdout : null;
            }
        }
        catch
        {
            return null;
        }
    }

    // deadline (ms epoch cua Environment.TickCount64): client Lampa bo sau 30s
    // nen moi lan fetch bi cat ngan theo thoi gian con lai, tranh tra loi 503
    // sau 24s khi server ngoai treo.
    public static async Task<string> CurlGetRetry(string url, string referer, string marker, int attempts = 3, int maxTime = 25, long deadline = 0, bool http2 = true)
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

            string body = await CurlGet(url, referer, maxTime, http2);
            if (!string.IsNullOrWhiteSpace(body) && (string.IsNullOrEmpty(marker) || body.Contains(marker)))
                return body;
        }
        return null;
    }

    // javclan.com/stream/... (hls4 cua SB) treo voi --http2 (000) nhung
    // http1.1 thi 200 ngay. Thu http2 truoc, rot thi http1.1.
    public static async Task<string> CurlGetRetryBoth(string url, string referer, string marker, int attempts = 2, int maxTime = 10, long deadline = 0)
    {
        string body = await CurlGetRetry(url, referer, marker, attempts, maxTime, deadline, true);
        if (!string.IsNullOrEmpty(body))
            return body;
        return await CurlGetRetry(url, referer, marker, attempts, maxTime, deadline, false);
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        var genres = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("JAV", host + "/javguru?c=category/jav"),
            new("Không che", host + "/javguru?c=category/decensored"),
            new("Có phụ đề", host + "/javguru?c=category/english-subbed"),
            new("Amateur", host + "/javguru?c=category/amateur"),
            new("Idol", host + "/javguru?c=category/idol"),
            new("FC2", host + "/javguru?c=category/FC2"),
            new("4K", host + "/javguru?c=category/4k"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/javguru"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javguru"
            },
            // Khong dua "Xem nhieu" (most-watched-rank) vao menu: trang do dung
            // markup <article class="rank-item"> va /page/N/ tra lai dung mot
            // bang xep hang -> phan trang se lap phim.
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres
            }
        };
    }
}
