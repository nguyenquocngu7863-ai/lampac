using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.Templates;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MoviesHub;

/// <summary>
/// XdMovies (`top.xdmovies.wtf`) — nguồn mở 2026-09-01 thay UhdMovies (lý do đóng: notes/UHD-MOVIES.md 11).
///
/// Hai điều làm nên file này, đọc `notes/XDMOVIES.md` trước khi sửa:
///  * BÀI viết cực sạch: meta TMDB + `### Download Links:`, mỗi chất lượng MỘT link
///    `https://link.xdmovies.wtf/download/<token>` và TÊN FILE (x mediainfo) nằm ngay trên link.
///    Số cuối slug = TMDB id (bài The Whisper Man `-860508`, Reacher `-108978`) => khớp id là ăn,
///    khỏi cần đoán tên.
///  * LINK thì ngược lại: `/download/<token>` đá sang `latestnewsonline.sbs/r/<code>` = trang
///    "Get Your Link": ĐẾM NGƯỢC 6s bằng JS + Cloudflare TURNSTILE (sitekey 0x4AAAAAACwMJhFoINTv6AGb),
///    rồi 3 nút Generate Link -> Step 2 -> Go to Link. Sau gate mới là server (fls ưu tiên, pixel backup,
///    và theo anh: chính ở bước này nó mới nhả link HubCloud).
///
/// Vì vậy: HttpClient hết cửa, chỉ `rch` (trình duyệt thật của client Lampa) đi qua được. `rch.Headers`
/// (`Shared/Services/HTTP/RchClient.cs:230`) điều hướng TRONG client và trả `(headers, currentUrl, body)`
/// sau khi JS chạy, client phải >= 484. Ta còn gửi kèm `data` = đoạn JS tự bấm 3 nút — `data` là
/// tham số mà Ebalovo/Porntrex/VideoDB chưa từng dùng, nên vòng 1 vừa chạy vừa log để biết client của
/// anh có chịu thi hành script không.
///
/// Sau gate: link HubCloud/GDFlix thì ném thẳng vào `ResolveHub` của HubController (đã có, máy đã xác
/// minh nhờ Movies4U) — em không viết lại extractor HubCloud. Không có `rch` thì module KHÔNG đoán bừa:
/// nó log một câu rõ ràng và trả lỗi, vì trả link sai (trang đếm ngược) là player đứng nhìn.
/// </summary>
public class XdmoviesController : HubController
{
    const string Source = "xdmovies";

    public XdmoviesController() : base(ModInit.xd)      // config section riêng: "XdMovies"
    {
    }

    static string Tag => "xdmovies:";

    /// <summary>Referer là CHI TIẾT SỐNG SÓC: gamerxyt.com (mặt nạ cùng kho) chỉ hiện link phim khi
    /// request mang `Referer: <link hubcloud>`; với top.xdmovies.wtf thì em lấy gốc site làm referer
    /// cho an toàn (bài đọc 1/9 không cần referer, nhưng họ đã bắt đầu đổi cách phục vụ).</summary>
    List<HeadersModel> SiteHeaders()
        => HeadersModel.Init(
            ("User-Agent", Http.UserAgent),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
            ("Referer", init.host.TrimEnd('/') + "/"));

    // ----------------------------------------------------------------------------------- routes

    [HttpGet, Staticache(manually: true)]
    [Route("lite/xdmovies")]
    public Task<ActionResult> Index(bool checksearch, long id, long tmdb_id, string imdb_id, string title, string original_title, byte serial, short s = -1, bool rjson = false)
        => CollectionCore(Source, checksearch, id, tmdb_id, imdb_id, title, original_title, serial, s, rjson);

    [HttpGet, Staticache(manually: true)]
    [Route("lite/xdmovies/video")]
    [Route("lite/xdmovies/file.mkv")]
    [Route("lite/xdmovies/file.mp4")]
    public async Task<ActionResult> Video(string src, string label, short s = -1, short e = -1, bool play = true)
    {
        StatiCacheDisabled = true;
        SetHeadersNoCache();

        if (await IsRequestBlocked(rch: false, rch_check: !play))
        {
            Console.WriteLine($"{Tag} blocked (enable={init.enable}, rip={init.rip})");
            return badInitMsg ?? OnError("disable", gbcache: false, statusCode: 403);
        }

        string token = Dec(src);

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine($"{Tag} src thiếu/hỏng trong query video");
            return OnError("stream", 502);
        }

        label = string.IsNullOrWhiteSpace(Dec(label)) ? "stream" : Dec(label);
        List<HubStream> found;

        try
        {
            found = await Resolve(token, label);
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
            Console.WriteLine($"{Tag} 0 link chơi được từ {Cut(token)}");
            return OnError("stream", 502);
        }

        Console.WriteLine($"{Tag} play {Cut(first.Url)} [{(init.streamproxy ? "proxy" : "direct")}] ({(s > 0 ? $"S{s}E{e} · " : "")}{label}) qua '{first.Label}' build={Build}");

        if (play)
            return RedirectToPlay(init.streamproxy ? HostStreamProxy(first.Url, headers: first.Headers, force_streamproxy: true) : first.Url);

        return ContentTo(VideoTpl.ToJson(
            "play",
            accsArgs($"{host}/lite/{init.plugin.ToLowerAndTrim()}/{RouteFor(first.Label, first.Url)}?src={Enc(Clean(token))}&label={Enc(label)}&play=true"),
            label,
            quality: first.Label,
            vast: init.vast,
            httpContext: HttpContext));
    }

    // ------------------------------------------------------------------------------- collection

    protected override async Task<List<HubEntry>> Collect(string source, string imdbId, long tmdbId, short season)
    {
        string site = init.host.TrimEnd('/');
        bool tv = season != 0;
        var headers = SiteHeaders();

        var meta = await TmdbMeta(tmdbId, tv);
        List<string> queries = [];

        foreach (string name in new[] { meta.originalTitle, meta.title })
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string key = name.Trim();

            if (!queries.Any(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)))
                queries.Add(tv ? $"{key} season {System.Math.Max((int)season, 1)}" : $"{key} {meta.year}");
        }

        if (queries.Count == 0)
        {
            Console.WriteLine($"{Tag} TMDB không trả title để tìm (tmdb={tmdbId})");
            return null;
        }

        string html = null;
        string postUrl = null;

        // Tìm bài: ?s= rồi /search/. Chưa đọc trang search của site này nên điều kiện nhận bài là
        // thứ chắc nhất em có: slug của họ CHỐT bằng TMDB id (`...-860508`).
        foreach (string query in queries.Take(2))
        {
            foreach (string form in new[] { "?s={0}", "/search/{0}" })
            {
                string page = await GetPage($"{site}/{string.Format(form, Uri.EscapeDataString(query))}", headers);

                if (string.IsNullOrWhiteSpace(page))
                {
                    Console.WriteLine($"{Tag} trang tìm rỗng (dạng {form}) q='{query}'");
                    continue;
                }

                List<(string Url, string Label)> hits = [.. Anchors(page, site + "/", 40, onlyFileHost: false)
                                            .Where(a => a.Url.Contains("/movies/", StringComparison.OrdinalIgnoreCase) || a.Url.Contains("/series/", StringComparison.OrdinalIgnoreCase))
                                            .Select(a => (Url: a.Url, Label: Plain(a.Label)))
                                            .Where(x => !string.IsNullOrWhiteSpace(x.Label))
                                            .DistinctBy(x => x.Url)];

                // Ưu tiên bài có id khớp TMDB; không có thì lấy bài nào cùng loại (movies/series).
                List<(string Url, string Label)> picked = [.. hits.Where(x => IdOf(x.Url) == tmdbId)];

                if (picked.Count == 0)
                    picked = [.. hits.Where(x => x.Url.Contains(tv ? "/series/" : "/movies/", StringComparison.OrdinalIgnoreCase))];

                Console.WriteLine($"{Tag} tìm q='{query}' dạng {form}: a={Regex.Matches(page, "(?i)<a[^>]+href=").Count} bai={hits.Count} khop_id={picked.Count}");

                if (picked.Count == 0)
                    continue;

                foreach (var cand in picked.Take(3))
                {
                    string post = await GetPage(cand.Url, headers);

                    if (string.IsNullOrWhiteSpace(post) || !post.Contains("/download/"))
                    {
                        Console.WriteLine($"{Tag} bài {Cut(cand.Url)} không có /download/ (len={post?.Length ?? 0})");
                        continue;
                    }

                    postUrl = cand.Url;
                    html = post;
                    break;
                }

                if (html != null)
                    break;
            }

            if (html != null)
                break;
        }

        if (html == null)
        {
            Console.WriteLine($"{Tag} không lấy được bài nào (tmdb={tmdbId}, site={site})");
            return null;
        }

        // ---------------- parsing: MỘT LƯỢT cho cả bài, không cửa sổ ký tự (bài học uhd vòng 30:
        // href ~700 ký tự nên window 1500 chỉ tóm được 2/8 nút trên một dòng).
        // Mỗi khối <p|li|div|h_> -> text (tên file / nhãn nhóm) + các anchor của nó.
        List<(string Text, List<(string Href, string Text)> Links)> blocks = Blocks(html);
        List<(string Film, string Group, string Label, string Url, short Season, short Ep)> rel = [];

        foreach (var b in blocks)
        {
            if (b.Links.Count == 0)
                continue;

            string text = b.Text;

            foreach (var a in b.Links)
            {
                string url = Clean(Absolute(a.Href, postUrl));

                if (!url.Contains("/download/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (JunkLink(url, a.Text))
                    continue;

                // Tên file có thể nằm ở text của khối (kèm các file khác) -> lấy dòng tên file gần nhất.
                string file = FileNameNear(text, a.Text);

                if (IsPack(file))
                {
                    Console.WriteLine($"{Tag} bỏ pack/zip: {Cut(file)}");
                    continue;
                }

                short se = 0, ep = 0;
                ParseEpisode(file, ref se, ref ep);

                rel.Add((FilmOf(file, text), ShortGroup(a.Text), QualityLabel(file, url), url, se, ep));
            }
        }

        if (rel.Count == 0)
        {
            Console.WriteLine($"{Tag} bài {Cut(postUrl)} có {blocks.Count} khối nhưng 0 link /download/ | hosts={HostHistogram(html, site)}");
            return null;
        }

        var entries = new List<HubEntry>();

        foreach (var x in rel)
            entries.Add(new HubEntry(x.Label, x.Url, x.Season, x.Ep, string.IsNullOrWhiteSpace(x.Film) ? "XdMovies" : x.Film));

        Console.WriteLine($"{Tag} bài {Cut(postUrl)}: {entries.Count} nút từ {blocks.Count} khối (tmdb={tmdbId})");

        return entries;
    }

    // ---------------------------------------------------------------------------------- gate

    /// <summary>token/link /download/&lt;tok&gt; -> 0..n link chơi được. Bấm gate bằng rch, rồi đưa
    /// HubCloud/GDFlix cho ResolveHub của HubController.</summary>
    async Task<List<HubStream>> Resolve(string token, string label)
    {
        var found = new List<HubStream>();

        if (!token.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            token = $"{init.host.TrimEnd('/')}/download/{token}";

        var info = rch?.InfoConnected();

        // "Đo" trước khi đoán: dòng này trả lời dứt điểm rhub có bật và client là ai, đủ tuổi chưa
        // (Headers() đòi apkVersion >= 484 — RchClient.cs:235). Không có client thì mọi script
        // mình gửi đều vô nghĩa, và log phải nói rõ như vậy thay vì để anh đoán.
        Console.WriteLine($"{Tag} rhub enable={rch?.enable} client={(info == null ? "KHONG CO (chưa có app Lampa nào đăng ký /nws)" : $"apk={info.apkVersion} type={info.rchtype} player={info.player}")}");

        if (rch?.enable != true)
        {
            Console.WriteLine($"{Tag} KHÔNG QUA GATE ĐƯỢC: trang link cần JS (đếm ngược 6s + Turnstile) mà rhub đang TẮT (rch.enable=false) — bật \"rhub\": true + rch_access trong init.conf, và client Lampa phải >= 484");
            return found;
        }

        // JS mồi: chờ nút bấm được thì bấm lần lượt, cuối cùng trả URL thật. Viết phòng thủ: bấm gì
        // có sẵn, không giả định id/class; mỗi bước đợi tối đa ~7s.
        string script = "async function q(t){for(let i=0;i<70;i++){var e=[...document.querySelectorAll('a,button,input[type=submit]')].find(x=>t.test((x.innerText||x.value||'')+' '+(x.href||'')));if(e){if(e.href)e.click();else e.click();await new Promise(r=>setTimeout(r,700));return e.href||location.href}await new Promise(r=>setTimeout(r,200))}return null}"
                      + "var u=await q(/generate/i);u=await q(/continue|step\\s*2/i)||u;var g=await q(/go\\s*to\\s*link|download/i);"
                      + "return JSON.stringify({u1:u,u2:g,href:[...document.querySelectorAll('a')].map(a=>a.href).filter(h=>/hubcloud|gdflix|pixeldrain|pixel|fls|filelions|drive|\\.(mkv|mp4)($|\\?)/i.test(h)).slice(0,25),cur:location.href})";

        var res = await rch.Headers(token, script, SiteHeaders());

        Console.WriteLine($"{Tag} rch len={res.body?.Length ?? 0} cur={Cut(res.currentUrl ?? "")}");

        if (res.body == null && res.currentUrl == null)
        {
            Console.WriteLine($"{Tag} rch trả rỗng — client có thể không chạy `data` (script). Đây là dữ kiện để vòng 2: đổi sang rch.Get/Eval hoặc bắt anh bấm tay.");
            return found;
        }

        List<string> urls = [];

        if (!string.IsNullOrWhiteSpace(res.currentUrl))
            urls.Add(res.currentUrl);

        string body = res.body ?? "";

        foreach (Match m in Regex.Matches(body, @"https?://[^\s""'<>\\]+", RegexOptions.IgnoreCase))
            urls.Add(m.Value.Replace("\\/", "/"));

        List<string> ranked = [.. urls.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Clean(x)).Distinct()
                                      .OrderByDescending(x => Rank(x))];

        Console.WriteLine($"{Tag} gate mở ra {ranked.Count} url | top={string.Join(" ", ranked.Take(4).Select(Cut))}");

        foreach (string url in ranked.Take(6))
        {
            if (url.Contains("xdmovies.wtf/download", StringComparison.OrdinalIgnoreCase) || url.Contains("/r/", StringComparison.OrdinalIgnoreCase) && url.Contains("latestnews", StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsHubHost(url) || IsGdHost(url) || IsMediaPath(url))
            {
                var got = await ResolveHub(url, label, Source);

                if (got.Count > 0)
                {
                    found.AddRange(got);
                    Console.WriteLine($"{Tag} qua {Cut(url)} -> {got.Count} link ({string.Join(", ", got.Select(x => x.Label).Take(3))})");
                    break;
                }

                // pixel (pixeldrain) không phải hubcloud -> đi qua Pixel() của HubController:
                // /api/file/<id>?download là link thẳng, có Range nên tua được.
                if (url.Contains("pixeldrain", StringComparison.OrdinalIgnoreCase))
                {
                    found.AddRange(Pixel(url, null, label, token));
                    Console.WriteLine($"{Tag} pixel bắt được: {Cut(url)}");
                    break;
                }

                if (IsMediaPath(url))
                {
                    found.Add(new HubStream(url, QualityLabel(label, url), null));
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>Hạng theo độ "thơm": fls (filelions) trước, pixel/pixeldrain sau, rồi HubCloud/GDFlix.</summary>
    static int Rank(string url)
    {
        string u = url.ToLowerInvariant();

        if (u.Contains("filelions") || u.Contains("fls."))
            return 5;

        if (u.Contains("pixeldrain") || u.Contains("pixel"))
            return 4;

        if (u.Contains("hubcloud") || u.Contains("vcloud") || u.Contains("gdflix"))
            return 3;

        if (u.Contains(".mkv") || u.Contains(".mp4"))
            return 2;

        return 1;
    }

    // ------------------------------------------------------------------------------- parsing aids

    /// <summary>Một lượt quét toàn bài: mỗi khối <p|li|div|h1..h6> cho (text, links). Không dùng cửa sổ
    /// ký tự — xem notes/UHD-MOVIES.md 10.</summary>
    static List<(string Text, List<(string Href, string Text)> Links)> Blocks(string html)
    {
        var list = new List<(string, List<(string, string)>)>();

        foreach (Match m in Regex.Matches(html, @"(?is)<(?<tag>p|li|div|h[1-6])\b[^>]*>(?<body>.*?)</\k<tag>>"))
        {
            string inner = m.Groups["body"].Value;
            var links = new List<(string, string)>();

            foreach (Match a in Regex.Matches(inner, @"(?is)<a\b[^>]*href\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>(?<t>.*?)</a>"))
                links.Add((a.Groups[1].Success ? a.Groups[1].Value : (a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value), Plain(a.Groups["t"].Value)));

            if (links.Count > 0)
                list.Add((Plain(Regex.Replace(inner, @"(?is)<a\b.*?</a>", " ")), links));
        }

        return list;
    }

    /// <summary>Nhóm: "Netflix Versions(7)" v.v.; rỗng thì để HubController tự gom.</summary>
    static string ShortGroup(string text) => Cut(Regex.Replace(text ?? "", @"\s*\(\d+\)\s*$", "").Trim());

    /// <summary>Rác trên trang post: menu, social, login — không bao giờ là link xem (copy từ uhd,
    /// file đó đã rút khỏi tree nên không dùng chung được).</summary>
    static bool JunkLink(string url, string text)
        => Regex.IsMatch(text ?? "", @"(?i)report|dmca|copyright|contact|privacy|polic|terms|\bhome\b|about|follow|channel|upload file|search|login|log in|sign in|register|premium|buy ")
        || Regex.IsMatch(url ?? "", @"(?i)(?:^|//)(?:t\.me|telegram|twitter\.com|facebook\.com|instagram\.com|pinterest|wa\.me)|(?:^|/)login\?|\.(?:png|jpe?g|webp|gif|ico|svg|css|js|woff2?)(?:\?|$)");

    /// <summary>Tên file đứng trước link (bài của họ: dòng tên file, rồi [1.62 GB](link)). Nếu label
    /// của chính anchor đã là tên file thì dùng nó.</summary>
    static string FileNameNear(string blockText, string anchorText)
    {
        if (Regex.IsMatch(anchorText ?? "", @"\d{3,4}p.*\.(mkv|mp4)", RegexOptions.IgnoreCase))
            return anchorText.Trim();

        var lines = (blockText ?? "").Split('\n');

        foreach (string raw in lines.Reverse())
        {
            string line = raw.Trim();

            if (Regex.IsMatch(line, @"(?i)\d{3,4}p.*\.(mkv|mp4|avi)"))
                return line;
        }

        return (blockText ?? "").Trim();
    }

    /// <summary>`The.Whisper.Man.2026.1080p.NF.WEB-DL...mkv` -> "The Whisper Man"; bài collection
    /// nhiều phim thì đây là chìa tách (bài học LOT R 3 phim một bài).</summary>
    static string FilmOf(string file, string blockText)
    {
        string name = Regex.Match(file ?? "", @"^(?<n>.+?)\.\d{4}\.").Groups["n"].Value;

        if (string.IsNullOrWhiteSpace(name))
            name = Regex.Match(blockText ?? "", @"^(?<n>.{4,60}?)\s*$").Groups["n"].Value;

        return Cut(name.Replace('.', ' ').Trim());
    }

    static bool IsPack(string file)
        => Regex.IsMatch(file ?? "", @"(?i)\b(zip|pack|batch)\b|\.rar");

    /// <summary>`S02E05` / `Season 2 Episode 5` / `1x05`. Không có thì se=0 (phim lẻ).</summary>
    static void ParseEpisode(string file, ref short se, ref short ep)
    {
        Match m = Regex.Match(file ?? "", @"(?i)S(?<s>\d{1,2})[\s._-]?E(?<e>\d{1,3})");

        if (!m.Success)
            m = Regex.Match(file ?? "", @"(?i)\b(?<s>[1-9])x(?<e>\d{2,3})\b");

        if (!m.Success)
            m = Regex.Match(file ?? "", @"(?i)season\s*(?<s>\d{1,2}).{0,20}?episode\s*(?<e>\d{1,3})");

        if (m.Success)
        {
            se = short.Parse(m.Groups["s"].Value);
            ep = short.Parse(m.Groups["e"].Value);
        }
    }

    /// <summary>`...-860508` -> 860508; 0 nếu không có.</summary>
    static long IdOf(string url)
    {
        Match m = Regex.Match(url ?? "", @"-(?<id>\d{4,8})(?:[/?#]|$)");
        return m.Success ? long.Parse(m.Groups["id"].Value) : 0;
    }

}
