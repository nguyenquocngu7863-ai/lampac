using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VidLink;

public class VidLinkController : BaseENGController
{
    const string PlayerOrigin = "https://vidlink.pro";
    const string EncDecUrl = "https://enc-dec.app/api/enc-vidlink";

    static readonly byte[] BoxKey = Convert.FromHexString(
        "c75136c5668bbfe65a7ecad431a745db68b5f381555b38d8f6c699449cf11fcd"
    );

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers);

    public VidLinkController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidlink")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        return ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidlink/video")]
    [Route("lite/vidlink/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
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
        string memKey = $"vidlink:http:{mediaType}:{tmdbId}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached) && cached?.Count > 0)
            return cached;

        List<ResolvedStream> resolved = await ResolveHttp(tmdbId, season, episode);
        if (resolved == null || resolved.Count == 0)
            resolved = await ResolvePlaywright(tmdbId, season, episode);

        if (resolved == null || resolved.Count == 0)
        {
            proxyManager?.Refresh();
            return null;
        }

        proxyManager?.Success();
        hybridCache.Set(memKey, resolved, cacheTime(15));
        return resolved;
    }

    async Task<List<ResolvedStream>> ResolveHttp(long tmdbId, short season, short episode)
    {
        var headers = ApiHeaders();
        int timeout = Math.Clamp(init.httptimeout > 0 ? init.httptimeout : 20, 8, 40);

        var tokens = new List<string>(2);
        string local = EncryptToken(tmdbId.ToString());
        if (!string.IsNullOrWhiteSpace(local))
            tokens.Add(local);

        string remote = await EncryptTokenRemote(tmdbId, headers, timeout);
        if (!string.IsNullOrWhiteSpace(remote) &&
            !tokens.Exists(i => string.Equals(i, remote, StringComparison.Ordinal)))
        {
            tokens.Add(remote);
        }

        foreach (string token in tokens)
        {
            string path = season > 0
                ? $"api/b/tv/{Uri.EscapeDataString(token)}/{season}/{Math.Max(episode, (short)1)}"
                : $"api/b/movie/{Uri.EscapeDataString(token)}";

            string url = $"{init.host.TrimEnd('/')}/{path}?multiLang=1";
            JToken root = await Http.Get<JToken>(
                url,
                timeoutSeconds: timeout,
                httpversion: 2,
                headers: headers,
                statusCodeOK: false,
                proxy: proxy
            );

            List<ResolvedStream> streams = ReadStreams(root);
            if (streams.Count > 0)
            {
                Console.WriteLine($"VidLink: {streams.Count} HTTP streams ({tmdbId})");
                return streams;
            }
        }

        Console.WriteLine($"VidLink: HTTP resolver empty ({tmdbId})");
        return null;
    }

    async Task<string> EncryptTokenRemote(long tmdbId, List<HeadersModel> headers, int timeout)
    {
        try
        {
            var root = await Http.Get<JObject>(
                $"{EncDecUrl}?text={tmdbId}",
                timeoutSeconds: timeout,
                headers: headers,
                statusCodeOK: false,
                proxy: proxy
            );

            string token = root?.Value<string>("result") ??
                root?["result"]?.Value<string>("encrypted") ??
                root?.Value<string>("encrypted");

            return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }
        catch
        {
            return null;
        }
    }

    List<ResolvedStream> ReadStreams(JToken root)
    {
        var result = new List<ResolvedStream>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root == null)
            return result;

        var headers = StreamHeaders();
        Collect(root, result, seen, headers, depth: 0);

        if (result.Count == 0 && root is JValue value && value.Type == JTokenType.String)
            TryAdd(value.Value<string>(), "Auto", result, seen, headers);

        return result;
    }

    static void Collect(
        JToken token,
        List<ResolvedStream> result,
        HashSet<string> seen,
        List<HeadersModel> headers,
        int depth
    )
    {
        if (token == null || depth > 8)
            return;

        if (token is JArray array)
        {
            foreach (JToken item in array)
                Collect(item, result, seen, headers, depth + 1);
            return;
        }

        if (token is not JObject obj)
            return;

        string url = FirstString(obj, "playlist", "file", "url", "src", "stream", "link");
        string label = FirstString(obj, "quality", "label", "title", "name", "language", "lang", "server");
        TryAdd(url, label, result, seen, headers);

        foreach (JProperty property in obj.Properties())
        {
            if (property.Value is JObject or JArray)
                Collect(property.Value, result, seen, headers, depth + 1);
            else if (property.Value?.Type == JTokenType.String &&
                     IsStreamName(property.Name))
            {
                TryAdd(property.Value.Value<string>(), label ?? property.Name, result, seen, headers);
            }
        }
    }

    static bool IsStreamName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string value = name.ToLowerInvariant();
        return value is "playlist" or "file" or "url" or "src" or "stream" or "link" or "m3u8" or "hls";
    }

    static string FirstString(JObject obj, params string[] names)
    {
        foreach (string name in names)
        {
            JToken token = obj[name];
            if (token == null || token.Type == JTokenType.Null)
                continue;

            if (token.Type == JTokenType.String)
            {
                string value = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    static void TryAdd(
        string url,
        string label,
        List<ResolvedStream> result,
        HashSet<string> seen,
        List<HeadersModel> headers
    )
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
            uri.Scheme is not ("http" or "https") ||
            !seen.Add(url))
        {
            return;
        }

        if (!LooksLikeMedia(url) && result.Count > 0)
            return;

        if (!LooksLikeMedia(url) && !url.Contains("m3u", StringComparison.OrdinalIgnoreCase))
            return;

        string quality = string.IsNullOrWhiteSpace(label) ? GuessQuality(url) : Compact(label);
        if (string.IsNullOrWhiteSpace(quality))
            quality = "Auto";

        if (result.Exists(i => i.Label.Equals(quality, StringComparison.OrdinalIgnoreCase)))
            quality += $" #{result.Count + 1}";

        result.Add(new ResolvedStream(url, quality, headers));
    }

    static bool LooksLikeMedia(string url)
    {
        return url.Contains(".m3u", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".mp4", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("m3u8", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/hls", StringComparison.OrdinalIgnoreCase);
    }

    static string GuessQuality(string url)
    {
        Match match = Regex.Match(url, @"(?<!\d)(2160|1440|1080|720|576|480|360)p?", RegexOptions.IgnoreCase);
        return match.Success ? $"{match.Groups[1].Value}p" : "Auto";
    }

    static string Compact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string result = Regex.Replace(value, @"\s+", " ").Trim();
        return result.Length > 80 ? result[..80] : result;
    }

    List<HeadersModel> ApiHeaders()
    {
        return HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", PlayerOrigin + "/"),
            ("Origin", PlayerOrigin),
            ("Accept", "application/json, text/plain, */*")
        );
    }

    List<HeadersModel> StreamHeaders()
    {
        if (init.headers_stream != null && init.headers_stream.Count > 0)
            return HeadersModel.Init(init.headers_stream);

        return HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", PlayerOrigin + "/"),
            ("Origin", PlayerOrigin)
        );
    }

    static string EncryptToken(string mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId))
            return null;

        byte[] id = Encoding.UTF8.GetBytes(mediaId);
        byte[] message = new byte[id.Length + 8];
        Buffer.BlockCopy(id, 0, message, 0, id.Length);

        ulong timestamp = unchecked((ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + 480;
        BinaryPrimitives.WriteUInt64BigEndian(message.AsSpan(id.Length), timestamp);

        byte[] nonce = new byte[24];
        byte[] cipher = XSalsa20Poly1305.Seal(BoxKey, nonce, message);
        byte[] payload = new byte[nonce.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length, cipher.Length);

        return Convert.ToBase64String(payload)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    async Task<List<ResolvedStream>> ResolvePlaywright(long id, short s, short e)
    {
        if (PlaywrightBrowser.Status == PlaywrightStatus.disabled)
            return null;

        string embed = $"{init.host}/movie/{id}";
        if (s > 0)
            embed = $"{init.host}/tv/{id}/{s}/{e}";

        var result = await black_magic(embed);
        if (string.IsNullOrWhiteSpace(result.m3u8))
            return null;

        return
        [
            new ResolvedStream(
                result.m3u8,
                "Playwright",
                result.headers ?? StreamHeaders()
            )
        ];
    }

    async Task<(string m3u8, List<HeadersModel> headers)> black_magic(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return default;

        try
        {
            string memKey = $"vidlink:black_magic:{uri}";
            if (!hybridCache.TryGetValue(memKey, out (string m3u8, List<HeadersModel> headers) cache))
            {
                using (var browser = new PlaywrightBrowser(init.priorityBrowser))
                {
                    var page = await browser.NewPageAsync(init.plugin, httpHeaders(init)?.ToDictionary(), proxy_data);
                    if (page == null)
                        return default;

                    await page.RouteAsync("**/*", async route =>
                    {
                        try
                        {
                            if (browser.IsCompleted)
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: true, fullCacheJS: true, patterCache: "/api/(mercury|venus)$"))
                                return;

                            if (route.Request.Url.Contains("adsco.") || route.Request.Url.Contains("pubtrky.") || route.Request.Url.Contains("clarity."))
                            {
                                PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                                await route.AbortAsync();
                                return;
                            }

                            if (route.Request.Url.Contains(".m3u") || route.Request.Url.Contains(".mp4"))
                            {
                                cache.headers = new List<HeadersModel>();
                                foreach (var item in route.Request.Headers)
                                {
                                    if (item.Key.ToLower() is "host" or "accept-encoding" or "connection" or "range")
                                        continue;

                                    cache.headers.Add(new HeadersModel(item.Key, item.Value.ToString()));
                                }

                                PlaywrightBase.ConsoleLog(() => ($"Playwright: SET {route.Request.Url}", cache.headers));
                                browser.SetPageResult(route.Request.Url);
                                await route.AbortAsync();
                                return;
                            }

                            await route.ContinueAsync();
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Error(ex, "{Class} {CatchId}", "VidLink", "id_ejvmtgh5");
                        }
                    });

                    PlaywrightBase.GotoAsync(page, uri);
                    cache.m3u8 = await browser.WaitPageResult(12);
                }

                if (cache.m3u8 == null)
                    return default;

                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            return cache;
        }
        catch
        {
            return default;
        }
    }

    static class XSalsa20Poly1305
    {
        static readonly uint[] Sigma =
        [
            0x61707865, 0x3320646e, 0x79622d32, 0x6b206574
        ];

        public static byte[] Seal(byte[] key, byte[] nonce, byte[] message)
        {
            byte[] subKey = HSalsa20(nonce.AsSpan(0, 16), key);
            byte[] padded = new byte[32 + message.Length];
            Buffer.BlockCopy(message, 0, padded, 32, message.Length);
            byte[] xored = Salsa20Xor(padded, nonce.AsSpan(16, 8), subKey);
            byte[] mac = Poly1305(xored.AsSpan(32), xored.AsSpan(0, 32));
            byte[] output = new byte[16 + message.Length];
            Buffer.BlockCopy(mac, 0, output, 0, 16);
            Buffer.BlockCopy(xored, 32, output, 16, message.Length);
            return output;
        }

        static byte[] HSalsa20(ReadOnlySpan<byte> nonce16, byte[] key)
        {
            Span<uint> state = stackalloc uint[16];
            LoadKey(state, key);
            state[6] = BinaryPrimitives.ReadUInt32LittleEndian(nonce16);
            state[7] = BinaryPrimitives.ReadUInt32LittleEndian(nonce16[4..]);
            state[8] = BinaryPrimitives.ReadUInt32LittleEndian(nonce16[8..]);
            state[9] = BinaryPrimitives.ReadUInt32LittleEndian(nonce16[12..]);
            CoreRounds(state);
            byte[] output = new byte[32];
            WriteU32(output, 0, state[0]);
            WriteU32(output, 4, state[5]);
            WriteU32(output, 8, state[10]);
            WriteU32(output, 12, state[15]);
            WriteU32(output, 16, state[6]);
            WriteU32(output, 20, state[7]);
            WriteU32(output, 24, state[8]);
            WriteU32(output, 28, state[9]);
            return output;
        }

        static byte[] Salsa20Xor(byte[] message, ReadOnlySpan<byte> nonce8, byte[] key)
        {
            Span<uint> input = stackalloc uint[16];
            LoadKey(input, key);
            input[6] = BinaryPrimitives.ReadUInt32LittleEndian(nonce8);
            input[7] = BinaryPrimitives.ReadUInt32LittleEndian(nonce8[4..]);
            input[8] = 0;
            input[9] = 0;

            byte[] output = new byte[message.Length];
            Span<uint> block = stackalloc uint[16];
            Span<byte> keystream = stackalloc byte[64];
            int offset = 0;
            while (offset < message.Length)
            {
                input.CopyTo(block);
                CoreRounds(block);
                for (int i = 0; i < 16; i++)
                    WriteU32(keystream, i * 4, unchecked(block[i] + input[i]));

                int n = Math.Min(64, message.Length - offset);
                for (int i = 0; i < n; i++)
                    output[offset + i] = (byte)(message[offset + i] ^ keystream[i]);

                input[8] = unchecked(input[8] + 1);
                if (input[8] == 0)
                    input[9] = unchecked(input[9] + 1);

                offset += n;
            }

            return output;
        }

        static void LoadKey(Span<uint> state, byte[] key)
        {
            state[0] = Sigma[0];
            state[5] = Sigma[1];
            state[10] = Sigma[2];
            state[15] = Sigma[3];
            state[1] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(0, 4));
            state[2] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(4, 4));
            state[3] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(8, 4));
            state[4] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(12, 4));
            state[11] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(16, 4));
            state[12] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(20, 4));
            state[13] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(24, 4));
            state[14] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(28, 4));
        }

        static void CoreRounds(Span<uint> x)
        {
            for (int i = 0; i < 10; i++)
            {
                x[4] ^= RotL(unchecked(x[0] + x[12]), 7);
                x[8] ^= RotL(unchecked(x[4] + x[0]), 9);
                x[12] ^= RotL(unchecked(x[8] + x[4]), 13);
                x[0] ^= RotL(unchecked(x[12] + x[8]), 18);

                x[9] ^= RotL(unchecked(x[5] + x[1]), 7);
                x[13] ^= RotL(unchecked(x[9] + x[5]), 9);
                x[1] ^= RotL(unchecked(x[13] + x[9]), 13);
                x[5] ^= RotL(unchecked(x[1] + x[13]), 18);

                x[14] ^= RotL(unchecked(x[10] + x[6]), 7);
                x[2] ^= RotL(unchecked(x[14] + x[10]), 9);
                x[6] ^= RotL(unchecked(x[2] + x[14]), 13);
                x[10] ^= RotL(unchecked(x[6] + x[2]), 18);

                x[3] ^= RotL(unchecked(x[15] + x[11]), 7);
                x[7] ^= RotL(unchecked(x[3] + x[15]), 9);
                x[11] ^= RotL(unchecked(x[7] + x[3]), 13);
                x[15] ^= RotL(unchecked(x[11] + x[7]), 18);

                x[1] ^= RotL(unchecked(x[0] + x[3]), 7);
                x[2] ^= RotL(unchecked(x[1] + x[0]), 9);
                x[3] ^= RotL(unchecked(x[2] + x[1]), 13);
                x[0] ^= RotL(unchecked(x[3] + x[2]), 18);

                x[6] ^= RotL(unchecked(x[5] + x[4]), 7);
                x[7] ^= RotL(unchecked(x[6] + x[5]), 9);
                x[4] ^= RotL(unchecked(x[7] + x[6]), 13);
                x[5] ^= RotL(unchecked(x[4] + x[7]), 18);

                x[11] ^= RotL(unchecked(x[10] + x[9]), 7);
                x[8] ^= RotL(unchecked(x[11] + x[10]), 9);
                x[9] ^= RotL(unchecked(x[8] + x[11]), 13);
                x[10] ^= RotL(unchecked(x[9] + x[8]), 18);

                x[12] ^= RotL(unchecked(x[15] + x[14]), 7);
                x[13] ^= RotL(unchecked(x[12] + x[15]), 9);
                x[14] ^= RotL(unchecked(x[13] + x[12]), 13);
                x[15] ^= RotL(unchecked(x[14] + x[13]), 18);
            }
        }

        static uint RotL(uint value, int count) => (value << count) | (value >> (32 - count));

        static void WriteU32(Span<byte> dest, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dest[offset..], value);
        }

        static byte[] Poly1305(ReadOnlySpan<byte> message, ReadOnlySpan<byte> key)
        {
            BigInteger r = new BigInteger(key[..16], isUnsigned: true, isBigEndian: false);
            r &= BigInteger.Parse("0ffffffc0ffffffc0ffffffc0fffffff", NumberStyles.HexNumber);
            BigInteger s = new BigInteger(key[16..], isUnsigned: true, isBigEndian: false);
            BigInteger h = BigInteger.Zero;
            BigInteger p = (BigInteger.One << 130) - 5;

            for (int i = 0; i < message.Length; i += 16)
            {
                int n = Math.Min(16, message.Length - i);
                byte[] block = new byte[n + 2];
                message.Slice(i, n).CopyTo(block);
                block[n] = 1;
                h = ((h + new BigInteger(block, isUnsigned: true, isBigEndian: false)) * r) % p;
            }

            h += s;
            byte[] tag = new byte[16];
            byte[] raw = h.ToByteArray(isUnsigned: true, isBigEndian: false);
            Buffer.BlockCopy(raw, 0, tag, 0, Math.Min(16, raw.Length));
            return tag;
        }
    }
}
