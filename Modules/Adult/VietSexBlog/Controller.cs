using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.HTTP;
using Shared.Services.Hybrid;
using System;
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
    async public Task<ActionResult> Index(string search, string sort, string c, string t, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // danh sach "Video lien quan" (popup long-press)
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

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

    #region info (popup long-press)
    ActionResult EmptyInfo() => Content("{\"items\":[],\"groups\":[]}", "application/json");

    [HttpGet]
    [Route("vietsexblog/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return EmptyInfo();

        string pageUrl = VietSexBlogTo.NormalizePageUrl(uri);
        if (pageUrl == null)
            return EmptyInfo();

        string key = ipkey($"vietsexblog:info:{pageUrl}");
        if (hybridCache.TryGetValue(key, out string hit) && !string.IsNullOrEmpty(hit))
            return Content(hit, "application/json");

        string html = await GetPageAsync(pageUrl);
        var chips = VietSexBlogTo.Chips(html ?? "");
        string json = InfoJson(chips, uri);
        if (chips.Count > 0)
            hybridCache.Set(key, json, cacheTime(360));
        WarmRelated(pageUrl);
        return Content(json, "application/json");
    }

    string InfoJson(List<(string kind, string slug, string name)> chips, string uri)
    {
        var groups = new List<object>();

        void Add(string title, string kind)
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/vietsexblog?c=" + HttpUtility.UrlEncode(kind + "/" + x.slug)
            }).ToList();
            if (items.Count == 0) return;
            groups.Add(new { title, items });
        }

        Add("Thể loại", "the-loai");
        Add("Từ khóa", "tu-khoa");

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = $"{host}/vietsexblog?rel=" + HttpUtility.UrlEncode(uri)
                }
            }
        });

        return System.Text.Json.JsonSerializer.Serialize(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = VietSexBlogTo.NormalizePageUrl(rel);
        if (pageUrl == null)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var playlists = await GetRelatedAsync(pageUrl);

        return PlaylistResult(playlists, false, VietSexBlogTo.Menu(host), total_pages: 1);
    }

    async Task<List<PlaylistItem>> GetRelatedAsync(string pageUrl)
    {
        string key = ipkey($"vietsexblog:rel:{pageUrl}");
        if (hybridCache.TryGetValue(key, out string raw) && !string.IsNullOrEmpty(raw))
        {
            try
            {
                var hit = System.Text.Json.JsonSerializer.Deserialize<List<PlaylistItem>>(raw);
                if (hit != null) return hit;
            }
            catch { }
        }

        var list = VietSexBlogTo.PlaylistRelated("vietsexblog/vidosik",
            await GetPageAsync(pageUrl) ?? "");
        if (list.Count > 0)
            hybridCache.Set(key, System.Text.Json.JsonSerializer.Serialize(list), cacheTime(180));
        return list;
    }

    // warm nen khi popup mo: bam "Video lien quan" la cache hit
    void WarmRelated(string pageUrl)
    {
        _ = Task.Run(async () =>
        {
            try { await GetRelatedAsync(pageUrl); }
            catch { }
        });
    }

    async Task<string> GetPageAsync(string url)
    {
        string html = null;
        try
        {
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            });
        }
        catch { }
        return html;
    }
    #endregion

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

                // Server Blogger (video.g?token=) khong phai file phat duoc —
                // mo bang Playwright roi trich URL googlevideo (itag 18/22).
                foreach (var kv in links.ToList())
                {
                    if (!VietSexBlogTo.IsBlogger(kv.Value))
                        continue;

                    var mp4s = await ResolveBloggerAsync(kv.Value, url);
                    links.Remove(kv.Key);

                    if (mp4s == null || mp4s.Count == 0)
                        continue;

                    foreach (var m in mp4s)
                        links.TryAdd(m.Key, m.Value);
                }

                if (links.Count == 0)
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

    // Trang video.g cua Blogger nhung san URL googlevideo trong HTML (da
    // escape \u003d / \u0026) nen GET thuong la du. Playwright chi la luoi
    // do khi Blogger doi sang render bang JS.
    async Task<Dictionary<string, string>> ResolveBloggerAsync(string embedUrl, string referer)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", VietSexBlogTo.ChromeUA),
            ("Referer", referer)
        );

        string html = null;

        try
        {
            await httpHydra.GetSpan(embedUrl, span =>
            {
                html = span.ToString();
            }, addheaders: headers);

            var mp4s = VietSexBlogTo.ExtractMp4s(html);
            if (mp4s.Count > 0)
                return mp4s;
        }
        catch { }

        try
        {
            html = null;

            var pwHeaders = httpHeaders(init, headers);
            await PlaywrightHttp.GetSpan(init.plugin, init.cors(embedUrl, pwHeaders, requestInfo), span =>
            {
                html = span.ToString();
            }, pwHeaders, proxy_data);
        }
        catch
        {
            return null;
        }

        return VietSexBlogTo.ExtractMp4s(html);
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

        // HLS di route .m3u8, MP4 (Blogger/googlevideo) di route .mp4 —
        // app bao "no EXTM3U delimiter" neu lan lon.
        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/vietsexblog/{(v.Value.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0 ? "video.m3u8" : "video.mp4")}"
            + $"?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("vietsexblog/video")]
    [Route("vietsexblog/video.m3u8")]
    [Route("vietsexblog/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        // Player trong app chi dung hls.js khi URL chua ".m3u8".
        // Tra link qua route video.m3u8 cua chinh minh (redirect 302 sang proxy).
        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        string referer = link.Contains("googlevideo.com")
            ? "https://www.blogger.com/"
            : "https://x.vietsex.blog/";

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", VietSexBlogTo.ChromeUA),
            ("referer", referer)
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
