using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace VnChich;

public class VnChichController : BaseSisiController
{
    public VnChichController() : base(ModInit.conf) { }

    // cache RAM (khong dung hybridCache: object custom khong serialize lai duoc)
    static readonly ConcurrentDictionary<string, (DateTime at, VnChichTo.VideoDetail det)> _detailCache = new();
    static readonly ConcurrentDictionary<string, (DateTime at, List<PlaylistItem> list)> _relCache = new();

    static readonly TimeSpan DetailTtl = TimeSpan.FromMinutes(30);
    static readonly TimeSpan RelTtl = TimeSpan.FromMinutes(180);

    [HttpGet, Staticache(manually: true)]
    [Route("vnchich")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // danh sách "Video liên quan" (mở từ popup long-press)
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel);

        if (pg < 1)
            pg = 1;

        sort = VnChichTo.ClampSort(sort);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"vnch:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string json = await GetJsonAsync(VnChichTo.ApiUri(init.host, search, c, pg, sort));

            var playlists = VnChichTo.Playlist(json, init.host);

            // danh mục (cat/..): thẻ không tồn tại -> lùi về search
            if (playlists.Count == 0
                && string.IsNullOrWhiteSpace(search)
                && !string.IsNullOrWhiteSpace(c)
                && c.StartsWith("cat/", StringComparison.OrdinalIgnoreCase))
            {
                json = await GetJsonAsync(VnChichTo.ApiUri(init.host, search, c, pg, sort, asSearch: true));
                playlists = VnChichTo.Playlist(json, init.host);
            }

            // list rong = het du lieu (search rong / page ngoai phan trang) -> khong Fail
            return e.Success((playlists, VnChichTo.TotalPages(json)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await menuTask, total_pages: cache.Value.total_pages);
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm",
                    search_on = "search_on",
                    playlist_url = host + "/vnchich"
                }
            }
            : VnChichTo.MenuHead(host, search, sort, c);

        res.AddRange(VnChichTo.Menu(host));
        return res;
    }

    async Task<string> GetJsonAsync(string url)
    {
        try
        {
            string json = null;
            await httpHydra.GetSpan(url, span =>
            {
                json = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", VnChichTo.ChromeUA),
                ("Referer", VnChichTo.SiteHost + "/")
            ));

            return json;
        }
        catch { return null; }
    }

    #region PLAYER
    async Task<VnChichTo.VideoDetail> DetailAsync(string uuid)
    {
        if (!IsId(uuid))
            return null;

        if (_detailCache.TryGetValue(uuid, out var hit)
            && DateTime.UtcNow - hit.at < DetailTtl && hit.det != null)
            return hit.det;

        string json = await GetJsonAsync(VnChichTo.DetailUri(init.host, uuid));
        var det = VnChichTo.Detail(json);

        if (!string.IsNullOrEmpty(det.Uuid))
            _detailCache[uuid] = (DateTime.UtcNow, det);

        return det;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("vnchich/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var det = await DetailAsync(uri);
        if (det == null || det.Streams.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, url) in det.Streams)
        {
            if (string.IsNullOrEmpty(url) || links.ContainsKey(label))
                continue;

            links[label] = $"{host}/vnchich/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(label)}";
        }

        if (links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links);
    }

    [HttpGet]
    [Route("vnchich/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var det = await DetailAsync(uri);
        if (det == null || det.Streams.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        string link = null;
        if (!string.IsNullOrWhiteSpace(q))
            link = det.Streams.FirstOrDefault(x => string.Equals(x.label, q, StringComparison.OrdinalIgnoreCase)).url;

        if (string.IsNullOrEmpty(link))
            link = det.Streams[0].url;

        return Redirect(HostStreamProxy(link));
    }
    #endregion

    #region POPUP (long-press)
    [HttpGet]
    [Route("vnchich/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (!IsId(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        var det = await DetailAsync(uri);
        if (det == null || string.IsNullOrEmpty(det.Uuid))
            return Json(new { items = new object[0], groups = new object[0] });

        var groups = new List<object>();

        var tagItems = det.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => new
            {
                title = t,
                url = $"{host}/vnchich?c=tag/{HttpUtility.UrlEncode(t)}"
            })
            .ToList();

        if (tagItems.Count > 0)
            groups.Add(new { title = "Thẻ", items = tagItems });

        var rel = await RelatedAsync(uri, det);
        if (rel.Count > 0)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new
                    {
                        title = "Video liên quan",
                        plain = true,
                        url = $"{host}/vnchich?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<List<PlaylistItem>> RelatedAsync(string uuid, VnChichTo.VideoDetail det)
    {
        if (_relCache.TryGetValue(uuid, out var hit)
            && DateTime.UtcNow - hit.at < RelTtl && hit.list != null && hit.list.Count > 0)
            return hit.list;

        // API bỏ qua channelOneOf/accountOneOf -> liên quan lấy theo thẻ
        string tag = VnChichTo.RelatedTag(det?.Tags);
        if (string.IsNullOrEmpty(tag))
            return new List<PlaylistItem>();

        string json = await GetJsonAsync(VnChichTo.ApiUri(init.host, null, "tag/" + tag, 1, "-publishedAt"));
        var res = VnChichTo.Playlist(json, init.host, uuid);

        if (res.Count > 0)
            _relCache[uuid] = (DateTime.UtcNow, res);

        return res;
    }

    async Task<ActionResult> RelatedList(string rel)
    {
        var det = await DetailAsync(rel);
        var playlists = det == null
            ? new List<PlaylistItem>()
            : await RelatedAsync(rel, det);

        if (playlists.Count == 0)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        return PlaylistResult(playlists, false,
            await MenuAsync(null, null, null, related: true), total_pages: 1);
    }
    #endregion

    static bool IsId(string id)
        => !string.IsNullOrWhiteSpace(id)
            && Regex.IsMatch(id, "^[a-z0-9\\-]{8,64}$", RegexOptions.IgnoreCase);
}
