using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DevFetch;

// Recon tam qua IP may (xoa truoc khi push).
// GET /devfetch?url=<http|https> -> raw HTML
// GET /devfetch?url=...&re=<regex>&n=50 -> cac doan match
public class DevFetchController : BaseSisiController
{
    static readonly HttpClient httpClient =
        FriendlyHttp.CreateHttpClient();

    const string ChromeUA = "Mozilla/5.0 (Linux; "
        + "Android 13) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/126.0.0.0 Mobile Safari/537.36";

    static readonly int[] BlockedPorts = new int[]
        { 9118, 9117, 3002, 8191, 9196, 9197, 20128 };

    public DevFetchController() : base(ModInit.conf) { }

    static bool BadTarget(Uri u)
    {
        if (u.Scheme != "http" && u.Scheme != "https")
            return true;
        string h = u.Host.ToLowerInvariant();
        if (h == "localhost" || h.EndsWith(".local")
            || h.EndsWith(".internal"))
            return true;
        if (h == "127.0.0.1" || h == "::1"
            || h.StartsWith("10.") || h.StartsWith("192.168.")
            || h.StartsWith("172."))
            return true;
        if (!u.IsDefaultPort && BlockedPorts.Contains(u.Port))
            return true;
        return false;
    }

    [HttpGet]
    [Route("devfetch")]
    async public Task<ActionResult> Fetch(
        string url, string re = null, int n = 50)
    {
        if (string.IsNullOrEmpty(url))
            return Content("missing url", "text/plain");

        Uri u;
        try
        {
            u = new Uri(url);
        }
        catch
        {
            return Content("bad url", "text/plain");
        }

        if (BadTarget(u))
            return Content("blocked", "text/plain");

        string html;
        try
        {
            using (var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(30)))
            using (var req = new HttpRequestMessage(
                HttpMethod.Get, u))
            {
                req.Headers.TryAddWithoutValidation(
                    "User-Agent", ChromeUA);
                req.Headers.TryAddWithoutValidation(
                    "Accept",
                    "text/html,application/xhtml+xml");
                req.Headers.TryAddWithoutValidation(
                    "Accept-Language", "en-US,en;q=0.9");
                using (var resp = await httpClient.SendAsync(
                    req, HttpCompletionOption.ResponseHeadersRead,
                    cts.Token))
                {
                    if (!resp.IsSuccessStatusCode)
                        return Content(
                            $"http {(int)resp.StatusCode}",
                            "text/plain");
                    var bytes = await resp.Content
                        .ReadAsByteArrayAsync(cts.Token);
                    if (bytes.Length > 2 * 1024 * 1024)
                        bytes = bytes.Take(2 * 1024 * 1024)
                            .ToArray();
                    html = System.Text.Encoding.UTF8
                        .GetString(bytes);
                }
            }
        }
        catch (Exception ex)
        {
            return Content("fetch error: "
                + ex.Message, "text/plain");
        }

        if (string.IsNullOrEmpty(re))
            return Content(html, "text/html");

        var outs = Regex.Matches(html, re).Cast<Match>()
            .Take(Math.Max(1, Math.Min(n, 200)))
            .Select(m =>
            {
                int s = Math.Max(0, m.Index - 250);
                int l = Math.Min(500, html.Length - s);
                return html.Substring(s, l);
            });
        return Content(string.Join("\n====\n", outs),
            "text/plain");
    }
}
