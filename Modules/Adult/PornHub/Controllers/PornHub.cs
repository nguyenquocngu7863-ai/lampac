using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace PornHub;

public class PornHubController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client(useCookies: false);

    public PornHubController() : base(ModInit.conf.PornHub)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache(11)]
    [Route("phub"), Route("phubgay"), Route("phubsml")]
    async public Task<ActionResult> Index(string search, string model, string sort, int c, int pg = 1, string rel = null)
    {
        // "Video liên quan" mở từ popup long-press: ?rel=<uri detail>
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string plugin = Regex.Match(HttpContext.Request.Path.Value, "^/([a-z]+)").Groups[1].Value;

        SemaphorManager semaphore = null;
        string semaphoreKey = $"{plugin}:list:{search}:{model}:{sort}:{c}:{pg}";

        PlaylistAndPage cache = null;
        HybridCacheEntry<PlaylistAndPage> entryCache;

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

            entryCache = await hybridCache.EntryAsync(semaphoreKey, jsonContext.PlaylistAndPage);

            // fallback cache
            if (!entryCache.success)
            {
                string userKey = headerKeys(semaphoreKey, "accept");

                bool next = rch == null;
                if (!next)
                {
                    // user cache разделенный по ip
                    entryCache = await hybridCache.EntryAsync(userKey, jsonContext.PlaylistAndPage);
                    if (entryCache.success)
                        StatiCacheDisabled = true;

                    next = !entryCache.success;
                }

                if (next)
                {
                    string uri = PornHubTo.Uri(init.host, plugin, search, model, sort, c, null, pg);

                    await httpHydra.GetSpan(uri, span =>
                    {
                        cache = new PlaylistAndPage(
                            PornHubTo.Pages(span),
                            PornHubTo.Playlist("phub/vidosik", "phub", span, IsModel_page: !string.IsNullOrEmpty(model))
                        );
                    });

                    if (cache?.playlists == null || cache.playlists.Count == 0)
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

                    hybridCache.Set(memKey, cache, cacheTime(10));
                }
            }
        }
        finally
        {
            semaphore?.Release();
        }

        if (cache == null)
            cache = entryCache.value;

        return PlaylistResult(
            cache.playlists,
            entryCache.singleCache,
            string.IsNullOrEmpty(model) ? PornHubTo.Menu(host, plugin, search, sort, c) : null,
            total_pages: cache.total_pages
        );
    }


    [HttpGet, Staticache(manually: true)]
    [Route("phub/vidosik")]
    async public Task<ActionResult> Vidosik(string vkey, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"phub:vidosik:{vkey}"), 20, jsonContext.StreamItem, async e =>
        {
            string url = PornHubTo.StreamLinksUri(init.host, vkey);
            if (url == null)
                return e.Fail("vkey");

            StreamItem stream_links = null;

            await httpHydra.GetSpan(url, span =>
            {
                stream_links = PornHubTo.StreamLinks(span, "phub/vidosik", "phub");
            });

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

    #region info (popup long-press) — chip diễn viên / thể loại / thẻ + "Video liên quan"
    static readonly Dictionary<string, (DateTime at, List<PornHubTo.InfoChip> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    static readonly Dictionary<string, string> _catNames = new();
    static readonly Dictionary<string, int> _catSlugs = new();
    static DateTime _catAt = DateTime.MinValue;

    static bool IsPornHub(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u)
           && (u.Host.EndsWith("pornhub.com", StringComparison.OrdinalIgnoreCase)
               || u.Host.EndsWith("phncdn.com", StringComparison.OrdinalIgnoreCase));

    ActionResult EmptyPopup()
        => Json(new { items = new object[0], groups = new object[0] });

    ActionResult EmptyRelated()
        => Json(new { total_pages = 1, list = new List<PlaylistItem>() });

    [HttpGet]
    [Route("phub/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || await IsRequestBlocked(rch: true))
            return EmptyPopup();

        string pageUrl = PageUrl(uri);
        if (pageUrl == null || !IsPornHub(pageUrl))
            return EmptyPopup();

        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(pageUrl, out var hit)
                && DateTime.UtcNow - hit.at < InfoTtl && hit.chips != null)
            {
                return InfoResult(hit.chips, hit.rel, pageUrl);
            }
        }

        string html = null;
        await httpHydra.GetSpan(pageUrl, span => html = span.ToString());

        if (string.IsNullOrEmpty(html))
            return EmptyPopup();

        await EnsureCategories();

        List<PornHubTo.InfoChip> chips;
        lock (_catNames)
            chips = PornHubTo.Chips(html, _catNames, _catSlugs);

        var rel = PornHubTo.Related(html, "phub/vidosik", "phub");

        if (chips.Count > 0 || (rel?.Count ?? 0) > 0)
        {
            lock (_infoCache)
            {
                _infoCache[pageUrl] = (DateTime.UtcNow, chips, rel);
            }
        }

        return InfoResult(chips, rel, pageUrl);
    }

    string PageUrl(string uri)
    {
        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return uri;

        try
        {
            return $"{init.host.TrimEnd('/')}/{uri.TrimStart('/')}";
        }
        catch
        {
            return null;
        }
    }

    // Bảng id -> tên tiếng Anh: trang rt. trả chữ Nga, menu không được có Cyrillic
    async Task EnsureCategories()
    {
        lock (_catNames)
        {
            if (_catNames.Count > 0 && DateTime.UtcNow - _catAt < TimeSpan.FromHours(12))
                return;
        }

        string html = null;
        try
        {
            await httpHydra.GetSpan($"{init.host.TrimEnd('/')}/categories", span => html = span.ToString());
        }
        catch
        {
            return;
        }

        if (string.IsNullOrEmpty(html))
            return;

        lock (_catNames)
        {
            PornHubTo.CategoryNames(html, _catNames, _catSlugs);
            _catAt = DateTime.UtcNow;
        }
    }

    ActionResult InfoResult(List<PornHubTo.InfoChip> chips, List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        foreach (var def in new[] {
            ("pornstar", "Diễn viên"),
            ("model", "Người đăng"),
            ("channel", "Kênh"),
            ("cat", "Thể loại"),
            ("tag", "Thẻ") })
        {
            var items = chips?.Where(x => x.kind == def.Item1)
                .Select(x => new { title = x.name, url = ChipUrl(def.Item1, x.slug) })
                .ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title = def.Item2, items });
        }

        if ((rel?.Count ?? 0) > 0)
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
                        url = $"{host}/phub?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    string ChipUrl(string kind, string slug)
    {
        switch (kind)
        {
            case "pornstar":
                return $"{host}/phub?model={HttpUtility.UrlEncode("pornstar/" + slug)}";

            case "channel":
                return $"{host}/phub?model={HttpUtility.UrlEncode("channels/" + slug)}";

            case "cat":
                return $"{host}/phub?c={slug}";

            case "tag":
                return $"{host}/phub?search={HttpUtility.UrlEncode(slug)}";

            default:
                return $"{host}/phub?model={HttpUtility.UrlEncode(slug)}";
        }
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1 || await IsRequestBlocked(rch: true))
            return EmptyRelated();

        string pageUrl = PageUrl(rel);
        if (pageUrl == null || !IsPornHub(pageUrl))
            return EmptyRelated();

        List<PlaylistItem> playlists = null;

        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(pageUrl, out var hit) && DateTime.UtcNow - hit.at < InfoTtl)
                playlists = hit.rel;
        }

        if (playlists == null)
        {
            string html = null;
            await httpHydra.GetSpan(pageUrl, span => html = span.ToString());
            playlists = PornHubTo.Related(html, "phub/vidosik", "phub");
        }

        return Json(new { total_pages = 1, list = playlists ?? new List<PlaylistItem>() });
    }
    #endregion
}
