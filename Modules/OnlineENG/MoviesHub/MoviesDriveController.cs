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
/// MoviesDrive — `GET {site}/search.php?q=&lt;imdb&gt;` (Typesense, JSON) → `document.permalink`
/// → trong bài có các heading chứa link HubCloud theo từng quality (480p…4K).
/// Series: `Season N` → trang danh sách tập → `Ep N` → 1-2 link.
/// Formula theo CSX `invokeMoviesdrive`, nhưng mọi bước đều có log vì nhóm site này
/// đổi markup thường xuyên.
/// </summary>
public class MoviesDriveController : HubController
{
    const string Source = "moviesdrive";

    public MoviesDriveController() : base(ModInit.conf)
    {
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/moviesdrive")]
    public async Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        var res = await ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");

        if (res is null or RedirectResult || (res as ContentResult)?.StatusCode > 200)
            Console.WriteLine($"moviesdrive: index không có dữ liệu (type={res?.GetType().Name ?? "null"}, status={(res as ContentResult)?.StatusCode?.ToString() ?? "-"}, id={id}, tmdb_id={tmdb_id}, serial={serial})");

        return res;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/moviesdrive/video")]
    [Route("lite/moviesdrive/video.m3u8")]
    public Task<ActionResult> Video(long id, string imdb_id, short s = -1, short e = -1, bool play = false)
        => VideoCore(Source, id, imdb_id, s, e, play);

    protected override async Task<List<(string Label, string Url)>> FindLinks(string source, string imdbId, long tmdbId, short season, short episode)
    {
        string site = init.host.TrimEnd('/');
        string imdb = imdbId?.Trim();
        bool tv = season > 0;

        string query = string.IsNullOrWhiteSpace(imdb) ? null : imdb;

        if (query == null)
        {
            var meta = await TmdbMeta(tmdbId, tv);

            if (!string.IsNullOrWhiteSpace(meta.title))
                query = tv ? $"{meta.title} season {season}" : $"{meta.title} {meta.year}";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine($"moviesdrive: không có từ khoá tìm (imdb rỗng, tmdb meta fail id={tmdbId})");
            return null;
        }

        string raw = await GetPage($"{site}/search.php?q={Uri.EscapeDataString(query)}");

        if (ParseJson(raw) is not JObject root)
        {
            Console.WriteLine($"moviesdrive: search không trả JSON ({site}) len={raw?.Length ?? 0}, head={Preview(raw)}");
            return null;
        }

        if (root["hits"] is not JArray hits || hits.Count == 0)
        {
            Console.WriteLine($"moviesdrive: search 0 kết quả (q={query}, found={(root["found"]?.ToString() ?? "?")})");
            return null;
        }

        var links = new List<(string Label, string Url)>();

        foreach (JToken hit in hits.Take(4))
        {
            JToken doc = hit["document"];
            if (doc is not JObject document)
                continue;

            string postImdb = Text(document, "imdb_id");
            string permalink = Text(document, "permalink");

            if (string.IsNullOrWhiteSpace(permalink))
                continue;

            // Có IMDb id mà bài khác id -> bỏ, đúng như CSX. Chỉ nới khi tìm bằng tên.
            if (!string.IsNullOrWhiteSpace(imdb) && !string.IsNullOrWhiteSpace(postImdb) && postImdb != imdb)
                continue;

            string postUrl = Absolute(permalink, site + "/");
            string html = await GetPage(postUrl);

            if (string.IsNullOrWhiteSpace(html))
            {
                Console.WriteLine($"moviesdrive: bài viết rỗng {Cut(postUrl)}");
                continue;
            }

            if (!tv)
            {
                foreach (Match m in Regex.Matches(html, @"(?is)<h[1-6][^>]*>\s*<a[^>]+href=""(?<u>[^""]+)""[^>]*>(?<t>.*?)</a>"))
                {
                    string url = Absolute(Unescape(m.Groups["u"].Value), postUrl);

                    if (!LooksLikeFileHost(url))
                        continue;

                    links.Add((Regex.Replace(m.Groups["t"].Value, @"<[^>]+>", "").Trim(), url));
                }
            }
            else
            {
                var seasonLinks = HrefsAfterHeading(html, $@"(Season\s*{season}\b|S{season:00}\b|Season\s*{season:00}\b)", 1);

                if (seasonLinks.Count == 0)
                {
                    Console.WriteLine($"moviesdrive: không thấy khối Season {season} trong {Cut(postUrl)} (h5={Regex.Matches(html, "(?is)<h[1-6]").Count})");
                    continue;
                }

                string epUrl = Absolute(seasonLinks[0].Url, postUrl);
                string epHtml = await GetPage(epUrl);

                if (string.IsNullOrWhiteSpace(epHtml))
                {
                    Console.WriteLine($"moviesdrive: trang danh sách tập rỗng {Cut(epUrl)}");
                    continue;
                }

                foreach ((string label, string url) in HrefsAfterHeading(epHtml, $@"(Ep\.?\s*{episode:00}\b|Ep\.?\s*{episode}\b|Episode\s*{episode}\b|\bE{episode:00}\b)", 4))
                {
                    string fileUrl = Absolute(url, epUrl);
                    if (LooksLikeFileHost(fileUrl))
                        links.Add((label, fileUrl));
                }
            }

            if (links.Count > 0)
                break;
        }

        Console.WriteLine($"moviesdrive: {links.Count} link file-host (q={query}, hits={hits.Count}, {(tv ? "tv" : "movie")}:{tmdbId})");

        return links.Count == 0 ? null : links.DistinctBy(l => l.Url).ToList();
    }
}
