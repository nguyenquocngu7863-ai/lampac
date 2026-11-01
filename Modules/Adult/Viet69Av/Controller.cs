using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace Viet69Av;

public class Viet69AvController : BaseSisiController
{
    public Viet69AvController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("viet69av")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;
        if (!string.IsNullOrWhiteSpace(c)) c = c.Trim().Trim('/');

        var menuTask = Task.FromResult<System.Collections.Generic.IReadOnlyList<MenuItem>>(
            string.IsNullOrEmpty(search) ? Viet69AvTo.Menu(host, search) : null);

    rhubFallback:
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            // khoa cache phai du search/c/pg (checklist 9e)
            ipkey($"viet69av:v3:{search}:{c}:{pg}"), 10, async e =>
        {
            string url = Viet69AvTo.Uri(init.host, search, c, pg);

            // retry 1 lan: loi mang/CF thoang qua ma cache lai la 10 phut 503
            string html = await FetchHtmlAsync(url);
            if (string.IsNullOrEmpty(html))
            {
                await Task.Delay(500);
                html = await FetchHtmlAsync(url);
            }

            var playlists = Viet69AvTo.Playlist("viet69av/vidosik", html);

            if (playlists == null || playlists.Count == 0)
            {
                // trang cuoi / search khong co ket qua: tra list rong, khong Fail
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));

                return e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success((playlists, Viet69AvTo.Pages(html)));
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await menuTask,
            total_pages: cache.Value.total_pages
        );
    }

    [HttpGet, Staticache(manually: true)]
    [Route("viet69av/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string decodedUri = WebUtility.UrlDecode(uri);
        if (string.IsNullOrEmpty(decodedUri)) decodedUri = uri;

        if (string.IsNullOrWhiteSpace(decodedUri) || !decodedUri.StartsWith("http"))
            return OnError("uri");

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"viet69av:v1:view:{decodedUri}"), 20, jsonContext.StreamItem, async e =>
        {
            string detail = await FetchHtmlAsync(decodedUri);
            if (string.IsNullOrEmpty(detail))
            {
                await Task.Delay(500);
                detail = await FetchHtmlAsync(decodedUri);
            }

            var stream_links = await Viet69AvTo.StreamLinks(decodedUri, detail, proxy, init.httptimeout);

            if (stream_links?.qualitys == null || stream_links.qualitys.Count == 0)
                return e.Fail("stream_links", refresh_proxy: true);

            return e.Success(stream_links);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        return OnResult(cache);
    }

    async Task<string> FetchHtmlAsync(string url)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", Viet69AvTo.ChromeUA),
                ("Referer", Viet69AvTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }
}
