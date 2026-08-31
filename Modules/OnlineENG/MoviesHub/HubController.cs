using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Online.Settings;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MoviesHub;

/// <summary>
/// Lớp dùng chung cho MoviesDrive + Movies4U. Hai nguồn này chỉ khác nhau ở bước
/// "tìm link trong trang của họ"; còn lại (file-host -> URL chơi được, headers, proxy,
/// cache, log) là một, nên để ở đây thay vì nhân bản.
///
/// Định luật của nhóm file-host này: link cuối có thể là
///   https://hubcloud.foo|cx|…/drive/&lt;id&gt;        (file page, phải lấy nốt nút Download)
///   https://hubcloud.*/drive/search-recover.php?… (trang tìm tên file -> 1 hop nữa)
///   https://drive.google.com/file/d/&lt;id&gt;/view      (Google Drive -> đổi sang usercontent)
///   https://hubcdn.*/…&lt;named&gt;.mkv                    (thẳng, hiếm, nhưng có)
/// Nên resolver chấp nhận cả bốn, và KHÔNG im lặng khi fail: mỗi bước đều log độ dài
/// và 300 ký tự đầu của trang vừa nhận.
/// </summary>
public abstract class HubController : BaseENGController
{
    protected sealed record HubStream(string Url, string Label, List<HeadersModel> Headers);

    /// <summary>Nhãn nguồn cho log/key cache — do controller cụ thể truyền xuống.</summary>
    protected HubController(OnlinesSettings conf) : base(conf)
    {
    }

    #region video shell (dùng chung)
    protected async Task<ActionResult> VideoCore(string source, long id, string imdb_id, short s, short e, bool play)
    {
        StatiCacheDisabled = true;
        SetHeadersNoCache();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
        {
            Console.WriteLine($"{Log(source)} blocked (enable={init.enable}, rip={init.rip})");
            return badInitMsg ?? OnError("disable", gbcache: false, statusCode: 403);
        }

        List<HubStream> streams;
        try
        {
            streams = await Resolve(source, id, imdb_id, s, e);
        }
        catch (Exception ex)
        {
            // Không được để nổ ra ngoài: Lampac sẽ trả 500 rỗng và không còn dấu vết gì.
            Console.WriteLine($"{Log(source)} ex {ex.GetType().Name} {ex.Message}");
            return OnError("resolve", 502);
        }

        if (streams == null || streams.Count == 0)
        {
            Console.WriteLine($"{Log(source)} không có stream nào ({(s > 0 ? "tv" : "movie")}:{id})");
            return OnError("stream", 502);
        }

        var qualities = new StreamQualityTpl(streams.Count);
        foreach (HubStream item in streams)
            qualities.Append(HostStreamProxy(item.Url, headers: item.Headers), item.Label);

        if (qualities.IsEmpty)
            return OnError("stream", 502);

        var first = qualities.Firts();
        Console.WriteLine($"{Log(source)} play {first.link}");

        if (play)
            return RedirectToPlay(first.link);

        // VideoTpl.ToJson(method, url, TITLE, ...): arg thứ 3 là title chứ không phải
        // quality — quality là key riêng, để Lampa hiện đúng 4K/1080p trên nút play.
        return ContentTo(VideoTpl.ToJson(
            "play",
            first.link,
            "English",
            streamquality: qualities,
            quality: first.quality,
            vast: init.vast,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        ));
    }

    static string Log(string source) => $"{source}: ";

    async Task<List<HubStream>> Resolve(string source, long id, string imdb_id, short s, short e)
    {
        string memKey = $"movieshub:{source}:{id}:{imdb_id}:{s}:{e}";

        if (hybridCache.TryGetValue(memKey, out List<HubStream> cached) && cached?.Count > 0)
            return cached;

        List<(string Label, string Url)> links = await FindLinks(source, imdb_id, id, s, e);

        if (links == null || links.Count == 0)
        {
            Console.WriteLine($"{Log(source)} không tìm thấy link nào trên trang nguồn");
            return null;
        }

        links = links.Take(8).ToList();
        Console.WriteLine($"{Log(source)} {links.Count} link nguồn ({(s > 0 ? "tv" : "movie")}:{id})");

        // File-host trả rỗng khi bị 5 request cùng lúc (bài học từ VidCore) -> hãm 2, có retry.
        using System.Threading.SemaphoreSlim gate = new(2);

        async Task<List<HubStream>> Guarded((string Label, string Url) link)
        {
            await gate.WaitAsync();
            try
            {
                return await ResolveHub(link.Url, link.Label, source);
            }
            finally
            {
                gate.Release();
            }
        }

        var answered = await Task.WhenAll(links.Select(Guarded));

        var streams = new List<HubStream>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (List<HubStream> part in answered)
        {
            if (part == null)
                continue;

            foreach (HubStream item in part)
                if (seen.Add(item.Url))
                    streams.Add(item);
        }

        Console.WriteLine($"{Log(source)} {streams.Count}/{links.Count} link giải được");

        if (streams.Count == 0)
            return null;

        hybridCache.Set(memKey, streams, cacheTime(15));
        return streams;
    }
    #endregion

    #region hubcloud / gdrive resolver
    /// <summary>
    /// Một link nguồn -> 0..n URL chơi được. depth dùng cho trang search-recover của
    /// HubCloud (nó liệt kê file, chưa phải file).
    /// </summary>
    async Task<List<HubStream>> ResolveHub(string url, string label, string source, int depth = 0)
    {
        var found = new List<HubStream>();
        if (string.IsNullOrWhiteSpace(url))
            return found;

        url = url.Trim();

        // 1) Google Drive: không cần xin trang, id nằm ngay trong URL.
        List<HubStream> gdrive = FromGoogleDrive(url, label);
        if (gdrive.Count > 0)
        {
            Console.WriteLine($"{Log(source)} gdrive trực tiếp {gdrive.Count}");
            return gdrive;
        }

        string html = await GetPage(url);
        if (string.IsNullOrWhiteSpace(html))
        {
            Console.WriteLine($"{Log(source)} trang rỗng {Cut(url)}");
            return found;
        }

        // 2) URL media nằm thẳng trong trang (hubcdn/…mkv, .m3u8) — ưu tiên nhất.
        foreach (Match m in Regex.Matches(html, @"https?://[^""'\s<>]+?\.(?:mkv|mp4|m4v|mov|avi|ts|m3u8)(?:\?[^""'\s<>]*)?", RegexOptions.IgnoreCase))
        {
            string media = Unescape(m.Value);
            if (media.Length < 20)
                continue;

            found.Add(new HubStream(media, QualityLabel(label, media), StreamHeaders(url)));
        }

        // 3) Nút download của HubCloud: /drive/download/<id>/<tên file>
        if (found.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, @"href=""(?<u>[^""]*?/drive/download/[^""]+)""", RegexOptions.IgnoreCase))
                found.Add(new HubStream(Absolute(m.Groups["u"].Value, url), QualityLabel(label, m.Groups["u"].Value), StreamHeaders(url)));
        }

        if (found.Count > 0)
            return found.DistinctBy(i => i.Url).ToList();

        // 4) Drive link nằm trong trang (nhiều post nhét gdrive ở nút thứ hai)
        found.AddRange(FromGoogleDrive(html, label));
        if (found.Count > 0)
        {
            Console.WriteLine($"{Log(source)} lấy được gdrive từ trong trang {Cut(url)}");
            return found;
        }

        // 5) Trang tìm kiếm của HubCloud -> vào file đầu tiên, thêm 1 hop nữa
        if (depth == 0)
        {
            var next = Regex.Matches(html, @"href=""(?<u>[^""]*?/drive/[A-Za-z0-9_-]{6,})""", RegexOptions.IgnoreCase)
                             .Select(m => Absolute(m.Groups["u"].Value, url))
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Take(3);

            foreach (string file in next)
            {
                found.AddRange(await ResolveHub(file, label, source, depth + 1));
                if (found.Count > 0)
                    break;
            }
        }

        if (found.Count == 0)
            Console.WriteLine($"{Log(source)} không extract được link | {Cut(url)} len={html.Length}, head={Preview(html)}");

        return found.DistinctBy(i => i.Url).ToList();
    }

    /// <summary>drive.google.com/file/d/&lt;id&gt;/view -> link tải trực tiếp (không cần cookie với file public).</summary>
    static List<HubStream> FromGoogleDrive(string text, string label)
    {
        var list = new List<HubStream>();
        if (string.IsNullOrWhiteSpace(text))
            return list;

        foreach (Match m in Regex.Matches(text, @"drive(?:\.google|\.usercontent\.google)\.com/[^\s""'<>]*(?:/file/d/|/uc\?|id=)(?<id>[01][0-9A-Za-z_-]{20,})"))
        {
            string id = m.Groups["id"].Value;
            if (list.Any(i => i.Url.Contains(id)))
                continue;

            list.Add(new HubStream(
                $"https://drive.usercontent.google.com/download?id={id}&export=download&confirm=t",
                QualityLabel(label, id),
                HeadersModel.Init(("User-Agent", Http.UserAgent), ("Accept", "*/*"))));
        }

        return list;
    }

    List<HeadersModel> StreamHeaders(string fromUrl)
        => HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Referer", OriginOf(fromUrl)),
            ("Accept", "*/*"));

    static string OriginOf(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri u))
            return $"{u.Scheme}://{u.Authority}/";

        return "https://hubcloud.cx/";
    }

    /// <summary>
    /// Browser-like headers: file-host trả rỗng/403 khi thấy dấu vết API client.
    /// Retry có backoff vì các hub này hay trả rỗng khi bị gọi dồn.
    /// </summary>
    protected async Task<string> GetPage(string url, List<HeadersModel> more = null, int attempts = 3)
    {
        for (int i = 1; i <= attempts; i++)
        {
            var headers = HeadersModel.Init(
                ("User-Agent", Http.UserAgent),
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                ("Accept-Language", "en-US,en;q=0.9"));

            if (more != null)
                headers.AddRange(more);

            string html = await httpHydra.Get(url, addheaders: headers, statusCodeOK: false);
            if (!string.IsNullOrWhiteSpace(html))
                return html;

            if (i < attempts)
                await Task.Delay(500 * i);
        }

        return null;
    }

    protected static string Absolute(string href, string from)
    {
        if (string.IsNullOrWhiteSpace(href))
            return href;

        if (href.StartsWith("//"))
            return "https:" + href;

        if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return href;

        if (Uri.TryCreate(from, UriKind.Absolute, out Uri baseUri) &&
            Uri.TryCreate(baseUri, href, out Uri abs))
            return abs.ToString();

        return href;
    }

    protected static string Unescape(string text)
        => text.Replace("&amp;", "&").Replace("&#038;", "&").Replace("\\/", "/");

    /// <summary>
    /// Nhét chất lượng vào nhãn: label của nguồn thường đã có "1080p [2.3GB]" / "4K";
    /// nếu không thì đoán từ tên file, cuối cùng mới tới tên host.
    /// </summary>
    static string QualityLabel(string label, string hint)
    {
        string text = $"{label} {hint}";

        string q = Regex.Match(text, @"(?i)\b(2160p|4k|1080p|720p|480p|360p)\b").Value;
        if (!string.IsNullOrEmpty(q))
            q = q.Equals("2160p", StringComparison.OrdinalIgnoreCase) ? "4K" : q.ToUpperInvariant();

        string size = Regex.Match(text, @"(?i)\[\s*([\d.]+\s*[KMGT]B)\s*\]").Groups[1].Value;

        string host = "";
        if (Uri.TryCreate(hint, UriKind.Absolute, out Uri u))
            host = u.Host.Replace("www.", "");

        string name = string.Join(" · ", new[] { q, size, host }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());

        return string.IsNullOrWhiteSpace(name) ? "stream" : name;
    }

    /// <summary>
    /// Lọc link rác: các trang này chèn cả nút "tìm 480p trên chính site" (…/cloud/?s=480p),
    /// tag, Telegram… chỉ giữ link của file-host, và link /drive/ của HubCloud.
    /// </summary>
    protected static bool LooksLikeFileHost(string url)
        => !string.IsNullOrWhiteSpace(url) &&
           Regex.IsMatch(url, @"(?i)hubcloud|hubgo\.|hubcdn|gdflix|gdlink|go2link|gdtot|fshare|kdrive|katamole|drive\.google|drive\.usercontent|/drive/[A-Za-z0-9_-]{6,}|/file/[A-Za-z0-9_-]{6,}|\.(mkv|mp4|m4v|avi|mov|ts|m3u8)(\?|$)");

    protected static string Cut(string url)
        => url != null && url.Length > 90 ? url[..90] + "…" : url;

    protected static string Preview(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "<empty>";

        raw = Regex.Replace(raw, @"\s+", " ").Replace("\"", "'");
        return raw.Length <= 300 ? raw : raw[..300] + "...";
    }
    #endregion

    #region DOM nghiệp dư (đủ dùng, không phụ thuộc HtmlAgilityPack)
    /// <summary>
    /// Tìm các href xuất hiện SAU một heading (h1..h6) khớp <paramref name="headingPattern"/>,
    /// dừng ở heading kế tiếp — thay cho `h5:matches(...) -> nextElementSibling -> a` của CSX.
    /// </summary>
    protected static List<(string Label, string Url)> HrefsAfterHeading(string html, string headingPattern, int maxHrefs = 2)
    {
        var result = new List<(string Label, string Url)>();
        if (string.IsNullOrWhiteSpace(html))
            return result;

        var headings = Regex.Matches(html, @"(?is)<h([1-6])[^>]*>(?<t>.*?)</h\1>");
        int stop = int.MaxValue;

        foreach (Match h in headings)
        {
            string text = Regex.Replace(h.Groups["t"].Value, @"<[^>]+>", "");

            if (!Regex.IsMatch(text, headingPattern, RegexOptions.IgnoreCase))
                continue;

            int from = h.Index + h.Length;
            foreach (Match nx in headings)
                if (nx.Index > from && nx.Index < stop)
                    stop = nx.Index;

            string section = html[from..Math.Min(stop, html.Length)];

            foreach (Match a in Regex.Matches(section, @"(?is)<a[^>]+href=""(?<u>[^""]+)""[^>]*>(?<t>.*?)</a>"))
            {
                string label = Regex.Replace(a.Groups["t"].Value, @"<[^>]+>", "").Trim();
                result.Add((string.IsNullOrWhiteSpace(label) ? "link" : label, Unescape(a.Groups["u"].Value)));

                if (result.Count >= maxHrefs)
                    return result;
            }

            break;
        }

        return result;
    }

    /// <summary>Mọi anchor bên trong một khối div có class cho trước (kiểu `div.download-links-div a.btn`).</summary>
    protected static List<(string Label, string Url)> HrefsInDiv(string html, string classFragment, int max = 10)
    {
        var result = new List<(string Label, string Url)>();
        if (string.IsNullOrWhiteSpace(html))
            return result;

        foreach (Match d in Regex.Matches(html, @"(?is)<div[^>]*class=""[^""]*" + Regex.Escape(classFragment) + @"[^""]*""[^>]*>(?<inner>.*?)</div>"))
        {
            foreach (Match a in Regex.Matches(d.Groups["inner"].Value, @"(?is)<a[^>]+href=""(?<u>[^""]+)""[^>]*>(?<t>.*?)</a>"))
            {
                string url = Unescape(a.Groups["u"].Value);
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    continue;

                string label = Regex.Replace(a.Groups["t"].Value, @"<[^>]+>", " ");
                label = Regex.Replace(label, @"\s+", " ").Trim();

                result.Add((string.IsNullOrWhiteSpace(label) ? "link" : label, url));

                if (result.Count >= max)
                    return result;
            }
        }

        return result;
    }
    #endregion

    /// <summary>Nguồn cụ thể trả về danh sách (nhãn, link file-host).</summary>
    protected abstract Task<List<(string Label, string Url)>> FindLinks(string source, string imdbId, long tmdbId, short season, short episode);

    #region tmdb meta (fallback khi không có imdb id)
    protected async Task<(string title, int year)> TmdbMeta(long tmdbId, bool tv)
    {
        try
        {
            var cub = CoreInit.conf.cub;
            if (cub == null || string.IsNullOrEmpty(cub.mirror))
                return (null, 0);

            var proxyManager = cub.useproxy ? new ProxyManager("cub_api", cub) : null;

            JObject root = await Http.Get<JObject>(
                $"{cub.scheme}://tmdb.{cub.mirror}/3/{(tv ? "tv" : "movie")}/{tmdbId}?api_key={cub.api_key}",
                proxy: proxyManager?.Get());

            if (root == null)
                return (null, 0);

            string title = (tv ? root.Value<string>("name") : root.Value<string>("title")) ?? root.Value<string>("original_name");
            string date = tv ? root.Value<string>("first_air_date") : root.Value<string>("release_date");

            int.TryParse(date?.Length >= 4 ? date[..4] : null, out int year);

            return (title, year);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Log("movieshub")} tmdb meta fail {ex.GetType().Name}");
            return (null, 0);
        }
    }
    #endregion

    #region json-safe helpers
    protected static JToken ParseJson(string raw)
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

    protected static string Text(JToken token, params string[] keys)
    {
        if (token is not JObject obj)
            return null;

        foreach (string key in keys)
            if (obj[key] is JValue value && value.Value is string str && !string.IsNullOrWhiteSpace(str))
                return str;

        return null;
    }
    #endregion
}
