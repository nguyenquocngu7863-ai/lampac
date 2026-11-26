using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Xvideos;

public class XvideosController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public XvideosController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache]
    [Route("xds"), Route("xdsgay"), Route("xdssml")]
    async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1, string t = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string plugin = Regex.Match(HttpContext.Request.Path.Value, "^/([a-z]+)").Groups[1].Value;

        // danh sach "Video lien quan" (chip popup)
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedAsync(rel, pg, plugin);

        if (!string.IsNullOrWhiteSpace(t) && !Regex.IsMatch(t, "^[A-Za-z0-9_-]{1,80}$"))
            t = null;

    rhubFallback:
        var cache = await InvokeCacheResult($"{plugin}:list:{search}:{sort}:{c}:{t}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            string listUrl = string.IsNullOrWhiteSpace(t)
                ? XvideosTo.Uri(init.host, plugin, search, sort, c, pg)
                : XvideosTo.TagUri(init.host, t, pg);

            await httpHydra.GetSpan(listUrl, span =>
            {
                playlists = XvideosTo.Playlist("xds/vidosik", $"{plugin}/stars", span);
            }, newheaders: HeadersModel.Init(("Accept-Language", "en-US,en;q=0.9")));

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache,
            string.IsNullOrEmpty(search) && string.IsNullOrEmpty(t) ? XvideosTo.Menu(host, plugin, sort, c) : null
        );
    }

    [HttpGet, Staticache]
    [Route("xds/stars"), Route("xdsgay/stars"), Route("xdssml/stars")]
    async public Task<ActionResult> Pornstars(string uri, string sort, int pg = 0)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg != 0)
            pg = pg - 1;

        string plugin = Regex.Match(HttpContext.Request.Path.Value, "^/([a-z]+)").Groups[1].Value;

    rhubFallback:
        var cache = await InvokeCacheResult($"{plugin}:stars:{uri}:{sort}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            var playlists = await XvideosTo.Pornstars("xds/vidosik", $"{plugin}/stars", init.host, plugin, uri, sort, pg,
                url => httpHydra.Get<PornstarsRoot>(url)
            );

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("xds/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"xvideos:view:{uri}", 20, jsonContext.StreamItem, async e =>
        {
            string url = XvideosTo.StreamLinksUri($"{host}/xds/stars", init.host, uri);
            if (url == null)
                return e.Fail("uri");

            StreamItem stream_links = null;

            await httpHydra.GetSpan(url, span =>
            {
                stream_links = XvideosTo.StreamLinks(span, "xds/vidosik", $"{host}/xds/stars");
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

    #region info (popup long-press) — dien vien / kenh / the / video lien quan
    static readonly Dictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, bool hasRel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("xds/genres"), Route("xdsgay/genres"), Route("xdssml/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = Json(new { items = new object[0], groups = new object[0] });

        string plugin = Regex.Match(HttpContext.Request.Path.Value, "^/([a-z]+)").Groups[1].Value;

        string key = NormalizeDetail(uri);
        if (key == null)
            return empty;

        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.at < InfoTtl)
                return InfoResult(hit.chips, hit.hasRel, uri, plugin);
        }

        string html = null;

        await httpHydra.GetSpan($"{init.host}/{key}", span =>
        {
            html = span.ToString();
        }, newheaders: HeadersModel.Init(("Accept-Language", "en-US,en;q=0.9")));

        var chips = XvideosTo.Chips(html ?? "");
        if (chips.Count == 0)
            return empty;

        var info = (DateTime.UtcNow, chips, XvideosTo.HasRelated(html ?? ""));

        lock (_infoCache)
        {
            _infoCache[key] = info;
        }

        return InfoResult(info.chips, info.Item3, uri, plugin);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, bool hasRel, string uri, string plugin)
    {
        var groups = new List<object>();

        foreach (string kind in new[] { "star", "channel", "tag" })
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = kind == "tag"
                    ? $"{host}/{plugin}?t={HttpUtility.UrlEncode(x.slug)}"
                    : $"{host}/{plugin}/stars?uri=" + HttpUtility.UrlEncode((kind == "star" ? "pornstars/" : "channels/") + x.slug)
            }).ToList();

            if (items.Count == 0)
                continue;

            groups.Add(new
            {
                title = kind == "star" ? "Diễn viên" : kind == "channel" ? "Kênh" : "Thẻ",
                items
            });
        }

        if (hasRel)
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
                        url = $"{host}/{plugin}?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedAsync(string rel, int pg, string plugin)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string key = NormalizeDetail(rel);
        if (key == null)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string html = null;

        await httpHydra.GetSpan($"{init.host}/{key}", span =>
        {
            html = span.ToString();
        }, newheaders: HeadersModel.Init(("Accept-Language", "en-US,en;q=0.9")));

        var playlists = XvideosTo.RelatedList(html ?? "", $"{plugin}/vidosik", $"{host}/{plugin}/stars");
        if (playlists == null || playlists.Count == 0)
            return OnError("playlists", refresh_proxy: true);

        return PlaylistResult(playlists, false, null, total_pages: 1);
    }

    // chi nhan detail page cua site (video.xxx): bo host la, giu path de chong SSRF
    static string NormalizeDetail(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string u = uri.Trim();

        if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var m = Regex.Match(u, "^https?://[^/]+(/.*)$", RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;

            u = m.Groups[1].Value;
        }

        u = u.TrimStart('/');

        if (!u.StartsWith("video.", StringComparison.OrdinalIgnoreCase) || u.Contains("..") || u.Contains('?'))
            return null;

        return u;
    }
    #endregion
}
