using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace VietSexBlog;

public class VietSexBlogController : BaseSisiController
{
    public VietSexBlogController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("vietsexblog")]
    async public Task<ActionResult> Index(string search, string sort, string c, string t, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"vietsexblog:{search}:{sort}:{c}:{t}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(VietSexBlogTo.Uri(host, search, sort, c, t, pg), span =>
            {
                playlists = VietSexBlogTo.Playlist("vietsexblog/vidosik", span.ToString());
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, VietSexBlogTo.Menu(host));
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"vietsexblog:view:{uri}";

        if (rch?.enable != true)
        {
            semaphore ??= new SemaphorManager(semaphoreKey, System.TimeSpan.FromSeconds(30));
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return (null, false);
        }

        try
        {
            string memKey = ipkey(semaphoreKey);
            if (!hybridCache.TryGetValue(memKey, out (Dictionary<string, string> links, bool userch) cache))
            {
                string url = uri;
                if (uri.StartsWith("/"))
                    url = VietSexBlogTo.SiteHost + uri;
                else if (!uri.StartsWith("http"))
                    url = VietSexBlogTo.SiteHost + "/phim/" + uri;

                Dictionary<string, string> links = null;

                await httpHydra.GetSpan(url, span =>
                {
                    links = VietSexBlogTo.StreamLinks(span.ToString());
                });

                if (links == null || links.Count == 0)
                    return (null, false);

                proxyManager?.Success();
                cache = (links, rch?.enable == true);
                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            return (cache.links, cache.userch);
        }
        finally
        {
            semaphore?.Release();
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("vietsexblog/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    reset:
        var (links, userch) = await ResolveLinksAsync(uri);

        if (links == null || links.Count == 0)
        {
            if (IsRhubFallback())
                goto reset;

            return OnError("stream_links", refresh_proxy: true);
        }

        if (userch)
            return OnResult(links);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/vietsexblog/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("vietsexblog/video")]
    [Route("vietsexblog/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        // Player trong app chi dung hls.js khi URL chua ".m3u8".
        // Tra link qua route video.m3u8 cua chinh minh (redirect 302 sang proxy).
        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://x.vietsex.blog/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
