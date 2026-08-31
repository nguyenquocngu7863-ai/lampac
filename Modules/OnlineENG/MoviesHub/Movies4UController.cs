using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MoviesHub;

/// <summary>
/// Movies4U — WordPress: `GET {site}/?s=&lt;title&gt;+&lt;year&gt;` kèm `Cookie: xla=s4t`
/// (thiếu cookie là bị chặn). Bài viết khớp qua `imdb.com/title/tt…/`.
///
/// Vòng test đầu cho thấy: KHÔNG được tìm bằng IMDb id — `?s=` chỉ tìm text đã index,
/// còn id nằm trong `href` nên không bao giờ khớp (log: "0 bài ứng viên"). Vì vậy luôn tìm
/// bằng tên + năm từ TMDB, còn IMDb id chỉ dùng để XÁC MINH bài.
///
/// Link nằm trong `div.download-links-div a.btn` (trang trung gian) → `div.downloads-btns-div
/// a.btn` (file-host). Series: mỗi `div.downloads-btns-div` là một mùa, heading phía trước
/// cho biết mùa nào; vào link đó rồi mỗi khối là một tập.
/// </summary>
public class Movies4UController : HubController
{
    const string Source = "movies4u";

    public Movies4UController() : base(ModInit.fouru)      // config section riêng: "Movies4U"
    {
    }

    static string Tag => "movies4u:";

    List<HeadersModel> SiteHeaders()
        => HeadersModel.Init(
            ("Cookie", "xla=s4t"),
            ("Referer", init.host.TrimEnd('/') + "/"));

    [HttpGet, Staticache(manually: true)]
    [Route("lite/movies4u")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
        => CollectionCore(Source, checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson);

    // Không route video.m3u8: .mkv phải để GStreamer của Lampac quyết, không ép HLS.
    // file.mkv/file.mp4 alias: chỉ để PATH của url kết thúc bằng .mkv cho gst.js (xem RouteFor
    // trong HubController). Cùng action, cùng logic — không có menu chất lượng, không ép HLS.
    // play=false: method:"call" cần JSON. Để play=true mặc định là làm chết cả hai nguồn.
    [HttpGet, Staticache(manually: true)]
    [Route("lite/movies4u/video")]
    [Route("lite/movies4u/file.mkv")]
    [Route("lite/movies4u/file.mp4")]
    // play=false => /video (method:"call") trả JSON; JSON đó trỏ về file.mkv?…&play=true nên
    // đường nào player cũng thấy path .mkv mà gst.js bắt được, còn bản thân link phát là link trần
    // từ extractor (VideoCore.PlayUrl). Ai bấm nút VLC/DLNA thì ăn &play=true -> 302 thẳng file.
    public Task<ActionResult> Video(string src, string label, short s = -1, short e = -1, bool play = true)
        => VideoCore(Source, src, label, s, e, play);

    protected override async Task<List<HubEntry>> Collect(string source, string imdbId, long tmdbId, short season)
    {
        string site = init.host.TrimEnd('/');
        string imdb = imdbId?.Trim();
        bool tv = season != 0;

        var headers = SiteHeaders();
        var meta = await TmdbMeta(tmdbId, tv);

        // Tìm bằng tên. Thử original_title trước vì site là tiếng Anh.
        var queries = new List<string>();

        foreach (string name in new[] { meta.originalTitle, meta.title })
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string key = name.Trim();

            if (queries.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

            queries.Add(tv ? $"{key} season {Math.Max((int)season, 1)}" : $"{key} {meta.year}");
            queries.Add(tv ? key : $"{key}");
        }

        if (queries.Count == 0)
        {
            Console.WriteLine($"{Tag} TMDB không trả title để tìm (tmdb={tmdbId}) — không có cách nào khác vì ?s= không tìm được theo IMDb id");
            return null;
        }

        var posts = new List<(string Url, string Label)>();

        foreach (string query in queries.Take(2))
        {
            string search = await GetPage($"{site}/?s={Uri.EscapeDataString(query)}", headers);

            if (string.IsNullOrWhiteSpace(search))
            {
                Console.WriteLine($"{Tag} trang tìm kiếm rỗng | q={query} site={site}");
                continue;
            }

            // Không giả định <article>: lấy mọi anchor nằm TRONG heading h1..h4 (heading có thể
            // bọc thêm <span>/<a class=...>, và nháy có thể là nháy đơn).
            foreach (Match h in Regex.Matches(search, @"(?is)<h([1-4])[^>]*>(?<body>.*?)</h\1>"))
                foreach (Match m in Regex.Matches(h.Groups["body"].Value, AnchorPattern))
                {
                    string url = Absolute(Unescape(HrefValue(m)), site + "/");
                    string label = Regex.Replace(m.Groups["t"].Value, @"<[^>]+>", " ").Trim();

                    if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !posts.Any(p => p.Url == url))
                        posts.Add((url, string.IsNullOrWhiteSpace(label) ? "bài viết" : label));
                }

            Console.WriteLine($"{Tag} q={query} -> {posts.Count} bài ứng viên");

            if (posts.Count > 0)
                break;
        }

        if (posts.Count == 0)
            return null;

        var entries = new List<HubEntry>();

        foreach ((string postUrl, string _) in posts.Take(4))
        {
            string html = await GetPage(postUrl, headers);

            if (string.IsNullOrWhiteSpace(html))
                continue;

            if (!string.IsNullOrWhiteSpace(imdb))
            {
                string found = Regex.Match(html, @"imdb\.com/title/(?<id>tt\d{6,8})").Groups["id"].Value;

                if (!string.IsNullOrEmpty(found) && found != imdb)
                {
                    Console.WriteLine($"{Tag} bài lệch IMDb ({found} ≠ {imdb}) — bỏ {Cut(postUrl)}");
                    continue;
                }
            }

            Console.WriteLine($"{Tag} bài: {Cut(postUrl)} len={html.Length} div={Regex.Matches(html, "(?i)<div").Count} a={Regex.Matches(html, "(?i)<a[^>]+href=").Count}");

            if (season == 0)
                await CollectMovie(html, postUrl, headers, entries);
            else if (season < 0)
                CollectSeasons(html, postUrl, entries);
            else
                await CollectEpisodes(html, postUrl, headers, season, entries);

            if (entries.Count > 0)
                break;
        }

        return entries.Count == 0 ? null : entries.DistinctBy(x => x.Url + x.Season + x.Episode).ToList();
    }

    async Task CollectMovie(string html, string postUrl, List<HeadersModel> headers, List<HubEntry> into)
    {
        // CSX chỉ bấm NÚT ĐẦU TIÊN của download-links-div — làm thế là mất các khối chất lượng khác.
        // Log thiết bị (Obsession 2025): module chỉ ra 480p/1080p trong khi bài có bản
        // "1080p BluRay [Hindi AMZN DDP 5.1 + English DDP 7.1] 13.72 GB" và bản 4K ~20GB ở khối
        // CUỐI bài. Nên ở đây lấy MỌI gate, không return sớm, và mang theo heading của khối để nhãn
        // vẫn có "4K"/"1080p" khi text của nút trơ trọi.
        List<(string Url, string Heading)> gates = [.. DivBlocks(html, "download-links-div", 12)
                                                        .SelectMany(b => b.Links.Select(l => (Url: Absolute(Unescape(l.Url), postUrl), b.Heading)))
                                                        .Where(x => x.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !DeadHost(x.Url))
                                                        .DistinctBy(x => x.Url)
                                                        .Take(10)];

        if (gates.Count == 0)
        {
            // Dự phòng: một số bài để link trần, không bọc div
            gates = [.. Anchors(html, postUrl, 20, onlyFileHost: false)
                        .Select(a => (Url: a.Url, Heading: (string)null))
                        .Where(x => x.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !DeadHost(x.Url))
                        .DistinctBy(x => x.Url)
                        .Take(10)];

            Console.WriteLine($"{Tag} không có download-links-div, thử anchor trần: {gates.Count} | hosts={HostHistogram(html, postUrl)} | classes={ClassHistogram(html)}");
        }

        int before = into.Count;

        foreach ((string gate, string heading) in gates)
        {
            // Gate đã là file-host (một số bài link thẳng hubcloud) thì dùng luôn.
            if (LooksLikeFileHost(gate))
            {
                Push(into, heading, "link", gate);
                continue;
            }

            string inner = await GetPage(gate, headers);

            if (string.IsNullOrWhiteSpace(inner))
            {
                Console.WriteLine($"{Tag} trang trung gian rỗng {Cut(gate)}");
                continue;
            }

            var blocks = DivBlocks(inner, "downloads-btns-div", 20);

            // kiểu tường minh: C# không suy được kiểu đích cho collection expression khi dùng var
            List<(string Label, string Url)> raw = [.. blocks.SelectMany(b => b.Links.Select(l => (Label: $"{b.Heading} {l.Label}".Trim(), Url: l.Url)))];

            if (blocks.Count == 0)
                Console.WriteLine($"{Tag} gate {Cut(gate)} không có downloads-btns-div | classes={ClassHistogram(inner)} | a={Regex.Matches(inner, "(?i)<a[^>]+href=").Count}");

            if (raw.Count == 0)
                raw = [.. Anchors(inner, gate, 20, onlyFileHost: false).Select(a => (a.Label, a.Url))];

            foreach ((string label, string url) in raw)
            {
                string abs = Absolute(Unescape(url), gate);

                if (!LooksLikeFileHost(abs) || DeadHost(abs))
                    continue;

                Push(into, heading, label, abs);
            }

            if (raw.Count > 0 && into.Count == before)
                Console.WriteLine($"{Tag} trang trung gian không có file-host | {Cut(gate)} len={inner.Length}, a={Regex.Matches(inner, "(?i)<a[^>]+href=").Count}, raw={raw.Count}, hosts={HostHistogram(inner, gate)}, head={Preview(inner)}");
        }

        if (into.Count == before)
            Console.WriteLine($"{Tag} 0 link từ {gates.Count} gate của bài {Cut(postUrl)} | hosts={HostHistogram(html, postUrl)} | classes={ClassHistogram(html)}");
        else
            Console.WriteLine($"{Tag} movie: {into.Count - before} link từ {gates.Count} gate (mọi khối gate, đã bỏ gdflix/gdlink)");
    }

    /// <summary>
    /// Nút nguồn vào collection: một link = một nút, nhãn là "heading của khối + text nút" đã qua
    /// QualityLabel (nên ra dạng "4K · 20 GB"). Chặn cả DeadHost ở đây, dù CollectMovie đã lọc, vì
    /// đây là cổ chai duy nhất mà mọi đường (gate / anchor trần / link thẳng file-host) đi qua.
    /// </summary>
    void Push(List<HubEntry> into, string heading, string label, string url)
    {
        if (into.Any(x => x.Url == url) || into.Count >= 24 || DeadHost(url))
            return;

        string text = QualityLabel($"{heading} {label}", url);

        // Nhãn trơ trọi tên host (log cũ: "hubcloud.cx", "gdflix.dev") = heading của khối không được
        // bắt. Tự in nguyên liệu để vòng sau sửa selector mà không cần anh dán html.
        if (!Regex.IsMatch(text, @"(?i)\d{3,4}p|\b4k\b|\bhd\b|\d+(?:[.,]\d+)?\s?(?:gb|mb)"))
            Console.WriteLine($"{Tag} nhãn thiếu chất lượng: '{text}' (heading='{heading}' nút='{label}')");

        into.Add(new HubEntry(text, url, 0, 0));
    }

    void CollectSeasons(string html, string postUrl, List<HubEntry> into)
    {
        foreach (var block in DivBlocks(html, "downloads-btns-div", 20))
        {
            short n = SeasonNumber(block.Heading);

            if (n <= 0 || into.Any(x => x.Season == n))
                continue;

            string url = block.Links.Count > 0 ? Absolute(Unescape(block.Links[0].Url), postUrl) : null;

            if (string.IsNullOrWhiteSpace(url))
                continue;

            into.Add(new HubEntry($"Mùa {n}", url, n, 0));
        }

        if (into.Count == 0)
            Console.WriteLine($"{Tag} không nhận diện được mùa nào trong downloads-btns-div (heading={string.Join(" | ", DivBlocks(html, "downloads-btns-div", 5).Select(b => b.Heading))})");
    }

    async Task CollectEpisodes(string html, string postUrl, List<HeadersModel> headers, short season, List<HubEntry> into)
    {
        var blocks = DivBlocks(html, "downloads-btns-div", 20);
        var block = blocks.FirstOrDefault(b => SeasonNumber(b.Heading) == season);

        if (block.Links == null || block.Links.Count == 0)
        {
            Console.WriteLine($"{Tag} không có khối Season {season}, thử khối đầu tiên (blocks={blocks.Count})");
            block = blocks.Count > 0 ? blocks[0] : default;
        }

        if (block.Links == null || block.Links.Count == 0)
            return;

        string packUrl = Absolute(Unescape(block.Links[0].Url), postUrl);

        // Khối mùa có thể chứa thẳng link từng tập (không có trang pack riêng).
        string packHtml = LooksLikeFileHost(packUrl) ? null : await GetPage(packUrl, headers);

        if (block.Heading == null && packHtml == null)
            return;

        if (packHtml == null)
        {
            if (LooksLikeFileHost(packUrl))
            {
                into.Add(new HubEntry(QualityLabel($"Mùa {season}", packUrl), packUrl, season, 0));
                return;
            }

            Console.WriteLine($"{Tag} trang pack mùa {season} rỗng {Cut(packUrl)}");
            return;
        }

        var epBlocks = DivBlocks(packHtml, "downloads-btns-div", 40);

        for (int i = 0; i < epBlocks.Count; i++)
        {
            short ep = EpisodeNumber(epBlocks[i].Heading);
            if (ep <= 0)
                ep = (short)(i + 1);

            int taken = 0;

            foreach ((string label, string url) in epBlocks[i].Links)
            {
                string abs = Absolute(Unescape(url), packUrl);

                if (!LooksLikeFileHost(abs) || DeadHost(abs))
                    continue;

                into.Add(new HubEntry($"Ep {ep} · {QualityLabel(label, abs)}", abs, season, ep));

                if (++taken >= 2)
                    break;
            }
        }

        if (into.Count == 0)
            Console.WriteLine($"{Tag} pack {Cut(packUrl)} có {epBlocks.Count} khối nhưng không có file-host nào");
    }

    static short SeasonNumber(string text)
    {
        var m = Regex.Match(text ?? "", @"(?i)(?:season\s*(\d{1,2})|s(\d{2})\b)");

        if (!m.Success)
            return 0;

        int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out int n);

        return (short)n;
    }

    static short EpisodeNumber(string text)
    {
        var m = Regex.Match(text ?? "", @"(?i)(?:ep(?:isode)?\.?\s*(\d{1,3})|\be(\d{2})\b)");

        if (!m.Success)
            return 0;

        int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out int n);

        return (short)n;
    }
}
