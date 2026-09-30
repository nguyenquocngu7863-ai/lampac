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

        if (hybridCache.TryGetValue(key,
            out List<(string name, string path)> nav)
            && nav != null && nav.Count > 0)
            return JavMoiTo.Menu(host, nav);

        // LAN DAU: tra menu rut gon ngay, warm nav that o background.
        // Ban cu fetch nav (3 attempt noi tiep) CHAN response home.
        // `host` capture truoc vi HttpContext co the da xong khi task
        // background chay. Nav la List<ValueTuple> nen giu inmemory.
        string hostLocal = host;

        _ = Task.Run(async () =>
        {
            try
            {
                string html = await FetchHtmlAsync(
                    JavMoiTo.SiteHost + "/danh-sach/phim-moi");

                var list = JavMoiTo.NavList(html);
                if (list.Count > 0)
                    hybridCache.Set(key, list, cacheTime(360), true);
            }
            catch { }
        });

        return JavMoiTo.Menu(hostLocal, new List<(string, string)>());
    }

    // Site khong cham, no TREO that thuong o ket noi dau (do truc tiep van
    // 200/1s trong khi module mat 14.5s). Ban cu goi noi tiep 3 attempt,
    // attempt 1 treo het timeout thi mat trang 8s+ vo ich. Nay chay song
    // song 2 duong, nhieu vong 4s trong tran 12s, vong nao ve truoc ma co
    // noi dung thi lay ngay.
    async Task<string> FetchHtmlAsync(string url, int attempts = 3)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", JavMoiTo.ChromeUA),
            ("Referer", JavMoiTo.SiteHost + "/"));

        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            int cap = (int)Math.Ceiling(Math.Min(4, left));
            string html = await RaceFetchAsync(url, headers, cap);
            if (!string.IsNullOrEmpty(html))
                return html;
        }

        return null;
    }

    async Task<string> RaceFetchAsync(string url,
        IReadOnlyList<HeadersModel> headers, int seconds)
    {
        string a = null, b = null;

        var t1 = Task.Run(async () =>
        {
            try
            {
                await httpHydra.GetSpan(url, span =>
                {
                    a = span.ToString();
                }, addheaders: headers);
            }
            catch { }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await httpHydra.GetSpan(url, span =>
                {
                    b = span.ToString();
                }, addheaders: headers);
            }
            catch { }
        });

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrEmpty(a))
                return a;

            if (!string.IsNullOrEmpty(b))
                return b;

            if (t1.IsCompleted && t2.IsCompleted)
                break;

            await Task.Delay(200);
        }

        return !string.IsNullOrEmpty(a) ? a : b;
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
