using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace Sexgenz2;

public class Sexgenz2Controller : BaseSisiController
{
    public Sexgenz2Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache]
    [Route("sgz2")]
    async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

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

    public static ValueTask<string> goHost(string host, WebProxy proxy = null)
    {
        return ValueTask.FromResult(host);
    }
}
