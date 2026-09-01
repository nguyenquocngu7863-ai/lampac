using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.HTTP;
using System;
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
            string url = StripchatTo.Uri(init.host, tag, pg);
            bool responseReceived = false;

            // Do not use httpHydra here: when an RCH client is connected Hydra silently routes
            // this public API request through the client, so the callback may never run. The
            // same URL is proven reachable directly from Ubuntu/Termux; force server-side HTTP.
            var directHeaders = HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 Chrome/151 Mobile Safari/537.36"),
                ("Referer", "https://stripchat.com/"),
                ("Accept", "application/json")
            );
            bool requestOk = await Http.GetSpan(url, span =>
            {
                responseReceived = true;
                playlists = StripchatTo.Playlist(init.host, span, pg, out totalPages);
            }, timeoutSeconds: Math.Max(20, init.httptimeout), headers: directHeaders,
               proxy: null, httpversion: 1, useDefaultHeaders: false);

            if (playlists == null || playlists.Count == 0)
            {
                string reason = StripchatTo.LastError;
                if (!responseReceived)
                    reason = $"server HTTP callback not called (requestOk={requestOk})";
                return e.Fail($"playlists: {reason ?? "unknown parser result"}", refresh_proxy: true);
            }
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
