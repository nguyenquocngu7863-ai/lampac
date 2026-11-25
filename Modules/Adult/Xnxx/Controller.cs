using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Net.Http;
using System.Threading.Tasks;

namespace Xnxx;

public class XnxxController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public XnxxController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache]
    [Route("xnx")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

    rhubFallback:
        var cache = await InvokeCacheResult($"xnx:list:{search}:{c}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(XnxxTo.Uri(init.host, search, c, pg), span =>
            {
                playlists = XnxxTo.Playlist("xnx/vidosik", span);
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
            string.IsNullOrEmpty(search) ? XnxxTo.Menu(host) : null
        );
    }

    #region popup (giữ thẻ -> /xnx/genres?uri= + "Похожие видео")
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("xnx/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return EmptyPopup();

        string pageUrl = XnxxTo.DetailUrl(init.host, uri);
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

        var chips = XnxxTo.Chips(html);
        var rel = XnxxTo.PlaylistRelated("xnx/vidosik", init.host, html, uri);

        if (chips.Count > 0 || rel.Count > 0)
            _infoCache[pageUrl] = (DateTime.UtcNow, chips, rel);

        return InfoResult(chips, rel, uri);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips,
        List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        foreach (var def in new[] {
            ("model", "Diễn viên"),
            ("tag", "Thẻ") })
        {
            var items = chips?.Where(x => x.kind == def.Item1).Select(x => new
            {
                title = x.name,
                url = $"{host}/xnx?c={def.Item1}/{HttpUtility.UrlEncode(x.slug)}"
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
                        url = $"{host}/xnx?rel={HttpUtility.UrlEncode(uri)}"
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

        string pageUrl = XnxxTo.DetailUrl(init.host, rel);
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

            playlists = XnxxTo.PlaylistRelated("xnx/vidosik", init.host, html, rel);
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
    [Route("xnx/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"xnxx:view:{uri}", 20, jsonContext.StreamItem, async e =>
        {
            string url = XnxxTo.StreamLinksUri(init.host, uri);
            if (url == null)
                return e.Fail("uri");

            StreamItem stream_links = null;

            await httpHydra.GetSpan(url, span =>
            {
                stream_links = XnxxTo.StreamLinks(span, "xnx/vidosik");
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
}
