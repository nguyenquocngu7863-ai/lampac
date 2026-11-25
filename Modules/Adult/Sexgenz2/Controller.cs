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
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace Sexgenz2;

public class Sexgenz2Controller : BaseSisiController
{
    public Sexgenz2Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache]
    [Route("sgz2")]
    async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

    rhubFallback:
        var cache = await InvokeCacheResult($"sexgenz2:v3:{search}:{sort}:{c}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            string ehost = await goHost(init.host, proxy);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(Sexgenz2To.Uri(ehost, search, sort, c, pg), span =>
            {
                playlists = Sexgenz2To.Playlist("sgz2/vidosik", span);
            },
            addheaders: HeadersModel.Init(
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-user", "?1"),
                ("upgrade-insecure-requests", "1")
            ));

            if (playlists == null)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache,
            string.IsNullOrEmpty(search) ? Sexgenz2To.Menu(host, sort, c) : null
        );
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sgz2/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string decodedUri = WebUtility.UrlDecode(uri);
        if (string.IsNullOrEmpty(decodedUri)) decodedUri = uri;
        if (decodedUri.StartsWith("/"))
            decodedUri = "https://x.sexgenz.blog" + decodedUri;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"sexgenz2:v3:view:{decodedUri}"), 20, jsonContext.StreamItem, async e =>
        {
            string ehost = await goHost(init.host);

            var headers = HeadersModel.Init(
                ("referer", $"{ehost}/"),
                ("sec-fetch-dest", "document"),
                ("sec-fetch-mode", "navigate"),
                ("sec-fetch-site", "same-origin")
            );

            string detailHtml = null;
            await httpHydra.GetSpan(init.cors(decodedUri, headers, requestInfo), span =>
            {
                detailHtml = new string(span.ToArray());
            }, addheaders: headers);

            var stream_links = Sexgenz2To.StreamLinks(detailHtml, ehost);

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

    #region popup (giữ thẻ -> menu /genres?uri= + "Phim liên quan")
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("sgz2/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : "https://x.sexgenz.blog/" + uri.Trim('/');

        if (!IsSiteUrl(pageUrl))
            return Json(new { items = new object[0], groups = new object[0] });

        if (_infoCache.TryGetValue(pageUrl, out var hit)
            && DateTime.UtcNow - hit.at < InfoTtl && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.rel, uri);
        }

        string html = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            html = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("sec-fetch-dest", "document"),
            ("sec-fetch-mode", "navigate"),
            ("sec-fetch-site", "same-origin"),
            ("sec-fetch-user", "?1"),
            ("upgrade-insecure-requests", "1")
        ));

        if (string.IsNullOrEmpty(html))
            return Json(new { items = new object[0], groups = new object[0] });

        var chips = Sexgenz2To.Chips(html);
        var rel = Sexgenz2To.PlaylistRelated("sgz2/vidosik", html, pageUrl);

        if (chips.Count > 0 || rel.Count > 0)
            _infoCache[pageUrl] = (DateTime.UtcNow, chips, rel);

        return InfoResult(chips, rel, uri);
    }

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips,
        List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in new[] { "cat", "country" })
        {
            var items = chips?.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/sgz2?c={kind}/{HttpUtility.UrlEncode(x.slug)}"
            }).ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title = kind == "cat" ? "Thể loại" : "Quốc gia", items });
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
                        title = "Phim liên quan",
                        plain = true,
                        url = $"{host}/sgz2?rel={HttpUtility.UrlEncode(uri)}"
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

        string pageUrl = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? rel : "https://x.sexgenz.blog/" + rel.Trim('/');

        if (!IsSiteUrl(pageUrl))
            return OnError("stream_links");

        string html = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            html = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("sec-fetch-dest", "document"),
            ("sec-fetch-mode", "navigate"),
            ("sec-fetch-site", "same-origin"),
            ("sec-fetch-user", "?1"),
            ("upgrade-insecure-requests", "1")
        ));

        if (string.IsNullOrEmpty(html))
            return OnError("playlists");

        var playlists = Sexgenz2To.PlaylistRelated("sgz2/vidosik", html, pageUrl);
        if (playlists == null || playlists.Count == 0)
            return EmptyResult1();

        return PlaylistResult(playlists, false, null, total_pages: 1);
    }

    // SISI hiểu total_pages=0 là "còn nữa" -> route 1 trang phải trả 1 kèm list rỗng
    ActionResult EmptyResult1()
    {
        Response.Headers.CacheControl = "no-cache";
        return Content("{\"count\":0,\"total_pages\":1,\"menu\":[],\"list\":[]}",
            "application/json; charset=utf-8");
    }

    static bool IsSiteUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            return false;

        string h = u.Host.ToLowerInvariant();
        return h == "sexgenz.blog" || h.EndsWith(".sexgenz.blog")
            || h == "sexgenz.xyz" || h.EndsWith(".sexgenz.xyz")
            || h == "clipchat.blog" || h.EndsWith(".clipchat.blog");
    }
    #endregion

    public static ValueTask<string> goHost(string host, WebProxy proxy = null)
    {
        return ValueTask.FromResult(host);
    }
}
