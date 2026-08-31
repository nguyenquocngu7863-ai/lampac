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
/// Movies4U — WordPress: `GET {site}/?s=&lt;title+year&gt;` kèm `Cookie: xla=s4t` (thiếu cookie
/// là bị chặn), bài viết khớp IMDb qua `imdb.com/title/tt…/`, link nằm trong
/// `div.download-links-div a.btn` -> sang trang trung gian -> `div.downloads-btns-div a.btn`.
/// Series: mỗi `div.downloads-btns-div` là một mùa, heading phía trước chứa "Season N";
/// vào link đó rồi lấy khối thứ `episode`.
///
/// Site này là họ hàng của MoviesDrive (cũng đổ về HubCloud/GDrive) nên resolver dùng
/// chung của HubController — đó là lý do hai nguồn nằm cùng một module.
/// </summary>
public class Movies4UController : HubController
{
    const string Source = "movies4u";

    public Movies4UController() : base(ModInit.fouru)      // config section riêng: "Movies4U"
    {
    }

    List<HeadersModel> SiteHeaders()
        => HeadersModel.Init(
            ("Cookie", "xla=s4t"),          // thiếu cookie là site chặn, theo CSX
            ("Referer", init.host.TrimEnd('/') + "/"));

    [HttpGet, Staticache(manually: true)]
    [Route("lite/movies4u")]
    public async Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
    {
        var res = await ViewTmdb(checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson, method: "call");

        if (res is null or RedirectResult || (res as ContentResult)?.StatusCode > 200)
            Console.WriteLine($"movies4u: index không có dữ liệu (type={res?.GetType().Name ?? "null"}, status={(res as ContentResult)?.StatusCode?.ToString() ?? "-"}, id={id}, tmdb_id={tmdb_id}, serial={serial})");

        return res;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lite/movies4u/video")]
    [Route("lite/movies4u/video.m3u8")]
    public Task<ActionResult> Video(long id, string imdb_id, short s = -1, short e = -1, bool play = false)
        => VideoCore(Source, id, imdb_id, s, e, play);

    protected override async Task<List<(string Label, string Url)>> FindLinks(string source, string imdbId, long tmdbId, short season, short episode)
    {
        string site = init.host.TrimEnd('/');   // host của riêng Movies4U, không mượn apihost
        string imdb = imdbId?.Trim();
        bool tv = season > 0;

        // IMDb id thường có ngay trong bài nên search theo nó trước (rẻ và chính xác);
        // không có thì mới lấy tên + năm từ TMDB.
        string query = string.IsNullOrWhiteSpace(imdb) ? null : imdb;

        if (query == null)
        {
            var meta = await TmdbMeta(tmdbId, tv);

            if (!string.IsNullOrWhiteSpace(meta.title))
                query = tv ? $"{meta.title} season {season}" : $"{meta.title} {meta.year}";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            Console.WriteLine($"movies4u: không có từ khoá tìm (imdb rỗng, tmdb meta fail id={tmdbId})");
            return null;
        }

        var headers = SiteHeaders();
        string search = await GetPage($"{site}/?s={Uri.EscapeDataString(query)}", headers);

        if (string.IsNullOrWhiteSpace(search))
        {
            Console.WriteLine($"movies4u: trang tìm kiếm rỗng {site}/?s={Cut(query)}");
            return null;
        }

        var posts = Regex.Matches(search, @"(?is)<h3[^>]*>\s*<a[^>]+href=""(?<u>[^""]+)""[^>]*>(?<t>.*?)</a>")
                         .Select(m => (Url: Absolute(Unescape(m.Groups["u"].Value), site + "/"),
                                        Label: Regex.Replace(m.Groups["t"].Value, @"<[^>]+>", "").Trim()))
                         .Where(p => p.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                         .DistinctBy(p => p.Url)
                         .Take(5)
                         .ToList();

        Console.WriteLine($"movies4u: {posts.Count} bài ứng viên (q={query})");

        var links = new List<(string Label, string Url)>();

        foreach ((string postUrl, string postLabel) in posts)
        {
            string html = await GetPage(postUrl, headers);

            if (string.IsNullOrWhiteSpace(html))
                continue;

            // Cùng site nhưng sai phim thì bỏ; chỉ bắt buộc khi mình tìm bằng IMDb id.
            if (!string.IsNullOrWhiteSpace(imdb))
            {
                string found = Regex.Match(html, @"imdb\.com/title/(?<id>tt\d{6,8})").Groups["id"].Value;

                if (!string.IsNullOrEmpty(found) && found != imdb)
                    continue;
            }

            if (!tv)
            {
                // download-links-div -> trang trung gian -> downloads-btns-div
                var gates = HrefsInDiv(html, "download-links-div", 2);

                if (gates.Count == 0)
                {
                    Console.WriteLine($"movies4u: không có download-links-div trong {Cut(postUrl)} (a={Regex.Matches(html, "(?i)<a[^>]+href=").Count})");
                    continue;
                }

                foreach ((string _, string gateUrl) in gates)
                {
                    string innerUrl = Absolute(gateUrl, postUrl);
                    string inner = await GetPage(innerUrl, headers);

                    if (string.IsNullOrWhiteSpace(inner))
                    {
                        Console.WriteLine($"movies4u: trang trung gian rỗng {Cut(innerUrl)}");
                        continue;
                    }

                    Collect(inner, innerUrl, links);

                    if (links.Count > 0)
                        break;
                }
            }
            else
            {
                var blocks = DivBlocks(html, "downloads-btns-div");

                if (blocks.Count == 0)
                {
                    Console.WriteLine($"movies4u: không có downloads-btns-div (series) trong {Cut(postUrl)}");
                    continue;
                }

                // Mỗi khối là một mùa, heading phía trước cho biết là mùa nào.
                (int Index, string Inner) seasonBlock = default;
                bool picked = false;

                foreach ((int index, string inner) in blocks)
                {
                    string heading = NearestHeadingBefore(html, index);

                    if (Regex.IsMatch(heading, $@"(Season\s*{season}\b|S{season:00}\b|Season\s*{season:00}\b)", RegexOptions.IgnoreCase))
                    {
                        seasonBlock = (index, inner);
                        picked = true;
                        break;
                    }
                }

                if (!picked)
                    seasonBlock = blocks[0];

                string firstHref = Regex.Match(seasonBlock.Inner, @"(?is)<a[^>]+href=""(?<u>[^""]+)""").Groups["u"].Value;

                if (string.IsNullOrWhiteSpace(firstHref))
                {
                    Console.WriteLine($"movies4u: khối mùa {season} không có link (blocks={blocks.Count})");
                    continue;
                }

                string packUrl = Absolute(Unescape(firstHref), postUrl);
                string packHtml = await GetPage(packUrl, headers);

                if (string.IsNullOrWhiteSpace(packHtml))
                {
                    Console.WriteLine($"movies4u: trangpack rỗng {Cut(packUrl)}");
                    continue;
                }

                var episodeBlocks = DivBlocks(packHtml, "downloads-btns-div");

                if (episodeBlocks.Count == 0)
                {
                    Console.WriteLine($"movies4u: pack không có downloads-btns-div {Cut(packUrl)}");
                    continue;
                }

                int pick = Math.Clamp(episode - 1 < 0 ? 0 : episode - 1, 0, episodeBlocks.Count - 1);

                Collect(packHtml, packUrl, links, episodeBlocks[pick].Inner, episodeBlocks.Count);
            }

            if (links.Count > 0)
                break;
        }

        Console.WriteLine($"movies4u: {links.Count} link file-host ({(tv ? "tv" : "movie")}:{tmdbId})");

        return links.Count == 0 ? null : links.DistinctBy(l => l.Url).ToList();
    }

    /// <summary>Lấy các link file-host trong một khối HTML (toàn trang hoặc một div).</summary>
    void Collect(string html, string baseUrl, List<(string Label, string Url)> into, string scope = null, int blockCount = 0)
    {
        string haystack = string.IsNullOrWhiteSpace(scope) ? html : scope;

        var found = HrefsInDiv(haystack, "downloads-btns-div", 8);

        if (found.Count == 0)
            found = HrefsInDiv(haystack, "download-links-div", 8);

        if (found.Count == 0)
        {
            // một số bài để link trần, không bọc div
            found = Regex.Matches(haystack, @"(?is)<a[^>]+href=""(?<u>[^""]+)""[^>]*>(?<t>.*?)</a>")
                         .Select(m => (Label: Regex.Replace(m.Groups["t"].Value, @"<[^>]+>", " ").Trim(), Url: m.Groups["u"].Value))
                         .ToList();
        }

        int added = 0;

        foreach ((string label, string url) in found)
        {
            string abs = Absolute(Unescape(url), baseUrl);

            if (!LooksLikeFileHost(abs))
                continue;

            into.Add((string.IsNullOrWhiteSpace(label) ? $"link {added + 1}" : Regex.Replace(label, @"\s+", " ").Trim(), abs));

            if (++added >= 8)
                break;
        }

        if (added == 0)
            Console.WriteLine($"movies4u: khối không có file-host nào (blocks={blockCount}) {Cut(baseUrl)}");
    }

    /// <summary>
    /// Các div có class chứa <paramref name="classFragment"/>, giữ cả vị trí để tìm được
    /// heading phía trước (WordPress ở đây đặt "Season 1" ngay trước khối link).
    /// </summary>
    static List<(int Index, string Inner)> DivBlocks(string html, string classFragment)
    {
        var blocks = new List<(int Index, string Inner)>();

        foreach (Match m in Regex.Matches(html, @"(?is)<div[^>]*class=""[^""]*" + Regex.Escape(classFragment) + @"[^""]*""[^>]*>"))
        {
            int start = m.Index + m.Length;
            int end = html.IndexOf("</div>", start, StringComparison.OrdinalIgnoreCase);

            if (end < 0)
                end = Math.Min(html.Length, start + 4000);

            blocks.Add((m.Index, html[start..end]));
        }

        return blocks;
    }

    static string NearestHeadingBefore(string html, int position)
    {
        int from = Math.Max(0, position - 1500);
        string before = html[from..position];

        var matches = Regex.Matches(before, @"(?is)<h([1-6])[^>]*>(?<t>.*?)</h\1>");

        if (matches.Count == 0)
            return "";

        string text = Regex.Replace(matches[matches.Count - 1].Groups["t"].Value, @"<[^>]+>", " ");

        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
