// MediaProbe — tool soi nhanh site moi cho module Lampac Adult.
// Dung: MediaProbe <url-detail-hoac-embed> [--proxy URL] [--timeout S]
//
// Pipeline: fetch trang -> liet ke ung vien (iframe, data-source/__pt/__pk,
// data-api, pass_md5, packer, robotlink, ajax/player) -> thu tung resolver
// da biet (Dood, StreamHg, UPN, Playmate, F4, Vidara, Streamtape) -> in link.
// Khong thay viec do live ky: pt xoay vong / cookie / Cloudflare rieng tung
// site van phai custom trong module.
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

static class Probe
{
    const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static string _proxy;
    static int _timeout = 15;
    static int _exit = 1;

    static async Task<int> Main(string[] args)
    {
        string url = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--proxy" && i + 1 < args.Length) _proxy = args[++i];
            else if (args[i] == "--timeout" && i + 1 < args.Length) int.TryParse(args[++i], out _timeout);
            else if (!args[i].StartsWith("--")) url = args[i];
        }
        _proxy ??= Environment.GetEnvironmentVariable("HTTPS_PROXY")
            ?? Environment.GetEnvironmentVariable("https_proxy")
            ?? Environment.GetEnvironmentVariable("HTTP_PROXY");

        if (string.IsNullOrEmpty(url))
        {
            Console.WriteLine("Dung: MediaProbe <url> [--proxy URL] [--timeout S]");
            return 1;
        }

        Console.WriteLine($"URL: {url}");
        Console.WriteLine($"proxy: {(_proxy ?? "(khong)")}, timeout: {_timeout}s");
        Console.WriteLine(new string('-', 70));

        var page = await Get(url, url);
        if (string.IsNullOrEmpty(page))
        {
            Console.WriteLine("FAIL: khong fetch duoc trang (thu --proxy http://168.107.66.134:3128).");
            return 1;
        }
        Console.WriteLine($"page: {page.Length} chars");
        _exit = 2;

        // ---- 1. ung vien ----
        var iframes = Re(page, @"<iframe\b[^>]+\bsrc\s*=\s*[""']([^""']+)[""']").Distinct().ToList();
        Console.WriteLine($"\n[CANDIDATES] iframe: {iframes.Count}");
        foreach (var f in iframes.Take(15)) Console.WriteLine($"  < {Norm(f, url)}");

        var ds = Re1(page, @"data-source\s*=\s*[""']([^""']+)[""']");
        var pt = Re1(page, @"window\.__pt\s*=\s*[""']([^""']+)[""']");
        var pk = Re1(page, @"window\.__pk\s*=\s*[""']([^""']+)[""']");
        var api = Re1(page, @"data-api\s*=\s*[""']([^""']+)[""']");
        var btns = Regex.Matches(page, @"<button\b[^>]*\bdata-id\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</button\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Count;
        Console.WriteLine($"  data-source={ds ?? "-"} __pt={(pt != null ? "co" : "-")} __pk={(pk != null ? "co" : "-")} data-api={api ?? "-"} buttons[data-id]={btns}");
        Console.WriteLine($"  pass_md5={(page.Contains("pass_md5/") ? "CO" : "-")} packer={(page.Contains("function(p,a,c,k,e,d)") ? "CO" : "-")} robotlink={(page.Contains("robotlink") ? "CO" : "-")}");
        bool ajaxHint = page.Contains("/ajax/player") || page.Contains("player_enc")
            || (ds != null && pt != null);
        Console.WriteLine($"  ajax/player={(ajaxHint ? "CO — trang detail kieu POST (data-source+pt): can module POST rieng, mau JavCt/SexTb (pt xoay vong, moi nut 1 pt tuoi)" : "-")}");
        if (page.Contains("We are updating")) Console.WriteLine("  !! trang bao 'We are updating' = server chet");

        // ---- 2. page-level: media truc tiep / packer inline ----
        foreach (var m in Re(page, @"https?://[^\s""'\\<>]+\.(m3u8|mp4)[^\s""'\\<>]*").Distinct().Take(10))
            Resolved("page-direct", m, url);

        var phls = StreamHgMaster(page);
        if (phls != null) Resolved("page-packer", phls, url);

        // ---- 3. tung embed (neu trang chinh no la embed thi giai luon) ----
        int n = 0;
        if (iframes.Count == 0)
        {
            bool looksEmbed = api != null || page.Contains("pass_md5/")
                || page.Contains("function(p,a,c,k,e,d)") || page.Contains("robotlink")
                || page.Contains("upn.one") || page.Contains("playmate.to")
                || page.Contains("vidara") || page.Contains("videoData");
            if (looksEmbed)
            {
                Console.WriteLine("\n[EMBED 1] (chinh trang nay)");
                await TryEmbed(url, page, url);
            }
            else
                Console.WriteLine("\n(trang detail, khong phai embed — embed nam sau POST ajax, can module rieng)");
        }
        foreach (var f in iframes.Distinct().Take(8))
        {
            string embed = Norm(f, url);
            if (!embed.StartsWith("http")) continue;
            n++;
            Console.WriteLine($"\n[EMBED {n}] {embed}");
            var sw = Stopwatch.StartNew();
            string html = await Get(embed, url);
            sw.Stop();
            if (string.IsNullOrEmpty(html)) { Console.WriteLine($"  fetch fail ({sw.ElapsedMilliseconds}ms)"); continue; }
            Console.WriteLine($"  embed html: {html.Length} chars ({sw.ElapsedMilliseconds}ms)");
            if (html.Contains("We are updating")) { Console.WriteLine("  !! 'We are updating' = server chet, bo qua"); continue; }
            await TryEmbed(embed, html, url);
        }

        Console.WriteLine(new string('-', 70));
        Console.WriteLine(_exit == 0 ? "XONG: co it nhat 1 link." : "XONG: khong resolve duoc — xem CANDIDATES + HINT o tren de custom module.");
        return _exit;
    }

    // Thu cac resolver theo thu tu re -> dat. Tra true neu ra link.
    static async Task TryEmbed(string embed, string html, string referer)
    {
        // StreamQQ (streamforester/vcast, gap o JavSub 2026-10-09):
        // vcast: sources nam san trong videoData; streamforester:
        // POST <origin>/videos/<id>/config?d=<domain> (body {} — body rong
        // bi Fastify 400). POST 404 tu server la binh thuong (site gate
        // server-side) -> module dung Chrome fallback mo embed that.
        if (Regex.IsMatch(embed, @"/videos/[A-Za-z0-9]+/play", RegexOptions.IgnoreCase))
        {
            var sq = await StreamQQSource(embed, html);
            if (sq != null) { Resolved("streamqq", sq, embed); return; }
            Console.WriteLine("  streamqq: khong ra sources (POST config 404 tu server-side -> module dung Chrome fallback, mau JavSub.ChromeSourceAsync)");
            return;
        }
        // DoodStream: pass_md5
        var pm = Regex.Match(html, @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
        if (pm.Success)
        {
            string apiHost = HostOf(embed);
            try
            {
                var u = new Uri(embed);
                int at = u.AbsoluteUri.IndexOf("/e/", StringComparison.OrdinalIgnoreCase);
                if (at > 8) apiHost = u.AbsoluteUri.Substring(0, at);
            }
            catch { }
            string host = new Uri(embed).Host;
            string src = await Get($"{apiHost}/{pm.Groups[1].Value}?referer={host}", apiHost + "/");
            src = (src ?? "").Trim();
            if (src.StartsWith("http")) { Resolved("dood", src.Split(' ')[0], embed); return; }
            Console.WriteLine("  dood: co pass_md5 nhung API khong tra link");
        }

        // F4 (f4s.top/f4stream): data-api -> JSON url
        if (embed.Contains("f4s.top") || embed.Contains("f4stream") || html.Contains("data-api"))
        {
            var f4 = await F4Source(embed, html);
            if (f4 != null) { Resolved("F4", f4, embed); return; }
            if (embed.Contains("f4s.top") || embed.Contains("f4stream"))
                Console.WriteLine("  F4: khong boc duoc data-api/url");
        }

        // Vidara: POST <host>/api/stream {filecode}
        if (embed.Contains("vidara") || Regex.IsMatch(embed, @"https?://[^/]+/e/[A-Za-z0-9]+/?$"))
        {
            var m = Regex.Match(embed, @"(https?://[^/]+)/e/([A-Za-z0-9]+)");
            if (m.Success)
            {
                string js = await Post(m.Groups[1].Value + "/api/stream",
                    "{\"filecode\":\"" + m.Groups[2].Value + "\",\"device\":\"web\"}", embed);
                var um = Regex.Match(js ?? "", @"""streaming_url""\s*:\s*""([^""]+)""");
                if (um.Success) { Resolved("vidara", um.Groups[1].Value.Replace("\\/", "/"), embed); return; }
                Console.WriteLine("  vidara: POST /api/stream khong tra streaming_url");
            }
        }

        // UPN: #id -> hex -> AES
        if (embed.Contains("upn.one") || embed.Contains("strp2p.com"))
        {
            var upn = await UpnSource(embed);
            if (upn != null) { Resolved("UPN", upn, embed); return; }
            Console.WriteLine("  UPN: API/AES khong ra cfNative");
        }

        // Playmate: POST /api/s
        if (embed.Contains("playmate.to"))
        {
            var pmm = await PlaymateSource(embed);
            if (pmm != null) { Resolved("playmate", pmm, embed); return; }
            Console.WriteLine("  playmate: POST /api/s khong ra sx");
        }

        // StreamHg clone: packer base36
        var hls = StreamHgMaster(html);
        if (hls != null) { Resolved("streamhg", hls, embed); return; }
        if (html.Contains("function(p,a,c,k,e,d)")) Console.WriteLine("  streamhg: co packer nhung khong thay links hls2/3/4");

        // Streamtape: robotlink -> get_video -> 302 CDN
        if (html.Contains("robotlink"))
        {
            string stApi = StreamtapeMp4(html);
            if (stApi != null)
            {
                string cdn = await FinalUrl(stApi, embed);
                Resolved("streamtape", string.IsNullOrEmpty(cdn) ? stApi : cdn, embed);
                return;
            }
            Console.WriteLine("  streamtape: co robotlink nhung khong boc duoc get_video");
        }

        Console.WriteLine("  --> khong thuoc ho nao da biet (them cong thuc moi vao tool + module).");
    }

    static void Resolved(string kind, string link, string referer)
    {
        string k = (link.Contains(".m3u8") || link.Contains("/hls/") || link.Contains("master") || link.Contains("/v/eyJ")) ? "hls" : "mp4";
        Console.WriteLine($"  [RESOLVED:{kind}/{k}] {link}");
        Console.WriteLine($"             referer: {referer}");
        _exit = 0;
    }

    // ============ fetch ============
    static HttpClient Client(bool follow)
    {
        var h = new SocketsHttpHandler
        {
            AllowAutoRedirect = follow,
            AutomaticDecompression = DecompressionMethods.All,
        };
        if (!string.IsNullOrEmpty(_proxy)) h.Proxy = new WebProxy(_proxy);
        h.UseProxy = !string.IsNullOrEmpty(_proxy);
        var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(Math.Max(6, _timeout)) };
        c.DefaultRequestHeaders.Add("User-Agent", ChromeUA);
        return c;
    }

    static async Task<string> Get(string url, string referer)
    {
        foreach (bool useProxy in string.IsNullOrEmpty(_proxy) ? new[] { false } : new[] { true })
        {
            try
            {
                // (hien chi dung direct hoac proxy theo --proxy; giu vong lap de mo rong fallback sau)
                using var c = Client(true);
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Referrer = new Uri(referer.StartsWith("http") ? referer : url);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                using var r = await c.SendAsync(req);
                if (!r.IsSuccessStatusCode) continue;
                return await r.Content.ReadAsStringAsync();
            }
            catch { }
        }
        return null;
    }

    static async Task<string> Post(string url, string json, string referer)
    {
        try
        {
            using var c = Client(true);
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            req.Headers.Referrer = new Uri(referer);
            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            using var r = await c.SendAsync(req);
            if (!r.IsSuccessStatusCode) return null;
            return await r.Content.ReadAsStringAsync();
        }
        catch { return null; }
    }

    static async Task<string> FinalUrl(string url, string referer)
    {
        try
        {
            using var c = Client(false);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri(referer);
            using var r = await c.SendAsync(req);
            if ((int)r.StatusCode is >= 300 and < 400 && r.Headers.Location != null)
            {
                var loc = r.Headers.Location;
                return loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(url), loc).ToString();
            }
            return null;
        }
        catch { return null; }
    }

    // ============ resolvers (copy cong thuc tu modules, giu nguyen) ============
    static string StreamHgMaster(string html)
    {
        string plain = Unpack(html);
        if (string.IsNullOrEmpty(plain)) return null;
        foreach (var key in new[] { "hls3", "hls2", "hls4" })
        {
            var m = Regex.Match(plain, "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"");
            if (!m.Success) continue;
            string u = m.Groups[1].Value.Replace("\\/", "/");
            if (u.StartsWith("//")) u = "https:" + u;
            if (u.StartsWith("http")) return u;
        }
        return null;
    }

    static string Unpack(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"eval\(function\(p,a,c,k,e,d\)\{[^}]*\}\('([\s\S]*?)',(\d+),(\d+),'([\s\S]*?)'\.split\('\|'\)\)");
        if (!m.Success) return null;
        string p = m.Groups[1].Value;
        if (!int.TryParse(m.Groups[2].Value, out int a) || !int.TryParse(m.Groups[3].Value, out int c)) return null;
        var k = m.Groups[4].Value.Split('|');
        for (int i = c - 1; i >= 0; i--)
        {
            if (i >= k.Length || string.IsNullOrEmpty(k[i])) continue;
            p = Regex.Replace(p, @"\b" + ToBase(i, a) + @"\b", k[i]);
        }
        return p;
    }

    static string ToBase(int v, int b)
    {
        if (v == 0) return "0";
        var sb = new StringBuilder();
        while (v > 0) { int d = v % b; sb.Insert(0, (char)(d < 10 ? '0' + d : 'a' + d - 10)); v /= b; }
        return sb.ToString();
    }

    static async Task<string> UpnSource(string embedUrl)
    {
        int hash = embedUrl.IndexOf('#');
        if (hash < 0) return null;
        string id = embedUrl.Substring(hash + 1).Trim().Trim('/');
        int amp = id.IndexOf('&');
        if (amp >= 0) id = id.Substring(0, amp);
        if (id.Length < 2) return null;
        string host;
        try { host = new Uri(embedUrl).GetLeftPart(UriPartial.Authority); }
        catch { return null; }
        string hex = await Get(host + "/api/v1/video?id=" + Uri.EscapeDataString(id), host + "/");
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim().Trim('"');
        if (hex.Length % 2 != 0) return null;
        var data = new byte[hex.Length / 2];
        for (int i = 0; i < data.Length; i++)
        {
            if (!Uri.IsHexDigit(hex[i * 2]) || !Uri.IsHexDigit(hex[i * 2 + 1])) return null;
            data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        try
        {
            using var aes = Aes.Create();
            aes.KeySize = 128; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes("kiemtienmua911ca");
            aes.IV = Encoding.UTF8.GetBytes("1234567890oiuytr");
            string js = Encoding.UTF8.GetString(aes.CreateDecryptor().TransformFinalBlock(data, 0, data.Length));
            var doc = JsonDocument.Parse(js);
            if (doc.RootElement.TryGetProperty("cfNative", out var cf) && cf.GetString().StartsWith("http"))
                return cf.GetString();
        }
        catch { }
        return null;
    }

    static async Task<string> PlaymateSource(string embedUrl)
    {
        string id, host;
        try
        {
            var u = new Uri(embedUrl);
            id = u.AbsolutePath.Trim('/').Split('/').Last();
            host = u.GetLeftPart(UriPartial.Authority);
        }
        catch { return null; }
        if (string.IsNullOrEmpty(id)) return null;
        string js = await Post(host + "/api/s", $"{{\"c\":\"{id}\",\"d\":\"desktop\"}}", host + "/");
        var m = Regex.Match(js ?? "", "\"sx\"\\s*:\\s*\"([^\"]+)\"");
        if (!m.Success) return null;
        string src = m.Groups[1].Value.Replace("\\/", "/");
        return src.StartsWith("http") ? src : null;
    }

    static async Task<string> F4Source(string embedUrl, string embedHtml)
    {
        string host;
        try { host = new Uri(embedUrl).GetLeftPart(UriPartial.Authority); }
        catch { return null; }
        var am = Regex.Match(embedHtml ?? "", @"data-api\s*=\s*[""']([^""']+)[""']");
        if (!am.Success) return null;
        string api = am.Groups[1].Value.Trim();
        if (api.StartsWith("/")) api = host + api;
        else if (!api.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;
        string js = await Get(api, embedUrl);
        var um = Regex.Match(js ?? "", @"""url""\s*:\s*""([^""]+)""");
        if (!um.Success) return null;
        string src = um.Groups[1].Value.Replace("\\/", "/");
        if (src.StartsWith("/")) src = host + src;
        return src.StartsWith("http") ? src : null;
    }

    static async Task<string> StreamQQSource(string embedUrl, string embedHtml)
    {
        string host;
        try { host = new Uri(embedUrl).GetLeftPart(UriPartial.Authority); }
        catch { return null; }
        // 1. sources inline trong videoData (vcast)
        var im = Regex.Match(embedHtml ?? "", @"""sources""\s*:\s*\[\s*\{[^}]*?""file""\s*:\s*""([^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (im.Success)
        {
            string src = im.Groups[1].Value.Replace("\\/", "/").Trim();
            if (src.StartsWith("//")) return "https:" + src;
            if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return src;
            if (src.StartsWith("/")) return host + src;
        }
        // 2. POST config (streamforester): id + domain tu URL/page
        var idm = Regex.Match(embedUrl, @"/videos/([A-Za-z0-9]+)/play", RegexOptions.IgnoreCase);
        if (!idm.Success) return null;
        var dm = Regex.Match(embedHtml ?? "", @"""domain""\s*:\s*""([^""]*)""", RegexOptions.IgnoreCase);
        string domain = dm.Success ? dm.Groups[1].Value : "";
        string js = await Post(host + "/videos/" + idm.Groups[1].Value + "/config?d=" + Uri.EscapeDataString(domain),
            "{}", embedUrl);
        var fm = Regex.Match(js ?? "", @"""file""\s*:\s*""([^""]+\.(m3u8|mp4)[^""]*)""", RegexOptions.IgnoreCase);
        if (!fm.Success) return null;
        string v = fm.Groups[1].Value.Replace("\\/", "/");
        if (v.StartsWith("/")) v = host + v;
        return v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : null;
    }

    static string StreamtapeMp4(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        string query = null;
        foreach (Match m in Regex.Matches(html, @"robotlink.{0,4}\.innerHTML\s*=\s*'[^']*get_video\?'\s*\+\s*\('([^']+)'", RegexOptions.IgnoreCase))
            query = m.Groups[1].Value;
        if (string.IsNullOrEmpty(query))
            foreach (Match m in Regex.Matches(html, @"robotlink'\)\.innerHTML\s*=\s*'([^']*)'\s*\+\s*\('([^']*)'\)((?:\s*\.substring\(\d+\)\s*)*)", RegexOptions.IgnoreCase))
            {
                string tail = m.Groups[2].Value;
                foreach (Match s in Regex.Matches(m.Groups[3].Value ?? "", @"substring\((\d+)\)"))
                    if (int.TryParse(s.Groups[1].Value, out int nn) && nn >= 0 && nn <= tail.Length)
                        tail = tail.Substring(nn);
                query = m.Groups[1].Value + tail;
            }
        if (string.IsNullOrEmpty(query))
        {
            var div = Regex.Match(html, @"id\s*=\s*""robotlink""[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            if (div.Success) query = div.Groups[1].Value;
        }
        if (string.IsNullOrEmpty(query)) return null;
        var tailm = Regex.Match(query, @"id=[A-Za-z0-9_-]+&expires=\d+[^']*");
        if (tailm.Success) return "https://strtape.cloud/get_video?" + tailm.Value;
        string path = query.Trim();
        if (string.IsNullOrEmpty(path)) return null;
        if (path.StartsWith("//")) return "https:" + path;
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;
        if (path.Contains("get_video")) return "https://strtape.cloud/get_video?" + path[(path.IndexOf("get_video") + 9)..].TrimStart('?', '&');
        return "https://streamtape.com/" + path.TrimStart('/');
    }

    // ============ helpers ============
    static List<string> Re(string s, string pat) =>
        Regex.Matches(s ?? "", pat, RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value.Trim()).Where(v => v.Length > 0).ToList();

    static string Re1(string s, string pat)
    {
        var m = Regex.Match(s ?? "", pat, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    static string Norm(string src, string page)
    {
        src = System.Net.WebUtility.HtmlDecode(src.Trim());
        if (src.StartsWith("//")) return "https:" + src;
        if (src.StartsWith("http")) return src;
        try { return new Uri(new Uri(page), src).ToString(); } catch { return src; }
    }

    static string HostOf(string url)
    {
        try { return new Uri(url).GetLeftPart(UriPartial.Authority); } catch { return url; }
    }
}
