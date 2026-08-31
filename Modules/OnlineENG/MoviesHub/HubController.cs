using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
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
/// Lớp dùng chung cho MoviesDrive + Movies4U. Hai nguồn chỉ khác bước "tìm link trên trang
/// của họ"; còn lại (file-host -> URL chơi được, headers, proxy, cache, log) là một — nên
/// nằm cùng assembly, vì dynamic module của Lampac compile riêng từng thư mục, không
/// tham chiếu chéo được.
///
/// MỘT QUYẾT ĐỊNH VỀ UI (theo yêu cầu của người dùng): link .mkv/.mp4 của nhóm file-host
/// KHÔNG được nhét vào menu "chất lượng" của player. Mỗi link là MỘT NÚT NGUỒN trong
/// MovieTpl/EpisodeTpl (label = 4K · 7.1GB), và /video trả về đúng MỘT url. Lý do: mkv phải
/// đi qua GStreamer/transcode của Lampac; đưa vào streamquality thì Lampa sẽ coi như HLS
/// variant và phát trực tiếp -> hỏng. Vì vậy route video.m3u8 cũng cố ý không tồn tại.
/// </summary>
public abstract class HubController : BaseENGController
{
    protected sealed record HubStream(string Url, string Label, List<HeadersModel> Headers);

    /// <summary>Một link file-host mà nguồn tìm thấy. Season/Episode = 0 với phim lẻ.</summary>
    protected sealed record HubEntry(string Label, string Url, short Season, short Episode);

    protected HubController(OnlinesSettings conf) : base(conf)
    {
    }

    #region collection: mỗi link một nút nguồn
    protected async Task<ActionResult> CollectionCore(string source, bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s, bool rjson)
    {
        // Lampa gọi ?checksearch=1 để hỏi "nguồn này có không" trước — phải trả đúng tín hiệu
        // của ViewTmdb, nếu không mỗi lần mở phim là một lần search site.
        if (checksearch)
            return Content("data-json=", "application/json; charset=utf-8");

        if (await IsRequestBlocked(rch: false))
        {
            Console.WriteLine($"{Log(source)} blocked ở collection (enable={init.enable}, rip={init.rip})");
            return badInitMsg ?? OnError("disable", gbcache: false, statusCode: 403);
        }

        long tmdbId = tmdb_id > 0 ? tmdb_id : id;

        if (tmdbId <= 0)
        {
            Console.WriteLine($"{Log(source)} không có tmdb id trong request collection");
            return OnError();
        }

        List<HubEntry> entries = null;

        try
        {
            // season: 0 = phim lẻ, -1 = series nhưng chưa chọn mùa (cần danh sách mùa),
            // N > 0 = các tập của mùa N.
            short want = serial != 1 ? (short)0 : (s <= 0 ? (short)-1 : s);

            entries = await CollectCached(source, imdb_id, tmdbId, want);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Log(source)} collection ex {ex.GetType().Name} {ex.Message}");
            return OnError("resolve", 502);
        }

        string plugin = init.plugin.ToLowerAndTrim();

        if (serial == 1)
        {
            if (s <= 0)
            {
                var seasons = new SeasonTpl();

                foreach (short n in entries.Select(x => x.Season).Where(x => x > 0).Distinct().OrderBy(x => x))
                    seasons.Append($"Mùa {n}", $"{host}/lite/{plugin}?id={tmdbId}&imdb_id={imdb_id}&serial=1&rjson={rjson}&s={n}", n);

                if (seasons.IsEmpty)
                    Console.WriteLine($"{Log(source)} không thấy mùa nào trong bài viết");

                return ContentTpl(seasons);
            }

            var etpl = new EpisodeTpl();

            foreach (HubEntry item in entries.Where(x => x.Episode > 0))
            {
                string link = $"{host}/lite/{plugin}/video?src={Enc(item.Url)}&label={Enc(item.Label)}&s={item.Season}&e={item.Episode}";

                etpl.Append(item.Label, title ?? original_title, item.Season, item.Episode, link, "call",
                            streamlink: $"{link}&play=true");
            }

            if (etpl.IsEmpty)
                Console.WriteLine($"{Log(source)} mùa {s} không có tập nào (entries={entries.Count})");

            return ContentTpl(etpl);
        }

        var mtpl = new MovieTpl(title, original_title);

        foreach (HubEntry item in entries)
        {
            string link = $"{host}/lite/{plugin}/video?src={Enc(item.Url)}&label={Enc(item.Label)}";

            mtpl.Append(item.Label, link, "call", stream: $"{link}&play=true");
        }

        if (mtpl.IsEmpty)
            Console.WriteLine($"{Log(source)} collection rỗng (tmdb={tmdbId}, imdb={imdb_id})");

        return ContentTpl(mtpl);
    }

    async Task<List<HubEntry>> CollectCached(string source, string imdbId, long tmdbId, short season)
    {
        string memKey = $"movieshub:{source}:{tmdbId}:{imdbId}:{season}";

        if (hybridCache.TryGetValue(memKey, out List<HubEntry> cached) && cached != null)
            return cached;

        List<HubEntry> entries = await Collect(source, imdbId, tmdbId, season) ?? [];

        Console.WriteLine($"{Log(source)} {entries.Count} link cho collection (tmdb={tmdbId}, season={season})");

        hybridCache.Set(memKey, entries, cacheTime(15));
        return entries;
    }
    #endregion

    #region video: ĐÚNG MỘT url, không streamquality
    protected async Task<ActionResult> VideoCore(string source, string src, string label, short s, short e, bool play)
    {
        StatiCacheDisabled = true;
        SetHeadersNoCache();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
        {
            Console.WriteLine($"{Log(source)} blocked (enable={init.enable}, rip={init.rip})");
            return badInitMsg ?? OnError("disable", gbcache: false, statusCode: 403);
        }

        string link = Dec(src);

        if (string.IsNullOrWhiteSpace(link))
        {
            Console.WriteLine($"{Log(source)} src thiếu/hỏng trong query video");
            return OnError("stream", 502);
        }

        label = string.IsNullOrWhiteSpace(Dec(label)) ? "stream" : Dec(label);

        List<HubStream> streams;

        try
        {
            streams = await ResolveHub(link, label, source);
        }
        catch (Exception ex)
        {
            // Không được để nổ: Lampac trả 500 rỗng và không còn dấu vết gì trong log.
            Console.WriteLine($"{Log(source)} ex {ex.GetType().Name} {ex.Message}");
            return OnError("resolve", 502);
        }

        HubStream first = streams.FirstOrDefault(x => IsPlayable(x.Url));

        if (first == null)
        {
            Console.WriteLine($"{Log(source)} không ra URL chơi được từ {Cut(link)}");
            return OnError("stream", 502);
        }

        string proxied = HostStreamProxy(first.Url, headers: first.Headers);

        Console.WriteLine($"{Log(source)} play {Cut(first.Url)} ({(s > 0 ? $"S{s}E{e} · " : "")}{label})");

        if (play)
            return RedirectToPlay(proxied);

        // quality là NHÃN của nút, không phải menu chất lượng: chỉ một link ở đây.
        return ContentTo(VideoTpl.ToJson(
            "play",
            proxied,
            label,
            quality: first.Label,
            vast: init.vast,
            hls_manifest_timeout: 120000,
            httpContext: HttpContext
        ));
    }

    /// <summary>mkv/mp4/m3u8 đều chơi được qua Lampac; link trung gian (go2link, /drive/… không đuôi) thì không.</summary>
    static bool IsPlayable(string url)
        => !string.IsNullOrWhiteSpace(url) &&
           Uri.TryCreate(url, UriKind.Absolute, out Uri u) &&
           (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
    #endregion

    #region hubcloud / gdrive resolver
    /// <summary>
    /// Một link nguồn -> 0..n URL chơi được. Thử theo thứ tự: URL media trần trong trang ->
    /// /drive/download/… -> Google Drive -> (nếu là trang tìm tên file) nhảy 1 hop vào file.
    /// </summary>
    protected async Task<List<HubStream>> ResolveHub(string url, string label, string source, int depth = 0)
    {
        var found = new List<HubStream>();

        if (string.IsNullOrWhiteSpace(url))
            return found;

        url = url.Trim();

        List<HubStream> gdrive = FromGoogleDrive(url, label);
        if (gdrive.Count > 0)
            return gdrive;

        string html = await GetPage(url);

        if (string.IsNullOrWhiteSpace(html))
        {
            Console.WriteLine($"{Log(source)} trang file-host rỗng {Cut(url)}");
            return found;
        }

        // drive link nằm ngay trong trang (nhiều bài nhét gdrive ở nút thứ hai)
        found.AddRange(FromGoogleDrive(html, label));

        foreach (Match m in Regex.Matches(html, @"https?://[^""'\s<>]+?\.(?:mkv|mp4|m4v|mov|avi|mkv\.js|ts|m3u8)(?:\?[^""'\s<>]*)?", RegexOptions.IgnoreCase))
        {
            string media = Unescape(m.Value);

            if (media.Length > 20 && !media.Contains("poster") && !media.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                found.Add(new HubStream(media, QualityLabel(label, media), StreamHeaders(url)));
        }

        if (found.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, @"href=""(?<u>[^""]*?/drive/download/[^""]+)""", RegexOptions.IgnoreCase))
                found.Add(new HubStream(Absolute(m.Groups["u"].Value, url), QualityLabel(label, m.Groups["u"].Value), StreamHeaders(url)));
        }

        if (found.Count > 0)
            return found.DistinctBy(x => x.Url).ToList();

        if (depth == 0)
        {
            // Trang tìm kiếm của HubCloud: danh sách file, chưa phải file -> 1 hop nữa
            foreach (string file in Regex.Matches(html, @"href=""(?<u>[^""]*?/drive/[A-Za-z0-9_-]{6,})""", RegexOptions.IgnoreCase)
                                             .Select(m => Absolute(m.Groups["u"].Value, url))
                                             .Distinct(StringComparer.OrdinalIgnoreCase)
                                             .Take(3))
            {
                found.AddRange(await ResolveHub(file, label, source, depth + 1));
                if (found.Count > 0)
                    break;
            }
        }

        if (found.Count == 0)
            Console.WriteLine($"{Log(source)} không extract được link | {Cut(url)} len={html.Length}, head={Preview(html)}");

        return found.DistinctBy(x => x.Url).ToList();
    }

    /// <summary>drive.google.com/file/d/&lt;id&gt;/view -> link tải trực tiếp, không cần xin trang.</summary>
    static List<HubStream> FromGoogleDrive(string text, string label)
    {
        var list = new List<HubStream>();

        if (string.IsNullOrWhiteSpace(text))
            return list;

        foreach (Match m in Regex.Matches(text, @"drive(?:\.google|\.usercontent\.google)\.com/[^\s""'<>]*(?:/file/d/|/uc\?|id=)(?<id>[01][0-9A-Za-z_-]{20,})"))
        {
            string id = m.Groups["id"].Value;

            if (list.Any(x => x.Url.Contains(id)))
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
        => Uri.TryCreate(url, UriKind.Absolute, out Uri u) ? $"{u.Scheme}://{u.Authority}/" : "https://hubcloud.cx/";

    /// <summary>
    /// Browser-like headers + retry có backoff: nhóm file-host này trả rỗng khi bị gọi dồn,
    /// và 403 khi thấy dấu vết API client (bài học từ VidCore).
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

        if (Uri.TryCreate(from, UriKind.Absolute, out Uri baseUri) && Uri.TryCreate(baseUri, href, out Uri abs))
            return abs.ToString();

        return href;
    }

    protected static string Unescape(string text)
        => text.Replace("&amp;", "&").Replace("&#038;", "&").Replace("\\/", "/");

    /// <summary>Base64 để link file-host an toàn qua query string (nó chứa ?, &, =).</summary>
    protected static string Enc(string text)
        => string.IsNullOrWhiteSpace(text) ? null : Uri.EscapeDataString(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)));

    protected static string Dec(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Uri.UnescapeDataString(text)));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Nhãn nút nguồn: chất lượng + dung lượng + host, lấy từ text của link hoặc tên file.</summary>
    protected static string QualityLabel(string label, string hint)
    {
        string text = $"{label} {hint}";

        string q = Regex.Match(text, @"(?i)\b(2160p|4k|1080p|720p|480p|360p)\b").Value;

        if (!string.IsNullOrEmpty(q))
            q = q.Equals("2160p", StringComparison.OrdinalIgnoreCase) ? "4K" : q.ToUpperInvariant();

        string size = Regex.Match(text, @"(?i)([\d.]+)\s*([KMGT]B)").Groups[0].Value.Trim();
        string host = Uri.TryCreate(hint, UriKind.Absolute, out Uri u) ? u.Host.Replace("www.", "") : "";

        string name = string.Join(" · ", new[] { q, size, host }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());

        if (string.IsNullOrWhiteSpace(name))
            name = string.IsNullOrWhiteSpace(label) ? "stream" : label;

        return name.Length > 40 ? name[..40] : name;
    }

    protected static string Cut(string url)
        => url != null && url.Length > 90 ? url[..90] + "…" : url;

    protected static string Preview(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "<empty>";

        raw = Regex.Replace(raw, @"\s+", " ").Replace("\"", "'");

        return raw.Length <= 300 ? raw : raw[..300] + "...";
    }

    static string Log(string source) => $"{source}:";
    #endregion

    #region DOM nghiệp dư (không phụ thuộc HtmlAgilityPack)
    /// <summary>
    /// Quét MỌI anchor trong HTML (không giả định nó nằm trong h5 như CSX), trả link + nhãn
    /// (text của anchor, nếu rỗng thì heading gần nhất phía trước). Lọc qua LooksLikeFileHost
    /// để loại nút "tìm 480p trên chính site", tag, Telegram…
    /// </summary>
    protected static List<(string Label, string Url, int Index)> Anchors(string html, string baseUrl, int max = 40, bool onlyFileHost = true)
    {
        var result = new List<(string Label, string Url, int Index)>();

        if (string.IsNullOrWhiteSpace(html))
            return result;

        foreach (Match m in Regex.Matches(html, @"(?is)<a[^>]+href=""(?<u>[^""]+)""[^>]*>(?<t>.*?)</a>"))
        {
            string url = Absolute(Unescape(m.Groups["u"].Value), baseUrl);

            // onlyFileHost=false: dùng cho các bước ĐIỀU HƯỚNG (trang pack của mùa, trang
            // trung gian của Movies4U) — những link này không phải file-host mà là trang nội.
            if (onlyFileHost && !LooksLikeFileHost(url))
                continue;

            string label = Regex.Replace(m.Groups["t"].Value, @"<[^>]+>", " ");
            label = Regex.Replace(label, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(label))
                label = NearestHeadingBefore(html, m.Index);

            result.Add((string.IsNullOrWhiteSpace(label) ? "link" : label, url, m.Index));

            if (result.Count >= max)
                break;
        }

        return result;
    }

    /// <summary>Mọi href trong khối div có class chứa classFragment (kèm vị trí để suy ra mùa).</summary>
    protected static List<(int Index, string Heading, List<(string Label, string Url)> Links)> DivBlocks(string html, string classFragment, int max = 30)
    {
        var blocks = new List<(int Index, string Heading, List<(string Label, string Url)> Links)>();

        if (string.IsNullOrWhiteSpace(html))
            return blocks;

        foreach (Match open in Regex.Matches(html, @"(?is)<div[^>]*class=""[^""]*" + Regex.Escape(classFragment) + @"[^""]*""[^>]*>"))
        {
            int start = open.Index + open.Length;
            int end = html.IndexOf("</div>", start, StringComparison.OrdinalIgnoreCase);

            if (end < 0)
                end = Math.Min(html.Length, start + 6000);

            string inner = html[start..end];

            var links = Regex.Matches(inner, @"(?is)<a[^>]+href=""(?<u>[^""]+)""[^>]*>(?<t>.*?)</a>")
                             .Select(m => (Label: Regex.Replace(m.Groups["t"].Value, @"<[^>]+>", " ").Trim(), Url: m.Groups["u"].Value))
                             .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                             .ToList();

            blocks.Add((open.Index, NearestHeadingBefore(html, open.Index), links));

            if (blocks.Count >= max)
                break;
        }

        return blocks;
    }

    static string NearestHeadingBefore(string html, int position)
    {
        if (position <= 0)
            return "";

        int from = Math.Max(0, position - 1500);
        var matches = Regex.Matches(html[from..position], @"(?is)<h([1-6])[^>]*>(?<t>.*?)</h\1>");

        if (matches.Count == 0)
            return "";

        string text = Regex.Replace(matches[matches.Count - 1].Groups["t"].Value, @"<[^>]+>", " ");

        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Lọc link rác. Trang của 2 nguồn này chèn cả nút "tìm quality trên chính site",
    /// tag, Telegram… nên chỉ giữ host của nhóm file-host và link /drive/|/file/ của họ.
    /// </summary>
    protected static bool LooksLikeFileHost(string url)
        => !string.IsNullOrWhiteSpace(url) &&
           Regex.IsMatch(url, @"(?i)hubcloud|hubgo\.|hubcdn|gdflix|gdlink|go2link|gdtot|fshare|kdrive|katamole|drive\.google|drive\.usercontent|/drive/[A-Za-z0-9_-]{6,}|/file/[A-Za-z0-9_-]{6,}|\.(mkv|mp4|m4v|avi|mov|ts|m3u8)(\?|$)");
    #endregion

    /// <summary>Nguồn cụ thể tự tìm link (Season/Episode = 0 với phim lẻ).</summary>
    protected abstract Task<List<HubEntry>> Collect(string source, string imdbId, long tmdbId, short season);

    #region tmdb meta (Movies4U tìm bằng tên+năm, không tìm được bằng IMDb id)
    protected async Task<(string title, string originalTitle, int year)> TmdbMeta(long tmdbId, bool tv)
    {
        try
        {
            var cub = CoreInit.conf.cub;

            if (cub == null || string.IsNullOrEmpty(cub.mirror))
                return (null, null, 0);

            var proxyManager = cub.useproxy ? new ProxyManager("cub_api", cub) : null;

            JObject root = await Http.Get<JObject>(
                $"{cub.scheme}://tmdb.{cub.mirror}/3/{(tv ? "tv" : "movie")}/{tmdbId}?api_key={cub.api_key}",
                proxy: proxyManager?.Get());

            if (root == null)
                return (null, null, 0);

            string title = tv ? root.Value<string>("name") : root.Value<string>("title");
            string original = root.Value<string>("original_title") ?? root.Value<string>("original_name");
            string date = tv ? root.Value<string>("first_air_date") : root.Value<string>("release_date");

            int.TryParse(date?.Length >= 4 ? date[..4] : null, out int year);

            return (title, original, year);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"movieshub: tmdb meta fail {ex.GetType().Name}");
            return (null, null, 0);
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
        catch (Exception)
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
