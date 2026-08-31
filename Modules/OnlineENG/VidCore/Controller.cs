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
    public async Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        var res = await ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");

        // Stage thu thập (collection) trước đây hoàn toàn mù: ViewTmdbAsync trả về
        // badInitMsg/RedirectResult mà không in gì, nên "nguồn hiện mà không có kết quả"
        // không phân biệt được là bị chặn, redirect, hay thiếu id.
        if (res is null or RedirectResult || (res as ContentResult)?.StatusCode > 200)
            Console.WriteLine($"VidCore: index không có dữ liệu (type={res?.GetType().Name ?? "null"}, status={(res as ContentResult)?.StatusCode?.ToString() ?? "-"}, id={id}, tmdb_id={tmdb_id}, serial={serial})");

        return res;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/vidcore/video")]
    [Route("lite/vidcore/video.m3u8")]
    public async Task<ActionResult> Video(long id, short s = -1, short e = -1, bool play = false)
    {
        StatiCacheDisabled = true;
        SetHeadersNoCache();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
        {
            // Đường này trước đây im lặng 100%: 403 mà không có dòng log nào, nên
            // "nguồn hiện mà bấm không ra gì" không thể chẩn đoán được.
            Console.WriteLine($"VidCore: blocked (enable={init.enable}, rip={init.rip})");
            return badInitMsg ?? OnError("disable", gbcache: false, statusCode: 403);
        }

        if (id <= 0)
        {
            // Lampa không truyền lên tmdb id -> không có gì để resolve. Cũng im lặng.
            Console.WriteLine("VidCore: id<=0 (không có TMDB id trong request)");
            return OnError();
        }

        List<ResolvedStream> resolved;
        try
        {
            resolved = await Resolve(id, s, e);
        }
        catch (Exception ex)
        {
            // Không được để nổ: Lampac sẽ trả 500 rỗng và không lại dấu vết gì ở stdout.
            Console.WriteLine($"VidCore: ex {ex.GetType().Name} {ex.Message}");
            return OnError("resolve", 502);
        }

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
        // GET trang player PHẢI là request kiểu trình duyệt. Nếu gửi kèm
        // `X-Requested-With: XMLHttpRequest` / `Accept: application/json` (headers của
        // bước enc-dec) thì vidcore.io không trả HTML chứa \"en\" nữa -> module báo
        // "token not found" dù mở link bằng browser vẫn thấy đúng tập. CSX cũng
        // app.get(baseUrl) trần, chỉ các POST enc-dec mới mang header XHR.
        var pageGet = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("Accept-Language", "en-US,en;q=0.9")
        );

        string watchUrl = season > 0
            ? $"{host}/tv/{tmdbId}/{season}/{Math.Max(episode, (short)1)}"
            : $"{host}/movie/{tmdbId}";

        string html = await httpHydra.Get(watchUrl, addheaders: pageGet, statusCodeOK: false);
        if (string.IsNullOrWhiteSpace(html))
        {
            Console.WriteLine($"VidCore: watch page empty ({mediaType}:{tmdbId}) {watchUrl}");
            return null;
        }

        string encrypted = ExtractEncrypted(html);
        if (string.IsNullOrWhiteSpace(encrypted))
        {
            Console.WriteLine($"VidCore: token not found in {watchUrl} | {HtmlDiag(html)}");

            // Nếu token không nằm trong trang này mà ở một document khác, thử 1 hop nữa.
            foreach (string embed in EmbedCandidates(html))
            {
                string inner = await httpHydra.Get(embed, addheaders: pageGet, statusCodeOK: false);
                encrypted = ExtractEncrypted(inner);
                if (!string.IsNullOrWhiteSpace(encrypted))
                {
                    Console.WriteLine($"VidCore: token lấy từ embed {embed}");
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(encrypted))
                return null;
        }

        // Từ đây về sau là các request tới enc-dec (XHR + CSRF), không phải trang player.
        var headers = PageHeaders(null);

        string encRaw = await httpHydra.Get(
            $"{api}{DecryptRouteEnc}?text={Uri.EscapeDataString(encrypted)}",
            addheaders: headers,
            statusCodeOK: false);

        JToken encParsed = ParseJson(encRaw);
        JToken encResult = Unwrap(Child(encParsed, "result") ?? encParsed);
        string serversUrl = Text(encResult, "servers");
        string streamBase = Text(encResult, "stream");
        string csrf = Text(encResult, "token");

        if (string.IsNullOrWhiteSpace(serversUrl) || string.IsNullOrWhiteSpace(streamBase))
        {
            Console.WriteLine($"VidCore: enc-vidcore incomplete ({mediaType}:{tmdbId}) resp={Preview(encRaw)}");
            return null;
        }

        headers = PageHeaders(csrf);   // từ đây mọi request mang luôn CSRF token vừa lấy được

        // Danh sách server bị mã hoá: POST lấy ciphertext rồi đưa sang dec-vidcore.
        string serversCipher = await PostCipher(serversUrl, headers);
        if (string.IsNullOrWhiteSpace(serversCipher))
        {
            Console.WriteLine($"VidCore: servers POST empty ({mediaType}:{tmdbId}) — thử đổi headers/apihost");
            return null;
        }

        // Ciphertext phải là chuỗi/JSON; nếu đây là HTML thì dec-vidcore sẽ trả rỗng
        // và ta chỉ còn "no servers" vô nghĩa — nên nói rõ ngay ở đây.
        string probe = serversCipher.TrimStart();
        if (probe.StartsWith('<') || probe.Contains("<html", StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"VidCore: servers POST trả HTML, không phải ciphertext ({mediaType}:{tmdbId}) resp={Preview(serversCipher)}");

        List<JToken> servers = await DecryptList(serversCipher, api, headers, "servers");
        if (servers.Count == 0)
        {
            Console.WriteLine($"VidCore: no servers ({mediaType}:{tmdbId}) cipher={Preview(serversCipher)}");
            return null;
        }

        Console.WriteLine($"VidCore: {servers.Count} servers ({mediaType}:{tmdbId})");

        // Mỗi server một luồng — fan-out song song, con chết không kéo cả dãy.
        async Task<ResolvedStream> FetchServer(JToken server)
        {
            string name = Text(server, "name");

            try
            {
                string data = Text(server, "data", "id", "cid");
                if (string.IsNullOrWhiteSpace(data))
                {
                    Console.WriteLine($"VidCore: {Show(name)} no data field");
                    return null;
                }

                string streamCipher = await PostCipher($"{streamBase}/{data}", headers);
                if (string.IsNullOrWhiteSpace(streamCipher))
                {
                    Console.WriteLine($"VidCore: {Show(name)} stream payload empty");
                    return null;
                }

                string decRaw = await httpHydra.Post(
                    $"{api}{DecryptRouteDec}",
                    JsonConvert.SerializeObject(new { text = streamCipher }),
                    addheaders: headers,
                    statusCodeOK: false);

                string m3u8 = Pick(ParseJson(decRaw), "url", "stream_url", "file", "src");
                if (string.IsNullOrWhiteSpace(m3u8) || !IsHttpUrl(m3u8))
                {
                    Console.WriteLine($"VidCore: {Show(name)} no url, dec={Preview(decRaw)}");
                    return null;
                }

                Console.WriteLine($"VidCore: {Show(name)} ok");
                return new ResolvedStream(m3u8, $"VidCore · {Show(name)}", headers);
            }
            catch (Exception ex)
            {
                // Một server hỏng không được kéo cả Task.WhenAll.
                Console.WriteLine($"VidCore: {Show(name)} ex {ex.GetType().Name}");
                return null;
            }
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
    /// CSX gọi các endpoint này bằng `app.post(url, headers=…)` — tức POST không body.
    /// Đo thực tế trên thiết bị (2026-08-31): body rỗng không trả gì, phải gửi `{}`
    /// mới nhận 1916 ký tự ciphertext. Nên thử `{}` trước
    /// để đỡ mất một round-trip cho từng server; giữ body rỗng làm fallback cho build cũ.
    /// </summary>
    async Task<string> PostCipher(string url, List<HeadersModel> headers)
    {
        string body = await httpHydra.Post(url, "{}", addheaders: headers, statusCodeOK: false);
        if (!string.IsNullOrWhiteSpace(body))
            return body;

        return await httpHydra.Post(url, "", addheaders: headers, statusCodeOK: false);
    }

    static readonly string[] EncryptedKeys = ["en", "token", "enc", "text", "cipher", "data"];

    /// <summary>
    /// VidCore nhét token vào trong một chuỗi JS đã escape, nên phải chấp nhận
    /// \\"en\\":\\"...\\" lẫn bản thường "en":"...". Quét trên cả bản gốc lẫn bản đã gỡ
    /// backslash, cho phép 0..n backslash quanh dấu nháy, và bắt giá trị >= 20 ký tự
    /// để không ăn nhầm mấy key ngôn ngữ kiểu "en":"English".
    /// </summary>
    static string ExtractEncrypted(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        string keys = string.Join("|", EncryptedKeys);
        string pattern = $@"\\*""(?:{keys})\\*""\s*:\s*\\*""([^""\r\n]{{20,}})";

        foreach (string hay in new[] { html, html.Replace("\\", "") })
            foreach (Match m in Regex.Matches(hay, pattern))
            {
                string value = m.Groups[1].Value.Trim();

                // ciphertext không chứa tag HTML; đó là dấu hiệu regex ăn xuyên markup
                if (value.Length < 20 || value.Contains('<') || value.Contains("</"))
                    continue;

                return value.TrimEnd('\\');
            }

        return null;
    }

    /// <summary>Mô tả ngắn thứ vừa nhận được, để lần sau khỏi phải đoán.</summary>
    static string HtmlDiag(string html)
    {
        string kind = html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
                      html.Contains("cf-chl", StringComparison.OrdinalIgnoreCase) ? "anti-bot" :
                      html.Contains("<html", StringComparison.OrdinalIgnoreCase) ? "html" : "not-html";

        return $"len={html.Length}, {kind}, head={Preview(html)}";
    }

    /// <summary>Nguồn iframe/embed, dùng khi token không nằm ngay trong trang.</summary>
    static IEnumerable<string> EmbedCandidates(string html)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html, @"(?i)<(?:iframe|embed)[^>]+src\s*=\s*[""'](?<u>[^""']+)[""']"))
        {
            string url = m.Groups["u"].Value;
            if (url.StartsWith("//"))
                url = "https:" + url;

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || !seen.Add(url))
                continue;

            yield return url;

            if (seen.Count >= 2)
                yield break;
        }
    }

    /// <summary>
    /// enc-dec trả `result` khi là JSON object/array thật, khi là một CHUỖI JSON đã
    /// escape (tuỳ build endpoint). Đọc Value&lt;T&gt;(key) trên JValue sẽ nổ, và chuỗi
    /// không parse thì bị đếm thành "no servers" giả — nên chuẩn hoá ở đây một lần.
    /// </summary>
    static JToken Unwrap(JToken token)
    {
        if (token is JValue value && value.Value is string text && !string.IsNullOrWhiteSpace(text))
        {
            char first = '\0';
            foreach (char c in text)
                if (!char.IsWhiteSpace(c))
                {
                    first = c;
                    break;
                }

            if (first is '[' or '{')
            {
                try
                {
                    return JToken.Parse(text);
                }
                catch (JsonReaderException)
                {
                }
            }
        }

        return token;
    }

    async Task<List<JToken>> DecryptList(string cipher, string api, List<HeadersModel> headers, string what)
    {
        if (string.IsNullOrWhiteSpace(cipher))
            return [];

        string decRaw = await httpHydra.Post(
            $"{api}{DecryptRouteDec}",
            JsonConvert.SerializeObject(new { text = cipher }),
            addheaders: headers,
            statusCodeOK: false);

        List<JToken> list = ExtractList(ParseJson(decRaw));

        if (list.Count == 0)
            Console.WriteLine($"VidCore: {what} dec rỗng, resp={Preview(decRaw)}");

        return list;
    }

    /// <summary>
    /// dec-vidcore đổi shape giữa các build: `{"result":[...]}`, `[...]` ở gốc,
    /// `{"result":"<json dạng chuỗi>"}`, `{"result":{"servers":[...]}}`, và có khi
    /// `{"result":{"0":{...},"1":{...}}}`. Đổ hết về một List ở đây, thay vì chỉ nhận
    /// đúng một hình rồi báo "no servers" giả như trước.
    /// </summary>
    static List<JToken> ExtractList(JToken root, int depth = 0)
    {
        var list = new List<JToken>();
        if (root == null || depth > 3)
            return list;

        root = Unwrap(root);

        if (root is JArray arr)
        {
            foreach (JToken item in arr)
                list.Add(Unwrap(item) ?? item);

            return list;
        }

        if (root is JObject obj)
        {
            // object rỗng hoặc lỗi -> không có gì
            if (!obj.HasValues)
                return list;

            if (obj["result"] is JToken inner && inner.Type != JTokenType.Null)
                return ExtractList(inner, depth + 1);

            if (obj["servers"] is JToken servers && servers.Type != JTokenType.Null)
                return ExtractList(servers, depth + 1);

            // { "0": {...}, "1": {...} }
            var children = obj.Properties()
                .Where(p => p.Value is JObject || p.Value is JArray)
                .Select(p => p.Value)
                .ToList();

            if (children.Count > 1 && obj["url"] == null && obj["name"] == null)
            {
                foreach (JToken child in children)
                    list.AddRange(ExtractList(child, depth + 1));

                return list;
            }

            // `{"error":"..."}` / token hết hạn: coi như không có gì để log line
            // "dec rỗng, resp=…" in ra nguyên văn, thay vì trả một object rác.
            if (obj["error"] != null)
                return list;

            list.Add(obj);
            return list;
        }

        return list;
    }

    #region json-safe helpers
    /// <summary>
    /// Lampac parse JSON hộ bằng Newtonsoft theo kiểu mạnh (Get&lt;JObject&gt;), nên chỉ cần
    /// enc-dec trả về mảng/chuỗi thay vì object là request nổ 500 rỗng, không để lại
    /// dấu vết. Vì vậy module đọc text thô rồi tự parse ở đây, mọi bước đều trả null an toàn.
    /// </summary>
    static JToken ParseJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JToken.Parse(raw);
        }
        catch (JsonReaderException)
        {
            return null;
        }
    }

    /// <summary>Lấy token con mà không nổ khi token đang là mảng/chuỗi.</summary>
    static JToken Child(JToken token, string name)
        => token is JObject obj ? obj[name] : null;

    /// <summary>Chuỗi ở key đầu tiên có thật; bỏ qua object/array để tránh InvalidCastException.</summary>
    static string Text(JToken token, params string[] keys)
    {
        if (token is not JObject obj)
            return null;

        foreach (string key in keys)
            if (obj[key] is JValue value && value.Value is string str && !string.IsNullOrWhiteSpace(str))
                return str;

        return null;
    }

    /// <summary>
    /// URL stream có thể nằm ở result.url / result.stream_url / hoặc result là chính chuỗi URL.
    /// Quét thêm một lớp mảng để không mất cả dãy chỉ vì khác shape.
    /// </summary>
    static string Pick(JToken decrypted, params string[] keys)
    {
        JToken node = Unwrap(Child(decrypted, "result") ?? decrypted);
        if (node == null)
            return null;

        string url = Text(node, keys);
        if (!string.IsNullOrWhiteSpace(url))
            return url;

        if (node is JValue value && value.Value is string text)
            return text.Trim().Trim('"');

        if (node is JArray arr)
        {
            foreach (JToken item in arr)
            {
                url = Text(item, keys);
                if (!string.IsNullOrWhiteSpace(url))
                    return url;
            }
        }

        return null;
    }

    static string Preview(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "<empty>";

        raw = raw.Replace('\r', ' ').Replace('\n', ' ');
        return raw.Length <= 180 ? raw : raw[..180] + "...";
    }

    static string Show(string name)
        => string.IsNullOrWhiteSpace(name) ? "server" : name;
    #endregion

    static bool IsHttpUrl(string uri) =>
        !string.IsNullOrWhiteSpace(uri) && Uri.TryCreate(uri, UriKind.Absolute, out Uri u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
    #endregion
}
