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
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Videasy;

public class VideasyController : BaseENGController
{
    const string ApiBase = "https://api.speedracelight.com";
    const string TmdbProxy = "https://db.speedracelight.com/3";
    const string PlayerOrigin = "https://player.videasy.to";

    static readonly string[] Providers = ["cdn", "neon2", "m4uhd", "meine", "lamovie"];

    static readonly Dictionary<string, string> ProviderLabels = new(StringComparer.Ordinal)
    {
        ["cdn"] = "Yoru",
        ["neon2"] = "Neon",
        ["m4uhd"] = "Breach",
        ["meine"] = "Killjoy",
        ["lamovie"] = "Omen"
    };

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers);

    static readonly uint[] MixTable =
    [
        1116352408, 1899447441, 3049323471, 3921009573,
        961987163, 1508970993, 2453635748, 2870763221,
        3624381080, 310598401, 607225278, 1426881987,
        1925078388, 2162078206, 2614888103, 3248222580
    ];

    public VideasyController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/videasy")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/videasy/video")]
    [Route("lite/videasy/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (id <= 0)
            return OnError();

        List<ResolvedStream> resolved = await ResolveDirect(id, s, e);
        if (resolved == null || resolved.Count == 0)
            return OnError("stream", 502);

        var qualities = new StreamQualityTpl(resolved.Count);
        foreach (ResolvedStream item in resolved)
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

    async Task<List<ResolvedStream>> ResolveDirect(long tmdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string memKey = $"videasy:direct:all:{mediaType}:{tmdbId}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached))
            return cached;

        var apiHeaders = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", PlayerOrigin + "/"),
            ("Origin", PlayerOrigin),
            ("Accept", "application/json, text/plain, */*")
        );

        try
        {
            string metadataUrl = $"{TmdbProxy}/{mediaType}/{tmdbId}?append_to_response=external_ids";
            var metadata = await httpHydra.Get<JObject>(metadataUrl, addheaders: apiHeaders);
            if (metadata == null)
            {
                Console.WriteLine($"Videasy: metadata failed ({mediaType}:{tmdbId})");
                return default;
            }

            string title = metadata.Value<string>("title")
                ?? metadata.Value<string>("name")
                ?? metadata.Value<string>("original_title")
                ?? metadata.Value<string>("original_name");
            if (string.IsNullOrWhiteSpace(title))
                return default;

            string date = metadata.Value<string>("release_date") ?? metadata.Value<string>("first_air_date") ?? string.Empty;
            string year = date.Length >= 4 ? date[..4] : string.Empty;
            string imdbId = metadata.Value<string>("imdb_id") ?? metadata["external_ids"]?.Value<string>("imdb_id") ?? string.Empty;
            int totalSeasons = metadata.Value<int?>("number_of_seasons") ?? 0;

            string seed = null;
            for (int attempt = 0; attempt < 3 && string.IsNullOrEmpty(seed); attempt++)
            {
                var seedRoot = await httpHydra.Get<JObject>($"{ApiBase}/seed?mediaId={tmdbId}", addheaders: apiHeaders);
                seed = seedRoot?.Value<string>("seed");
                if (string.IsNullOrEmpty(seed))
                    await Task.Delay(500 * (attempt + 1));
            }

            if (string.IsNullOrEmpty(seed))
            {
                Console.WriteLine($"Videasy: seed failed ({mediaType}:{tmdbId})");
                return default;
            }

            var query = new Dictionary<string, string>
            {
                // Player pre-encodes the title before URLSearchParams encodes
                // the complete query, so percent signs are intentionally
                // encoded a second time (e.g. space -> %2520).
                ["title"] = Uri.EscapeDataString(title),
                ["mediaType"] = mediaType,
                ["year"] = year,
                ["tmdbId"] = tmdbId.ToString(),
                ["imdbId"] = imdbId,
                ["enc"] = "2",
                ["seed"] = seed
            };

            if (mediaType == "tv")
            {
                query["seasonId"] = season.ToString();
                query["episodeId"] = Math.Max(episode, (short)1).ToString();
                if (totalSeasons > 0)
                    query["totalSeasons"] = totalSeasons.ToString();
            }
            else
            {
                query["seasonId"] = "1";
                query["episodeId"] = "1";
            }

            string queryString = BuildQuery(query);
            var resolved = new List<ResolvedStream>(16);
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string provider in Providers)
            {
                string encrypted = await httpHydra.Get(
                    $"{ApiBase}/{provider}/sources-with-title?{queryString}",
                    addheaders: apiHeaders,
                    statusCodeOK: false
                );

                if (string.IsNullOrWhiteSpace(encrypted))
                    continue;

                encrypted = encrypted.Trim();
                if (encrypted.StartsWith('"') && encrypted.EndsWith('"'))
                {
                    try { encrypted = JsonConvert.DeserializeObject<string>(encrypted); }
                    catch { continue; }
                }

                if (encrypted.StartsWith('{'))
                    continue;

                JObject payload;
                try
                {
                    payload = JObject.Parse(DecryptPayload(encrypted, seed, checked((int)tmdbId)));
                }
                catch
                {
                    continue;
                }

                if (payload["sources"] is not JArray sources)
                    continue;

                int providerIndex = 0;
                foreach (var source in sources)
                {
                    string url = source.Value<string>("url") ?? source.Value<string>("file");
                    if (string.IsNullOrWhiteSpace(url) ||
                        !Uri.TryCreate(url, UriKind.Absolute, out _) ||
                        !seenUrls.Add(url))
                        continue;

                    if (!url.Contains(".m3u", StringComparison.OrdinalIgnoreCase) &&
                        !url.Contains(".mp4", StringComparison.OrdinalIgnoreCase) &&
                        !url.Contains(".mpd", StringComparison.OrdinalIgnoreCase))
                        continue;

                    providerIndex++;
                    string providerLabel = ProviderLabels.TryGetValue(provider, out string knownLabel)
                        ? knownLabel
                        : provider;
                    string quality = source.Value<string>("quality");
                    if (string.IsNullOrWhiteSpace(quality))
                        quality = "Auto";

                    string label = $"{providerLabel} · {quality}";
                    if (providerIndex > 1)
                        label += $" #{providerIndex}";

                    var streamHeaders = HeadersModel.Init(
                        ("User-Agent", Http.UserAgent),
                        ("Referer", PlayerOrigin + "/"),
                        ("Origin", PlayerOrigin)
                    );

                    resolved.Add(new ResolvedStream(url, label, streamHeaders));
                }
            }

            if (resolved.Count == 0)
            {
                Console.WriteLine($"Videasy: providers returned no stream ({mediaType}:{tmdbId})");
                proxyManager?.Refresh();
                return null;
            }

            hybridCache.Set(memKey, resolved, cacheTime(15));
            proxyManager?.Success();
            Console.WriteLine($"Videasy: {resolved.Count} streams resolved ({mediaType}:{tmdbId})");
            return resolved;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Videasy direct resolver failed for {MediaType}:{TmdbId}", mediaType, tmdbId);
            return default;
        }
    }

    static string BuildQuery(Dictionary<string, string> values)
    {
        var query = new StringBuilder();
        foreach (var item in values)
        {
            if (string.IsNullOrEmpty(item.Value))
                continue;

            if (query.Length > 0)
                query.Append('&');

            query.Append(HttpUtility.UrlEncode(item.Key));
            query.Append('=');
            query.Append(HttpUtility.UrlEncode(item.Value));
        }
        return query.ToString();
    }

    static string DecryptPayload(string payload, string seed, int mediaId)
    {
        string normalized = payload.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(((normalized.Length + 3) / 4) * 4, '=');
        byte[] encrypted = Convert.FromBase64String(normalized);
        byte[] key = KeyStream(seed, unchecked((uint)mediaId), encrypted.Length);

        for (int i = 0; i < encrypted.Length; i++)
            encrypted[i] ^= key[i];

        if (encrypted.Length < 4 || encrypted[0] != 109 || encrypted[1] != 118 || encrypted[2] != 109 || encrypted[3] != 49)
            throw new InvalidOperationException("Videasy decrypt magic mismatch");

        return Encoding.UTF8.GetString(encrypted, 4, encrypted.Length - 4);
    }

    static byte[] KeyStream(string seed, uint mediaId, int length)
    {
        var state = BuildState(seed, mediaId);
        var output = new byte[length];
        uint counter = 0;

        for (int index = 0; index < length;)
        {
            uint word = NextWord(state.values, ref state.acc, counter++);
            output[index++] = (byte)word;
            if (index < length) output[index++] = (byte)(word >> 8);
            if (index < length) output[index++] = (byte)(word >> 16);
            if (index < length) output[index++] = (byte)(word >> 24);
        }

        return output;
    }

    static (Dictionary<int, uint> values, uint acc) BuildState(string seed, uint mediaId)
    {
        var values = new Dictionary<int, uint>();

        if (IsOddTri(seed.Length))
        {
            int[] box = Rc4Sbox(seed);
            for (int i = 0; i < box.Length; i++)
                values[i] = (uint)box[i];
            return (values, AccSeed(seed));
        }

        uint acc = Mix(Fnv1a(seed) ^ Mix(mediaId ^ 2654435769u));
        for (int i = 0; i < 8; i++)
        {
            if (IsEvenTri(i))
            {
                int slot = (int)(acc % 61);
                acc = RotL(unchecked(acc + 2654435769u), 7 + (i & 7));
                values[slot] = acc ^ Mix(acc);
                acc = Mix(unchecked(acc + (uint)slot));
            }
            else
            {
                values[i] = MixTable[i & 15];
            }
        }

        return (values, Mix(2779096485u ^ acc));
    }

    static uint NextWord(Dictionary<int, uint> values, ref uint acc, uint counter)
    {
        int slot = (int)(acc % 61);
        uint mask = values.TryGetValue(slot, out uint value) ? uint.MaxValue : 0u;
        uint mixed = value ^ unchecked(2654435769u * (counter + 1));
        uint data = (acc ^ mixed) | (acc & mixed & mask);
        data = RotL(unchecked(data + acc), slot & 31) ^ RotL(acc, (slot * 7) & 31);
        acc = Mix(unchecked(data + 2654435769u));
        values[slot] = acc;
        return acc;
    }

    static int[] Rc4Sbox(string seed)
    {
        var box = new int[256];
        for (int i = 0; i < box.Length; i++)
            box[i] = i;

        int cursor = 0;
        for (int i = 0; i < box.Length; i++)
        {
            cursor = (cursor + box[i] + seed[i % seed.Length]) & 255;
            (box[i], box[cursor]) = (box[cursor], box[i]);
        }
        return box;
    }

    static uint AccSeed(string seed)
    {
        uint acc = 1732584193u;
        for (int i = 0; i < seed.Length; i++)
            acc = RotL(acc ^ unchecked((uint)seed[i] * MixTable[i & 15]), 5);
        return Mix(acc);
    }

    static uint Fnv1a(string value)
    {
        uint hash = 2166136261u;
        foreach (char c in value)
            hash = unchecked((hash ^ c) * 16777619u);
        return Mix(hash);
    }

    static uint Mix(uint value)
    {
        value ^= value >> 16;
        value = unchecked(value * 2246822507u);
        value ^= value >> 13;
        value = unchecked(value * 3266489909u);
        return value ^ (value >> 16);
    }

    static uint RotL(uint value, int count)
    {
        count &= 31;
        return count == 0 ? value : (value << count) | (value >> (32 - count));
    }

    static bool IsEvenTri(int value) => ((value * (value + 1)) & 1) == 0;
    static bool IsOddTri(int value) => !IsEvenTri(value);
}
