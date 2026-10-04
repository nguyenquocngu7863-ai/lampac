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

        // LAN DAU: fetch nav CHAN DONG BO (1 trang HTTP nhe, vai giay)
        // de tra menu du ngay. Ban cu warm o background -> response dau
        // rut gon, app cache luon ban thieu cho ca phien (user thay
        // "the loai an lan dau"). Fail moi warm nen + tra rut gon.
        string hostLocal = host;

        try
        {
            string html = await FetchHtmlAsync(JavMoiTo.SiteHost + "/");

            var list = JavMoiTo.NavList(html);
            if (list.Count > 0)
            {
                hybridCache.Set(key, list, cacheTime(360), true);
                return JavMoiTo.Menu(host, list);
            }
        }
        catch { }

        _ = Task.Run(async () =>
        {
            try
            {
                // Nav PHAI lay tu TRANG CHU `/`, khong phai `/danh-sach/phim-moi`.
                // Do 2026-10-03: trang chu 24 danh muc, trang danh sach chi 23
                // (thieu `/the-loai/khong-che` — muc do `home-v2 movie-list-index`
                // `more-list-index` chi render o trang chu).
                string html = await FetchHtmlAsync(
                    JavMoiTo.SiteHost + "/");

                var list = JavMoiTo.NavList(html);
                if (list.Count > 0)
                    hybridCache.Set(key, list, cacheTime(360), true);
            }
            catch { }
        });

        return JavMoiTo.Menu(hostLocal, new List<(string, string)>());
    }

    // Site khong cham, no TREO that thuong o ket noi dau (do truc tiep van
    // 200/1s trong khi module mat 14.5s). Moi vong chay SONG 2 duong:
    //   duong 1 = host dang chon (x. — do chu nhung hay rought SSL)
    //   duong 2 = host phu cua site (z. — on dinh 3/3 200)
    // Ben nao ve truoc ma co noi dung thi lay ngay -> x. hong van con z.,
    // khong bao gio tra 0 phim. Vong 4s, tran 12s.
    // `allowSwap=false` = chi do dung chinh URL do, khong nhay sang host phu
    // — dung khi phai biet ro URL nay co SONG hay khong.
    // `seconds` = han cho mot lan (mac dinh 12s).
    async Task<string> FetchHtmlAsync(string url,
        bool allowSwap = true, int seconds = 12)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            int cap = (int)Math.Ceiling(Math.Min(4, left));
            string html = await RaceFetchAsync(url, cap, allowSwap);
            if (!string.IsNullOrEmpty(html))
                return html;
        }

        return null;
    }

    // Referer lay GOC cua chinh URL dang goi, khong lay SiteHost — vi khi
    // duong 2 (host phu) chay thi Referer cua no phai theo host do.
    static IReadOnlyList<HeadersModel> HFor(string url)
        => HeadersModel.Init(
            ("User-Agent", JavMoiTo.ChromeUA),
            ("Referer", JavMoiTo.Origin(url) + "/"));

    async Task<string> RaceFetchAsync(string url,
        int seconds, bool allowSwap)
    {
        string a = null, b = null;

        // Duong 2 la host phu (x <-> z). Neu khong cho phep nhay hoac URL
        // khong thuoc host nay thi quay lai URL goc — van giu duoc loi
        // chay 2 song nhu ban cu.
        string alt = allowSwap
            ? (JavMoiTo.SwapHost(url) ?? url)
            : url;

        var t1 = Task.Run(async () =>
        {
            try
            {
                await httpHydra.GetSpan(url, span =>
                {
                    a = span.ToString();
                }, addheaders: HFor(url));
            }
            catch { }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                await httpHydra.GetSpan(alt, span =>
                {
                    b = span.ToString();
                }, addheaders: HFor(alt));
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

        // Origin lay tu trang chi tiet. Nhung trang chi tiet co the den tu
        // HOST PHU (x. hong -> z. phuc vu) nen ben duoi se do ca 2 host.
        string origin = JavMoiTo.Origin(uri);
        string url = JavMoiTo.PlaylistUrl(path, origin);

        if (path.EndsWith("/main.m3u8", StringComparison.OrdinalIgnoreCase))
        {
            string alt = JavMoiTo.PlaylistUrl(
                JavMoiTo.AltPaths(path), origin);

            if (!string.IsNullOrEmpty(alt))
            {
                string altHtml = await FetchHtmlAsync(alt);
                if (!string.IsNullOrEmpty(altHtml)
                    && altHtml.Contains("#EXTINF"))
                    url = alt;
            }
        }

        // Do ro rang URL nay co THAT SU song khong: x. hay rought nen
        // trang chi tiet lay duoc co the tu z. ma `url` van dung origin x.
        // Host nao co segment (#EXTINF) thi lay; ca hai deu khong thi giu
        // URL goc, khong lam het hon truoc day.
        if (!await HasSegmentsAsync(url))
        {
            string swap = JavMoiTo.SwapHost(url);
            if (!string.IsNullOrEmpty(swap)
                && await HasSegmentsAsync(swap))
                url = swap;
        }

        hybridCache.Set(memKey, url, cacheTime(20));
        return url;
    }

    // Do mot URL rieng (khong cho phep nhay host) de biet no co song.
    // 5s la du — m3u8 treo 5s thi co cung khong dung duoc.
    async Task<bool> HasSegmentsAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        string body = await FetchHtmlAsync(url,
            allowSwap: false, seconds: 5);

        return !string.IsNullOrEmpty(body)
            && body.Contains("#EXTINF");
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
            ("Referer", JavMoiTo.Origin(url) + "/")));

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
            ("Referer", JavMoiTo.Origin(url) + "/")));

        return Redirect(HostStreamProxy(url, headers));
    }
}
