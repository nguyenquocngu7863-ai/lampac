using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.HTTP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace Topsex;

public class TopsexController : BaseSisiController
{
    public TopsexController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("topsex")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"topsex:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(
                TopsexTo.Uri(init.host, search, c, pg, sort));

            var playlists = TopsexTo.Playlist(
                "topsex/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync(search, c));
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
                ("User-Agent", TopsexTo.ChromeUA),
                ("Referer", TopsexTo.SiteHost + "/")
            ));

            if (!string.IsNullOrEmpty(html))
                return html;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Topsex: fetch fail " + url + " - " + ex.Message);
        }

        return null;
    }

    async Task<List<MenuItem>> MenuAsync(string search, string c)
    {
        var res = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/topsex"
            }
        };

        // Thêm menu categories nếu không có search
        if (string.IsNullOrEmpty(search))
        {
            var categories = await GetCategoriesAsync();
            if (categories.Count > 0)
            {
                res.Add(new MenuItem()
                {
                    title = "Danh mục",
                    playlist_url = "submenu",
                    submenu = categories.Select(cat =>
                        new MenuItem(cat.name, host + "/topsex?c=" + HttpUtility.UrlEncode(cat.path))
                    ).ToList()
                });
            }
        }

        return res;
    }

    async Task<List<(string name, string path)>> GetCategoriesAsync()
    {
        string key = ipkey("topsex:categories");

        if (!hybridCache.TryGetValue(key, out List<(string, string)> cats)
            || cats == null || cats.Count == 0)
        {
            string html = await FetchHtmlAsync(TopsexTo.SiteHost);
            if (!string.IsNullOrEmpty(html))
            {
                cats = TopsexTo.Categories(html);
                if (cats.Count > 0)
                    hybridCache.Set(key, cats, cacheTime(360));
            }
        }

        return cats ?? new List<(string, string)>();
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"topsex:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = TopsexTo.SiteHost + pageUrl;

        var links = new Dictionary<string, string>();
        var seenUrls = new HashSet<string>();

        void AddLink(string name, string u, string type)
        {
            if (string.IsNullOrWhiteSpace(u) || !seenUrls.Add(u))
                return;

            string key = name;
            int n = 2;
            while (links.ContainsKey(key))
                key = name + " " + (n++);

            links.TryAdd(key, u);
        }

        string pageHtml = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            pageHtml = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", TopsexTo.ChromeUA),
            ("Referer", TopsexTo.SiteHost + "/")
        ));
        if (!string.IsNullOrEmpty(pageHtml))
        {
            var servers = TopsexTo.StreamServers(pageHtml);
            foreach (var (name, link, type) in servers)
            {
                // Xử lý link - đảm bảo https
                string finalLink = link.Replace("http://", "https://");

                string resolvedLink = finalLink;
                string resolvedType = type;

                // Nếu là embed (blogger video.g), fetch bằng Chrome thật (Playwright)
                // rồi trích link mp4 thật (googlevideo.com/videoplayback)
                if (type == "embed" || finalLink.Contains("blogger.com/video.g"))
                {
                    string embedHtml = null;
                    var headers = httpHeaders(init, HeadersModel.Init(
                        ("User-Agent", TopsexTo.ChromeUA),
                        ("Referer", pageUrl)
                    ));
                    await PlaywrightHttp.GetSpan(init.plugin, init.cors(finalLink, headers, requestInfo), span =>
                    {
                        embedHtml = span.ToString();
                    }, headers, proxy_data);

                    string mp4 = TopsexTo.ExtractMp4(embedHtml ?? "");
                    if (!string.IsNullOrEmpty(mp4))
                    {
                        resolvedLink = mp4;
                        resolvedType = "mp4";
                    }
                    else
                    {
                        // Fallback: giữ link embed (iframe)
                        resolvedLink = finalLink;
                        resolvedType = "iframe";
                    }
                }

                if (resolvedType == "embed")
                    resolvedType = "iframe";

                AddLink(name, resolvedLink, resolvedType);
            }
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("topsex/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/topsex/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("topsex/video")]
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
            ("User-Agent", TopsexTo.ChromeUA),
            ("Referer", TopsexTo.SiteHost + "/"),
            ("Origin", TopsexTo.SiteHost)
        ));

        return Redirect(HostStreamProxy(link, headers));
    }
}
