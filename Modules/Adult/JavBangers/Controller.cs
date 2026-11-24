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

namespace JavBangers;

public class JavBangersController : BaseSisiController
{
    public JavBangersController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javbangers")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // clamp TRUOC cache key (context khong sort duoc -> ve mac dinh cua context)
        sort = JbTo.ClampSort(sort, search, c);

        // search cua site KHONG phan trang bang URL (/search/<kw>/2/ = 404, &page= bi bo qua,
        // ajax get_block tra ve y het trang 1) => chi tra trang 1
        if (!string.IsNullOrWhiteSpace(search) && pg > 1)
            pg = 1;

        var menuTask = MenuAsync(search, c, sort);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"javbangers:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string url = !string.IsNullOrWhiteSpace(search)
                ? JbTo.SearchUrl(init.host, search, sort, pg)
                : ListUrlWithSort(init.host, c, sort, pg);

            string html = await GetPageAsync(url);

            if (JbTo.IsBlocked(html))
                return e.Fail("playlists", refresh_proxy: true);

            var playlists = JbTo.Playlist("javbangers/vidosik", html);
            int total = string.IsNullOrWhiteSpace(search) ? JbTo.Pages(html) : 1;
            return e.Success((playlists, total));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await menuTask,
            total_pages: cache.Value.total_pages);
    }

    // sort chi gui khi context do co sort (ClampSort da bao dam gia tri hop le)
    static string ListUrlWithSort(string host, string c, string sort, int pg)
    {
        string url = JbTo.ListUrl(host, c, pg);
        if (!string.IsNullOrEmpty(sort))
            url += (url.Contains('?') ? "&" : "?") + "sort_by=" + HttpUtility.UrlEncode(sort);
        return url;
    }

    // ================= Menu =================
    async Task<List<MenuItem>> MenuAsync(string search, string c, string sort)
    {
        string memKey = ipkey("javbangers:cats:v1");
        var cats = JbTo.FallbackCats;

        if (hybridCache.TryGetValue(memKey, out List<(string path, string name)> hit) && hit != null && hit.Count > 0)
            cats = hit;
        else
        {
            try
            {
                string html = await GetPageAsync((string.IsNullOrEmpty(init.host) ? JbTo.SiteHost : init.host).TrimEnd('/') + "/categories/");
                var t = JbTo.CategoryIndex(html);
                if (t.Count > 0)
                {
                    cats = t;
                    hybridCache.Set(memKey, cats, cacheTime(720), true);
                }
            }
            catch { }
        }

        return JbTo.Menu(host, cats, search, c, sort);
    }

    // ================= Vidosik =================
    [HttpGet, Staticache(manually: true)]
    [Route("javbangers/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string stream = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(stream))
            return OnError("stream_links", refresh_proxy: true);

        return Json(new Dictionary<string, string>()
        {
            ["MP4"] = $"{host}/javbangers/video.mp4?uri={HttpUtility.UrlEncode(uri)}"
        });
    }

    // ================= Video =================
    [HttpGet]
    [Route("javbangers/video.mp4")]
    async public Task<ActionResult> Video(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string stream = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(stream))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JbTo.ChromeUA),
            ("Referer", JbTo.SiteHost + "/")
        ));

        return Redirect(HostStreamProxy(stream, direct));
    }

    // ================= Resolve =================
    // trang /video/<id>/ tra 404 cho guest => lay stream tu /embed/<id>
    async Task<string> ResolveAsync(string uri)
    {
        string id = JbTo.VideoId(uri);
        if (string.IsNullOrEmpty(id))
            return null;

        string memKey = ipkey($"javbangers:view:{id}");
        if (hybridCache.TryGetValue(memKey, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        string embed = await GetPageAsync(JbTo.EmbedUrl(init.host, id), embed: true);
        string stream = JbTo.StreamFromEmbed(embed);
        if (string.IsNullOrEmpty(stream))
            return null;

        hybridCache.Set(memKey, stream, cacheTime(120));
        return stream;
    }

    #region popup (long-press)
    // Trang detail 404 => thong tin phim lay tu flashvars cua /embed/<id>:
    //   video_categories: 'Creampie, Censored, Hardcore'
    //   video_models:     '...' (thuong rong)
    //   video_tags:       'Hardcore, Blowjob, pov'
    // The loai/Models duoc gan link qua index /categories/ va /models/ (cache 12h).
    // Tags: site KHONG co trang tag (/tags/ = 502, /tags/<slug>/ = 404, sitemap khong co type=tags)
    //       => khong tao chip. Lien quan: khong co block related (khong co trang detail) => bo.
    [HttpGet]
    [Route("javbangers/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = new { items = new object[0], groups = new object[0] };

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string id = JbTo.VideoId(uri);
        if (id == null)
            return Json(empty);

        string cacheKey = ipkey($"javbangers:info:{id}");
        if (!hybridCache.TryGetValue(cacheKey, out string embed) || string.IsNullOrEmpty(embed))
        {
            embed = await GetPageAsync(JbTo.EmbedUrl(init.host, id), embed: true);
            if (string.IsNullOrEmpty(embed))
                return Json(empty);
            hybridCache.Set(cacheKey, embed, cacheTime(360));
        }

        var groups = new List<object>();

        var catNames = JbTo.Names(embed, "video_categories");
        if (catNames.Count > 0)
        {
            var map = await IndexMapAsync("categories");
            var items = catNames
                .Where(n => map.ContainsKey(n))
                .Select(n => new { title = n, url = JbTo.MenuUrl(host, map[n]) })
                .ToList();

            if (items.Count > 0)
                groups.Add(new { title = "Thể loại", items });
        }

        var modelNames = JbTo.Names(embed, "video_models");
        if (modelNames.Count > 0)
        {
            var map = await IndexMapAsync("models");
            var items = modelNames
                .Where(n => map.ContainsKey(n))
                .Select(n => new { title = n, url = JbTo.MenuUrl(host, map[n]) })
                .ToList();

            if (items.Count > 0)
                groups.Add(new { title = "Diễn viên", items });
        }

        if (groups.Count == 0)
            return Json(empty);

        return Json(new { items = new object[0], groups });
    }

    async Task<Dictionary<string, string>> IndexMapAsync(string section)
    {
        string key = ipkey($"javbangers:idx:{section}");
        if (hybridCache.TryGetValue(key, out Dictionary<string, string> hit) && hit != null && hit.Count > 0)
            return hit;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string html = await GetPageAsync((string.IsNullOrEmpty(init.host) ? JbTo.SiteHost : init.host).TrimEnd('/') + "/" + section + "/");
            map = JbTo.NameMap(html, section);
        }
        catch { }

        if (map.Count > 0)
            hybridCache.Set(key, map, cacheTime(720), true);

        return map;
    }
    #endregion

    // ================= Fetch =================
    async Task<string> GetPageAsync(string url, bool embed = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var headers = HeadersModel.Init(
            ("User-Agent", JbTo.ChromeUA),
            ("Accept-Language", "en-US,en;q=0.9,vi;q=0.8"),
            ("Referer", JbTo.SiteHost + (embed ? "/latest-updates/" : "/")));

        string last = null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string a = null, b = null;

            var t1 = Task.Run(async () =>
            {
                try
                {
                    await httpHydra.GetSpan(url, span => { a = span.ToString(); }, addheaders: headers);
                }
                catch { }
            });

            var t2 = Task.Run(async () =>
            {
                try
                {
                    b = await Http.Get(
                        url,
                        timeoutSeconds: 12,
                        httpversion: init.httpversion,
                        proxy: proxy,
                        headers: headers);
                }
                catch { }
            });

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (!string.IsNullOrEmpty(a) && a.Length >= 1000) break;
                if (!string.IsNullOrEmpty(b) && b.Length >= 1000) break;
                if (t1.IsCompleted && t2.IsCompleted) break;
                await Task.Delay(200);
            }

            last = !string.IsNullOrEmpty(a) && a.Length >= 1000 ? a : b;

            if (!JbTo.IsBlocked(last))
                return last;

            await Task.Delay(1000);
        }

        return last;
    }
}
