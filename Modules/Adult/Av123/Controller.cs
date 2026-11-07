using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Av123;

public class Av123Controller : BaseSisiController
{
    public Av123Controller() : base(ModInit.conf) { }

    #region taxonomy cache
    static List<(string name, string slug, int count)> _genres;
    static List<(string name, string slug, int count)> _makers;
    static DateTime _genresAt = DateTime.MinValue;
    static DateTime _makersAt = DateTime.MinValue;
    static bool _makersWarming;
    static bool _diskTried;
    static readonly object _taxoLock = new object();
    static readonly TimeSpan TaxoTtl = TimeSpan.FromMinutes(720);

    class TaxoDto
    {
        public string at { get; set; }
        public string lang { get; set; }
        public string[][] genres { get; set; }
        public string[][] makers { get; set; }
    }

    static string TaxoFile()
    {
        string dir = ModInit.modpath;
        if (string.IsNullOrEmpty(dir)) return null;
        return System.IO.Path.Combine(dir, "taxo.json");
    }

    static void LoadTaxoFromDisk()
    {
        if (_diskTried) return;
        _diskTried = true;
        try
        {
            string file = TaxoFile();
            if (file == null || !System.IO.File.Exists(file)) return;
            var dto = System.Text.Json.JsonSerializer.Deserialize<TaxoDto>(System.IO.File.ReadAllText(file));
            if (dto == null || !DateTime.TryParse(dto.at, null, DateTimeStyles.AdjustToUniversal, out DateTime at)) return;
            // taxo luu theo ngon ngu: doi /en -> /vi thi cache cu (ten tieng Anh) phai bi bo.
            if (!string.Equals(dto.lang, Av123To.Lang, StringComparison.Ordinal)) return;
            if (DateTime.UtcNow - at >= TaxoTtl) return;
            lock (_taxoLock)
            {
                _genres = ToList(dto.genres);
                _makers = ToList(dto.makers);
                if (_genres != null && _genres.Count > 0) _genresAt = at;
                if (_makers != null && _makers.Count > 0) _makersAt = at;
            }
        }
        catch { }
    }

    static List<(string, string, int)> ToList(string[][] raw)
    {
        if (raw == null) return null;
        var list = new List<(string, string, int)>();
        foreach (var x in raw)
        {
            if (x == null || x.Length < 3) continue;
            int.TryParse(x[2], out int c);
            list.Add((x[0], x[1], c));
        }
        return list;
    }

    static void SaveTaxoToDisk()
    {
        try
        {
            string file = TaxoFile();
            if (file == null) return;
            var dto = new TaxoDto
            {
                at = DateTime.UtcNow.ToString("o"),
                lang = Av123To.Lang,
                genres = _genres?.Select(x => new[] { x.Item1, x.Item2, x.Item3.ToString() }).ToArray(),
                makers = _makers?.Select(x => new[] { x.Item1, x.Item2, x.Item3.ToString() }).ToArray()
            };
            System.IO.File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(dto));
        }
        catch { }
    }

    async Task<List<(string, string, int)>> GetGenresAsync()
    {
        LoadTaxoFromDisk();
        lock (_taxoLock)
        {
            if (_genres != null && DateTime.UtcNow - _genresAt < TaxoTtl) return _genres;
        }
        try
        {
            string html = await FetchHtmlAsync((ModInit.conf?.host ?? Av123To.SiteHost) + Av123To.Lang + "/genres", seconds: 6, marker: "gchip");
            var list = Av123To.Taxo(html, "genres");
            if (list.Count > 0)
            {
                list.Sort((a, b) => b.count.CompareTo(a.count));
                lock (_taxoLock) { _genres = list; _genresAt = DateTime.UtcNow; }
                SaveTaxoToDisk();
                return list;
            }
        }
        catch { }
        lock (_taxoLock) return _genres ?? new List<(string, string, int)>();
    }

    List<(string, string, int)> GetMakers()
    {
        LoadTaxoFromDisk();
        lock (_taxoLock)
        {
            if (_makers != null && DateTime.UtcNow - _makersAt < TaxoTtl) return _makers;
        }
        WarmTaxo();
        lock (_taxoLock) return _makers ?? new List<(string, string, int)>();
    }

    public static void WarmTaxo()
    {
        LoadTaxoFromDisk();
        lock (_taxoLock)
        {
            if (_makers != null && DateTime.UtcNow - _makersAt < TaxoTtl) return;
            if (_makersWarming) return;
            _makersWarming = true;
        }
        _ = Task.Run(WarmMakersAsync);
    }

    static async Task WarmMakersAsync()
    {
        try
        {
            var all = new List<(string, string, int)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string host0 = ModInit.conf?.host ?? Av123To.SiteHost;
            for (int p = 1; p <= 40; p++)
            {
                if (p > 1) await Task.Delay(600);
                string html = await FetchHtmlAsync(host0 + Av123To.Lang + "/makers?sort=count&page=" + p, seconds: 10, marker: "mcard", warm: true);
                var list = Av123To.Taxo(html, "makers");
                if (list.Count == 0) break;
                int added = 0;
                foreach (var x in list)
                    if (seen.Add(x.slug)) { all.Add(x); added++; }
                if (added == 0) break;
                var snapshot = new List<(string, string, int)>(all);
                snapshot.Sort((a, b) => b.Item3.CompareTo(a.Item3));
                lock (_taxoLock) { _makers = snapshot; _makersAt = DateTime.UtcNow; }
                if (list.Count < 60) break;
            }
            if (all.Count > 0) SaveTaxoToDisk();
        }
        catch { }
        finally { _makersWarming = false; }
    }
    #endregion

    #region list
    [HttpGet, Staticache(manually: true)]
    [Route("av123")]
    async public Task<ActionResult> Index(string search, string c, string sort, string rel, string rec, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (!string.IsNullOrWhiteSpace(rec))
            return await RecList(rec, pg);

        if (pg < 1) pg = 1;
        bool isSearch = !string.IsNullOrWhiteSpace(search);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"av123:v3:{search}:{c}:{sort}:{pg}"),
            isSearch || !string.IsNullOrWhiteSpace(c) ? 10 : 30, async e =>
        {
            string url = BuildUrl(search, c, sort, pg);
            string html = await FetchHtmlAsync(url, marker: "card__cover");

            var playlists = Av123To.Playlist("av123/vidosik", html);
            if (playlists == null || playlists.Count == 0)
            {
                if (isSearch || !string.IsNullOrWhiteSpace(c) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success((playlists, Av123To.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var genres = await GetGenresAsync();
        var makers = GetMakers();
        var menu = Av123To.Menu(host, c, sort, genres, makers);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            menu,
            total_pages: cache.Value.total_pages
        );
    }

    static readonly Dictionary<string, List<string>> _recChain = new();
    static readonly object _recLock = new object();
    static readonly string _recUser = Guid.NewGuid().ToString();

    // "You may like" (rec2) — y nhu nut Load more tren web:
    // trang 1 POST /items/<code>/items, trang sau POST /next/items/<recomm_id>
    async Task<ActionResult> RecList(string rec, int pg)
    {
        string code = rec.TrimEnd('/');
        int slash = code.LastIndexOf('/');
        if (slash >= 0) code = code.Substring(slash + 1);
        code = code.ToLowerInvariant();

        var items = new List<PlaylistItem>();
        string nextRec = null;

        try
        {
            string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            string ua = Av123To.ChromeUA;
            string url, payload;

            if (pg <= 1)
            {
                url = Av123Rec.Url("/items/" + code + "/items", ua, ts);
                payload = "{\"count\":12,\"target_user\":\"" + _recUser + "\",\"included_properties\":[\"code\",\"title_en\",\"duration\"]}";
            }
            else
            {
                string recId = null;
                lock (_recLock)
                {
                    if (_recChain.TryGetValue(code, out var chain) && chain.Count >= pg - 1)
                        recId = chain[pg - 2];
                }

                if (string.IsNullOrEmpty(recId))
                {
                    await RecList(rec, 1);
                    lock (_recLock)
                    {
                        if (_recChain.TryGetValue(code, out var ch) && ch.Count >= pg - 1)
                            recId = ch[pg - 2];
                    }
                    if (string.IsNullOrEmpty(recId)) return EmptyPage();
                }

                url = Av123Rec.Url("/next/items/" + Uri.EscapeDataString(recId), ua, ts);
                payload = "{\"count\":12}";
            }

            string json = await Http.Post(url, new StringContent(payload, Encoding.UTF8, "application/json"), timeoutSeconds: 20, headers: HeadersModel.Init(
                ("User-Agent", ua),
                ("Content-Type", "application/json"),
                ("Origin", Av123To.SiteHost),
                ("Referer", Av123To.SiteHost + "/")));

            if (!string.IsNullOrEmpty(json))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("recomm_id", out var rid) && rid.ValueKind == System.Text.Json.JsonValueKind.String)
                    nextRec = rid.GetString();

                if (root.TryGetProperty("recomms", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var it in arr.EnumerateArray())
                    {
                        string c = it.TryGetProperty("code", out var cEl) ? cEl.GetString() : null;
                        if (string.IsNullOrEmpty(c)) continue;

                        string title = it.TryGetProperty("title_en", out var tEl) ? tEl.GetString() : null;
                        string dur = it.TryGetProperty("duration", out var dEl) ? dEl.GetString() : null;

                        string name = (c + " — " + title).Trim();
                        if (name.Length > 150) name = name.Substring(0, 147) + "...";

                        string detail = Av123To.Lang + "/v/" + c.ToLowerInvariant();
                        string poster = Av123Rec.Cover(c);

                        items.Add(new PlaylistItem
                        {
                            name = name,
                            video = "av123/vidosik?uri=" + HttpUtility.UrlEncode(detail),
                            picture = poster,
                            time = Av123Rec.Duration(dur),
                            json = true,
                            bookmark = new Bookmark { site = "av123", href = Av123To.SiteHost + detail, image = poster }
                        });
                    }
                }
            }
        }
        catch { }

        if (!string.IsNullOrEmpty(nextRec))
        {
            lock (_recLock)
            {
                if (_recChain.Count > 200) _recChain.Clear();

                if (!_recChain.TryGetValue(code, out var chain))
                {
                    chain = new List<string>();
                    _recChain[code] = chain;
                }
                while (chain.Count < pg) chain.Add(null);
                chain[pg - 1] = nextRec;
            }
        }

        if (items.Count == 0)
            return EmptyPage();

        var menu = Av123To.Menu(host, null, null, await GetGenresAsync(), GetMakers());

        return PlaylistResult(items, false, menu, total_pages: 999);
    }

    // "Related videos" cua trang detail (12 video cung series/dien vien) — 1 trang
    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1) return EmptyPage();

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"av123:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? rel : init.host + rel;
            string html = await FetchHtmlAsync(url, seconds: 20, marker: "vside");

            var playlists = Av123To.PlaylistRelated("av123/vidosik", html);
            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 0));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var menu = Av123To.Menu(host, null, null, await GetGenresAsync(), GetMakers(), related: true);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache, menu, total_pages: 1);
    }

    ActionResult EmptyPage() => Json(new { total_pages = 1, list = new List<PlaylistItem>() });

    string BuildUrl(string search, string c, string sort, int pg)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return init.host + Av123To.Lang + "/search?keyword=" + Uri.EscapeDataString(search.Trim()) + "&page=" + pg;

        string path = string.IsNullOrWhiteSpace(c) ? Av123To.Lang + "/new" : c;
        string q = string.IsNullOrWhiteSpace(sort) ? "?page=" + pg : "?sort=" + sort + "&page=" + pg;
        return init.host + path + q;
    }
    #endregion

    #region detail -> stream
    [HttpGet, Staticache(manually: true)]
    [Route("av123/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (string.IsNullOrEmpty(uri) || !uri.Contains("/v/", StringComparison.OrdinalIgnoreCase))
            return OnError("bad_uri");

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : init.host + uri;
        string html = await FetchHtmlAsync(pageUrl, seconds: 25);

        string episode = Av123To.ExtractEpisode(html);
        if (string.IsNullOrEmpty(episode) || !Uri.TryCreate(episode, UriKind.Absolute, out Uri ep))
            return OnError("no_episode", refresh_proxy: true);

        string embedHost = ep.GetLeftPart(UriPartial.Authority);
        string hash = ep.AbsolutePath.Trim('/');
        if (hash.StartsWith("e/")) hash = hash.Substring(2);
        string poster = HttpUtility.ParseQueryString(ep.Query).Get("poster");

        string apiUrl = embedHost + "/stream?id=" + hash
            + (string.IsNullOrEmpty(poster) ? "" : "&poster=" + Uri.EscapeDataString(poster));

        string json = await FetchTextAsync(apiUrl, HeadersModel.Init(
            ("User-Agent", Av123To.ChromeUA),
            ("Referer", episode),
            ("Accept", "application/json")));

        string m3u8 = Av123To.ExtractStream(json);
        if (string.IsNullOrEmpty(m3u8))
            return OnError("no_stream", refresh_proxy: true);

        var dict = new Dictionary<string, string>
        {
            ["Auto"] = $"{host}/av123/hls.m3u8?u={HttpUtility.UrlEncode(m3u8)}&r={HttpUtility.UrlEncode(embedHost + "/")}"
        };
        return Json(dict);
    }
    #endregion

    #region info
    static readonly Dictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<(string slug, string title, string dur)> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("av123/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.Contains("/v/", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        lock (_taxoLock)
        {
            if (_infoCache.TryGetValue(uri, out var hit) && DateTime.UtcNow - hit.at < InfoTtl)
                return InfoResult(hit.chips, hit.rel, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : init.host + uri;
        string html = await FetchHtmlAsync(pageUrl, seconds: 20, marker: "watch__info");

        var chips = Av123To.Chips(html);
        var rel = Av123To.Related(html);

        if (chips.Count > 0)
        {
            lock (_taxoLock) { _infoCache[uri] = (DateTime.UtcNow, chips, rel); }
        }

        return InfoResult(chips, rel, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "actresses" => "Diễn viên",
        "makers" => "Hãng",
        "series" => "Series",
        "genres" => "Thể loại",
        "tags" => "Tag",
        _ => char.ToUpperInvariant(kind[0]) + kind.Substring(1)
    };

    // Thu tu uu tien hien thi; kind la do site tra ve nen KHONG whitelist —
    // site them loai moi (vd "labels") van phai ra menu.
    static readonly string[] KindOrder = { "actresses", "makers", "series", "genres", "tags" };

    static int KindRank(string kind)
    {
        int i = System.Array.IndexOf(KindOrder, kind);
        return i < 0 ? KindOrder.Length : i;
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, List<(string slug, string title, string dur)> rel, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct()
                     .OrderBy(KindRank).ThenBy(x => x))
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/av123?c={HttpUtility.UrlEncode(Av123To.Lang + "/" + kind + "/" + x.slug)}"
            }).ToList();

            if (items.Count == 0) continue;

            groups.Add(new { title = KindLabel(kind), items });
        }

        if (rel.Count > 0)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new
                    {
                        title = "Video liên quan",
                        plain = true,
                        url = $"{host}/av123?rel={HttpUtility.UrlEncode(uri)}"
                    },
                    new
                    {
                        title = "Có thể bạn thích",
                        plain = true,
                        url = $"{host}/av123?rec={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        // `items` chi la fallback khi khong co `groups` (client: sisi.js "groups = [{title,items}]").
        // Menu long-press luon co groups nen de rong cho khoi lap.
        return Json(new { items = new object[0], groups });
    }
    #endregion

    #region hls proxy
    static string Ref(string r) => string.IsNullOrEmpty(r) ? Av123To.SiteHost + "/" : r;

    [HttpGet]
    [Route("av123/hls")]
    [Route("av123/hls.m3u8")]
    async public Task<ActionResult> Hls(string u, string r)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(u) || !u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return StatusCode(404);

        string body = await FetchTextAsync(u, HeadersModel.Init(
            ("User-Agent", Av123To.ChromeUA),
            ("Referer", Ref(r)),
            ("Accept", "*/*")));

        if (string.IsNullOrEmpty(body) || !body.Contains("#EXTM3U"))
            return OnError("stream_links", refresh_proxy: true);

        if (!Uri.TryCreate(u, UriKind.Absolute, out Uri baseUri))
            return OnError("bad_uri");

        bool master = body.Contains("#EXT-X-STREAM-INF");
        var sb = new StringBuilder(body.Length + 2048);
        char nl = (char)10;

        foreach (string raw in body.Split(nl))
        {
            string line = raw.Trim();
            if (line.Length == 0) { sb.Append(nl); continue; }

            if (line[0] == '#')
            {
                sb.Append(Regex.Replace(line, @"URI=\x22([^\x22]+)\x22", mm =>
                {
                    string abs = Abs(baseUri, mm.Groups[1].Value);
                    return abs == null ? mm.Value : "URI=\x22" + SegUrl(abs, r) + "\x22";
                }));
                sb.Append(nl);
                continue;
            }

            string target = Abs(baseUri, line);
            if (target == null) { sb.Append(line).Append(nl); continue; }

            sb.Append(master ? HlsUrl(target, r) : SegUrl(target, r));
            sb.Append(nl);
        }

        return Content(sb.ToString(), "application/vnd.apple.mpegurl");
    }

    [HttpGet]
    [Route("av123/seg")]
    async public Task<ActionResult> Seg(string u, string r)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(u) || !u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return StatusCode(404);

        Response.ContentType = ContentTypeFor(u);

        bool ok = false;
        try
        {
            await Http.BaseGetReaderAsync(async e =>
            {
                try { await e.stream.CopyToAsync(Response.Body); ok = true; }
                catch { }
            }, u, timeoutSeconds: 30, headers: HeadersModel.Init(
                ("User-Agent", Av123To.ChromeUA),
                ("Referer", Ref(r)),
                ("Accept", "*/*")), statusCodeOK: false, proxy: proxy, cookieContainer: _cookies);
        }
        catch { }

        if (!ok) Response.StatusCode = 404;
        return new EmptyResult();
    }

    string HlsUrl(string url, string r) => $"{host}/av123/hls.m3u8?u={HttpUtility.UrlEncode(url)}&r={HttpUtility.UrlEncode(r)}";

    string SegUrl(string url, string r) => $"{host}/av123/seg?u={HttpUtility.UrlEncode(url)}&r={HttpUtility.UrlEncode(r)}";

    static string ContentTypeFor(string url)
    {
        string path = url;
        int i = path.IndexOf('?');
        if (i > 0) path = path.Substring(0, i);
        path = path.ToLowerInvariant();
        if (path.EndsWith(".ts") || path.EndsWith(".css")) return "video/mp2t";
        if (path.EndsWith(".m4s") || path.EndsWith(".mp4")) return "video/mp4";
        if (path.EndsWith(".m3u8")) return "application/vnd.apple.mpegurl";
        return "video/mp2t";
    }

    static string Abs(Uri baseUri, string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value.StartsWith("//")) return "https:" + value;
            return new Uri(baseUri, value).AbsoluteUri;
        }
        catch { return null; }
    }
    #endregion

    #region http
    static async Task<string> FetchHtmlAsync(string url, int seconds = 30, string marker = null, bool warm = false)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var headers = HeadersModel.Init(
            ("User-Agent", Av123To.ChromeUA),
            ("Referer", Av123To.SiteHost + "/"));
        int attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            if (attempt > 0)
            {
                await Task.Delay(Math.Min(2500, 500 * attempt));
                if (attempt % 2 == 0)
                    await FetchTextAsync(Av123To.SiteHost + Av123To.Lang, headers, 8, warm);
            }
            attempt++;
            string html = await FetchTextAsync(url, headers, 8, warm);
            if (string.IsNullOrEmpty(html)) continue;
            if (marker != null && !html.Contains(marker, StringComparison.OrdinalIgnoreCase)) continue;
            return html;
        }
        return null;
    }

    static readonly System.Threading.SemaphoreSlim _netGate = new System.Threading.SemaphoreSlim(1, 1);
    static readonly System.Threading.SemaphoreSlim _warmGate = new System.Threading.SemaphoreSlim(1, 1);
    static readonly System.Net.CookieContainer _cookies = new System.Net.CookieContainer();

    static async Task<string> FetchTextAsync(string url, IReadOnlyList<HeadersModel> headers, int timeoutSeconds = 15, bool warm = false)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var gate = warm ? _warmGate : _netGate;
        await gate.WaitAsync();
        try
        {
            await Task.Delay(120);
            var (content, response) = await Http.BaseGet(url, headers: headers, statusCodeOK: false, timeoutSeconds: timeoutSeconds, cookieContainer: _cookies);
            if (response == null || (int)response.StatusCode >= 400) return null;
            return content;
        }
        catch { return null; }
        finally { gate.Release(); }
    }
    #endregion
}
