using Microsoft.AspNetCore.Mvc;
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

namespace PubJav;

public class PubJavController : BaseSisiController
{
    public PubJavController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("pubjav")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"pubjav:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string page = await GetPageAsync(PubJavTo.Uri(init.host, search, c, pg));
            var playlists = PubJavTo.Playlist("pubjav/vidosik", page);

            // Search ma khong co ket qua (site chi index slug tieng Anh,
            // ten Nhap dien se ra 0 phim) -> `success` + list rong de app
            // hien "khong co ket qua", khong phai loi.
            if (playlists.Count == 0 && !string.IsNullOrWhiteSpace(search))
                return e.Success(new List<PlaylistItem>());

            if (playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync());
    }

    // ================= MENU (danh muc) =================

    // Menu mang 627 muc (315 genre + 312 studio) nen dung `hybridCache` 12h
    // va chi tao lai 1 lan; neu khong cache thi phai FETCH.
    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("pubjav:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        // KHONG chay nen roi tra menu rut gon ngay: app cache response dau
        // cho ca phien -> the loai/han phim khong bao gio hien (JavCt,
        // MissAV da gap). `/genres` + `/studios` la 2 trang tinh 140KB,
        // fetch song song ~1-3s nen cho lay xong roi tra menu that.
        try
        {
            var genresTask = TaxonomiesAsync("genres", "genre/");
            var studiosTask = TaxonomiesAsync("studios", "studio/");
            await Task.WhenAll(genresTask, studiosTask);

            var g = await genresTask;
            var s = await studiosTask;

            if (g.Count > 0 || s.Count > 0)
            {
                var menu = PubJavTo.Menu(hostLocal, g, s);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                return menu;
            }
        }
        catch { }

        return PubJavTo.Menu(hostLocal, null, null);
    }

    async Task<List<(string slug, string name)>> TaxonomiesAsync(string page, string prefix)
    {
        string memKey = ipkey($"pubjav:tax:{page}");

        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync($"{PubJavTo.SiteHost}/{page}");
        var res = PubJavTo.Taxonomies(html, prefix);
        if (res.Count == 0)
            return res;

        // inmemory: List<ValueTuple> qua file cache co the doc lai khong duoc
        // (giong menu), giu object song trong memory la chac nhat.
        hybridCache.Set(memKey, res, cacheTime(720), true);
        return res;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("pubjav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var servers = await IframesAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in servers)
        {
            result[server.Label] = StreamRoute(uri, server.Label, server.Kind);
        }

        if (rch?.enable == true)
            return OnResult(result);

        return Json(result);
    }

    // App bo sau ~30s (thuc te 18s) nen phan trong deadline 22s.
    [HttpGet]
    [Route("pubjav/video.mp4")]
    [Route("pubjav/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var servers = await IframesAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Thu `q` user chon truoc, fail thi tu thu server cung dang con lai.
        // KHONG fallback cheo dang: app chon player theo duoi url, tra mp4
        // qua route .m3u8 se ra "no EXTM3U delimiter".
        var ordered = Order(servers, q);
        if (ordered.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        long deadline = Environment.TickCount64 + 22000;

        foreach (var server in ordered)
        {
            long left = deadline - Environment.TickCount64;
            if (left < 3000)
                break;

            var (url, referer) = await StreamAsync(uri, server, (int)Math.Min(left, 12000));
            if (string.IsNullOrEmpty(url))
                continue;

            return Redirect(HostStreamProxy(url, StreamHeaders(server, referer)));
        }

        return OnError("stream_links", refresh_proxy: true);
    }

    // ================= IFRAME (dung chung cho vidosik + video) =================

    // 1 lan fetch detail + N lan POST /ajax/player (noi tiep next_pt) cho ra
    // iframe cua tat ca server. Token pt chi dung MOT LAN nen phai noi tiep,
    // dung lai pt cu se ra 403.
    public async Task<List<PubJavServer>> IframesAsync(string uri)
    {
        string pageUrl = PubJavTo.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return null;

        string memKey = ipkey($"pubjav:iframe:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<PubJavServer> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        SemaphorManager semaphore = null;
        if (rch?.enable != true)
        {
            semaphore = new SemaphorManager($"pubjav:iframe:{pageUrl}", TimeSpan.FromSeconds(20));
            if (!await semaphore.WaitAsync())
                return null;
        }

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<PubJavServer> current) &&
                current != null && current.Count > 0)
                return current;

            string detail = await GetPageAsync(pageUrl);
            var (filmId, pt, pk) = PubJavTo.Tokens(detail);
            if (string.IsNullOrEmpty(filmId) || string.IsNullOrEmpty(pt))
                return null;

            var episodes = PubJavTo.Servers(detail, filmId);
            if (episodes.Count == 0)
                return null;

            var result = new List<PubJavServer>();
            foreach (var (label, episode) in episodes)
            {
                if (!PubJavTo.IsSupported(label))
                    continue;

                string json = await PostPlayerAsync(pageUrl, filmId, episode, pt);
                var (html, nextPt, nextPk) = PubJavTo.DecryptResponse(json, pk);

                if (!string.IsNullOrEmpty(nextPt) && !string.IsNullOrEmpty(nextPk))
                {
                    pt = nextPt;
                    pk = nextPk;
                }

                string iframe = PubJavTo.IframeUrl(html);
                string kind = PubJavTo.Kind(iframe);
                if (string.IsNullOrEmpty(iframe) || string.IsNullOrEmpty(kind))
                    continue;

                result.Add(new PubJavServer()
                {
                    Label = label,
                    Kind = kind,
                    Iframe = iframe
                });
            }

            if (result.Count == 0)
                return null;

            result = Order(result, null);
            proxyManager?.Success();
            hybridCache.Set(memKey, result, cacheTime(20));
            return result;
        }
        finally
        {
            semaphore?.Release();
        }
    }

    // ================= RESOLVE =================

    public async Task<(string url, string referer)> StreamAsync(
        string uri, PubJavServer server, int maxTime)
    {
        string memKey = ipkey($"pubjav:stream:{uri}:{server.Label}");

        if (hybridCache.TryGetValue(memKey, out (string url, string referer) hit) &&
            !string.IsNullOrEmpty(hit.url))
            return hit;

        string url = null;
        string referer = null;

        if (PubJavTo.IsUpn(server.Iframe))
        {
            // US/PP: `.../#<id>` -> GET /api/v1/video?id= -> hex -> AES-CBC
            var upn = await PubJavTo.UpnResolveAsync(server.Iframe,
                Math.Min(maxTime, 10));

            url = upn.url;
            referer = upn.referer;
        }
        else if (PubJavTo.IsPlaymate(server.Iframe))
        {
            // PM: POST /api/s {"c":id} -> `sx` = master .txt
            var pm = await PubJavTo.PlaymateResolveAsync(server.Iframe,
                Math.Min(maxTime, 10));

            url = pm.url;
            referer = pm.referer;
        }
        else if (server.Kind == "hls")
        {
            // FL/SW: trang player StreamHG, giai packer -> `var links`
            string player = PubJavTo.StreamHgUrl(server.Iframe);
            string html = await PubJavTo.CurlGetRetry(player, PubJavTo.SiteHost + "/",
                null, 2, Math.Min(maxTime, 10));

            var masters = PubJavTo.StreamHgMasters(html, player);
            url = await PubJavTo.FirstWorkingMaster(masters, Math.Min(maxTime, 8));
            referer = PubJavTo.HostOf(player) + "/";
        }
        else if (server.Iframe.IndexOf("strtape", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 server.Iframe.IndexOf("streamtape", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // ST: trang player Streamtape -> `get_video` -> 302 sang .mp4
            string html = await PubJavTo.CurlGetRetry(server.Iframe, PubJavTo.SiteHost + "/",
                null, 2, Math.Min(maxTime, 10));

            string api = PubJavTo.StreamtapeUrl(html);
            if (!string.IsNullOrEmpty(api))
                url = await PubJavTo.CurlFinalUrl(api, PubJavTo.SiteHost + "/",
                    Math.Min(maxTime, 8));
        }
        else
        {
            // DD: cong thuc DoodStream — referer PHAT sinh theo tung video
            var dood = await PubJavTo.DoodSourceAsync(server.Iframe,
                PubJavTo.SiteHost + "/", Math.Min(maxTime, 10));

            url = dood.url;
            referer = dood.referer;
        }

        // KHONG check duoi file: URL cua DoodStream khong co `.mp4`
        // (`.../cloudatacdn.com/<hash>/<token>`) du CDN tra `Content-Type:
        // video/mp4`. Check `IsMedia` o day se loai nham server DD.
        if (string.IsNullOrEmpty(url) ||
            !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        // Server mp4 (ST/DD) co the chet san tren upstream — mot so phim
        // tra `{"status":500,"msg":"Sorry, error on our side!"}`. Probe 1KB
        // de lo, app fallback duoc sang server mp4 con lai.
        if (server.Kind == "mp4" &&
            !await PubJavTo.ProbeMediaAsync(url, referer, Math.Min(maxTime, 6)))
        {
            Console.WriteLine($"PubJav: dead media {server.Label}");
            return (null, null);
        }

        // Link co token ngan han -> cache 1 phut, khong cache 10 phut
        hybridCache.Set(memKey, (url, referer), cacheTime(1));
        return (url, referer);
    }

    // Server user chon luon len dau, con lai giu thu tu do do duoc.
    // KHONG lay thu tu xuat hien tren trang HTML: no khong phai thu tu
    // chat luong, ma chi la thu tu nut tren web.
    //
    // Chi giu lai server CUNG DANG voi server da chon. App Lampa chon
    // player theo DUOI url cua route (video.m3u8 vs video.mp4) nen tra
    // mp4 qua route .m3u8 se bao "no EXTM3U delimiter" — thay vi fallback
    // qua dang, thu het server cung dang roi tra loi ro rang.
    static List<PubJavServer> Order(List<PubJavServer> servers, string q)
    {
        var sorted = servers
            .OrderBy(x => PubJavTo.Priority(x.Label))
            .ToList();

        if (string.IsNullOrEmpty(q))
            return sorted;

        var pick = sorted.FirstOrDefault(x =>
            x.Label.Equals(q, StringComparison.OrdinalIgnoreCase));

        if (pick == null)
            return new List<PubJavServer>();

        return new List<PubJavServer>()
        {
            pick
        }.Concat(sorted.Where(x => x.Kind == pick.Kind &&
                                 !x.Label.Equals(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    // ================= HTTP =================

    async Task<string> GetPageAsync(string url)
    {
        // Site treo that thuong o ket noi dau, nhung goi lai ngay sau thuong
        // thong (do truc tiep: 000, 200, 000 xen ke). Vong 6s+8s van thua khi
        // ca 2 vong deu roi vao luc treo. Nay chia nho thanh nhieu vong 4s
        // trong cung tran 14s: moi vong la 2 ket noi moi (httpHydra + Http),
        // vong nao ve truoc ma co noi dung thi lay ngay.
        var deadline = DateTime.UtcNow.AddSeconds(14);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            string page = await RacePageAsync(url, (int)Math.Ceiling(Math.Min(4, left)));
            if (!string.IsNullOrEmpty(page))
                return page;
        }

        return null;
    }

    async Task<string> RacePageAsync(string url, int seconds)
    {
        // Chay song song 2 duong httpHydra + Http.Get thang nao ve truoc ma
        // co noi dung thi lay ngay, tran tong `seconds` giay. Ban cu goi noi
        // tiep (hydra treo het 20s roi Http.Get them 20s) nen trang home lan
        // dau >25s, app timeout ~15s va bao "khong load duoc home".
        string hydraPage = null, directPage = null;

        var t1 = Task.Run(async () =>
        {
            try { await httpHydra.GetSpan(url, span => hydraPage = span.ToString(), addheaders: PageHeaders()); }
            catch { }
        });

        var t2 = Task.Run(async () =>
        {
            try
            {
                directPage = await Http.Get(
                    url,
                    timeoutSeconds: Math.Max(6, seconds),
                    httpversion: init.httpversion,
                    proxy: proxy,
                    headers: PageHeaders());
            }
            catch { }
        });

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrEmpty(hydraPage))
                return hydraPage;

            if (!string.IsNullOrEmpty(directPage))
                return directPage;

            if (t1.IsCompleted && t2.IsCompleted)
                break;

            await Task.Delay(200);
        }

        return !string.IsNullOrEmpty(hydraPage) ? hydraPage : directPage;
    }

    async Task<string> PostPlayerAsync(string pageUrl, string filmId,
        string episode, string pt)
    {
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["episode"] = episode,
                ["filmId"] = filmId,
                ["pt"] = pt
            });

            return await Http.Post(
                PubJavTo.PlayerApi,
                content,
                timeoutSeconds: Math.Max(20, init.httptimeout),
                headers: ApiHeaders(pageUrl),
                proxy: proxy,
                httpversion: init.httpversion,
                statusCodeOK: true,
                disposeData: true);
        }
        catch
        {
            return null;
        }
    }

    string StreamRoute(string uri, string label, string kind)
    {
        string route = kind == "hls" ? "video.m3u8" : "video.mp4";
        return $"{host}/pubjav/{route}?uri={HttpUtility.UrlEncode(uri)}" +
               $"&q={HttpUtility.UrlEncode(label)}";
    }

    static IReadOnlyList<HeadersModel> PageHeaders()
    {
        return HeadersModel.Init(
            ("User-Agent", PubJavTo.ChromeUA),
            ("Referer", PubJavTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
        );
    }

    static IReadOnlyList<HeadersModel> ApiHeaders(string referer)
    {
        return HeadersModel.Init(
            ("User-Agent", PubJavTo.ChromeUA),
            ("Referer", referer),
            ("Origin", PubJavTo.SiteHost),
            ("Accept", "application/json, text/plain, */*"),
            ("X-Requested-With", "XMLHttpRequest")
        );
    }

    // `referer` co the null — CHI dung cho DoodStream (Referer phai dung
    // host API theo tung phim). Khong gan no cho StreamHG/Streamtape.
    static IReadOnlyList<HeadersModel> StreamHeaders(PubJavServer server, string referer)
    {
        var res = new List<HeadersModel>()
        {
            new("User-Agent", PubJavTo.ChromeUA)
        };

        if (!string.IsNullOrEmpty(referer))
            res.Add(new("Referer", referer));

        res.Add(new("Origin", PubJavTo.HostOf(referer ?? server.Iframe)));
        res.Add(new("Accept", "*/*"));

        return res;
    }
}

public class PubJavServer
{
    public string Label { get; set; }
    public string Kind { get; set; }
    public string Iframe { get; set; }
}
