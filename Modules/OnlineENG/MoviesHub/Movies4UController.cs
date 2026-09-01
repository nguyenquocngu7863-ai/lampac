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

    /// <summary>
    /// Danh sách mùa cho serial. Trước đây em đọc từ khối `downloads-btns-div` — trên bài Reacher
    /// khối đó KHÔNG TỒN TẠI, nên module chỉ ra đúng một "Mùa 1" (log thiết bị 1/9). Giờ mùa được đúc
    /// ra từ CHÍNH các nút "Download Links" của bài: số Season trong nhãn của nút = một mùa. Nhờ vậy
    /// menu mùa và danh sách nhóm của mùa đó luôn cùng một nguồn, không bao giờ lệch nhau.
    /// </summary>
    void CollectSeasons(string html, string postUrl, List<HubEntry> into)
    {
        var groups = AllGroups(html, postUrl);
        var found = groups.Where(x => x.Season > 0).Select(x => x.Season).Distinct().OrderBy(x => x).ToList();

        foreach (short n in found)
        {
            var first = groups.First(x => x.Season == n);
            into.Add(new HubEntry($"Mùa {n}", first.Url, n, 0));
        }

        Console.WriteLine($"{Tag} mùa: {found.Count} [{string.Join(", ", found)}] đúc từ {groups.Count} nút download");
    }

    async Task CollectEpisodes(string html, string postUrl, List<HeadersModel> headers, short season, List<HubEntry> into)
    {
        // Movies4U đặt link theo NHÓM cho từng mùa: mỗi "Season 4 [Hindi ORG. + Multi Audio]
        // 1080p [900MB/E]" là một heading + một nút DOWNLOAD LINKS dẫn sang trang riêng (ảnh chụp
        // của người dùng: 480p/720p/1080p/1080p-4GB/2160p cho cùng Season 4). Trước đây em chỉ lấy
        // Links[0] của khối mùa => MỌI tập đúng 1 link, và mất hết bản khác. Giờ: trả phiếu nhóm cho
        // CollectionCore dựng màn hình chọn (?g=N), và khi đã chọn thì chỉ dịch trang của nhóm đó.
        List<(string Heading, string Url)> groups = GroupsForSeason(html, postUrl, season);

        if (groups.Count == 0)
        {
            Console.WriteLine($"{Tag} mùa {season}: không thấy nhóm release nào | classes={ClassHistogram(html)} | hosts={HostHistogram(html, postUrl)}");
            return;
        }

        // PHIẾU NHÓM luôn được trả về, kể cả khi đã chọn (g>0), vì CollectionCore cần danh sách đó
        // để dựng VoiceTpl — người dùng phải đổi nhóm được NGAY TRONG danh sách tập, không phải
        // quay lại một màn hình chọn riêng.
        foreach ((string head, string url) in groups)
            into.Add(new HubEntry(head, url, season, 0, head));

        if (groups.Count > 1)
            // In MỌI nhãn nhóm mỗi lần mở mùa: đây là thứ duy nhất cho biết NearestLabelBefore lấy
            // đúng dòng mô tả chưa (nếu nhãn vẫn là "Download Links 900MB" thì dòng text đó nằm SAU
            // nút chứ không trước, và em cần đổi hướng quét) — khỏi phải hỏi anh thêm một vòng.
            Console.WriteLine($"{Tag} mùa {season} nhãn nhóm: [{string.Join(" | ", groups.Select(g => g.Heading))}]");

        // Chưa chọn thì dùng nhóm đầu (Mirage cũng mặc định nhóm đầu tiên: "if (t == -1) t = id").
        int pick = ReleaseGroup > 0 ? Math.Min((int)ReleaseGroup, groups.Count) - 1 : 0;
        string groupUrl = groups[pick].Url;

        // Nhóm trỏ thẳng file-host (bài đơn giản không có trang trung gian)
        if (LooksLikeFileHost(groupUrl))
        {
            into.Add(new HubEntry(QualityLabel($"Mùa {season} · {groups[pick].Heading}", groupUrl), groupUrl, season, 0, groups[pick].Heading));
            return;
        }

        string inner = await GetPage(groupUrl, headers);

        if (string.IsNullOrWhiteSpace(inner))
        {
            Console.WriteLine($"{Tag} trang nhóm '{Cut(groups[pick].Heading)}' rỗng/blocked {Cut(groupUrl)}");
            return;
        }

        var epBlocks = DivBlocks(inner, "downloads-btns-div", 80);
        int links = 0;

        for (int i = 0; i < epBlocks.Count; i++)
        {
            short ep = EpisodeNumber(epBlocks[i].Heading);

            if (ep <= 0)
                ep = (short)(i + 1);

            // MỌI host trong cùng một tập (trước đây chặn ở 2): tập có 3 link thì vào streamquality
            // hết, người dùng chọn host ngay trong danh sách tập (README: biến thể được phép ở TẬP,
            // cái bị cấm là nhét link phim lẻ vào menu chất lượng).
            foreach ((string label, string raw) in epBlocks[i].Links.Take(6))
            {
                string abs = Absolute(Unescape(raw), groupUrl);

                if (!LooksLikeFileHost(abs) || DeadHost(abs))
                    continue;

                into.Add(new HubEntry($"Ep {ep} · {QualityLabel(label, abs)}", abs, season, ep, groups[pick].Heading));
                links++;
            }
        }

        if (links == 0)
        {
            // Tầng trang nhóm TRÊN SITE NÀY KHÔNG DÙNG CLASS NÀO CẢ. Đã đọc trực tiếp
            // https://m4ulinks.site/number/62782:
            //     ##### -:Episodes: 1:-
            //     [🚀 Hub-Cloud [DD]](https://hubcloud.cx/drive/kk1lk7kvdmvim8m) [🚀 GDFlix](https://gdflix.dev/…)
            //     ##### -:Episodes: 2:- …
            // Nên mỗi tập = một heading, các anchor file-host ngay sau nó là các host của tập đó
            // (GDFlix đã bị DeadHost chặn). Bucket theo heading là cách duy nhất không phụ thuộc class.
            var headings = new List<string>();
            var perHeading = new Dictionary<string, List<(string Label, string Url)>>();

            foreach (Match a in Regex.Matches(inner, AnchorPattern))
            {
                string url = Absolute(Unescape(HrefValue(a)), groupUrl);

                if (!LooksLikeFileHost(url) || DeadHost(url))
                    continue;

                string head = NearestHeadingBefore(inner, a.Index);

                if (!perHeading.TryGetValue(head, out var list))
                {
                    list = new List<(string Label, string Url)>();
                    perHeading[head] = list;
                    headings.Add(head);
                }

                list.Add((Plain(a.Groups["t"].Value), url));
            }

            for (int i = 0; i < headings.Count; i++)
            {
                short ep = EpisodeNumber(headings[i]);

                if (ep <= 0)
                    ep = (short)(i + 1);

                foreach ((string label, string url) in perHeading[headings[i]].Take(6))
                {
                    into.Add(new HubEntry($"Ep {ep} · {QualityLabel(label, url)}", url, season, ep, groups[pick].Heading));
                    links++;
                }
            }

            if (links > 0)
                Console.WriteLine($"{Tag} mùa {season}: trang nhóm không có downloads-btns-div -> {headings.Count} heading tập, {links} link");
        }

        Console.WriteLine($"{Tag} mùa {season} nhóm {pick + 1}/{groups.Count} '{Cut(groups[pick].Heading)}': {epBlocks.Count} khối, {links} link");

        if (links == 0)
            Console.WriteLine($"{Tag} nhóm đã chọn không có file-host | len={inner.Length} a={Regex.Matches(inner, "(?i)<a[^>]+href=").Count} hosts={HostHistogram(inner, groupUrl)} classes={ClassHistogram(inner)}");
    }

    /// <summary>Toàn bộ nút nhóm của bài viết (mọi mùa): nhãn = heading ngay trước nút, Season đọc từ
    /// nhãn đó. Đây là cấp cao nhất Movies4U đặt tên ổn định, và theo log thiết bị thì class cũng đổi,
    /// nên module không dựa vào class ở tầng này nữa.</summary>
    List<(string Heading, string Url, short Season)> AllGroups(string html, string postUrl)
    {
        var all = new List<(string Heading, string Url, short Season)>();
        int batches = 0;

        foreach (Match m in Regex.Matches(html ?? "", AnchorPattern))
        {
            string text = Plain(m.Groups["t"].Value);

            // Nút của nhóm là nút có chữ "Download Links"; mọi thứ khác (Telegram, Telegram Filter,
            // phụ đề, báo lỗi) không phải nhóm.
            if (!Regex.IsMatch(text, @"(?i)download\s*-?\s*links?\b"))
                continue;

            // BATCH/ZIP: người dùng dặn đừng lấy. Nhiều nhóm trỏ CÙNG một id zip nên phải loại bằng
            // CHỮ TRÊN NÚT TRƯỚC khi dedupe theo url, nếu không link thật bị mất.
            if (Regex.IsMatch(text, @"(?i)batch|\.?zip\b"))
            {
                batches++;
                continue;
            }

            string url = Absolute(Unescape(HrefValue(m)), postUrl);

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || all.Any(x => x.Url == url))
                continue;

            string label = NearestLabelBefore(html, m.Index);

            all.Add((string.IsNullOrWhiteSpace(label) ? $"Nhóm {all.Count + 1}" : label, url, SeasonNumber(label)));
        }

        if (batches > 0)
            Console.WriteLine($"{Tag} bỏ {batches} nút BATCH/ZIP (đúng yêu cầu: không lấy pack trọn bộ)");

        if (all.Count == 0)
            Console.WriteLine($"{Tag} 0 nhóm | a={Regex.Matches(html ?? "", AnchorPattern).Count} classes={ClassHistogram(html)} hosts={HostHistogram(html, postUrl)}");

        return all;
    }

    /// <summary>
    /// Mọi nhóm release của MỘT mùa. Cấu trúc THẬT của new5.movies4u.clinic (đọc trực tiếp bài
    /// Reacher (Season 1-4) ngày 1/9, không đoán):
    ///
    ///   &lt;h4&gt;Season 4 [Hindi ORG. + English] 480p [250MB/E]&lt;/h4&gt;
    ///   &lt;a href="https://m4ulinks.site/number/62782"&gt;🚀 Download Links &lt;/a&gt;
    ///   &lt;a href="https://m4ulinks.site/number/36737"&gt;🚀 BATCH/ZIP [1.5GB] 🚀&lt;/a&gt;   (cùng dòng, KHÁC nút)
    ///
    /// Nên: quét ANCHOR, không dựa vào class nào hết (class đổi là nguồn chết); một nhóm = một nút có
    /// chữ "Download Links"; BATCH/ZIP loại bằng CHỮ TRÊN NÚT (bản cũ kiểm trên heading là sai, vì
    /// heading của nhóm chứa BATCH nằm ở dòng khác — hệ quả là nhóm bị ăn nhầm và menu rỗng).
    /// Link nhóm trỏ sang m4ulinks.site/number/&lt;id&gt; — trang trung gian, TỤT HẠ domain theo site nên
    /// không được hardcode host: chỉ cần nút đúng chữ là nhận.
    /// </summary>
    List<(string Heading, string Url)> GroupsForSeason(string html, string postUrl, short season)
    {
        // kiểu tường minh: C# không suy được kiểu đích cho collection expression khi dùng var (CS9176)
        List<(string Heading, string Url)> all = [.. AllGroups(html, postUrl).Select(x => (x.Heading, x.Url))];

        // Có nhãn nào tự nhắc mùa không? Có => BẮT BUỘC khớp mùa. Đây là chỗ sửa lỗi "cả 4 mùa hiện y
        // hệt nhau": mỗi nhóm trên site đều đề "Season N" nên tách được sạch; bài một mùa (không nhãn
        // nào nhắc season) thì nhận hết.
        bool require = all.Any(x => SeasonNumber(x.Heading) > 0);

        List<(string Heading, string Url)> picked = [.. all.Where(x => SeasonHeadingMatches(x.Heading, season, require))
                                                           .DistinctBy(x => x.Heading)];

        if (picked.Count == 0 && require)
        {
            Console.WriteLine($"{Tag} mùa {season}: {all.Count} nhóm nhưng không nhãn nào nhắc mùa {season} — dùng cả {all.Count} nhóm");
            picked = [.. all];
        }

        if (picked.Count == 0)
            Console.WriteLine($"{Tag} mùa {season}: 0 nhóm | a={Regex.Matches(html ?? "", AnchorPattern).Count} nhãn=[{string.Join(" | ", all.Select(x => Cut(x.Heading)))}] | classes={ClassHistogram(html)}");

        // Nhãn làm ngắn (bỏ "Season N" thừa). CollectionCore so khớp đúng chuỗi này => một nguồn sự
        // thật cho cả chỗ hiển thị lẫn chỗ lọc.
        return [.. picked.Select(x => (Heading: GroupShort(x.Heading) ?? x.Heading, Url: x.Url))];
    }

    /// <summary>Nhãn có nhắc đúng mùa đang xem không. require=true (site CÓ ghi mùa trên từng nhóm)
    /// thì nhãn không nhắc mùa bị loại — nhờ 4 mùa không dùng chung một danh sách. require=false (bài
    /// một mùa, không nhãn nào nhắc season) thì nhận hết.</summary>
    static bool SeasonHeadingMatches(string text, short season, bool require = false)
    {
        short n = SeasonNumber(text);

        if (n > 0)
            return n == season;

        return !require;
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
        // "Episodes: 1" / "Ep 3" / "Episode 03" / "S04E05" / "-:Episodes: 12:-" — trang m4ulinks.site
        // viết "-:Episodes: 1:-", mẫu cũ ep(?:isode)?\.?\s* không khớp nổi chữ "Episodes:" (có 's' và
        // dấu chấm hỏi), nên mọi tập bị đánh số theo thứ tự thay vì theo heading.
        var m = Regex.Match(text ?? "", @"(?is)e(?:pi)?sod(?:e|es)?[^0-9]{0,4}(?<n>\d{1,3})");

        if (!m.Success)
            m = Regex.Match(text ?? "", @"(?i)\bep\.?\s*(?<n>\d{1,3})\b");

        if (!m.Success)
            m = Regex.Match(text ?? "", @"(?i)\bS?\d{0,2}E(?<n>\d{1,3})\b");

        if (!m.Success)
        {
            // "-:Episodes: 1:-" / "Tập 1" / "Ep. 1" — lấy số đứng một mình quanh dấu :-
            m = Regex.Match(text ?? "", @"(?:^|[\s:.\-])(?<n>\d{1,3})(?=[\s:.\-]*[-:]?$)");
        }

        if (!m.Success)
            return 0;

        int.TryParse(m.Groups["n"].Value, out int num);

        return (short)Math.Clamp(num, 0, 999);
    }
}
