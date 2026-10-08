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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SexTb;

public class SexTbController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 25000;
    public SexTbController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sextb")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        // Gop sort ve dung tap cua context (genre/studio moi co sort that;
        // home/search thi sort la no-op) — TRUOC khi lap vao cache key.
        // Clamp ve null nghia la: context nay khong sort duoc.
        // GIU c goc cho MenuHead (c sau merge da lan sort -> link se nhan doi).
        string menuC = c;
        sort = SexTbTo.ClampSort(sort, search, c);

        // sort param legacy: map to c query if provided
        if (!string.IsNullOrWhiteSpace(sort))
        {
            if (string.IsNullOrWhiteSpace(c))
                c = "?sort=" + sort.Trim();
            else if (c.IndexOf('?') < 0)
                c = c.TrimEnd('/') + "?sort=" + sort.Trim();
            else
                c = c.Trim() + "&sort=" + sort.Trim();
        }

        var menuTask = MenuAsync(search, sort, menuC);

        var cache = await InvokeCacheResult(ipkey($"sextb:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(SexTbTo.Uri(init.host, search, c, pg));
            var playlists = SexTbTo.Playlist("sextb/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search))
                    return e.Success(new List<PlaylistItem>());
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await menuTask, total_pages: 0);
    }

    // menu = head (phu thuoc search/sort/c, dung lai moi request — rat re)
    //        + base (taxonomy nghìn muc, cache 1 lan, khong phu thuoc context)
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var menu = SexTbTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("sextb:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return SexTbTo.Menu(hostLocal, null, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return SexTbTo.Menu(hostLocal, null, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        long tr = Environment.TickCount64;
        try
        {
            var genresTask = TaxonomiesAsync("/genres", "genre/");
            var studiosTask = StudiosAsync();
            await Task.WhenAll(genresTask, studiosTask);
            var genres = await genresTask;
            var studios = await studiosTask;
            if (genres.Count > 0 || studios.Count > 0)
            {
                var menu = SexTbTo.Menu(hostLocal, genres, studios);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine($"SexTb: menu genres={genres.Count} studios={studios.Count} ({Environment.TickCount64 - tr}ms)");
                return menu;
            }
        }
        catch { }
        Console.WriteLine($"SexTb: menu fail ({Environment.TickCount64 - tr}ms)");
        return SexTbTo.Menu(hostLocal, null, null);
    }

    // Studios co 2055 muc, phan theo chu cai /list-studios/a..z. Trang goc chi co ~168.
    // Fetch song song base + a-z roi gop dedup + sort.
    async Task<List<(string slug, string name)>> StudiosAsync()
    {
        string memKey = ipkey("sextb:tax:studio/");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) && hit != null && hit.Count > 0)
            return hit;
        var pages = new List<string>(27) { "/list-studios" };
        for (char c = 'a'; c <= 'z'; c++) pages.Add("/list-studios/" + c);
        var tasks = pages.Select(p => GetPageAsync($"{SexTbTo.SiteHost}{p}")).ToArray();
        await Task.WhenAll(tasks);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(string slug, string name)>();
        foreach (var t in tasks)
        {
            string html = await t;
            if (string.IsNullOrEmpty(html)) continue;
            foreach (var it in SexTbTo.Taxonomies(html, "studio/"))
                if (seen.Add(it.slug)) all.Add(it);
        }
        all.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        if (all.Count == 0) return all;
        hybridCache.Set(memKey, all, cacheTime(720), true);
        return all;
    }

    async Task<List<(string slug, string name)>> TaxonomiesAsync(string page, string prefix)
    {
        string memKey = ipkey($"sextb:tax:{prefix}");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) && hit != null && hit.Count > 0)
            return hit;
        string html = await GetPageAsync($"{SexTbTo.SiteHost}{page}");
        var res = SexTbTo.Taxonomies(html, prefix);
        if (res.Count == 0) return res;
        hybridCache.Set(memKey, res, cacheTime(720), true);
        return res;
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(14);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1) break;
            string page = await RaceGetAsync(url, (int)Math.Ceiling(Math.Min(4, left)));
            if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                return page;
        }
        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;
        var headers = HeadersModel.Init(
            ("User-Agent", SexTbTo.ChromeUA),
            ("Referer", SexTbTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        var t1 = Task.Run(async () =>
        {
            try { await httpHydra.GetSpan(url, span => { a = span.ToString(); }, addheaders: headers); }
            catch { }
        });
        var t2 = Task.Run(async () =>
        {
            try
            {
                b = await Http.Get(url, timeoutSeconds: Math.Max(6, seconds), httpversion: init.httpversion, proxy: proxy, headers: headers);
            }
            catch { }
        });
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrEmpty(a)) return a;
            if (!string.IsNullOrEmpty(b)) return b;
            if (t1.IsCompleted && t2.IsCompleted) break;
            await Task.Delay(200);
        }
        return !string.IsNullOrEmpty(a) ? a : b;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sextb/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        // LAZY PLAYER (chuan SupJav/JavHDToday 2026-10-08): /vidosik chi
        // liet ke server (1 fetch detail), KHONG resolve — server nao user
        // BAM moi resolve o /video. Cu~ resolve het 5-6 ajax + media
        // (F4/StreamHg/Dood/Streamtape...) mat 10-30s, app timeout.
        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in servers.OrderBy(x => SexTbTo.Priority(x.label)).ThenBy(x => x.label))
        {
            if (!SexTbTo.IsSupported(s.label) || dict.ContainsKey(s.label))
                continue;
            string tail = SexTbTo.KindByLabel(s.label) == "mp4" ? ".mp4" : ".m3u8";
            dict[s.label] = $"{host}/sextb/video{tail}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(s.label)}";
        }
        if (dict.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        return Json(dict);
    }

    // Server + episode id tu trang detail (nut btn-player data-source).
    async Task<List<(string label, string episode)>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : SexTbTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"sextb:srv2:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<(string label, string episode)> cached) && cached != null && cached.Count > 0)
            return cached;
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return null;
        var (filmId, _, _) = SexTbTo.Tokens(page);
        if (string.IsNullOrEmpty(filmId))
            return null;
        var servers = SexTbTo.Servers(page, filmId);
        if (servers.Count == 0)
            return null;
        var res = servers.Where(kv => SexTbTo.IsSupported(kv.Key))
            .Select(kv => (kv.Key, kv.Value)).ToList();
        if (res.Count == 0)
            return null;
        hybridCache.Set(memKey, res, cacheTime(15));
        return res;
    }

    // Tokens + auth tuoi tu trang detail cho /video: pt xoay theo moi
    // ajax, nen lay tuoi cho server duoc bam — nut nao cung co the la
    // "click dau tien" nhu tren web.
    static (string filmId, string pt, string pk, string auth) PageTokens(string page)
    {
        if (string.IsNullOrEmpty(page))
            return (null, null, null, "");
        var (filmId, pt, pk) = SexTbTo.Tokens(page);
        if (string.IsNullOrEmpty(filmId) || string.IsNullOrEmpty(pt))
            return (null, null, null, "");
        var mToken = Regex.Match(page, @"<meta[^>]*name\s*=\s*[""']_token[""'][^>]*value\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!mToken.Success) mToken = Regex.Match(page, @"id\s*=\s*[""']token[""'][^>]*value\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        var mSocket = Regex.Match(page, @"<meta[^>]*name\s*=\s*[""']_socket[""'][^>]*value\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!mSocket.Success) mSocket = Regex.Match(page, @"id\s*=\s*[""']socket[""'][^>]*value\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        string auth = "";
        if (mToken.Success && mSocket.Success)
        {
            try { auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(mToken.Groups[1].Value.Trim() + ":" + mSocket.Groups[1].Value.Trim())); } catch { auth = ""; }
        }
        return (filmId, pt, pk ?? "", auth);
    }

    async Task<(string packed, string nextPt, string nextPk)> ResolveServerAsync(string pageUrl, string filmId, string episode, string pt, string pk, string auth, string label)
    {
        string api;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["episode"] = episode,
                ["filmId"] = filmId,
                ["pt"] = pt
            });
            var headers = HeadersModel.Init(
                ("User-Agent", SexTbTo.ChromeUA),
                ("Referer", pageUrl),
                ("Origin", SexTbTo.SiteHost),
                ("Accept", "application/json, text/plain, */*"),
                ("X-Requested-With", "XMLHttpRequest")
            );
            if (!string.IsNullOrEmpty(auth))
                headers = HeadersModel.Init(
                    ("User-Agent", SexTbTo.ChromeUA),
                    ("Referer", pageUrl),
                    ("Origin", SexTbTo.SiteHost),
                    ("Accept", "application/json, text/plain, */*"),
                    ("X-Requested-With", "XMLHttpRequest"),
                    ("Authorization", "Basic " + auth)
                );
            api = await Http.Post(SexTbTo.PlayerApi, content, timeoutSeconds: Math.Max(15, init.httptimeout), headers: headers, proxy: proxy, httpversion: init.httpversion, statusCodeOK: true, disposeData: true);
        }
        catch { return (null, null, null); }
        if (string.IsNullOrEmpty(api)) return (null, null, null);
        string playerHtml = null, nextPt = null, nextPk = null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(api);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind != System.Text.Json.JsonValueKind.Null && err.ToString().Length > 0)
            {
                // try extract next_pt/pk even on error? but skip
            }
            if (root.TryGetProperty("next_pt", out var npt) && npt.ValueKind == System.Text.Json.JsonValueKind.String) nextPt = npt.GetString();
            if (root.TryGetProperty("next_pk", out var npk) && npk.ValueKind == System.Text.Json.JsonValueKind.String) nextPk = npk.GetString();
            if (root.TryGetProperty("player_enc", out var enc) && enc.ValueKind == System.Text.Json.JsonValueKind.String && enc.GetString().Length > 0)
                playerHtml = SexTbTo.XorDecrypt(enc.GetString(), pk);
            else if (root.TryGetProperty("player", out var pl))
                playerHtml = pl.ToString();
        }
        catch { return (null, nextPt, nextPk); }
        string embed = SexTbTo.IframeUrl(playerHtml);
        if (string.IsNullOrEmpty(embed)) return (null, nextPt, nextPk);
        // try resolve by kind
        try
        {
            if (embed.IndexOf("f4scdn.com", StringComparison.OrdinalIgnoreCase) >= 0 || embed.IndexOf("f4stream.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string f4 = await SexTbTo.F4SourceAsync(embed, Math.Min(10, init.httptimeout), proxy, init.httpversion);
                if (!string.IsNullOrEmpty(f4))
                    return (f4 + "\n" + embed, nextPt, nextPk);
            }
        }
        catch { }
        try
        {
            if (embed.IndexOf("ryderjet.com", StringComparison.OrdinalIgnoreCase) >= 0 || embed.IndexOf("hglink.to", StringComparison.OrdinalIgnoreCase) >= 0 || embed.IndexOf("vibuxer.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string player = SexTbTo.StreamHgUrl(embed);
                // CDN hls (ryderjet/vibuxer) sinh token theo IP - phai dung cung IP voi stream (direct).
                // Truoc dung proxy o day nhung stream lai direct -> token lech -> 403.
                // Dood cung chi mo khi direct (proxy tra captcha 5k). Giua nguyen direct cho ca embed lan stream.
                string html = await Http.Get(player, timeoutSeconds: 12, headers: HeadersModel.Init(("User-Agent", SexTbTo.ChromeUA), ("Referer", pageUrl)), httpversion: init.httpversion);
                var masters = SexTbTo.StreamHgMasters(html, player);
                if (masters.Count > 0)
                {
                    string master = masters[0];
                    return (master + "\n" + player, nextPt, nextPk);
                }
                // fallback: thu qua proxy neu direct khong ra (truong hop CDN chan VN)
                try
                {
                    string html2 = await Http.Get(player, timeoutSeconds: 12, headers: HeadersModel.Init(("User-Agent", SexTbTo.ChromeUA), ("Referer", pageUrl)), proxy: proxy, httpversion: init.httpversion);
                    var masters2 = SexTbTo.StreamHgMasters(html2, player);
                    if (masters2.Count > 0)
                        return (masters2[0] + "\n" + player, nextPt, nextPk);
                }
                catch { }
            }
        }
        catch { }
        try
        {
            if (SexTbTo.IsPlaymate(embed))
            {
                string pm = await SexTbTo.PlaymateSourceAsync(embed, 10, proxy, init.httpversion);
                if (!string.IsNullOrEmpty(pm))
                    return (pm + "\n" + embed, nextPt, nextPk);
            }
        }
        catch { }
        try
        {
            if (SexTbTo.IsUpn(embed))
            {
                var (upn, upnRef) = await SexTbTo.UpnSourceAsync(embed, 10, proxy, init.httpversion);
                if (!string.IsNullOrEmpty(upn))
                    return (upn + "\n" + upnRef, nextPt, nextPk);
            }
        }
        catch { }
        try
        {
            if (embed.IndexOf("playmogo", StringComparison.OrdinalIgnoreCase) >= 0 || embed.IndexOf("dood", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var (dood, doodRef) = await SexTbTo.DoodSourceAsync(embed, 10, proxy, init.httpversion);
                if (!string.IsNullOrEmpty(dood))
                    return (dood + "\n" + doodRef, nextPt, nextPk);
            }
        }
        catch { }
        try
        {
            // ST (Streamtape): player ajax tra thang trang embed (co
            // #robotlink) hoac iframe streamtape — boc token MOI (script
            // gan lai) -> get_video -> 302 CDN .mp4 (cong thuc SupJav).
            string stHtml = null;
            if (SexTbTo.IsStreamtape(embed))
            {
                try
                {
                    stHtml = await Http.Get(embed, timeoutSeconds: 12,
                        headers: HeadersModel.Init(("User-Agent", SexTbTo.ChromeUA), ("Referer", pageUrl)),
                        httpversion: init.httpversion);
                }
                catch { }
                if (string.IsNullOrEmpty(stHtml) || stHtml.IndexOf("robotlink", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    try
                    {
                        stHtml = await Http.Get(embed, timeoutSeconds: 12,
                            headers: HeadersModel.Init(("User-Agent", SexTbTo.ChromeUA), ("Referer", pageUrl)),
                            proxy: proxy, httpversion: init.httpversion);
                    }
                    catch { }
                }
            }
            else if (!string.IsNullOrEmpty(playerHtml) && playerHtml.IndexOf("robotlink", StringComparison.OrdinalIgnoreCase) >= 0)
                stHtml = playerHtml;

            string stApi = SexTbTo.StreamtapeMp4(stHtml);
            if (!string.IsNullOrEmpty(stApi))
            {
                string stRef = SexTbTo.StreamtapeEmbedUrl(stHtml ?? "") ?? embed;
                string cdn = await SexTbTo.CurlFinalUrl(stApi, stRef, 10);
                string mp4 = !string.IsNullOrEmpty(cdn) ? cdn : stApi;
                return (mp4 + "\n" + stRef, nextPt, nextPk);
            }
        }
        catch { }
        // generic: if embed is direct hls/mp4 already?
        if (embed.StartsWith("http", StringComparison.OrdinalIgnoreCase) && (embed.Contains(".m3u8") || embed.Contains(".mp4")))
            return (embed + "\n" + pageUrl, nextPt, nextPk);
        return (null, nextPt, nextPk);
    }

    [HttpGet]
    [Route("sextb/video")]
    [Route("sextb/video.m3u8")]
    [Route("sextb/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        // Cache resolve: an ngay neu da resolve trong 10 phut truoc.
        string streamKey = ipkey($"sextb:pack:{uri}:{q}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            var servers = await DetailServersAsync(uri);
            var pick = servers?.FirstOrDefault(x =>
                string.Equals(x.label, q, StringComparison.OrdinalIgnoreCase));
            if (pick == null || string.IsNullOrEmpty(pick.Value.episode))
                return OnError("stream_links", refresh_proxy: true);
            string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : SexTbTo.SiteHost + "/" + uri.Trim('/');
            string page = await GetPageAsync(pageUrl);
            var (filmId, pt, pk, auth) = PageTokens(page);
            if (string.IsNullOrEmpty(filmId))
                return OnError("stream_links", refresh_proxy: true);
            var (one, _, _) = await ResolveServerAsync(pageUrl, filmId, pick.Value.episode, pt, pk, auth, pick.Value.label);
            if (string.IsNullOrEmpty(one))
                return OnError("stream_links", refresh_proxy: true);
            packed = one;
            hybridCache.Set(streamKey, packed, cacheTime(10));
        }
        string link = packed, referer = SexTbTo.SiteHost + "/";
        int nl = packed.IndexOf('\n');
        if (nl > 0) { link = packed.Substring(0, nl); referer = packed.Substring(nl + 1); }
        var direct = httpHeaders(init, HeadersModel.Init(("referer", referer)));
        return Redirect(HostStreamProxy(link, direct));
    }
}
