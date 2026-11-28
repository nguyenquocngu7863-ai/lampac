using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Net.Http;
using System.Threading.Tasks;

namespace Tizam;

public class TizamController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public TizamController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);
        };
    }

    [HttpGet, Staticache]
    [Route("tizam")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (!string.IsNullOrEmpty(search))
            return OnError("no search", false);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

    rhubFallback:
        var cache = await InvokeCacheResult($"tizam:{c}:{pg}", 60, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(TizamTo.Uri(init.host, c, pg), span =>
            {
                playlists = TizamTo.Playlist(init.host, span);
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache);
    }

    #region popup (giữ thẻ -> /tizam/genres?uri= + "Похожие фильмы")
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("tizam/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return EmptyPopup();

        string pageUrl = TizamTo.DetailUrl(init.host, uri);
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

        var chips = TizamTo.Chips(html);
        var rel = TizamTo.PlaylistRelated(init.host, html, uri);

        if (chips.Count > 0 || rel.Count > 0)
            _infoCache[pageUrl] = (DateTime.UtcNow, chips, rel);

        return InfoResult(chips, rel, uri);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips,
        List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        foreach (var def in new[] {
            ("actress", "Diễn viên"),
            ("cat", "Thể loại"),
            ("tag", "Thẻ") })
        {
            var items = chips?.Where(x => x.kind == def.Item1).Select(x => new
            {
                title = x.name,
                url = $"{host}/tizam?c={def.Item1}/{HttpUtility.UrlEncode(x.slug)}"
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
                        url = $"{host}/tizam?rel={HttpUtility.UrlEncode(uri)}"
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

        string pageUrl = TizamTo.DetailUrl(init.host, rel);
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

            playlists = TizamTo.PlaylistRelated(init.host, html, rel);
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
    [Route("tizam/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"tizam:view:{uri}", 180, jsonContext.StreamItem, async e =>
        {
            var stream = await TizamTo.Stream(httpHydra, init.host, uri);
            if (stream == null)
                return e.Fail("location", refresh_proxy: true);

            return e.Success(stream);
        });

        if (related)
            return PlaylistResult(cache.Value?.recomends, cache.ISingleCache, null, total_pages: 1);

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return OnResult(cache);
    }
}
