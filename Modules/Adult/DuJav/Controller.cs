using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace DuJav;

public class DuJavController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10_000;
    public DuJavController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("dujav")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // Gop sort ve dung tap cua context (search thi sort la no-op) —
        // TRUOC khi lap vao cache key. Clamp ve null = khong sort.
        sort = DuJavTo.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult(
            ipkey($"dujav:{search}:{c}:{sort}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(
                DuJavTo.Uri(init.host, search, c, pg, sort));
            var playlists = DuJavTo.Playlist("dujav/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await menuTask, total_pages: 0);
    }

    // menu = head (phu thuoc search/sort/c, dung lai moi request — rat re)
    //        + base (taxonomy, cache 1 lan, khong phu thuoc context)
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var menu = DuJavTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("dujav:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        if (!await menuLock.WaitAsync(2000))
            return DuJavTo.Menu(hostLocal, null);

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2)
                && hit2 != null && hit2.Count > 0)
                return hit2;

            var build = BuildMenuAsync(hostLocal, memKey);

            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return DuJavTo.Menu(hostLocal, null);

            return await build;
        }
        finally
        {
            menuLock.Release();
        }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        try
        {
            // Chi 1 request: topics gom het (~500, khong phan trang).
            var topics = await TaxonomiesAsync("/vi/topics", "topics");

            if (topics.Count > 0)
            {
                var menu = DuJavTo.Menu(hostLocal, topics);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                return menu;
            }
        }
        catch { }

        return DuJavTo.Menu(hostLocal, null);
    }

    async Task<List<(string name, string path)>> TaxonomiesAsync(string page, string kind, string cacheKey = null)
    {
        string memKey = ipkey($"dujav:tax:{cacheKey ?? kind}");

        if (hybridCache.TryGetValue(memKey, out List<(string name, string path)> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync($"{DuJavTo.SiteHost}{page}");
        var res = DuJavTo.Taxonomies(html, kind);
        if (res.Count == 0)
            return res;

        hybridCache.Set(memKey, res, cacheTime(720), true);
        return res;
    }

    async Task<string> GetPageAsync(string url)
    {
        try
        {
            return await Http.Get(url, timeoutSeconds: 15, proxy: proxy,
                headers: HeadersModel.Init(("User-Agent", DuJavTo.ChromeUA)));
        }
        catch
        {
            return null;
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("dujav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/dujav/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : DuJavTo.SiteHost + "/" + uri.Trim('/');

        string memKey = ipkey($"dujav:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        // Dinh tuyen backend THEO NOI DUNG token page (bug 2026-10-06: regex
        // loose o backend 1 vot ca URL helvid trong PLAYER_CONFIG -> Video mat
        // cookie -> proxy 404). Helvid uu tien: co PLAYER_CONFIG la di duong
        // cookie, khong bao gio di duong direct.
        string tokenHtml = await DuJavTo.FetchTokenHtml(pageUrl, proxy);
        if (string.IsNullOrEmpty(tokenHtml))
            return null;

        string helvidCfg = DuJavTo.ExtractPlayerConfig(tokenHtml);
        System.Console.WriteLine($"DuJav: route helvidCfg={(helvidCfg == null ? "null" : helvidCfg.Substring(0, Math.Min(60, helvidCfg.Length)))}");
        if (!string.IsNullOrEmpty(helvidCfg) && helvidCfg.Contains("helvid"))
        {
            var hel = await ResolveHelvidFromTokenAsync(pageUrl, tokenHtml);
            if (hel.m3u8 == null)
                return null;

            // Cache "cookie\nreferer" giong kieu JavCt giu "url\nreferer" (F5):
            // helvid doi ca 2 tren moi fetch (master + segment).
            // NOTE: luu dang Dictionary (string tran bi miss kho hieu) —
            // view-cache Dictionary hit on dinh, ck string miss lien tuc.
            System.Console.WriteLine($"DuJav: ck WRITE key={ipkey($"dujav:ck:{pageUrl}")}");
            hybridCache.Set(ipkey($"dujav:ck:{pageUrl}"),
                new Dictionary<string, string>() { { "packed", hel.cookie + "\n" + hel.referer } },
                cacheTime(20), true);

            var res2 = new Dictionary<string, string>() { { "1080p", hel.m3u8 } };
            hybridCache.Set(memKey, res2, cacheTime(20), true);
            return res2;
        }

        // Backend 1 (uncenxcdn): m3u8 truc tiep, khong CF, khong can cookie.
        // VALIDATE bat buoc (bai hoc HeoVl: URL tracker co ".m3u8" trong query).
        string m3u8 = DuJavTo.ExtractDirectM3U8(tokenHtml);
        if (!await ValidM3U8Async(m3u8, null))
            return null;

        var res = new Dictionary<string, string>() { { "1080p", m3u8 } };
        hybridCache.Set(memKey, res, cacheTime(30), true);
        return res;
    }

    // GET nhe: chi nhan link tra 200 + mo dau #EXTM3U (khong tin regex).
    async Task<bool> ValidM3U8Async(string url, List<HeadersModel> headers)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.Contains(".m3u8"))
            return false;
        try
        {
            string body = await Http.Get(url, timeoutSeconds: 12, proxy: proxy,
                headers: HeadersModel.Join(
                    HeadersModel.Init(("User-Agent", DuJavTo.ChromeUA)), headers));
            return !string.IsNullOrEmpty(body) && body.Contains("#EXTM3U");
        }
        catch { return false; }
    }

    // Chuoi helvid (can cookie jar xuyen suot -> HttpClient rieng, khong dung
    // Http.Get tinh). tokenHtml lay san tu FetchTokenHtml (khong fetch lai).
    async Task<(string m3u8, string cookie, string referer)> ResolveHelvidFromTokenAsync(
        string pageUrl, string tokenHtml)
    {
        try
        {
            var jar = new System.Net.CookieContainer();
            var handler = new System.Net.Http.HttpClientHandler { CookieContainer = jar };
            if (proxy != null)
            {
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }
            using var client = new System.Net.Http.HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DuJavTo.ChromeUA);

            async Task<string> Get(string url, string referer)
            {
                using var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(referer))
                    req.Headers.TryAddWithoutValidation("Referer", referer);
                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return null;
                return await resp.Content.ReadAsStringAsync();
            }

            string m3u8 = DuJavTo.ExtractPlayerConfig(tokenHtml);
            if (string.IsNullOrEmpty(m3u8) || !m3u8.Contains("helvid"))
                return (null, null, null);

            // Trang play upload18 cap cookie u18ps (khong can sid/rid).
            // videoKey uy tin hon watch slug (lay san tu PLAYER_CONFIG).
            string vkey = DuJavTo.ExtractVideoKey(tokenHtml);
            if (string.IsNullOrEmpty(vkey))
                return (null, null, null);
            string u18 = "https://upload18.org/play/index/" + vkey;
            await Get(u18, pageUrl);

            // Master playlist: doi Referer=trang play u18 + cookie jar.
            // (Thu truc tiep: Referer u18 + u18ps -> 200 #EXTM3U.)
            string master;
            {
                using var req = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get, m3u8);
                req.Headers.TryAddWithoutValidation("Referer", u18);
                using var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                    return (null, null, null);
                master = await resp.Content.ReadAsStringAsync();
            }
            if (string.IsNullOrEmpty(master) || !master.Contains("#EXTM3U"))
                return (null, null, null);

            var parts = new List<string>();
            foreach (System.Net.Cookie c in jar.GetCookies(new System.Uri("https://helvid.com/")))
                parts.Add($"{c.Name}={c.Value}");
            foreach (System.Net.Cookie c in jar.GetCookies(new System.Uri("https://upload18.org/")))
            {
                string kv = $"{c.Name}={c.Value}";
                if (!parts.Contains(kv))
                    parts.Add(kv);
            }
            if (parts.Count == 0)
                return (null, null, null);

            return (m3u8, string.Join("; ", parts), u18);
        }
        catch { return (null, null, null); }
    }

    [HttpGet]
    [Route("dujav/video")]
    [Route("dujav/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        // Helvid bat buoc qua PROXY (app khong tai noi manifest truc tiep —
        // manifestLoadError tren app that 2026-10-06). Cookie + referer lay tu
        // cache luc resolve (ck HIT da verify). Uncenxcdn cung duong nay.
        var headers = HeadersModel.Init(
            ("referer", DuJavTo.SiteHost + "/")
        );
        // Backend helvid: kem cookie + referer (trang play upload18) lay luc
        // resolve — thieu la Cloudflare 403 (xem ResolveHelvidFromTokenAsync).
        // Cache dang Dictionary (string tran miss kho hieu — view-cache
        // Dictionary hit on dinh).
        try
        {
            string purl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? uri : DuJavTo.SiteHost + "/" + uri.Trim('/');
            if (hybridCache.TryGetValue(ipkey($"dujav:ck:{purl}"), out Dictionary<string, string> ckhit) &&
                ckhit != null && ckhit.TryGetValue("packed", out string packed2) &&
                !string.IsNullOrEmpty(packed2))
            {
                // packed = "cookie\nreferer" (kieu JavCt giu "url\nreferer")
                int nl = packed2.IndexOf('\n');
                string ck = nl > 0 ? packed2.Substring(0, nl) : packed2;
                string rf = nl > 0 ? packed2.Substring(nl + 1) : null;
                if (!string.IsNullOrEmpty(ck))
                    headers.Add(new HeadersModel("cookie", ck));
                if (!string.IsNullOrEmpty(rf))
                {
                    headers.RemoveAll(h => h.name == "referer");
                    headers.Add(new HeadersModel("referer", rf));
                }
            }
        }
        catch { }

        var direct = httpHeaders(init, headers);
        return Redirect(HostStreamProxy(link, direct));
    }
}
