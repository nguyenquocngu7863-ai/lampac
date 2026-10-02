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
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Jable;

public class JableController : BaseSisiController
{
    public JableController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("jable")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string q = null, string sort = null)
        => await Core(search ?? q, c, pg, sort);

    // Duong dan thay cho query — app diem manh hon, khong lo query bi
    // mat/chen khi no ghep them `pg=1`:
    //   /jable/p/hot  /jable/p/hot/2
    //   /jable/p/latest-updates
    //   /jable/p/search/<q>   /jable/p/search/<q>/2
    //   /jable/p/categories/<slug>[/2]
    //   /jable/p/tags/<slug>[/2]
    [HttpGet, Staticache(manually: true)]
    [Route("jable/p/{**rest}")]
    async public Task<ActionResult> Path(string rest, int pg = 1, string sort = null)
    {
        string search = null, c = null;

        // App phan trang bang QUERY `?pg=N` (sisi.js addUrlComponent),
        // khong phai duong dan -> bat buoc doc pg tu query.
        if (pg < 1)
            pg = 1;

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
        else if (seg.Length >= 2 && (seg[0].Equals("categories", StringComparison.OrdinalIgnoreCase)
                  || seg[0].Equals("tags", StringComparison.OrdinalIgnoreCase)))
        {
            c = string.Join("/", seg).Trim('/');
        }
        else if (seg.Length >= 1)
        {
            c = seg[0];
        }

        return await Core(search, c, pg, sort);
    }

    async Task<ActionResult> Core(string search, string c, int pg, string sort = null)
    {
        Req("CORE s=" + (search ?? "-") + " c=" + (c ?? "-") + " sort=" + (sort ?? "-") + " pg=" + pg);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        var cache = await InvokeCacheResult(
            ipkey($"jable:{search}:{c}:{sort}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(JableTo.Uri(init.host, search, c, pg, sort));

            var playlists = string.IsNullOrEmpty(html)
                ? null
                : JableTo.Playlist(html);

            // Tim khong co ket qua: site tra trang "There is no data here."
            // (va request kieu module hay bi fail/404). Day KHONG phai loi
            // -> tra SUCCESS voi list rong cho app hien trong.
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

        return PlaylistResult(cache, await MenuAsync());
    }

    // App CACHE response DAU CA PHIEN -> lan truy cap dau ma tra menu
    // rut gon (chi Tìm kiếm + Sắp xếp) thi user restart app cung thay,
    // phai xoa cache app. Nen:
    //   1. await fetch trong gioi han ~10s de menu that co ngay;
    //   2. tran han / fetch loi -> tra FALLBACK TINH (12 the loai +
    //      114 tu khoa) — van day du, khong bao gio rut gon.
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10_000;

    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("jable:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        // Request khac dang giu lock: tra fallback ngay, khong cho app
        // treo them.
        if (!await menuLock.WaitAsync(3000))
            return Fallback(hostLocal);

        try
        {
            // Request truoc do da nap xong cache trong luc ta cho lock.
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) &&
                hit2 != null && hit2.Count > 0)
                return hit2;

            var build = BuildMenuAsync(hostLocal, memKey);

            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return Fallback(hostLocal);   // fetch chay tiep nen lan sau co menu dong

            return await build;
        }
        finally
        {
            menuLock.Release();
        }
    }

    List<MenuItem> Fallback(string hostLocal)
        => JableTo.Menu(hostLocal, JableTo.FallbackCats, JableTo.FallbackTags);

    // LUON tra menu day du: dynamic neu fetch duoc, nguoc lai fallback.
    // Khong bao gio nem ra list rong.
    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        IReadOnlyList<(string name, string path)> cats = JableTo.FallbackCats;
        IReadOnlyList<(string name, string path)> tags = JableTo.FallbackTags;

        try
        {
            // /categories/ = 12 muc, KHONG co phan trang (da kiem
            // page-link rong) -> 12 la het site co.
            var catsTask = TaxonomiesAsync("/categories/", "categories");
            // /tags/ = trang index day du 115 tag. Nav trang chu chi
            // lo 40 -> phai lay tu index, khong lay nav.
            var tagsTask = TaxonomiesAsync("/tags/", "tags", 130);
            await Task.WhenAll(catsTask, tagsTask);

            var gotCats = await catsTask;
            var gotTags = await tagsTask;

            if (gotCats.Count > 0)
                cats = gotCats;
            if (gotTags.Count > 0)
                tags = gotTags;
        }
        catch { }

        var menu = JableTo.Menu(hostLocal, cats, tags);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    async Task<List<(string name, string path)>> TaxonomiesAsync(
        string page, string kind, int top = int.MaxValue)
    {
        string memKey = ipkey($"jable:tax:{kind}");

        if (hybridCache.TryGetValue(memKey, out List<(string name, string path)> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync($"{JableTo.SiteHost}{page}");
        var res = kind == "categories"
            ? JableTo.Categories(html)
            : JableTo.Tags(html);

        if (res.Count == 0)
            return res;

        if (res.Count > top)
            res = res.Take(top).ToList();

        hybridCache.Set(memKey, res, cacheTime(720), true);
        return res;
    }

    // Site chay rat rat: curl_cffi 5 lan moi 1 lan loi SSL, phai retry
    // trong nhieu vong voi backoff. Dual path httpHydra + Http.Get nhu cac
    // module khac, moi vong 14s, toi da 3-4 vong.
    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            string page = await RaceGetAsync(url,
                (int)Math.Ceiling(Math.Min(4, left)));

            if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                return page;

            await Task.Delay(1200);
        }

        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;

        // Header toi thieu: Accept + Sec-Fetch-* lam req bi dong SSL ngay
        // (curl_cffi chrome124). Giu UA + Accept-Language + Referer.
        var headers = HeadersModel.Init(
            ("User-Agent", JableTo.ChromeUA),
            ("Accept-Language", "en-US,en;q=0.9"),
            ("Referer", JableTo.SiteHost + "/"));

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

    static void Req(string m)
    {
        try
        {
            System.IO.File.AppendAllText("/data/data/com.termux/files/home/lampac-run/jable-req.log",
                DateTime.Now.ToString("HH:mm:ss") + " " + m + Environment.NewLine);
        }
        catch { }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("jable/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/jable/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    // Trang phim chi co MOT nguon: <script> var hlsUrl = 'https://
    // hot-box-gen.mushroomtrack.com/hls/<token>/<ts>/<id2>/<id>/<id>.m3u8';
    // Host doi random moi lan load nen phai lay nguyen tu trang.
    // Playlist la MEDIA playlist (AES-128, key URI tuong doi) -> Hls.js
    // tu xu ly, module khong can gi ma hoa gi.
    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : JableTo.SiteHost + "/videos/" + uri.Trim('/') + "/";

        string memKey = ipkey($"jable:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return null;

        string hls = JableTo.HlsUrl(page);
        if (string.IsNullOrEmpty(hls))
            return null;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HLS"] = hls
        };

        hybridCache.Set(memKey, links, cacheTime(10));
        return links;
    }

    [HttpGet]
    [Route("jable/video")]
    [Route("jable/video.m3u8")]
    [Route("jable/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("Referer", JableTo.SiteHost + "/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}