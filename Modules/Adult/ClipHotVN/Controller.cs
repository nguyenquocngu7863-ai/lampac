using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace ClipHotVN;

public class ClipHotVNController : BaseSisiController
{
    public ClipHotVNController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("cliphotvn")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var menuTask = MenuAsync();

        var cache = await InvokeCacheResult(ipkey($"cliphotvn:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string page = await GetPageAsync(ClipHotVNTo.Uri(init.host, search, c, pg));
            var playlists = ClipHotVNTo.Playlist("cliphotvn/vidosik", page);
            if (playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));
            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await menuTask);
    }

    async Task<List<MenuItem>> MenuAsync()
    {
        string key = ipkey("cliphotvn:menu");
        if (hybridCache.TryGetValue(key, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;
        try
        {
            string home = await GetPageAsync(ClipHotVNTo.SiteHost + "/");
            var cats = ClipHotVNTo.NavCats(home);
            var tags = ClipHotVNTo.TagList(home);
            if (cats.Count > 0 || tags.Count > 0)
            {
                var menu = ClipHotVNTo.Menu(hostLocal, cats.Count>0?cats:null, tags.Count>0?tags:null);
                hybridCache.Set(key, menu, cacheTime(360), true);
                return menu;
            }
        }
        catch { }
        return ClipHotVNTo.Menu(hostLocal, null, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        string page = null;
        await httpHydra.GetSpan(url, s => page = s.ToString(), addheaders: PageHeaders(url));
        if (string.IsNullOrEmpty(page))
        {
            page = await Http.Get(url, timeoutSeconds: Math.Max(20, init.httptimeout), httpversion: init.httpversion, proxy: proxy, headers: PageHeaders(url));
        }
        return page;
    }

    async Task<Dictionary<string,string>> ResolveLinksAsync(string uri)
    {
        string pageUrl = ClipHotVNTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl)) return null;
        string memKey = ipkey($"cliphotvn:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string,string> cache) && cache != null && cache.Count>0) return cache;

        SemaphorManager semaphore = null;
        if (rch?.enable != true)
        {
            semaphore = new SemaphorManager($"cliphotvn:view:{pageUrl}", TimeSpan.FromSeconds(30));
            if (!await semaphore.WaitAsync()) return null;
        }
        try
        {
            string page = await GetPageAsync(pageUrl);
            var servers = ClipHotVNTo.VideoServers(page);
            if (servers.Count == 0) return null;
            var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            int n=0;
            foreach (var (movie,type,label) in servers)
            {
                n++;
                string prefix = servers.Count==1?"":"Server "+n+" ";
                foreach (var (itag,url) in await ResolveServerAsync(pageUrl,movie,type))
                {
                    string key = prefix + (itag==22?"720p":"360p");
                    if (!result.ContainsKey(key)) result[key]=url;
                }
            }
            if (result.Count==0) return null;
            hybridCache.Set(memKey,result,cacheTime(10));
            return result;
        }
        finally { semaphore?.Release(); }
    }

    async Task<List<(int itag,string url)>> ResolveServerAsync(string pageUrl,string movie,string type)
    {
        var res = new List<(int,string)>();
        try
        {
            // movie = go.qooglevideo.shop/x/... URL extracted from detail page
            string goHtml = await Http.Get(movie, timeoutSeconds: Math.Max(20, init.httptimeout), httpversion: init.httpversion, proxy: proxy, headers: PageHeaders(pageUrl));
            if (string.IsNullOrEmpty(goHtml)) goHtml = await httpHydra.Get(movie, addheaders: PageHeaders(pageUrl), statusCodeOK:false);
            foreach (var (file,label) in ClipHotVNTo.JwPlayerSources(goHtml))
            {
                if (ClipHotVNTo.IsMedia(file))
                    res.Add((file.Contains(".m3u8")?22:18, file));
            }
        }
        catch { }
        return res;
    }

    // Header helpers
    static IReadOnlyList<HeadersModel> PageHeaders(string referer) => HeadersModel.Init(("User-Agent", ClipHotVNTo.ChromeUA),("Referer", string.IsNullOrEmpty(referer)?ClipHotVNTo.SiteHost+"/":referer),("Accept","text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
    static IReadOnlyList<HeadersModel> PlayerHeaders(string referer) => HeadersModel.Init(("User-Agent", ClipHotVNTo.ChromeUA),("Referer", referer),("Origin", ClipHotVNTo.SiteHost),("Accept","*/*"),("X-Requested-With","XMLHttpRequest"));
    static IReadOnlyList<HeadersModel> EmbHeaders(string referer) => HeadersModel.Init(("User-Agent", ClipHotVNTo.ChromeUA),("Referer", referer),("Accept","application/json, text/plain, */*"),("X-Requested-With","XMLHttpRequest"));
    static IReadOnlyList<HeadersModel> BloggerHeaders() => HeadersModel.Init(("User-Agent", Http.UserAgent),("Referer","https://www.blogger.com/"),("X-Same-Domain","1"),("Accept","*/*"));

    // Route video
    [HttpGet, Staticache(manually: true)]
    [Route("cliphotvn/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/cliphotvn/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet, Staticache(manually:true)]
    [Route("cliphotvn/video")]
    [Route("cliphotvn/video.m3u8")]
    [Route("cliphotvn/video.mp4")]
    async public Task<ActionResult> Video(string uri,string q)
    {
        if (await IsRequestBlocked(rch:true)) return badInitMsg;
        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        string link = null;
        if (!string.IsNullOrEmpty(q))
            links.TryGetValue(q, out link);
        if (string.IsNullOrEmpty(link))
        {
            if (!links.TryGetValue("720p", out link))
                link = links.Values.FirstOrDefault();
        }
        if (string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);
        var headers = httpHeaders(init, HeadersModel.Init(("referer",ClipHotVNTo.SiteHost+"/")));
        return Redirect(HostStreamProxy(link,headers));
    }

    [HttpGet]
    [Route("cliphotvn/strem")]
    async public Task<ActionResult> Strem(string link,string uri,string q)
    {
        if (await IsRequestBlocked(rch:true)) return badInitMsg;
        if (!string.IsNullOrEmpty(uri)&&!string.IsNullOrEmpty(q))
        {
            var links=await ResolveLinksAsync(uri);
            if (links==null||!links.TryGetValue(q,out link)||string.IsNullOrEmpty(link))
                return OnError("stream_links",refresh_proxy:true);
        }
        if (string.IsNullOrEmpty(link)) return OnError("link");
        var headers = httpHeaders(init, HeadersModel.Init(("referer",ClipHotVNTo.SiteHost+"/")));
        return Redirect(HostStreamProxy(link,headers));
    }
}
