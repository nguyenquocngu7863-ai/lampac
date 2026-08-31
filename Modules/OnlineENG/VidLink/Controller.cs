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
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace VidLink;

public class VidLinkController : BaseENGController
{
    const string PlayerOrigin = "https://vidlink.pro";
    const string CdnOrigin = "https://filmboom.top";
    const string EncDecUrl = "https://enc-dec.app/api/enc-vidlink";
    const string CsUserAgent =
        "Mozilla/5.0 (Linux; Android 11; Mi 9T Pro) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/95.0.4638.69 Mobile Safari/537.36 EdgA/95.0.1020.48";

    static readonly byte[] BoxKey = Convert.FromHexString(
        "c75136c5668bbfe65a7ecad431a745db68b5f381555b38d8f6c699449cf11fcd"
    );

    sealed record ResolvedStream(string Url, string Label, List<HeadersModel> Headers, bool Hls = false);

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
        StatiCacheDisabled = true;
        SetHeadersNoCache();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
            return badInitMsg;

        if (id <= 0)
            return OnError();

        List<ResolvedStream> resolved = await Resolve(id, s, e);
        if (resolved == null || resolved.Count == 0)
            return OnError("stream", 502);

        resolved.Sort((a, b) => b.Hls.CompareTo(a.Hls));

        var qualities = new StreamQualityTpl(resolved.Count);
        foreach (ResolvedStream item in resolved)
        {
            // CSX: type m3u8 / URL .m3u8 → HLS (hls.js). Else progressive MP4
            // with filmboom.top headers. Lampa only uses hls.js for *.m3u8.
            bool playlist = item.Hls || LooksLikeHls(item.Url);
            qualities.Append(
                playlist ? PlaylistLink(item.Url) : MediaLink(item.Url, ".mp4"),
                item.Label
            );
        }

        if (qualities.IsEmpty)
            return OnError("stream", 502);

        var first = qualities.Firts();
        Console.WriteLine($"VidLink: play {first.link}");
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

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidlink/playlist.m3u8")]
    public async Task<ActionResult> Playlist(string uri)
    {
        string source = DecryptQuery(uri);
        if (string.IsNullOrWhiteSpace(source) || !IsHttpUrl(source))
            return OnError("uri");

        var fetched = await FetchPlaylist(source);
        if (string.IsNullOrWhiteSpace(fetched.body))
            return OnError("playlist", refresh_proxy: true);

        string rewritten = RewritePlaylist(fetched.body, fetched.url, fetched.headers);
        if (!IsExtM3u(rewritten))
            return OnError("playlist", refresh_proxy: true);

        return ContentTo(rewritten, "application/vnd.apple.mpegurl; charset=utf-8");
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidlink/media")]
    [Route("lite/vidlink/media.ts")]
    [Route("lite/vidlink/media.m4s")]
    [Route("lite/vidlink/media.mp4")]
    [Route("lite/vidlink/file.mp4")]
    public async Task<ActionResult> Media(string uri)
    {
        StatiCacheDisabled = true;
        SetHeadersNoCache();

        string source = DecryptQuery(uri);
        if (string.IsNullOrWhiteSpace(source) || !IsHttpUrl(source))
            return OnError("uri");

        return await PipeMedia(source);
    }

    async Task<List<ResolvedStream>> Resolve(long tmdbId, short season, short episode)
    {
        string mediaType = season > 0 ? "tv" : "movie";
        string memKey = $"vidlink:play6:{mediaType}:{tmdbId}:{season}:{episode}";
        if (hybridCache.TryGetValue(memKey, out List<ResolvedStream> cached) && cached?.Count > 0)
            return cached;

        List<ResolvedStream> resolved = await ResolveHttp(tmdbId, season, episode);
        if (resolved == null || resolved.Count == 0)
        {
            var playwright = await ResolvePlaywright(tmdbId, season, episode);
            if (playwright != null && playwright.Count > 0)
                resolved = await FinalizeStreams(playwright);
        }

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

        // CSX uses enc-dec.app first, then GET /api/b/movie|tv/{token} (no multiLang).
        var tokens = new List<string>(2);
        string remote = await EncryptTokenRemote(tmdbId, headers, timeout);
        if (!string.IsNullOrWhiteSpace(remote))
            tokens.Add(remote);

        string local = EncryptToken(tmdbId.ToString());
        if (!string.IsNullOrWhiteSpace(local) &&
            !tokens.Exists(i => string.Equals(i, local, StringComparison.Ordinal)))
        {
            tokens.Add(local);
        }

        foreach (string token in tokens)
        {
            string path = season > 0
                ? $"api/b/tv/{Uri.EscapeDataString(token)}/{season}/{Math.Max(episode, (short)1)}"
                : $"api/b/movie/{Uri.EscapeDataString(token)}";

            foreach (string extra in new[] { "", "?multiLang=1" })
            {
                string url = $"{init.host.TrimEnd('/')}/{path}{extra}";
                JToken root = await Http.Get<JToken>(
                    url,
                    timeoutSeconds: timeout,
                    httpversion: 2,
                    headers: headers,
                    statusCodeOK: false,
                    proxy: proxy
                );

                List<ResolvedStream> streams = await FinalizeStreams(ReadStreams(root));
                if (streams.Count > 0)
                {
                    int hls = streams.FindAll(i => i.Hls).Count;
                    Console.WriteLine($"VidLink: {streams.Count} HTTP streams ({tmdbId}), hls={hls}");
                    return streams;
                }
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

        Collect(root, result, seen, depth: 0);
        return result;
    }

    async Task<List<ResolvedStream>> FinalizeStreams(List<ResolvedStream> streams)
    {
        if (streams == null || streams.Count == 0)
            return streams;

        var result = new List<ResolvedStream>(streams.Count);
        List<HeadersModel> working = null;

        foreach (ResolvedStream item in streams)
        {
            ProbeResult probe = null;
            if (item.Headers?.Count > 0)
                probe = await ProbeUrl(item.Url, item.Headers);

            if (probe is not { Ok: true } && working != null)
                probe = await ProbeUrl(item.Url, working);

            if (probe is not { Ok: true })
            {
                foreach (List<HeadersModel> headers in HeaderVariants(item.Url))
                {
                    probe = await ProbeUrl(item.Url, headers);
                    if (probe?.Ok == true)
                    {
                        working = headers;
                        RememberHeaders(item.Url, headers);
                        break;
                    }
                }
            }

            if (probe is { Ok: true })
            {
                result.Add(item with
                {
                    Url = probe.Url,
                    Hls = probe.Hls,
                    Headers = probe.Headers
                });
                continue;
            }

            // Still expose a local playlist URL so hls.js does not hit the CDN
            // through /proxy with Origin / no-redirect. Playlist() retries.
            result.Add(item with
            {
                Hls = item.Hls || LooksLikeHls(item.Url),
                Headers = item.Headers?.Count > 0 ? item.Headers : StreamHeaders()
            });
        }

        result.Sort((a, b) => b.Hls.CompareTo(a.Hls));
        return result;
    }

    sealed record ProbeResult(bool Ok, bool Hls, string Url, List<HeadersModel> Headers);

    async Task<ProbeResult> ProbeUrl(string url, List<HeadersModel> headers)
    {
        try
        {
            var probe = await Http.BaseGet(
                url,
                timeoutSeconds: 12,
                headers: headers,
                statusCodeOK: false,
                MaxResponseContentBufferSize: 262144,
                proxy: proxy,
                useDefaultHeaders: false
            );

            int status = (int)(probe.response?.StatusCode ?? 0);
            string body = probe.content ?? string.Empty;
            string trim = body.TrimStart();
            string preview = PreviewOf(trim);
            string finalUrl = StripHash(probe.response?.RequestMessage?.RequestUri?.AbsoluteUri ?? url);

            if (status is 200 or 206)
            {
                if (IsExtM3u(trim))
                {
                    Console.WriteLine($"VidLink: {HostOf(url)} {status} hls {preview}");
                    return new ProbeResult(true, true, finalUrl, headers);
                }

                if (LooksLikeMp4(body))
                {
                    Console.WriteLine($"VidLink: {HostOf(url)} {status} mp4 {preview}");
                    return new ProbeResult(true, false, finalUrl, headers);
                }

                if (trim.StartsWith('{'))
                {
                    try
                    {
                        var root = JObject.Parse(trim);
                        string inner = FirstString(root, "playlist", "url", "file", "src");
                        if (!string.IsNullOrWhiteSpace(inner) &&
                            !string.Equals(inner, url, StringComparison.OrdinalIgnoreCase) &&
                            Uri.TryCreate(inner, UriKind.Absolute, out _))
                        {
                            Console.WriteLine($"VidLink: {HostOf(url)} {status} json→{HostOf(inner)}");
                            return await ProbeUrl(inner, headers);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            Console.WriteLine($"VidLink: {HostOf(url)} {status} fail {preview}");
            return new ProbeResult(false, false, url, headers);
        }
        catch (Exception ex)
        {
            if (LooksLikeProgressive(url))
            {
                Console.WriteLine($"VidLink: {HostOf(url)} mp4-ex {ex.GetType().Name}");
                return new ProbeResult(true, false, url, headers);
            }

            Console.WriteLine($"VidLink: {HostOf(url)} ex {ex.GetType().Name}");
            return new ProbeResult(false, false, url, headers);
        }
    }

    async Task<(string body, string url, List<HeadersModel> headers)> FetchPlaylist(string url)
    {
        var variants = new List<List<HeadersModel>>();
        string hostKey = $"vidlink:hdr:{HostOf(url)}";
        if (hybridCache.TryGetValue(hostKey, out List<HeadersModel> remembered) && remembered?.Count > 0)
            variants.Add(remembered);

        foreach (List<HeadersModel> headers in HeaderVariants(url))
            variants.Add(headers);

        foreach (List<HeadersModel> headers in variants)
        {
            try
            {
                var probe = await Http.BaseGet(
                    url,
                    timeoutSeconds: 15,
                    headers: headers,
                    statusCodeOK: false,
                    MaxResponseContentBufferSize: 1_000_000,
                    proxy: proxy,
                    useDefaultHeaders: false
                );

                int status = (int)(probe.response?.StatusCode ?? 0);
                string body = probe.content ?? string.Empty;
                string trim = body.TrimStart();
                Console.WriteLine($"VidLink: playlist {HostOf(url)} {status} {PreviewOf(trim)}");

                if (status is not (200 or 206))
                    continue;

                if (!IsExtM3u(trim) && trim.StartsWith('{'))
                {
                    try
                    {
                        var root = JObject.Parse(trim);
                        string inner = FirstString(root, "playlist", "url", "file", "src")
                            ?? FirstString(root["stream"] as JObject, "playlist", "url", "file", "src");
                        if (!string.IsNullOrWhiteSpace(inner) &&
                            !string.Equals(inner, url, StringComparison.OrdinalIgnoreCase) &&
                            Uri.TryCreate(inner, UriKind.Absolute, out _))
                        {
                            Console.WriteLine($"VidLink: playlist json→{HostOf(inner)}");
                            return await FetchPlaylist(inner);
                        }
                    }
                    catch
                    {
                    }
                }

                if (!IsExtM3u(trim))
                    continue;

                string finalUrl = StripHash(probe.response?.RequestMessage?.RequestUri?.AbsoluteUri ?? url);
                RememberHeaders(url, headers);
                RememberHeaders(finalUrl, headers);
                return (body, finalUrl, headers);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VidLink: playlist {HostOf(url)} ex {ex.GetType().Name}");
            }
        }

        return default;
    }

    string RewritePlaylist(string playlist, string sourceUrl, List<HeadersModel> headers)
    {
        var output = new StringBuilder(playlist.Length * 2);
        var baseUri = new Uri(sourceUrl);
        bool nextIsPlaylist = false;

        foreach (string rawLine in playlist.Replace("\r", string.Empty).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                output.Append('\n');
                continue;
            }

            if (line.StartsWith('#') )
            {
                if (line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
                    nextIsPlaylist = true;

                output.Append(RewriteTagUris(rawLine, baseUri, headers));
                output.Append('\n');
                continue;
            }

            bool nested = nextIsPlaylist || LooksLikeHls(line);
            nextIsPlaylist = false;
            output.Append(MapUri(baseUri, line, headers, nested)).Append('\n');
        }

        return output.ToString();
    }

    string RewriteTagUris(string line, Uri baseUri, List<HeadersModel> headers)
    {
        if (!line.Contains("URI=", StringComparison.OrdinalIgnoreCase))
            return line;

        bool key = line.StartsWith("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("#EXT-X-SESSION-KEY", StringComparison.OrdinalIgnoreCase) ||
                   line.StartsWith("#EXT-X-MAP", StringComparison.OrdinalIgnoreCase);

        return Regex.Replace(line, @"URI=""([^""]+)""", match =>
        {
            string value = match.Groups[1].Value;
            bool playlist = !key && (LooksLikeHls(value) ||
                line.StartsWith("#EXT-X-MEDIA", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("#EXT-X-I-FRAME", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase));
            return $"URI=\"{MapUri(baseUri, value, headers, playlist)}\"";
        }, RegexOptions.IgnoreCase);
    }

    string MapUri(Uri baseUri, string value, List<HeadersModel> headers, bool playlist)
    {
        if (!Uri.TryCreate(baseUri, value, out Uri uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return value;
        }

        return playlist
            ? PlaylistLink(uri.AbsoluteUri)
            : MediaLink(uri.AbsoluteUri);
    }

    string PlaylistLink(string source)
    {
        return accsArgs($"{host}/lite/vidlink/playlist.m3u8?uri={HttpUtility.UrlEncode(EncryptQuery(source))}");
    }

    string MediaLink(string source, string ext = null)
    {
        if (ext == null)
        {
            if (LooksLikeProgressive(source))
                ext = ".mp4";
            else if (source.Contains(".m4s", StringComparison.OrdinalIgnoreCase))
                ext = ".m4s";
            else
                ext = ".ts";
        }

        return accsArgs($"{host}/lite/vidlink/media{ext}?uri={HttpUtility.UrlEncode(EncryptQuery(source))}");
    }

    async Task<ActionResult> PipeMedia(string url)
    {
        var variants = new List<List<HeadersModel>>();
        string hostKey = $"vidlink:hdr:{HostOf(url)}";
        if (hybridCache.TryGetValue(hostKey, out List<HeadersModel> remembered) && remembered?.Count > 0)
            variants.Add(remembered);

        foreach (List<HeadersModel> headers in HeaderVariants(url))
            variants.Add(headers);

        foreach (List<HeadersModel> headers in variants)
        {
            var client = FriendlyHttp.MessageClient(
                "base",
                Http.HandlerOrNull(url, proxy),
                out bool dispose,
                allowAutoRedirect: true
            );

            HttpResponseMessage resp = null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                Http.DefaultRequestHeaders(url, req, null, null, headers, useDefaultHeaders: false);

                if (Request.Headers.TryGetValue("Range", out var range) && range.Count > 0)
                    req.Headers.TryAddWithoutValidation("Range", range.ToString());

                resp = await client.SendAsync(
                    req,
                    HttpCompletionOption.ResponseHeadersRead,
                    HttpContext.RequestAborted
                );

                int status = (int)resp.StatusCode;
                Console.WriteLine($"VidLink: media {HostOf(url)} {status}");
                if (status is not (200 or 206))
                    continue;

                RememberHeaders(url, headers);
                string finalUrl = StripHash(resp.RequestMessage?.RequestUri?.AbsoluteUri ?? url);
                RememberHeaders(finalUrl, headers);

                await using var input = await resp.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
                byte[] peek = new byte[512];
                int n = await input.ReadAsync(peek.AsMemory(0, peek.Length), HttpContext.RequestAborted);
                string head = n > 0 ? Encoding.ASCII.GetString(peek, 0, n).TrimStart() : string.Empty;

                if (IsExtM3u(head))
                {
                    using var ms = new System.IO.MemoryStream();
                    if (n > 0)
                        ms.Write(peek, 0, n);
                    await input.CopyToAsync(ms, HttpContext.RequestAborted);
                    string body = Encoding.UTF8.GetString(ms.ToArray());
                    string rewritten = RewritePlaylist(body, finalUrl, headers);
                    Console.WriteLine($"VidLink: media {HostOf(url)} was hls, rewrite");
                    return ContentTo(rewritten, "application/vnd.apple.mpegurl; charset=utf-8");
                }

                string ct = resp.Content.Headers.ContentType?.ToString();
                if (string.IsNullOrWhiteSpace(ct) ||
                    ct.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                    ct.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                    ct.Contains("mpegurl", StringComparison.OrdinalIgnoreCase))
                {
                    ct = LooksLikeProgressive(url) || LooksLikeMp4(head) ? "video/mp4" : "video/mp2t";
                }

                Response.StatusCode = status;
                Response.ContentType = ct;
                Response.Headers["Accept-Ranges"] = "bytes";

                if (resp.Content.Headers.ContentLength is long length && n >= 0)
                    Response.ContentLength = length;

                if (resp.Content.Headers.TryGetValues("Content-Range", out var contentRange))
                    Response.Headers["Content-Range"] = string.Join(", ", contentRange);

                if (n > 0)
                    await Response.Body.WriteAsync(peek.AsMemory(0, n), HttpContext.RequestAborted);
                await input.CopyToAsync(Response.Body, HttpContext.RequestAborted);
                return new EmptyResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VidLink: media {HostOf(url)} ex {ex.GetType().Name}");
            }
            finally
            {
                resp?.Dispose();
                if (dispose)
                    client.Dispose();
            }
        }

        return OnError("media", refresh_proxy: true);
    }

    void RememberHeaders(string url, List<HeadersModel> headers)
    {
        if (string.IsNullOrWhiteSpace(url) || headers == null || headers.Count == 0)
            return;

        hybridCache.Set($"vidlink:hdr:{HostOf(url)}", headers, cacheTime(20));
    }

    IEnumerable<List<HeadersModel>> HeaderVariants(string url)
    {
        // CSX default CDN headers are filmboom.top, not vidlink.pro.
        yield return HeadersModel.Init(
            ("User-Agent", CsUserAgent),
            ("Referer", CdnOrigin + "/"),
            ("Origin", CdnOrigin),
            ("Accept", "*/*")
        );

        yield return HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", CdnOrigin + "/"),
            ("Origin", CdnOrigin),
            ("Accept", "*/*")
        );

        yield return HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", CdnOrigin + "/"),
            ("Accept", "*/*")
        );

        yield return HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", PlayerOrigin + "/"),
            ("Accept", "*/*")
        );

        yield return HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", PlayerOrigin + "/"),
            ("Origin", PlayerOrigin),
            ("Accept", "*/*")
        );

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
        {
            string origin = uri.GetLeftPart(UriPartial.Authority);
            yield return HeadersModel.Init(
                ("User-Agent", Http.UserAgent),
                ("Referer", origin + "/"),
                ("Origin", origin),
                ("Accept", "*/*")
            );
        }

        yield return HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "*/*")
        );
    }

    void Collect(JToken token, List<ResolvedStream> result, HashSet<string> seen, int depth)
    {
        if (token == null || depth > 8)
            return;

        if (token is JArray array)
        {
            foreach (JToken item in array)
                Collect(item, result, seen, depth + 1);
            return;
        }

        if (token is not JObject obj)
            return;

        string type = FirstString(obj, "type");
        string label = FirstString(obj, "quality", "label", "title", "name", "language", "lang", "server");

        if (obj["qualities"] is JObject qualities)
        {
            foreach (JProperty quality in qualities.Properties())
            {
                if (quality.Value is not JObject qobj)
                    continue;

                string qurl = FirstString(qobj, "url", "file", "playlist", "src");
                string qtype = FirstString(qobj, "type") ?? type;
                TryAdd(
                    qurl,
                    string.IsNullOrWhiteSpace(label) ? quality.Name : $"{label} {quality.Name}",
                    result,
                    seen,
                    qtype,
                    ReadHeaders(qobj)
                );
            }
        }

        string url = FirstString(obj, "playlist", "file", "url", "src", "link");
        TryAdd(url, label, result, seen, type, ReadHeaders(obj));

        foreach (JProperty property in obj.Properties())
        {
            string name = property.Name;
            if (name is "captions" or "subtitles" or "subtitle" or "flags")
                continue;

            if (property.Value is JObject or JArray)
                Collect(property.Value, result, seen, depth + 1);
        }
    }

    void TryAdd(string url, string label, List<ResolvedStream> result, HashSet<string> seen, string type, List<HeadersModel> headers = null)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
            uri.Scheme is not ("http" or "https") ||
            !seen.Add(url))
        {
            return;
        }

        bool hls = IsHls(type, url);
        if (!hls && !LooksLikeProgressive(url) && !LooksLikeHls(url) && result.Count > 0)
            return;

        if (!hls && !LooksLikeProgressive(url) && !LooksLikeHls(url))
            return;

        string quality = string.IsNullOrWhiteSpace(label) ? GuessQuality(url) : Compact(label);
        if (string.IsNullOrWhiteSpace(quality))
            quality = hls ? "HLS" : "MP4";

        if (result.Exists(i => i.Label.Equals(quality, StringComparison.OrdinalIgnoreCase)))
            quality += $" #{result.Count + 1}";

        result.Add(new ResolvedStream(url, quality, headers?.Count > 0 ? headers : StreamHeaders(), hls));
    }

    static List<HeadersModel> ReadHeaders(JObject obj)
    {
        if (obj?["headers"] is not JObject map)
            return null;

        var list = new List<HeadersModel>();
        foreach (JProperty property in map.Properties())
        {
            if (property.Value?.Type != JTokenType.String)
                continue;

            string value = property.Value.Value<string>();
            if (!string.IsNullOrWhiteSpace(property.Name) && !string.IsNullOrWhiteSpace(value))
                list.Add(new HeadersModel(property.Name, value));
        }

        return list.Count > 0 ? list : null;
    }

    static string FirstString(JObject obj, params string[] names)
    {
        if (obj == null)
            return null;

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

    static bool IsHls(string type, string url)
    {
        if (!string.IsNullOrWhiteSpace(type))
        {
            string value = type.Trim().ToLowerInvariant();
            if (value is "hls" or "m3u8" or "m3u" or "mpegurl")
                return true;
            if (value is "file" or "mp4" or "mkv" or "webm" or "dash" or "mpd")
                return false;
            // "movie" / "tv" / "srt" are metadata, not stream containers.
        }

        if (LooksLikeHls(url))
            return true;

        // CSX: only type m3u8 or URL .m3u8 is HLS; otherwise INFER (probe).
        return false;
    }

    static bool LooksLikeHls(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return url.Contains(".m3u", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("m3u8", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/hls", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("mpegurl", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeProgressive(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        string path = url;
        int cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
            path = path[..cut];

        return path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase);
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

    static string HostOf(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? uri.Host : url;
    }

    static string StripHash(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        int hash = url.IndexOf('#');
        return hash >= 0 ? url[..hash] : url;
    }

    static bool IsHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
           && uri.Scheme is "http" or "https"
           && !string.IsNullOrWhiteSpace(uri.Host);

    static bool IsExtM3u(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        string trim = body.TrimStart();
        return trim.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase) ||
               trim.StartsWith("#EXT-X-", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeMp4(string body)
    {
        if (string.IsNullOrEmpty(body) || body.Length < 8)
            return false;

        int n = Math.Min(body.Length, 32);
        int i = body.IndexOf("ftyp", 0, n, StringComparison.Ordinal);
        return i is >= 0 and <= 8;
    }

    static string PreviewOf(string body)
    {
        if (string.IsNullOrEmpty(body))
            return "-";

        string text = Regex.Replace(body.Trim(), @"\s+", " ");
        return text.Length > 80 ? text[..80] : text;
    }

    List<HeadersModel> FileHeaders()
        => StreamHeaders();

    List<HeadersModel> ApiHeaders()
    {
        return HeadersModel.Init(
            ("User-Agent", CsUserAgent),
            ("Connection", "keep-alive"),
            ("Referer", PlayerOrigin + "/"),
            ("Origin", PlayerOrigin)
        );
    }

    List<HeadersModel> StreamHeaders()
    {
        return HeadersModel.Init(
            ("User-Agent", CsUserAgent),
            ("Referer", CdnOrigin + "/"),
            ("Origin", CdnOrigin),
            ("Accept", "*/*")
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
                result.headers ?? StreamHeaders(),
                IsHls(null, result.m3u8)
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
