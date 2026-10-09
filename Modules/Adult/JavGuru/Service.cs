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
    public static IReadOnlyList<HeadersModel> StreamHeaders(string label = null, string referer = null)
    {
        if (!string.IsNullOrEmpty(label) && label.IndexOf("JK", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return HeadersModel.Init(
                ("Accept", "*/*"),
                ("User-Agent", ChromeUA),
                ("Referer", "https://maxstream.org/")
            );
        }

        // DD (DoodStream): CDN chi phuc vu khi Referer khop host embed. Referer
        // nay tra ve cung video (host API doi theo lan tai) nen phai truyen
        // vao tu `DoodSourceAsync`, khong gan tinh trong StreamHeaders.
        if (!string.IsNullOrEmpty(referer))
        {
            return HeadersModel.Init(
                ("Accept", "*/*"),
                ("User-Agent", ChromeUA),
                ("Referer", referer)
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

    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        // Sort cua site (WP) la QUERY `?orderby=views|likes|title`, dat SAU
        // phan trang /page/N/.
        // DO 2026-10-06 (fetch truc tiep, khong qua proxy):
        //   /  ,  /category/*  ,  /?s=X  deu doi list khi ?orderby=...
        //   -> sort PHAI ghep cho ca search (ban cu return truoc doan sort ->
        //      search bi bo qua) va home.
        //   views / likes / title khac default 0/13/0 -> 3 sort that.
        //   KHONG ghep sort != whitelist: `post__in` tra list RONG (module 503).
        string so = string.IsNullOrWhiteSpace(sort) ? "" : "orderby=" + sort.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "?s=" + HttpUtility.UrlEncode(search.Trim());
            if (pg > 1) q += "&paged=" + pg;
            if (so.Length > 0) q += "&" + so;
            return host + "/" + q;
        }

        string sq = so.Length > 0 ? "?" + so : "";

        string path;
        if (!string.IsNullOrWhiteSpace(c))
        {
            if (c.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return NormalizePageUrl(c, pg) + sq;

            path = "/" + c.Trim().Trim('/') + "/";
        }
        else
        {
            path = "/";
        }

        // WP: trang 1 khong gan /page/1/, phan trang nam o /page/N/
        if (pg > 1)
            path = path.TrimEnd('/') + "/page/" + pg + "/";

        return host + path + sq;
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

    // `referer` trong ket qua CHI dung cho DoodStream: app khong gui duoc
    // Referer nen Controller phai gan no vao stream proxy (xem DoodSourceAsync).
    public static async Task<List<(string url, string tag, string referer)>> Streams(string gateway, string referer, long deadline = 0, string serverReferer = null)
    {
        var res = new List<(string url, string tag, string referer)>();
        if (string.IsNullOrEmpty(gateway))
            return res;

        // DD (DoodStream): gateway 302 thang sang embed `vide0.net/e/<id>`,
        // khong co trang player de boc data-hash/urlPlay nen phai nhan dien
        // rieng. `-w %{url_effective}` lay URL cuoi sau khi da follow hop 302.
        if (IsDdGateway(gateway))
        {
            var hop = await CurlGetUrl(gateway, referer, 6);
            if (string.IsNullOrEmpty(hop.finalUrl))
                return res;

            var (doodUrl, doodRef) = await DoodSourceAsync(hop.finalUrl, referer, 6, deadline);
            if (string.IsNullOrEmpty(doodUrl))
                return res;

            // tag "mp4" de /vidosik gan duoi `.mp4` — app chi ep hls.js khi
            // URL co `.m3u8`, mp4 di qua `.m3u8` se bao "no EXTM3U delimiter".
            return new List<(string, string, string)> { (doodUrl, "mp4", doodRef) };
        }

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
        //
        // VO (voe.sx) KHONG di duong curl o day: trang embed obfuscate nen
        // curl khong ra link. Di that bang Chrome (VoSourceAsync), mac dinh
        // o CUOI chuoi fallback nen hiem khi toi luot (VO la phao cuu sinh
        // khi 5 server kia chet het).
        if (IsVoGateway(gateway))
        {
            var vo = await VoSourceAsync(gateway, referer, deadline);
            if (vo.Count == 0)
                return res;

            return vo.Select(x => (x.url, x.tag, (string)null)).ToList();
        }

        bool isSbGw = IsSbGateway(gateway);
        // LU: token CDN *.tnmr.org ky theo TLS fingerprint cua ben mint — phai
        // mint bang Chrome (luresolve.py, curl_cffi chrome124) thi app (Chromium)
        // moi tai duoc; curl thuong se tao token "ho curl" app an 403.
        // Rot ve duong cu (curl) neu thieu python/cffi hoac script that bai.
        if (IsLuGateway(gateway))
        {
            var lu = await LuResolveAsync(gateway, referer, 8);
            if (!string.IsNullOrEmpty(lu.url))
                return new List<(string, string, string)> { (lu.url, lu.tag, null) };
        }
        string player = null;
        player = await CurlGetRetry(gateway, referer, null, isSbGw ? 6 : 4, isSbGw ? 6 : 4, deadline);
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
            res.Add((jsFile, "", null));
        }
        else if (sbLinks != null && sbLinks.Count > 0)
        {
            foreach (string u in sbLinks)
                res.Add((u, "", null));
        }
        else
        {
            if (res.Count == 0)
            {
                foreach (string u in StreamUrls(player))
                    res.Add((u, "", null));
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
                    return variants.Select(x => (x.url, x.tag, (string)null)).ToList();
            }
            return new List<(string url, string tag, string referer)>();
        }

        // Master 2 level -> tach o server, tra playlist media tung chat luong
        // de app khong phai doi level (xem MasterVariants).
        if (res.Count == 1)
        {
            var variants = await MasterVariants(res[0].url, deadline: deadline, referer: serverReferer);
            if (variants.Count > 0)
                return variants.Select(x => (x.url, x.tag, (string)null)).ToList();
        }

        return res;
    }

    // SB di qua gateway /searcho (rtype x: ?xd= trong iframe_url, ?xr= sau khi
    // dao). Nhan dien de bat probe multi-link; TV/JK/LU giu nguyen duong cu.
    static bool IsSbGateway(string gateway)
        => !string.IsNullOrEmpty(gateway)
           && Regex.IsMatch(gateway, @"[?&]x[dr]=[0-9a-z]+", RegexOptions.IgnoreCase);

    // DD (rtype h: ?hd= trong iframe_url, ?hr= sau khi dao) — gateway 302
    // THANG sang embed DoodStream `https://vide0.net/e/<id>` nen KHONG co trang
    // player de boc data-hash/urlPlay. Phai biet URL cuoi cua gateway roi chay
    // cong thuc DoodStream (xem DoodSourceAsync).
    public static bool IsDdGateway(string gateway)
        => !string.IsNullOrEmpty(gateway)
           && Regex.IsMatch(gateway, @"[?&]h[dr]=[0-9a-z]+", RegexOptions.IgnoreCase);

    // LU (rtype e: ?ed= trong iframe_url, ?er= sau khi dao) — embed LuluStream
    // (streamhihi). Token CDN *.tnmr.org ky theo TLS fingerprint cua ben mint
    // (xem LuResolveAsync/luresolve.py) nen resolve rieng bang Chrome.
    public static bool IsLuGateway(string gateway)
        => !string.IsNullOrEmpty(gateway)
           && Regex.IsMatch(gateway, @"[?&]e[dr]=[0-9a-z]+", RegexOptions.IgnoreCase);

    public static bool IsDdServer(string label)
        => !string.IsNullOrEmpty(label)
           && label.IndexOf("DD", StringComparison.OrdinalIgnoreCase) >= 0;

    // ================= DANG PHAT (mp4 / hls) =================
    //
    // App Lampa CHI ep hls.js khi URL khop regex `\.m3u8?(?:$|[?#])`
    // (SISI/plugins/sisi.js -> applyHlsType). Do lai route phai mang duoi
    // `.m3u8`, mp4 di qua do se bao "no EXTM3U delimiter".
    public const string KindMp4 = "mp4";
    public const string KindHls = "hls";

    // Rtype cua gateway /searcho (sau khi dao token): ur=TV, or=JK, er=LU,
    // xr=SB, hr=DD, tr=VO. Rong = chua biet (TV da dang).
    //
    // VO gan tam KindHls vi da so phim VOE tra HLS; sai dang thi `/video`
    // Redirect thang ve upstream va app bao loi — nhung VO chi chay khi 5
    // server kia chet het, nen gan tam tot hon de trong (de trong thi
    // fallback loai het server cung dang HLS ra khoi chuoi).
    public static string ServerKind(string gateway)
    {
        if (string.IsNullOrEmpty(gateway))
            return "";

        if (IsVoGateway(gateway))
            return KindHls;   // tam dinh, se resolve that o /video

        if (IsDdGateway(gateway))
            return KindMp4;   // DoodStream chi phuc vu mp4

        if (Regex.IsMatch(gateway, @"[?&][oex]r=[0-9a-z]+", RegexOptions.IgnoreCase))
            return KindHls;   // JK (maxstream) / LU (lulustream) / SB (streamhg)

        return "";
    }

    public static bool IsDirectMp4(string url)
        => !string.IsNullOrEmpty(url)
           && url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    // Chi con TV (turbovid) la DA DANG: 2/3 phim co `data-hash` (master
    // .m3u8), 1/3 phim chi co `var urlPlay` (mp4). Phai mo trang player de
    // do. Cac server khai tra ve ngay tu ten nen chi ton mot request.
    //
    // Phai thu NHIEU lan nhu `Streams` (4) vi gateway /searcho hay tra 520
    // rong (16 byte) — thu 2 lan thi thuong rong, do xong -> `.m3u8` trong
    // khi resolve ra mp4, lai loi "no EXTM3U delimiter".
    public static async Task<string> ServerKindAsync(string gateway, int maxTime = 4, long deadline = 0)
    {
        string known = ServerKind(gateway);
        if (known != "")
            return known;

        string player = await CurlGetRetry(gateway, SiteHost + "/", null, 4, maxTime, deadline);
        if (string.IsNullOrEmpty(player))
        {
            Console.WriteLine($"JavGuru: kind probe rong {gateway}");
            return "";
        }

        // Soi dung thu tu chon cua `Streams`: packer truoc, StreamUrls sau,
        // neu lech thi route ra duoi sai.
        string packed = Unpack(player);
        string jsFile = packed != null ? PlayerJsFile(packed) : null;
        if (!string.IsNullOrEmpty(jsFile))
            return IsDirectMp4(jsFile) ? KindMp4 : KindHls;

        foreach (string u in StreamUrls(player))
            return IsDirectMp4(u) ? KindMp4 : KindHls;

        return "";
    }

    // ================= DD (DoodStream) =================
    //
    // Cong thuc (dung chung moi site, xem skill `lampac-adult-module` muc 15):
    //   1. GET embed `vide0.net/e/<id>` (-L) -> HTML chua
    //      `pass_md5/<hash>/<token>`; host API = url_effective CAT TAI "/e/"
    //   2. GET https://<hostAPI>/pass_md5/<hash>/<token>?referer=jav.guru
    //      -> TEXT THUAN: https://<rand>.cloudatacdn.com/<path>/<file>
    //   3. Nhan mp4 do query `?token=<token>&expiry=<unix_ms>` va bat buoc co
    //      header Referer khop host API.
    //
    // Buoc 3 la mau chot: thieu query HOAC thieu Referer thi CDN 302 sang
    // `*.dood.video`, host do tro 127.0.0.1 o ca public DNS (Cloudflare/Google
    // DoH deu vay) nen chet tuyet doi. Gia tri token/expiry khong quan trong —
    // token sai van 206 — chi can URL *co* query string.
    public static async Task<(string url, string referer)> DoodSourceAsync(
        string embedUrl, string referer, int maxTime = 6, long deadline = 0)
    {
        if (string.IsNullOrEmpty(embedUrl))
            return (null, null);

        var got = await CurlGetUrl(embedUrl, referer ?? (SiteHost + "/"), maxTime);
        var m = Regex.Match(got.body ?? "", @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
        if (!m.Success)
            return (null, null);

        string baseUrl = null;
        if (!string.IsNullOrEmpty(got.finalUrl))
        {
            int at = got.finalUrl.IndexOf("/e/", StringComparison.OrdinalIgnoreCase);
            if (at > 8)
                baseUrl = got.finalUrl[..at];
        }

        if (string.IsNullOrEmpty(baseUrl))
            return (null, null);

        string src = await CurlGetRetry(
            $"{baseUrl}/{m.Groups[1].Value}?referer=jav.guru",
            referer ?? (SiteHost + "/"), null, 4, maxTime, deadline);
        src = (src ?? "").Trim();

        // API co the tra chuoi "RELOAD" (thi phai tai lai embed roi thu lai) —
        // check StartsWith("http") loai ca truong hop do.
        if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        string token = m.Groups[1].Value.Split('/')[^1];
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        return (src + (src.Contains('?') ? "&" : "?")
                 + $"token={token}&expiry={expiry}", baseUrl);
    }

// VO (voe.sx): gateway /searcho/?tr= 302 THANG sang trang embed
// `https://<rand>.com/e/<id>` — giong DD (302 sang vide0.net/e/). Nhan dien
// rieng vi khong co trang player de boc data-hash/urlPlay (xem IsDdGateway).
//
// Trang embed la JW Player + JS obfuscate (mang chuoi ma hoa o(), khong packer,
// khong sources san): curl khong lay duoc link nen DUNG Chrome (Playwright)
// mo that trang embed, cho JW Player hydrate (~3s), bat network request co
// `.m3u8`/`.mp4` + doc `jwplayer('a').getPlaylist()[0].sources`.
//
// KHONG boc trong `/vidosik`: mo Chrome ton 4-8s moi server, vuot deadline 6s
// do dang cua ca menu. VO o `/vidosik` chi duoc gan tam `.m3u8` (da so phim
// VOE tra HLS); `/video` resolve that roi Redirect thang ve URL upstream, sai
// dang thi app bao loi — nhung VO chi chay khi 5 server kia chet het nen sai
// so it gap hon la cho 30s moi cua popup.
public static bool IsVoGateway(string gateway)
    => !string.IsNullOrEmpty(gateway)
       && Regex.IsMatch(gateway, @"[?&]t[dr]=[0-9a-z]+",
           RegexOptions.IgnoreCase);

public static bool IsVoServer(string label)
    => !string.IsNullOrEmpty(label)
       && label.IndexOf("VO", StringComparison.OrdinalIgnoreCase) >= 0;

    // ================= VO (voe.sx, qua Chrome) =================
    //
    // Xem comment o IsVoGateway: trang embed obfuscate, curl khong ra link.
    // Mo that bang Playwright (mau TopGai): bat network `.m3u8`/`.mp4` truoc
    // roi moi Goto, cho JW Player hydrate, doc `jwplayer('a').getPlaylist()`,
    // fallback network da bat duoc.
    //
    // `pageUrl` la URL gateway /searcho/?tr= (CHUA dao nguoc gi ca): Chrome tu
    // follow 302 sang trang embed nhu trinh duyet that. `sub` la deadline
    // rieng cua server nay (12s): het gio thi tra rong de chuoi fallback thu
    // server tiep theo, khong treo ca chuoi.
    public static async Task<List<(string url, string tag)>> VoSourceAsync(
        string pageUrl, string referer, long sub)
    {
        var res = new List<(string url, string tag)>();
        if (string.IsNullOrEmpty(pageUrl))
            return res;

        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync("JavGuru",
                    new Dictionary<string, string>
                    {
                        ["User-Agent"] = ChromeUA,
                        ["Referer"] = referer ?? (SiteHost + "/")
                    }, keepopen: false);

                if (page == null)
                    return res;

                string got = null;
                page.Request += (_, req) =>
                {
                    try
                    {
                        string u = req.Url;
                        if (got != null)
                            return;

                        bool media = u.Contains(".m3u8") || u.Contains(".mp4");
                        bool ad = u.IndexOf("ima3.js",
                                StringComparison.OrdinalIgnoreCase) >= 0
                            || u.IndexOf("ads",
                                StringComparison.OrdinalIgnoreCase) >= 0;

                        if (media && !ad)
                            got = u;
                    }
                    catch { }
                };

                int ms = (int)Math.Max(3000,
                    Math.Min(11000, sub - Environment.TickCount64));
                await page.GotoAsync(pageUrl,
                    new Microsoft.Playwright.PageGotoOptions
                    {
                        Timeout = ms,
                        WaitUntil =
                            Microsoft.Playwright.WaitUntilState.DOMContentLoaded
                    });

                // Cho JW Player hydrate: poll playlist, thay vi Sleep cung 5s.
                string file = null;
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        file = await page.EvaluateAsync<string>(@"() => {
                            try {
                                var p = jwplayer('a');
                                var pl = p && p.getPlaylist
                                    ? p.getPlaylist() : null;
                                var s = pl && pl[0] && pl[0].sources
                                    ? pl[0].sources : null;
                                return (s && s[0] && s[0].file)
                                    ? s[0].file : null;
                            } catch (e) { return null; }
                        }");
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(file) || got != null)
                        break;

                    await Task.Delay(700);
                }

                try { await page.CloseAsync(); } catch { }

                string best = !string.IsNullOrEmpty(file) ? file : got;
                if (string.IsNullOrEmpty(best))
                    return res;

                // VOE tra HLS; mp4 thi tag de /video Redirect dung dang.
                string tag = best.IndexOf(".mp4",
                    StringComparison.OrdinalIgnoreCase) >= 0 ? "mp4" : "";

                res.Add((best, tag));
                return res;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavGuru: voe loi {ex.Message}");
            return res;
        }
    }

    public static async Task<(string body, string finalUrl)> CurlGetUrl(
        string url, string referer, int maxTime = 25, bool http2 = true)
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
            psi.ArgumentList.Add("-w");
            psi.ArgumentList.Add("\n@@FINAL@@%{url_effective}");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return (null, null);

                string stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                    return (null, null);

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

    // LU: mint token TRON 1 session curl_cffi (Chrome fingerprint) via
    // luresolve.py — CDN *.tnmr.org ky token theo TLS cua ben mint nen minh
    // phai giong Chrome thi app (Chromium) moi tai duoc. Dung file .py rieng
    // (test truc tiep duoc), khong ghep string trong C#.
    // Tra (variant, cao, embed).
    public static async Task<(string url, string tag, string embed)> LuResolveAsync(
        string gatewayUrl, string referer, int maxTime = 8)
    {
        if (string.IsNullOrEmpty(gatewayUrl))
            return (null, null, null);
        try
        {
            string script = System.IO.Path.Combine(ModInit.modpath ?? "", "luresolve.py");
            if (!System.IO.File.Exists(script))
                return (null, null, null);
            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            PrepCurlEnv(psi);
            string py = "/data/data/com.termux/files/usr/bin/python3";
            psi.FileName = System.IO.File.Exists(py) ? py : "python3";
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add(gatewayUrl);
            psi.ArgumentList.Add(referer ?? "");
            psi.ArgumentList.Add(Math.Max(5, maxTime).ToString());
            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return (null, null, null);
                string stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                    return (null, null, null);
                var lines = stdout.Split('\n');
                string url = lines.Length > 0 ? lines[0].Trim() : "";
                string tag = lines.Length > 1 && int.TryParse(lines[1].Trim(), out int h) && h > 0 ? h + "p" : "";
                string emb = lines.Length > 2 ? lines[2].Trim() : "";
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return (null, null, null);
                return (url, tag, emb);
            }
        }
        catch
        {
            return (null, null, null);
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

    // AV MAKER + TAGS — lay tu cac trang list san cua site (giong JavTsunami
    // boc category tu /categories). Chi la dong TEXT trong submenu (cache 1
    // gio, 0.008s khong ton request) nen boc bao nhieu cung duoc; thoi gian
    // nam o luc bam vao va tai phim ben trong.
    //
    //   makers : 1 trang x 992 hang (fetch 1 lan, boc ngau 40)
    //   studios: 1 trang x 4663 hang (fetch 1 lan, boc ngau 40)
    //   tags   : 1 trang x 553 hang (co so phim, boc TOP 100 theo so phim)
    public const string MakerPath = "/jav-makers-list";
    public const string StudioPath = "/jav-studio-list";
    public const string TagsPath = "/tags";
    // MOI NHOM = 1 muc TANG 1, submenu la cac muc trong nhom do.
    // Client SISI chi hien MOT tang submenu nen KHONG tach
    // `Hang phim -> A -> MOODYZ` (3 tang = bay, muc 9c) — phai chia
    // CHU CAI o tang 1.
    public const int MaxPerBucket = 300;

    // Muc la `<a href=".../maker/moodyz/">MOODYZ</a>`. Tra ve (ten, duong dan
    // doi) de Controller doi vao `?c=...`.
    public static List<(string name, string path)> DirList(
        string html, string kind, int limit = int.MaxValue)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(kind))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string rx = "<a href=\"https://jav\\.guru/" + kind
            + "/([^\"]+/)\">([^<]{2,50})</a>";

        foreach (Match m in Regex.Matches(html, rx, RegexOptions.IgnoreCase))
        {
            if (list.Count >= limit)
                break;

            string path = (kind + "/" + m.Groups[1].Value).Trim('/');
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());

            if (name.Length == 0 || !seen.Add(path))
                continue;

            list.Add((name, path));
        }

        return list;
    }

    // Hang TAG co so phim trong ngoac: `<a ...>3P <span>(16627)</span></a>`.
    // Boc TOP theo so phim thay vi ngau nhien — tag la the loai, lay top la
    // dung nhu menu The loai cu (FC2, 4K...). Dung so trong `/tags` vi trang
    // nay ghi ca tag 0 phim.
    public static List<(string name, string path)> TagList(
        string html, int limit = int.MaxValue)
    {
        var rows = new List<(string name, string path, int n)>();
        if (string.IsNullOrEmpty(html))
            return rows.Select(x => (x.name, x.path)).ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<li><a href=\"https://jav\\.guru/tag/([^\"]+/)\".*?>"
                + "(.*?)</a></li>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string raw = m.Groups[2].Value;
            raw = Regex.Replace(raw, "<[^>]+>", " ").Trim();
            raw = HttpUtility.HtmlDecode(raw);
            raw = Regex.Replace(raw, @"\s+", " ").Trim();

            var mc = Regex.Match(raw, @"^(.*?)\s*\((\d+)\)\s*$");
            string name = mc.Success ? mc.Groups[1].Value.Trim() : raw;
            int n = 0;
            if (mc.Success)
                int.TryParse(mc.Groups[2].Value, out n);

            string path = ("tag/" + m.Groups[1].Value).Trim('/');
            if (name.Length == 0 || n <= 0 || !seen.Add(path))
                continue;

            rows.Add((name, path, n));
        }

        return rows.OrderByDescending(x => x.n)
            .Take(limit)
            .Select(x => (x.name, x.path))
            .ToList();
    }

    // ============ BUCKET THEO CHU CAI (taxonomy 4669 muc) ============
    //
    // `/jav-makers-list` = 992 hang · `/jav-studio-list` = 4669 studio ·
    // `/tags` = 540 tag co phim > 0.
    //
    // KHONG cat top N (JavCt tung cat 100/1479 -> user tuong thieu muc, ke
    // ca 40 ngau nhien o day cung vay). Cung khong boc 4669 muc vao MOT
    // submenu (client cuon nang). Cach dung: goi KY TU DAU cua ten thanh
    // nhom, roi GOI CAC NHOM LIEN TIEP vao cung mot muc tang 1 cho den
    // khi du `maxPer`. Ten muc `<title> A–B`, nhom 1 chu cai thi
    // `<title> A`.
    //
    // Client chi 1 tang submenu nen KHONG tach `Hang phim -> A -> MOODYZ`
    // (3 tang = muc chet, muc 9c) — moi nhom phai la muc TANG 1.
    public static List<Shared.Models.SISI.Base.MenuItem> DirBuckets(
        string host, string title,
        List<(string name, string path)> all, int maxPer = MaxPerBucket)
    {
        var res = new List<Shared.Models.SISI.Base.MenuItem>();
        if (all == null || all.Count == 0)
            return res;

        // Gom theo KY TU DAU cua ten. Ky tu khong phai A-Z (so, `#`, tieng
        // Nhat) gom vao nhom `#` — khong in ky tu la ra ten muc, Lampa
        // hien thi duoc nhung client tach subtitle bang `:` nen ky ten
        // la se bay dong.
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

        // Flatten thanh 1 danh sach co nhan de cat chunk. Nhom nao vuot
        // `maxPer` (vd Studio M 459) se bi cat giua: phan du lot sang
        // chunk sau, KHONG bo muc nao.
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
                    x.name, host + "/javguru?c=" + x.path)).ToList());
        }

        // Nhom bi cat GIUA (from == to, vd `Studio M` 459 muc -> 2 chunk
        // cung bat dau bang `M`) thi them `(1/2)`, `(2/2)` — khong thi
        // menu hien hai dong trung ten, user khong biet phan nao nao.
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

    // `#` < 0 < 1 < ... < 9 < A < B < ... < Z. So PHAI xep TANG so
    // (cua chu so) chu khong phai tat ca so cung hang — neu khong thi
    // ten muc ra `Studio 9–7` (thu tu site) thay vi `Studio 0–7`.
    static int CharRank(char c)
    {
        if (c == '#')
            return 0;
        if (c >= '0' && c <= '9')
            return 1 + (c - '0');
        if (c >= 'A' && c <= 'Z')
            return 20 + (c - 'A');
        return 100;      // ky tu khac (tieng Nhat...)
    }

    // Tags da xep theo SO PHIM giam dan (TagList) nen chia theo THU TU,
    // khong theo chu cai — nhom dau la tag pho bien nhat.
    public static List<Shared.Models.SISI.Base.MenuItem> TagBuckets(
        string host, List<(string name, string path)> all, int maxPer = MaxPerBucket)
    {
        var res = new List<Shared.Models.SISI.Base.MenuItem>();
        if (all == null || all.Count == 0)
            return res;

        for (int i = 0; i < all.Count; i += maxPer)
        {
            var chunk = all.Skip(i).Take(maxPer).ToList();
            string t = i == 0
                ? (all.Count <= maxPer ? "Từ khóa" : "Từ khóa phổ biến")
                : $"Từ khóa {i + 1}–{i + chunk.Count}";

            res.Add(new Shared.Models.SISI.Base.MenuItem(t, "submenu")
            {
                submenu = chunk.Select(x =>
                    new Shared.Models.SISI.Base.MenuItem(
                        x.name, host + "/javguru?c=" + x.path)).ToList()
            });
        }

        return res;
    }

    // XOA `DirPick` (Fisher-Yates cat con 40 ngau nhien): user thay
    // "sao lay chi co 40" ma khong hieu quy tac nao. Menu di qua
    // `DirBuckets` — lay HET roi chia nhom theo chu cai.

    // ===== DÒNG 2 "Sắp xếp" — sort CHO LIST ĐANG MỞ (chuẩn 9g, chốt 2026-10-06) =====
    // BẢN CŨ SAI MỤC ĐÍCH: dòng 2 bám cứng `?c=category/jav` -> mở tag/hãng nào
    // nó vẫn hiện list của category/jav, không sort gì cả.
    // BẢN MỚI: row 2 GIỮ NGUYÊN `c`/`search`/home, chỉ đổi sort. Vẫn 2 tầng.
    //
    // Sort thật (fetch truc tiep jav.guru, 17 gia tri thu, 2026-10-06):
    //   views / likes / title  -> khac default (0/0/0) va khac nhau (0/13/0)
    //   date / relevance / year / popular / hot -> no-op (WP bo qua)
    //   post__in               -> list RONG  => bat buoc whitelist
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất",         ""),
        ("Xem nhiều nhất",   "views"),
        ("Nhiều like nhất",  "likes"),
        ("Tên A–Z",          "title"),
    };

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in Sorts) if (s == sort) return s;
        return null;
    }

    public static string SortLabel(string sort) =>
        string.IsNullOrEmpty(sort) ? "mới nhất"
        : sort == "views" ? "xem nhiều"
        : sort == "likes" ? "nhiều like"
        : sort == "title" ? "tên A–Z" : sort;

    // head: phụ thuộc search/sort/c -> dựng lại mỗi request (rẻ)
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(
        string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/javguru";
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
                title = "Tìm kiếm", search_on = "search_on", playlist_url = root
            }
        };
        var sub = new List<Shared.Models.SISI.Base.MenuItem>(Sorts.Length);
        foreach (var (name, s) in Sorts)
            sub.Add(new(name, link(s)));
        res.Add(new Shared.Models.SISI.Base.MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort)}", playlist_url = "submenu", submenu = sub
        });
        return res;
    }

    // base: KHÔNG phụ thuộc search/sort/c -> cache 1 lần
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host,
        List<(string name, string path)> makers = null,
        List<(string name, string path)> studios = null,
        List<(string name, string path)> tags = null)
    {
        var genres = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            // "Tất cả" = trang chủ (không lọc category) — pattern Eporner.
            // Phải có ở đây vì dòng 2 không còn chứa link trang chủ nữa
            // (dòng 2 = sort cho list đang mở, xem MenuHead()).
            new("Tất cả (trang chủ)", host + "/javguru"),
            new("JAV", host + "/javguru?c=category/jav"),
            new("Không che", host + "/javguru?c=category/decensored"),
            new("Có phụ đề", host + "/javguru?c=category/english-subbed"),
            new("Amateur", host + "/javguru?c=category/amateur"),
            new("Idol", host + "/javguru?c=category/idol"),
            new("FC2", host + "/javguru?c=category/FC2"),
            new("4K", host + "/javguru?c=category/4k"),
        };

        // DÒNG 1 + DÒNG 2 do MenuHead() dựng riêng mỗi request:
        //   "Tìm kiếm" + "Sắp xếp: <sort hiện tại>" — GIỮ NGUYÊN c/search của
        //   list đang mở, chỉ đổi sort (chuẩn 9g). Bản cũ bám cứng
        //   `?c=category/jav` nên mở tag/hãng nào nó cũng hiện list category/jav.
        // Trang chủ ("Mới nhất") chuyển vào "Thể loại" -> mục "Tất cả"
        // (pattern Eporner): JavGuru không có bảng xếp hạng nào khác.
        var menu = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            // Khong dua trang "Xem nhieu" (most-watched-rank) vao menu: trang do
            // dung markup <article class="rank-item"> va /page/N/ tra lai
            // dung mot bang xep hang -> phan trang se lap phim. Dung `?orderby=views`
            // o tren thi lay duoc noi dung that cua WP.
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres
            }
        };

        // Taxonomy: CHIA THEO CHU CAI (muc tang 1 = 1 nhom), khong cat top N.
        // submenu rong thi bo qua — trang dir fetch loi thi menu cu van du.
        menu.AddRange(JavGuruTo.DirBuckets(host, "Hãng phim", makers));
        menu.AddRange(JavGuruTo.DirBuckets(host, "Studio", studios));
        menu.AddRange(JavGuruTo.TagBuckets(host, tags));

        return menu;
    }
}
