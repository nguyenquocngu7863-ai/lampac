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
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace TellyHD;

public class TellyHDController : BaseSisiController
{
    public TellyHDController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("tellyhd")]
    async public Task<ActionResult> Index(
        string search, string c, int pg = 1, string q = null, string rel = null)
    {
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        return await Core(search ?? q, c, pg);
    }

    // Duong dan thay cho query:
    //   /tellyhd/p/genre/jav        /tellyhd/p/genre/jav/2
    //   /tellyhd/p/search/<q>       /tellyhd/p/2
    [HttpGet, Staticache(manually: true)]
    [Route("tellyhd/p/{**rest}")]
    async public Task<ActionResult> Path(string rest, int pg = 1)
    {
        if (pg < 1)
            pg = 1;

        string search = null, c = null;
        string[] seg = (rest ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (seg.Length > 0 && seg[seg.Length - 1].All(char.IsDigit)
            && int.TryParse(seg[seg.Length - 1], out int dp) && dp > 1)
        {
            pg = dp;
            seg = seg.Take(seg.Length - 1).ToArray();
        }

        if (seg.Length >= 2 && seg[0].Equals("search", StringComparison.OrdinalIgnoreCase))
        {
            search = string.Join("/", seg.Skip(1));
        }
        else if (seg.Length >= 2 && HdTo.IsTaxonomy(seg[0] + "/" + seg[1]))
        {
            c = string.Join("/", seg);
        }
        else if (seg.Length >= 1)
        {
            c = seg[0];
        }

        return await Core(search, c, pg);
    }

    async Task<ActionResult> Core(string search, string c, int pg)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // SISI khong bieu dien duoc trang rong: search khong phan trang nen
        // pg>1 tra ve list rong + total_pages=1 thay vi 503.
        string pageUrl = HdTo.Uri(init.host, search, c, pg);

        if (string.IsNullOrEmpty(pageUrl))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult(
            ipkey($"tellyhd:{search}:{c}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string url = pageUrl;

            // search trang 2: site khong phan trang -> list rong, khong phai loi.
            if (string.IsNullOrEmpty(url))
                return e.Success(new List<PlaylistItem>());

            string html = await GetPageAsync(url);

            // Trang vuot qua trang cuoi: site tra 404/rong -> coi la het
            // danh sach, tra list rong thay vi 503.
            if (string.IsNullOrEmpty(html) && pg > 1)
                return e.Success(new List<PlaylistItem>());

            var playlists = string.IsNullOrEmpty(html)
                ? null
                : (!string.IsNullOrWhiteSpace(search)
                    ? HdTo.Search(html)
                    : HdTo.Playlist(html, host));

            if (playlists != null && playlists.Count == 0)
                return e.Success(playlists);

            if (playlists == null && !string.IsNullOrWhiteSpace(search))
                return e.Success(new List<PlaylistItem>());

            if (playlists == null)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        // Trang vuot qua trang cuoi: tra list rong thay vi 503.
        if (cache.IsSuccess && (cache.Value == null || cache.Value.Count == 0))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        return PlaylistResult(cache, await MenuAsync());
    }

    // App cache nguyen phien -> menu phai day du ngay lan dau.
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 9_000;

    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("tellyhd:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        if (!await menuLock.WaitAsync(3000))
            return Fallback(hostLocal);

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) &&
                hit2 != null && hit2.Count > 0)
                return hit2;

            var build = BuildMenuAsync(hostLocal, memKey);

            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return Fallback(hostLocal);

            return await build;
        }
        finally
        {
            menuLock.Release();
        }
    }

    List<MenuItem> Fallback(string hostLocal)
        => HdTo.Menu(hostLocal, HdTo.FallbackGenres, HdTo.FallbackYears, HdTo.FallbackQualities);

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        var genres = HdTo.FallbackGenres;
        var years = HdTo.FallbackYears;
        var qualities = HdTo.FallbackQualities;

        try
        {
            var tHome = GetPageAsync(HdTo.SiteHost + "/");
            var tYear = GetPageAsync(HdTo.SiteHost + "/dtyear-sitemap.xml");
            var tQual = GetPageAsync(HdTo.SiteHost + "/dtquality-sitemap.xml");

            await Task.WhenAll(tHome, tYear, tQual);

            var g = HdTo.Genres(tHome.Result);
            if (g.Count > 0)
                genres = g;

            var y = HdTo.FromSitemap(tYear.Result, "release", true, 40);
            if (y.Count > 0)
                years = y;

            var qq = HdTo.FromSitemap(tQual.Result, "quality", false, 31);
            if (qq.Count > 0)
                qualities = qq;
        }
        catch { }

        var menu = HdTo.Menu(hostLocal, genres, years, qualities);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    async Task<string> GetPageAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var headers = HeadersModel.Init(
            ("User-Agent", HdTo.ChromeUA),
            ("Accept-Language", "en-US,en;q=0.9"),
            ("Referer", HdTo.SiteHost + "/"));

        for (int i = 0; i < 3; i++)
        {
            try
            {
                string page = await Http.Get(
                    url,
                    timeoutSeconds: 12,
                    httpversion: init.httpversion,
                    proxy: proxy,
                    headers: headers);

                if (!string.IsNullOrEmpty(page) && page.Length > 1000)
                    return page;
            }
            catch { }

            await Task.Delay(600);
        }

        return null;
    }

    // ================= stream =================

    sealed class Streams
    {
        public Dictionary<string, string> links = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> referers = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> kinds = new(StringComparer.OrdinalIgnoreCase);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("tellyhd/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // KHONG resolve stream o day: app goi /vidosik truoc khi mo player,
        // resolve 2-3 server (embed + token) mat 10-20s -> app timeout, popup
        // khong bao gio mo. Chi tra danh sach server (1 request trang detail,
        // cache 6h), server nao bam moi resolve trong /video.
        string pageUrl = HdTo.PageUrl(uri);
        if (pageUrl == null)
            return OnError("stream_links", refresh_proxy: true);

        string html = await PageCached(pageUrl);
        var servers = HdTo.Servers(html ?? "");
        if (servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var s in servers)
        {
            // LU (tnmr.org) CHET site-wide (2026-11-18): user test thuc te -> den
            // man; chinh JavGuru cung da go LU khoi trang video. Module van giu
            // duong resolve+redirect (LuluMaster) de bat lai sau; chi can bo
            // dong loc nay neu CDN song lai.
            if (HdTo.IsLulu(s.name))
                continue;

            string key = s.name;
            if (dict.ContainsKey(key))
                continue;

            // Lampa bat hls.js theo duoi `.m3u8`; nguon mp4 (DoodStream) phai
            // di qua `video.mp4` neu khong app bao "no EXTM3U delimiter".
            string ext = HdTo.IsMp4Server(s.name) ? ".mp4" : ".m3u8";

            dict[key] = $"{host}/tellyhd/video{ext}"
                + $"?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(key)}";
        }

        // Khoi "lien quan" cua trang detail (da tai o tren) -> playlist "tiep theo"
        var recomends = HdTo.Related(html ?? "");
        recomends.RemoveAll(x => SamePage(x, pageUrl));

        if (related)
            return PlaylistResult(recomends, false, null, total_pages: 1);

        return Json(new { qualitys = dict, recomends });
    }

    // Cache link da resolve theo (trang, server) — player co the goi lai URL
    // ngay sau khi mo (hls.js probe), khong duoc resolve lai tu dau.
    async Task<(string url, string referer, string kind)> ResolveServerAsync(string pageUrl, string q)
    {
        string memKey = ipkey($"tellyhd:link:{pageUrl}|{q}");

        if (hybridCache.TryGetValue(memKey, out Streams hit) &&
            hit != null && hit.links.TryGetValue(q, out string cached) &&
            !string.IsNullOrEmpty(cached))
        {
            hit.referers.TryGetValue(q, out string r);
            hit.kinds.TryGetValue(q, out string k);
            return (cached, r, k);
        }

        string html = await PageCached(pageUrl);
        var server = HdTo.Servers(html ?? "")
            .FirstOrDefault(s => string.Equals(s.name, q, StringComparison.OrdinalIgnoreCase));

        if (server == null)
            return (null, null, null);

        string embed = await EmbedAsync(server, pageUrl);
        if (string.IsNullOrEmpty(embed))
            return (null, null, null);

        var (url, referer, kind) = await ResolveEmbedAsync(embed);
        if (string.IsNullOrEmpty(url))
            return (null, null, null);

        // tok cua telly.xin song ~3h, token Dood song ~1h -> cache ngan.
        var res = new Streams();
        res.links[q] = url;
        res.referers[q] = referer;
        res.kinds[q] = kind;
        hybridCache.Set(memKey, res, cacheTime(5));

        return (url, referer, kind);
    }

    // POST /wp-admin/admin-ajax.php  action=doo_player_ajax
    async Task<string> EmbedAsync(HdTo.Server server, string pageUrl)
    {
        try
        {
            var headers = HeadersModel.Init(
                ("User-Agent", HdTo.ChromeUA),
                ("Referer", pageUrl),
                ("X-Requested-With", "XMLHttpRequest"),
                ("Accept", "application/json, text/javascript, */*; q=0.01"));

            string body = "action=doo_player_ajax"
                + "&post=" + HttpUtility.UrlEncode(server.post)
                + "&nume=" + HttpUtility.UrlEncode(server.nume)
                + "&type=" + HttpUtility.UrlEncode(server.type);

            string json = await HydraPost(HdTo.SiteHost + "/wp-admin/admin-ajax.php", body, headers);
            return HdTo.EmbedUrl(json);
        }
        catch
        {
            return null;
        }
    }

    async Task<(string url, string referer, string kind)> ResolveEmbedAsync(string embed)
    {
        try
        {
            if (!Uri.TryCreate(embed, UriKind.Absolute, out var eu))
                return (null, null, null);

            string embedOrigin = eu.Scheme + "://" + eu.Authority;

            // DoodStream can cookie dat tu trang embed cho /pass_md5 -> dung 1 container.
            var cookies = new CookieContainer();

            // Token cua cac CDN (tnmr.org) gan chat voi header cua request embed:
            // fetch embed bang header toi thieu -> token sinh ra chi hop voi
            // header do, mang di phat lai 403. Phai dung full browser header.
            var headers = BrowserHeaders(HdTo.SiteHost + "/", "https://tellyhd.at");

            string html = await HydraGet(embed, headers, cookies);

            if (string.IsNullOrEmpty(html))
                return (null, null, null);

            // Vidara (server du phong cua nhanh JAV): `/e/<code>` -> 302 ->
            // `https://<host>/e/<filecode>` -> POST /api/stream -> streaming_url.
            if (embed.IndexOf("vidara", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var (vurl, vref) = await ResolveVidaraAsync(embed);
                if (!string.IsNullOrEmpty(vurl))
                    return (vurl, vref, "m3u8");
            }

            // telly.xin/play?v= : data-stream-url /playlist/...?tok=JWT
            string stream = HdTo.StreamAttr(html);
            if (!string.IsNullOrEmpty(stream))
            {
                if (stream.StartsWith("/"))
                    stream = embedOrigin + stream;

                string pl = await HydraGet(stream, headers, cookies);

                if (!string.IsNullOrEmpty(pl) && pl.Contains("#EXTM3U"))
                    return (stream, embedOrigin + "/", "m3u8");
            }

            // DoodStream: /pass_md5/... -> base + 10 ky tu + token
            string pass = HdTo.DoodPass(html);
            if (!string.IsNullOrEmpty(pass))
            {
                string token = pass.TrimEnd('/').Split('/').Last();

                string baseUrl = await HydraGet(
                    embedOrigin + pass,
                    BrowserHeaders(embed, embedOrigin, xhr: true),
                    cookies);

                if (!string.IsNullOrEmpty(baseUrl) && baseUrl.StartsWith("http"))
                {
                    string rand = RandStr(10);
                    string mp4 = baseUrl.Trim() + rand
                        + "?token=" + token
                        + "&expiry=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    return (mp4, embed, "mp4");
                }
            }

            // Gateway (cdntelly.sbs) + LuluStream (tnmr.org): trang embed la
            // P.A.C.K.E.R, `file:` trong jwplayer setup chinh la master.m3u8
            // (token nam trong query, GIU NGUYEN). CDN tra 403 cho /proxy/ cua
            // Lampac va cho httpHydra nhung 200 voi stack Http cua module
            // -> KHONG redirect ra CDN, relay playlist/segment trong module.
            string master = HdTo.LuluMaster(html);
            if (!string.IsNullOrEmpty(master))
                return (master, embed, "m3u8");

            return (null, null, null);
        }
        catch
        {
            return (null, null, null);
        }
    }

    // Vidara: API tra master HLS (streaming_url), Referer phai la embedding
    // cua chinh no (`<host>/e/<filecode>`) chu khong phai trang phim.
    async Task<(string url, string referer)> ResolveVidaraAsync(string embed)
    {
        try
        {
            string loc = await Http.GetLocation(
                embed,
                timeoutSeconds: 15,
                headers: HeadersModel.Init(
                    ("User-Agent", HdTo.ChromeUA),
                    ("Referer", HdTo.SiteHost + "/")),
                httpversion: init.httpversion);

            var (apiHost, code) = HdTo.VidaraTarget(loc ?? "");
            if (string.IsNullOrEmpty(apiHost) || string.IsNullOrEmpty(code))
                return (null, null);

            string emb = apiHost + "/e/" + code;

            string json;
            using (var content = new System.Net.Http.StringContent(
                "{\"filecode\":\"" + code + "\",\"device\":\"web\"}",
                System.Text.Encoding.UTF8, "application/json"))
            {
                json = await Http.Post(
                    apiHost + "/api/stream",
                    content,
                    timeoutSeconds: 15,
                    headers: HeadersModel.Init(
                        ("User-Agent", HdTo.ChromeUA),
                        ("Referer", emb),
                        ("Origin", apiHost)),
                    httpversion: init.httpversion,
                    disposeData: true);
            }

            string master = HdTo.VidaraStream(json ?? "");
            return string.IsNullOrEmpty(master) ? (null, null) : (master, emb);
        }
        catch
        {
            return (null, null);
        }
    }

    static List<HeadersModel> BrowserHeaders(string referer, string origin, bool xhr = false)
    {
        var h = HeadersModel.Init(
            ("User-Agent", HdTo.ChromeUA),
            ("Referer", referer),
            ("Origin", origin),
            ("Accept", xhr ? "*/*" : "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"),
            ("Accept-Language", HdTo.AcceptLanguage),
            ("Sec-Fetch-Dest", xhr ? "empty" : "iframe"),
            ("Sec-Fetch-Mode", xhr ? "cors" : "navigate"),
            ("Sec-Fetch-Site", "cross-site"),
            ("sec-ch-ua", HdTo.SecChUa),
            ("sec-ch-ua-mobile", "?0"),
            ("sec-ch-ua-platform", "\"Windows\"")
        );

        if (xhr)
            h.Add(new HeadersModel("X-Requested-With", "XMLHttpRequest"));

        return h;
    }

    static string RandStr(int n)
    {
        const string t = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < n; i++)
            sb.Append(t[Random.Shared.Next(t.Length)]);

        return sb.ToString();
    }

    // Embed cua playmogo/cdntelly chan TLS fingerprint cua HttpClient thuong
    // (Http.Get -> null, trong khi curl_cffi 200) -> di qua httpHydra truoc.
    async Task<string> HydraGet(string url, IReadOnlyList<HeadersModel> headers, CookieContainer cookies = null)
    {
        // playmogo/cdntelly: HTTP/1.1 tra 200, HTTP/2 (init.httpversion=2) tra rong
        // -> thu 1 truoc, roi moi den httpversion cua module.
        foreach (int ver in new[] { 1, init.httpversion })
        {
            try
            {
                string res = await Http.Get(
                    url,
                    timeoutSeconds: 12,
                    httpversion: ver,
                    proxy: proxy,
                    headers: headers,
                    cookieContainer: cookies);

                if (!string.IsNullOrEmpty(res))
                    return res;
            }
            catch { }
        }

        try
        {
            return await httpHydra.Get(url, addheaders: headers, cookieContainer: cookies);
        }
        catch
        {
            return null;
        }
    }

    async Task<string> HydraPost(string url, string data, IReadOnlyList<HeadersModel> headers)
    {
        try
        {
            string res = await httpHydra.Post(url, data, addheaders: headers);
            if (!string.IsNullOrEmpty(res))
                return res;
        }
        catch { }

        try
        {
            return await Http.Post(
                url,
                data,
                headers: headers,
                timeoutSeconds: 12,
                httpversion: init.httpversion,
                proxy: proxy);
        }
        catch
        {
            return null;
        }
    }

    [HttpGet]
    [Route("tellyhd/video.m3u8")]
    [Route("tellyhd/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = HdTo.PageUrl(uri);
        if (pageUrl == null || string.IsNullOrEmpty(q))
            return OnError("stream_links", refresh_proxy: true);

        var (link, referer, kind) = await ResolveServerAsync(pageUrl, q);
        if (string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        if (string.IsNullOrEmpty(referer))
            referer = HdTo.SiteHost + "/";

        // HLS: tu relay playlist de URL con `.ts` that (nguon telly.xin tra
        // segment duoi dang `.png`: ffmpeg va mot so player tu choi vi duoi).
        // mp4 (DoodStream) van day qua /proxy/ nhu cu.
        // tnmr.org (LuluStream) chan TLS fingerprint: HttpClient cua .NET bi 403
        // voi MOI bo header (da do 12 to hop: ua/ua+ref/ua+acc/full x http1/http2
        // = 403 het), trong khi Chrome/curl_cffi 200. Khong the relay trong module
        // -> tra thang master cho PLAYER tu tai bang TLS cua no (Chrome/ExoPlayer
        // co the lot, co the khong -> server nay "hen xui" dung nhu ten).
        if (HdTo.IsLulu(link))
            return Redirect(link);

        if (kind == "m3u8" || link.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string pl = await HydraGet(link, BrowserHeaders(referer, HdTo.SiteHost + "/"));
            if (string.IsNullOrEmpty(pl) || !pl.Contains("#EXTM3U"))
                return OnError("stream_links", refresh_proxy: true);

            return Content(HdTo.RewriteHls(pl, link, host, referer), "application/vnd.apple.mpegurl");
        }

        string origin = referer;
        if (Uri.TryCreate(referer, UriKind.Absolute, out var ru))
            origin = ru.Scheme + "://" + ru.Authority;

        // CDN cua LuluStream (tnmr.org) doi header giong browser: thieu
        // Sec-Fetch-*/Accept la 403 (da do: urllib thieu -> 403, du -> 200).
        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", HdTo.ChromeUA),
            ("Referer", referer),
            ("Origin", origin),
            ("Accept", "*/*"),
            ("Accept-Language", HdTo.AcceptLanguage),
            ("Sec-Fetch-Dest", "empty"),
            ("Sec-Fetch-Mode", "cors"),
            ("Sec-Fetch-Site", "cross-site"),
            ("sec-ch-ua", HdTo.SecChUa),
            ("sec-ch-ua-mobile", "?0"),
            ("sec-ch-ua-platform", "\"Windows\"")
        ));

        return Redirect(HostStreamProxy(link, direct));
    }

    // ================= HLS relay =================
    // Playlist/variant/segment deu di qua day: URL con duoi chuan (.m3u8/.ts)
    // de player khong tu choi, va khong phu thuoc /proxy/ (nguon telly.xin
    // tra segment duoi `.png` + content-type image/png).

    [HttpGet]
    [Route("tellyhd/hls.m3u8")]
    async public Task<ActionResult> HlsPlaylist(string u, string r)
    {
        string up = HdTo.FromB64(u);
        if (!Uri.TryCreate(up, UriKind.Absolute, out _))
            return OnError("stream_links", refresh_proxy: true);

        string referer = HdTo.FromB64(r);
        if (string.IsNullOrEmpty(referer))
            referer = HdTo.SiteHost + "/";

        string pl = await HydraGet(up, BrowserHeaders(referer, HdTo.SiteHost + "/"));
        if (string.IsNullOrEmpty(pl) || !pl.Contains("#EXTM3U"))
            return OnError("stream_links", refresh_proxy: true);

        return Content(HdTo.RewriteHls(pl, up, host, referer), "application/vnd.apple.mpegurl");
    }

    [HttpGet]
    [Route("tellyhd/hls.ts")]
    async public Task<ActionResult> HlsSegment(string u, string r)
    {
        string up = HdTo.FromB64(u);
        if (!Uri.TryCreate(up, UriKind.Absolute, out _))
            return OnError("stream_links");

        string referer = HdTo.FromB64(r);
        if (string.IsNullOrEmpty(referer))
            referer = HdTo.SiteHost + "/";

        var headers = HeadersModel.Init(
            ("User-Agent", HdTo.ChromeUA),
            ("Referer", referer),
            ("Accept", "*/*"));

        byte[] bytes = await Http.Download(up, timeoutSeconds: 20, headers: headers, proxy: proxy);

        // tnmr.org: UA Windows co the 403 -> thu them UA Android truoc khi bao loi.
        if ((bytes == null || bytes.Length == 0) && HdTo.IsLulu(up))
        {
            headers = HeadersModel.Init(
                ("User-Agent", HdTo.AndroidUA),
                ("Referer", referer),
                ("Accept", "*/*"));

            bytes = await Http.Download(up, timeoutSeconds: 20, headers: headers, proxy: proxy);
        }

        if (bytes == null || bytes.Length == 0)
            return OnError("stream_links", refresh_proxy: true);

        return File(bytes, "video/mp2t");
    }

    [HttpGet]
    [Route("tellyhd/hls.vtt")]
    async public Task<ActionResult> HlsSubtitle(string u, string r)
    {
        string up = HdTo.FromB64(u);
        if (!Uri.TryCreate(up, UriKind.Absolute, out _))
            return OnError("stream_links");

        string referer = HdTo.FromB64(r);
        if (string.IsNullOrEmpty(referer))
            referer = HdTo.SiteHost + "/";

        string body = await HydraGet(up, BrowserHeaders(referer, HdTo.SiteHost + "/"));
        if (string.IsNullOrEmpty(body))
            return OnError("stream_links");

        return Content(body, "text/vtt");
    }

    // ================= popup long-press =================

    static readonly Dictionary<string, (DateTime at, string html)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("tellyhd/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        string pageUrl = HdTo.PageUrl(uri);

        // uri rac / host la -> popup rong, KHONG 500.
        if (pageUrl == null)
            return Json(new { items = new object[0], groups = new object[0] });

        string html = await PageCached(pageUrl);

        var chips = HdTo.Chips(html ?? "");
        var groups = new List<object>();

        if (chips.Count > 0)
        {
            groups.Add(new
            {
                title = "Thể loại",
                items = chips.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/tellyhd?c=" + HttpUtility.UrlEncode("genre/" + x.slug)
                }).ToList()
            });
        }

        if (HdTo.Related(html ?? "").Count > 0)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new
                    {
                        title = "Phim tương tự",
                        plain = true,
                        url = $"{host}/tellyhd?rel=" + HttpUtility.UrlEncode(pageUrl)
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<string> PageCached(string pageUrl)
    {
        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(pageUrl, out var hit)
                && DateTime.UtcNow - hit.at < InfoTtl && hit.html != null)
                return hit.html;
        }

        string html = await GetPageAsync(pageUrl) ?? "";

        lock (_infoCache)
            _infoCache[pageUrl] = (DateTime.UtcNow, html);

        return html;
    }

    // Site chi co 1 khoi "Similar titles" (~10 phim) -> total_pages = 1
    // (0 = "con trang vo han" voi SISI).
    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = HdTo.PageUrl(rel);

        if (pageUrl == null)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string html = await PageCached(pageUrl);
        var items = HdTo.Related(html);

        items.RemoveAll(x => SamePage(x, pageUrl));

        if (items.Count == 0)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        return PlaylistResult(items, true, await MenuAsync(), total_pages: 1);
    }

    static bool SamePage(PlaylistItem x, string pageUrl)
    {
        string href = x?.bookmark?.href;
        if (string.IsNullOrEmpty(href))
            return false;

        string u = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? href
            : HdTo.SiteHost + "/" + href.Trim('/');

        return string.Equals(u.TrimEnd('/'), pageUrl.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
