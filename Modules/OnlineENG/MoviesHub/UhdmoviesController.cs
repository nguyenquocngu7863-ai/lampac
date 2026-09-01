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

                if (!string.IsNullOrWhiteSpace(imdb) && page.Contains("imdb.com/title/", StringComparison.OrdinalIgnoreCase))
                {
                    string found = Regex.Match(page, @"imdb\.com/title/(?<id>tt\d{6,8})").Groups["id"].Value;

                    if (!string.IsNullOrWhiteSpace(found) && found != imdb)
                    {
                        Console.WriteLine($"{Tag} bài lệch IMDb ({found} ≠ {imdb}) — bỏ {Cut(cand.Url)}");
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

            foreach (short n in all.Select(x => SeasonOf(x.Heading)).Where(x => x > 0).Distinct().OrderBy(x => x))
            {
                var first = all.First(x => SeasonOf(x.Heading) == n);
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
            bool require = all.Any(x => SeasonOf(x.Heading) > 0);
            List<Release> picked = [.. all.Where(x => SeasonMatches(x.Heading, season, require)).DistinctBy(x => x.Heading)];

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

        // Phim lẻ: mỗi nút = một chất lượng = một nút nguồn (không menu chất lượng).
        var movie = new List<HubEntry>();

        foreach (var g in all)
            foreach ((string label, string url, short ep) in g.Links)
                if (!movie.Any(x => x.Url == url))
                    movie.Add(new HubEntry(QualityLabel($"{g.Heading} {label}", url), url, 0, 0));

        Console.WriteLine($"{Tag} movie: {movie.Count} nút từ {all.Count} nhóm");

        return movie;
    }

    sealed record Release(string Heading, List<(string Label, string Url, short Ep)> Links);

    /// <summary>Dòng <c>&lt;strong&gt;</c> gần nút nhất CÓ dấu vết chất lượng (2160p / 1080p / 4K /
    /// x265) — tức tên bản release, chứ không phải dòng dung tích nằm sát nút.</summary>
    static string ReleaseLine(string html, int before)
    {
        int from = Math.Max(0, before - 1500);
        var strongs = Regex.Matches(html[from..before], @"(?is)<strong[^>]*>(?<t>.*?)</strong>");

        for (int i = strongs.Count - 1; i >= 0; i--)
        {
            string t = Plain(strongs[i].Groups["t"].Value);

            if (!string.IsNullOrWhiteSpace(t) && Regex.IsMatch(t, @"(?i)\d{3,4}p|\b4k\b|2160|x26[45]"))
                return t;
        }

        return "";
    }

    /// <summary>
    /// Mọi nút tải của bài viết. CSX chọn "dòng &lt;p&gt; nhắc S0?N/Season 0?N rồi lấy phần tử KẾ TIẾP"
    /// (CineStreamExtractors.kt:2375) — tức release-name và dãy nút là hai khối anh-em. Em làm tương
    /// đương mà không phụ thuộc cấu trúc cha/con: với mỗi anchor tải, nhãn nhóm = khối text ngay trước
    /// nó (NearestLabelBefore đã bắt cả &lt;p&gt; lẫn h1-h6, và đã lọc theo 1080p/4K/GB).
    /// "Zip / Pack" (pack cả mùa) loại bằng chữ TRÊN NÚT — người dùng dặn không lấy.
    /// </summary>
    List<Release> Groups(string html, string baseUrl)
    {
        var byLabel = new List<(string Heading, string Url, short Ep, string Label)>();
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

            // Bài thật 1/9: dòng NGAY TRÊN nút chỉ là "**[16.42 GB]**", còn tên mang 2160p / x265 /
            // (KRATOS-UHDMovies) nằm ở dòng trên nữa. Mỗi mình NearestLabelBefore thì 4 nút movie ra
            // nhãn "16 GB / 13 GB..." không phân biệt được bản nào — gom cả hai dòng.
            string label = NearestLabelBefore(html, m.Index);
            string rel = ReleaseLine(html, m.Index);
            string heading = string.IsNullOrWhiteSpace(rel) ? label
                           : string.IsNullOrWhiteSpace(label) ? rel : $"{rel} {label}";

            byLabel.Add((string.IsNullOrWhiteSpace(heading) ? "Nhóm" : heading, url, EpisodeOf(text), text));
        }

        if (packs > 0)
            Console.WriteLine($"{Tag} bỏ {packs} nút Zip/Pack/BATCH");

        var out0 = new List<Release>();

        foreach (var x in byLabel)
        {
            var g = out0.FirstOrDefault(gg => gg.Heading == x.Heading);

            if (g == null)
                out0.Add(g = new Release(x.Heading, new List<(string, string, short)>()));

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

        return [.. out0.Where(g => g.Links.Count > 0)];
    }

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

        if (!string.IsNullOrWhiteSpace(again) && !again.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            page = await Get(Absolute(again, file), jar, OriginOf(file));
            Console.WriteLine($"{Tag} trang file nháy tiếp -> {Cut(file + again)}");
        }
        else if (!string.IsNullOrWhiteSpace(again))
        {
            page = await Get(again, jar, OriginOf(file));
            file = again;
        }

        // Tên file + dung tích nằm ngay trên trang file => nhãn nút tốt hơn mọi suy đoán từ slug
        string fname = Text(page, @"list-group-item[^>]*>\s*Name\s*:?\s*(?<v>[^<\n]{3,300})");
        string fsize = Text(page, @"list-group-item[^>]*>\s*Size\s*:?\s*(?<v>[^<\n]{1,60})");

        if (string.IsNullOrWhiteSpace(fname))
            fname = Clean(Cut(Regex.Match(page ?? "", @"(?is)<title[^>]*>(?<t>.*?)</title>").Groups["t"].Value));

        var buttons = Buttons(page, file);

        foreach ((string href, string kind, int rank) in buttons.OrderBy(x => x.Rank))
        {
            try
            {
                string link = kind switch
                {
                    "resume" => await LinkFrom(Absolute(href, file), jar, file, @"btn-success"),
                    "cf" => await CfLinks(Absolute(href, file), jar, file),
                    "worker" => await WorkerLink(Absolute(href, file), jar, file),
                    _ => (await Http.GetLocation(Absolute(href, file), referer: OriginOf(file), headers: PlayHeaders())) is string loc && !string.IsNullOrWhiteSpace(loc)
                            ? Absolute(loc, file) : Absolute(href, file)
                };

                if (string.IsNullOrWhiteSpace(link) || !IsMedia(link))
                    continue;

                if (found.Any(x => x.Url == link))
                    continue;

                string tag = kind == "instant" || kind == "cloud" ? " [download]" : "";

                found.Add(new HubStream(link, QualityLabel($"{fname} {fsize} · {kind}{tag}", link), PlayHeaders()));
                Console.WriteLine($"{Tag} ăn: {kind} {Cut(link)}");
            }
            catch (Exception ex)
            {
                // worker die là chuyện thường xuyên ở họ này: bỏ qua, thử nút kế — không fail cả tập
                Console.WriteLine($"{Tag} nút {kind} fail ({ex.GetType().Name}) — thử nút kế tiếp");
            }
        }

        if (found.Count == 0)
            Console.WriteLine($"{Tag} 0 link chơi được trên {Cut(file)} | a={Regex.Matches(page ?? "", AnchorPattern).Count} hosts={HostHistogram(page, file)} head={Cut(Regex.Replace(page ?? "", @"\s+", " "))}");
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

        foreach (Match m in Regex.Matches(page ?? "", AnchorPattern))
        {
            string text = Plain(m.Groups["t"].Value).ToLowerInvariant();
            string href = Unescape(HrefValue(m));

            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#") || href.StartsWith("javascript", StringComparison.OrdinalIgnoreCase))
                continue;

            string kind =
                text.Contains("resume cloud") || text.Contains("cloud resume") ? "resume" :
                text.Contains("direct link") ? "cf" :
                text.Contains("worker") ? "worker" :
                text.Contains("instant") ? "instant" :
                text.Contains("cloud download") ? "cloud" : null;

            if (kind == null || list.Any(x => x.Item2 == kind && x.Item1 == href))
                continue;

            list.Add((href, kind, kind switch { "resume" => 0, "cf" => 1, "worker" => 2, "instant" => 3, _ => 4 }));
        }

        return [.. list];
    }

    async Task<string> LinkFrom(string url, CookieContainer jar, string referer, string selector)
    {
        string html = await Get(url, jar, OriginOf(referer));
        var m = Regex.Match(html ?? "", @"(?is)<a\b[^>]*class\s*=\s*[""'][^""']*" + selector + @"[^""']*[""'][^>]*>");

        return m.Success ? Absolute(Unescape(HrefValue(m)), url) : null;
    }

    /// <summary>Direct Links = trang Cloudflare-type: ?type=1 rồi ?type=2, lấy a.btn-success.</summary>
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

    /// <summary>
    /// Resume Worker Bot: token + id nằm trong <script>, POST /download?id=... rồi ăn JSON .url.
    /// Worker chết là thường (báo cáo thiết bị) => exception được nuốt ở Resolve và thử nút kế.
    /// </summary>
    async Task<string> WorkerLink(string url, CookieContainer jar, string referer)
    {
        string html = await Get(url, jar, OriginOf(referer));

        if (string.IsNullOrWhiteSpace(html))
            return null;

        string token = Regex.Match(html, @"formData\.append\(\s*['""]token['""]\s*,\s*['""](?<v>[a-fA-F0-9]+)['""]\s*\)").Groups["v"].Value;
        string id = Regex.Match(html, @"fetch\(\s*['""](?<p>/download\?id=[^'""]+)['""]\s*\)").Groups["p"].Value;

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(id))
        {
            Console.WriteLine($"{Tag} worker: không thấy token/id trong script | len={html.Length}");
            return null;
        }

        string json = await Post($"{OriginOf(url)}{id}", new List<(string, string)> { ("token", token) }, jar, url, xhr: true);
        string link = json == null ? null : (JObject.Parse(json).Value<string>("url") ?? "");

        return string.IsNullOrWhiteSpace(link) ? null : Absolute(link, url);
    }

    // -------------------------------------------------------------------------------------- plumbing

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

    static bool SeasonMatches(string text, short season, bool require)
    {
        short n = SeasonOf(text);

        return n > 0 ? n == season : !require;
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
