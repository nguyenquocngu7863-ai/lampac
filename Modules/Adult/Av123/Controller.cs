using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Av123;

public class Av123Controller : BaseSisiController
{
    public Av123Controller() : base(ModInit.conf) { }

    // 123av render kieu Alpine: cho hydrate roi EvaluateAsync lay tiles
    // waitTiles: trang list cho Alpine hydrate tile; trang video cho <video src>
    async Task<(string content, string tilesJson)> PageFetchAsync(string url, bool waitTiles)
    {
        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync("Av123", new Dictionary<string, string>
                {
                    ["User-Agent"] = Av123To.ChromeUA,
                    ["Referer"] = "https://123av.com/"
                }, keepopen: false);

                if (page == null)
                    return (null, null);

                var resp = await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = 20000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });

                if (resp == null || !resp.Ok)
                    return (null, null);

                if (!waitTiles)
                {
                    // trang video: doi <video src> toi da ~10s
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            string has = await page.EvaluateAsync<string>(@"() => {
                                const v = document.querySelector('video[src], video source[src], iframe[src*=""javplayer""]');
                                return v ? (v.getAttribute('src') || v.src || 'x') : '';
                            }");
                            if (!string.IsNullOrEmpty(has))
                                break;
                        }
                        catch { break; }
                        await Task.Delay(2000);
                    }
                    return (await page.ContentAsync(), null);
                }

                string content = await page.ContentAsync();

                // poll den khi tile co title + href that on dinh
                try
                {
                    int lastT = -1, lastL = -1, stable = 0;
                    for (int i = 0; i < 15; i++)
                    {
                        int v = 0;
                        try
                        {
                            // title that su (ma phim ~10-25 ky tu) chua tinh:
                            // chi dem tile co h2/h3 dai > 30 ky tu
                            v = await page.EvaluateAsync<int>(@"() => {
                                let titled = 0, linked = 0;
                                document.querySelectorAll('.card, .featured').forEach(d => {
                                    const a = d.querySelector('a[href^=""/vi/v/""], a[href^=""/en/v/""]');
                                    const href = a ? (a.getAttribute('href') || '') : '';
                                    if (href.length > 6) linked++;
                                    const h = d.querySelector('h2, h3');
                                    const t = ((h || {}).innerText || '').trim();
                                    if (t.length > 30) titled++;
                                });
                                return titled * 100000 + linked;
                            }");
                        }
                        catch { break; }
                        int t = v / 100000, l = v % 100000;
                        if ((t > 0 || l > 0) && t == lastT && l == lastL && ++stable >= 3)
                            break;
                        if (t != lastT || l != lastL)
                            stable = 0;
                        lastT = t; lastL = l;
                        await Task.Delay(2000);
                    }
                }
                catch { }

                string tiles = null;
                try
                {
                    tiles = await page.EvaluateAsync<string>(@"() => JSON.stringify(Array.from(document.querySelectorAll('.card, .featured')).map(d => {
                        const a = d.querySelector('a[href^=""/vi/v/""], a[href^=""/en/v/""]') || d.querySelector('a[href]');
                        const img = d.querySelector('img');
                        const h = d.querySelector('h2, h3');
                        const dur = d.querySelector('.card__dur, .featured__dur');
                        let p = '';
                        if (img) {
                            const ds = img.getAttribute('data-src') || '';
                            if (ds && !ds.startsWith('data:')) p = ds;
                            else if (img.src && !img.src.startsWith('data:')) p = img.src;
                        }
                        return { u: a ? a.getAttribute('href') : '', t: h ? (h.innerText || '').trim() : (d.innerText || '').trim().split('\n')[0] || '', p: p, d: dur ? (dur.innerText || '').trim() : '' };
                    }))");
                }
                catch { }

                return (content, tiles);
            }
        }
        catch
        {
            return (null, null);
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("123av")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, int page = 1, int p = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // app co the gui page/p thay vi pg -> lay max de khong bao gio dung o trang 1
        pg = Math.Max(pg, Math.Max(page, p));
        if (pg < 1)
            pg = 1;

        async Task<CacheResult<List<PlaylistItem>>> GetPageAsync(string s, string cc, int page, bool allowRefresh)
        {
            return await InvokeCacheResult(ipkey($"av123:{s}:{cc}:{page}"), 10, jsonContext.ListPlaylistItem, async e =>
            {
                List<PlaylistItem> playlists = null;

                string pageUrl = Av123To.Uri(init.host, s, cc, page);
                var (_, tilesJson) = await PageFetchAsync(pageUrl, true);
                if (!string.IsNullOrEmpty(tilesJson))
                    playlists = Av123To.Playlist("123av/vidosik", tilesJson);

                if (playlists == null || playlists.Count == 0)
                    return e.Fail("playlists", refresh_proxy: allowRefresh && string.IsNullOrEmpty(s));

                return e.Success(playlists);
            });
        }

        var cache = await GetPageAsync(search, c, pg, true);

        if (cache.IsSuccess)
        {
            string nsearch = search, nc = c;
            int npg = pg + 1;
            _ = Task.Run(async () =>
            {
                try { await GetPageAsync(nsearch, nc, npg, false); }
                catch { }
            });
        }

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, Av123To.Menu(host));
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri, bool forceFresh = false)
    {
        // chuan hoa uri (trailing slash) de khong resolve trung lap:
        // app luc gui co / luc khong -> 2 key khac nhau -> resolve lai
        // full browser moi lan bam play -> timeout -> interrupt vong lap
        uri = (uri ?? "").Trim().TrimEnd('/');
        string memKey = ipkey($"av123:view:{uri}");
        if (!forceFresh && hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        int fi = pageUrl.IndexOf('#');
        if (fi >= 0)
            pageUrl = pageUrl.Substring(0, fi);
        if (pageUrl.StartsWith("/"))
            pageUrl = "https://123av.com" + pageUrl;

        var links = new Dictionary<string, string>();

        // mp4 bkcdn tren trang la QUANG CAO -> bo. Flow that:
        // iframe javplayer.cc/e/{hash} -> GET /stream?id={hash} -> m3u8
        var (content, _) = await PageFetchAsync(pageUrl, false);
        if (string.IsNullOrEmpty(content))
            return null;

        var (hash, poster) = Av123To.EmbedInfo(content);
        if (string.IsNullOrEmpty(hash))
            return null;

        string streamJson = await Av123To.CurlGet(Av123To.StreamJsonUrl(hash, poster), "https://javplayer.cc/e/" + hash);
        var (master, _) = Av123To.ParseStreamJson(streamJson);
        if (string.IsNullOrEmpty(master))
            return null;

        links.TryAdd("HLS", master);

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("123av/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/123av/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("123av/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var swv = System.Diagnostics.Stopwatch.StartNew();
        var links = await ResolveLinksAsync(uri);
        string link = null;
        if (links != null && !string.IsNullOrEmpty(q))
            links.TryGetValue(q, out link);
        if (string.IsNullOrEmpty(link))
            links = await ResolveLinksAsync(uri, forceFresh: true);
        if (string.IsNullOrEmpty(link) && links != null && !string.IsNullOrEmpty(q))
            links.TryGetValue(q, out link);
        if (string.IsNullOrEmpty(link) && links != null)
            link = links.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
        swv.Stop();
        try { System.IO.File.AppendAllText("/tmp/av123_slow.log", $"{DateTime.UtcNow:HH:mm:ss} video {swv.Elapsed.TotalSeconds:F1}s links={(links?.Count ?? 0)} q={q} hit={(!string.IsNullOrEmpty(link)).ToString().ToLower()}\n"); } catch { }
        if (string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", Av123To.ChromeUA),
            ("Referer", "https://123av.com/"),
            ("Origin", "https://123av.com")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }
}
