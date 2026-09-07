using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Vidrock;

public class VidrockController : BaseENGController
{
    const string ApiBase = "https://vidrock.ru";
    const string AesKeyHex = "7f3e9c2a8b5d1f4e6a9c3b7d2e5f8a1c4b6d9e2f5a8c1b4d7e9f2a5c8b1d4e7f";

    static readonly byte[] AesKey = Convert.FromHexString(AesKeyHex);

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers);

    public VidrockController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidrock")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidrock/video")]
    [Route("lite/vidrock/video.m3u8")]
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

    async Task<List<ResolvedStream>> Resolve(long tmdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string query = mediaType == "movie"
            ? tmdbId.ToString()
            : $"{tmdbId}/{season}/{Math.Max(episode, (short)1)}";

        string memKey = $"vidrock:{mediaType}:{query}";
        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached) && cached != null && cached.Count > 0)
            return cached;

        var apiHeaders = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "application/json, text/plain, */*"),
            ("Referer", ApiBase + "/"),
            ("Origin", ApiBase)
        );

        try
        {
            string url = $"{ApiBase}/api/{mediaType}/{query}";
            var json = await httpHydra.Get<JObject>(url, addheaders: apiHeaders, statusCodeOK: false);
            if (json == null)
            {
                Console.WriteLine($"Vidrock: empty json ({mediaType}:{query}) {url}");
                return null;
            }

            var resolved = new List<ResolvedStream>(8);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in json.Properties())
            {
                if (prop.Value is not JObject srv)
                    continue;

                string enc = srv.Value<string>("url");
                if (string.IsNullOrWhiteSpace(enc))
                    continue;

                string dec = DecryptAesGcm(enc);
                if (string.IsNullOrWhiteSpace(dec))
                {
                    Console.WriteLine($"Vidrock: decrypt failed server={prop.Name} len={enc.Length}");
                    continue;
                }

                if (!Uri.TryCreate(dec, UriKind.Absolute, out _))
                    continue;

                if (!seen.Add(dec))
                    continue;

                // keep all types but prefer hls; type field from api
                string label = prop.Name;
                // e.g. Nova, Luna, Orion...
                var headers = HeadersModel.Init(
                    ("User-Agent", Http.UserAgent),
                    ("Referer", ApiBase + "/"),
                    ("Origin", ApiBase)
                );

                resolved.Add(new ResolvedStream(dec, label, headers));
            }

            if (resolved.Count == 0)
            {
                Console.WriteLine($"Vidrock: no streams ({mediaType}:{query}) json={json.ToString(Newtonsoft.Json.Formatting.None).Substring(0, Math.Min(300, json.ToString(Newtonsoft.Json.Formatting.None).Length))}");
                proxyManager?.Refresh();
                return null;
            }

            hybridCache.Set(memKey, resolved, cacheTime(30));
            proxyManager?.Success();
            Console.WriteLine($"Vidrock: {resolved.Count} streams ({mediaType}:{query})");
            return resolved;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Vidrock resolve failed {MediaType}:{Query}", mediaType, query);
            return null;
        }
    }

    static string DecryptAesGcm(string payload)
    {
        try
        {
            string s = payload.Trim().Replace('-', '+').Replace('_', '/');
            s = s.PadRight((s.Length + 3) / 4 * 4, '=');
            byte[] data = Convert.FromBase64String(s);
            if (data.Length < 12 + 16)
                return null;

            byte[] nonce = new byte[12];
            Buffer.BlockCopy(data, 0, nonce, 0, 12);

            int ctagLen = data.Length - 12;
            byte[] ctag = new byte[ctagLen];
            Buffer.BlockCopy(data, 12, ctag, 0, ctagLen);

            // split tag (last 16 bytes)
            byte[] ct = new byte[ctagLen - 16];
            byte[] tag = new byte[16];
            Buffer.BlockCopy(ctag, 0, ct, 0, ct.Length);
            Buffer.BlockCopy(ctag, ct.Length, tag, 0, 16);

            byte[] plain = new byte[ct.Length];
            using var aes = new AesGcm(AesKey, 16);
            aes.Decrypt(nonce, ct, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }
}
