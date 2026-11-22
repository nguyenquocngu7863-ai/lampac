using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
namespace FapClub;
public class FapClubController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();
    public FapClubController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);
        };
    }

    [HttpGet, Staticache]
    [Route("fapclub")]
    async public Task<ActionResult> Index(string c, string search, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
    rhubFallback:
        var cache = await InvokeCacheResult($"fapclub:{c}:{search}:{pg}", 60, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;
            await httpHydra.GetSpan(FapClubTo.Uri(init.host, c, search, pg), span =>
            {
                playlists = FapClubTo.Playlist(init.host, span);
            });
            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);
            return e.Success(playlists);
        });
        if (IsRhubFallback(cache))
            goto rhubFallback;
        return PlaylistResult(cache);
    }

    [HttpGet]
    [Route("fapclub/menu")]
    async public Task<ActionResult> Menu()
    {
        List<MenuItem> menu = new List<MenuItem>();
        List<MenuItem> categories = null;
        await httpHydra.GetSpan(FapClubTo.SiteHost + "/", span =>
        {
            categories = FapClubTo.Categories(span);
        });
        if (categories != null && categories.Count > 0)
        {
            foreach (var batch in ToChunks(categories, 25))
                menu.Add(new MenuItem { title = "Thể loại", playlist_url = "submenu", submenu = new List<MenuItem>(batch) });
        }
        return Json(menu);
    }

    static IEnumerable<List<MenuItem>> ToChunks(List<MenuItem> src, int size)
    {
        for (int i = 0; i < src.Count; i += size)
        {
            int n = System.Math.Min(size, src.Count - i);
            yield return src.GetRange(i, n);
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("fapclub/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        string page = FapClubTo.DetailUrl(uri);
        if (page == null)
            return OnError("stream_links", refresh_proxy: true);
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
    rhubFallback:
        var cache = await InvokeCacheResult($"fapclub:view:{page}", 180, jsonContext.StreamItem, async e =>
        {
            StreamItem stream = null;
            await httpHydra.GetSpan(FapClubTo.SiteHost + page, span =>
            {
                stream = FapClubTo.Stream(span);
            });
            if (stream == null)
                return e.Fail("stream_links", refresh_proxy: true);
            return e.Success(stream);
        });
        if (IsRhubFallback(cache))
            goto rhubFallback;
        return OnResult(cache);
    }
}
