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

namespace JavTsunami;

public class JavTsunamiController : BaseSisiController
{
    public JavTsunamiController() : base(ModInit.conf) { }

    static long Ms() => Environment.TickCount64;

    // KHONG dung httpHydra cho site nay. Cloudflare cua javtsunami chan
    // client .NET nhung curl chay 0.5-1s — nguoc lai hydra treo 8s roi tra
    // 0 (do timeout cua no khong phu thuoc maxTime), 4 lan = 32s vượt han
    // 20s cua DetailServersAsync nen /vidosik tra 503. Chi curl.
    async Task<string> FetchHtmlAsync(string url, string marker, int attempts = 3, int maxTime = 25, long deadline = 0)
    {
        long tr = Ms();
        string body = await JavTsunamiTo.CurlGetRetry(url, "https://javtsunami.com/", marker, attempts, maxTime, deadline);
        Console.WriteLine($"JavTsunami: curl ({Ms() - tr}ms) len={body?.Length ?? 0}");

        return body;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javtsunami")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"javtsunami:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(JavTsunamiTo.Uri(init.host, search, c, pg), "data-video-id=");
            if (string.IsNullOrEmpty(html))
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            var playlists = JavTsunamiTo.Playlist("javtsunami/vidosik", html);
            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync());
    }

    // Menu "Thể loại" FULL tu trang `/categories` (4 trang, 93 muc).
    // Tags (~1000 muc) TAM NGHI: nhieu tag it phim, submenu dai kho dung.
    // Code lay tag (TagList/TagAll) giu lai, can thi bat lai.
    // Fetch o background, tra menu rut gon ngay de khong chan response
    // home. Cache 12h inmemory (ValueTuple qua file cache doc lai ko duoc).
    async Task<List<MenuItem>> MenuAsync()
    {
        string key = ipkey("javtsunami:menu");

        if (hybridCache.TryGetValue(key, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        _ = Task.Run(async () =>
        {
            try
            {
                var cats = await JavTsunamiTo.CatAll(6, Ms() + 15000);

                // Site treo giua chung: lan 1 chi duoc 56/93. Thu lai 1 lan
                // truoc khi chot, neu khong partial bi dong bang 12h.
                if (cats.Count < 70)
                    cats = await JavTsunamiTo.CatAll(6, Ms() + 15000);

                Console.WriteLine($"JavTsunami: menu cats={cats.Count}");

                if (cats.Count <= 0)
                    return;

                // Du (>=70/93 do duoc) thi 12h; thieu thi 5 phut de warm sau
                // thu lai, khong dong bang ban thieu ca ngay.
                var exp = cats.Count >= 70 ? cacheTime(720) : cacheTime(5);
                hybridCache.Set(key, JavTsunamiTo.Menu(hostLocal, cats), exp, true);
            }
            catch { }
        });

        return JavTsunamiTo.Menu(hostLocal, new List<(string, string)>());
    }

    // Trang detail: tach iframe trong <div class="video-player">. Cache 15
    // phut de khi nguoi dung bam server thu hai thi khong tai lai trang detail.
    async Task<List<JavTsunamiServer>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !JavTsunamiTo.IsSiteUrl(uri))
            return null;

        string memKey = ipkey($"javtsunami:servers:{uri}");
        if (hybridCache.TryGetValue(memKey, out List<JavTsunamiServer> cached) && cached != null && cached.Count > 0)
            return cached;

        // Timeout ngan + nhieu lan thu: lan thanh cong chi 0.3-1s, con lan
        // treo an het --max-time. Deadline 12s de con duoi tran 30s cua client
        // va con cho /video con thoi gian thu server du phong.
        long deadline = Ms() + 12000;
        string detail = await FetchHtmlAsync(JavTsunamiTo.NormalizePageUrl(uri), "video-player", 3, 3, deadline);
        if (string.IsNullOrEmpty(detail))
            return null;

        var servers = JavTsunamiTo.Servers(detail);
        if (servers.Count == 0)
            return null;

        hybridCache.Set(memKey, servers, cacheTime(15));
        return servers;
    }

    // KHONG resolve tai day. /vidosik chi tra danh sach server de app hien ra,
    // server nao nguoi dung BAM moi resolve — chi mot server nen 1-4s, luon duoi
    // tran 30s cua client.
    [HttpGet, Staticache(manually: true)]
    [Route("javtsunami/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var all = await DetailServersAsync(uri);
        if (all == null || all.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // LAY HET server ma trang detail co, chi bo qua server chua xu ly.
        // Phim `category/jav-uncensored` tren site khong co turbovidhls
        // (chi hicherri + vide0) — loc chi Turbo se lam man hinh trong.
        var servers = all.Where(x => JavTsunamiTo.IsSupported(x.Label)).ToList();
        if (servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Do kieu phat cho TAT CA server song song, deadline 6s. Chi server
        // dang 0 (Turbo) moi can fetch — Vide0 la mp4, Hicherri la HLS, biet
        // ngay tu host nen 2 cai kia tra ve ngay lap tuc.
        int total = servers.Count;
        var kinds = new string[total];
        long dl = Ms() + 6000;

        async Task Probe(int i, string pageUrl)
        {
            kinds[i] = await JavTsunamiTo.ServerKindAsync(pageUrl, 4, dl);
        }

        await Task.WhenAll(Enumerable.Range(0, total).Select(i => Probe(i, servers[i].PageUrl)));

        // Nho lai de /video biet dang cua tung server (khong phai do lai).
        for (int i = 0; i < total; i++)
            servers[i].Kind = kinds[i];

        hybridCache.Set(ipkey($"javtsunami:servers:{uri}"), servers, cacheTime(15));

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < total; i++)
        {
            if (dict.ContainsKey(servers[i].Label))
                continue;

            dict[servers[i].Label] = StreamUrl(host, uri, servers[i].Label, kinds[i]);
        }

        return Json(dict);
    }

    // Duoi file PHAI khop dang thuc that cua nguon: app Lampa chi ep hls.js
    // khi URL co `.m3u8`, nen mp4 phat qua `.m3u8` se bao "no EXTM3U
    // delimiter". Kind rong (probe that bai) -> `.m3u8` nhu truoc.
    static string StreamUrl(string host, string uri, string label, string kind)
        => $"{host}/javtsunami/video{(kind == JavTsunamiTo.KindMp4 ? ".mp4" : ".m3u8")}"
         + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(label)}";

    // Resolve DUNG MOT server theo `srv`, roi chuyen tiep sang link phat.
    // `referer` chi DoodStream dung — xem ghi chu trong DoodSourceAsync.
    async Task<List<(string url, string tag, string referer)>> ResolveAsync(
        JavTsunamiServer pick, long deadline)
    {
        // Server Turbo: iframe /t/<id> -> trang player. Marker rong (khong kiem
        // data-hash) vi player co 2 dang: HLS co data-hash, MP4 chi co
        // `var urlPlay` — yeu cau data-h8 se loai sach 1/3 video.
        string player = await JavTsunamiTo.CurlGetRetry(
            pick.PageUrl, "https://javtsunami.com/", null, 6, 5, deadline);
        if (string.IsNullOrEmpty(player))
            return new List<(string, string, string)>();

        // --- Vide0 (DoodStream): 301 sang host doi -> API pass_md5 -> mp4 ---
        if (JavTsunamiTo.IsVide0(pick.PageUrl))
        {
            var (src, refDood) = await JavTsunamiTo.DoodSourceAsync(pick.PageUrl, 6, deadline);
            if (string.IsNullOrEmpty(src))
                return new List<(string, string, string)>();

            return new List<(string, string, string)> { (src, "mp4", refDood) };
        }

        // --- Hicherri (StreamHG): player packer, lay var links{hls4,hls3,hls2} ---
        if (JavTsunamiTo.IsHicherri(pick.PageUrl))
        {
            var masters = JavTsunamiTo.HicherriMasters(player);
            var hvars = await JavTsunamiTo.HicherriVariantsAsync(masters, 5, deadline);
            if (hvars.Count > 0)
                return hvars.Select(x => (x.url, x.tag, (string)null)).ToList();

            return new List<(string, string, string)>();
        }

        // --- Turbo: cung thu tu uu tien voi JavGuruTo.StreamUrls ---
        // m3u8 (data-hash) truoc, mp4 (urlPlay) sau.
        foreach (string media in JavTsunamiTo.StreamUrls(player))
        {
            if (JavTsunamiTo.IsDirectMp4(media))
                return new List<(string, string, string)> { (media, "mp4", null) };

            // Master 2 level -> tach o server, tra playlist media cua variant
            // cao nhat de app khong phai doi level (xem MasterVariants).
            var variants = await JavTsunamiTo.MasterVariants(media, 3, 7, deadline);
            if (variants.Count > 0)
                return variants.Select(x => (x.url, x.tag, (string)null)).ToList();

            // Master khong tach duoc -> tra luon master, hls.js tu xu ly.
            return new List<(string, string, string)> { (media, "1080p", null) };
        }

        return new List<(string, string, string)>();
    }

    [HttpGet]
    [Route("javtsunami/video")]
    [Route("javtsunami/video.mp4")]
    [Route("javtsunami/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string srv, string q = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        // Cho phep q = ten server (tu cache cu) de URL da bookmark/da mo van chay
        if (string.IsNullOrEmpty(srv))
            srv = q;

        // Cache resolve: an ngay neu da resolve trong 1 phut truoc. Giu ca
        // REFERER cung URL — DoodStream bat buoc co ca hai, cache rieng URL
        // se lam lan phat thu hai trong 1 phut chet (proxy khong Referer ->
        // CDN 302 sang host chet).
        string streamKey = ipkey($"javtsunami:stream:{uri}:{srv}");
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
        {
            var c = SplitCached(cachedRaw);
            if (!string.IsNullOrEmpty(c.url))
                return Redirect(StreamLink(c.url, c.referer));
        }

        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var pick = servers.FirstOrDefault(x =>
            string.Equals(x.Label, srv, StringComparison.OrdinalIgnoreCase)
            && JavTsunamiTo.IsSupported(x.Label));

        // Thu `srv` truoc, neu that bai tu dong thu cac server con lai theo
        // thu tu uu tien da sap sanh. Host cua StreamHG/DoodStream chet ngau
        // nhieu, nen mot lan bam server chet khong phai la loi cua nguoi dung.
        var order = new List<JavTsunamiServer>();
        if (pick != null)
            order.Add(pick);

        // App da chon player theo DUOI cua URL /vidosik phat ra. Tra ve nguon
        // KHAC dang (mp4 qua route `.m3u8`) thi ton player va app bao loi
        // "no EXTM3U delimiter" — thong bao "khong co nguon" de hon nhieu, nen
        // fallback chi duoc sang server cung dang. Server chua probe duoc ("")
        // van cho phep thu het vi co the dung dang.
        string wantKind = pick != null && !string.IsNullOrEmpty(pick.Kind)
            ? pick.Kind
            : JavTsunamiTo.ServerKind(pick?.Label);

        foreach (var s in servers)
        {
            if (pick != null && s.Label == pick.Label)
                continue;

            string k = string.IsNullOrEmpty(s.Kind)
                ? JavTsunamiTo.ServerKind(s.Label)
                : s.Kind;

            if (wantKind.Length == 0 || k.Length == 0 || k == wantKind)
                order.Add(s);
        }

        if (order.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Deadline 20s cho ca chuoi, 12s cho tung server (Lampa bo o 30s).
        long deadline = Ms() + 20000;

        foreach (var s in order)
        {
            long sub = Math.Min(deadline, Ms() + 12000);
            long tr = Ms();
            var streams = await ResolveAsync(s, sub);
            Console.WriteLine($"JavTsunami: srv={s.Label} n={streams.Count} ({Ms() - tr}ms)");

            if (streams.Count == 0)
                continue;

            // MasterVariants da sap xep giam dan theo chieu cao, MP4 chi co 1
            // phan tu -> lay thang dau tien, uu tien ban co RESOLUTION.
            var best = streams.FirstOrDefault(x => !string.IsNullOrEmpty(x.tag));
            if (best.url == null)
                best = streams[0];
            // Log ca duong dan vi DoodStream phai co `?token&expiry` moi phuc vu
            // duoc — thieu query la CDN 302 sang host chet (xem DoodSourceAsync).
            string dbg = new Uri(best.url).PathAndQuery;
            if (dbg.Length > 110)
                dbg = dbg[..110] + "...";

            Console.WriteLine($"JavTsunami: chon srv={s.Label} tag={best.tag} ref={best.referer} {dbg}");

            // Cache resolve 1 PHUT: link cua StreamHG co token ngan han, host
            // CDN chet sau 10-30s (do - link tra ve 404 ngay). Cache dai se
            // tra lai chinh link da chet do cho user bam lai. 1 phut van duoi
            // duoi manifestLoadTimeout 10s cua app.
            string raw = best.url + "\n" + (best.referer ?? "");
            hybridCache.Set(ipkey($"javtsunami:stream:{uri}:{s.Label}"), raw, cacheTime(1));
            hybridCache.Set(streamKey, raw, cacheTime(1));

            return Redirect(StreamLink(best.url, best.referer));
        }

        return OnError("stream_links", refresh_proxy: true);
    }

    // Cache luu "url\nreferer" — URL mp4 khong bao gio chua '\n'.
    static (string url, string referer) SplitCached(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (null, null);

        int at = raw.IndexOf('\n');
        return at < 0
            ? (raw, null)
            : (raw[..at], raw[(at + 1)..]);
    }

    // DoodStream chi phuc vu khi URL co query string VA request co Referer
    // khop host embed; thieu mot trong hai thi CDN 302 sang `*.dood.video`
    // (host tro 127.0.0.1 ca public DNS). `headers_stream` cua module co tinh
    // bo Referer (Turbo CDN tra 429 khi thay Referer) nen gan rieng o day.
    string StreamLink(string url, string referer)
    {
        if (string.IsNullOrEmpty(referer))
            return HostStreamProxy(url, httpHeaders(init));

        // Base phai lay tu `headers_stream`: module khong dat `headers` nen
        // `httpHeaders(init)` tra NULL, va `new List<>(null)` nem loi 500.
        // `HostStreamProxy` chi tu them headers_stream khi headers == null.
        var baseHeaders = HeadersModel.InitOrNull(init.headers_stream);
        var hs = new List<HeadersModel>(baseHeaders?.Count + 1 ?? 1);

        if (baseHeaders != null)
            hs.AddRange(baseHeaders);

        hs.Add(new("Referer", referer + "/"));

        return HostStreamProxy(url, hs);
    }
}
