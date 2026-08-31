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
    [HttpGet, Staticache(manually: true)]
    [Route("lite/movies4u/video")]
    public Task<ActionResult> Video(string src, string label, short s = -1, short e = -1, bool play = false)
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

            queries.Add(tv ? $"{key} season {Math.Max(season, 1)}" : $"{key} {meta.year}");
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

            // Không giả định <article>: mọi anchor trong heading h1..h4 đều là một bài.
            foreach (Match m in Regex.Matches(search, @"(?is)<h([1-4])[^>]*>\s*<a[^>]+href=""(?<u>[^""]+)""[^>]*>(?<t>.*?)</a>"))
            {
                string url = Absolute(Unescape(m.Groups["u"].Value), site + "/");
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
        var gates = DivBlocks(html, "download-links-div", 4)
                        .SelectMany(b => b.Links)
                        .Select(l => Absolute(Unescape(l.Url), postUrl))
                        .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(3)
                        .ToList();

        if (gates.Count == 0)
        {
            // Dự phòng: một số bài để link trần, không bọc div
            gates = Anchors(html, postUrl, 6, onlyFileHost: false).Select(a => a.Url)
                          .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .Take(3)
                          .ToList();

            Console.WriteLine($"{Tag} không có download-links-div, thử anchor trần: {gates.Count}");
        }

        foreach (string gate in gates)
        {
            // Gate đã là file-host (một số bài link thẳng hubcloud) thì dùng luôn.
            if (LooksLikeFileHost(gate))
            {
                into.Add(new HubEntry(QualityLabel("link", gate), gate, 0, 0));
                continue;
            }

            string inner = await GetPage(gate, headers);

            if (string.IsNullOrWhiteSpace(inner))
            {
                Console.WriteLine($"{Tag} trang trung gian rỗng {Cut(gate)}");
                continue;
            }

            var blocks = DivBlocks(inner, "downloads-btns-div", 12);
            var raw = blocks.SelectMany(b => b.Links).ToList();

            if (raw.Count == 0)
                raw = Anchors(inner, gate, 12, onlyFileHost: false).Select(a => (a.Label, a.Url)).ToList();

            int added = 0;

            foreach ((string label, string url) in raw)
            {
                string abs = Absolute(Unescape(url), gate);

                if (!LooksLikeFileHost(abs))
                    continue;

                into.Add(new HubEntry(QualityLabel(label, abs), abs, 0, 0));

                if (++added >= 8)
                    break;
            }

            if (added == 0)
                Console.WriteLine($"{Tag} trang trung gian không có file-host | {Cut(gate)} len={inner.Length}, head={Preview(inner)}");

            if (into.Count > 0)
                return;
        }
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

                if (!LooksLikeFileHost(abs))
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
