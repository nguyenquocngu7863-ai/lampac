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
using System.Threading.Tasks;
using System.Web;

namespace Po85;

public class Po85Controller : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public Po85Controller() : base(ModInit.conf) { }

        [HttpGet, Staticache(manually: true)]
        [Route("po85")]
        async public Task<ActionResult> Index(string search, string sort, string c, string t, int pg = 1)
        {
            if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"po85:{search}:{sort}:{c}:{t}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(Po85To.Uri(init.host, search, sort, c, t, pg), span =>
            {
                playlists = Po85To.Playlist("po85/vidosik", span);
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache,
            Po85To.Menu(host, search, sort, c, t)
        );
    }

    /// <summary>
    /// Resolve link upstream (get_file / signed CDN) theo uri phim.
    /// Link upstream het han nhanh theo phien nen khong duoc nhung thang
    /// vao URL tra ve (bookmark/history mo lai se chet) — moi lan mo lai
    /// phai resolve moi qua ham nay (co hybridCache 20p).
    /// Tra (null, false) khi that bai hoan toan.
    /// </summary>
    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"po85:view:{uri}";

        if (rch?.enable != true)
        {
            semaphore ??= new SemaphorManager(semaphoreKey, System.TimeSpan.FromSeconds(30));
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return (null, false);
        }

        try
        {
            string memKey = ipkey(semaphoreKey);
            if (!hybridCache.TryGetValue(memKey, out (Dictionary<string, string> links, bool userch) cache))
            {
                if (init.httpversion == 1)
                    httpHydra.RegisterHttp(httpClient);

                string url = Po85To.StreamLinksUri(uri);

                // Bookmark/history chi luu id so (vd 20818), URL /v/{id}/ khong slug
                // tra 404 — mo trang embed de lay URL day du co slug roi di tiep.
                if (!string.IsNullOrEmpty(url) && System.Text.RegularExpressions.Regex.IsMatch(url, @"^[0-9]+$"))
                {
                    string full = null;
                    await httpHydra.GetSpan($"https://www.85po.com/embed/{url}/", span =>
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(span.ToString(), $@"/v/{url}/[^""']+/");
                        if (m.Success)
                            full = "https://www.85po.com" + m.Value;
                    });
                    url = full;
                }

                if (url == null)
                    return (null, false);

                // ban /vi/: dropdown nhu cu + flashvars co prefix /vi/get_file/
                // (chi URL /vi/ moi mo duoc file 2160p)
                string viPage = Po85To.ViPage(url);
                string uhdFile = null;

                await httpHydra.GetSpan(viPage, span =>
                {
                    cache.links = Po85To.StreamLinks(span);
                    uhdFile = Po85To.ParseUhd(span);
                });

                if (cache.links == null || cache.links.Count == 0)
                    return (null, false);

                // 4K: nho node resolver mo bang Chrome that, lay signed CDN URL
                if (!string.IsNullOrEmpty(uhdFile))
                {
                    string signed = await Po85To.ResolveUhd(viPage, uhdFile);
                    if (!string.IsNullOrEmpty(signed))
                    {
                        var with4k = new Dictionary<string, string>(cache.links.Count + 1);
                        foreach (var kv in cache.links)
                            with4k.TryAdd(kv.Key, kv.Value);
                        with4k.TryAdd("4K", signed);
                        cache.links = with4k;
                    }
                }

                proxyManager?.Success();

                cache.userch = rch?.enable == true;
                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            return (cache.links, cache.userch);
        }
        finally
        {
            semaphore?.Release();
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("po85/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    reset:
        var (links, userch) = await ResolveLinksAsync(uri);

        try { System.IO.File.AppendAllText("/root/lampac/data/po85_dbg.log", $"{DateTime.Now:HH:mm:ss} VIDOSIK uri={uri} n={links?.Count} keys={string.Join("|", links?.Keys ?? Enumerable.Empty<string>())}\n"); } catch { }

        if (links == null || links.Count == 0)
        {
            if (IsRhubFallback())
                goto reset;

            return OnError("stream_links", refresh_proxy: true);
        }

        if (userch)
            return OnResult(links);

        // URL strem giu theo uri+label (ben vung cho bookmark/history),
        // moi lan mo lai Strem se resolve link upstream moi.
        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/po85/strem?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }


    [HttpGet]
    [Route("po85/strem")]
    async public Task<ActionResult> Strem(string link, string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (rch?.enable == true && 484 > rch.InfoConnected()?.apkVersion)
        {
            rch.Disabled(); // на версиях ниже java.lang.OutOfMemoryError
            if (!init.rhub_fallback)
                return OnError("apkVersion", false);
        }

        // URL dang ben vung (bookmark/history): resolve lai link upstream moi
        // theo uri+label thay vi dung link cu da het han.
        if (!string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(q))
        {
            var (links, _) = await ResolveLinksAsync(uri);
            if (links == null || !links.TryGetValue(q, out link) || string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        try { System.IO.File.AppendAllText("/root/lampac/data/po85_dbg.log", $"{DateTime.Now:HH:mm:ss} STREM uri={uri} q={q} link={(link ?? "").Substring(0, Math.Min(90, (link ?? "").Length))} qs={HttpContext?.Request?.QueryString}\n"); } catch { }

        SemaphorManager semaphore = null;
        string semaphoreKey = $"po85:strem:{link}";

        if (rch?.enable != true)
        {
            semaphore ??= new SemaphorManager(semaphoreKey, System.TimeSpan.FromSeconds(30));
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return OnError();
        }

        try
        {
            string memKey = ipkey(semaphoreKey);
            if (!hybridCache.TryGetValue(memKey, out string location))
            {
                var headers = httpHeaders(init, HeadersModel.Init(
                    ("sec-fetch-dest", "document"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "none"),
                    ("referer", "https://www.85po.com/")
                ));

                if (rch?.enable == true)
                {
                    var res = await rch.Headers(init.cors(link, headers, requestInfo), null, headers);
                    location = res.currentUrl;
                }
                else
                {
                    location = await Http.GetLocation(init.cors(link, headers, requestInfo), timeoutSeconds: init.httptimeout, httpversion: init.httpversion, proxy: proxy, headers: headers);
                }

                if (string.IsNullOrEmpty(location) || link == location)
                {
                    // 85po get_file tra file truc tiep (206), khong redirect:
                    // proxy thang link goc kem Referer thay vi bao loi
                    proxyManager?.Success();
                    var direct = httpHeaders(init, HeadersModel.Init(
                        ("referer", "https://www.85po.com/")
                    ));
                    hybridCache.Set(memKey, link, cacheTime(40));
                    return Redirect(HostStreamProxy(link, direct));
                }

                proxyManager?.Success();
                hybridCache.Set(memKey, location, cacheTime(40));
            }

            return Redirect(HostStreamProxy(location));
        }
        finally
        {
            semaphore?.Release();
        }
    }
}
