using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace SexVietTuyChon;

public class SexVietTuyChonController : BaseSisiController
{
    public SexVietTuyChonController() : base(ModInit.conf) { }

    async Task<string> FetchHtmlAsync(string url)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", SexVietTuyChonTo.ChromeUA),
                ("Referer", SexVietTuyChonTo.SiteHost + "/")
            ));
            return html;
        }
        catch
        {
            return null;
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sexviettuyenchon")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        var cache = await InvokeCacheResult<
            (List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"sexviettuyenchon:{search}:{c}:{pg}"), 10, async e =>
        {
            string html = await FetchHtmlAsync(
                SexVietTuyChonTo.Uri(init.host, search, c, pg));

            var playlists = SexVietTuyChonTo.Playlist(
                "sexviettuyenchon/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
            {
                // search / trang cuoi -> list rong la binh thuong, khong bao loi.
                if (!string.IsNullOrEmpty(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: false);
            }

            return e.Success((playlists, SexVietTuyChonTo.Pages(html)));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            SexVietTuyChonTo.Menu(host), total_pages: cache.Value.total_pages);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sexviettuyenchon/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"sexviettuyenchon:view:{uri}"), 20,
            jsonContext.StreamItem, async e =>
        {
            string detailUrl = uri;
            if (detailUrl.StartsWith("/"))
                detailUrl = SexVietTuyChonTo.SiteHost + detailUrl;

            string html = await FetchHtmlAsync(detailUrl);
            if (string.IsNullOrEmpty(html))
                return e.Fail("detail", refresh_proxy: true);

            string videoId = SexVietTuyChonTo.VideoId(html);
            if (string.IsNullOrEmpty(videoId))
                return e.Fail("video_id", refresh_proxy: true);

            var qualitys = new Dictionary<string, string>();
            var playHeaders = HeadersModel.Init(
                ("User-Agent", SexVietTuyChonTo.ChromeUA),
                ("Referer", detailUrl),
                ("X-Requested-With", "XMLHttpRequest"),
                ("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8"),
                ("Accept", "application/json")
            );

            foreach (string server in SexVietTuyChonTo.Servers(html))
            {
                string body = "id=" + HttpUtility.UrlEncode(videoId)
                    + "&server=" + HttpUtility.UrlEncode(server);

                string resp = null;
                await httpHydra.PostSpan(
                    SexVietTuyChonTo.SiteHost + "/api/player",
                    body, span => { resp = span.ToString(); },
                    addheaders: playHeaders, statusCodeOK: false);

                string url = SexVietTuyChonTo.PlayerUrl(resp);
                if (!string.IsNullOrEmpty(url) && !qualitys.ContainsValue(url))
                    qualitys[server] = url;
            }

            if (qualitys.Count == 0)
                return e.Fail("player", refresh_proxy: true);

            return e.Success(new StreamItem { qualitys = qualitys });
        });

        return OnResult(cache);
    }

    #region popup (long-press)
    [HttpGet]
    [Route("sexviettuyenchon/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = new { items = new object[0], groups = new object[0] };
        if (string.IsNullOrEmpty(uri))
            return Json(empty);

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri : SexVietTuyChonTo.SiteHost + uri;
        // chong SSRF: chi fetch trong host cua site
        if (!pageUrl.StartsWith(SexVietTuyChonTo.SiteHost, StringComparison.OrdinalIgnoreCase))
            return Json(empty);

        string cacheKey = ipkey($"sexviettuyenchon:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey,
            out List<(string group, string name, string cpath)> chips) && chips != null)
            return InfoResult(chips, uri);

        string html = await FetchHtmlAsync(pageUrl);
        chips = SexVietTuyChonTo.Chips(html);
        if (chips.Count > 0)
            hybridCache.Set(cacheKey, chips, cacheTime(360));

        return InfoResult(chips, uri);
    }

    ActionResult InfoResult(List<(string group, string name, string cpath)> chips, string uri)
    {
        var groups = new List<object>();

        foreach (var (grp, label) in new[] { ("category", "Thể loại"), ("tag", "Thẻ") })
        {
            var items = chips.Where(x => x.group == grp).Select(x => new
            {
                title = x.name,
                url = $"{host}/sexviettuyenchon?c={HttpUtility.UrlEncode(x.cpath)}"
            }).ToList();

            if (items.Count == 0) continue;
            groups.Add(new { title = label, items });
        }

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = $"{host}/sexviettuyenchon?rel={HttpUtility.UrlEncode(uri)}"
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<
            (List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"sexviettuyenchon:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rel : SexVietTuyChonTo.SiteHost + rel;
            string html = await FetchHtmlAsync(url);

            var playlists = SexVietTuyChonTo.Playlist(
                "sexviettuyenchon/vidosik", html ?? "");
            // bo chinh video dang xem khoi list lien quan
            playlists.RemoveAll(p => p.bookmark?.href != null &&
                string.Equals(p.bookmark.href, url, StringComparison.OrdinalIgnoreCase));

            if (playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            SexVietTuyChonTo.Menu(host), total_pages: 1);
    }
    #endregion
}