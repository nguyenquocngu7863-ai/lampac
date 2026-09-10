using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VixSrc;

public class VixSrcController : BaseENGController
{
    const string ApiBase = "https://vixsrc.to";

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers);

    public VixSrcController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vixsrc")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vixsrc/video")]
    [Route("lite/vixsrc/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (id <= 0)
            return OnError();

        var resolved = await Resolve(id, s, e);
        if (resolved == null || resolved.Count == 0)
            return OnError("stream", 502);

        var qualities = new StreamQualityTpl(resolved.Count);
        foreach (var item in resolved)
            qualities.Append(HlsUrl(HostStreamProxy(item.Url, headers: item.Headers)), item.Label);

        if (qualities.IsEmpty)
            return OnError("stream", 502);

        var first = qualities.Firts();
        if (play)
            return RedirectToPlay(first.link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            "English",
            streamquality: qualities,
            vast: init.vast,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        ));
    }

    // Player trong cua app chi dung hls.js khi URL chua ".m3u8"
    // (check regex tren URL, khong theo Content-Type). Link /proxy/ khong co
    // duoi nen roi vao native va bao "no supported source" — them query gia,
    // proxy bo qua query nen manifest ve nhu cu.
    static string HlsUrl(string url)
        => string.IsNullOrEmpty(url) ? url : url.Contains("?") ? url + "&.m3u8" : url + "?.m3u8";

    async Task<List<ResolvedStream>> Resolve(long tmdbId, short season, short episode)
    {
        bool isTv = season > 0;
        string query = isTv
            ? $"{tmdbId}/{season}/{Math.Max(episode, (short)1)}"
            : tmdbId.ToString();

        string memKey = $"vixsrc:{(isTv ? "tv" : "movie")}:{query}";
        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached) && cached != null && cached.Count > 0)
            return cached;

        var apiHeaders = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "application/json, text/javascript, */*"),
            ("Accept-Language", "en-US,en;q=0.9"),
            ("Referer", ApiBase + "/"),
            ("Origin", ApiBase)
        );

        try
        {
            // 1. API tra link trang embed: /embed/{id}?token=...&expires=...
            string apiUrl = isTv
                ? $"{ApiBase}/api/tv/{query}"
                : $"{ApiBase}/api/movie/{query}";

            var apiJson = await httpHydra.Get<JObject>(apiUrl, addheaders: apiHeaders, statusCodeOK: false);
            string src = apiJson?.Value<string>("src");
            if (string.IsNullOrWhiteSpace(src))
            {
                Console.WriteLine($"VixSrc: empty api ({query}) {apiUrl}");
                return null;
            }

            string embedUrl = src.StartsWith("http") ? src : ApiBase + src;

            // 2. Trang embed chua window.streams + window.masterPlaylist (token/expires)
            string html = await httpHydra.Get(embedUrl, addheaders: HeadersModel.Init(
                ("User-Agent", Http.UserAgent),
                ("Referer", ApiBase + "/")
            ), statusCodeOK: false);

            if (string.IsNullOrWhiteSpace(html) || !html.Contains("window.streams"))
            {
                Console.WriteLine($"VixSrc: empty embed ({query})");
                return null;
            }

            string token = Regex.Match(html, @"'token':\s*'([^']+)'").Groups[1].Value;
            string expires = Regex.Match(html, @"'expires':\s*'([^']+)'").Groups[1].Value;
            string asn = Regex.Match(html, @"'asn':\s*'([^']*)'").Groups[1].Value;
            bool fhd = html.Contains("window.canPlayFHD = true");

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(expires))
            {
                Console.WriteLine($"VixSrc: no token ({query})");
                return null;
            }

            // 3. Dung playlist tu stream url + params (them h=1 de mo FHD/1080p)
            var names = Regex.Matches(html, @"\""name\"":\""([^\""]+)\""");
            var urls = Regex.Matches(html, @"\""url\"":\""([^\""]+)\""");

            var resolved = new List<ResolvedStream>(4);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Math.Min(names.Count, urls.Count); i++)
            {
                string pl = urls[i].Groups[1].Value.Replace("\\/", "/");
                if (string.IsNullOrWhiteSpace(pl))
                    continue;

                string sep = pl.Contains("?") ? "&" : "?";
                pl = $"{pl}{sep}token={Uri.EscapeDataString(token)}&expires={Uri.EscapeDataString(expires)}";
                if (!string.IsNullOrEmpty(asn))
                    pl += $"&asn={Uri.EscapeDataString(asn)}";
                if (fhd)
                    pl += "&h=1";

                if (!pl.StartsWith("http"))
                    pl = ApiBase + pl;

                if (!seen.Add(pl))
                    continue;

                string label = names[i].Groups[1].Value;
                if (fhd)
                    label += " FHD";

                var headers = HeadersModel.Init(
                    ("User-Agent", Http.UserAgent),
                    ("Referer", embedUrl),
                    ("Origin", ApiBase)
                );

                resolved.Add(new ResolvedStream(pl, label, headers));
            }

            if (resolved.Count == 0)
            {
                Console.WriteLine($"VixSrc: no streams ({query})");
                proxyManager?.Refresh();
                return null;
            }

            hybridCache.Set(memKey, resolved, cacheTime(30));
            proxyManager?.Success();
            Console.WriteLine($"VixSrc: {resolved.Count} streams ({query})");
            return resolved;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "VixSrc resolve failed {Query}", query);
            return null;
        }
    }
}
