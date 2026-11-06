using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace Eporner;

public class EpornerController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public EpornerController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);

            // IsRequestBlocked can merge account/device Kit settings before
            // this callback. Keep Eporner on the local proxy policy regardless
            // of stale remote profiles that switch playback back to the CDN.
            const string browserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            init.kit = false;
            init.rhub = false;
            init.streamproxy = true;
            init.qualitys_proxy = false;
            init.url_reserve = false;
            init.headers_stream = HeadersModel.Init(
                ("User-Agent", browserUserAgent),
                ("Referer", "https://www.eporner.com/"),
                ("Origin", "https://www.eporner.com"),
                ("Accept", "video/webm,video/mp4,video/*;q=0.9,*/*;q=0.5"),
                ("Accept-Language", "en-US,en;q=0.9")
            ).ToDictionary();
        };
    }

    [HttpGet, Staticache(11)]
    [Route("epr")]
    async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1, string t = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        pg += 1;

        SemaphorManager semaphore = null;
        string semaphoreKey = $"epr:{search}:{sort}:{c}:{t}:{pg}";

        List<PlaylistItem> playlists = null;
        HybridCacheEntry<List<PlaylistItem>> entryCache;

        try
        {
        reset: // http запросы последовательно
            if (rch?.enable != true)
            {
                semaphore ??= new SemaphorManager(semaphoreKey, TimeSpan.FromSeconds(30));
                bool _acquired = await semaphore.WaitAsync();
                if (!_acquired)
                    return OnError();
            }

            entryCache = await hybridCache.EntryAsync(semaphoreKey, jsonContext.ListPlaylistItem);

            // fallback cache
            if (!entryCache.success)
            {
                string userKey = headerKeys(semaphoreKey, "accept");

                bool next = rch == null;
                if (!next)
                {
                    // user cache разделенный по ip
                    entryCache = await hybridCache.EntryAsync(userKey, jsonContext.ListPlaylistItem);
                    if (entryCache.success)
                        StatiCacheDisabled = true;
                    next = !entryCache.success;
                }

                if (next)
                {
                    string url = EpornerTo.Uri(init.host, search, sort, c, pg, t);

                    await httpHydra.GetSpan(url, span =>
                    {
                        playlists = EpornerTo.Playlist("epr/vidosik", span);
                    });

                    if (playlists == null || playlists.Count == 0)
                    {
                        if (IsRhubFallback())
                            goto reset;

                        return OnError("playlists", refresh_proxy: string.IsNullOrEmpty(search));
                    }

                    string memKey = semaphoreKey;

                    if (rch?.enable == true)
                    {
                        memKey = userKey;
                        StatiCacheDisabled = true;
                    }
                    else
                    {
                        proxyManager?.Success();
                    }

                    hybridCache.Set(memKey, playlists, cacheTime(10));
                }
            }
        }
        finally
        {
            semaphore?.Release();
        }

        if (playlists == null)
            playlists = entryCache.value;

        return PlaylistResult(
            playlists,
            entryCache.singleCache,
            EpornerTo.Menu(host, search, sort, c)
        );
    }

    [HttpGet, Staticache(manually: true)]
    [Route("epr/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !IsEpornerUri(uri))
            return EmptyGenres();

        if (_chips.TryGetValue(uri, out var hit)
            && DateTime.UtcNow - hit.at < TimeSpan.FromMinutes(360)
            && hit.chips.Count > 0)
        {
            return GenresResult(hit.chips, uri);
        }

        List<(string kind, string slug, string name)> chips = null;

        try
        {
            await httpHydra.GetSpan(PageUrl(uri), span =>
            {
                chips = EpornerTo.Chips(span);
            });
        }
        catch
        {
            return EmptyGenres();
        }

        if (chips == null || chips.Count == 0)
            return EmptyGenres();

        _chips[uri] = (DateTime.UtcNow, chips);

        return GenresResult(chips, uri);
    }

    #region popup
    static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string, (DateTime at, List<(string kind, string slug, string name)> chips)> _chips = new();

    static ActionResult EmptyGenres()
        => new JsonResult(new { items = new object[0], groups = new object[0] });

    string PageUrl(string uri)
        => uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : $"{init.host.TrimEnd('/')}/{uri.Trim('/')}";

    bool IsEpornerUri(string uri)
    {
        // đường dẫn tương đối lấy từ chính playlist (video-xxx/title/)
        if (!uri.Contains("://", StringComparison.Ordinal))
            return !uri.StartsWith("//", StringComparison.Ordinal);

        return System.Uri.TryCreate(uri, UriKind.Absolute, out var u)
            && u.Host.EndsWith("eporner.com", StringComparison.OrdinalIgnoreCase);
    }

    ActionResult GenresResult(List<(string kind, string slug, string name)> chips, string uri)
    {
        var groups = new List<object>(3);

        foreach (string kind in new[] { "cat", "tag" })
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = kind == "cat"
                    ? $"{host}/epr?c={HttpUtility.UrlEncode(x.slug)}"
                    : $"{host}/epr?t={HttpUtility.UrlEncode(x.slug)}"
            }).ToList();

            if (items.Count == 0)
                continue;

            groups.Add(new { title = kind == "cat" ? "Thể loại" : "Thẻ", items });
        }

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = $"{host}/epr?rel={HttpUtility.UrlEncode(uri)}"
                }
            }
        });

        return new JsonResult(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { count = 0, total_pages = 1, list = new List<PlaylistItem>() });

        if (!IsEpornerUri(rel))
            return Json(new { count = 0, total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult(ipkey($"eporner:rel:{rel}"), 180, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            try
            {
                await httpHydra.GetSpan(PageUrl(rel), span =>
                {
                    playlists = EpornerTo.Playlist("epr/vidosik", span);
                });
            }
            catch
            {
                return e.Success(new List<PlaylistItem>());
            }

            return e.Success(playlists ?? new List<PlaylistItem>());
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        if (cache.Value == null || cache.Value.Count == 0)
            return Json(new { count = 0, total_pages = 1, list = new List<PlaylistItem>() });

        return PlaylistResult(cache.Value, cache.ISingleCache,
            EpornerTo.Menu(host, null, null, null), total_pages: 1);
    }
    #endregion

    [HttpGet, Staticache(manually: true)]
    [Route("epr/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"eporner:view:{uri}"), 20, jsonContext.StreamItem, async e =>
        {
            var stream_links = await EpornerTo.StreamLinks(httpHydra, "epr/vidosik", init.host, uri);

            if (stream_links?.qualitys == null || stream_links.qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(stream_links);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (related)
            return PlaylistResult(cache.Value?.recomends, cache.ISingleCache, null, total_pages: 1);

        return OnResult(cache);
    }
}
