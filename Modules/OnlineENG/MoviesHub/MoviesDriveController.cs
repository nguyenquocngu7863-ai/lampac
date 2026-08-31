using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MoviesHub;

/// <summary>
/// MoviesDrive — `GET {site}/search.php?q=&lt;imdb&gt;` (Typesense, JSON) → `hits[].document.permalink`.
/// Trong bài, mỗi quality là một anchor trỏ thẳng sang HubCloud (không cần giả định nó nằm
/// trong h5 như CSX — chính giả định đó làm module trả 0 link ở vòng test đầu).
///
/// Series: `Season N` (link trang nội) → trang pack có các khối `Ep N` → 1-2 link/tập.
/// </summary>
public class MoviesDriveController : HubController
{
    const string Source = "moviesdrive";

    public MoviesDriveController() : base(ModInit.drive)   // config section riêng: "MoviesDrive"
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/moviesdrive")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
        => CollectionCore(Source, checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson);

    // KHÔNG có route video.m3u8: file .mkv phải để Lampac/GStreamer quyết định, không ép HLS.
    [HttpGet, Staticache(manually: true)]
    [Route("lite/moviesdrive/video")]
    public Task<ActionResult> Video(string src, string label, short s = -1, short e = -1, bool play = false)
        => VideoCore(Source, src, label, s, e, play);

    protected override async Task<List<HubEntry>> Collect(string source, string imdbId, long tmdbId, short season)
    {
        string site = init.host.TrimEnd('/');
        string imdb = imdbId?.Trim();
        bool tv = season != 0;

        // MoviesDrive tìm được bằng IMDb id (field imdb_id trong Typesense) — giữ ưu tiên đó.
        string query = string.IsNullOrWhiteSpace(imdb) ? null : imdb;

        if (query == null)
        {
            var meta = await TmdbMeta(tmdbId, tv);

            if (!string.IsNullOrWhiteSpace(meta.title))
                query = tv ? $"{meta.title} season {Math.Max((int)season, 1)}" : $"{meta.title} {meta.year}";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine($"{Tag} không có từ khoá tìm (imdb rỗng, tmdb meta fail, tmdb={tmdbId})");
            return null;
        }

        string raw = await GetPage($"{site}/search.php?q={Uri.EscapeDataString(query)}");

        if (ParseJson(raw) is not JObject root)
        {
            Console.WriteLine($"{Tag} search không trả JSON ({site}) len={raw?.Length ?? 0}, head={Preview(raw)}");
            return null;
        }

        if (root["hits"] is not JArray hits || hits.Count == 0)
        {
            Console.WriteLine($"{Tag} search 0 kết quả (q={query}, found={root["found"] ?? "?"})");
            return null;
        }

        var entries = new List<HubEntry>();

        foreach (JToken hit in hits.Take(3))
        {
            if (hit["document"] is not JObject document)
                continue;

            string postImdb = Text(document, "imdb_id");

            if (!string.IsNullOrWhiteSpace(imdb) && !string.IsNullOrWhiteSpace(postImdb) && postImdb != imdb)
                continue;

            string permalink = Text(document, "permalink");

            if (string.IsNullOrWhiteSpace(permalink))
                continue;

            string postUrl = Absolute(permalink, site + "/");
            string html = await GetPage(postUrl);

            if (string.IsNullOrWhiteSpace(html))
            {
                Console.WriteLine($"{Tag} bài viết rỗng {Cut(postUrl)}");
                continue;
            }

            Console.WriteLine($"{Tag} bài: {Cut(postUrl)} a={Regex.Matches(html, "(?i)<a[^>]+href=").Count} len={html.Length}");

            if (season == 0)
            {
                int before = entries.Count;

                foreach ((string label, string url, int _) in Anchors(html, postUrl, 20))
                    entries.Add(new HubEntry(QualityLabel(label, url), url, 0, 0));

                // host của các anchor: một dòng log này trả lời hết câu "link nằm ở đâu"
                if (entries.Count == before)
                    Console.WriteLine($"{Tag} 0 link file-host | hosts={HostHistogram(html, postUrl)}");
            }
            else if (season < 0)
            {
                // Danh sách mùa: link là trang nội (pack), nên KHÔNG lọc file-host.
                foreach ((string label, string url, int _) in Anchors(html, postUrl, 60, onlyFileHost: false))
                {
                    short n = SeasonNumber(label);

                    if (n > 0 && !entries.Any(x => x.Season == n))
                        entries.Add(new HubEntry($"Mùa {n}", url, n, 0));
                }
            }
            else
            {
                var pack = Anchors(html, postUrl, 60, onlyFileHost: false)
                           .FirstOrDefault(a => SeasonNumber(a.Label) == season);

                string packUrl = pack.Url ?? postUrl;
                string packHtml = pack.Url == null ? html : await GetPage(pack.Url);

                if (string.IsNullOrWhiteSpace(packHtml))
                {
                    Console.WriteLine($"{Tag} trang mùa {season} rỗng {Cut(packUrl)}");
                    continue;
                }

                var perEpisode = new Dictionary<short, int>();

                foreach ((string label, string url, int _) in Anchors(packHtml, packUrl, 200))
                {
                    short ep = EpisodeNumber(label);

                    if (ep <= 0)
                        continue;

                    // CSX lấy 2 link/tập; giữ nguyên để danh sách tập không phình.
                    perEpisode.TryGetValue(ep, out int taken);
                    if (taken >= 2)
                        continue;

                    perEpisode[ep] = taken + 1;
                    entries.Add(new HubEntry($"Ep {ep} · {QualityLabel(label, url)}", url, season, ep));
                }

                if (entries.Count == 0)
                    Console.WriteLine($"{Tag} mùa {season}: không parse được tập nào (a={Regex.Matches(packHtml, "(?i)<a[^>]+href=").Count}, hosts={HostHistogram(packHtml, packUrl)}, pack={Cut(packUrl)})");
            }

            if (entries.Count > 0)
                break;
        }

        return entries.Count == 0 ? null : entries.DistinctBy(x => x.Url + x.Season + x.Episode).ToList();
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

    static string Tag => "moviesdrive:";
}
