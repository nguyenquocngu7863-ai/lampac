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
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Mapple4K;

public sealed class Mapple4KController : BaseENGController
{
    const string SessionEndpoint = "https://enc-dec.app/api/enc-mapple";

    static readonly (string Id, string Label)[] Sources =
    [
        ("europa", "Europa · 4K · Multi-audio"),
        ("ganymede", "Ganymede · 4K · Multi-audio"),
        ("callisto", "Callisto · 4K"),
        ("io", "Io · 4K"),
        // Older action aliases remain useful fallback mirrors.
        ("mapple", "Mapple"),
        ("sakura", "Sakura"),
        ("alfa", "Alfa"),
        ("oak", "Oak"),
        ("wiggles", "Wiggles")
    ];

    sealed record Candidate(string Url, string Label, List<HeadersModel> Headers);

    public Mapple4KController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/mapple4k")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/mapple4k/video")]
    [Route("lite/mapple4k/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;
        if (id <= 0)
            return OnError();

        List<Candidate> candidates = await ResolveAll(id, s, e);
        if (candidates.Count == 0)
            return OnError("Mapple không trả stream", 502);

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
        string memKey = $"mapple4k:action-v1:{mediaType}:{tmdbId}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out List<Candidate> cached))
            return cached;

        var result = new List<Candidate>(Sources.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in Sources)
        {
            try
            {
                JObject handshake = await httpHydra.Get<JObject>(
                    SessionEndpoint,
                    addheaders: HeadersModel.Init(
                        ("Accept", "application/json"),
                        ("Referer", init.host + "/"),
                        ("User-Agent", Http.UserAgent)
                    )
                );
                string sessionId = handshake?["result"]?.Value<string>("sessionId");
                string nextAction = handshake?["result"]?.Value<string>("nextAction");
                if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(nextAction))
                    continue;

                var payload = new JArray
                {
                    new JObject
                    {
                        ["mediaId"] = tmdbId,
                        ["mediaType"] = mediaType,
                        ["source"] = source.Id,
                        ["tv_slug"] = mediaType == "tv" ? $"{season}-{episode}" : string.Empty,
                        ["sessionId"] = sessionId
                    }
                };

                string actionUrl = $"{init.host}/watch/{mediaType}/{tmdbId}";
                var headers = HeadersModel.Init(
                    ("Accept", "text/x-component"),
                    ("Referer", init.host + "/"),
                    ("Origin", init.host),
                    ("User-Agent", Http.UserAgent),
                    ("Next-Action", nextAction)
                );
                using var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "text/plain");
                string response = await Http.Post(
                    actionUrl,
                    content,
                    timeoutSeconds: 25,
                    headers: headers,
                    proxy: proxy,
                    statusCodeOK: false
                );

                JObject action = ParseActionResponse(response);
                if (action?.Value<bool?>("success") != true)
                    continue;

                string streamUrl = action["data"]?.Value<string>("stream_url")
                    ?? action.Value<string>("stream_url");
                if (string.IsNullOrWhiteSpace(streamUrl) ||
                    !Uri.TryCreate(streamUrl, UriKind.Absolute, out _) ||
                    !seen.Add(streamUrl))
                    continue;

                var streamHeaders = HeadersModel.Init(
                    ("User-Agent", Http.UserAgent),
                    ("Referer", init.host + "/"),
                    ("Origin", init.host)
                );
                result.Add(new Candidate(streamUrl, $"{result.Count + 1:00}. {source.Label}", streamHeaders));
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Mapple source {Source} failed", source.Id);
            }
        }

        if (result.Count > 0)
        {
            hybridCache.Set(memKey, result, cacheTime(15));
            Console.WriteLine($"Mapple4K: {result.Count} streams ({mediaType}:{tmdbId}) [{string.Join(", ", result.Select(i => i.Label))}]");
            proxyManager?.Success();
        }
        else
        {
            Console.WriteLine($"Mapple4K: no stream ({mediaType}:{tmdbId})");
            proxyManager?.Refresh();
        }

        return result;
    }

    static JObject ParseActionResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        foreach (string rawLine in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            int separator = line.IndexOf(':');
            if (separator >= 0)
                line = line[(separator + 1)..].Trim();

            if (!line.StartsWith('{'))
                continue;

            try
            {
                JObject parsed = JObject.Parse(line);
                if (parsed.ContainsKey("success") || parsed["data"]?["stream_url"] != null)
                    return parsed;
            }
            catch { }
        }
        return null;
    }
}
