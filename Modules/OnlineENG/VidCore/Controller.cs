using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VidCore;

/// <summary>
/// VidCore — nguồn 4K, resolver thuần HTTP (không Playwright).
///
/// Luồng (theo CSX/CineStream `invokeVidcore`, đã xác minh route còn sống 2026-08-31):
///   1. GET  {host}/movie/{tmdb}  |  {host}/tv/{tmdb}/{s}/{e}
///      → trong HTML có chuỗi mã hoá:  \"en\":\"...\"  (fallback: \"token\":\"...\")
///   2. GET  {apihost}/enc-vidcore?text=&lt;encrypted&gt;
///      → {result: {servers, stream, token}}
///   3. POST {result.servers}                     (kèm X-CSRF-Token)  → payload mã hoá
///      POST {apihost}/dec-vidcore {text:...}     → result = [{name, data}, ...]
///   4. mỗi server: POST {result.stream}/{data}   → payload mã hoá
///      POST {apihost}/dec-vidcore                → {result: {url, tracks[]}}
///
/// apihost mặc định https://enc-dec.app/api — đổi được trong init.conf nếu tự host.
/// </summary>
public class VidCoreController : BaseENGController
{
    const string DecryptRouteEnc = "/enc-vidcore";
    const string DecryptRouteDec = "/dec-vidcore";

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers);

    public VidCoreController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidcore")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidcore/video")]
    [Route("lite/vidcore/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
        StatiCacheDisabled = true;
        SetHeadersNoCache();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (id <= 0)
            return OnError();

        List<ResolvedStream> resolved = await Resolve(id, s, e);
        if (resolved == null || resolved.Count == 0)
            return OnError("stream", 502);

        var qualities = new StreamQualityTpl(resolved.Count);
        foreach (ResolvedStream item in resolved)
            qualities.Append(HostStreamProxy(item.Url, headers: item.Headers), item.Label);

        if (qualities.IsEmpty)
            return OnError("stream", 502);

        var first = qualities.Firts();
        Console.WriteLine($"VidCore: play {first.link}");

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

    #region resolve
    async Task<List<ResolvedStream>> Resolve(long tmdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string memKey = $"vidcore:{mediaType}:{tmdbId}:{season}:{episode}";

        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached) && cached?.Count > 0)
            return cached;

        List<ResolvedStream> resolved = await ResolveHttp(tmdbId, season, episode, mediaType);

        if (resolved == null || resolved.Count == 0)
        {
            proxyManager?.Refresh();
            return null;
        }

        proxyManager?.Success();
        hybridCache.Set(memKey, resolved, cacheTime(15));
        return resolved;
    }

    async Task<List<ResolvedStream>> ResolveHttp(long tmdbId, short season, short episode, string mediaType)
    {
        string host = init.host.TrimEnd('/');
        string api = (string.IsNullOrWhiteSpace(init.apihost) ? "https://enc-dec.app/api" : init.apihost).TrimEnd('/');
        var headers = PageHeaders(null);

        string watchUrl = season > 0
            ? $"{host}/tv/{tmdbId}/{season}/{Math.Max(episode, (short)1)}"
            : $"{host}/movie/{tmdbId}";

        string html = await httpHydra.Get(watchUrl, addheaders: headers, statusCodeOK: false);
        if (string.IsNullOrWhiteSpace(html))
        {
            Console.WriteLine($"VidCore: watch page empty ({mediaType}:{tmdbId})");
            return null;
        }

        string encrypted = ExtractEncrypted(html);
        if (string.IsNullOrWhiteSpace(encrypted))
        {
            Console.WriteLine($"VidCore: token not found in {watchUrl}");
            return null;
        }

        var enc = await httpHydra.Get<JObject>(
            $"{api}{DecryptRouteEnc}?text={Uri.EscapeDataString(encrypted)}",
            addheaders: headers,
            statusCodeOK: false);

        JToken encResult = enc?["result"] ?? enc;
        string serversUrl = encResult?.Value<string>("servers");
        string streamBase = encResult?.Value<string>("stream");
        string csrf = encResult?.Value<string>("token");

        if (string.IsNullOrWhiteSpace(serversUrl) || string.IsNullOrWhiteSpace(streamBase))
        {
            Console.WriteLine($"VidCore: enc-vidcore incomplete ({mediaType}:{tmdbId})");
            return null;
        }

        var pageHeaders = PageHeaders(csrf);

        // Danh sách server bị mã hoá: POST lấy ciphertext rồi đưa sang dec-vidcore.
        string serversCipher = await PostCipher(serversUrl, pageHeaders);
        List<JToken> servers = await DecryptList(serversCipher, api, headers);
        if (servers.Count == 0)
        {
            Console.WriteLine($"VidCore: no servers ({mediaType}:{tmdbId})");
            return null;
        }

        // Mỗi server một luồng — fan-out song song, con chết không kéo cả dãy.
        async Task<ResolvedStream> FetchServer(JToken server)
        {
            string data = server.Value<string>("data");
            string name = server.Value<string>("name");
            if (string.IsNullOrWhiteSpace(data))
                return null;

            string streamCipher = await PostCipher($"{streamBase}/{data}", pageHeaders);
            if (string.IsNullOrWhiteSpace(streamCipher))
                return null;

            var dec = await httpHydra.Post<JObject>(
                $"{api}{DecryptRouteDec}",
                JsonConvert.SerializeObject(new { text = streamCipher }),
                addheaders: headers,
                statusCodeOK: false);

            JToken result = dec?["result"] ?? dec;
            string m3u8 = result?.Value<string>("url") ?? result?.Value<string>("stream_url");
            if (string.IsNullOrWhiteSpace(m3u8) || !IsHttpUrl(m3u8))
                return null;

            return new ResolvedStream(m3u8, $"VidCore · {(string.IsNullOrWhiteSpace(name) ? "server" : name)}", pageHeaders);
        }

        var answered = await Task.WhenAll(servers.Select(server => FetchServer(server)));

        var resolved = new List<ResolvedStream>(servers.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ResolvedStream item in answered)
        {
            if (item == null || !seen.Add(item.Url))
                continue;
            resolved.Add(item);
        }

        Console.WriteLine($"VidCore: {resolved.Count}/{servers.Count} streams ({mediaType}:{tmdbId})");
        return resolved;
    }
    #endregion

    #region helpers
    List<HeadersModel> PageHeaders(string csrf)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", $"{init.host.TrimEnd('/')}/"),
            ("Origin", init.host.TrimEnd('/')),
            ("X-Requested-With", "XMLHttpRequest"),
            ("Accept", "application/json, text/plain, */*"),
            ("Content-Type", "application/json")
        );

        if (!string.IsNullOrWhiteSpace(csrf))
            headers.Add(new HeadersModel("X-CSRF-Token", csrf));

        return headers;
    }

    /// <summary>
    /// CSX gọi các endpoint này bằng `app.post(url, headers=…)` — tức POST với body rỗng.
    /// Một số build của VidCore lại đòi JSON object, nên thử rỗng trước rồi thử `{}`.
    /// </summary>
    async Task<string> PostCipher(string url, List<HeadersModel> headers)
    {
        string body = await httpHydra.Post(url, "", addheaders: headers, statusCodeOK: false);
        if (!string.IsNullOrWhiteSpace(body))
            return body;

        return await httpHydra.Post(url, "{}", addheaders: headers, statusCodeOK: false);
    }

    /// <summary>
    /// VidCore nhét token vào trong một chuỗi JS đã escape, nên phải chấp nhận
    /// cả bản có backslash: \"en\":\"...\" lẫn bản thường "en":"...".
    /// </summary>
    static string ExtractEncrypted(string html)
    {
        foreach (string pattern in new[]
        {
            "\\\\\"(?:en|token)\\\\\":\\s*\\\\\"([^\"\\\\]+)",
            "\"(?:en|token)\":\\s*\"([^\"]+)\""
        })
        {
            var m = Regex.Match(html, pattern);
            if (m.Success && m.Groups.Count > 1 && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                return m.Groups[1].Value;
        }

        return null;
    }

    async Task<List<JToken>> DecryptList(string cipher, string api, List<HeadersModel> headers)
    {
        var list = new List<JToken>();
        if (string.IsNullOrWhiteSpace(cipher))
            return list;

        var dec = await httpHydra.Post<JObject>(
            $"{api}{DecryptRouteDec}",
            JsonConvert.SerializeObject(new { text = cipher }),
            addheaders: headers,
            statusCodeOK: false);

        JToken result = dec?["result"] ?? dec;
        if (result is JArray arr)
        {
            list.AddRange(arr.Children());
        }
        else if (result is JObject obj)
        {
            if (obj["servers"] is JArray inner)
                list.AddRange(inner.Children());
            else
                list.Add(obj);
        }

        return list;
    }

    static bool IsHttpUrl(string uri) =>
        !string.IsNullOrWhiteSpace(uri) && Uri.TryCreate(uri, UriKind.Absolute, out Uri u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
    #endregion
}
