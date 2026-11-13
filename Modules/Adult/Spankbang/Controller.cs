using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.HTTP;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace Spankbang;

public class SpankbangController : BaseSisiController
{
    static readonly HttpClient http2Client = FriendlyHttp.CreateHttp2Client();

    public SpankbangController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 2)
                httpHydra.RegisterHttp(http2Client);
        };
    }

    [HttpGet, Staticache]
    [Route("sbg")]
    async public Task<ActionResult> Index(string search, string sort, string path, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"sbg:{search}:{sort}:{path}:{pg}", 10, jsonContext.ListPlaylistItem, async e =>
        {
            string url = string.IsNullOrWhiteSpace(path)
                ? SpankbangTo.Uri(init.host, search, sort, pg)
                : SpankbangTo.UriPath(init.host, path, pg);

            if (url == null)
                return e.Fail("uri");

            List<PlaylistItem> playlists = null;

            if (rch?.enable == true || init.priorityBrowser == "http")
            {
                await httpHydra.GetSpan(url, span =>
                {
                    playlists = SpankbangTo.Playlist("sbg/vidosik", span);
                });
            }
            else
            {
                var headers = httpHeaders(init);

                await PlaywrightHttp.GetSpan(init.plugin, init.cors(url, headers, requestInfo), span =>
                {
                    playlists = SpankbangTo.Playlist("sbg/vidosik", span);
                }, headers, proxy_data);
            }

            if (playlists == null || playlists.Count == 0)
            {
                // trang dien vien/kenh khong co video (vd "Video: 0") -> list rong, khong bao loi
                if (!string.IsNullOrWhiteSpace(path))
                    return e.Success(new List<PlaylistItem>());

                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));
            }

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        // trang dien vien/kenh rong (vd "Video: 0") -> tra list rong, khong bao loi
        if (!string.IsNullOrWhiteSpace(path) && cache.IsSuccess && (cache.Value == null || cache.Value.Count == 0))
            return Json(new { total_pages = 0, list = new List<PlaylistItem>() });

        return PlaylistResult(cache,
            string.IsNullOrEmpty(search) ? SpankbangTo.Menu(host, sort) : null
        );
    }

    [HttpGet]
    [Route("sbg/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = SpankbangTo.PageUri(init.host, uri);
        if (pageUrl == null)
            return Json(new { items = new object[0], groups = new object[0] });

        string memKey = ipkey($"sbg:info:{uri}");
        if (!hybridCache.TryGetValue(memKey, out SpankbangTo.InfoBox info) || info == null)
        {
            SpankbangTo.InfoBox box = null;

            if (rch?.enable == true || init.priorityBrowser == "http")
            {
                await httpHydra.GetSpan(pageUrl, span =>
                {
                    box = SpankbangTo.Info(span.ToString());
                });
            }
            else
            {
                var headers = httpHeaders(init);

                await PlaywrightHttp.GetSpan(init.plugin, init.cors(pageUrl, headers, requestInfo), span =>
                {
                    box = SpankbangTo.Info(span.ToString());
                }, headers, proxy_data);
            }

            info = box;
            if (info != null)
                hybridCache.Set(memKey, info, cacheTime(360));
        }

        var groups = new List<object>();

        void addGroup(string title, List<SpankbangTo.Chip> chips)
        {
            if (chips == null || chips.Count == 0) return;

            groups.Add(new
            {
                title,
                items = chips.Select(c => new
                {
                    title = c.title,
                    url = $"{host}/sbg?path={HttpUtility.UrlEncode(c.path)}"
                }).ToList()
            });
        }

        if (info != null)
        {
            addGroup("Diễn viên", info.cast);
            addGroup("Kênh", info.channels);
            addGroup("Thẻ", info.tags);
        }

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    url = $"{host}/sbg/vidosik?uri={HttpUtility.UrlEncode(uri)}&related=true"
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sbg/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult($"spankbang:view:{uri}", 20, jsonContext.StreamItem, async e =>
        {
            string url = SpankbangTo.StreamLinksUri(init.host, uri);
            if (url == null)
                return e.Fail("uri");

            StreamItem stream_links = null;

            if (rch?.enable == true || init.priorityBrowser == "http")
            {
                await httpHydra.GetSpan(url, span =>
                {
                    stream_links = SpankbangTo.StreamLinks("sbg/vidosik", span);
                });
            }
            else
            {
                var headers = httpHeaders(init);

                await PlaywrightHttp.GetSpan(init.plugin, init.cors(url, headers, requestInfo), span =>
                {
                    stream_links = SpankbangTo.StreamLinks("sbg/vidosik", span);
                }, headers, proxy_data);
            }

            if (stream_links?.qualitys == null || stream_links.qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(stream_links);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (related)
        {
            var recomends = cache.Value?.recomends;
            if (recomends == null || recomends.Count == 0)
                return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

            return PlaylistResult(recomends, cache.ISingleCache, null, total_pages: 1);
        }

        return OnResult(cache);
    }
}
