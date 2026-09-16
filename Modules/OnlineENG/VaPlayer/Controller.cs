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
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VaPlayer;

public class VaPlayerController : BaseENGController
{
    const string ApiBase = "https://streamdata.vaplayer.ru";
    const string PlayerReferer = "https://nextgencloudfabric.com/";

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers);

    public VaPlayerController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vaplayer")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vaplayer/video")]
    [Route("lite/vaplayer/video.m3u8")]
    public async Task<ActionResult> Video(long id, string imdb_id, short s = -1, short e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (string.IsNullOrWhiteSpace(imdb_id))
            return OnError();

        var resolved = await Resolve(imdb_id, s, e);
        if (resolved == null || resolved.Count == 0)
            return OnError("stream", 502);

        var qualities = new StreamQualityTpl(resolved.Count);
        foreach (var item in resolved)
            qualities.Append(HostStreamProxy(item.Url, headers: item.Headers), item.Label);

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

    async Task<List<ResolvedStream>> Resolve(string imdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string memKey = $"vaplayer:{mediaType}:{imdbId}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached) && cached != null && cached.Count > 0)
            return cached;

        var apiHeaders = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "application/json, text/plain, */*"),
            ("Referer", PlayerReferer)
        );

        try
        {
            string url = mediaType == "movie"
                ? $"{ApiBase}/api.php?imdb={Uri.EscapeDataString(imdbId)}&type=movie"
                : $"{ApiBase}/api.php?imdb={Uri.EscapeDataString(imdbId)}&type=tv&season={season}&episode={Math.Max(episode, (short)1)}";

            var json = await httpHydra.Get<JObject>(url, addheaders: apiHeaders, statusCodeOK: false);
            var data = json?["data"] as JObject;
            var urls = data?["urls"] as JArray ?? data?["stream_urls"] as JArray;
            if (urls == null || urls.Count == 0)
            {
                Console.WriteLine($"VaPlayer: no stream_urls ({mediaType}:{imdbId})");
                return null;
            }

            string fileName = data.Value<string>("file_name") ?? string.Empty;
            string tag = Regex.Match(fileName, @"\[([^\]]+)\]").Groups[1].Value;
            if (string.IsNullOrWhiteSpace(tag))
                tag = Regex.Match(fileName, @"(2160p|1080p|720p|480p)", RegexOptions.IgnoreCase).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(tag))
                tag = mediaType;

            var resolved = new List<ResolvedStream>(urls.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;

            foreach (var token in urls)
            {
                string link = token?.Value<string>();
                if (string.IsNullOrWhiteSpace(link) ||
                    !Uri.TryCreate(link, UriKind.Absolute, out _) ||
                    !seen.Add(link))
                    continue;

                index++;
                string label = $"VaPlayer • {tag}";
                if (index > 1)
                    label += $" #{index}";

                var streamHeaders = HeadersModel.Init(
                    ("User-Agent", Http.UserAgent),
                    ("Referer", PlayerReferer)
                );

                resolved.Add(new ResolvedStream(link, label, streamHeaders));
            }

            if (resolved.Count == 0)
            {
                Console.WriteLine($"VaPlayer: urls unusable ({mediaType}:{imdbId})");
                proxyManager?.Refresh();
                return null;
            }

            hybridCache.Set(memKey, resolved, cacheTime(15));
            proxyManager?.Success();
            Console.WriteLine($"VaPlayer: {resolved.Count} streams resolved ({mediaType}:{imdbId}) {fileName}");
            return resolved;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "VaPlayer resolver failed for {MediaType}:{ImdbId}", mediaType, imdbId);
            return default;
        }
    }
}
