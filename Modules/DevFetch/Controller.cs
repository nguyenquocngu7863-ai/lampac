using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevFetch;

// TAM: proxy cleanup cho agent (xoa ca module truoc khi push).
// GET /devfetch?url=https://site/page -> raw HTML (fetch qua IP may)
// GET /devfetch?url=...&re=pattern&n=50 -> cac doan match (do ton context)
public class DevFetchController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public DevFetchController() : base(ModInit.conf) { }

    static bool IsBlockedHost(string url, out string why)
    {
        why = null;
        try
        {
            var u = new Uri(url);
            if (u.Scheme != "http" && u.Scheme != "https") { why = "scheme"; return true; }
            string h = u.Host.ToLowerInvariant();
            if (h == "localhost" || h.EndsWith(".localhost") || h.EndsWith(".internal") || h.EndsWith(".local")) { why = "local-name"; return true; }
            if (System.Net.IPAddress.TryParse(h, out var ip))
            {
                byte[] b = ip.GetAddressBytes();
                bool priv = b.Length == 4 && ((b[0] == 10) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 127) || (b[0] == 0));
                bool v6local = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    && (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || System.Net.IPAddress.IPv6Loopback.Equals(ip));
                if (priv || v6local) { why = "private-ip"; return true; }
            }
            int port = u.IsDefaultPort ? (u.Scheme == "https" ? 443 : 80) : u.Port;
            if (port == 9118 || port == 9117 || port == 3002 || port == 8191 || port == 9196) { why = "local-port"; return true; }
            return false;
        }
        catch { why = "bad-url"; return true; }
    }

    [HttpGet]
    [Route("devfetch")]
    async public Task<ActionResult> Fetch(string url, string re, int n = 50, string ua = null, string mode = null, string method = null, string body = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Content("usage: /devfetch?url=https://site/page&re=pattern&n=50", "text/plain");
        if (IsBlockedHost(url, out string why))
            return Content("blocked:" + why, "text/plain");
        // mode=probe: curl media nhu that bai playback - redirect chain + Range + codec
        if (mode == "probe")
            return await ProbeMedia(url);
        string html = null;
        try
        {
            using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                string uaStr = ua == "mobile"
                    ? "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"
                    : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                HttpRequestMessage req;
                if (method == "post")
                {
                    req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Content = new StringContent((body ?? "").Length > 2000 ? body.Substring(0, 2000) : (body ?? ""), System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
                }
                else
                {
                    req = new HttpRequestMessage(HttpMethod.Get, url);
                }
                req.Headers.TryAddWithoutValidation("User-Agent", uaStr);
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                req.Headers.TryAddWithoutValidation("Referer", new Uri(url).GetLeftPart(UriPartial.Authority) + "/");
                var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!resp.IsSuccessStatusCode)
                    return Content("http:" + (int)resp.StatusCode, "text/plain");
                html = await resp.Content.ReadAsStringAsync();
            }
        }
        catch (System.Exception ex) { return Content("ERR " + ex.Message, "text/plain"); }
        if (string.IsNullOrEmpty(html))
            return Content("empty", "text/plain");
        if (html.Length > 2000000)
            html = html.Substring(0, 2000000) + "\n[truncated]";
        if (string.IsNullOrEmpty(re))
            return Content(html, "text/html");
        try
        {
            var rx = new Regex(re, RegexOptions.Singleline);
            var o = new System.Text.StringBuilder();
            int c = 0;
            foreach (Match m in rx.Matches(html))
            {
                string line = m.Value;
                if (line.Length > 500) line = line.Substring(0, 500);
                o.AppendLine("### " + line.Trim());
                if (++c >= n) break;
            }
            return Content("matches=" + c + "\n" + o.ToString(), "text/plain");
        }
        catch (System.Exception ex) { return Content("reERR " + ex.Message, "text/plain"); }
    }

    async Task<ActionResult> ProbeMedia(string url)
    {
        try
        {
            var o = new System.Text.StringBuilder();
            string cur = url;
            using (var handler = new HttpClientHandler()
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
            })
            using (var cli = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                for (int hop = 0; hop < 5; hop++)
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, cur);
                    req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    req.Headers.TryAddWithoutValidation("Referer", new Uri(url).GetLeftPart(UriPartial.Authority) + "/");
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 524287);
                    using (var resp = await cli.SendAsync(req, HttpCompletionOption.ResponseHeadersRead))
                    {
                        int sc = (int)resp.StatusCode;
                        o.Append("hop" + hop + "=" + sc + " ");
                        if (sc >= 300 && sc < 400 && resp.Headers.Location != null)
                        {
                            var loc = resp.Headers.Location;
                            cur = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(cur), loc).ToString();
                            o.Append("->" + (cur.Length > 120 ? cur.Substring(0, 120) + "..." : cur) + " ");
                            continue;
                        }
                        o.Append("ct=" + (resp.Content?.Headers?.ContentType?.ToString() ?? "-") + " ");
                        try
                        {
                            var cr = resp.Content?.Headers?.ContentRange;
                            if (cr != null && cr.Length.HasValue)
                                o.Append("total=" + cr.Length.Value + " ");
                        }
                        catch { }
                        byte[] buf = await resp.Content.ReadAsByteArrayAsync();
                        o.Append("got=" + buf.Length + " " + ScanBoxes(buf));
                        break;
                    }
                }
            }
            // Chan RCH (exit qua IP dan cu cua app, nhu Strem cu cua Po85):
            // cho biet duong dan cu co qua duoc WAF khong
            try
            {
                if (rch?.enable == true)
                {
                    var rheaders = httpHeaders(init, HeadersModel.Init(
                        ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"),
                        ("Referer", new Uri(url).GetLeftPart(UriPartial.Authority) + "/")
                    ));
                    var rres = await rch.Headers(init.cors(url, rheaders, requestInfo), null, rheaders);
                    o.Append(" rch=" + (rres.currentUrl ?? "-"));
                }
                else
                {
                    o.Append(" rch=off");
                }
            }
            catch (System.Exception rex) { o.Append(" rchERR:" + rex.Message); }
            return Content(o.ToString(), "text/plain");
        }
        catch (System.Exception ex) { return Content("probeERR " + ex.Message, "text/plain"); }
    }

    static string ScanBoxes(byte[] b)
    {
        var o = new System.Text.StringBuilder();
        try
        {
            int n = b.Length, pos = 0;
            while (pos + 8 <= n)
            {
                long size = ((long)b[pos] << 24) | ((long)b[pos + 1] << 16) | ((long)b[pos + 2] << 8) | b[pos + 3];
                string type = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
                long hlen = 8;
                if (size == 1)
                {
                    if (pos + 16 > n) break;
                    size = 0;
                    for (int i = 0; i < 8; i++) size = (size << 8) | b[pos + 8 + i];
                    hlen = 16;
                }
                if (size < hlen || size > n + 100L * 1024 * 1024) { o.Append("[badsize:" + type + "]"); break; }
                if (type == "ftyp")
                {
                    string major = pos + 12 <= n ? System.Text.Encoding.ASCII.GetString(b, pos + 8, 4) : "?";
                    o.Append("[ftyp:" + major + "]");
                }
                else if (type == "moov")
                {
                    bool full = pos + size <= n;
                    o.Append("[moov@" + pos + (full ? ":full]" : ":cut-beyond-buffer]"));
                    if (full)
                        o.Append(ScanCodecs(b, pos + (int)hlen, (int)(pos + size)));
                    break;
                }
                else if (type == "mdat") { o.Append("[mdat@" + pos + "]"); }
                else { o.Append("[" + type + "@" + pos + "]"); }
                if (pos + size > n || size == 0) break;
                pos += (int)size;
            }
        }
        catch (System.Exception ex) { o.Append("scanerr:" + ex.Message); }
        return o.ToString();
    }

    static string ScanCodecs(byte[] b, int from, int to)
    {
        try
        {
            string[] tags = new string[] { "avc1", "hvc1", "hev1", "av01", "mp4a", "ac-3", "ec-3", "avcC", "hvcC" };
            string s = System.Text.Encoding.ASCII.GetString(b, from, to - from);
            var o = new System.Text.StringBuilder();
            foreach (var t in tags)
            {
                int k = s.IndexOf(t);
                if (k >= 0)
                    o.Append(t + "@" + (from + k) + " ");
            }
            return o.Length == 0 ? "nocodec" : o.ToString().Trim();
        }
        catch (System.Exception ex) { return "codecerr:" + ex.Message; }
    }
}
