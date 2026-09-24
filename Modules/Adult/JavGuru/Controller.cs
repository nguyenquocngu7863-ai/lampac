using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace JavGuru;

public class JavGuruController : BaseSisiController
{
    // client rieng: khong auto-redirect de doc Location cua /searcho/
    static readonly HttpClient resolveClient = JavGuruResolver.CreateClient();

    public JavGuruController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javguru")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked())
            return badInitMsg;

        bool rank = string.IsNullOrWhiteSpace(search) && JavGuruTo.IsRank(c);

        var cache = await InvokeCacheResult(ipkey($"javguru:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            for (int t = 0; t < 2 && (playlists == null || playlists.Count == 0); t++)
            {
                if (t > 0)
                    await Task.Delay(1000);

                await httpHydra.GetSpan(JavGuruTo.Uri(init.host, search, c, rank ? 1 : pg), span =>
                {
                    playlists = JavGuruTo.Playlist("javguru/vidosik", span.ToString(), rank, pg);
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", JavGuruTo.ChromeUA),
                    ("Referer", JavGuruTo.SiteHost + "/"),
                    ("Accept-Language", "en-US,en;q=0.9")
                ));
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        return PlaylistResult(cache, JavGuruTo.Menu(host));
    }

    string PageUrl(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;
        if (uri.StartsWith("/"))
            return JavGuruTo.SiteHost + uri;
        if (!uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return JavGuruTo.SiteHost + "/" + uri.Trim('/') + "/";
        // chi cho phep jav.guru / init.host (tranh SSRF qua ?uri=)
        bool own = System.Text.RegularExpressions.Regex.IsMatch(uri, @"^https?://(?:www\.)?jav\.guru/", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || (!string.IsNullOrEmpty(init.host) && uri.StartsWith(init.host.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
        return own ? uri : null;
    }

    class ViewCache
    {
        public Dictionary<string, JavGuruStream> links { get; set; }

        public List<PlaylistItem> related { get; set; }
    }

    async Task<ViewCache> ResolveAsync(string uri)
    {
        string pageUrl = PageUrl(uri);
        if (pageUrl == null)
            return null;

        string memKey = $"javguru:view:{pageUrl}";
        if (memoryCache.TryGetValue(memKey, out ViewCache cache) && cache?.links != null && cache.links.Count > 0)
            return cache;

        var semaphore = new SemaphorManager(memKey, TimeSpan.FromSeconds(40));
        if (!await semaphore.WaitAsync())
            return null;

        try
        {
            if (memoryCache.TryGetValue(memKey, out cache) && cache?.links != null && cache.links.Count > 0)
                return cache;

            string html = null;
            for (int t = 0; t < 2 && string.IsNullOrEmpty(html); t++)
            {
                if (t > 0)
                    await Task.Delay(1000);

                await httpHydra.GetSpan(pageUrl, span =>
                {
                    string s = span.ToString();
                    if (s.Contains("iframe_url"))
                        html = s;
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", JavGuruTo.ChromeUA),
                    ("Referer", JavGuruTo.SiteHost + "/"),
                    ("Accept-Language", "en-US,en;q=0.9")
                ));
            }

            if (string.IsNullOrEmpty(html))
                return null;

            var mirrors = JavGuruTo.Mirrors(html);
            if (mirrors.Count == 0)
                return null;

            HttpClient client = resolveClient;
            bool dispose = false;
            if (init.useproxy && proxy != null)
            {
                client = JavGuruResolver.CreateClient(proxy);
                dispose = true;
            }

            try
            {
                var resolver = new JavGuruResolver(client, JavGuruTo.ChromeUA);
                var links = await resolver.ResolveAll(mirrors, pageUrl, TimeSpan.FromSeconds(30));
                if (links.Count == 0)
                    return null;

                cache = new ViewCache()
                {
                    links = links,
                    related = JavGuruTo.Related("javguru/vidosik", html)
                };

                // token vidara/dood gan IP + het han nhanh -> cache ngan
                memoryCache.Set(memKey, cache, cacheTime(15));
                return cache;
            }
            finally
            {
                if (dispose)
                    client.Dispose();
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    string VideoUrl(string uri, JavGuruStream s)
        => $"{host}/javguru/video.{(s.hls ? "m3u8" : "mp4")}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(s.label)}";

    [HttpGet, Staticache(manually: true)]
    [Route("javguru/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked())
            return badInitMsg;

        var cache = await ResolveAsync(uri);
        if (cache?.links == null || cache.links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var qualitys = new Dictionary<string, string>();
        foreach (var kv in cache.links)
            qualitys[kv.Key] = VideoUrl(uri, kv.Value);

        List<PlaylistItem> recomends = null;
        if (cache.related != null && cache.related.Count > 0)
        {
            var headers_image = httpHeaders(init.host, HeadersModel.InitOrNull(init.headers_image));
            recomends = cache.related.Select(r => new PlaylistItem()
            {
                name = r.name,
                video = r.video.StartsWith("http") ? r.video : $"{host}/{r.video}",
                picture = string.IsNullOrEmpty(r.picture) ? r.picture : HostImgProxy(init, r.picture, 110, headers_image),
                json = true
            }).ToList();
        }

        return Json(new Shared.Models.SISI.OnResult.StreamItem()
        {
            qualitys = qualitys,
            recomends = recomends
        });
    }

    [HttpGet]
    [Route("javguru/video")]
    [Route("javguru/video.m3u8")]
    [Route("javguru/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked())
            return badInitMsg;

        var cache = await ResolveAsync(uri);
        if (cache?.links == null || cache.links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        if (string.IsNullOrEmpty(q) || !cache.links.TryGetValue(q, out JavGuruStream s))
            s = cache.links.Values.First();

        string referer = s.referer ?? (JavGuruTo.SiteHost + "/");
        string origin = referer;
        try { origin = new Uri(referer).GetLeftPart(UriPartial.Authority); } catch { }

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JavGuruTo.ChromeUA),
            ("Referer", referer),
            ("Origin", origin)
        ));

        return Redirect(HostStreamProxy(s.url, headers));
    }
}
