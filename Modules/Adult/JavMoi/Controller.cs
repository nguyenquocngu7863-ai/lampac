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

namespace JavMoi;

public class JavMoiController : BaseSisiController
{
    public JavMoiController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javmoi")]
    async public Task<ActionResult> Index(
        string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        var cache = await InvokeCacheResult(
            ipkey($"javmoi:{search}:{c}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string url = JavMoiTo.Uri(init.host, search, c, pg);
            string html = await FetchHtmlAsync(url);

            var playlists = JavMoiTo.Playlist(
                "javmoi/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync());
    }

    async Task<List<MenuItem>> MenuAsync()
    {
        string key = ipkey("javmoi:nav");

        if (!hybridCache.TryGetValue(key,
            out List<(string name, string path)> nav)
            || nav == null || nav.Count == 0)
        {
            string html = await FetchHtmlAsync(
                JavMoiTo.SiteHost + "/danh-sach/phim-moi");

            nav = JavMoiTo.NavList(html);
            if (nav.Count > 0)
                hybridCache.Set(key, nav, cacheTime(360));
        }

        return JavMoiTo.Menu(host, nav);
    }

    // Site khong cham, no TREO: do duoc 15 lan thi 7 lan code 200
    // (0.7-1.6s) va 8 lan code 000 (15.2s = dung bo dem timeout).
    // Nen dung "timeout ngan + retry nhieu" chu KHONG dung
    // "timeout dai + mot lan": rut thoi gian che tu 40s xuong ~10s.
    // KHONG probe rồi chặn link — chặn nhầm sẽ giết phim dang chay.
    // 3 lan: do 5 lan chi 1 lan 200, 2 lan la 2/5 kha nhat — hay treo.
    async Task<string> FetchHtmlAsync(string url, int attempts = 3)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", JavMoiTo.ChromeUA),
            ("Referer", JavMoiTo.SiteHost + "/"));

        for (int i = 0; i < attempts; i++)
        {
            if (i > 0)
                await Task.Delay(300);

            try
            {
                string html = null;
                await httpHydra.GetSpan(url, span =>
                {
                    html = span.ToString();
                }, addheaders: headers);

                if (!string.IsNullOrEmpty(html))
                    return html;
            }
            catch { }
        }

        return null;
    }

    // /phim/<slug> -> /storage/m3u8/<slug>/{main|master|index}.m3u8
    // main.m3u8 tro host ap4r.com ma segment 404 ca trong nuoc lan nuoc
    // ngoai => thu master.m3u8 truoc, co segments thi dung no.
    async Task<string> ResolveAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;

        string memKey = ipkey($"javmoi:stream:{uri}");
        if (hybridCache.TryGetValue(memKey, out string cached)
            && !string.IsNullOrEmpty(cached))
            return cached;

        string html = await FetchHtmlAsync(uri);
        string path = JavMoiTo.StreamPath(html);

        if (string.IsNullOrEmpty(path))
            return null;

        string url = JavMoiTo.PlaylistUrl(path);

        if (path.EndsWith("/main.m3u8", StringComparison.OrdinalIgnoreCase))
        {
            string alt = JavMoiTo.PlaylistUrl(
                JavMoiTo.AltPaths(path));

            if (!string.IsNullOrEmpty(alt))
            {
                string altHtml = await FetchHtmlAsync(alt);
                if (!string.IsNullOrEmpty(altHtml)
                    && altHtml.Contains("#EXTINF"))
                    url = alt;
            }
        }

        hybridCache.Set(memKey, url, cacheTime(20));
        return url;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javmoi/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var url = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(url))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JavMoiTo.ChromeUA),
            ("Referer", JavMoiTo.SiteHost + "/")));

        return Json(new Dictionary<string, string>()
        {
            ["HLS"] = $"{host}/javmoi/video.m3u8"
                + $"?uri={HttpUtility.UrlEncode(uri)}"
        });
    }

    [HttpGet]
    [Route("javmoi/video.m3u8")]
    async public Task<ActionResult> Video(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var url = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(url))
            return OnError("stream_links", refresh_proxy: true);

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JavMoiTo.ChromeUA),
            ("Referer", JavMoiTo.SiteHost + "/")));

        return Redirect(HostStreamProxy(url, headers));
    }
}
