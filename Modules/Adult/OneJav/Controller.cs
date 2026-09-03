using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace OneJav;

public class OneJavController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public OneJavController() : base(ModInit.conf) { }

    [HttpGet, Staticache]
    [Route("ojv")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"ojv:{search}:{c}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(OneJavTo.Uri(init.host, search, c, pg), span =>
            {
                playlists = OneJavTo.Playlist("ojv", span);
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache,
            string.IsNullOrEmpty(search) ? OneJavTo.Menu(host) : null
        );
    }

    // Detail route — torrent/magnet resolution is the next step; for now return
    // an empty stream list so tapping a card opens without a hard error.
    [HttpGet, Staticache(manually: true)]
    [Route("ojv/view")]
    async public Task<ActionResult> View(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"OneJav:view:{uri}"), 20, jsonContext.DictionaryStringString, async e =>
        {
            // Playback resolution comes later (onejav serves a .torrent that
            // must be converted to a magnet and handed to TorrServer).
            await Task.CompletedTask;
            return e.Success(new Dictionary<string, string>());
        });

        return OnResult(cache);
    }
}
