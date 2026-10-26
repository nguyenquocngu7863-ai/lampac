using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace JAVSnap;

public class JAVSnapController : BaseSisiController
{
    public JAVSnapController() : base(ModInit.conf) { }

    #region list
    [HttpGet, Staticache(manually: true)]
    [Route("javsnap")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        bool isSearch = !string.IsNullOrWhiteSpace(search);
        bool isCat = !string.IsNullOrWhiteSpace(c);

        // Site khong co sort (SortWhitelist rong) => NormalizeSort luon tra null.
        // Van goi de sort la khong bao gio lot vao Uri lan cache key (checklist 9g #5).
        sort = JAVSnapTo.NormalizeSort(sort);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            // v3: doi key vi (a) sua poster (b) trang chu gio gop 2 danh muc de co phan trang
            ipkey($"javsnap:v4:{search}:{c}:{sort}:{pg}"),
            isSearch || isCat ? 10 : 25, async e =>
        {
            if (!isSearch && !isCat)
            {
                // Trang chu /en/ KHONG phan trang (/en/pg-N = 404, /en/?p=2 = trang 1)
                // => gop 2 danh muc lon nhat (censored + uncensored, khong trung nhau)
                // thanh list "moi nhat" co phan trang that: ~60 phim/trang, 247 trang.
                string url1 = JAVSnapTo.Uri(init.host, null, "en/censored", pg);
                string url2 = JAVSnapTo.Uri(init.host, null, "en/uncensored", pg);

                // 2 request site chay SONG SONG + deadline 8s moi cai: truoc day chay tuan tu
                // 12s x2 = 24s => Lampa timeout trang chu ("home bi timeout"). Site chan theo
                // TLS fingerprint tung dot nen phai co tran thoi gian, khong cong don.
                var t1 = FetchHtmlAsync(url1, marker: "data-movie-item", seconds: 8);
                var t2 = FetchHtmlAsync(url2, marker: "data-movie-item", seconds: 8);
                await Task.WhenAll(t1, t2);

                string html1 = t1.Result;
                string html2 = t2.Result;

                var merged = JAVSnapTo.MergeLatest(
                    JAVSnapTo.PlaylistWithId("javsnap/vidosik", html1),
                    JAVSnapTo.PlaylistWithId("javsnap/vidosik", html2));

                // 1 trong 2 danh muc fail (site chan) => van tra list cua danh muc con lai,
                // khong de nguoi dung thay trang loi.
                if (merged.Count == 0)
                    return e.Fail("playlists", refresh_proxy: true);

                int total = Math.Max(JAVSnapTo.Pages(html1), JAVSnapTo.Pages(html2));
                return e.Success((merged, total));
            }

            string url = JAVSnapTo.Uri(init.host, search, c, pg);
            string html = await FetchHtmlAsync(url, marker: "data-movie-item");

            var playlists = JAVSnapTo.Playlist("javsnap/vidosik", html);
            if (playlists == null || playlists.Count == 0)
            {
                // Het trang / khong co ket qua: trang rong, khong phai loi (checklist: khong tra 503).
                if (isSearch || pg > 1 || isCat)
                    return e.Success((new List<PlaylistItem>(), 0));

                return e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success((playlists, JAVSnapTo.Pages(html, isSearch)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        var menu = await menuTask;

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            menu,
            total_pages: cache.Value.total_pages
        );
    }

    // Menu 2 tang (checklist 9g): dong 1 = Tim kiem (head, khong cache);
    // dong 3+ = Danh sach + taxonomy lay dong tu site (cache 12h).
    // Site khong co sort => khong co dong 2.
    // Khi site chan TLS, taxonomy fail lien tuc => khong duoc goi lai 3 request
    // cho MOI request trang chu (moi request mat 8s). Tam nghi 2 phut sau loi.
    static DateTime _taxFailAt = DateTime.MinValue;

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var menu = JAVSnapTo.MenuHead(host, search, sort, c);
        menu.Add(JAVSnapTo.MenuLists(host));

        if (DateTime.UtcNow - _taxFailAt < TimeSpan.FromMinutes(2))
            return menu;

        var cache = await InvokeCacheResult<List<MenuItem>>(
            ipkey("javsnap:menu:v3"),
            720, async e =>
        {
            string gh = JAVSnapTo.SiteHost;

            // 3 trang taxonomy (categories/studios/labels) chay SONG SONG, deadline 8s.
            // Truoc day tuan tu 12s x3 = 36s => menu treo, keo theo trang chu timeout.
            var tg = FetchHtmlAsync(JAVSnapTo.TaxonomyUri(gh, "categories", 1), marker: "category-card", seconds: 8);
            var ts = FetchHtmlAsync(JAVSnapTo.TaxonomyUri(gh, "studios", 1), marker: "category-card", seconds: 8);
            var tl = FetchHtmlAsync(JAVSnapTo.TaxonomyUri(gh, "labels", 1), marker: "category-card", seconds: 8);
            await Task.WhenAll(tg, ts, tl);

            var genres = JAVSnapTo.Taxonomy(tg.Result);
            var studios = JAVSnapTo.Taxonomy(ts.Result);
            var labels = JAVSnapTo.Taxonomy(tl.Result);

            // Thieu 1 nhom la do site chan/lag => khong cache ket qua khuyet,
            // de request sau lay lai du menu.
            if (genres.Count == 0 || studios.Count == 0 || labels.Count == 0)
            {
                _taxFailAt = DateTime.UtcNow;
                return e.Fail("menu");
            }

            var groups = JAVSnapTo.MenuTaxonomy(host, genres, studios, labels);

            if (groups.Count == 0)
            {
                _taxFailAt = DateTime.UtcNow;
                return e.Fail("menu");
            }

            _taxFailAt = DateTime.MinValue;
            return e.Success(groups);
        });

        if (cache.IsSuccess && cache.Value != null)
            menu.AddRange(cache.Value);

        return menu;
    }
    #endregion


    #region vidosik
    [HttpGet, Staticache(manually: true)]
    [Route("javsnap/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var dict = new Dictionary<string, string>();

        foreach (var s in Order(servers, null))
        {
            if (!JAVSnapTo.Supported(s.EmbedUrl))
                continue;

            int kind = JAVSnapTo.Kind(s.EmbedUrl);
            string route = kind == 2 ? "video.mp4" : "video.m3u8";
            if (dict.ContainsKey(s.Label))
                continue;

            dict[s.Label] = $"{host}/javsnap/{route}"
                         + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(s.Label)}";
        }

        if (dict.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(dict);
    }
    #endregion

    #region video
    [HttpGet]
    [Route("javsnap/video")]
    [Route("javsnap/video.m3u8")]
    [Route("javsnap/video.mp4")]
    async public Task<ActionResult> Video(string uri, string srv, string q = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(srv))
            srv = q;

        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Co srv => chi giai dung server nguoi dung bam. Truoc day resolve fail la tu dong
        // phat server khac (chon LLV lai ra VIP) => nguoi dung khong biet dang xem gi.
        var candidates = Order(servers, null).ToList();
        if (!string.IsNullOrEmpty(srv))
        {
            var pick = candidates.Where(x => string.Equals(x.Label, srv, StringComparison.OrdinalIgnoreCase)).ToList();
            if (pick.Count > 0)
                candidates = pick;
        }

        foreach (var s in candidates)
        {
            var res = await ResolveAsync(uri, s);
            if (res == null || string.IsNullOrEmpty(res.Url))
                continue;

            if (res.Kind == 2)
            {
                var mp4Headers = httpHeaders(init, JAVSnapTo.StreamHeaders(res.Url));
                return Redirect(HostStreamProxy(res.Url, mp4Headers));
            }

            var page = await ServeHlsAsync(res.Url);
            if (page != null)
                return page;

            // URL trong cache co the da het han token (token CDN song ~1h nhung hay bi chan som)
            // => giai lai tu dau, khong doc cache.
            res = await ResolveAsync(uri, s, useCache: false);
            if (res != null && res.Kind == 1 && !string.IsNullOrEmpty(res.Url))
            {
                page = await ServeHlsAsync(res.Url);
                if (page != null)
                    return page;
            }
        }

        return OnError("stream_links", refresh_proxy: true);
    }
    #endregion

    #region hls rewriter
    // Manifest con (variant) van co the chua link tuong doi => tu viet lai playlist
    // thay vi tin vao proxy cua Lampac (proxy lay base la URL truoc 302 nen sai duong dan).
    [HttpGet]
    [Route("javsnap/hls")]
    async public Task<ActionResult> Hls(string u)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var page = await ServeHlsAsync(u);
        return page ?? OnError("stream_links", refresh_proxy: true);
    }

    // Segment/key cua CDN phai di bang Http cua Lampac: ProxyAPI (/proxy/) dung HttpClient
    // kem Accept-Encoding: br nen CDN *.tnmr.org tra 403, trong khi Http.GetSpan/BaseGetReader 200.
    [HttpGet]
    [Route("javsnap/seg")]
    async public Task<ActionResult> Seg(string u, string ct = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(u) || !u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return StatusCode(404);

        Response.ContentType = string.IsNullOrEmpty(ct) ? ContentTypeFor(u) : ct;

        bool ok = false;

        try
        {
            await Http.BaseGetReaderAsync(async e =>
            {
                try
                {
                    await e.stream.CopyToAsync(Response.Body);
                    ok = true;
                }
                catch { }
            }, u, timeoutSeconds: 30, headers: JAVSnapTo.StreamHeaders(u), statusCodeOK: false, proxy: proxy);
        }
        catch { }

        if (!ok)
            Response.StatusCode = 404;

        return new EmptyResult();
    }

    static string ContentTypeFor(string url)
    {
        string path = url;
        int i = path.IndexOf('?');
        if (i > 0) path = path.Substring(0, i);
        path = path.ToLowerInvariant();

        if (path.EndsWith(".ts")) return "video/mp2t";
        if (path.EndsWith(".m4s") || path.EndsWith(".mp4")) return "video/mp4";
        if (path.EndsWith(".png")) return "image/png";
        if (path.EndsWith(".jpg") || path.EndsWith(".jpeg")) return "image/jpeg";
        if (path.EndsWith(".webp")) return "image/webp";
        if (path.EndsWith(".vtt")) return "text/vtt";
        if (path.EndsWith(".m3u8")) return "application/vnd.apple.mpegurl";
        return "application/octet-stream";
    }

    async Task<ActionResult> ServeHlsAsync(string url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        string body = await FetchStreamAsync(url);
        if (string.IsNullOrEmpty(body) || !body.Contains("#EXTM3U"))
            return null;

        bool master = body.Contains("#EXT-X-STREAM-INF");
        Uri baseUri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out baseUri))
            return null;

        var sb = new StringBuilder(body.Length + 4096);

        foreach (string raw in body.Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0)
            {
                sb.Append('\n');
                continue;
            }

            if (line[0] == '#')
            {
                sb.Append(Regex.Replace(line, "URI=\"([^\"]+)\"", mm =>
                {
                    string abs = Abs(baseUri, mm.Groups[1].Value);
                    return abs == null ? mm.Value
                         : "URI=\"" + SegUrl(abs, "application/octet-stream") + "\"";
                }));
                sb.Append('\n');
                continue;
            }

            string target = Abs(baseUri, line);
            if (target == null)
            {
                sb.Append(line).Append('\n');
                continue;
            }

            // Manifest con: dong khong phai '#' la media segment => luon la mpegts
            // (site dat ten kieu video0.png nen khong the doan content-type theo duoi file).
            sb.Append(master
                ? $"{host}/javsnap/hls?u={HttpUtility.UrlEncode(target)}"
                : SegUrl(target, "video/mp2t"));
            sb.Append('\n');
        }

        return Content(sb.ToString(), "application/vnd.apple.mpegurl");
    }

    string SegUrl(string url, string ct = null)
        => $"{host}/javsnap/seg?u={HttpUtility.UrlEncode(url)}"
         + (string.IsNullOrEmpty(ct) ? "" : "&ct=" + HttpUtility.UrlEncode(ct));

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

    #region resolve
    sealed class Resolved
    {
        public string Url { get; set; }
        public int Kind { get; set; }
    }

    // Sap xep: server nguoi dung bam -> VIP -> cac server da giai duoc -> con lai
    static IEnumerable<JAVSnapTo.ServerItem> Order(List<JAVSnapTo.ServerItem> servers, string srv)
    {
        var list = new List<JAVSnapTo.ServerItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(srv))
        {
            var pick = servers.FirstOrDefault(x => string.Equals(x.Label, srv, StringComparison.OrdinalIgnoreCase));
            if (pick != null && seen.Add(pick.Label))
                list.Add(pick);
        }

        foreach (var s in servers.Where(x => JAVSnapTo.VipUuid(x.EmbedUrl) != null))
            if (seen.Add(s.Label)) list.Add(s);

        foreach (var s in servers.Where(x => JAVSnapTo.Supported(x.EmbedUrl)))
            if (seen.Add(s.Label)) list.Add(s);

        foreach (var s in servers)
            if (seen.Add(s.Label)) list.Add(s);

        return list;
    }

    async Task<List<JAVSnapTo.ServerItem>> DetailServersAsync(string pageUri)
    {
        if (string.IsNullOrEmpty(pageUri) || !pageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        string key = ipkey($"javsnap:srv:{pageUri}");
        if (hybridCache.TryGetValue(key, out List<JAVSnapTo.ServerItem> cached) && cached != null && cached.Count > 0)
            return cached;

        string html = await FetchHtmlAsync(pageUri, seconds: 12);
        var list = JAVSnapTo.Servers(html);
        if (list.Count > 0)
            hybridCache.Set(key, list, cacheTime(10));

        return list;
    }

    async Task<Resolved> ResolveAsync(string pageUri, JAVSnapTo.ServerItem server, bool useCache = true)
    {
        string cacheKey = ipkey($"javsnap:stream:{pageUri}:{server.Label}");
        if (useCache && hybridCache.TryGetValue(cacheKey, out Resolved hit) && hit != null && !string.IsNullOrEmpty(hit.Url))
            return hit;

        try
        {
            var res = await ResolveDirectAsync(server);
            if (res != null && !string.IsNullOrEmpty(res.Url))
                hybridCache.Set(cacheKey, res, cacheTime(3));

            return res;
        }
        catch
        {
            return null;
        }
    }

    async Task<Resolved> ResolveDirectAsync(JAVSnapTo.ServerItem server)
    {
        #region VIP (player cua site: /api/playlist-token -> /v/<token> -> CDN)
        string uuid = JAVSnapTo.VipUuid(server.EmbedUrl);
        if (uuid != null)
        {
            string tokenUrl = JAVSnapTo.PlayerHost + "/api/playlist-token/" + uuid;
            string json = await FetchTextAsync(tokenUrl, HeadersModel.Init(
                ("User-Agent", JAVSnapTo.ChromeUA),
                ("Accept", "application/json"),
                ("Referer", JAVSnapTo.PlayerHost + "/")));

            string t = JAVSnapTo.VipToken(json);
            if (string.IsNullOrEmpty(t))
                return null;

            string vurl = JAVSnapTo.PlayerHost + "/v/" + t;
            string loc = await Http.GetLocation(vurl,
                referer: JAVSnapTo.PlayerHost + "/",
                timeoutSeconds: 12,
                headers: JAVSnapTo.StreamHeaders(vurl),
                proxy: proxy);

            if (string.IsNullOrEmpty(loc) || loc.Contains("/error/", StringComparison.OrdinalIgnoreCase))
                return null;

            return new Resolved() { Url = loc, Kind = 1 };
        }
        #endregion

        #region luluvid (P.A.C.K.E.R -> jwplayer sources[].file = master.m3u8)
        if (server.EmbedUrl.Contains("luluvid.", StringComparison.OrdinalIgnoreCase))
        {
            string html = await FetchHtmlAsync(server.EmbedUrl, seconds: 15,
                referer: "https://luluvid.xyz/");
            string js = JAVSnapTo.UnpackPacker(html);
            string m3u8 = JAVSnapTo.ExtractM3u8(js);
            if (string.IsNullOrEmpty(m3u8))
                return null;

            return new Resolved() { Url = m3u8, Kind = 1 };
        }
        #endregion

        #region streamtape (div#robotlink token -> /get_video 302 -> CDN mp4)
        if (JAVSnapTo.IsStreamtape(server.EmbedUrl))
        {
            string html = await FetchHtmlAsync(server.EmbedUrl, seconds: 12);
            string api = JAVSnapTo.StreamtapeApi(html);
            if (string.IsNullOrEmpty(api))
                return null;

            string cdn = await Http.GetLocation(api,
                referer: server.EmbedUrl,
                timeoutSeconds: 12,
                headers: JAVSnapTo.StreamHeaders(server.EmbedUrl),
                proxy: proxy);

            if (string.IsNullOrEmpty(cdn) || !cdn.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return null;

            return new Resolved() { Url = cdn, Kind = 2 };
        }
        #endregion

        #region doodstream family (playmogo): pass_md5/<hash>/<token> -> base + rnd + ?token=
        if (JAVSnapTo.Kind(server.EmbedUrl) == 2)
        {
            string html = await FetchHtmlAsync(server.EmbedUrl, seconds: 15);
            string passPath = JAVSnapTo.DoodPassPath(html);
            string token = JAVSnapTo.DoodToken(passPath);
            if (string.IsNullOrEmpty(passPath) || string.IsNullOrEmpty(token))
                return null;

            string passUrl = "https://" + new Uri(server.EmbedUrl).Host + "/pass_md5/" + passPath;
            string body = await FetchTextAsync(passUrl, HeadersModel.Init(
                ("User-Agent", JAVSnapTo.ChromeUA),
                ("Referer", server.EmbedUrl),
                ("Accept", "*/*")));

            string mp4 = JAVSnapTo.DoodStream(body, token);
            if (string.IsNullOrEmpty(mp4))
                return null;

            return new Resolved() { Url = mp4, Kind = 2 };
        }
        #endregion

        return null;
    }
    #endregion



    #region http
    async Task<string> FetchHtmlAsync(string url, int seconds = 12, string referer = null, string marker = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        int attempt = 0;

        while (DateTime.UtcNow < deadline)
        {
            if (attempt++ > 0)
                await Task.Delay(300);

            var headers = HeadersModel.Init(
                ("User-Agent", JAVSnapTo.ChromeUA),
                ("Referer", referer ?? (JAVSnapTo.SiteHost + "/")));

            string html = await FetchTextAsync(url, headers);
            if (string.IsNullOrEmpty(html))
                continue;

            // Site thinh thoang tra trang chan (200 nhung khong co noi dung that):
            // yeu cau marker de retry thay vi parse ra list rong.
            if (marker != null && !html.Contains(marker, StringComparison.OrdinalIgnoreCase))
                continue;

            return html;
        }

        return null;
    }

    async Task<string> FetchTextAsync(string url, IReadOnlyList<HeadersModel> headers)
    {
        if (string.IsNullOrEmpty(url)) return null;

        try
        {
            // statusCodeOK:false de van doc duoc body khi site tra trang chan,
            // roi FetchHtmlAsync tu retry theo marker.
            return await httpHydra.Get(url, addheaders: headers, statusCodeOK: false);
        }
        catch
        {
            return null;
        }
    }

    // Manifest/segment cua CDN (vd *.tnmr.org cua luluvid) tra 503 khi di qua httpHydra
    // (bi ghep them baseHeaders cua controller) nhung 200 khi goi Http thang voi headers rieng.
    async Task<string> FetchStreamAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        try
        {
            string result = null;
            // Manifest: chi UA + Referer + Accept. Origin/Sec-Fetch-* lam CDN *.tnmr.org tra 403.
            var manifestHeaders = HeadersModel.Init(
                ("User-Agent", JAVSnapTo.ChromeUA),
                ("Referer", JAVSnapTo.RefererFor(url)),
                ("Accept", "*/*"));

            await Http.BaseGetReaderAsync(async e =>
            {
                using var reader = new System.IO.StreamReader(e.stream, Encoding.UTF8);
                result = await reader.ReadToEndAsync();
            }, url, timeoutSeconds: 12, headers: manifestHeaders, statusCodeOK: false, proxy: proxy);

            return result;
        }
        catch
        {
            return null;
        }
    }
    #endregion
}
