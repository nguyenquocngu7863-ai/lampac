using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Net.Http;
using System.Web;
using System.Threading.Tasks;

namespace HQporner;

public class HQpornerController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public HQpornerController() : base(ModInit.conf) { }

    [HttpGet, Staticache]
    [Route("hqr")]
    async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

    rhubFallback:
        var cache = await InvokeCacheResult($"hqr:{search}:{sort}:{c}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(HQpornerTo.Uri(init.host, search, sort, c, pg), span =>
            {
                playlists = HQpornerTo.Playlist("hqr/vidosik", span);
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache,
            string.IsNullOrEmpty(search) ? HQpornerTo.Menu(host, sort, c) : null
        );
    }

    #region popup (giữ thẻ -> /hqr/genres?uri= + "Similar HD porn")
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("hqr/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return EmptyPopup();

        string pageUrl = HQpornerTo.DetailUrl(uri);
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
        },
        addheaders: HeadersModel.Init(("referer", HQpornerTo.DesktopHost + "/")));

        if (string.IsNullOrEmpty(html))
            return EmptyPopup();

        var chips = HQpornerTo.Chips(html);
        var rel = HQpornerTo.PlaylistRelated("hqr/vidosik", html, uri);

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
            ("cat", "Thể loại") })
        {
            var items = chips?.Where(x => x.kind == def.Item1).Select(x => new
            {
                title = x.name,
                url = $"{host}/hqr?c={def.Item1}/{HttpUtility.UrlEncode(x.slug)}"
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
                        url = $"{host}/hqr?rel={HttpUtility.UrlEncode(uri)}"
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

        string pageUrl = HQpornerTo.DetailUrl(rel);
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
            },
            addheaders: HeadersModel.Init(("referer", HQpornerTo.DesktopHost + "/")));

            if (string.IsNullOrEmpty(html))
                return EmptyJson();

            playlists = HQpornerTo.PlaylistRelated("hqr/vidosik", html, rel);
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
    [Route("hqr/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"HQporner:view:{uri}"), 5, jsonContext.DictionaryStringString, async e =>
        {
            var stream_links = await HQpornerTo.StreamLinks(httpHydra, init.host, uri);

            if (stream_links == null || stream_links.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(stream_links);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return OnResult(cache);
    }
}
