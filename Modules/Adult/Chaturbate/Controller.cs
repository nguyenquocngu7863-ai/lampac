using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Chaturbate;

public class ChaturbateController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();
    static readonly Regex SafeTag = new("^[a-z0-9_-]+$", RegexOptions.Compiled);

    public ChaturbateController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache]
    [Route("chu")]
    async public Task<ActionResult> Index(string search, string sort, string c, int pg = 1)
    {
        if (!string.IsNullOrEmpty(search))
            return OnError("no search", false);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrEmpty(c) && !SafeTag.IsMatch(c))
            c = null;

    rhubFallback:
        var cache = await InvokeCacheResult($"Chaturbate:list:{sort}:{c}:{pg}", 5, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(ChaturbateTo.Uri(init.host, sort, c, pg), span =>
            {
                playlists = ChaturbateTo.Playlist("chu/potok", span);
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache, ChaturbateTo.Menu(host, sort, c));
    }


    [HttpGet, Staticache(manually: true)]
    [Route("chu/potok")]
    async public Task<ActionResult> Vidosik(string baba)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        // Live LL-HLS URLs are session-bound and can rotate while a room stays
        // online. Never reuse the old one-minute URL cache on replay.
        var cache = await InvokeCacheResult($"chaturbate:stream-live-v2:{baba}", 0, jsonContext.DictionaryStringString, async e =>
        {
            string url = ChaturbateTo.StreamLinksUri(init.host, baba);
            if (url == null)
                return e.Fail("baba");

            Dictionary<string, string> stream_links = null;

            await httpHydra.GetSpan(url, span =>
            {
                stream_links = ChaturbateTo.StreamLinks(span);
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
