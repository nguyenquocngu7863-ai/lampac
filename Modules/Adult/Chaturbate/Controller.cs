using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Chaturbate;

public class ChaturbateController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

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
    async public Task<ActionResult> Index(string search, string sort, int pg = 1)
    {
        if (!string.IsNullOrEmpty(search))
            return OnError("no search", false);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"Chaturbate:list:{sort}:{pg}", 5, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(ChaturbateTo.Uri(init.host, sort, pg), span =>
            {
                playlists = ChaturbateTo.Playlist("chu/potok", span);
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return PlaylistResult(cache, ChaturbateTo.Menu(host, sort));
    }


    [HttpGet, Staticache(manually: true)]
    [Route("chu/potok")]
    async public Task<ActionResult> Vidosik(string baba)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"chaturbate:stream:{baba}", 1, jsonContext.DictionaryStringString, async e =>
        {
            string url = ChaturbateTo.StreamLinksUri(init.host, baba);
            if (url == null)
                return e.Fail("baba");

            Dictionary<string, string> stream_links = null;

            // The room page now often exposes a low-latency `llhls.m3u8`
            // master with a separate audio rendition. Android hls.js can play
            // a few seconds, then fail with audioTrackLoadError. Chaturbate's
            // own edge endpoint returns its compatibility HLS URL and is also
            // the extraction path used by current yt-dlp.
            var edge = await httpHydra.Post<JObject>(
                $"{init.host}/get_edge_hls_url_ajax/",
                $"room_slug={Uri.EscapeDataString(baba)}",
                addheaders: HeadersModel.Init(
                    ("X-Requested-With", "XMLHttpRequest"),
                    ("Accept", "application/json"),
                    ("Referer", $"{init.host}/{baba}/")
                )
            );

            string edgeUrl = edge?.Value<string>("url");
            if (!string.IsNullOrWhiteSpace(edgeUrl))
            {
                stream_links = new Dictionary<string, string>
                {
                    ["auto"] = edgeUrl
                };
            }
            else
            {
                // Keep the page parser as fallback for mirrors/older servers.
                await httpHydra.GetSpan(url, span =>
                {
                    stream_links = ChaturbateTo.StreamLinks(span);
                });
            }

            if (stream_links == null || stream_links.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(stream_links);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return OnResult(cache);
    }
}
