using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace VNJav;

public class VNJavController : BaseSisiController
{
    public VNJavController() : base(ModInit.conf) { }

    #region list
    [HttpGet, Staticache(manually: true)]
    [Route("vnjav")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // "Video liên quan" (popup) trỏ ?rel=<uri detail>
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;
        sort = VNJavTo.NormalizeSort(sort);

        var cache = await InvokeCacheResult(ipkey($"vnjav:{search}:{c}:{sort}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchTextAsync(
                VNJavTo.Uri(init.host, search, c, pg, sort), ListHeaders());
            var playlists = VNJavTo.Playlist("vnjav/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));
            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync(search, sort, c));
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        // related = danh sách gợi ý -> chỉ dòng Tìm kiếm + menu base, ẩn dòng Sắp xếp
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/vnjav" }
            }
            : VNJavTo.MenuHead(host, search, sort, c);
        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("vnjav:taxonomy");
        if (!hybridCache.TryGetValue(key,
            out (List<(string name, string slug)> tags, List<(string name, string slug)> actors) hit)
            || hit.tags == null)
        {
            var tagTask = FetchTextAsync(VNJavTo.SiteHost + "/the", ListHeaders());
            var actorTask = FetchTextAsync(VNJavTo.SiteHost + "/dien-vien", ListHeaders());
            await Task.WhenAll(tagTask, actorTask);
            hit = (VNJavTo.Taxonomy(await tagTask, "the"),
                   VNJavTo.Taxonomy(await actorTask, "dien-vien"));
            if (hit.tags.Count > 0 || hit.actors.Count > 0)
                hybridCache.Set(key, hit, cacheTime(360));
        }
        return VNJavTo.Menu(host, hit.tags, hit.actors);
    }
    #endregion

    #region popup (info long-press) + Video liên quan
    // sisi.js gọi đúng route <mod>/genres?uri= ; trả {items:[], groups:[...]}, KHÔNG bao giờ 500
    static readonly Dictionary<string, (DateTime at,
        List<(string kind, string slug, string name)> chips,
        List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("vnjav/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = new { items = new object[0], groups = new object[0] };
        if (string.IsNullOrWhiteSpace(uri))
            return Json(empty);

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : VNJavTo.SiteHost + "/" + uri.Trim('/');

        // Chặn SSRF: chỉ fetch đúng host site
        if (!pageUrl.Contains("vnjav.net", StringComparison.OrdinalIgnoreCase))
            return Json(empty);

        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(uri, out var hit)
                && DateTime.UtcNow - hit.at < InfoTtl && hit.chips != null)
                return InfoResult(hit.chips, hit.rel, uri);
        }

        // 1 fetch detail duy nhất -> chip + related
        string html = await FetchTextAsync(pageUrl, ListHeaders());
        var chips = VNJavTo.Chips(html ?? "");
        var rel = VNJavTo.PlaylistRelated("vnjav/vidosik", html ?? "", uri);

        if (chips.Count > 0 || rel.Count > 0)
        {
            lock (_infoCache)
                _infoCache[uri] = (DateTime.UtcNow, chips, rel);
        }

        return InfoResult(chips, rel, uri);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips,
        List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        // Thứ tự: người -> phân loại -> liên quan; bỏ hẳn nhóm rỗng; title 1 từ.
        foreach (var (kind, label, c) in new[] {
            ("actor", "Diễn viên", "dien-vien"),
            ("cat", "Danh mục", "danh-muc"),
            ("tag", "Thẻ", "the") })
        {
            var items = chips?.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/vnjav?c={HttpUtility.UrlEncode(c + "/" + x.slug)}"
            }).ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title = label, items });
        }

        // "Liên quan" = 1 item plain trỏ ?rel= (KHÔNG nhồi list phim vào popup)
        if (rel != null && rel.Count > 0)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new { title = "Video liên quan", plain = true,
                          url = $"{host}/vnjav?rel={HttpUtility.UrlEncode(uri)}" }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        // 1 trang duy nhất -> total_pages PHẢI = 1 (0 = app nạp lặp vô hạn)
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? rel : VNJavTo.SiteHost + "/" + rel.Trim('/');
        if (!pageUrl.Contains("vnjav.net", StringComparison.OrdinalIgnoreCase))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult(ipkey($"vnjav:rel:{rel}"), 180, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchTextAsync(pageUrl, ListHeaders());
            var playlists = VNJavTo.PlaylistRelated("vnjav/vidosik", html ?? "", rel);
            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: false);
            return e.Success(playlists);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache, await MenuAsync(null, null, null, related: true), total_pages: 1);
    }
    #endregion

    #region resolve 2 server
    // Trả Dictionary<label, packed> — packed = "loadvid|<embedUrl>" | "streamtape|<embedUrl>"
    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"vnjav:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cached))
            return cached;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : VNJavTo.SiteHost + (uri.StartsWith("/") ? uri : "/" + uri);

        var links = new Dictionary<string, string>();
        string html = await FetchTextAsync(pageUrl, ListHeaders());
        if (string.IsNullOrEmpty(html))
            return null;

        var (loadvid, streamtape) = VNJavTo.Servers(html);
        if (!string.IsNullOrEmpty(loadvid)) links["LoadVid"] = "loadvid|" + loadvid;
        if (!string.IsNullOrEmpty(streamtape)) links["StreamTape"] = "streamtape|" + streamtape;
        // Server 2 (upload18/helvid): bỏ — CF Worker chặn mọi client server-side (xem todo.md).

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("vnjav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
        {
            // LoadVid = HLS -> cần đuôi .m3u8 để app chọn hls.js;
            // StreamTape = mp4 -> giữ route video trần.
            string route = v.Value.StartsWith("loadvid|", StringComparison.OrdinalIgnoreCase)
                ? "vnjav/video.m3u8" : "vnjav/video";
            return $"{host}/{route}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}";
        }));
    }

    [HttpGet]
    [Route("vnjav/video")]
    [Route("vnjav/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        if (string.IsNullOrEmpty(q))
            return OnError("stream_links", refresh_proxy: true);

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string packed) || string.IsNullOrEmpty(packed))
            return OnError("stream_links", refresh_proxy: true);

        int bar = packed.IndexOf('|');
        string kind = packed.Substring(0, bar);
        string embed = packed.Substring(bar + 1);

        if (kind == "loadvid")
        {
            // Tự phục vụ playlist (resolve-token trả text m3u8, không phải URL).
            return Redirect($"{host}/vnjav/hls.m3u8?e={HttpUtility.UrlEncode(embed)}");
        }

        // StreamTape: bóc token nhánh script -> GET (Http.BaseGet tự follow 302) -> lấy URL CDN cuối
        string embedHtml = await FetchTextAsync(embed, StreamHeaders(embed));
        string getVideo = VNJavTo.StreamTapeGetVideo(embedHtml);
        if (string.IsNullOrEmpty(getVideo))
            return OnError("stream_links", refresh_proxy: true);

        string final = await FinalUrlAsync(getVideo, StreamHeaders(embed));
        if (string.IsNullOrEmpty(final))
            return OnError("stream_links", refresh_proxy: true);

        var hdr = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", VNJavTo.ChromeUA),
            ("Referer", "https://streamtape.com/")));
        return Redirect(HostStreamProxy(final, hdr));
    }
    #endregion

    #region loadvid: hls.m3u8 + seg (cookie jar dùng chung cho GET embed + POST resolve-token)
    static readonly CookieContainer _lvCookies = new CookieContainer();

    [HttpGet]
    [Route("vnjav/hls.m3u8")]
    async public Task<ActionResult> Hls(string e)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        if (string.IsNullOrEmpty(e) || !e.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return StatusCode(404);

        // 1) GET embed -> set cookie (XSRF/session) + lấy csrf/token/hash
        string embedHtml = await FetchTextAsync(e, LoadVidHeaders(e), 15, _lvCookies);
        if (string.IsNullOrEmpty(embedHtml))
            return OnError("stream_links", refresh_proxy: true);

        var (csrf, token, hash) = VNJavTo.LoadVidConfig(embedHtml);
        if (string.IsNullOrEmpty(csrf) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(hash))
            return OnError("stream_links", refresh_proxy: true);

        // 2) POST /videos/resolve-token -> thân là nội dung M3U8 (segment URL tuyệt đối)
        string payload = "{\"token\":\"" + token + "\",\"hash\":\"" + hash + "\"}";
        string m3u8 = await Http.Post("https://cdn.loadvid.com/videos/resolve-token",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            timeoutSeconds: 20, cookieContainer: _lvCookies,
            headers: HeadersModel.Init(
                ("User-Agent", VNJavTo.ChromeUA),
                ("Referer", e),
                ("Origin", "https://cdn.loadvid.com"),
                ("X-CSRF-TOKEN", csrf),
                ("Accept", "application/vnd.apple.mpegurl,*/*")));

        if (string.IsNullOrEmpty(m3u8) || !m3u8.Contains("#EXTM3U"))
            return OnError("stream_links", refresh_proxy: true);

        // 3) rewrite mọi URL segment về route /vnjav/seg (set Content-Type video/mp2t,
        //    vì upstream trả image/png nhưng thân là MPEG-TS thật ngay byte 0 — đã đo).
        var sb = new StringBuilder(m3u8.Length + 2048);
        foreach (string raw in m3u8.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) { sb.Append('\n'); continue; }
            if (line[0] == '#') { sb.Append(line).Append('\n'); continue; }
            if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                // Segment upstream BẮT BUỘC đuôi .png (đổi .ts là 404) dù thân là MPEG-TS thật.
                // ffprobe dò format theo đuôi CUỐI của URL -> thêm &x=ts để URL kết thúc bằng
                // "ts" (không phải ".png"), ffprobe/hls.js mới nhận h264+aac thay vì image2/png.
                sb.Append($"{host}/vnjav/seg.ts?u={HttpUtility.UrlEncode(line)}&x=ts").Append('\n');
            else
                sb.Append(line).Append('\n');
        }
        return Content(sb.ToString(), "application/vnd.apple.mpegurl");
    }

    [HttpGet]
    [Route("vnjav/seg")]
    [Route("vnjav/seg.ts")]
    async public Task<ActionResult> Seg(string u)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        if (string.IsNullOrEmpty(u) || !u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return StatusCode(404);

        Response.ContentType = "video/mp2t";   // upstream nói image/png nhưng là TS thật
        bool ok = false;
        try
        {
            await Http.BaseGetReaderAsync(async r =>
            {
                try { await r.stream.CopyToAsync(Response.Body); ok = true; }
                catch { }
            }, u, timeoutSeconds: 30,
            headers: HeadersModel.Init(
                ("User-Agent", VNJavTo.ChromeUA),
                ("Referer", "https://cdn.loadvid.com/"),
                ("Accept", "*/*")),
            statusCodeOK: false, proxy: proxy, cookieContainer: _lvCookies);
        }
        catch { }
        if (!ok) Response.StatusCode = 404;
        return new EmptyResult();
    }
    #endregion

    #region http helpers
    static IReadOnlyList<HeadersModel> ListHeaders() => HeadersModel.Init(
        ("User-Agent", VNJavTo.ChromeUA),
        ("Referer", VNJavTo.SiteHost + "/"));

    static IReadOnlyList<HeadersModel> LoadVidHeaders(string embed) => HeadersModel.Init(
        ("User-Agent", VNJavTo.ChromeUA),
        ("Referer", VNJavTo.SiteHost + "/"));

    static IReadOnlyList<HeadersModel> StreamHeaders(string embed) => HeadersModel.Init(
        ("User-Agent", VNJavTo.ChromeUA),
        ("Referer", embed));

    static async Task<string> FetchTextAsync(string url, IReadOnlyList<HeadersModel> headers,
        int timeoutSeconds = 15, CookieContainer cookies = null)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var (content, response) = await Http.BaseGet(url, headers: headers,
                statusCodeOK: false, timeoutSeconds: timeoutSeconds, cookieContainer: cookies);
            if (response == null || (int)response.StatusCode >= 400) return null;
            return content;
        }
        catch { return null; }
    }

    // StreamTape get_video trả 302 -> Location = URL CDN mp4 (1.2GB).
    // Không được follow redirect (sẽ tải cả body 1GB rồi timeout): dùng 1 HttpClient
    // riêng AllowAutoRedirect=false, chỉ đọc header Location.
    static readonly HttpClient _noRedirect = new HttpClient(new HttpClientHandler()
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });

    static async Task<string> FinalUrlAsync(string url, IReadOnlyList<HeadersModel> headers)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var h in headers)
                req.Headers.TryAddWithoutValidation(h.name, h.val);
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var resp = await _noRedirect.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            string loc = resp.Headers.Location?.ToString();
            if (loc != null && !loc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                loc = new Uri(new Uri(url), loc).ToString();
            return loc?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true ? loc : null;
        }
        catch { return null; }
    }
    #endregion
}
