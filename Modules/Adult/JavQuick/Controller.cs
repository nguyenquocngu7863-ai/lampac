using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace JavQuick;

public class JavQuickController : BaseSisiController
{
    public JavQuickController() : base(ModInit.conf) { }

    static readonly HttpClient _client = new HttpClient()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    [HttpGet, Staticache(manually: true)]
    [Route("javquick")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        var cache = await InvokeCacheResult(ipkey($"javquick:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(JavQuickTo.Uri(init.host, search, c, pg));

            if (html == null)
                return e.Fail("playlists", refresh_proxy: true);

            var playlists = JavQuickTo.Playlist("javquick/vidosik", html);

            if (playlists == null || playlists.Count == 0)
                return e.Success(new List<PlaylistItem>());

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return PlaylistResult(cache, await MenuAsync(search, c));

        var list = cache.Value;
        if (list == null || list.Count == 0)
            return EmptyPlaylistResult(await MenuAsync(search, c));

        return PlaylistResult(cache, await MenuAsync(search, c));
    }

    // MVC bo count=0 khoi JSON -> tra tay de app hien "Khong tim thay ket qua"
    static readonly System.Text.Json.JsonSerializerOptions menuJsonOptions =
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

    ActionResult EmptyPlaylistResult(IReadOnlyList<MenuItem> menu, int total_pages = 1)
    {
        Response.Headers.CacheControl = "no-cache";

        string body = "{\"count\":0,\"total_pages\":" + total_pages
            + ",\"menu\":" + System.Text.Json.JsonSerializer.Serialize(menu ?? new List<MenuItem>(), menuJsonOptions)
            + ",\"list\":[]}";

        return Content(body, "application/json; charset=utf-8");
    }

    async Task<string> FetchHtmlAsync(string url, string referer = null)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string html = null;
                await httpHydra.GetSpan(url, span =>
                {
                    html = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", JavQuickTo.ChromeUA),
                    ("Referer", string.IsNullOrEmpty(referer) ? JavQuickTo.SiteHost + "/" : referer)
                ));

                if (!string.IsNullOrEmpty(html))
                    return html;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"JavQuick: fetch {url} attempt {attempt} -> {ex.Message}");
            }

            if (attempt == 0)
                await Task.Delay(600);
        }

        Console.WriteLine("JavQuick: fetch fail " + url);
        return null;
    }

    #region menu
    async Task<List<MenuItem>> MenuAsync(string search, string c)
    {
        string root = host + "/javquick";

        string link(string s)
        {
            string q;
            if (!string.IsNullOrWhiteSpace(search)) q = "search=" + HttpUtility.UrlEncode(search);
            else if (!string.IsNullOrWhiteSpace(c)) q = "c=" + HttpUtility.UrlEncode(c);
            else q = "";
            return q.Length == 0 ? root : root + "?" + q;
        }

        var res = new List<MenuItem>(3)
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };

        // Trang web khong co sort -> dong 2 la nhom list co dinh
        res.Add(new MenuItem()
        {
            title = "Danh sách",
            playlist_url = "submenu",
            submenu = new List<MenuItem>()
            {
                new MenuItem("Mới nhất", root + "?c=release"),
                new MenuItem("Sắp ra mắt", root + "?c=upcoming"),
                new MenuItem("Trang chủ", root)
            }
        });

        res.AddRange(await MenuBaseAsync());
        return res;
    }

    // /genres (361 muc) + /makers (945 muc), cache 12h, chia nhom <= 250/submenu
    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("javquick:tax");

        if (!hybridCache.TryGetValue(key, out (List<(string name, string path)> g, List<(string name, string path)> m) tax)
            || tax.g == null || tax.g.Count == 0)
        {
            string gh = await FetchHtmlAsync(JavQuickTo.SiteHost + "/genres");
            string mh = await FetchHtmlAsync(JavQuickTo.SiteHost + "/makers");

            tax = (JavQuickTo.Taxonomy(gh, "genres"), JavQuickTo.Taxonomy(mh, "maker"));
            if (tax.g.Count > 0)
                hybridCache.Set(key, tax, cacheTime(720));

            Console.WriteLine($"JavQuick: genres={tax.g.Count} makers={tax.m.Count}");
        }

        var res = new List<MenuItem>();
        res.AddRange(TaxGroup("Thể loại", "genre", tax.g));
        res.AddRange(TaxGroup("Hãng", "maker", tax.m));
        return res;
    }

    List<MenuItem> TaxGroup(string title, string kind, List<(string name, string path)> items)
    {
        var res = new List<MenuItem>();
        if (items == null || items.Count == 0) return res;

        const int chunk = 250;
        int groups = (items.Count + chunk - 1) / chunk;

        for (int i = 0; i < groups; i++)
        {
            var sub = items.Skip(i * chunk).Take(chunk).Select(x =>
            {
                string c = kind == "genre" ? "genre/" + x.path.Substring(8) : x.path.TrimStart('/');
                return new MenuItem(x.name, host + "/javquick?c=" + HttpUtility.UrlEncode(c));
            }).ToList();

            if (groups == 1)
                res.Add(new MenuItem() { title = title, playlist_url = "submenu", submenu = sub });
            else
                res.Add(new MenuItem() { title = $"{title} ({i + 1}/{groups})", playlist_url = "submenu", submenu = sub });
        }

        return res;
    }
    #endregion

    #region resolve
    sealed class SrcInfo
    {
        public bool direct;
        public string url;
        public string iv;
        public List<string> urls;
        public List<long> sizes;
        public List<long> starts;
        public long total;
    }

    // detail page -> danh sach format/part, moi muc la 1 URL trang (co ?type=&p=)
    async Task<Dictionary<string, string>> LinksAsync(string uri)
    {
        string key = ipkey($"javquick:links:{uri}");
        if (hybridCache.TryGetValue(key, out Dictionary<string, string> hit) && hit != null && hit.Count > 0)
            return hit;

        string pageUrl = JavQuickTo.Abs(JavQuickTo.SiteHost, uri);
        string html = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return null;

        var src = JavQuickTo.Sources(html);
        var links = new Dictionary<string, string>();

        if (src.Count == 0)
        {
            links["Video"] = pageUrl;
        }
        else
        {
            int maxPart = src.Max(x => x.part);
            foreach (var s in src)
            {
                string name = maxPart > 1 ? $"{s.fmt} P{s.part}" : s.fmt;
                string target = (string.IsNullOrEmpty(s.href) || s.href == "#")
                    ? pageUrl
                    : JavQuickTo.Abs(JavQuickTo.SiteHost, s.href);

                if (!links.ContainsKey(name))
                    links[name] = target;
            }
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(key, links, cacheTime(360));
        return links;
    }

    // trang player -> /watch?token=<data-id> -> iv + danh sach part (+ size tung part)
    async Task<SrcInfo> SrcAsync(string uri)
    {
        string key = ipkey($"javquick:src:{uri}");
        if (hybridCache.TryGetValue(key, out SrcInfo hit) && hit != null)
            return hit;

        string pageUrl = JavQuickTo.Abs(JavQuickTo.SiteHost, uri);
        string html = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return null;

        string dataId = JavQuickTo.DataId(html);
        if (string.IsNullOrEmpty(dataId))
            return null;

        string raw = await FetchHtmlAsync(
            JavQuickTo.SiteHost + "/watch?token=" + HttpUtility.UrlEncode(dataId), pageUrl);
        if (string.IsNullOrEmpty(raw))
            return null;

        var payload = JavQuickTo.ParsePayload(raw);
        if (payload == null)
            return null;

        var info = new SrcInfo();

        if (payload.direct)
        {
            info.direct = true;
            info.url = payload.url;
        }
        else
        {
            info.iv = payload.iv;
            info.urls = payload.urls;
            info.sizes = new List<long>();
            info.starts = new List<long>();

            long acc = 0;
            foreach (string u in payload.urls)
            {
                long sz = await SizeAsync(u);
                if (sz <= 0)
                    return null;

                info.starts.Add(acc);
                info.sizes.Add(sz);
                acc += sz;
            }

            info.total = acc;

            Console.WriteLine("JavQuick: src parts=" + info.urls.Count
                + " total=" + info.total + " iv=" + info.iv);
        }

        hybridCache.Set(key, info, cacheTime(15), inmemory: true);
        return info;
    }

    static async Task<long> SizeAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", JavQuickTo.ChromeUA);
            req.Headers.TryAddWithoutValidation("Range", "bytes=0-0");

            using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 206)
                return 0;

            var cr = resp.Content.Headers.ContentRange;
            if (cr != null && cr.Length.HasValue)
                return cr.Length.Value;

            return resp.Content.Headers.ContentLength ?? 0;
        }
        catch { }

        return 0;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javquick/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await LinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/javquick/video.mp4?uri={HttpUtility.UrlEncode(v.Value)}"));
    }

    [HttpGet]
    [Route("javquick/video")]
    [Route("javquick/video.mp4")]
    async public Task<ActionResult> Video(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(uri))
            return OnError("stream_links", refresh_proxy: true);

        var src = await SrcAsync(uri);
        if (src == null)
            return OnError("stream_links", refresh_proxy: true);

        // URL mp4 khong ma hoa -> cho Lampac proxy binh thuong (co Range)
        if (src.direct)
        {
            var headers = httpHeaders(init, HeadersModel.Init(
                ("User-Agent", JavQuickTo.ChromeUA),
                ("Referer", JavQuickTo.SiteHost + "/")));

            return Redirect(HostStreamProxy(src.url, headers));
        }

        if (!await StreamAsync(src, HttpContext.RequestAborted)
            && !HttpContext.Response.HasStarted)
            return OnError("stream_links");

        return new EmptyResult();
    }
    #endregion

    #region video stream (AES-CTR decrypt + Range, nhieu part)
    async Task<bool> StreamAsync(SrcInfo info, CancellationToken abort)
    {
        long total = info.total;
        if (total <= 0 || info.urls == null || info.urls.Count == 0)
            return false;

        long start = 0, end = total - 1;
        bool hasRange = false;

        string range = HttpContext.Request.Headers["Range"].ToString();
        var rm = Regex.Match(range ?? "", @"bytes\s*=\s*(\d+)\s*-\s*(\d*)");
        if (rm.Success)
        {
            hasRange = true;
            long.TryParse(rm.Groups[1].Value, out start);
            if (rm.Groups[2].Value.Length > 0)
                long.TryParse(rm.Groups[2].Value, out end);
        }

        if (start < 0) start = 0;
        if (end < start || end > total - 1) end = total - 1;

        if (start >= total)
        {
            HttpContext.Response.StatusCode = 416;
            HttpContext.Response.Headers["Content-Range"] = "bytes */" + total;
            return true;
        }

        var resp = HttpContext.Response;
        resp.StatusCode = hasRange ? 206 : 200;
        resp.ContentType = "video/mp4";
        resp.Headers["Accept-Ranges"] = "bytes";
        if (hasRange)
            resp.Headers["Content-Range"] = $"bytes {start}-{end}/{total}";
        resp.ContentLength = end - start + 1;

        byte[] ivBytes = System.Text.Encoding.ASCII.GetBytes(info.iv);

        using var aes = Aes.Create();
        aes.Key = JavQuickTo.EncKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var ecb = aes.CreateEncryptor();

        byte[] ctr = new byte[16];
        byte[] ks = new byte[16];
        byte[] buf = new byte[65536];

        long pos = start;

        while (pos <= end)
        {
            int part = 0;
            while (part + 1 < info.starts.Count && info.starts[part + 1] <= pos)
                part++;

            long partStart = info.starts[part];
            long partEnd = partStart + info.sizes[part] - 1;
            long segEnd = Math.Min(end, partEnd);
            long localStart = pos - partStart;
            long localEnd = segEnd - partStart;

            using var req = new HttpRequestMessage(HttpMethod.Get, info.urls[part]);
            req.Headers.TryAddWithoutValidation("User-Agent", JavQuickTo.ChromeUA);
            req.Headers.TryAddWithoutValidation("Referer", JavQuickTo.SiteHost + "/");
            req.Headers.TryAddWithoutValidation("Range", $"bytes={localStart}-{localEnd}");

            using var up = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, abort);
            if (!up.IsSuccessStatusCode)
                return false;

            await using var input = await up.Content.ReadAsStreamAsync(abort);

            long off = localStart;
            while (true)
            {
                int n = await input.ReadAsync(buf.AsMemory(0, buf.Length), abort);
                if (n <= 0)
                    break;

                int i = 0;
                while (i < n)
                {
                    long abs = off + i;
                    int inKs = (int)(abs % 16);
                    int take = Math.Min(16 - inKs, n - i);

                    Buffer.BlockCopy(ivBytes, 0, ctr, 0, 16);
                    JavQuickTo.AddCounter(ctr, (abs % JavQuickTo.BlockSize) / 16);
                    ecb.TransformBlock(ctr, 0, 16, ks, 0);

                    for (int j = 0; j < take; j++)
                        buf[i + j] ^= ks[inKs + j];

                    i += take;
                }

                await resp.Body.WriteAsync(buf.AsMemory(0, n), abort);
                off += n;
            }

            pos = segEnd + 1;
        }

        return true;
    }
    #endregion

    #region popup (long-press): the loai / hang / nhan + Video lien quan
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("javquick/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        string pageUrl = JavQuickTo.Abs(JavQuickTo.SiteHost, uri);
        if (!pageUrl.Contains("javquick.com", StringComparison.OrdinalIgnoreCase))
            return Json(new { items = new object[0], groups = new object[0] });

        if (_infoCache.TryGetValue(pageUrl, out var hit)
            && DateTime.UtcNow - hit.at < InfoTtl && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.rel, pageUrl);
        }

        string html = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return Json(new { items = new object[0], groups = new object[0] });

        var chips = JavQuickTo.Chips(html);
        var rel = JavQuickTo.Playlist("javquick/vidosik", html);

        if (chips.Count > 0)
            _infoCache[pageUrl] = (DateTime.UtcNow, chips, rel);

        return InfoResult(chips, rel, pageUrl);
    }

    static string KindLabel(string kind) => kind switch
    {
        "genre" => "Thể loại",
        "maker" => "Hãng",
        "label" => "Nhãn",
        _ => kind
    };

    static int KindRank(string kind) => kind switch
    {
        "genre" => 0,
        "maker" => 1,
        "label" => 2,
        _ => 99
    };

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct().OrderBy(KindRank).ThenBy(x => x))
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/javquick?c={HttpUtility.UrlEncode(kind + "/" + x.slug)}"
            }).ToList();

            if (items.Count == 0) continue;
            groups.Add(new { title = KindLabel(kind), items });
        }

        if (rel != null && rel.Count > 0)
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
                        url = $"{host}/javquick?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? rel : JavQuickTo.SiteHost + "/" + rel.Trim('/');

        if (!url.Contains("javquick.com", StringComparison.OrdinalIgnoreCase))
            return OnError("stream_links");

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"javquick:rel:{url}"), 180, async e =>
        {
            string html = await FetchHtmlAsync(url);
            if (html == null)
                return e.Fail("related", refresh_proxy: true);

            var playlists = JavQuickTo.Playlist("javquick/vidosik", html);

            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        if (cache.Value.playlists == null || cache.Value.playlists.Count == 0)
            return EmptyPlaylistResult(await MenuAsync(null, null));

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null), total_pages: 1);
    }
    #endregion
}
