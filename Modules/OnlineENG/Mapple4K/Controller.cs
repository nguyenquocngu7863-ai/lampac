using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Mapple4K;

public sealed class Mapple4KController : BaseENGController
{
    static readonly (string Id, string Label)[] Sources =
    [
        ("mapple", "Mapple"),
        ("s1", "Nexus"),
        ("s2", "Cipher"),
        ("s3", "Pulse"),
        ("s4", "Vertex"),
        ("s10", "Chimp")
    ];

    // Backup domains advertised by mapple.uk itself (footer "Bookmark our
    // backup sites", checked 2026-08-28). mapple.vip is gone from that list, so
    // it is no longer probed.
    static readonly string[] Mirrors =
    [
        "https://mapple.uk", "https://mapple.tv", "https://mapple.rip", "https://mapple.bid",
        "https://mappl.tv", "https://mapplee.com", "https://mapple.cc", "https://lightflix.app"
    ];

    // /api/stream answers {"success":false,"error":"This playback endpoint has
    // been retired. Refresh the watch page."}, so the playlist is taken from the
    // watch page when it is rendered there. The class excludes "\" so a URL at
    // the end of a JSON string stops before its closing escape.
    static readonly Regex EmbeddedStreamRegex = new(
        "https?://[^\"'<>\\s\\\\]+?\\.(?:m3u8|mp4)(?:\\?[^\"'<>\\s\\\\]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    // First upstream reason seen for this request; reported instead of the
    // generic "no stream" message. Controllers are per-request instances.
    string lastFailure;

    static readonly Regex RequestTokenRegex = new(
        "window\\.__REQUEST_TOKEN__\\s*=\\s*\"([^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    static readonly Regex ClientKeyRegex = new(
        "mptv_sk_[a-zA-Z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    static readonly Regex ScriptRegex = new(
        "<script[^>]+src=[\"']([^\"']+)[\"']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    sealed record Candidate(string Url, string Label, List<HeadersModel> Headers);

    public Mapple4KController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/mapple4k")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
        => ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");

    [HttpGet, Staticache(manually: true)]
    [Route("lite/mapple4k/video")]
    [Route("lite/mapple4k/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;
        if (id <= 0)
            return OnError();

        lastFailure = null;
        List<Candidate> candidates = await ResolveAll(id, s, e);
        if (candidates.Count == 0)
        {
            return OnError(string.IsNullOrWhiteSpace(lastFailure)
                ? "Mapple không trả stream"
                : $"Mapple: {lastFailure}", 502);
        }

        var qualities = new StreamQualityTpl(candidates.Count);
        foreach (Candidate candidate in candidates)
            qualities.Append(HostStreamProxy(candidate.Url, candidate.Headers), candidate.Label);

        if (qualities.IsEmpty)
            return OnError("Mapple không chuẩn bị được stream", 502);

        var first = qualities.Firts();
        if (play)
            return RedirectToPlay(first.link);

        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            "Mapple 4K",
            streamquality: qualities,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        ));
    }

    async Task<List<Candidate>> ResolveAll(long tmdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string memKey = $"mapple4k:playback-protocol-v4:{mediaType}:{tmdbId}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out List<Candidate> cached))
            return cached;

        foreach (string configuredBase in PreferredMirrors())
        {
            string baseUrl = configuredBase.TrimEnd('/');
            try
            {
                List<Candidate> result = await ResolveMirror(baseUrl, tmdbId, mediaType, season, episode);
                if (result.Count == 0)
                    continue;

                hybridCache.Set(memKey, result, cacheTime(15));
                proxyManager?.Success();
                Console.WriteLine($"Mapple4K: {result.Count} streams ({mediaType}:{tmdbId}) [{string.Join(", ", result.Select(i => i.Label))}]");
                return result;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Mapple mirror failed: {Mirror}", baseUrl);
            }
        }

        proxyManager?.Refresh();
        Console.WriteLine($"Mapple4K: no stream ({mediaType}:{tmdbId})");
        return [];
    }

    IEnumerable<string> PreferredMirrors()
    {
        string configured = string.IsNullOrWhiteSpace(init.host) ? "https://mapple.uk" : init.host.TrimEnd('/');
        yield return configured;
        foreach (string mirror in Mirrors)
        {
            if (!mirror.Equals(configured, StringComparison.OrdinalIgnoreCase))
                yield return mirror;
        }
    }

    async Task<List<Candidate>> ResolveMirror(string baseUrl, long tmdbId, string mediaType, short season, short episode)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", baseUrl + "/"),
            ("Origin", baseUrl),
            ("Accept", "*/*")
        );

        // The player docs (mappletv.uk/docs/getting-started/endpoints) use one
        // dash-joined segment for TV: /watch/tv/{id}-{season}-{episode}. The
        // older slash form is kept as a fallback for mirrors still routing it.
        string slug = $"{season}-{episode}";
        string[] pageUrls = mediaType == "tv"
            ? [$"{baseUrl}/watch/tv/{tmdbId}-{slug}", $"{baseUrl}/watch/tv/{tmdbId}/{slug}"]
            : [$"{baseUrl}/watch/movie/{tmdbId}"];

        foreach (string pageUrl in pageUrls)
        {
            string html = await httpHydra.Get(pageUrl, addheaders: headers, statusCodeOK: false);
            if (string.IsNullOrWhiteSpace(html))
            {
                lastFailure ??= $"{new Uri(pageUrl).Host}: trang watch trống";
                continue;
            }

            // /api/stream is retired, so the watch page is scanned first: when
            // Mapple renders the playlist server-side this is the only step
            // that yields anything, and it needs no Chromium.
            List<Candidate> embedded = EmbeddedCandidates(html, headers);
            if (embedded.Count > 0)
                return embedded;

            List<Candidate> viaApi = await ResolveViaApi(baseUrl, html, headers, tmdbId, mediaType, slug);
            if (viaApi.Count > 0)
                return viaApi;
        }

        return [];
    }

    static List<Candidate> EmbeddedCandidates(string html, List<HeadersModel> headers)
    {
        var result = new List<Candidate>();

        // Next.js RSC payloads escape slashes ("https:\/\/host\/x.m3u8"), and a
        // payload nested one level deeper escapes them twice, so unescape until
        // no "\/" is left.
        string normalized = html;
        while (normalized.Contains("\\/", StringComparison.Ordinal))
            normalized = normalized.Replace("\\/", "/");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in EmbeddedStreamRegex.Matches(normalized).Cast<Match>().Take(12))
        {
            string media = match.Value.TrimEnd(')', ']', '}', ',', ';');
            if (!Uri.TryCreate(media, UriKind.Absolute, out _))
                continue;
            if (media.Contains("nocach", StringComparison.OrdinalIgnoreCase))
                continue;
            if (media.Contains("omena-puu", StringComparison.OrdinalIgnoreCase))
                media += media.Contains('?') ? "&format=.m3u8" : "?format=.m3u8";
            if (!seen.Add(media))
                continue;

            result.Add(new Candidate(media, $"{result.Count + 1:00}. Mapple [Web]", headers));
        }

        return result;
    }

    async Task<List<Candidate>> ResolveViaApi(string baseUrl, string html, List<HeadersModel> headers, long tmdbId, string mediaType, string tvSlug)
    {
        string host = new Uri(baseUrl).Host;

        string requestToken = RequestTokenRegex.Match(html).Groups[1].Value;
        if (string.IsNullOrWhiteSpace(requestToken))
        {
            lastFailure ??= $"{host}: thiếu request token";
            Console.WriteLine($"Mapple4K: {host} request token missing");
            return [];
        }

        string clientKey = await FindClientKey(baseUrl, html, headers);
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            lastFailure ??= $"{host}: thiếu client key";
            Console.WriteLine($"Mapple4K: {host} client key missing");
            return [];
        }

        JObject initRequest = new()
        {
            ["mediaId"] = tmdbId,
            ["mediaType"] = mediaType,
            ["requestToken"] = requestToken
        };
        JObject initialization = await PostJson($"{baseUrl}/api/playback-init", initRequest, headers);
        if (initialization == null)
        {
            lastFailure ??= $"{host}: playback-init thất bại";
            Console.WriteLine($"Mapple4K: {host} playback init failed");
            return [];
        }

        if (initialization.Value<bool?>("requiresPow") == true)
        {
            JObject pow = initialization["pow"] as JObject;
            string challenge = pow?.Value<string>("challenge");
            string challengeId = pow?.Value<string>("challengeId");
            int difficulty = pow?.Value<int?>("difficulty") ?? 0;
            string nonce = SolvePow(challenge, difficulty);
            if (string.IsNullOrWhiteSpace(challengeId) || nonce == null)
            {
                lastFailure ??= $"{host}: proof-of-work thất bại";
                Console.WriteLine($"Mapple4K: {host} proof-of-work failed");
                return [];
            }

            initRequest["pow"] = new JObject
            {
                ["challengeId"] = challengeId,
                ["nonce"] = nonce
            };
            initialization = await PostJson($"{baseUrl}/api/playback-init", initRequest, headers);
        }

        string playbackToken = initialization?.Value<string>("token");
        if (string.IsNullOrWhiteSpace(playbackToken))
        {
            lastFailure ??= $"{host}: thiếu playback token";
            Console.WriteLine($"Mapple4K: {host} playback token missing");
            return [];
        }

        var result = new List<Candidate>(Sources.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Sources)
        {
            string streamUrl = $"{baseUrl}/api/stream" +
                $"?mediaId={tmdbId}" +
                $"&mediaType={HttpUtility.UrlEncode(mediaType)}" +
                $"&tv_slug={HttpUtility.UrlEncode(tvSlug)}" +
                $"&source={HttpUtility.UrlEncode(source.Id)}" +
                $"&apikey={HttpUtility.UrlEncode(clientKey)}" +
                $"&requestToken={HttpUtility.UrlEncode(requestToken)}" +
                $"&token={HttpUtility.UrlEncode(playbackToken)}";

            JObject response = await GetJsonNoLog(streamUrl, headers);
            if (response?.Value<bool?>("success") != true)
            {
                // Today this is {"success":false,"error":"This playback endpoint
                // has been retired. Refresh the watch page."} — keep the server's
                // own wording so the log says why nothing came back.
                lastFailure ??= response?.Value<string>("error") ?? $"{host}: /api/stream không trả success";
                continue;
            }

            string media = response["data"]?.Value<string>("stream_url") ?? response.Value<string>("stream_url");
            if (string.IsNullOrWhiteSpace(media) || !Uri.TryCreate(media, UriKind.Absolute, out _) || !seen.Add(media))
                continue;

            if (media.Contains("omena-puu", StringComparison.OrdinalIgnoreCase))
                media += media.Contains('?') ? "&format=.m3u8" : "?format=.m3u8";
            if (media.Contains("nocach", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new Candidate(media, $"{result.Count + 1:00}. Mapple [{source.Label}]", headers));
        }
        return result;
    }

    async Task<string> FindClientKey(string baseUrl, string html, List<HeadersModel> headers)
    {
        string cacheKey = $"mapple4k:client-key:{baseUrl}";
        if (hybridCache.TryGetValue(cacheKey, out string cachedKey) && !string.IsNullOrWhiteSpace(cachedKey))
            return cachedKey;

        Match direct = ClientKeyRegex.Match(html);
        if (direct.Success)
        {
            hybridCache.Set(cacheKey, direct.Value, TimeSpan.FromHours(6), inmemory: true);
            return direct.Value;
        }

        foreach (Match script in ScriptRegex.Matches(html).Cast<Match>().Take(20))
        {
            string src = script.Groups[1].Value;
            if (!Uri.TryCreate(src, UriKind.Absolute, out Uri scriptUri))
                scriptUri = new Uri(new Uri(baseUrl + "/"), src);

            string javascript = await httpHydra.Get(scriptUri.AbsoluteUri, addheaders: headers, statusCodeOK: false);
            if (string.IsNullOrEmpty(javascript))
                continue;

            Match key = ClientKeyRegex.Match(javascript);
            if (key.Success)
            {
                hybridCache.Set(cacheKey, key.Value, TimeSpan.FromHours(6), inmemory: true);
                return key.Value;
            }
        }
        return null;
    }

    async Task<JObject> GetJsonNoLog(string url, List<HeadersModel> headers)
    {
        try
        {
            using var client = new HttpClient(Http.Handler(url, proxy))
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (HeadersModel header in headers)
                request.Headers.TryAddWithoutValidation(header.name, header.val);

            using HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            string body = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body) ? null : JObject.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    async Task<JObject> PostJson(string url, JObject payload, List<HeadersModel> headers)
    {
        using var content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
        return await Http.Post<JObject>(
            url,
            content,
            timeoutSeconds: 25,
            headers: headers,
            proxy: proxy,
            statusCodeOK: false
        );
    }

    static string SolvePow(string challenge, int difficulty)
    {
        if (string.IsNullOrWhiteSpace(challenge) || difficulty < 0 || difficulty > 30)
            return null;

        int fullBytes = difficulty / 8;
        int remainingBits = difficulty % 8;
        int mask = remainingBits == 0 ? 0 : (0xff << (8 - remainingBits)) & 0xff;
        byte[] challengeBytes = Encoding.UTF8.GetBytes(challenge);

        using SHA256 sha = SHA256.Create();
        for (int nonce = 0; nonce < 10_000_000; nonce++)
        {
            byte[] nonceBytes = Encoding.UTF8.GetBytes(nonce.ToString());
            byte[] input = new byte[challengeBytes.Length + nonceBytes.Length];
            Buffer.BlockCopy(challengeBytes, 0, input, 0, challengeBytes.Length);
            Buffer.BlockCopy(nonceBytes, 0, input, challengeBytes.Length, nonceBytes.Length);
            byte[] hash = sha.ComputeHash(input);

            bool valid = true;
            for (int i = 0; i < fullBytes; i++)
            {
                if (hash[i] != 0)
                {
                    valid = false;
                    break;
                }
            }
            if (valid && (mask == 0 || (hash[fullBytes] & mask) == 0))
                return nonce.ToString();
        }
        return null;
    }
}
