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

namespace Porntrex;

public class PorntrexController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public PorntrexController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("ptx")]
    async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"ptx:{search}:{sort}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(PorntrexTo.Uri(init.host, search, sort, c, pg), span =>
            {
                playlists = PorntrexTo.Playlist("ptx/vidosik", span);
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
            PorntrexTo.Menu(host, search, sort, c)
        );
    }

    #region popup (giữ thẻ -> /ptx/genres?uri= + "Related Videos")
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("ptx/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return EmptyPopup();

        string pageUrl = PorntrexTo.DetailUrl(init.host, uri);
        if (pageUrl == null)
            return EmptyPopup();

        if (_infoCache.TryGetValue(pageUrl, out var hit)
            && DateTime.UtcNow - hit.at < InfoTtl && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.rel, uri);
        }

        string html = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            html = span.ToString();
        });

        if (string.IsNullOrEmpty(html))
            return EmptyPopup();

        var chips = PorntrexTo.Chips(html);
        var rel = PorntrexTo.PlaylistRelated("ptx/vidosik", html, uri);

        if (chips.Count > 0 || rel.Count > 0)
            _infoCache[pageUrl] = (DateTime.UtcNow, chips, rel);

        return InfoResult(chips, rel, uri);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips,
        List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        foreach (var def in new[] {
            ("cat", "Thể loại"),
            ("model", "Diễn viên"),
            ("channel", "Kênh"),
            ("tag", "Thẻ") })
        {
            var items = chips?.Where(x => x.kind == def.Item1).Select(x => new
            {
                title = x.name,
                url = $"{host}/ptx?c={def.Item1}/{HttpUtility.UrlEncode(x.slug)}"
            }).ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title = def.Item2, items });
        }

        if (rel != null && rel.Count > 0)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new
                    {
                        title = "Video liên quan",
                        plain = true,
                        url = $"{host}/ptx?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = PorntrexTo.DetailUrl(init.host, rel);
        if (pageUrl == null)
            return EmptyJson();

        List<PlaylistItem> playlists = null;

        if (_infoCache.TryGetValue(pageUrl, out var hit) && DateTime.UtcNow - hit.at < InfoTtl)
            playlists = hit.rel;

        if (playlists == null)
        {
            string html = null;
            await httpHydra.GetSpan(pageUrl, span =>
            {
                html = span.ToString();
            });

            if (string.IsNullOrEmpty(html))
                return EmptyJson();

            playlists = PorntrexTo.PlaylistRelated("ptx/vidosik", html, rel);
        }

        if (playlists == null || playlists.Count == 0)
            return EmptyJson();

        return PlaylistResult(playlists, false, null, total_pages: 1);
    }

    ActionResult EmptyJson()
    {
        Response.Headers.CacheControl = "no-cache";
        return Content("{\"count\":0,\"total_pages\":1,\"menu\":[],\"list\":[]}",
            "application/json; charset=utf-8");
    }

    ActionResult EmptyPopup() => Json(new { items = new object[0], groups = new object[0] });
    #endregion

    [HttpGet, Staticache(manually: true)]
    [Route("ptx/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        SemaphorManager semaphore = null;
        string semaphoreKey = $"porntrex:view:{uri}";

    reset:
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
            if (!hybridCache.TryGetValue(memKey, out (Dictionary<string, string> links, bool userch) cache))
            {
                if (init.httpversion == 1)
                    httpHydra.RegisterHttp(httpClient);

                string url = PorntrexTo.StreamLinksUri(init.host, uri);
                if (url == null)
                    return OnError("uri");

                await httpHydra.GetSpan(url, span =>
                {
                    cache.links = PorntrexTo.StreamLinks(span);
                });

                if (cache.links == null || cache.links.Count == 0)
                {
                    if (IsRhubFallback())
                        goto reset;

                    return OnError("stream_links", refresh_proxy: true);
                }

                proxyManager?.Success();

                cache.userch = rch?.enable == true;
                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            if (cache.userch)
                return OnResult(cache.links);

            var recomends = await RecomendsAsync(uri);
            if (related)
                return PlaylistResult(recomends, false, null, total_pages: 1);

            return Json(new
            {
                qualitys = cache.links.ToDictionary(k => k.Key, v =>
                    $"{host}/ptx/strem?link={HttpUtility.UrlEncode(v.Value)}"),
                recomends
            });
        }
        finally
        {
            semaphore?.Release();
        }
    }

    // Khoi "Related Videos" cua trang detail -> playlist "tiep theo" trong player
    async Task<List<PlaylistItem>> RecomendsAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return new List<PlaylistItem>();

        string pageUrl = PorntrexTo.DetailUrl(init.host, uri);
        if (pageUrl == null)
            return new List<PlaylistItem>();

        string memKey = ipkey($"ptx:rec:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<PlaylistItem> hit) && hit != null)
            return hit;

        string html = null;
        try
        {
            await httpHydra.GetSpan(pageUrl, span => { html = span.ToString(); });
        }
        catch { }

        var rec = PorntrexTo.PlaylistRelated("ptx/vidosik", html ?? "", uri);

        foreach (var p in rec)
        {
            if (!string.IsNullOrEmpty(p.video) && !p.video.StartsWith("http"))
                p.video = host + "/" + p.video.TrimStart('/');
        }

        if (rec.Count > 0)
            hybridCache.Set(memKey, rec, cacheTime(20));

        return rec;
    }


    [HttpGet]
    [Route("ptx/strem")]
    async public Task<ActionResult> Strem(string link)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (rch?.enable == true && 484 > rch.InfoConnected()?.apkVersion)
        {
            rch.Disabled(); // на версиях ниже java.lang.OutOfMemoryError
            if (!init.rhub_fallback)
                return OnError("apkVersion", false);
        }

        SemaphorManager semaphore = null;
        string semaphoreKey = $"porntrex:strem:{link}";

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
                    ("sec-fetch-site", "none")
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
                    return OnError("location", refresh_proxy: true);

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
