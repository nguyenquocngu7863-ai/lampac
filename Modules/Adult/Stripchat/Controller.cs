using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace Stripchat;

public class StripchatController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public StripchatController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache]
    [Route("strp")]
    async public Task<ActionResult> Index(string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"stripchat:list:{sort}:{pg}", 2, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            var headers = HeadersModel.Init(
                ("accept", "application/json, text/plain, */*"),
                ("referer", "https://stripchat.com/"),
                ("origin", "https://stripchat.com"),
                ("accept-language", "en-US,en;q=0.9"),
                ("user-agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36")
            );

            await httpHydra.GetSpan(StripchatTo.Uri(init.host, sort, pg), span =>
            {
                playlists = StripchatTo.Playlist("strp/potok", span);
            }, addheaders: headers);

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache, StripchatTo.Menu(host, sort));
    }

    [HttpGet, Staticache(manually: true)]
    [Route("strp/potok")]
    async public Task<ActionResult> Vidosik(string hls, string baba)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        // hls is already provided from list - no second fetch needed
        if (!string.IsNullOrWhiteSpace(hls))
        {
            var dict = new Dictionary<string, string>() { ["auto"] = hls };
            return OnResult(dict);
        }

    rhubFallback:
        var cache = await InvokeCacheResult($"stripchat:stream:{baba}", 0, jsonContext.DictionaryStringString, async e =>
        {
            string url = StripchatTo.StreamLinksUri(init.host, baba, hls);
            if (url == null)
                return e.Fail("baba");

            Dictionary<string, string> stream_links = null;

            await httpHydra.GetSpan(url, span =>
            {
                stream_links = StripchatTo.StreamLinks(span, baba, hls);
            });

            if (stream_links == null || stream_links.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(stream_links);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return OnResult(cache);
    }
}
