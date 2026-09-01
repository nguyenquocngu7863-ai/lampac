using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MoviesHub;

/// <summary>
/// ĐÃ ĐÓNG 2026-09-01 — không còn trong `manifest.json` → `tree`, thiết bị không compile file này
/// nữa, và `ModInit` không đăng ký nguồn. Lý do ở notes/UHD-MOVIES.md mục 11. GIỮ LẠI vì máy đã
/// xác minh cả chuỗi (search / countdown x2 / driveseed /zfile/ / worker link). GIỮ LẠI vì đó là bằng chứng
/// chạy được của bộ helper Bypass / ResumeLink / LabelBlocks / Playable / IsResume / Unwrap — ai mở
/// nguồn file-host mới có cùng luồng xác minh và chuyển hướng lấy nguyên bộ này dùng.
///
/// UhdMovies (`uhdmovies.autos`) — cùng họ bài-đặt-nút-download như Movies4U nhưng KHÁC resolver:
/// mỗi nút trỏ `…?sid=<blob>` rồi phải qua 2 trang countdown của WordPress trước khi tới trang file
/// DriveLeech/DriveSeed. Thuật toán port từ code CSX còn sống (`CineStreamExtractors.kt:2353`
/// `invokeUhdmovies` + `CineStreamUtils.kt:869` `bypassHrefli`, đọc 2026-09-01) — toàn bộ suy luận
/// và bằng chứng thiết bị ở `notes/UHD-MOVIES.md` (mục 1, 2b, 2c, 2d, 2g).
///
/// Ba luật vùng này giữ nguyên: (1) link file-host là NÚT NGUỒN, không vào menu chất lượng;
/// (2) extractor trả link nào thì phát link đó (302 verbatim, không /proxy, không HLS hop);
/// (3) pack/BATCH không bao giờ được collect — ở đây nó là nút "Zip / Pack".
///
/// Khác Movies4U ở chỗ resolve là lúc BẤM chứ không lúc dựng danh sách: một lượt bypass = 3-6 request,
/// nên 10 tập × 5 nhóm mà resolve hết thì chết timeout (httptimeout 30). Vì vậy HubEntry.Url là
/// CHÍNH url `?sid=` (mồi), còn Resolve() trong file này chạy chuỗi khi player bấm.
/// </summary>
public class UhdmoviesController : HubController
{
    const string Source = "uhdmovies";

    public UhdmoviesController() : base(ModInit.uhd)      // config section riêng: "UhdMovies"
    {
    }

    static string Tag => "uhdmovies:";

    List<HeadersModel> SiteHeaders()
        => HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("Referer", init.host.TrimEnd('/') + "/"));   // host trong config, không hardcode: họ đổi domain là cái chết

    // ----------------------------------------------------------------------------------- routes

    [HttpGet, Staticache(manually: true)]
    [Route("lite/uhdmovies")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
        => CollectionCore(Source, checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson);

    // Không có route video.m3u8: .mkv để GStreamer của Lampac quyết (xem HubController).
    [HttpGet, Staticache(manually: true)]
    [Route("lite/uhdmovies/video")]
    [Route("lite/uhdmovies/file.mkv")]
    [Route("lite/uhdmovies/file.mp4")]
    public async Task<ActionResult> Video(string src, string label, short s = -1, short e = -1, bool play = true)
    {
        StatiCacheDisabled = true;
        SetHeadersNoCache();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
        {
            Console.WriteLine($"{Tag} blocked (enable={init.enable}, rip={init.rip})");
            return badInitMsg ?? OnError("disable", gbcache: false, statusCode: 403);
        }

        string sid = Dec(src);

        if (string.IsNullOrWhiteSpace(sid))
        {
            Console.WriteLine($"{Tag} src thiếu/hỏng trong query video");
            return OnError("stream", 502);
        }

        label = string.IsNullOrWhiteSpace(Dec(label)) ? "stream" : Dec(label);
        List<HubStream> found;

        try
        {
            found = await Resolve(sid, label);
        }
        catch (Exception ex)
        {
            // Không được để nổ: Lampac trả 500 rỗng và mất hết dấu vết trong log.
            Console.WriteLine($"{Tag} ex {ex.GetType().Name} {ex.Message}");
            return OnError("resolve", 502);
        }

        HubStream first = found.FirstOrDefault();

        if (first == null)
        {
            Console.WriteLine($"{Tag} 0 link chơi được từ {Cut(sid)}");
            return OnError("stream", 502);
        }

        Console.WriteLine($"{Tag} play {Cut(first.Url)} [{(init.streamproxy ? "proxy" : "direct")}] ({(s > 0 ? $"S{s}E{e} · " : "")}{label}) qua '{first.Label}' build={Build}");

        if (play)
            return RedirectToPlay(init.streamproxy ? HostStreamProxy(first.Url, headers: first.Headers, force_streamproxy: true) : first.Url);

        return ContentTo(VideoTpl.ToJson(
            "play",
            accsArgs($"{host}/lite/{init.plugin.ToLowerAndTrim()}/{RouteFor(first.Label, first.Url)}?src={Enc(Clean(sid))}&label={Enc(label)}&play=true"),
            label,
            quality: first.Label,
            vast: init.vast,
            httpContext: HttpContext));
    }

    // -------------------------------------------------------------------------------- collection

    protected override async Task<List<HubEntry>> Collect(string source, string imdbId, long tmdbId, short season)
    {
        string site = init.host.TrimEnd('/');
        bool tv = season != 0;
        var headers = SiteHeaders();
        var meta = await TmdbMeta(tmdbId, tv);

        var queries = new List<string>();

        foreach (string name in new[] { meta.originalTitle, meta.title })
        {
            if (string.IsNullOrWhiteSpace(name) || queries.Any(x => x.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
                continue;

            string key = name.Trim();

            queries.Add(tv ? $"{key} season {Math.Max((int)season, 1)}" : $"{key} {meta.year}");
            queries.Add(key);
        }

        if (queries.Count == 0)
        {
            Console.WriteLine($"{Tag} TMDB không trả title để tìm (tmdb={tmdbId})");
            return null;
        }

        string postUrl = null;
        string html = null;

        // ?s= TRƯỚC, /search/<q> sau. Bằng chứng 1/9: `/?s=the+whisper+man` được site tự chuyển về
        // /search/the+whisper+man và trả đúng bài; gọi thẳng /search/<q> (mã hoá %20) lại ra trang
        // "không kết quả" mà HTML vẫn đầy menu (26 anchor) => kiểu cũ "trang đầu tiên thắng, chỉ đổi
        // dạng khi trang RỖNG" không bao giờ thử ?s=. Điều kiện để nhận một trang: có "/download-".
        string[] forms = { "?s={0}", "/search/{0}" };
        int attempt = 0;

        foreach (string query in queries.Take(2))
        {
            string search = null;

            foreach (string form in forms)
            {
                attempt++;

                string page = await GetPage($"{site}/{string.Format(form, Uri.EscapeDataString(query))}", headers);

                if (string.IsNullOrWhiteSpace(page))
                {
                    Console.WriteLine($"{Tag} dạng {form} trả trang rỗng/blocked (lần {attempt})");
                    continue;
                }

                if (page.Contains("/download-"))
                {
                    search = page;
                    Console.WriteLine($"{Tag} tìm ăn ở dạng {form} (lần {attempt})");
                    break;
                }

                search ??= page;   // giữ trang cuối làm nguyên liệu log
                Console.WriteLine($"{Tag} dạng {form} không có bài (lần {attempt}, a={Regex.Matches(page, "(?i)<a[^>]+href=").Count}) — thử dạng kế");
            }

            if (string.IsNullOrWhiteSpace(search))
            {
                Console.WriteLine($"{Tag} trang tìm kiếm rỗng | q={query} site={site}");
                continue;
            }

            // Slug bài của họ luôn bắt đầu bằng /download- (bài thật đọc lại 1/9 xác nhận) — chắc hơn
            // giả định <article>/h3 rất nhiều.
            // onlyFileHost: PHẢI TẮT. Mặc định của Anchors là chỉ giữ link hubcloud/gdflix/driveseed...,
            // còn link KẾT QUẢ TÌM KIẾM nằm ngay trên uhdmovies.autos => bị lọc sạch. Đây là thủ phạm
            // "0 bài ứng viên" trong log 1/9 dù trang tìm kiếm CÓ bài (xem lại tay: /?s=the+whisper+man
            // trả đúng /download-the-whisper-man-2026-.../).
            List<(string Url, string Label)> candidates = [.. Anchors(search, site + "/", 40, onlyFileHost: false)
                                .Where(a => a.Url.Contains("/download-", StringComparison.OrdinalIgnoreCase))
                                .Select(a => (Url: a.Url, Label: Plain(a.Label)))
                                .DistinctBy(x => x.Url)];

            if (candidates.Count == 0)
            {
                Console.WriteLine($"{Tag} 0 bài ứng viên | q='{query}' a={Regex.Matches(search, "(?i)<a[^>]+href=").Count} dh={Regex.Matches(search, "/download-").Count} sid={Regex.Matches(search, @"[?&]sid=").Count} hosts={HostHistogram(search, site)} classes={ClassHistogram(search)}");
                continue;
            }

            // Có IMDb id trong bài thì bắt buộc khớp — tránh ăn phải phim trùng tên.
            string imdb = imdbId?.Trim();

            foreach (var cand in candidates.Take(4))
            {
                string page = await GetPage(cand.Url, headers);

                if (string.IsNullOrWhiteSpace(page))
                    continue;

                if (!string.IsNullOrWhiteSpace(imdb))
                {
                    // Distinct + Contains: bài collection nhét imdb của TỪNG phim (LOT R có 3 id), chỉ
                    // so id đầu tiên là loại nhầm bài đúng. Bài không có imdb nào thì cho qua (nhiều
                    // bài của họ không ghi), chỉ chặn khi bài có id mà không cái nào là của mình.
                    var ids = Regex.Matches(page ?? "", @"imdb\.com/title/(?<id>tt\d{6,8})");
                    bool ok = ids.Count == 0;

                    foreach (Match x in ids)
                        if (x.Groups["id"].Value == imdb)
                            ok = true;

                    if (!ok)
                    {
                        Console.WriteLine($"{Tag} bài lệch IMDb ({ids.Count} id, không có {imdb}) — bỏ {Cut(cand.Url)}");
                        continue;
                    }
                }

                postUrl = cand.Url;
                html = page;
                break;
            }

            if (postUrl != null)
                break;
        }

        if (html == null)
        {
            Console.WriteLine($"{Tag} không lấy được bài nào (tmdb={tmdbId}, queries={queries.Count}) | site={site}");
            return null;
        }

        var all = Groups(html, site);

        if (all.Count == 0)
        {
            Console.WriteLine($"{Tag} 0 nhóm release | {Cut(postUrl)} a={Regex.Matches(html, AnchorPattern).Count} classes={ClassHistogram(html)} hosts={HostHistogram(html, postUrl)}");
            return null;
        }

        // season == -1: series mà người dùng CHƯA chọn mùa. CollectionCore dựng menu mùa từ
        // x.Season > 0 (HubController.cs:182) nên thiếu bước này là màn hình mùa rỗng — đúng cái bẫy
        // đã sửa ở Movies4U (log thiết bị 1/9: chỉ ra "Mùa 1"). Mùa ở đây lấy từ chính nhãn nhóm,
        // cùng nguồn với danh sách nhóm, không bao giờ lệch nhau.
        if (season < 0)
        {
            var seasons = new List<HubEntry>();

            foreach (short n in all.Select(x => x.Season).Where(x => x > 0).Distinct().OrderBy(x => x))
            {
                var first = all.First(x => x.Season == n);
                seasons.Add(new HubEntry($"Mùa {n}", first.Links.Count > 0 ? first.Links[0].Url : postUrl, n, 0));
            }

            if (seasons.Count == 0)
            {
                // Bài của họ thường tách theo mùa (mỗi mùa một bài) hoặc không ghi Season ở nhãn.
                // Trả đúng một phiếu để người dùng còn vào được danh sách nhóm, và in nhãn thật ra
                // log để vòng test sau biết phải tìm bài theo mùa hay lọc khác đi.
                Console.WriteLine($"{Tag} 0 mùa trong bài | nhãn=[{string.Join(" | ", all.Select(x => Cut(x.Heading)))}] — giả định Mùa 1");
                return [new HubEntry("Mùa 1", postUrl, 1, 0)];
            }

            Console.WriteLine($"{Tag} mùa: {seasons.Count} [{string.Join(", ", seasons.Select(x => x.Season))}] đúc từ {all.Count} nhóm");
            return seasons;
        }

        if (tv)
        {
            // So theo Release.Season (đã tra lúc gom nhóm: nhãn S0x, nếu không có thì khối "Season N"
            // phía trên) chứ không Regex lại trên chuỗi nhãn — đó là chỗ bản cũ đánh mất 6/8 tập.
            bool require = all.Any(x => x.Season > 0);
            List<Release> picked = [.. all.Where(x => x.Season == 0 ? !require : x.Season == season).DistinctBy(x => x.Heading)];

            if (picked.Count == 0)
            {
                Console.WriteLine($"{Tag} mùa {season}: {all.Count} nhóm nhưng không nhãn nào nhắc mùa {season} — dùng cả {all.Count}");
                picked = [.. all];
            }

            Console.WriteLine($"{Tag} mùa {season}: {picked.Count} nhóm [{string.Join(" | ", picked.Select(x => Cut(x.Heading)))}]");

            var into = new List<HubEntry>();

            // PHIẾU NHÓM (Episode==0) để CollectionCore dựng màn hình chọn ở Bộ lọc → thuyết minh (?g=N)
            foreach (var g in picked)
                into.Add(new HubEntry(ShortLabel(g.Heading), g.Links.Count > 0 ? g.Links[0].Url : postUrl, season, 0, ShortLabel(g.Heading)));

            int pick = ReleaseGroup > 0 ? Math.Min((int)ReleaseGroup, picked.Count) - 1 : 0;
            var group = picked[Math.Max(pick, 0)];

            foreach ((string label, string url, short ep) in group.Links)
                into.Add(new HubEntry($"Ep {ep} · {QualityLabel($"{group.Heading} {label}", url)}", url, season, ep, ShortLabel(group.Heading)));

            Console.WriteLine($"{Tag} mùa {season} nhóm {Math.Max(pick, 0) + 1}/{picked.Count} '{Cut(group.Heading)}': {group.Links.Count} tập");

            return into;
        }

        // Phim lẻ: mỗi nút = một chất lượng = một nút nguồn (không menu chất lượng). NHƯNG site này
        // hay gộp cả bộ thành MỘT bài ("The Lord of the Rings Collection (2001-2003)" — 3 phim, không
        // có bài riêng từng phim; bằng chứng 1/9). Khi đó mỗi nút thuộc một khối <h2> tên phim, nên:
        // khớp phim đang xem (TMDB title/original_title + năm) rồi chỉ lấy nút của phim đó; không khớp
        // được phim nào thì để hết nhưng DÁN TÊN PHIM vào nhãn để anh còn tự chọn, không nhầm phim.
        List<string> films = [.. all.Select(x => x.Film).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()];
        List<Release> use = all;

        if (films.Count > 1)
        {
            var hit = films.FirstOrDefault(f => SameFilm(f, meta.originalTitle, meta.year) || SameFilm(f, meta.title, meta.year));

            Console.WriteLine($"{Tag} bài collection: {films.Count} phim, tmdb hỏi '{Cut(meta.title)} ({meta.year})' -> '{Cut(hit ?? $"KHÔNG KHỚP trong {films.Count} phim")}'");

            if (!string.IsNullOrWhiteSpace(hit))
                use = [.. all.Where(x => x.Film == hit)];
        }

        var movie = new List<HubEntry>();

        foreach (var g in use)
            foreach ((string label, string url, short ep) in g.Links)
                if (!movie.Any(x => x.Url == url))
                    movie.Add(new HubEntry(QualityLabel($"{g.Film} {g.Heading} {label}", url), url, 0, 0,
                                            films.Count > 1 ? ShortLabel(g.Film) : null));

        Console.WriteLine($"{Tag} movie: {movie.Count} nút từ {use.Count}/{all.Count} nhóm");

        return movie;
    }

    sealed record Release(string Heading, string Film, short Season, List<(string Label, string Url, short Ep)> Links);

    /// <summary>
    /// Toàn bộ nhóm release của một bài viết. Nhãn của từng nút được tra từ danh sách khối nhãn đã quét
    /// MỘT LƯỢT trên cả bài — không phải window vài trăm ký tự: mỗi nút ở họ này mang `?sid=` ~700 ký
    /// tự, nên window ngắn chỉ với tới 2 nút cuối của một nhóm 8 tập; 6 nút kia mất nhãn, mất luôn mã
    /// S0x và bị bộ lọc mùa bỏ. Đó chính là bệnh trong ảnh 1/9 (Reacher S02 chỉ có Ep 1-2 có link).
    ///
    /// Hai kiểu bài, cùng xử lý ở đây (bằng chứng đọc trực tiếp 1/9, note mục 1 & 10):
    ///   * Series: `Season 2` rồi `Reacher.S02.1080p.AMZN.WEB-DL.DUAL.DDP5.1.H.264-YAGAMi`
    ///     + `[3.5 GB/E] [34 GB ZIP]` + `Episode 1..8` + `Zip / Pack` (Zip bỏ — lệnh của anh, vòng 16).
    ///   * Collection phim lẻ: MỘT bài cho cả 3 phim, mỗi phim một `&lt;h2&gt;` tên phim + các dòng nhãn
    ///     TRẦN (không có `&lt;strong&gt;`): "…Fellowship of the Ring (2001) EXTENDED UHD … [27GB]".
    /// </summary>
    List<Release> Groups(string html, string baseUrl)
    {
        var labels = LabelBlocks(html);
        var seasons = SeasonBlocks(html);
        var heads = HeadingBlocks(html);
        var byLabel = new List<(string Heading, string Film, short Season, string Url, short Ep, string Label)>();
        int packs = 0;

        foreach (Match m in Regex.Matches(html ?? "", AnchorPattern))
        {
            string text = Plain(m.Groups["t"].Value);

            if (!Regex.IsMatch(text, @"(?i)(episode\s*\d+|\bdownload\b|\bdown\s*load\b)"))
                continue;

            if (Regex.IsMatch(text, @"(?i)zip|pack|batch|\.?rar\b"))
            {
                packs++;
                continue;
            }

            string url = Absolute(Unescape(HrefValue(m)), baseUrl + "/");

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || byLabel.Any(x => x.Url == url))
                continue;

            // Heading = dòng nhãn cuối cùng TRƯỚC nút. Dòng đó không mang mã mùa (vài bài chỉ ghi
            // "1080p [900MB/E]") thì lấy mùa của khối "Season N" gần nhất phía trên.
            string heading = LastBefore(labels, m.Index) ?? NearestLabelBefore(html, m.Index);
            short season = SeasonOf(heading ?? "");

            if (season == 0)
                season = LastBefore(seasons, m.Index);

            byLabel.Add((string.IsNullOrWhiteSpace(heading) ? "Nhóm" : heading, LastBefore(heads, m.Index) ?? "", season,
                         url, EpisodeOf(text), text));
        }

        if (packs > 0)
            Console.WriteLine($"{Tag} bỏ {packs} nút Zip/Pack/BATCH");

        var out0 = new List<Release>();

        foreach (var x in byLabel)
        {
            var g = out0.FirstOrDefault(gg => gg.Heading == x.Heading && gg.Film == x.Film);

            if (g == null)
                out0.Add(g = new Release(x.Heading, x.Film, x.Season, new List<(string, string, short)>()));

            g.Links.Add((x.Label, x.Url, x.Ep));
        }

        // Tập không ghi số trong nút (vài bài để "Download" trần) thì đánh số theo thứ tự xuất hiện.
        foreach (var g in out0)
        {
            int auto = 1;

            for (int i = 0; i < g.Links.Count; i++)
                if (g.Links[i].Item3 <= 0)
                    g.Links[i] = (g.Links[i].Item1, g.Links[i].Item2, (short)auto);
                else
                    auto = g.Links[i].Item3 + 1;
        }

        Console.WriteLine($"{Tag} {out0.Count} nhóm / {byLabel.Count} nút | mùa=[{string.Join(",", out0.Select(x => x.Season).Distinct().OrderBy(x => x))}] | phim=[{string.Join(" ;; ", out0.Select(x => x.Film).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Select(Cut))}]");

        return [.. out0.Where(g => g.Links.Count > 0)];
    }

    /// <summary>Khối có vẻ là nhãn release/phim: h1..h6 hoặc p, mang CHẤT LƯỢNG và (dung tích hoặc năm).
    /// Không lấy `div`: div bọc cả bài nên text của nó chứa mọi nhãn => gộp nhầm cả bài thành 1 nhóm.
    /// Dãy nút ("Episode 1 Episode 2 … Zip / Pack") không mang 1080p/GB nên tự loại.</summary>
    static List<(int End, string Text)> LabelBlocks(string html)
    {
        var list = new List<(int, string)>();

        foreach (Match m in Regex.Matches(html ?? "", @"(?is)<(?<tag>h[1-6]|p)\b[^>]*>(?<body>.*?)</\k<tag>>"))
        {
            string t = Plain(m.Groups["body"].Value);

            if (t.Length < 20 || !Regex.IsMatch(t, @"(?i)\d{3,4}p|\b4k\b|2160|x26[45]|remux"))
                continue;

            if (!Regex.IsMatch(t, @"(?i)\d+(?:[.,]\d+)?\s?(?:gb|mb)\b|\((?:19|20)\d{2}\)"))
                continue;

            list.Add((m.Index + m.Length, t));
        }

        return list;
    }

    /// <summary>Khối h1..h4 = tên phim (bài collection dựng mỗi phim một h2). Loại tiêu đề bài viết
    /// ("Download Reacher (2022-2025)(Season 1-3) …") vì nó KHÔNG phải tên một phim.</summary>
    static List<(int End, string Text)> HeadingBlocks(string html)
        => [.. Regex.Matches(html ?? "", @"(?is)<(?<tag>h[1-4])\b[^>]*>(?<body>.*?)</\k<tag>>")
                .Select(m => (End: m.Index + m.Length, Text: Plain(m.Groups["body"].Value)))
                .Where(x => x.Text.Length > 6 && !Regex.IsMatch(x.Text, @"(?i)^download\b.*\b(season|collection)\b"))];

    static List<(int End, short N)> SeasonBlocks(string html)
        => [.. Regex.Matches(html ?? "", @"(?i)(?:season|serie)\s*\.?\s*(?<n>\d{1,2})\b")
                .Select(m => (End: m.Index + m.Length, N: short.TryParse(m.Groups["n"].Value, out short n) ? n : (short)0))
                .Where(x => x.N > 0)];

    /// <summary>Khối CUỐI cùng kết thúc trước `pos`. Danh sách đã theo thứ tự xuất hiện nên duyệt
    /// ngược là đủ — một bài chỉ vài chục khối, không cần nhị phân.</summary>
    static string LastBefore(List<(int End, string Text)> blocks, int pos)
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
            if (blocks[i].End <= pos)
                return blocks[i].Text;

        return null;
    }

    static short LastBefore(List<(int End, short N)> blocks, int pos)
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
            if (blocks[i].End <= pos)
                return blocks[i].N;

        return 0;
    }

    /// <summary>Có phải cùng một phim không? Bài collection ghi "The Lord of the Rings: The Fellowship
    /// of the Ring (2001)" còn TMDB trả "The Lord of the Rings: The Fellowship of the Ring" — so sau
    /// khi bỏ mọi thứ không phải chữ/số, nên phần đuôi cũng khớp được.</summary>
    static bool SameFilm(string heading, string want, int year)
    {
        string a = Norm(heading);
        string b = Norm(want);

        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        int same = a.Split(' ').Intersect(b.Split(' ')).Count();
        int need = Math.Min(3, b.Split(' ').Length);

        if (!(a.Contains(b) || b.Contains(a) || same >= need))
            return false;

        var years = Regex.Matches(heading ?? "", @"\((?<y>(?:19|20)\d{2})\)");

        // Hai bên đều có năm thì năm phải khớp: nhãn "(2002)" không được nhận vơ cho phim 2001
        return year <= 0 || years.Count == 0 || years.Any(y => y.Groups["y"].Value == year.ToString());
    }

    static string Norm(string v)
        => string.Join(" ", Regex.Split((v ?? "").ToLowerInvariant().Replace("&", " and "), @"[^a-z0-9]+").Where(x => x.Length > 0));

    // ------------------------------------------------------------------------------- the extractor

    /// <summary>
    /// `?sid=` -> trang file -> link CDN. Thứ tự ưu tiên theo "cái này tua được không" (báo cáo thiết
    /// bị 2026-09-01: Resume Cloud / worker CF cho link play được; worker chết thì chỉ còn link
    /// download, không resume) — KHÁC CSX, vốn ném hết mọi nút cho extractor:
    /// Resume Cloud -> Direct Links (CF) -> Resume Worker Bot -> Instant/Cloud Download (gắn nhãn
    /// [download], xuống đáy danh sách vì player không seek được).
    /// </summary>
    async Task<List<HubStream>> Resolve(string sid, string label)
    {
        var jar = new CookieContainer();
        var found = new List<HubStream>();
        string file = sid;

        // Link đã là driveseed/driveleech thì bỏ qua chuỗi countdown (CSX cũng làm vậy, tiết kiệm 3 request)
        if (!file.Contains("driveseed", StringComparison.OrdinalIgnoreCase) && !file.Contains("driveleech", StringComparison.OrdinalIgnoreCase))
        {
            file = await Bypass(sid, jar);

            if (file == null)
            {
                Console.WriteLine($"{Tag} bypass không tới được trang file (sid={Cut(sid)})");
                return found;
            }
        }

        string page = await Get(file, jar, OriginOf(sid));

        if (string.IsNullOrWhiteSpace(page))
        {
            Console.WriteLine($"{Tag} trang file rỗng {Cut(file)}");
            return found;
        }

        // Driveseed/Driveleech đôi khi nháy thêm một lần nữa
        string again = Regex.Match(page, @"replace\(\s*[""'](?<u>[^""']+)[""']\s*\)").Groups["u"].Value;

        if (!string.IsNullOrWhiteSpace(again))
        {
            string hop = Absolute(again, file);

            if (hop != file)
            {
                page = await Get(hop, jar, OriginOf(file));
                file = hop;   // PHẢI đổi base: href tương đối + HostOf phía sau tính theo trang MỚI
                Console.WriteLine($"{Tag} trang file nháy tiếp -> {Cut(hop)}");
            }
        }

        // Trang file bị Cloudflare chặn? Lampac có sẵn đường vòng: httpHydra.Get(..., safety: true) đi
        // qua rch/rhub (init.conf: rhub + rch_access). RCH KHÔNG giữ CookieContainer nên chỉ dùng được
        // ở bước cuối này — chuỗi countdown vẫn phải tự chạy tay.
        if (Challenge(page))
        {
            Console.WriteLine($"{Tag} trang file là challenge — thử qua rch (init.conf rhub)");

            string viaRch = await httpHydra.Get(file, addheaders: PlayHeaders(), statusCodeOK: false, safety: true);

            if (!string.IsNullOrWhiteSpace(viaRch) && !Challenge(viaRch) && viaRch.Length > (page ?? "").Length)
            {
                page = viaRch;
                Console.WriteLine($"{Tag} rch ăn, trang file {page.Length}B");
            }
            else
                Console.WriteLine($"{Tag} rch cũng không qua được (len={(viaRch ?? "").Length})");
        }

        // Tên file + dung tích nằm ngay trên trang file => nhãn nút tốt hơn mọi suy đoán từ slug
        string fname = Text(page, @"list-group-item[^>]*>\s*Name\s*:?\s*(?<v>[^<\n]{3,300})");
        string fsize = Text(page, @"list-group-item[^>]*>\s*Size\s*:?\s*(?<v>[^<\n]{1,60})");

        if (string.IsNullOrWhiteSpace(fname))
            fname = Clean(Cut(Regex.Match(page ?? "", @"(?is)<title[^>]*>(?<t>.*?)</title>").Groups["t"].Value));

        // driveseed.org/r?key=<base64> là endpoint ĐỔI LINK: nhiều khi nó 302 thẳng tới CDN. Một cú
        // GetLocation (không tự follow) rẻ hơn cả loạt nút, và là đường duy nhất nếu trang là SPA.
        string direct = await Http.GetLocation(file, referer: OriginOf(sid), headers: PlayHeaders());

        if (!string.IsNullOrWhiteSpace(direct))
        {
            direct = Absolute(direct, file);

            if (Playable(direct, file))
            {
                found.Add(new HubStream(direct, QualityLabel($"{fname} {fsize} · redirect", direct), PlayHeaders()));
                Console.WriteLine($"{Tag} ăn: redirect {Cut(direct)} (khỏi cần đọc nút)");
                return found;
            }

            Console.WriteLine($"{Tag} /r?key= không 302 ra file (Location={Cut(direct)}) — đọc nút");
        }

        var buttons = Buttons(page, file);

        foreach ((string href, string kind, int rank) in buttons.OrderBy(x => x.Rank))
        {
            try
            {
                string link = kind switch
                {
                    // Resume Cloud / Resume Worker Bot / mọi nút không tên khác: Ở HỌ NÀY CÙNG MỘT
                    // ĐƯỜNG — hoặc trang đích nhúng sẵn link worker, hoặc form /download?id= + token
                    // trả JSON .url. Link worker là thứ DUY NHẤT tua được; người dùng báo 1/9:
                    // "bạn toàn get nhầm link install download" vì Resume Cloud fail nên rơi xuống Instant.
                    "resume" or "worker" or "btn" or "center" => await ResumeLink(Absolute(href, file), jar, file),
                    "cf" => await CfLinks(Absolute(href, file), jar, file),
                    _ => (await Http.GetLocation(Absolute(href, file), referer: OriginOf(file), headers: PlayHeaders())) is string loc && !string.IsNullOrWhiteSpace(loc)
                            ? Absolute(loc, file) : Absolute(href, file)
                };

                link = string.IsNullOrWhiteSpace(link) ? link : Unwrap(Clean(link));

                if (string.IsNullOrWhiteSpace(link) || !Playable(link, file, loose: kind is not ("media" or "ext" or "center")))
                    continue;

                if (found.Any(x => x.Url == link))
                    continue;

                // Nhãn nói THẲNG vào mặt: tua được hay chỉ tải một lèo. video-downloads.googleusercontent.com
                // play được nhưng không seek; worker-*.workers.dev/<hex>::<hex>/ten.mkv thì tua bình thường.
                string tag = IsResume(link) ? " · tua được" : " [download]";

                found.Add(new HubStream(link, QualityLabel($"{fname} {fsize} · {kind}{tag}", link), PlayHeaders()));
                Console.WriteLine($"{Tag} ăn: {kind}{tag} {Cut(link)}");

                // Có link worker (tua được) rồi thì KHÔNG gọi thêm nút nào nữa — mỗi nút là 1-3 request
                // mà init.httptimeout chỉ 30s cho cả một tập. Nhưng vẫn ăn MIỄN PHÍ các nút còn lại
                // trên trang (chỉ đọc href, không request): người dùng cần bản "tải" phòng worker die
                // (dặn vòng 21), mà Instant của driveseed lại là link cdn thẳng, không cần resolve.
                if (IsResume(link))
                {
                    foreach (var rest in buttons)
                    {
                        if (rest.Rank < 3)
                            continue;

                        string alt = Unwrap(Clean(Absolute(rest.Href, file)));

                        if (found.Any(f => f.Url == alt) || !Playable(alt, file))
                            continue;

                        found.Add(new HubStream(alt, QualityLabel($"{fname} {fsize} · {rest.Kind} [download]", alt), PlayHeaders()));
                        Console.WriteLine($"{Tag} thêm miễn phí: {rest.Kind} {Cut(alt)}");
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                // worker die là chuyện thường xuyên ở họ này: bỏ qua, thử nút kế — không fail cả tập
                Console.WriteLine($"{Tag} nút {kind} fail ({ex.GetType().Name}) — thử nút kế tiếp");
            }
        }

        if (found.Count == 0)
        {
            string title = Text(page ?? "", @"(?is)<title[^>]*>(?<v>.*?)</title>");
            bool challenge = Challenge(page);

            Console.WriteLine($"{Tag} 0 link chơi được trên {Cut(file)} | title='{title}' a={Regex.Matches(page ?? "", AnchorPattern).Count} hosts={HostHistogram(page, file)} nút=[{AnchorDump(page, 8)}] head={Cut(Regex.Replace(page ?? "", @"\s+", " "))}");

            if (challenge)
                Console.WriteLine($"{Tag} ĐÂY LÀ TRANG CHALLENGE (Cloudflare/JS), không phải trang thiếu nút: hết đường không-JS -> bật rch trong init.conf rồi thử lại");
        }
        else
            Console.WriteLine($"{Tag} {found.Count} link cho '{Cut($"{fname} {fsize}")}' (nút: {string.Join(",", buttons.Select(x => x.Kind))})");

        return found;
    }

    /// <summary>
    /// Chuỗi vượt 2 trang countdown — y hệt `bypassHrefli` của CSX (CineStreamUtils.kt:869), nhưng
    /// lặp thay vì đếm cứng 2 lần: mỗi lượt là một cái countdown, site thêm bước thứ 3 thì mình vẫn
    /// sống. CookieJar dùng chung từ đầu (thiếu cookie là bị đá về form đầu tiên).
    /// </summary>
    async Task<string> Bypass(string url, CookieContainer jar)
    {
        string host0 = OriginOf(url);
        var html = await Get(url, jar, host0 + "/");
        int rounds = 0;
        string lastWp2 = "";

        for (int i = 0; i < 5; i++)
        {
            var (action, form) = Landing(html);

            if (string.IsNullOrWhiteSpace(action) || form == null || form.Count == 0)
                break;

            rounds++;
            lastWp2 = form.FirstOrDefault(f => f.Name == "_wp_http2").Value ?? "";
            string referer = OriginOf(url) + "/";
            html = await Post(Absolute(action, host0), form, jar, referer);

            if (string.IsNullOrWhiteSpace(html))
            {
                Console.WriteLine($"{Tag} bypass round {i + 1}: trang rỗng/blocked");
                return null;
            }
        }

        // ?go= nằm trong <script> của trang SAU các POST; token vừa là tên cookie vừa là key query
        string go = Regex.Match(html ?? "", @"\?go=(?<t>[A-Za-z0-9+/=_-]+)").Groups["t"].Value;

        if (string.IsNullOrWhiteSpace(go))
        {
            Console.WriteLine($"{Tag} bypass hết form (rounds={rounds}) mà không thấy ?go= | len={(html ?? "").Length} head={Cut(Regex.Replace(html ?? "", @"\s+", " "))}");
            return null;
        }

        string wp2 = lastWp2;
        var next = await Get($"{host0}?go={go}", jar, host0 + "/", cookieName: go, cookieValue: wp2);

        string meta = Regex.Match(next ?? "", @"(?is)refresh[^>]*url=(?<u>[^""'>\s]+)").Groups["u"].Value;

        if (string.IsNullOrWhiteSpace(meta))
        {
            Console.WriteLine($"{Tag} bypass: ?go= không ra meta refresh | len={(next ?? "").Length} head={Cut(Regex.Replace(next ?? "", @"\s+", " "))}");
            return null;
        }

        string file = Absolute(meta, host0);
        Console.WriteLine($"{Tag} bypass ok (rounds={rounds}) -> {Cut(file)}");

        return file;
    }

    /// <summary>form#landing + toàn bộ input của nó (CSX gửi hết, không chọn từng field — ít vỡ hơn).</summary>
    static (string Action, List<(string Name, string Value)> Fields) Landing(string html)
    {
        var m = Regex.Match(html ?? "", @"(?is)<form[^>]*id\s*=\s*[""']?landing[""']?[^>]*>(?<body>.*?)</form>");

        if (!m.Success)
            return (null, null);

        var fields = new List<(string, string)>();

        foreach (Match i in Regex.Matches(m.Groups["body"].Value, @"(?is)<input\b[^>]*>"))
        {
            string name = Attr(i.Value, "name");
            string value = Attr(i.Value, "value");

            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrEmpty(value))
                fields.Add((name, value));
        }

        return (Attr(m.Value, "action"), fields);
    }

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag ?? "", @"(?is)(?:^|[\s""])" + name + @"\s*=\s*(?:""(?<d>[^""]*)""|'(?<s>[^']*)'|(?<p>[^\s""'>]+))");

        return !m.Success ? "" : m.Groups["d"].Success ? m.Groups["d"].Value
             : m.Groups["s"].Success ? m.Groups["s"].Value : m.Groups["p"].Value;
    }

    /// <summary>Các nút trên trang file, kèm hạng theo "tua được không".</summary>
    static List<(string Href, string Kind, int Rank)> Buttons(string page, string file)
    {
        var list = new List<(string, string, int)>();
        string fileHost = HostOf(file);

        // CSX (Extractors.kt:556 Driveleech.getUrl) chỉ cần "div.text-center > a" là lấy được hết nút
        // của driveseed/driveleech. Ghi lại các khối đó để nút KHÔNG TÊN (chỉ có icon) vẫn được nhận.
        var centers = new List<(int A, int B)>();

        foreach (Match c in Regex.Matches(page ?? "", @"(?is)<div[^>]*class\s*=\s*[""'][^""']*text-center[^""']*[""'][^>]*>(?<b>.*?)</div>"))
            centers.Add((c.Groups["b"].Index, c.Groups["b"].Index + c.Groups["b"].Length));

        foreach (Match m in Regex.Matches(page ?? "", AnchorPattern))
        {
            string text = Plain(m.Groups["t"].Value).ToLowerInvariant();
            string href = Unescape(HrefValue(m));

            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#") || href.StartsWith("javascript", StringComparison.OrdinalIgnoreCase))
                continue;

            string abs = Absolute(href, file);

            if (JunkLink(abs, text))
                continue;

            // Ba tầng nhận nút: (1) từ khoá DriveLeech cũ, (2) class btn* (UI mới của driveseed chỉ ghi
            // "Download"), (3) link CÓ VẼ FILE = mang đuôi media hoặc ở host KHÁC host trang file.
            // Log 1/9 chết vì thiếu tầng 2+3: trang có 11 anchor, cdn.video-gen.xyz:1 chính là file.
            string kind =
                text.Contains("resume cloud") || text.Contains("cloud resume") ? "resume" :
                text.Contains("direct link") ? "cf" :
                text.Contains("worker") ? "worker" :
                text.Contains("instant") ? "instant" :
                text.Contains("cloud download") ? "cloud" :
                Attr(m.Value, "class").Contains("btn", StringComparison.OrdinalIgnoreCase) ? "btn" :
                IsMedia(abs) ? "media" :
                centers.Any(r => m.Index >= r.A && m.Index < r.B) ? "center" :
                !abs.Contains(fileHost, StringComparison.OrdinalIgnoreCase) ? "ext" : null;

            if (kind == null || list.Any(x => x.Item1 == href))
                continue;

            list.Add((href, kind, kind switch { "resume" => 0, "cf" => 1, "worker" => 2, "instant" => 3, "cloud" => 4, "media" => 5, "btn" => 6, "center" => 7, _ => 8 }));
        }

        return [.. list];
    }

    /// <summary>Rác trên trang file: menu, logo, kênh social, ảnh/js — không bao giờ là link xem.</summary>
    static bool JunkLink(string url, string text)
        => Regex.IsMatch(text ?? "", @"(?i)report|dmca|copyright|contact|privacy|polic|terms|\bhome\b|about|follow|channel|upload file|search|login|log in|sign in|register|premium|buy ")
        || Regex.IsMatch(url ?? "", @"(?i)(?:^|//)(?:t\.me|telegram|twitter\.com|facebook\.com|instagram\.com|pinterest|wa\.me)|(?:^|/)login\?|\.(?:png|jpe?g|webp|gif|ico|svg|css|js|woff2?)(?:\?|$)");

    static string HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri u) ? u.Host : "";

    /// <summary>Link có được đưa vào menu play không. loose = nhận cả link cùng host (driveseed hay
    /// nhét /dl/&lt;hash&gt; không đuôi) với điều kiện nó không phải chính trang vừa lấy.</summary>
    static bool Playable(string url, string file, bool loose = false)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri u))
            return false;

        if (Regex.IsMatch(u.AbsolutePath, @"(?i)\.(png|jpe?g|webp|gif|ico|svg|css|js|woff2?)$"))
            return false;

        if (IsMedia(url) || !u.Host.Equals(HostOf(file), StringComparison.OrdinalIgnoreCase))
            return true;

        return loose && StripAnchor(url) != StripAnchor(file);
    }

    static string StripAnchor(string url) => Regex.Replace(url ?? "", @"[#?].*$", "").TrimEnd('/');

    /// <summary>Dấu vết trang Cloudflare/JS. Nhận ra nó để (a) log nói đúng bệnh,
    /// (b) thử lại qua rch.</summary>
    static bool Challenge(string html)
        => Regex.IsMatch(html ?? "", @"(?i)just a moment|attention required|verify you are|access denied|cf-challenge|cf_clearance|checking your browser|pardon our interruption|enable javascript|security check|before you can access");

    /// <summary>driveleech/driveseed hay nhả Location kiểu `https://host/dl?url=&lt;file&gt;` — phần CHƠI
    /// ĐƯỢC là cái nằm sau `?url=` (CSX: instantLink = location.substringAfter("?url=")). Cả Location
    /// mà đưa cho player là ăn một trang HTML nhảy tiếp, đúng cái bẫy "link có mà không play".</summary>
    static string Unwrap(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        var m = Regex.Match(url, @"[?&]url=(?<v>[^&]+)", RegexOptions.IgnoreCase);

        if (!m.Success)
            return url;

        string inner = Uri.UnescapeDataString(m.Groups["v"].Value);

        return inner.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? inner : url;
    }

    /// <summary>Nguyên liệu cho vòng sửa selector: in mọi anchor của trang, kể cả cái bị bỏ.</summary>
    static string AnchorDump(string html, int max = 8)
    {
        var dump = new List<string>();

        foreach (Match m in Regex.Matches(html ?? "", AnchorPattern))
        {
            if (dump.Count >= max)
                break;

            string href = Unescape(HrefValue(m));

            if (string.IsNullOrWhiteSpace(href))
                continue;

            string text = Plain(m.Groups["t"].Value);
            dump.Add($"'{(string.IsNullOrWhiteSpace(text) ? "(trống)" : Cut(text))}'->{Cut(href)}");
        }

        return string.Join(" ;; ", dump);
    }

    /// <summary>
    /// RESUME CLOUD = <c>/zfile/&lt;id&gt;</c>. Đọc thẳng trang thật 1/9 (driveseed.org/file/b4cXjsCvPdz0nPXw8KnX):
    ///   Resume Cloud      -> https://driveseed.org/zfile/b4cXjsCvPdz0nPXw8KnX
    ///   Instant Download  -> https://cdn.video-gen.xyz/&lt;hex&gt;::&lt;hex&gt;   (302 sang Google = CHỈ TẢI, không seek)
    ///   Login to download -> https://driveseed.org/login?ref=/file/…        (rác, JunkLink chặn)
    /// Log vòng trước ("ăn: instant …google…") là hậu quả của việc /zfile/ fail nên Resolve rơi xuống
    /// Instant. /zfile/ thường 302 THẲNG tới worker CF (link có tên file ở đuôi -> tua được) nên thử
    /// GetLocation trước: một request, khỏi đọc HTML. Các bước sau là cho driveleech/UI cũ.
    /// </summary>
    async Task<string> ResumeLink(string url, CookieContainer jar, string referer)
    {
        string loc = await Http.GetLocation(url, referer: OriginOf(referer), headers: PlayHeaders());

        if (!string.IsNullOrWhiteSpace(loc))
        {
            loc = Clean(Absolute(loc, url));

            if (IsResume(loc) || IsMedia(loc))
                return loc;
        }

        string page = await Get(url, jar, OriginOf(referer));
        string hit = WorkerUrl(page);

        if (!string.IsNullOrWhiteSpace(hit))
            return Clean(hit);

        hit = BtnUrl(page, url);

        if (!string.IsNullOrWhiteSpace(hit))
            return hit;

        hit = await DownloadIdLink(page, url, jar);

        if (!string.IsNullOrWhiteSpace(hit))
            return hit;

        Console.WriteLine($"{Tag} resume fail: {Cut(url)} | len={(page ?? "").Length} title='{Text(page ?? "", @"(?is)<title[^>]*>(?<v>.*?)</title>")}' — rơi xuống nút khác");
        return null;
    }

    /// <summary>Link worker/R2: thứ DUY NHẤT trong họ này tua được (Range + resume). Link Google
    /// (video-downloads…) và cdn.video-gen.xyz chỉ là link tải — chơi một lèo, không seek.</summary>
    static bool IsResume(string url)
        => !string.IsNullOrWhiteSpace(url) && Regex.IsMatch(url, @"(?i)workers\.dev|r2\.dev|video-leech\.pro");

    /// <summary>Worker link nhúng sẵn trong HTML/JS (`window.location = "https://worker-….workers.dev/…"`).</summary>
    static string WorkerUrl(string page)
    {
        var m = Regex.Match(page ?? "", @"https?://[A-Za-z0-9\.\-_]+(?:workers\.dev|r2\.dev|video-leech\.pro)[A-Za-z0-9\.\-_/?:=%&+]*");

        if (m.Success)
            return Unescape(m.Value);

        // "<256 hex>::<32 hex>" không host cũng đủ nhận ra — cả họ này chỉ file mới mang ::
        var h = Regex.Match(page ?? "", @"""(?<u>https?://[^""]*::[^""]+)""");

        return h.Success ? Unescape(h.Groups["u"].Value) : null;
    }

    /// <summary>Nút <c>class*="btn"</c> đầu tiên trông như file (DriveLeech cũ: a.btn-success).</summary>
    static string BtnUrl(string page, string baseUrl)
    {
        foreach (Match m in Regex.Matches(page ?? "", AnchorPattern))
        {
            if (!Attr(m.Value, "class").Contains("btn", StringComparison.OrdinalIgnoreCase))
                continue;

            string href = Unescape(HrefValue(m));

            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("javascript", StringComparison.OrdinalIgnoreCase))
                continue;

            string abs = Clean(Absolute(href, baseUrl));

            if (IsResume(abs) || IsMedia(abs) || Playable(abs, baseUrl))
                return abs;
        }

        return null;
    }

    /// <summary>Resume Worker Bot: token + <c>/download?id=</c> trong script, POST giữ cookie -> JSON .url.</summary>
    async Task<string> DownloadIdLink(string page, string url, CookieContainer jar)
    {
        if (string.IsNullOrWhiteSpace(page))
            return null;

        string token = Regex.Match(page, @"(?i)token['""\s]*[:=,]\s*[""'](?<v>[A-Za-z0-9+/=_\-]{8,})[""']").Groups["v"].Value;
        string path = Regex.Match(page, @"(?<p>/download\?id=[A-Za-z0-9+/=_%\-]+)").Groups["p"].Value;

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(path))
            return null;

        string json = await Post($"{OriginOf(url)}{path}", new List<(string, string)> { ("token", token) }, jar, url, xhr: true);
        string link = JsonUrl(json);

        return string.IsNullOrWhiteSpace(link) ? null : Clean(Absolute(link, url));
    }

    static string JsonUrl(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var t = JObject.Parse(json);

            foreach (string key in new[] { "url", "link", "file", "download" })
            {
                string v = t.Value<string>(key);

                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }

            var d = t["data"];

            if (d != null)
                foreach (string key in new[] { "url", "link" })
                {
                    string v = d.Value<string>(key);

                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }
        }
        catch (Exception)
        {
            // JSON hỏng / lẫn HTML: còn đường regex bên dưới, không nổ cả tập vì một nút
        }

        var m = Regex.Match(json, @"https?://[^""\\ ]+(?:::|workers\.dev)[^""\\ ]*");

        return m.Success ? m.Value : null;
    }

    /// <summary>Trang DriveLeech kiểu Cloudflare: <c>?type=1</c> rồi <c>?type=2</c>, lấy a.btn-success.</summary>
    async Task<string> CfLinks(string url, CookieContainer jar, string referer)
    {
        foreach (string t in new[] { "1", "2" })
        {
            string html = await Get($"{url}{(url.Contains('?') ? "&" : "?")}type={t}", jar, OriginOf(referer));

            foreach (Match a in Regex.Matches(html ?? "", AnchorPattern))
            {
                if (!Regex.IsMatch(a.Value, @"(?is)class\s*=\s*[""'][^""']*btn-success", RegexOptions.IgnoreCase))
                    continue;

                string href = Absolute(Unescape(HrefValue(a)), url);

                if (IsMedia(href))
                    return href;
            }
        }

        return null;
    }

    // -------------------------------------------------------------------------------------- plumbing

    /// <summary>Header cho lúc play: UA là bắt buộc — Google/CF cắt request không User-Agent.</summary>
    List<HeadersModel> PlayHeaders()
        => HeadersModel.Init(("User-Agent", Http.UserAgent), ("Accept", "*/*"));

    async Task<string> Get(string url, CookieContainer jar, string referer, string cookieName = null, string cookieValue = null)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));

        if (!string.IsNullOrWhiteSpace(referer))
            headers.AddRange(HeadersModel.Init(("Referer", referer)));

        if (!string.IsNullOrWhiteSpace(cookieName))
            headers.AddRange(HeadersModel.Init(("Cookie", $"{cookieName}={cookieValue}")));

        return await httpHydra.Get(url, addheaders: headers, statusCodeOK: false, cookieContainer: jar);
    }

    async Task<string> Post(string url, List<(string Name, string Value)> form, CookieContainer jar, string referer, bool xhr = false)
    {
        string body = string.Join("&", form.Select(x => $"{Uri.EscapeDataString(x.Name)}={Uri.EscapeDataString(x.Value)}"));

        var headers = HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Content-Type", "application/x-www-form-urlencoded"));

        if (!string.IsNullOrWhiteSpace(referer))
            headers.AddRange(HeadersModel.Init(("Referer", referer), ("Origin", OriginOf(referer))));

        if (xhr)
            headers.AddRange(HeadersModel.Init(("X-Requested-With", "XMLHttpRequest")));

        return await httpHydra.Post(url, body, addheaders: headers, statusCodeOK: false, cookieContainer: jar);
    }

    static string OriginOf(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri u) && !string.IsNullOrWhiteSpace(u.Host))
            return $"{u.Scheme}://{u.Host}";

        return url ?? "";
    }

    static string Text(string html, string pattern)
        => Clean(Regex.Match(html ?? "", pattern).Groups["v"].Value.Trim());

    /// <summary>Link CDN của họ luôn là file media; không có đuôi thì coi như không chơi được.</summary>
    static bool IsMedia(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri u))
            return false;

        return Regex.IsMatch(u.AbsolutePath, @"(?i)\.(mkv|mp4|m4v|avi|mov|ts|m3u8)(\.[a-z0-9]+)?$");
    }

    static short SeasonOf(string text)
    {
        var m = Regex.Match(text ?? "", @"(?i)(?:season\s*(?<a>\d{1,2})|\bs(?<b>\d{2})\b)");

        if (!m.Success)
            return 0;

        return short.TryParse(m.Groups["a"].Success ? m.Groups["a"].Value : m.Groups["b"].Value, out short n) ? n : (short)0;
    }

    static short EpisodeOf(string text)
    {
        var m = Regex.Match(text ?? "", @"(?i)(?:episode|ep\.?|s\d{1,2}e)\s*:?\s*(?<n>\d{1,3})");

        return m.Success && short.TryParse(m.Groups["n"].Value, out short n) ? n : (short)0;
    }

    /// <summary>"Season 4 [Hindi ORG. + English] 480p [250MB/E]" -> "480p [250MB/E]": chip ngắn mà vẫn tự nhận mùa nào.</summary>
    static string ShortLabel(string heading)
    {
        string t = Regex.Replace(heading ?? "", @"(?i)season\s*\d+\s*[-–]?\s*", "").Trim();

        return string.IsNullOrWhiteSpace(t) ? heading : Cut(t);
    }
}
