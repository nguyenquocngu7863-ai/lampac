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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Vjav;

public class VjavController : BaseSisiController
{
    public VjavController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("vjav")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1,
        string q = null, string sort = null)
        => await Core(search ?? q, c, pg, sort);

    async Task<ActionResult> Core(string search, string c, int pg, string sort)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // cache key PHAI du search + c + sort + pg (skill 9e)
        var cache = await InvokeCacheResult(
            ipkey($"vjav:{search}:{c}:{sort}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string json = await GetJsonAsync(VjavTo.ApiUrl(
                init.host, search, c, pg, sort));

            var playlists = string.IsNullOrEmpty(json)
                ? null
                : VjavTo.Playlist(json);

            // Search khong co ket qua -> tra SUCCESS + list rong, khong Fail
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

        var menuTask = MenuAsync();

        return PlaylistResult(cache, await menuTask);
    }

    // ───────── MENU ─────────
    // App cache response DAU CA PHIEN -> neu lan dau tra menu rut gon
    // (2 dong) thi restart app van thay. Nen await fetch trong gioi han
    // 10s, het gio thi tra FALLBACK TINH (12 the loai) — khong bao gio
    // nem list rong (skill 9a: thieu taxonomy duoc, thieu dong thi khong).
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10_000;

    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("vjav:menu");

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
        => VjavTo.Menu(hostLocal, VjavTo.FallbackCats);

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        IReadOnlyList<(string name, string path)> cats = VjavTo.FallbackCats;

        try
        {
            // PHAI dung SiteHost, KHONG dung hostLocal. `host` trong
            // controller = http://127.0.0.1:9118 (Lampac). Gọi hostLocal +
            // "/api/..." se tro ve chinh Lampac -> 404 -> rơi fallback 12 muc.
            string json = await GetJsonAsync(
                VjavTo.SiteHost + "/api/json/categories/14400/str.all.en.json", 12);

            var got = VjavTo.Categories(json);
            if (got.Count > 0)
                cats = got;          // 156 muc, duoi nguong 300 -> 1 submenu phang
        }
        catch { }

        var menu = VjavTo.Menu(hostLocal, cats);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    // ───────── LAY JSON ─────────
    // Site hay dong SSL dot ngot (curl_cffi 5 lan moi ~4 lan 200) -> chay
    // song song httpHydra + Http.Get nhu Jable, retry trong 20s.
    async Task<string> GetJsonAsync(string url, int budgetSec = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(budgetSec);

        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            string body = await RaceGetAsync(url,
                (int)Math.Ceiling(Math.Min(5, left)));

            if (!string.IsNullOrEmpty(body) && body.Length >= 50)
                return body;

            await Task.Delay(1200);
        }

        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;

        var headers = HeadersModel.Init(
            ("User-Agent", VjavTo.ChromeUA),
            ("Accept", "application/json, text/plain, */*"),
            ("Accept-Language", "en-US,en;q=0.9"),
            ("Referer", VjavTo.SiteHost + "/"));

        var t1 = Task.Run(async () =>
        {
            try
            {
                await httpHydra.GetSpan(url, span =>
                {
                    a = span.ToString();
                }, addheaders: headers);
            }
            catch { }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                b = await Http.Get(
                    url,
                    timeoutSeconds: Math.Max(6, seconds),
                    httpversion: init.httpversion,
                    proxy: proxy,
                    headers: headers);
            }
            catch { }
        });

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrEmpty(a))
                return a;

            if (!string.IsNullOrEmpty(b))
                return b;

            if (t1.IsCompleted && t2.IsCompleted)
                break;

            await Task.Delay(200);
        }

        return !string.IsNullOrEmpty(a) ? a : b;
    }

    // ───────── STREAM ─────────
    // /videos/<id>/<dir>/ -> /api/videofile.php?video_id=<id>
    //   -> [{ "format":"_hq.mp4", "video_url":"<MA HOA>" }]
    //   -> DecodeVideoUrl -> /get_file/3/<token>/<id1>/<id>/<id>_hq.mp4/?...
    // Token co `ti=<epoch>` nen KHONG duoc tra URL upstream cho client
    // (skill 8: sua token het han -> 403 khi tua). Luon resolve server-side.
    [HttpGet, Staticache(manually: true)]
    [Route("vjav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key,
            k => $"{host}/vjav/video.mp4?uri={HttpUtility.UrlEncode(uri)}"
                + $"&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    [HttpGet]
    [Route("vjav/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || string.IsNullOrEmpty(q)
            || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", VjavTo.ChromeUA),
            ("Referer", VjavTo.SiteHost + "/")));

        return Redirect(HostStreamProxy(link, direct));
    }

    static string VideoId(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        var m = Regex.Match(uri, @"/videos/(\d{3,})/",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success)
            return m.Groups[1].Value;

        // Chi con id so thoi
        m = Regex.Match(uri.Trim(), @"^\d{3,}$");
        if (m.Success)
            return m.Value;

        return null;
    }

    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        string id = VideoId(uri);
        if (string.IsNullOrEmpty(id))
            return null;

        string memKey = ipkey($"vjav:file:{id}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string json = await GetJsonAsync(VjavTo.SiteHost
            + "/api/videofile.php?video_id=" + id + "&lifetime=864000000", 12);

        if (string.IsNullOrEmpty(json))
            return null;

        var res = ParseVideofile(json);
        if (res.Count == 0)
        {
            Req("vjav resolve id=" + id + " rong json=" +
                (json.Length > 0 ? json.Substring(0, Math.Min(160, json.Length)) : "-"));
            return null;
        }

        hybridCache.Set(memKey, res, cacheTime(10));
        return res;
    }

    // Videofile tra MANG: [{format, video_url}, ...] (khong phai object)
    static Dictionary<string, string> ParseVideofile(string json)
    {
        var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return res; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return res;

            foreach (var it in root.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object)
                    continue;

                string raw = it.TryGetProperty("video_url", out var u)
                    && u.ValueKind == JsonValueKind.String
                    ? u.GetString() : null;

                string format = it.TryGetProperty("format", out var f)
                    && f.ValueKind == JsonValueKind.String
                    ? f.GetString() : null;

                string link = VjavTo.DecodeVideoUrl(raw);
                if (string.IsNullOrEmpty(link))
                    continue;

                string key = VjavTo.Quality(format);
                int n = 2;
                while (res.ContainsKey(key))
                    key = VjavTo.Quality(format) + "-" + n++;

                res[key] = link;
            }
        }

        return res;
    }

    // Debug ghi file (skill 9f: Console.WriteLine khong vao log native)
    static void Req(string m)
    {
        try
        {
            System.IO.File.AppendAllText(
                "/data/data/com.termux/files/home/lampac-run/vjav-req.log",
                DateTime.Now.ToString("HH:mm:ss") + " " + m + Environment.NewLine);
        }
        catch { }
    }
}
