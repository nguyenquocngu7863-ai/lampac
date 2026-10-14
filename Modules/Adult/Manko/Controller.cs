using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Manko;

public class MankoController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10000;
    public MankoController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("manko")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        var menuTask = MenuAsync(search);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"manko:v1:{search}:{c}:{pg}"), 10, async e =>
        {
            string body = await MankoTo.GetApiAsync(MankoTo.Uri(init.host, search, c, pg), proxy, init.httpversion);
            var (items, total) = MankoTo.ParseList(body ?? "");
            var playlists = MankoTo.Playlist("manko/vidosik", items);
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, MankoTo.TotalPages(total)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await menuTask,
            total_pages: cache.Value.total_pages
        );
    }

    async Task<List<MenuItem>> MenuAsync(string search)
    {
        var menu = MankoTo.MenuHead(host, search);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("manko:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return MankoTo.Menu(hostLocal, null, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return MankoTo.Menu(hostLocal, null, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        try
        {
            var gTask = MankoTo.GetApiAsync(MankoTo.ApiHost + "/common/list-genre", proxy, init.httpversion);
            var cTask = MankoTo.GetApiAsync(MankoTo.ApiHost + "/common/country-list", proxy, init.httpversion);
            await Task.WhenAll(gTask, cTask);
            var genres = MankoTo.ParsePlainArray(await gTask ?? "");
            var countries = MankoTo.ParsePlainArray(await cTask ?? "");
            var menu = MankoTo.Menu(hostLocal, genres, countries);
            if (genres.Count > 0 || countries.Count > 0)
                hybridCache.Set(memKey, menu, cacheTime(720), true);
            Console.WriteLine($"Manko: menu genres={genres.Count} countries={countries.Count}");
            return menu;
        }
        catch { }
        return MankoTo.Menu(hostLocal, null, null);
    }

    // ========== PLAYER (lazy-resolve, chuan SupJav §11c2) ==========
    // Detail API -> server1 wasabi_embed_url (/e/<hash> + /stream API),
    // server2 upload18_player[0] (PLAYER_CONFIG m3u8 helvid, IP-bound + cookie).
    async Task<(JsonElement detail, List<(string label, string kind, string url)> servers)> DetailAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return (default, null);
        string apiUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : MankoTo.ApiHost + "/swx/movie/detail/" + uri.Trim('/');
        string memKey = ipkey($"manko:detail:{apiUrl}");
        if (hybridCache.TryGetValue(memKey, out (JsonElement detail, List<(string label, string kind, string url)> servers) cached)
            && cached.servers != null && cached.servers.Count > 0)
            return cached;
        string body = await MankoTo.GetApiAsync(apiUrl, proxy, init.httpversion);
        var d = MankoTo.ParseDetail(body ?? "");
        if (d == null) return (default, null);
        var servers = MankoTo.DetailServers(d.Value);
        if (servers.Count == 0) return (default, null);
        var res = (d.Value, servers);
        hybridCache.Set(memKey, res, cacheTime(15));
        return res;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("manko/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        var (_, servers) = await DetailAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, _, _) in servers)
        {
            if (dict.ContainsKey(label))
                continue;
            dict[label] =
                $"{host}/manko/video.m3u8"
              + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(label)}";
        }
        return Json(dict);
    }

    async Task<string> ResolveOneAsync(string pageUrl, (string label, string kind, string url) server)
    {
        if (server.kind == "wasabi")
        {
            string hash = MankoTo.WasabiHashId(server.url);
            string api = MankoTo.WasabiStreamUrl(server.url, hash);
            if (string.IsNullOrEmpty(api)) return null;
            string body = await MankoTo.GetHtmlAsync(api, server.url, 15, proxy, init.httpversion);
            if (string.IsNullOrEmpty(body))
                body = await MankoTo.GetHtmlAsync(api, server.url, 15, null, init.httpversion);
            string stream = MankoTo.ParseWasabiStream(body ?? "");
            if (string.IsNullOrEmpty(stream)) return null;
            // Master wasabi chi co 1 variant: /hls.m3u8 tu phang luc play
            // (khong cache hybridCache, stateless qua m/r trong URL).
            string self = host + "/manko/hls.m3u8?uri=" + HttpUtility.UrlEncode(pageUrl)
                + "&srv=" + HttpUtility.UrlEncode(server.label)
                + "&m=" + HttpUtility.UrlEncode(stream)
                + "&r=" + HttpUtility.UrlEncode(server.url);
            return self + "\n" + server.url;
        }
        if (server.kind == "upload18")
        {
            var hel = await ResolveHelvidAsync(pageUrl, server.url);
            if (hel.m3u8 == null) return null;
            // packed = "m3u8\nreferer[\ncookie]": cookie co thi kem, khong thi thoi.
            string packed = hel.m3u8 + "\n" + hel.referer;
            if (!string.IsNullOrEmpty(hel.cookie))
                packed += "\n" + hel.cookie;
            return packed;
        }
        return null;
    }

    // Server2 chung minh that 12/10: chi Http.UserAgent moi 200 (F18).
    async Task<(string m3u8, string cookie, string referer)> ResolveHelvidAsync(
        string pageUrl, string playUrl)
    {
        try
        {
            string playHtml = await MankoTo.GetHtmlAsync(playUrl, pageUrl, 15, proxy, init.httpversion, MankoTo.HelvidUA);
            if (string.IsNullOrEmpty(playHtml))
                return (null, null, null);
            string m3u8 = MankoTo.ExtractPlayerConfigM3u8(playHtml);
            if (string.IsNullOrEmpty(m3u8) || !m3u8.Contains("helvid"))
                return (null, null, null);
            string vkey = MankoTo.ExtractVideoKey(playHtml);
            if (string.IsNullOrEmpty(vkey))
                return (null, null, null);
            string u18 = "https://upload18.org/play/index/" + vkey;

            string master = await MankoTo.GetHtmlAsync(m3u8, u18, 15, proxy, init.httpversion, MankoTo.HelvidUA);
            if (string.IsNullOrEmpty(master))
                master = await MankoTo.GetHtmlAsync(m3u8, u18, 15, null, init.httpversion, MankoTo.HelvidUA);
            if (string.IsNullOrEmpty(master) || !master.Contains("#EXTM3U"))
                return (null, null, null);

            string cookie = null;
            try
            {
                var jar = new System.Net.CookieContainer();
                var handler = new System.Net.Http.HttpClientHandler { CookieContainer = jar };
                if (proxy != null) { handler.Proxy = proxy; handler.UseProxy = true; }
                using var client = new System.Net.Http.HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", MankoTo.HelvidUA);
                using (var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, u18))
                {
                    req.Headers.TryAddWithoutValidation("Referer", playUrl);
                    using var resp = await client.SendAsync(req);
                    await resp.Content.ReadAsStringAsync();
                }
                var parts = new List<string>();
                foreach (System.Net.Cookie c in jar.GetCookies(new System.Uri("https://upload18.org/")))
                    parts.Add($"{c.Name}={c.Value}");
                if (parts.Count > 0) cookie = string.Join("; ", parts);
            }
            catch { }

            return (m3u8, cookie, u18);
        }
        catch { return (null, null, null); }
    }

    [HttpGet]
    [Route("manko/video")]
    [Route("manko/video.m3u8")]
    [Route("manko/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        string label = !string.IsNullOrEmpty(srv) ? srv : q;
        if (string.IsNullOrEmpty(label))
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : MankoTo.ApiHost + "/swx/movie/detail/" + (uri ?? "").Trim('/');

        string streamKey = ipkey($"manko:stream:{pageUrl}:{label}");
        string packed = null;
        // Cache 10 phut nhung phai check ban: ban cu (self URL k=) cua code
        // truoc van con trong fdb sau restart -> redirect ra route cu da xoa.
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw)
            && (cachedRaw.IndexOf("&m=", StringComparison.OrdinalIgnoreCase) >= 0
                || cachedRaw.IndexOf("/manko/hls.m3u8?", StringComparison.OrdinalIgnoreCase) < 0))
            packed = cachedRaw;
        if (packed == null)
        {
            var (_, servers) = await DetailAsync(uri);
            if (servers == null || servers.Count == 0)
                return OnError("stream_links", refresh_proxy: true);
            var pick = servers.FirstOrDefault(x =>
                string.Equals(x.label, label, StringComparison.OrdinalIgnoreCase));
            if (pick.label == null)
                return OnError("stream_links", refresh_proxy: true);
            packed = await ResolveOneAsync(pageUrl, pick);
            if (string.IsNullOrEmpty(packed))
                return OnError("stream_links", refresh_proxy: true);
            hybridCache.Set(streamKey, packed, cacheTime(10));
        }

        {
            // packed server1 (tu phuc vu) = "selfUrl\nreferer(embed)": redirect
            // thang, khong verify (master 526 B song o moi phim).
            // packed server2 = "m3u8\nreferer(u18)[\ncookie]": verify bang
            // HelvidUA (URL bind UA, UA sai -> 404).
            bool isSelf = packed.IndexOf("/manko/hls.m3u8?", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isSelf)
            {
                string self = packed.Split('\n')[0].Trim();
                if (!string.IsNullOrEmpty(self)) return Redirect(self);
                return OnError("stream_links", refresh_proxy: true);
            }
            bool isHelvid = label.IndexOf("Server 2", StringComparison.OrdinalIgnoreCase) >= 0;
            string link = packed, referer = pageUrl, cookie = null;
            var nl = packed.Split('\n');
            if (nl.Length > 0) link = nl[0].Trim();
            if (nl.Length > 1 && !string.IsNullOrWhiteSpace(nl[1])) referer = nl[1].Trim();
            if (nl.Length > 2 && !string.IsNullOrWhiteSpace(nl[2])) cookie = nl[2].Trim();
            var headers = HeadersModel.Init(("referer", referer));
            if (!string.IsNullOrEmpty(cookie))
                headers.Add(new HeadersModel("cookie", cookie));
            string vua = isHelvid ? MankoTo.HelvidUA : null;
            if (await VerifyLinkAsync(link, referer, cookie, vua))
            {
                var direct = httpHeaders(init, headers);
                return Redirect(HostStreamProxy(link, direct));
            }
        }
        return OnError("stream_links", refresh_proxy: true);
    }

    // Lay variant dau tien trong master (wasabi chi co 1 variant qc/v.m3u8).
    async Task<string> FlattenVariantAsync(string master, string referer)
    {
        try
        {
            string body = await MankoTo.GetHtmlAsync(master, referer, 15, proxy, init.httpversion);
            if (string.IsNullOrEmpty(body))
                body = await MankoTo.GetHtmlAsync(master, referer, 15, null, init.httpversion);
            if (string.IsNullOrEmpty(body)) return null;
            string dir = master.Contains("/") ? master.Substring(0, master.LastIndexOf('/') + 1) : master;
            foreach (string raw in body.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string abs = line.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? line : dir + line.TrimStart('/');
                if (abs.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)) return abs;
            }
        }
        catch { }
        return null;
    }

    // ========== TU PHUC VU wasabi (playlist + segment) ==========
    // Upstream doi content-type theo duoi file (.css/.txt -> text/*) khien
    // ProxyAPI coi segment la text, app hls.js bo qua -> chet o ~msn 940.
    // Tu fetch + tra content-type video/MP2T cho moi segment (mau §8, quy tac 3).
    // Khong cache playlist trong hybridCache (fdb race sau restart): /hls.m3u8
    // fetch master + variant moi lan (526 B + 91 KB, ~1s), /hls.ts mang du
    // master + referer trong URL nen stateless.
    [HttpGet]
    [Route("manko/hls.m3u8")]
    async public Task<ActionResult> Hls(string uri, string srv, string m, string r)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        if (string.IsNullOrEmpty(m)) return OnError("stream_links", refresh_proxy: true);
        string master = m;
        string referer = string.IsNullOrEmpty(r) ? null : r;
        if (string.IsNullOrEmpty(referer) && !string.IsNullOrEmpty(uri))
            referer = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? uri : MankoTo.ApiHost + "/swx/movie/detail/" + (uri ?? "").Trim('/');
        string variant = await FlattenVariantAsync(master, referer);
        string listUrl = variant ?? master;
        string body = null;
        if (variant != null)
        {
            body = await MankoTo.GetHtmlAsync(variant, referer, 15, proxy, init.httpversion);
            if (string.IsNullOrEmpty(body))
                body = await MankoTo.GetHtmlAsync(variant, referer, 15, null, init.httpversion);
        }
        if (string.IsNullOrEmpty(body))
        {
            body = await MankoTo.GetHtmlAsync(master, referer, 15, proxy, init.httpversion);
            if (string.IsNullOrEmpty(body))
                body = await MankoTo.GetHtmlAsync(master, referer, 15, null, init.httpversion);
            listUrl = master;
        }
        if (string.IsNullOrEmpty(body) || !body.Contains("#EXTM3U"))
            return OnError("stream_links", refresh_proxy: true);
        string dir = listUrl.Contains("/") ? listUrl.Substring(0, listUrl.LastIndexOf('/') + 1) : listUrl;
        var sb = new System.Text.StringBuilder(body.Length + 256);
        foreach (string raw in body.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                sb.AppendLine(raw.TrimEnd());
                continue;
            }
            string abs = line.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? line : dir + line.TrimStart('/');
            string seg = host + "/manko/hls.ts?m=" + HttpUtility.UrlEncode(master)
                + "&r=" + HttpUtility.UrlEncode(referer ?? "")
                + "&u=" + HttpUtility.UrlEncode(abs);
            sb.AppendLine(seg);
        }
        Response.ContentType = "application/vnd.apple.mpegurl";
        return Content(sb.ToString());
    }

    [HttpGet]
    [Route("manko/hls.ts")]
    async public Task<ActionResult> HlsTs(string m, string r, string u)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        if (string.IsNullOrEmpty(u))
            return OnError("stream_links", refresh_proxy: true);
        string referer = string.IsNullOrEmpty(r) ? null : r;
        if (string.IsNullOrEmpty(referer) || !referer.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return OnError("stream_links", refresh_proxy: true);
        try
        {
            var headers = HeadersModel.Init(
                ("User-Agent", MankoTo.ChromeUA),
                ("Referer", referer),
                ("Accept", "*/*"));
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, u);
            foreach (var h in headers)
                req.Headers.TryAddWithoutValidation(h.name, h.val);
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            // Gate text/* theo DUOI file (.css/.js/.txt…), khong theo
            // content-type: TS doi lot text/css van cho qua. Chi chan html/
            // json that (trang bao loi dang text/html) — nhan bang dau body
            // '<' sau khi doc vai byte dau thay vi trust header.
            bool isTextExt = u.IndexOf(".css", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".svg", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".webp", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".xml", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".json", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".woff", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".txt", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".vtt", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".srt", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf(".js", StringComparison.OrdinalIgnoreCase) >= 0;
            if (proxy != null)
            {
                string range = Request.Headers["Range"].ToString();
                if (!string.IsNullOrEmpty(range))
                    req.Headers.TryAddWithoutValidation("Range", range);
                using var resp = await client.SendAsync(req,
                    System.Net.Http.HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
                string ct = resp.Content?.Headers?.ContentType?.ToString() ?? "";
                if (!isTextExt && (ct.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                    || ct.Contains("json", StringComparison.OrdinalIgnoreCase)
                    || ct.Contains("html", StringComparison.OrdinalIgnoreCase)))
                    return OnError("stream_links", refresh_proxy: true);
                Response.StatusCode = (int)resp.StatusCode;
                Response.ContentType = "video/mp2t";
                Response.Headers["Accept-Ranges"] = "bytes";
                await using var input = await resp.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
                await input.CopyToAsync(Response.Body, HttpContext.RequestAborted);
                return new EmptyResult();
            }
            using var resp2 = await client.SendAsync(req,
                System.Net.Http.HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            string ct2 = resp2.Content?.Headers?.ContentType?.ToString() ?? "";
            if (!isTextExt && (ct2.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || ct2.Contains("json", StringComparison.OrdinalIgnoreCase)
                || ct2.Contains("html", StringComparison.OrdinalIgnoreCase)))
                return OnError("stream_links", refresh_proxy: true);
            Response.StatusCode = (int)resp2.StatusCode;
            Response.ContentType = "video/mp2t";
            Response.Headers["Accept-Ranges"] = "bytes";
            await using var input2 = await resp2.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            await input2.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            return new EmptyResult();
        }
        catch (OperationCanceledException) { return new EmptyResult(); }
        catch { return OnError("stream_links", refresh_proxy: true); }
    }

    static bool IsMediaContentType(string ct)
    {
        if (string.IsNullOrEmpty(ct)) return false;
        ct = ct.ToLowerInvariant();
        return ct.Contains("mpegurl") || ct.Contains("mp2t") || ct.Contains("octet-stream")
            || ct.Contains("video/") || ct.Contains("application/vnd.apple");
    }

    async Task<bool> VerifyLinkAsync(string link, string referer, string cookie = null, string ua = null)
    {
        if (string.IsNullOrEmpty(link)) return false;
        var headers = HeadersModel.Init(
            ("User-Agent", string.IsNullOrEmpty(ua) ? MankoTo.ChromeUA : ua),
            ("Referer", referer));
        if (!string.IsNullOrEmpty(cookie))
            headers.Add(new HeadersModel("Cookie", cookie));
        try
        {
            using var resp = await Http.ResponseHeaders(link, timeoutSeconds: 6, headers: headers, httpversion: init.httpversion);
            if (resp != null && resp.IsSuccessStatusCode && IsMediaContentType(resp.Content?.Headers?.ContentType?.ToString()))
                return true;
        }
        catch { }
        try
        {
            using var resp = await Http.ResponseHeaders(link, timeoutSeconds: 6, headers: headers, proxy: proxy, httpversion: init.httpversion);
            return resp != null && resp.IsSuccessStatusCode && IsMediaContentType(resp.Content?.Headers?.ContentType?.ToString());
        }
        catch { return false; }
    }
}
