using Microsoft.AspNetCore.Mvc;
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

namespace VidNest;

public class VidNestController : BaseENGController
{
    const string ApiBase = "https://new.vidnest.fun";
    const string Server = "hollymoviehd";

    // Custom alphabet giai ma cua VidNest (port tu decrypt.ts cua cinepro).
    const string Alphabet = "RB0fpH8ZEyVLkv7c2i6MAJ5u3IKFDxlS1NTsnGaqmXYdUrtzjwObCgQP94hoeW+/=";

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers);

    public VidNestController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidnest")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidnest/video")]
    [Route("lite/vidnest/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false, int srv = 0)
    {
        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (id <= 0)
            return OnError();

        var resolved = await Resolve(id, s, e);
        if (resolved == null || resolved.Count == 0)
            return OnError("stream", 502);

        // Link phat qua route video.m3u8 cua chinh mình (redirect 302 sang proxy)
        // de player trong nhan ra HLS (xem bai hoc VixSrc).
        if (play)
        {
            int i = Math.Clamp(srv, 0, resolved.Count - 1);
            return RedirectToPlay(HostStreamProxy(resolved[i].Url, headers: resolved[i].Headers));
        }

        var qualities = new StreamQualityTpl(resolved.Count);
        for (int i = 0; i < resolved.Count; i++)
            qualities.Append(MediaUrl(id, s, e, i), resolved[i].Label);

        if (qualities.IsEmpty)
            return OnError("stream", 502);

        var first = qualities.Firts();

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

    string MediaUrl(long id, short s, short e, int srv)
    {
        string q = $"id={id}&s={s}&e={e}&play=true&srv={srv}";
        return $"{host}/lite/vidnest/video.m3u8?{q}";
    }

    async Task<List<ResolvedStream>> Resolve(long tmdbId, short season, short episode)
    {
        bool isTv = season > 0;
        string query = isTv
            ? $"{tmdbId}/{season}/{Math.Max(episode, (short)1)}"
            : tmdbId.ToString();

        string memKey = $"vidnest:{Server}:{(isTv ? "tv" : "movie")}:{query}";
        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached) && cached != null && cached.Count > 0)
            return cached;

        var apiHeaders = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "application/json, text/javascript, */*"),
            ("Accept-Language", "en-US,en;q=0.9"),
            ("Referer", "https://vidnest.fun/"),
            ("Origin", "https://vidnest.fun")
        );

        try
        {
            string apiUrl = isTv
                ? $"{ApiBase}/{Server}/tv/{query}"
                : $"{ApiBase}/{Server}/movie/{query}";

            var apiJson = await httpHydra.Get<JObject>(apiUrl, addheaders: apiHeaders, statusCodeOK: false);
            if (apiJson == null)
            {
                Console.WriteLine($"VidNest: empty api ({query}) {apiUrl}");
                return null;
            }

            string payload = apiJson.Value<string>("data");
            if (string.IsNullOrWhiteSpace(payload))
            {
                Console.WriteLine($"VidNest: no data ({query})");
                return null;
            }

            JObject root;
            if (string.Equals(apiJson.Value<string>("encrypted"), "True", StringComparison.OrdinalIgnoreCase)
                || apiJson["encrypted"]?.Type == JTokenType.Boolean && apiJson.Value<bool>("encrypted"))
            {
                string dec = DecodeVidnestBase64(payload);
                if (string.IsNullOrWhiteSpace(dec))
                {
                    Console.WriteLine($"VidNest: decrypt failed ({query})");
                    return null;
                }
                root = JObject.Parse(dec);
            }
            else
            {
                root = JObject.Parse(payload);
            }

            var streams = root["streams"] as JArray;
            if (streams == null || streams.Count == 0)
            {
                Console.WriteLine($"VidNest: no streams ({query})");
                return null;
            }

            var resolved = new List<ResolvedStream>(streams.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var st in streams)
            {
                string url = st.Value<string>("url");
                if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
                    continue;

                string lang = st.Value<string>("language") ?? "auto";
                string referer = st["headers"]?.Value<string>("Referer") ?? "https://vidnest.fun/";

                var headers = HeadersModel.Init(
                    ("User-Agent", Http.UserAgent),
                    ("Referer", referer),
                    ("Origin", "https://vidnest.fun")
                );

                resolved.Add(new ResolvedStream(url, $"{Server} {lang}", headers));
            }

            if (resolved.Count == 0)
            {
                proxyManager?.Refresh();
                return null;
            }

            hybridCache.Set(memKey, resolved, cacheTime(30));
            proxyManager?.Success();
            Console.WriteLine($"VidNest: {resolved.Count} streams ({query})");
            return resolved;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "VidNest resolve failed {Query}", query);
            return null;
        }
    }

    static string DecodeVidnestBase64(string input)
    {
        try
        {
            if (string.IsNullOrEmpty(input))
                return null;

            var rev = new Dictionary<char, int>(64);
            for (int i = 0; i < Alphabet.Length; i++)
            {
                if (Alphabet[i] != '=' && !rev.ContainsKey(Alphabet[i]))
                    rev[Alphabet[i]] = i;
            }

            string padded = input;
            int mod = padded.Length % 4;
            if (mod != 0)
                padded += new string('=', 4 - mod);

            var bytes = new List<byte>(padded.Length);

            for (int i = 0; i < padded.Length; i += 4)
            {
                int c0 = padded[i] == '=' ? 64 : rev.TryGetValue(padded[i], out int v0) ? v0 : 64;
                int c1 = padded[i + 1] == '=' ? 64 : rev.TryGetValue(padded[i + 1], out int v1) ? v1 : 64;
                int c2 = padded[i + 2] == '=' ? 64 : rev.TryGetValue(padded[i + 2], out int v2) ? v2 : 64;
                int c3 = padded[i + 3] == '=' ? 64 : rev.TryGetValue(padded[i + 3], out int v3) ? v3 : 64;

                bytes.Add((byte)(((c0 << 2) | (c1 >> 4)) & 0xff));
                if (c2 != 64)
                    bytes.Add((byte)((((c1 & 0x0f) << 4) | (c2 >> 2)) & 0xff));
                if (c3 != 64)
                    bytes.Add((byte)((((c2 & 0x03) << 6) | c3) & 0xff));
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
