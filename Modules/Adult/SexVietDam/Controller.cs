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

namespace SexVietDam;

public class SexVietDamController : BaseSisiController
{
    public SexVietDamController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sexvietdam")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string q = null)
        => await Core(search ?? q, c, pg);

    // Duong dan thay cho query — app diem manh hon, khong lo query bi
    // mat/chen khi no ghep them `pg=1`:
    //   /sexvietdam/p/the-loai/mong-dep   /sexvietdam/p/tag/xnxx/2
    //   /sexvietdam/p/search/<q>          /sexvietdam/p/2
    [HttpGet, Staticache(manually: true)]
    [Route("sexvietdam/p/{**rest}")]
    async public Task<ActionResult> Path(string rest, int pg = 1)
    {
        if (pg < 1)
            pg = 1;

        string search = null, c = null;
        string[] seg = (rest ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Trang 2 co the la phan duong dan cuoi (/p/the-loai/x/2)
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
        else if (seg.Length >= 2 && (seg[0].Equals("the-loai", StringComparison.OrdinalIgnoreCase)
              || seg[0].Equals("tag", StringComparison.OrdinalIgnoreCase)))
        {
            c = string.Join("/", seg).Trim('/');
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

        var cache = await InvokeCacheResult(
            ipkey($"svd:{search}:{c}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(SvdTo.Uri(init.host, search, c, pg));

            var playlists = string.IsNullOrEmpty(html)
                ? null
                : SvdTo.Playlist(html);

            // Tim khong co ket qua / `k` rong -> site tra 404. Day KHONG phai
            // loi -> tra SUCCESS voi list rong cho app hien trong.
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

    // App CACHE response DAU CA PHIEN -> phai tra menu day du ngay lan dau.
    // Fetch qua han / loi -> FALLBACK TINH (16 the loai + 2 tu khoa).
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 8_000;

    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("svd:menu");

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
        => SvdTo.Menu(hostLocal, SvdTo.FallbackGenres, SvdTo.FallbackTags);

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        var genres = (IReadOnlyList<(string name, string path)>)SvdTo.FallbackGenres;
        var tags = SvdTo.FallbackTags;

        try
        {
            // Trang chu la trang duy nhat co NAV day du (khong co /the-loai
            // hay /tag index — ca hai deu 404).
            string html = await GetPageAsync(SvdTo.SiteHost + "/");
            if (!string.IsNullOrEmpty(html))
            {
                var g = SvdTo.Genres(html);
                var t = SvdTo.Tags(html);

                if (g.Count > 0) genres = g;
                if (t.Count > 0) tags = t;
            }
        }
        catch { }

        var menu = SvdTo.Menu(hostLocal, genres, tags);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    // Site khong can Chrome nhung hay bi loi SSL lan dau -> retry + dual path
    // giong Jable (httpHydra + Http.Get), moi vong toi da 4s.
    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            string page = await RaceGetAsync(url, (int)Math.Ceiling(Math.Min(4, left)));

            if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                return page;

            await Task.Delay(1000);
        }

        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;

        var headers = HeadersModel.Init(
            ("User-Agent", SvdTo.ChromeUA),
            ("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8"),
            ("Referer", SvdTo.SiteHost + "/"));

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

    [HttpGet, Staticache(manually: true)]
    [Route("sexvietdam/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/sexvietdam/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    // Trang chi tien co 3 nút `set-player-source`, moi nut 1 cach lay stream
    // (xem skill lampac-deobfuscate F13). PlayHQ da bo vi CDN chet.
    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : SvdTo.SiteHost + "/phim-sex/" + uri.Trim('/');

        string memKey = ipkey($"svd:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return null;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, src) in SvdTo.Servers(page))
        {
            string url = await ResolveServerAsync(src);
            if (string.IsNullOrEmpty(url))
                continue;

            string key = name;
            int n = 2;
            while (links.ContainsKey(key))
                key = name + " " + (n++);

            links[key] = url;
        }

        if (links.Count == 0)
            return null;

        // master.m3u8 cua wogplayer co `?e=` het han khoang 1h -> cache ngan.
        hybridCache.Set(memKey, links, cacheTime(5));
        return links;
    }

    async Task<string> ResolveServerAsync(string src)
    {
        try
        {
            var u = new Uri(src);

            // PlayHQ: da giai het (POST /playiframe + AES + MD5) nhung segment
            // bi 302 sang host NXDOMAIN -> khong phat duoc, khong dua vao list.
            if (u.Host.IndexOf("playheovl", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            // [2] Alias — trang player tu khai `window.videoData.sources`.
            if (u.Host.EndsWith("vcast.name", StringComparison.OrdinalIgnoreCase))
            {
                string pl = await GetPageAsync(src);
                string f = SvdTo.FileUrl(pl);
                if (string.IsNullOrEmpty(f))
                    return null;
                if (f.StartsWith("/"))
                    f = "https://" + u.Host + f;
                return f.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? f : null;
            }

            // [1] StreamQQ — `data-source` tro e.streamforester.name nhung 302
            // sang host that (wogplayer.top). Lay host cuoi bang GetLocation,
            // roi POST /videos/<id>/config?d=<domain> de nhan sources.
            string final = await Http.GetLocation(
                src,
                referer: SvdTo.SiteHost + "/",
                timeoutSeconds: 12,
                httpversion: init.httpversion,
                allowAutoRedirect: true,
                proxy: proxy);

            if (string.IsNullOrEmpty(final))
                return null;

            var fu = new Uri(final);
            string id = SvdTo.VideoId(final);
            if (string.IsNullOrEmpty(id))
                return null;

            string cfg = fu.Scheme + "://" + fu.Authority + "/videos/" + id
                + "/config?d=" + HttpUtility.UrlEncode(SvdTo.Domain);

            var headers = HeadersModel.Init(
                ("User-Agent", SvdTo.ChromeUA),
                ("Referer", fu.Scheme + "://" + fu.Authority + "/"));

            // Endpoint nhan moi kieu body (da do: khong body / json / form deu 200)
            string json = await Http.Post(
                cfg, "",
                headers: headers,
                timeoutSeconds: 12,
                httpversion: init.httpversion,
                proxy: proxy);

            return SvdTo.FileUrl(json);
        }
        catch
        {
            return null;
        }
    }

    [HttpGet]
    [Route("sexvietdam/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || string.IsNullOrEmpty(q) ||
            !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        // Referer bat buoc va KHAC NHAU:
        //   vcast.name/sdeli.name : treo Referer sexvietdam -> 403
        //   wogplayer/mio9ecge    : khong can, sexvietdam van 200
        string referer = link.IndexOf("vcast.name", StringComparison.OrdinalIgnoreCase) >= 0
            ? "https://vcast.name/"
            : SvdTo.SiteHost + "/";

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", SvdTo.ChromeUA),
            ("Referer", referer)
        ));

        return Redirect(HostStreamProxy(link, direct));
    }
}
