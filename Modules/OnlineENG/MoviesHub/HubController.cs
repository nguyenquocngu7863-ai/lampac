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
using System.Text;
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

    /// <summary>
    /// Mồi cho Modules/GStreamer/plugins/gst.js: plugin bật GStreamer khi VÀ CHỈ khi phần PATH của
    /// url kết thúc bằng .mkv (nó cắt query trước khi test: url.split('#')[0].split('?')[0], rồi
    /// /\.mkv$/ — nên query string thoải mái, chỉ path là tính). Không có .mkv trong path thì Lampa
    /// tự phát bằng ExoPlayer = mất E-AC3/DDP 5.1 ("mất tiếng" anh hỏi). WebStreamr, Sootio,
    /// AIOStreams, K20 đều dùng cách này: MỘT action, nhiều route alias file.mkv / file.mp4 / video.
    /// </summary>
    protected static string RouteFor(string label, string url)
        => Regex.IsMatch($"{label} {url}", @"(?i)\.mp4\b") ? "file.mp4" : "file.mkv";

    /// <summary>
    /// Marker của bản build, in vào MỌI log collection/play. Lampac compile module bằng Roslyn
    /// TRONG BỘ NHỚ (Core/Startup.cs -> Shared/Services/CSharpEval.cs) nên không có dll trên đĩa mà
    /// hash, phải đánh tay: mỗi commit sửa MoviesHub là đổi chuỗi này (luật trong README). Log ra
    /// marker khác = máy đang compile bản cũ -> dừng việc sửa code, kéo lại từ commit đó.
    /// </summary>
    protected const string Build = "v15-play-302";

    /// <summary>
    /// Host mà module này không thể tự chơi: gdflix/gdlink/go2link bị Cloudflare js challenge,
    /// không Playwright trên Android là chết. Mỗi nút kiểu đó = 6 dòng log vô ích, nên xoá khỏi
    /// menu (yêu cầu của người dùng) thay vì để họ bấm rồi đoán. Lọc ở 2 lớp: nguồn + CollectionCore.
    /// </summary>
    protected static bool DeadHost(string url)
        => Regex.IsMatch(url ?? "", @"(?i)gdflix|gdlink|go2link");

    /// <summary>
    /// Dọn link TRƯỚC KHI dùng, chỉ những gì bắt buộc:
    ///  - &amp;amp; -> &amp; : link presigned của HubCloud nằm trong href của HTML nên nhiều khi còn
    ///    nguyên entity; để nguyên thì R2 nhận tham số "amp;X-Amz-Credential" -> 400/403 -> Lampa báo
    ///    "Không play được". Chữa ở đây vì log sẽ im lặng nếu chỉ chữa một đường.
    ///  - khoảng trắng -> %20: ClearStreamUri/RedirectToPlay của Shared CẮT url ở khoảng trắng đầu
    ///    tiên, nên ".../Movie 4K.mkv" sẽ thành ".../Movie".
    /// Không dùng Uri.AbsoluteUri để "escape" như bản trước: round-trip qua Uri có thể đổi cách mã
    /// hóa query, mà query của link presigned (X-Amz-Signature/Credential) chạm vào là chết.
    /// </summary>
    protected static string Clean(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        string c = url.Trim().Trim('"', '\'', '`', '<', '>');

        while (c.Contains("&amp;", StringComparison.OrdinalIgnoreCase))
            c = c.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

        c = c.Replace("&#038;", "&").Replace("&#38;", "&").Replace("%252F", "%2F");

        return c;
    }

    /// <summary>
    /// Clean() + đổi khoảng trắng thành %20 (player và ClearStreamUri không chịu được space). Không
    /// có gì khác: link presigned mà bị "chuẩn hóa" thêm (Uri.AbsoluteUri, escape lại query) là
    /// X-Amz-Signature lệch một ký tự cũng thành 403.
    /// </summary>
    protected static string NormalizeUrl(string url)
    {
        string raw = url?.Trim() ?? "";
        string c = Clean(url);

        if (string.IsNullOrEmpty(c))
            return c;

        c = c.Replace(" ", "%20");

        if (c != raw)
            Console.WriteLine($"movieshub: link có ký tự rác (&amp; / nháy / space) — đã sửa, nếu player vẫn 403 thì gửi em dòng này: {Cut(c)}");

        return c;
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

        // Chặn cuối cho yêu cầu "xoá nút gdflix": MỌI đường thu thập (gate, anchor trần, pack mùa)
        // đều đi qua danh sách này nên không còn khả năng lọt nút chết vào menu.
        List<HubEntry> shown = [.. entries.Where(x => !DeadHost(x.Url))];

        if (shown.Count != entries.Count)
            Console.WriteLine($"{Log(source)} bỏ {entries.Count - shown.Count} nút gdflix/gdlink/go2link khỏi menu");

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

            foreach (HubEntry item in shown.Where(x => x.Episode > 0))
            {
                // method:"play" + accsArgs — sao chép Sootio (Controller.cs:116-158 + BuildVideoEndpoint
                // :858 + BuildMovieTemplate:857 "play"): url đi thẳng vào player, Lampac 302 sang link
                // trần. KHÔNG dùng "call" (JSON) vì player sẽ phải tự gọi /lite mà không có token access
                // của accsArgs -> Lampac chặn -> "Không play được" dù link r2 vẫn tốt.
                string link = accsArgs($"{host}/lite/{plugin}/{RouteFor(item.Label, item.Url)}?src={Enc(Clean(item.Url))}&label={Enc(item.Label)}&s={item.Season}&e={item.Episode}&play=true");

                etpl.Append(item.Label, title ?? original_title, item.Season, item.Episode, link, "play",
                            streamlink: link);
            }

            if (etpl.IsEmpty)
                Console.WriteLine($"{Log(source)} mùa {s} không có tập nào (entries={entries.Count})");

            return ContentTpl(etpl);
        }

        var mtpl = new MovieTpl(title, original_title);

        foreach (HubEntry item in shown)
        {
            string link = accsArgs($"{host}/lite/{plugin}/{RouteFor(item.Label, item.Url)}?src={Enc(Clean(item.Url))}&label={Enc(item.Label)}&play=true");

            mtpl.Append(item.Label, link, "play", stream: link,
                        details: TryCreate(item.Url, out Uri u) ? u.Host : null);
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

        Console.WriteLine($"{Log(source)} {entries.Count} link cho collection (tmdb={tmdbId}, season={season}, build={Build})");

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

        //triết lý của người dùng, cũng là của Sootio (addon Stremio): extractor trả link nào thì
        // phát link đó. Không tự ý nhét file mkv tiến bộ vào /proxy của mình: token proxy làm mất
        // đuôi .mkv trong url mà Lampa thấy (gst.js chỉ nhìn path, không nhìn 302) và thêm một hop
        // cắt Range không cần thiết. Proxy chỉ còn cho host BẮT BUỘC header lạ — xem PlayUrl.
        string direct = NormalizeUrl(first.Url);

        Console.WriteLine($"{Log(source)} play {Cut(direct)} [{(init.streamproxy ? "proxy" : "direct")}] ({(s > 0 ? $"S{s}E{e} · " : "")}{label}) build={Build}");

        if (play)
            return RedirectToPlay(PlayUrl(first, direct));

        // method:"call" thì BẮT BUỘC trả JSON. Bản trước em để play=true mặc định nên route này trả
        // 302 -> Lampa theo redirect, nhận nguyên xác file mkv rồi cố parse JSON -> CHẾT CẢ hai
        // nguồn (log thiết bị: mọi link hỏng sau khi đổi sang mkv).
        //
        // url trong JSON là CHÍNH route .mkv của module + &play=true: path vẫn kết thúc .mkv nên
        // gst.js bật GStreamer (giữ được tiếng DDP/E-AC3), còn 302 bên dưới thì GStreamer hoặc
        // ExoPlayer tự theo được. Link gốc (trang chia sẻ) được truyền lại qua src= để mỗi lần bấm
        // là extractor resolve lại — link r2 presigned hết hạn sau ~1 giờ mà lưu lại là toi.
        return ContentTo(VideoTpl.ToJson(
            "play",
            accsArgs($"{host}/lite/{init.plugin.ToLowerAndTrim()}/{RouteFor(label, direct)}?src={Enc(Clean(link))}&label={Enc(label)}&play=true"),
            label,
            quality: first.Label,
            vast: init.vast,
            httpContext: HttpContext
        ));
    }

    /// <summary>
    /// Link mà player nhận = link trần mà extractor vừa trả. Đây là yêu cầu thẳng của người dùng
    /// ("extraktor được link như nào thì phát như ấy", giống Sootio) và là điều kiện để gst.js còn
    /// thấy đuôi .mkv: mỗi lần bọc vào /proxy/{token} là path mất đuôi file, plugin hết bắt, lại ra
    /// ExoPlayer. Ai cần header lạ cho một host nào đó thì bật "streamproxy": true cho ĐÚNG section
    /// của nguồn đó trong init.conf — force_streamproxy để vẫn proxy khi Lampac tắt serverproxy.
    /// headers_stream của module không áp vào đây: player tự gửi UA của nó, còn link r2 presigned
    /// của HubCloud thì không cần header gì cả.
    /// </summary>
    string PlayUrl(HubStream stream, string direct)
        => init.streamproxy ? HostStreamProxy(direct, headers: stream.Headers, force_streamproxy: true) : direct;

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
        bool blocked = string.IsNullOrWhiteSpace(html);

        html ??= "";

        // drive link nằm ngay trong trang (nhiều bài nhét gdrive ở nút thứ hai)
        found.AddRange(FromGoogleDrive(html, label));

        foreach (Match m in Regex.Matches(html, @"https?://[^""'\s<>]+?/[^""'\s<>/?]*\.(?:mkv|mp4|m4v|mov|avi|mkv\.js|ts|m3u8)(?:\?[^""'\s<>]*)?", RegexOptions.IgnoreCase))
        {
            string media = Unescape(m.Value);

            if (media.Length > 20 && !media.Contains("poster") && !media.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                found.Add(new HubStream(media, QualityLabel(label, media), StreamHeaders(url)));
        }

        if (found.Count == 0)
        {
            // quét URL trần thay vì href=""..."" — cùng lý do nháy đơn ở trên
            foreach (Match m in Regex.Matches(html, @"https?://[^""'\s<>\\]+?/drive/download/[^""'\s<>\\]+", RegexOptions.IgnoreCase))
                found.Add(new HubStream(Unescape(m.Value), QualityLabel(label, m.Value), StreamHeaders(url)));
        }

        if (found.Count > 0)
            return found.DistinctBy(x => x.Url).ToList();

        // == extractor: bước BẮT BUỘC của hai engine này (CSX: class HubCloud / class GDFlix) ==
        // Trang chia sẻ không chứa link chơi được, nên không thể chỉ regex trên HTML của nó.
        if (IsHubHost(url) || IsGdHost(url))
        {
            var ext = IsHubHost(url) ? await HubExtract(source, url, html, label) : await GdExtract(source, url, html, label);

            if (ext.Count > 0)
            {
                Console.WriteLine($"{Log(source)} extractor trả {ext.Count} link chơi được");
                return ext;
            }
        }

        // (d) Trang file của HubCloud/GDFlix là app JS: HTML không chứa link tải, và đó chính là
        //     tình huống trong log thiết bị ("len=20070, head=<title>(Movies4u.Foo).Mutiny.2026.
        //     480p...mkv</title>" — có tên file, không có url). Engine này có 2 cửa:
        //       ?downloadfile=true  -> 302 vào file thô
        //       /drive/<id>/<tên>.mkv -> phục vụ thẳng theo id (tên chỉ để player đoán định dạng)
        //     Tên file lấy từ <title>. GetLocation dùng HEAD-ish (ResponseHeadersRead) nên không
        //     tải cả film, và vẫn đi qua proxy của Lampac.
        var filepage = Regex.Match(url, @"(?<root>https?://[^/]+)/(?<seg>drive|dr|d|file)/(?<id>[A-Za-z0-9_-]{10,})", RegexOptions.IgnoreCase);

        if (filepage.Success)
        {
            string root = filepage.Groups["root"].Value;
            string seg = filepage.Groups["seg"].Value;
            string id = filepage.Groups["id"].Value;

            string name = Regex.Match(html, @"(?is)<title>\s*(?<t>[^<|]*\.(?:mkv|mp4|m4v|mov|avi))", RegexOptions.IgnoreCase).Groups["t"].Value.Trim();

            string ask = url.Contains('?') ? $"{url}&downloadfile=true" : $"{url}?downloadfile=true";
            string loc = await Http.GetLocation(ask, referer: url, headers: StreamHeaders(url));

            if (IsMediaPath(Absolute(loc, url)))
            {
                Console.WriteLine($"{Log(source)} 302 của downloadfile -> {Cut(Absolute(loc, url))}");
                return [new HubStream(Absolute(loc, url), QualityLabel(label, loc), StreamHeaders(url))];
            }

            if (name.Length > 4)
            {
                string built = $"{root}/{seg}/{id}/{Uri.EscapeDataString(name)}";
                Console.WriteLine($"{Log(source)} dựng link từ <title>: {Cut(built)}");
                return [new HubStream(built, QualityLabel(label, name), StreamHeaders(url))];
            }

            Console.WriteLine($"{Log(source)} trang file không có link tải, <title> không có tên file, GetLocation={(loc == null ? "null" : Cut(loc))}");
        }

        // (m) MoviesDrive không đặt link HubCloud thẳng vào bài: nút chất lượng trỏ sang trang
        //     rút gọn của họ (log: mdrive.lol), trong trang đó mới có link HubCloud/GDFlix.
        //     CSX gọi bước này là extractMdrive(). Thử 302 trước (rẻ), rồi mới đọc trang.
        if (depth == 0 && !IsHubHost(url) && !IsGdHost(url))
        {
            string via = Absolute(await Http.GetLocation(url, headers: StreamHeaders(url)), url);

            if (IsPlayable(via) && via != url && (IsHubHost(via) || IsGdHost(via) || IsMediaPath(via)))
            {
                Console.WriteLine($"{Log(source)} trang trung gian 302 -> {Cut(via)}");
                return await ResolveHub(source, via, label, depth + 1);
            }

            // C# 12 KHÔNG suy được kiểu cho collection expression khi đích là var (CS9176) —
            // phải ghi List<string> tường minh.
            List<string> middle = [.. Links(html).Select(x => Absolute(x.Href, url))
                                       .Where(u => IsHubHost(u) || IsGdHost(u) || IsMediaPath(u))
                                       .Distinct(StringComparer.OrdinalIgnoreCase)];

            foreach (string hop in middle.Take(3))
            {
                var more = await ResolveHub(source, hop, label, depth + 1);

                if (more.Count > 0)
                {
                    Console.WriteLine($"{Log(source)} trang trung gian {Cut(url)} -> {Cut(hop)}");
                    return more;
                }
            }

            if (blocked)
                Console.WriteLine($"{Log(source)} trang trung gian {Cut(url)} không đọc được (blocked), không có link HubCloud/GDFlix");
            else if (middle.Count == 0)
                Console.WriteLine($"{Log(source)} trang trung gian {Cut(url)} có {Links(html).Count} anchor nhưng không có link HubCloud/GDFlix | hosts={HostHistogram(html, url)}");
        }

        // Trang chia sẻ đọc được mà không còn cách nào khác. Để (m) chạy TRƯỚC cả khi blocked vì
        // Http.GetLocation vẫn theo được 302 trên trang mà httpHydra coi là challenge.
        if (blocked)
        {
            Console.WriteLine($"{Log(source)} trang file-host rỗng/blocked {Cut(url)} — host đổi hoặc Cloudflare challenge");
            return found;
        }

        if (depth == 0)
        {
            // Trang tìm kiếm/tìm-lại-file của HubCloud: chưa phải file -> nhảy tiếp. Quét URL TRẦN
            // (không chỉ href="") vì trang file hay nhét link vào chuỗi JS/onclick.
            // id của engine này là chuỗi random ~15 ký tự; {6,} từng bắt cả "/drive/assets" (file tĩnh)
            foreach (string file in Regex.Matches(html, @"https?://[^""'\s<>\\]+?/(?:drive|dr|d|file)/[A-Za-z0-9_-]{13,}(?:/[^""'\s<>\\]*)?", RegexOptions.IgnoreCase)
                                             .Select(m => Absolute(Unescape(m.Value), url))
                                             .Where(u => u.Split('?')[0] != url.Split('?')[0])
                                             .Where(u => !Regex.IsMatch(u, @"(?i)\.(js|css|png|jpe?g|svg|ico|woff2?)$"))
                                             .Where(u => !u.Contains("search-recover"))
                                             .Distinct(StringComparer.OrdinalIgnoreCase)
                                             .Take(4))
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
            {
                // Trang thách thức của Cloudflare: retry chỉ tổ mất 3x thời gian, nói rõ để
                // người dùng biết nguồn này cần bypass (Lampac bật Playwright) chứ không phải bug.
                if (html.Length < 9000 && Regex.IsMatch(html, @"(?i)just a moment|cf-browser-verification|attention required|__cf_chl"))
                {
                    Console.WriteLine($"{Hub} {Cut(url)} bị Cloudflare chặn (js challenge) — bỏ qua, không retry");
                    return null;
                }

                return html;
            }

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

    /// <summary>
    /// Nhãn nút nguồn = text của anchor, VÀ nếu nhãn chưa có dung lượng thì mò thêm quanh anchor:
    /// hai site này để "720p" trong text còn "[1.9GB]" ở <span> ngay cạnh nút (yêu cầu "lấy luôn
    /// dung lượng ở phần tên link"). Chỉ quét xung quanh nên không thêm request nào.
    /// </summary>
    protected static string WidenLabel(string html, int anchorStart, string label)
    {
        if (string.IsNullOrWhiteSpace(html))
            return label ?? "";

        if (Regex.IsMatch(label ?? "", @"(?i)\d+(?:[.,]\d+)?\s*[KMGT]B"))
            return label;

        int from = Math.Max(0, anchorStart - 200);
        int to = Math.Min(html.Length, anchorStart + 600);

        var m = Regex.Match(Plain(html[from..to]), @"(?i)\d+(?:[.,]\d+)?\s*(?:KB|MB|GB|TB)");

        return m.Success ? (label + " " + m.Value).Trim() : label;
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

    // GetPage không nhận source, nên log của nó dùng prefix chung
    const string Hub = "hub";

    #region extractor HubCloud / GDFlix (học CSX: class HubCloud, class GDFlix trong Extractors.kt)
    //
    // Trang chia sẻ của hai engine này KHÔNG chứa link chơi được, CSX phải đi thêm một bước:
    //
    //   HubCloud:  GET share   -> <script> var url = '/xxx'          (vcloud: var url = atob(atob('..')))
    //              GET root+url-> div.card-header (tên file), i#size (dung lượng),
    //                             <h2><a class="btn" href="...">FSL Server</a></h2> = file thô chơi được
    //   GDFlix:    GET share   -> <li>Name : ...</li> <li>Size : ...</li>
    //                             <div class="text-center"><a href="...">FSL V2 / DIRECT DL / GD Index</a></div>
    //
    // Host của chúng đổi TLD liên tục nên CSX đọc urls.json để lấy mirror mới nhất. Em dùng cùng
    // nguồn nhưng CHỈ như fallback: json fail thì giữ host trong link, không để nó làm chết nguồn.
    //
    // Quy ước viết regex trong vùng này: KHÔNG dùng verbatim string chứa [""] (dễ lệch nháy), mà
    // dùng plain string + helper Block()/JsVar() để khỏi phải escape HTML attribute.
    const string CsxDomainsJson = "https://raw.githubusercontent.com/SaurabhKaperwan/Utils/refs/heads/main/urls.json";

    protected static bool IsHubHost(string url)
        => Regex.IsMatch(url ?? "", "(?i)hubcloud|hubcdn|hubdrive|vcloud");

    protected static bool IsGdHost(string url)
        => Regex.IsMatch(url ?? "", "(?i)gdflix|gdlink|go2link");

    /// <summary>Nội dung bên trong phần tử mở đầu có class/id chứa fragment (hỗ trợ mọi loại nháy
    /// vì chỉ so substring trên thẻ mở). Trả "" nếu không thấy.</summary>
    protected static string Block(string html, string tag, string fragment = null, int take = 1)
    {
        var parts = new List<string>();

        if (string.IsNullOrWhiteSpace(html))
            return "";

        foreach (Match open in Regex.Matches(html, "(?is)<" + tag + @"\b[^>]*>"))
        {
            if (fragment != null && !open.Value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                continue;

            int close = html.IndexOf("</" + tag, open.Index + open.Length, StringComparison.OrdinalIgnoreCase);

            parts.Add(close < 0 ? html[(open.Index + open.Length)..] : html[(open.Index + open.Length)..close]);

            if (parts.Count >= take)
                break;
        }

        return parts.Count == 0 ? "" : parts[0];
    }

    /// <summary>Mọi anchor trong block, kèm nhãn đã bỏ tag; loc theo fragment ở thẻ mở nếu có.</summary>
    protected static List<(string Href, string Text)> Links(string block, string fragment = null)
        => [.. Regex.Matches(block ?? "", AnchorPattern)
                 .Where(m => fragment == null || m.Value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                 .Select(m => (Href: Unescape(HrefValue(m)), Text: WidenLabel(block, m.Index, Plain(m.Groups["t"].Value))))
                 .Where(x => !string.IsNullOrWhiteSpace(x.Href))];

    /// <summary>
    /// Giá trị của  var NAME = 'chuỗi'  trong JS, tính cả dạng atob() / atob(atob()) mà vcloud dùng.
    /// </summary>
    protected static string JsVar(string html, string name, out int atobCount)
    {
        atobCount = 0;

        var m = Regex.Match(html ?? "", "(?is)\\bvar\\s+" + name + "\\s*=\\s*((?:atob\\s*\\(\\s*)*['\"]([^'\"]*)['\"])");

        if (!m.Success)
            return null;

        atobCount = Regex.Matches(m.Groups[1].Value, "atob").Count;

        return m.Groups[2].Value;
    }

    /// <summary>JS atob(): bù padding nếu thiếu rồi base64-decode; null nếu không phải base64.</summary>
    static string Atob(string b64)
    {
        if (string.IsNullOrWhiteSpace(b64))
            return null;

        b64 = b64.Trim();

        int pad = b64.Length % 4;

        if (pad == 2)
            b64 += "==";
        else if (pad == 3)
            b64 += "=";
        else if (pad != 0)
            return null;

        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return null; }
    }

    static string Root(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out Uri u) ? u.Scheme + "://" + u.Host : url;

    static string Plain(string html)
        => Regex.Replace(Regex.Replace(html ?? "", "<[^>]+>", " "), @"\s+", " ").Trim();

    /// <summary>mirror mới nhất theo key ("hubcloud"/"vcloud"/"gdflix"); null nếu không lấy được json.</summary>
    protected async Task<string> LatestRoot(string key)
    {
        Dictionary<string, string> map = null;

        try
        {
            if (hybridCache.ContainsKey("movieshub_domains", out Dictionary<string, string> cached, out _))
            {
                map = cached;
            }
            else
            {
                string json = await Http.Get(CsxDomainsJson, timeoutSeconds: 10);

                if (ParseJson(json) is JObject o)
                {
                    map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var p in o.Properties())
                        map[p.Name] = p.Value.ToString();

                    hybridCache.Set("movieshub_domains", map, TimeSpan.FromMinutes(180), true);
                }
                else
                {
                    Console.WriteLine($"{Hub} urls.json không parse được (len={json?.Length ?? 0}) - dùng host trong link");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"{Hub} urls.json fail ({e.GetType().Name}) - dùng host trong link");
        }

        if (map != null && map.TryGetValue(key, out string val) && Uri.TryCreate(val, UriKind.Absolute, out Uri u))
            return u.Scheme + "://" + u.Host;

        return null;
    }

    /// <summary>Đổi host của link sang mirror mới nhất; giữ nguyên nếu bên đó cũng không đọc được.</summary>
    protected async Task<(string Url, string Html)> WithLatestMirror(string source, string url, string html, string key)
    {
        string root = Root(url);
        string latest = await LatestRoot(key);

        if (latest == null || latest == root)
            return (url, html);

        string rotated = url.Replace(root, latest);
        string page = await GetPage(rotated);

        if (string.IsNullOrWhiteSpace(page))
        {
            Console.WriteLine($"{Log(source)} mirror {Cut(latest)} cũng không đọc được - giữ {Cut(root)}");
            return (url, html);
        }

        Console.WriteLine($"{Log(source)} đổi host {Cut(root)} -> {Cut(latest)}");
        return (rotated, page);
    }

    /// <summary>pixeldrain: CSX nối root + /api/file/&lt;id&gt;?download — link đó chơi được luôn.</summary>
    protected List<HubStream> Pixel(string shareLink, string pxl, string hint, string referer)
    {
        string link = string.IsNullOrWhiteSpace(pxl) ? shareLink : pxl;

        if (!link.Contains("download", StringComparison.OrdinalIgnoreCase))
            link = Root(link) + "/api/file/" + link.Split('?')[0].TrimEnd('/').Split('/').Last() + "?download";

        return [new HubStream(link, QualityLabel(hint + " [Pixeldrain]", link), StreamHeaders(referer))];
    }

    /// <summary>HubCloud/VCloud: 2 trang — trang chia sẻ (var url) rồi trang download (nút h2 a.btn).</summary>
    protected async Task<List<HubStream>> HubExtract(string source, string url, string html, string label)
    {
        var list = new List<HubStream>();

        (url, html) = await WithLatestMirror(source, url, html, url.Contains("vcloud", StringComparison.OrdinalIgnoreCase) ? "vcloud" : "hubcloud");

        if (string.IsNullOrWhiteSpace(html))
            return list;

        string root = Root(url);
        string step = null;

        if (url.Contains("/video/"))
        {
            step = Links(Block(html, "center")).Select(x => x.Href).FirstOrDefault();
        }
        else
        {
            // CSX: selectFirst("script:containsData(url)") — đúng <script> gán biến url
            string script = Block(html, "script");

            if (string.IsNullOrEmpty(script) || !script.Contains("var"))
                script = Regex.Match(html, "(?is)<script[^>]*>[\\s\\S]*?var\\s+url[\\s\\S]*?</script>").Groups[0].Value;

            step = JsVar(script, "url", out int atobCount);

            for (int i = 0; i < atobCount && step != null; i++)
                step = Atob(step);

            if (string.IsNullOrWhiteSpace(step))
            {
                Console.WriteLine($"{Log(source)} HubCloud: trang chia sẻ không có biến url | len={html.Length} script={script.Length} head={Preview(html)}");
                return list;
            }
        }

        string page = Absolute(Unescape(step), root + "/");

        if (!IsPlayable(page))
        {
            Console.WriteLine($"{Log(source)} HubCloud: biến url không ra url (step={Cut(step ?? "null")})");
            return list;
        }

        if (IsMediaPath(page))
        {
            Console.WriteLine($"{Log(source)} HubCloud: biến url đã là file - {Cut(page)}");
            return [new HubStream(page, QualityLabel(label, page), StreamHeaders(page))];
        }

        string inner = await GetPage(page, StreamHeaders(url));

        if (string.IsNullOrWhiteSpace(inner))
        {
            Console.WriteLine($"{Log(source)} HubCloud: trang download rỗng {Cut(page)}");
            return list;
        }

        string name = Plain(Block(inner, "div", "card-header"));
        string size = Plain(Block(inner, "i", "size"));
        string hint = label + " " + name + " " + size;

        var buttons = Links(Regex.Match(inner, @"(?is)<h2[^>]*>[\s\S]*?</h2>").Value, "btn");

        if (buttons.Count == 0)
            buttons = Links(inner, "btn");

        Console.WriteLine($"{Log(source)} HubCloud: {buttons.Count} nút server, file={Cut(name)} {size}");

        foreach ((string href, string text) in buttons)
        {
            string link = Absolute(href, page);

            if (Regex.IsMatch(text, "(?i)FSL|Download File|Mega Server|Direct"))
            {
                list.Add(new HubStream(link, QualityLabel(hint, link), StreamHeaders(page)));
                continue;
            }

            if (link.Contains("pixeldra", StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(Pixel(link, JsVar(inner, "pxl", out _), hint, page));
                continue;
            }

            if (Regex.IsMatch(text, "(?i)10Gbps"))
            {
                string loc = Absolute(await Http.GetLocation(link, referer: page, headers: StreamHeaders(page)), link);

                if (loc != null && loc.Contains("link="))
                    loc = loc.Substring(loc.IndexOf("link=", StringComparison.Ordinal) + 5);

                if (IsPlayable(loc))
                    list.Add(new HubStream(loc, QualityLabel(hint + " [Download]", loc), StreamHeaders(loc)));

                continue;
            }

            if (Regex.IsMatch(text, "(?i)Buzz"))
            {
                string buzz = await GetPage(link, StreamHeaders(page));
                string dl = Links(Block(buzz, "a", "download-btn")).Select(x => x.Href).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(dl) && IsPlayable(Absolute(dl, link)))
                    list.Add(new HubStream(Absolute(dl, link), QualityLabel(hint + " [Buzz]", dl), StreamHeaders(link)));

                continue;
            }
        }

        if (list.Count == 0)
        {
            // site đổi nhãn nút -> quét url file trần, thà còn link hơn là 0
                foreach (Match m in Regex.Matches(inner, @"https?://[^""'\s<>]+?/[^""'\s<>/?]*\.(?:mkv|mp4|m4v|mov|avi)(?:\?[^""'\s<>]*)?", RegexOptions.IgnoreCase))
                list.Add(new HubStream(Unescape(m.Value), QualityLabel(hint, m.Value), StreamHeaders(page)));
        }

        if (list.Count == 0)
            Console.WriteLine($"{Log(source)} HubCloud: {buttons.Count} nút mà không cái nào ra file | head={Preview(inner)}");

        return list;
    }

    /// <summary>GDFlix: một trang, các nút nằm trong div.text-center (CSX class GDFlix).</summary>
    protected async Task<List<HubStream>> GdExtract(string source, string url, string html, string label)
    {
        var list = new List<HubStream>();

        (url, html) = await WithLatestMirror(source, url, html, "gdflix");

        if (string.IsNullOrWhiteSpace(html))
        {
            Console.WriteLine($"{Log(source)} GDFlix: {Cut(url)} không đọc được (Cloudflare challenge - cần bật Playwright) - bỏ qua");
            return list;
        }

        string name = Plain(Regex.Match(html, "(?is)<li[^>]*>[^<]*Name\\s*[:.]\\s*([^<]*)").Groups[1].Value);
        string size = Plain(Regex.Match(html, "(?is)<li[^>]*>[^<]*Size\\s*[:.]\\s*([^<]*)").Groups[1].Value);
        string hint = label + " " + name + " " + size;

        var buttons = Links(Regex.Match(html, @"(?is)<div[^>]*text-center[^>]*>[\s\S]*?</div>").Value);

        if (buttons.Count == 0)
            buttons = Links(html);

        Console.WriteLine($"{Log(source)} GDFlix: {buttons.Count} nút server, file={Cut(name)} {size}");

        foreach ((string href, string text) in buttons)
        {
            string link = Absolute(href, url);

            if (Regex.IsMatch(text, "(?i)FSL|DIRECT DL|DIRECT SERVER|CLOUD DOWNLOAD"))
            {
                list.Add(new HubStream(link, QualityLabel(hint, link), StreamHeaders(url)));
                continue;
            }

            if (Regex.IsMatch(text, "(?i)GD Index"))
            {
                foreach (int type in new[] { 1, 2 })
                {
                    string ask = link.Contains('?') ? link + "&type=" + type : link + "?type=" + type;
                    string idx = await GetPage(ask, StreamHeaders(url));

                    foreach ((string cf, string _) in Links(idx, "btn-success"))
                        if (IsPlayable(Absolute(cf, link)))
                            list.Add(new HubStream(Absolute(cf, link), QualityLabel(hint + $" [CF{type}]", cf), StreamHeaders(url)));
                }

                continue;
            }

            if (Regex.IsMatch(text, "(?i)FAST CLOUD"))
            {
                string card = await GetPage(link, StreamHeaders(url));
                string dl = Links(Block(card, "div", "card-body")).Select(x => x.Href).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(dl) && IsPlayable(Absolute(dl, link)))
                    list.Add(new HubStream(Absolute(dl, link), QualityLabel(hint + " [Cloud]", dl), StreamHeaders(url)));

                continue;
            }

            if (link.Contains("pixeldra", StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(Pixel(link, null, hint, url));
                continue;
            }

            if (Regex.IsMatch(text, "(?i)Instant DL"))
            {
                string loc = await Http.GetLocation(link, referer: url, headers: StreamHeaders(url));

                if (!string.IsNullOrWhiteSpace(loc) && loc.Contains("url="))
                    loc = loc.Substring(loc.IndexOf("url=", StringComparison.Ordinal) + 4);

                if (IsPlayable(loc))
                    list.Add(new HubStream(loc, QualityLabel(hint + " [Instant]", loc), StreamHeaders(url)));

                continue;
            }
        }

        if (list.Count == 0)
                foreach (Match m in Regex.Matches(html, @"https?://[^""'\s<>]+?/[^""'\s<>/?]*\.(?:mkv|mp4|m4v|mov|avi)(?:\?[^""'\s<>]*)?", RegexOptions.IgnoreCase))
                list.Add(new HubStream(Unescape(m.Value), QualityLabel(hint, m.Value), StreamHeaders(url)));

        if (list.Count == 0)
            Console.WriteLine($"{Log(source)} GDFlix: {buttons.Count} nút mà không cái nào ra file | head={Preview(html)}");

        return list;
    }
    #endregion

    #endregion

    #region DOM nghiệp dư (không phụ thuộc HtmlAgilityPack)
    // Hai site này (và HubCloud) trộn nháy đơn/nháy kép trong HTML. Regex cứng href="..." từng
    // làm module thấy `a=154` mà vẫn 0 link ở MoviesDrive -> mọi pattern ở đây phải chấp nhận
    // href="x" | href='x' | href=x. .NET cho phép trùng tên nhóm (?<u>) giữa các nhánh.
    // d = nháy kép, s = nháy đơn, n = không nháy. Không dùng trùng tên nhóm để khỏi phụ thuộc
    // đặc tính .NET — đọc bằng HrefValue(m).
    protected const string AnchorPattern =
        @"(?is)<a[^>]+href\s*=\s*(?:""(?<d>[^""]*)""|'(?<s>[^']*)'|(?<n>[^\s""'>]+))[^>]*>(?<t>.*?)</a>";

    protected const string HrefPattern =
        @"(?is)href\s*=\s*(?:""(?<d>[^""]*)""|'(?<s>[^']*)'|(?<n>[^\s""'>]+))";

    /// <summary>Giá trị href của Match do AnchorPattern/HrefPattern sinh ra (bất kể loại nháy).</summary>
    protected static string HrefValue(Match m)
    {
        Group g = m.Groups["d"];

        if (g.Success)
            return g.Value;

        g = m.Groups["s"];

        if (g.Success)
            return g.Value;

        return m.Groups["n"].Value;
    }

    // {0} = class fragment (đã Regex.Escape)
    protected const string DivOpenPattern =
        @"(?is)<div[^>]*class\s*=\s*(?:""[^""]*{0}[^""]*""|'[^']*{0}[^']*'|[^\s""'>]*{0}[^\s""'>]*)[^>]*>";
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

        foreach (Match m in Regex.Matches(html, AnchorPattern))
        {
            string url = Absolute(Unescape(HrefValue(m)), baseUrl);

            string label = Regex.Replace(m.Groups["t"].Value, @"<[^>]+>", " ");
            label = Regex.Replace(label, @"\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(label))
                label = NearestHeadingBefore(html, m.Index);

            // onlyFileHost=false: dùng cho các bước ĐIỀU HƯỚNG (trang pack của mùa, trang
            // trung gian của Movies4U) — những link này không phải file-host mà là trang nội.
            if (onlyFileHost && !LooksLikeFileHost(url) && !LooksLikeQualityLink(url, label, baseUrl))
                continue;

            // link trơ trọi về homepage (https://moviesdrives.mov) từng bị nhận là link .mov
            if (onlyFileHost && Uri.TryCreate(url, UriKind.Absolute, out Uri bare) && bare.AbsolutePath.Trim('/').Length == 0)
                continue;

            label = WidenLabel(html, m.Index, label);

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

        foreach (Match open in Regex.Matches(html, string.Format(DivOpenPattern, Regex.Escape(classFragment))))
        {
            int start = open.Index + open.Length;

            // </div> ĐẦU TIÊN sau khi mở khối không phải </div> của khối: WP bọc div lồng nhau, cắt
            // sớm làm mất các nút phía sau và mất luôn heading — một trong các nghi phạm khiến
            // Movies4U chỉ ra bản nhỏ, mất nhóm 4K/20GB. DivEnd đếm độ sâu.
            int end = DivEnd(html, start);

            if (end < 0)
                end = Math.Min(html.Length, start + 6000);

            string inner = html[start..end];

            var links = Regex.Matches(inner, AnchorPattern)
                             .Select(m => (Label: WidenLabel(inner, m.Index, Plain(m.Groups["t"].Value)), Url: HrefValue(m)))
                             .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                             .ToList();

            blocks.Add((open.Index, NearestHeadingBefore(html, open.Index), links));

            if (blocks.Count >= max)
                break;
        }

        return blocks;
    }

    /// <summary>Vị trí &lt;/div&gt; đóng khối mở tại <paramref name="start"/>, có đếm div lồng nhau.
    /// Trả -1 khi phải cắt cứng (html hở / quá dài).</summary>
    static int DivEnd(string html, int start, int hardLimit = 250_000)
    {
        int depth = 1;
        int stop = Math.Min(html.Length, start + hardLimit);

        foreach (Match m in Regex.Matches(html[start..stop], @"(?is)<div\b[^>]*>|</div\s*>"))
        {
            if (m.Value.StartsWith("</", StringComparison.OrdinalIgnoreCase))
            {
                if (--depth == 0)
                    return start + m.Index;
            }
            else
                depth++;
        }

        return -1;
    }

    /// <summary>Thống kê class của div/anchor nào trông như "khối nút tải". Log "0 link" mà không
    /// có dòng này thì không biết site đổi selector sang gì.</summary>
    protected static string ClassHistogram(string html, int top = 8)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "(html rỗng)";

        string[] names = [.. Regex.Matches(html, @"(?is)class\s*=\s*(?:""[^""]*""|'[^']*')")
                               .Select(m => Regex.Replace(m.Value, @"^class\s*=\s*[""']|[""']$", "").Trim())
                               .Where(v => v.Length > 0 && Regex.IsMatch(v, @"(?i)download|links|btn|file|qual"))];

        string hist = string.Join(" ", names.GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                                           .OrderByDescending(g => g.Count())
                                           .Take(top)
                                           .Select(g => $"{g.Key}:{g.Count()}"));

        return string.IsNullOrWhiteSpace(hist) ? "(không class nào gợi ý nút tải)" : hist;
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
    /// <summary>
    /// Đếm host của mọi anchor, in ra khi bộ lọc không bắt được link nào. Không có dòng này thì
    /// "0 link" không nói được gì; có nó thì biết ngay site đổi chỗ hay link nằm ở host lạ.
    /// </summary>
    protected static string HostHistogram(string html, string baseUrl, int top = 6)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "(html rỗng)";

        return string.Join(" ", Regex.Matches(html, @"(?is)<a[^>]+href\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'>]+)")
                                    .Select(m =>
                                    {
                                        string raw = HrefValue(Regex.Match(m.Value, HrefPattern));
                                        string u = Absolute(Unescape(raw), baseUrl);
                                        return Uri.TryCreate(u, UriKind.Absolute, out Uri x) ? x.Host : "(relative)";
                                    })
                                    .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
                                    .OrderByDescending(g => g.Count())
                                    .Take(top)
                                    .Select(g => $"{g.Key}:{g.Count()}"));
    }

    /// <summary>
    /// Nhãn kiểu "480p [540MB]" / "1080p HEVC 5.4GB" + host khác host bài viết = nhiều khả năng là
    /// một nút nguồn thật, kể cả khi host chưa có trong danh sách. Thêm cửa này để một lần sai
    /// danh sách file-host không làm nguồn trả 0 link (chính là case moviesdrive: a=154 / 0 link).
    /// </summary>
    protected static bool LooksLikeQualityLink(string url, string label, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url))
            return false;

        if (!Regex.IsMatch(label, @"(?i)\d{3,4}p|\b4k\b|\d+(?:[.,]\d+)?\s?(?:gb|mb)"))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri u) || !Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri p))
            return false;

        return !u.Host.Equals(p.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Đường dẫn trông như file video (không có query) — thứ mà player chơi thẳng được.</summary>
    protected static bool IsMediaPath(string url)
        => !string.IsNullOrWhiteSpace(url) && Regex.IsMatch(url.Split('?')[0], @"(?i)\.(mkv|mp4|m4v|mov|avi|ts|webm)$");

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
