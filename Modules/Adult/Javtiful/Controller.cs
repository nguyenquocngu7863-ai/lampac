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

namespace Javtiful;

public class JavtifulController : BaseSisiController
{
    public JavtifulController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javtiful")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // Gop sort ve dung tap cua context TRUOC khi lap cache key —
        // `sort=popular_week` o /vn/category/* da do la no-op.
        sort = JavtifulTo.ClampSort(sort, search, c);

        var cache = await InvokeCacheResult(ipkey($"javtiful:{search}:{c}:{sort}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(
                JavtifulTo.Uri(init.host, search, c, pg, sort));

            var playlists = JavtifulTo.Playlist(
                "javtiful/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync(search, sort, c));
    }

    // httpHydra.GetSpan re-entrant (bien cuc bo moi lan goi) nen goi
    // song song cho cac trang /vn/channels duoc.
    async Task<string> FetchHtmlAsync(string url)
    {
        try
        {
            string html = null;
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", JavtifulTo.ChromeUA),
                ("Referer", JavtifulTo.SiteHost + "/")
            ));

            return html;
        }
        catch
        {
            return null;
        }
    }

    // head (Tìm kiếm + "Sắp xếp: <sort hiện tại>") phụ thuộc c/search/sort
    // -> dựng lại mỗi request; base (nhóm dòng 3 trở xuống) mới cache.
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var res = JavtifulTo.MenuHead(host, search, sort, c);
        res.AddRange(await MenuBaseAsync());
        return res;
    }

    // Menu "Kenh" boc tu /vn/channels (24 kenh/trang, 13 trang).
    // Fetch cac trang con SONG SONG roi gom trung ten, cache 6 gio RAM.
    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string key = ipkey("javtiful:channels");

        if (!hybridCache.TryGetValue(key,
            out List<(string name, string slug)> chans)
            || chans == null || chans.Count == 0)
        {
            string first = await FetchHtmlAsync(
                JavtifulTo.SiteHost + "/vn/channels");

            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var pool = new List<(string, string)>();

            void Add(string html)
            {
                if (string.IsNullOrEmpty(html))
                    return;
                foreach (var c in JavtifulTo.ChannelList(html))
                    if (seen.Add(c.slug))
                        pool.Add(c);
            }

            Add(first);

            int pages = JavtifulTo.ChannelPages(first);
            if (pool.Count > 0 && pages > 1)
            {
                var tasks = new List<Task<string>>();
                for (int p = 2; p <= pages; p++)
                    tasks.Add(FetchHtmlAsync(JavtifulTo.SiteHost
                        + "/vn/channels?page=" + p));

                foreach (var html in await Task.WhenAll(tasks))
                    Add(html);
            }

            chans = pool;
            if (chans.Count > 0)
                hybridCache.Set(key, chans, cacheTime(360));

            Console.WriteLine("Javtiful: channels pages="
                + pages + " n=" + chans.Count);
        }

        return JavtifulTo.Menu(host, chans);
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"javtiful:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = "https://javtiful.com" + pageUrl;

        var links = new Dictionary<string, string>();

        string pageHtml = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            pageHtml = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", JavtifulTo.ChromeUA),
            ("Referer", "https://javtiful.com/")
        ));

        if (!string.IsNullOrEmpty(pageHtml))
        {
            foreach (var u in JavtifulTo.StreamUrls(pageHtml))
            {
                string label = u.Contains(".m3u8") ? "HLS" : "MP4";
                string key = label;
                int n = 2;
                while (links.ContainsKey(key))
                    key = label + " " + (n++);
                if (!links.ContainsValue(u))
                    links.TryAdd(key, u);
            }
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javtiful/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/javtiful/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("javtiful/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JavtifulTo.ChromeUA),
            ("Referer", "https://javtiful.com/"),
            ("Origin", "https://javtiful.com")
        ));
        return Redirect(HostStreamProxy(link, headers));
    }
}
