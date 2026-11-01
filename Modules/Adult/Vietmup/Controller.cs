using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace Vietmup;

public class VietmupController : BaseSisiController
{
    public VietmupController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("vietmup")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"vietmup:{search}:{c}:{sort}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(
                VietmupTo.Uri(init.host, search, c, pg, sort));

            var playlists = VietmupTo.Playlist(
                "vietmup/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync(search, sort, c));
    }

    async Task<string> FetchHtmlAsync(string url)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                string html = null;
                await httpHydra.GetSpan(url, span =>
                {
                    html = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", VietmupTo.ChromeUA),
                    ("Referer", VietmupTo.SiteHost + "/")
                ));

                if (!string.IsNullOrEmpty(html))
                    return html;
            }
            catch
            {
                // retry
            }

            if (attempt == 0)
                await Task.Delay(700);
        }

        Console.WriteLine("Vietmup: fetch fail " + url);
        return null;
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var res = VietmupTo.MenuHead(host, search, sort, c);
        res.AddRange(await MenuBaseAsync());
        return res;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("vietmup:categories");

        if (!hybridCache.TryGetValue(key, out List<(string name, string slug)> cats)
            || cats == null || cats.Count == 0)
        {
            string html = await FetchHtmlAsync(VietmupTo.SiteHost + "/");
            cats = VietmupTo.Categories(html ?? "");

            if (cats.Count > 0)
                hybridCache.Set(key, cats, cacheTime(360));

            Console.WriteLine("Vietmup: categories n=" + cats.Count);
        }

        return VietmupTo.Menu(host, cats);
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"vietmup:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = VietmupTo.SiteHost + pageUrl;

        var links = new Dictionary<string, string>();

        string pageHtml = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            pageHtml = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", VietmupTo.ChromeUA),
            ("Referer", VietmupTo.SiteHost + "/")
        ));

        if (!string.IsNullOrEmpty(pageHtml))
        {
            string tokenPath = VietmupTo.StreamToken(pageHtml);
            if (!string.IsNullOrEmpty(tokenPath))
            {
                string streamUrl = VietmupTo.SiteHost + tokenPath;
                links.TryAdd("HLS", streamUrl);
            }
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("vietmup/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/vietmup/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("vietmup/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(q))
            return OnError("stream_links", refresh_proxy: true);

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", VietmupTo.ChromeUA),
            ("Referer", VietmupTo.SiteHost + "/")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }
}