using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Stripchat;

public class StripchatController : BaseSisiController
{
    public StripchatController() : base(ModInit.conf) { }

    [HttpGet, Staticache]
    [Route("stripchat")]
    public async Task<ActionResult> Index(string search, string tag, int pg = 1)
    {
        if (!string.IsNullOrEmpty(search))
            return OnError("no search", false);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>($"Stripchat:list:{tag}:{pg}", 3, async e =>
        {
            List<PlaylistItem> playlists = null;
            int totalPages = 0;
            await httpHydra.GetSpan(StripchatTo.Uri(init.host, tag, pg), span =>
            {
                playlists = StripchatTo.Playlist(init.host, span, pg, out totalPages);
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);
            return e.Success((playlists, totalPages));
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            StripchatTo.Menu(host, tag),
            total_pages: cache.Value.total_pages
        );
    }
}
