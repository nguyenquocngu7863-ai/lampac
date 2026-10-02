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

namespace JavGuru;

public class JavGuruController : BaseSisiController
{
    public JavGuruController() : base(ModInit.conf) { }

    // Cloudflare ben jav.guru chan client khong giong trinh duyet (HttpClient .NET
    // va urllib deu 403). Thu httpHydra truoc cho qua proxy, khong co marker thi
    // lui ve curl — curl la thu duoc xac nhan chay duoc tren may nay.
    static long Ms() => Environment.TickCount64;

    async Task<string> FetchHtmlAsync(string url, string marker, int attempts = 3, int maxTime = 25, long deadline = 0)
    {
        long tr = Ms();
        string html = null;

        try
        {
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", JavGuruTo.ChromeUA),
                ("Referer", "https://jav.guru/")
            ));
        }
        catch { }

        Console.WriteLine($"JavGuru: hydra ({Ms() - tr}ms) len={html?.Length ?? 0}");

        if (!string.IsNullOrEmpty(html) && (string.IsNullOrEmpty(marker) || html.Contains(marker)))
            return html;

        tr = Ms();
        string body = await JavGuruTo.CurlGetRetry(url, "https://jav.guru/", marker, attempts, maxTime, deadline);
        Console.WriteLine($"JavGuru: curl ({Ms() - tr}ms) len={body?.Length ?? 0}");

        return body;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javguru")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"javguru:{search}:{c}:{sort}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(JavGuruTo.Uri(init.host, search, c, pg, sort), "<div class=\"inside-article\">");
            if (string.IsNullOrEmpty(html))
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            var playlists = JavGuruTo.Playlist("javguru/vidosik", html);
            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync());
    }

    // "Hãng phim" (992) + "Studio" (4663) + "Tags" (553, co so phim) lay tu
    // 3 trang list san cua site — giong JavTsunami boc category tu /categories.
    // Moi trang 1 request (khong phan trang), cache 1 gio trong RAM; makers +
    // studios boc NGAU (lay dau thi toan chu A-C), tags boc TOP theo so phim.
    async Task<List<MenuItem>> MenuAsync()
    {
        List<(string name, string path)> makers = null;
        List<(string name, string path)> studios = null;
        List<(string name, string path)> tags = null;

        string key = ipkey("javguru:dirs");
        if (hybridCache.TryGetValue(key,
                out List<(string name, string path)>[] cached)
            && cached != null && cached.Length == 3)
        {
            makers = cached[0];
            studios = cached[1];
            tags = cached[2];
        }
        else
        {
            long dl = Ms() + 25000;
            var t1 = JavGuruTo.CurlGetRetry(
                JavGuruTo.SiteHost + JavGuruTo.MakerPath,
                JavGuruTo.SiteHost + "/", "jav.guru/maker/", 2, 20, dl);
            var t2 = JavGuruTo.CurlGetRetry(
                JavGuruTo.SiteHost + JavGuruTo.StudioPath,
                JavGuruTo.SiteHost + "/", "jav.guru/studio/", 2, 20, dl);
            var t3 = JavGuruTo.CurlGetRetry(
                JavGuruTo.SiteHost + JavGuruTo.TagsPath,
                JavGuruTo.SiteHost + "/", "jav.guru/tag/", 2, 20, dl);

            await Task.WhenAll(t1, t2, t3);

            makers = JavGuruTo.DirPick(JavGuruTo.DirList(await t1, "maker"));
            studios = JavGuruTo.DirPick(
                JavGuruTo.DirList(await t2, "studio"));
            tags = JavGuruTo.TagList(await t3);

            Console.WriteLine(
                $"JavGuru: dirs makers={makers.Count}"
                + $" studios={studios.Count} tags={tags.Count}");

            if (makers.Count + studios.Count + tags.Count > 0)
                hybridCache.Set(key,
                    new List<(string name, string path)>[]
                        { makers, studios, tags },
                    cacheTime(60));
        }

        return JavGuruTo.Menu(host, makers, studios, tags);
    }

    // Danh sach server cua video, doc tu trang detail. Cache 15 phut de khi
    // nguoi dung bam server thu hai thi khong tai lai trang detail.
    async Task<List<JavGuruServer>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string memKey = ipkey($"javguru:servers:{uri}");
        if (hybridCache.TryGetValue(memKey, out List<JavGuruServer> cached) && cached != null && cached.Count > 0)
            return cached;

        // Timeout ngan + nhieu lan thu: lan thanh cong chi 0.3-0.5s, con lan
        // treo an het --max-time. Deadline 20s de con duoi tran 30s cua client.
        long deadline = Ms() + 20000;
        string detail = await FetchHtmlAsync(JavGuruTo.NormalizePageUrl(uri), "wp-btn-iframe", 4, 4, deadline);
        if (string.IsNullOrEmpty(detail))
            return null;

        var servers = JavGuruTo.Servers(detail);
        if (servers.Count == 0)
            return null;

        // Sap xep theo `Priority` (xem ham duoi) — thu tu nay cung la thu tu
        // app hien trong player, va phan dau la muc mac dinh.
        servers = servers
            .Select((s, i) => new { s, i })
            .OrderBy(x => Priority(x.s.Label))
            .ThenBy(x => x.i)
            .Select(x => x.s)
            .ToList();

        hybridCache.Set(memKey, servers, cacheTime(15));
        return servers;
    }

    // 0 = tot nhat. Thu tu nay KHONG chi dung de fallback: `/vidosik` phat
    // dict theo thu tu nay va app Lampa lay **phan dau** lam muc mac dinh
    // player => doi o day la doi muc mac dinh cua ca module.
    //   LU  - user do nhanh nhat nen lam mac dinh.
    //   DD  - DoodStream, ben nhung nhat trong 5 server.
    //   TV  - gateway /searcho hay 520, cham/chop -> sau DD.
    //   JK  - maxstream on dinh nhung cham hon LU.
    //   SB  - streamhg ~26KB/s, khong tua duoc -> gan cuoi.
    //   VO  - CHUA xu ly, LUON o cuoi cho den khi lam.
    static int Priority(string label)
    {
        if (label.IndexOf("LU", StringComparison.OrdinalIgnoreCase) >= 0)
            return 0;
        if (JavGuruTo.IsDdServer(label))
            return 1;
        if (label.IndexOf("TV", StringComparison.OrdinalIgnoreCase) >= 0)
            return 2;
        if (label.IndexOf("JK", StringComparison.OrdinalIgnoreCase) >= 0)
            return 3;
        if (label.IndexOf("SB", StringComparison.OrdinalIgnoreCase) >= 0)
            return 4;
        return 5;
    }

    // KHONG resolve tai day. Truoc day thu 2-3 server lien tiep ton 20-30s, app
    // bao het thoi gian va popup khong bao gio mo. Nay /vidosik chi tra danh
    // sach server de app hien ra, server nao nguoi dung BAM moi resolve — chi
    // mot server nen 2-13s, luon duoi tran 30s cua client.
    [HttpGet, Staticache(manually: true)]
    [Route("javguru/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // DUOI cua route phai khop dang thuc that cua nguon: app Lampa chi bat
        // hls.js khi URL co `.m3u8` (SISI/plugins/sisi.js applyHlsType), nen
        // nguon mp4 (DD, va ~1/3 phim cua TV) phai di qua `video.mp4` — neu
        // khong app bao "no EXTM3U delimiter".
        // Do kieu cho MOI server song song, deadline 6s. Chi TV can fetch
        // (DD=mp4, JK/LU/SB=HLS biet ngay tu ten gateway nen tra ve luon).
        int total = servers.Count;
        var kinds = new string[total];
        var gateways = new string[total];
        long kindDl = Ms() + 6000;

        for (int i = 0; i < total; i++)
            gateways[i] = JavGuruTo.GatewayUrl(servers[i].PageUrl);

        async Task Probe(int i)
        {
            kinds[i] = await JavGuruTo.ServerKindAsync(gateways[i], 4, kindDl);
        }

        await Task.WhenAll(Enumerable.Range(0, total).Select(Probe));

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < total; i++)
        {
            if (dict.ContainsKey(servers[i].Label))
                continue;

            dict[servers[i].Label] =
                $"{host}/javguru/video{(kinds[i] == JavGuruTo.KindMp4 ? ".mp4" : ".m3u8")}"
              + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(servers[i].Label)}";

            // Nho dang phat da do de `/video` fallback khong phai do lai (do
            // la ton mat 4-6s, client chi cho 30s).
            hybridCache.Set(
                ipkey($"javguru:kind:{uri}:{servers[i].Label}"),
                kinds[i] ?? "",
                cacheTime(15));
        }

        // Warm-up: resolve nen truoc server cham de khi bam an lien (app cat
        // manifest sau 10s trong khi SB resolve 6-15s). Chi warm SB/LU;
        // TV/JK/DD nhanh nen bo qua. LU la muc mac dinh nen duoc warm nhat.

        var warm = servers.Where(x =>
            x.Label.IndexOf("LU", StringComparison.OrdinalIgnoreCase) >= 0 ||
            x.Label.IndexOf("SB", StringComparison.OrdinalIgnoreCase) >= 0);

        _ = Task.Run(async () =>
        {
            // SONG SONG, khong tuan tu: mot server treo toi 25s, giu lai ton
            // 50s moi cua popup.
            try
            {
                await Task.WhenAll(warm.Select(WarmOne));
            }
            catch { }

            async Task WarmOne(JavGuruServer s)
            {
                string key = ipkey($"javguru:stream:{uri}:{s.Label}");
                if (hybridCache.TryGetValue(key, out string _))
                    return;
                string gw = JavGuruTo.GatewayUrl(s.PageUrl);
                if (string.IsNullOrEmpty(gw))
                    return;
                var st = await JavGuruTo.Streams(gw, "https://jav.guru/",
                    Ms() + 25000, JavGuruTo.ServerReferer(s.Label));
                if (st.Count == 0)
                    return;
                var b = st.OrderByDescending(x => x.tag.Length).First();
                string raw = b.url + "\n" + (b.referer ?? "");
                hybridCache.Set(key, raw, cacheTime(10));
            }
        });

        return Json(dict);
    }

    // Resolve server theo `srv` roi chuyen tiep sang link phat; server do chet
    // thi TU THU cac server con lai theo `Priority` (LU -> DD -> TV -> JK ->
    // SB -> VO) — cung co che chuoi du phong nhu JavTsunami. Moi server mot
    // `sub-deadline` nen ca chuoi van duoi tran 30s cua client.
    //
    // NGOAI LE: app Lampa cho manifest ~10s (manifestLoadTimeout) trong khi SB
    // resolve 6-15s (gateway /searcho cham) nen app cat truoc khi co 302. Giai
    // phap: cache ket qua resolve 10 phut + warm-up nen sau khi mo popup
    // /vidosik: lan bam dau tien co the timeout, nhung bam lai an cache (<1s).
    [HttpGet]
    [Route("javguru/video")]
    [Route("javguru/video.mp4")]
    [Route("javguru/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string srv, string q = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        // Cho phep q = ten server (tu cache cu) de URL da bookmark/da mo van chay
        if (string.IsNullOrEmpty(srv))
            srv = q;

        // Cache resolve: an ngay neu da resolve trong 10 phut truoc. Giu ca
        // REFERER cung URL — DoodStream bat buoc co Referer, cache rieng URL
        // se lam lan phat thu hai trong 10 phut chet (proxy khong Referer ->
        // CDN 302 sang host chet).
        string streamKey = ipkey($"javguru:stream:{uri}:{srv}");
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
        {
            var c = SplitCached(cachedRaw);
            if (!string.IsNullOrEmpty(c.url))
            {
                var pick0 = (await DetailServersAsync(uri))?.FirstOrDefault(x => string.Equals(x.Label, srv, StringComparison.OrdinalIgnoreCase));
                return Redirect(HostStreamProxy(c.url, httpHeaders(init, JavGuruTo.StreamHeaders(pick0?.Label, c.referer))));
            }
        }

        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // `servers` da sap xep theo `Priority` (LU > DD > TV > JK > SB > VO).
        // Thu server nguoi dung BAM truoc; no chet thi tu thu phan con lai
        // theo cung thu tu — dung ma JavTsunami. Host cua tung server chet
        // ngau nhien (gateway /searcho 520, CDN StreamHG, DoodStream 302 sang
        // host chet) nen mot lan bam server chet KHONG phai loi cua nguoi dung.
        var order = new List<JavGuruServer>();
        var pick = servers.FirstOrDefault(x =>
            string.Equals(x.Label, srv, StringComparison.OrdinalIgnoreCase));
        if (pick != null)
            order.Add(pick);

        // App da chon player theo DUOI cua URL /vidosik (mp4 -> route `.mp4`,
        // hls -> `.m3u8`). Tra ve nguon KHAC dang se ton player va app bao
        // "no EXTM3U delimiter" — nen fallback chi sang server CUNG dang.
        // Server chua do duoc (TV da dang, VO) van cho phep thu het.
        string wantKind = pick == null ? "" : KindOf(uri, pick);
        foreach (var s in servers)
        {
            if (pick != null && s.Label == pick.Label)
                continue;

            string k = KindOf(uri, s);
            if (wantKind.Length == 0 || k.Length == 0 || k == wantKind)
                order.Add(s);
        }

        if (order.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Deadline 26s cho ca chuoi, 12s cho tung server (Lampa bo o 30s).
        long deadline = Ms() + 26000;

        foreach (var s in order)
        {
            long sub = Math.Min(deadline, Ms() + 12000);
            string gateway = JavGuruTo.GatewayUrl(s.PageUrl);
            if (string.IsNullOrEmpty(gateway))
                continue;

            long tr = Ms();
            var streams = await JavGuruTo.Streams(gateway,
                "https://jav.guru/", sub, JavGuruTo.ServerReferer(s.Label));
            Console.WriteLine($"JavGuru: srv={s.Label} n={streams.Count}"
              + $" ({Ms() - tr}ms)");
            if (streams.Count == 0)
                continue;

            // Uu tien variant cao nhat (da duoc tach master o server).
            var best = streams.OrderByDescending(x => x.tag.Length).First();
            Console.WriteLine($"JavGuru: chon srv={s.Label} tag={best.tag}"
              + $" ref={best.referer} host={new Uri(best.url).Host}");

            // Header theo server: JK (maxstream) can Referer cua no, turbo tra
            // 429 neu thay Referer jav.guru, DD can Referer host embed cua tung
            // video.
            var headers = httpHeaders(init,
                JavGuruTo.StreamHeaders(s.Label, best.referer));

            // Luu resolve 10 phut: token SB/LU ngan han nhung du cho bam lai,
            // va lan bam sau an cache <1s (duoi manifestLoadTimeout 10s cua
            // app). Giu ca key cua server vua resolve de bam lai khong phai
            // resolve lai tu dau.
            string raw = best.url + "\n" + (best.referer ?? "");
            hybridCache.Set(
                ipkey($"javguru:stream:{uri}:{s.Label}"), raw, cacheTime(10));
            hybridCache.Set(streamKey, raw, cacheTime(10));

            return Redirect(HostStreamProxy(best.url, headers));
        }

        return OnError("stream_links", refresh_proxy: true);
    }

    // Dang phat cua mot server: uu tien ket qua `/vidosik` da do va cache
    // lai, khong thi do lai bang ten gateway (DD=mp4, JK/LU/SB=hls,
    // TV="" vi da dang) — khong ton request.
    string KindOf(string uri, JavGuruServer s)
    {
        if (s == null)
            return "";

        string key = ipkey($"javguru:kind:{uri}:{s.Label}");
        if (hybridCache.TryGetValue(key, out string k)
            && !string.IsNullOrEmpty(k))
            return k;

        return JavGuruTo.ServerKind(JavGuruTo.GatewayUrl(s.PageUrl));
    }

    // Cache luu "url\nreferer" — URL khong bao gio chua '\n'.
    static (string url, string referer) SplitCached(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (null, null);

        int at = raw.IndexOf('\n');
        return at < 0
            ? (raw, null)
            : (raw[..at], raw[(at + 1)..]);
    }
}
