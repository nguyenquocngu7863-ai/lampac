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

    public VidrockController() : base(ModInit.conf) { }

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
        var streams = await Resolve(id, s, e);
        if (streams == null || streams.Count == 0)
            return OnError("stream", 502);
        var qualities = new StreamQualityTpl(streams.Count);
        foreach (var st in streams)
            qualities.Append(HostStreamProxy(st.Url, headers: st.Headers), st.Label);
        if (qualities.IsEmpty)
            return OnError("stream", 502);
        var first = qualities.Firts();
        if (play)
            return RedirectToPlay(first.link);
        return ContentTo(VideoTpl.ToJson("play", first.link, "English", streamquality: qualities, vast: init.vast, hls_manifest_timeout: 120000, httpContext: HttpContext));
    }

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers);

    async Task<List<ResolvedStream>> Resolve(long tmdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string query = mediaType == "movie" ? tmdbId.ToString() : $"{tmdbId}_{season}_{episode}";
        string memKey = $"vidrock:{mediaType}:{query}";
        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached))
            return cached;

        var apiHeaders = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", ApiBase + "/"),
            ("Origin", ApiBase),
            ("Accept", "application/json")
        );

        try
        {
            string url = $"{ApiBase}/api/{mediaType}/{query}/";
            var json = await httpHydra.Get<JObject>(url, addheaders: apiHeaders, statusCodeOK: false);
            if (json == null)
                return null;

            var resolved = new List<ResolvedStream>();
            foreach (var prop in json.Properties())
            {
                var serverData = prop.Value as JObject;
                if (serverData == null) continue;
                string enc = serverData.Value<string>("url");
                if (string.IsNullOrWhiteSpace(enc) || enc == "null" || enc == "error") continue;
                string dec = Decrypt(enc);
                if (string.IsNullOrWhiteSpace(dec)) continue;
                if (!Uri.TryCreate(dec, UriKind.Absolute, out _)) continue;
                string label = $"Vidrock [{prop.Name}]";
                // Orion often gives 1080p, keep label simple
                var h = HeadersModel.Init(
                    ("Referer", ApiBase + "/"),
                    ("Origin", ApiBase),
                    ("User-Agent", Http.UserAgent)
                );
                resolved.Add(new ResolvedStream(dec, label, h));
            }

            if (resolved.Count == 0)
                return null;

            hybridCache.Set(memKey, resolved, cacheTime(30));
            return resolved;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Vidrock resolve failed {Tmdb}", tmdbId);
            return null;
        }
    }

    static string Decrypt(string payload)
    {
        try
        {
            string s = payload.Replace('-', '+').Replace('_', '/');
            s = s.PadRight((s.Length + 3) / 4 * 4, '=');
            byte[] data = Convert.FromBase64String(s);
            if (data.Length < 12 + 16) return null;
            byte[] nonce = new byte[12];
            Array.Copy(data, 0, nonce, 0, 12);
            byte[] cttag = new byte[data.Length - 12];
            Array.Copy(data, 12, cttag, 0, cttag.Length);
            byte[] ct = new byte[cttag.Length - 16];
            byte[] tag = new byte[16];
            Array.Copy(cttag, 0, ct, 0, ct.Length);
            Array.Copy(cttag, ct.Length, tag, 0, 16);
            byte[] key = Convert.FromHexString(AesKeyHex);
            byte[] plain = new byte[ct.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ct, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }
}
