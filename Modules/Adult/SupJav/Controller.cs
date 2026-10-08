using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SupJav;

public class SupJavController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 25000;
    public SupJavController() : base(ModInit.conf) { }

    // HOME: SupJav hien khong con phim moi -> trang chu luon tra cung 1 list,
    // cuon them thi van do. Moi lan vao home (pg=1) lay NGAU NHIEN 1 trong 3
    // the loai, roi GIU NGUYEN lua chon do cho ca chuoi cuon vo van (pg=2..N)
    // de khong lon giua cac trang.
    static readonly string[] HomeCats =
    {
        "category/censored-jav",
        "category/uncensored-jav",
        "category/amateur",
    };

    // Key khong dung `ipkey()`: ipkey kham `proxy.CurrentProxyIp` (do doi khi
    // proxy refresh) -> luon ton khoa giua trang 1 va trang 2 se doi the loai
    // khi dang cuon. Dung IP client (on dinh hon nhieu).
    string HomeCatKey()
    {
        string client = "";
        try { client = rch?.enable == true ? requestInfo?.IP : null; } catch { }
        if (string.IsNullOrEmpty(client))
        {
            try { client = HttpContext?.Connection?.RemoteIpAddress?.ToString(); } catch { }
        }
        return "supjav:homecat:" + (client ?? "");
    }

    string HomeCategory(int pg)
    {
        string key = HomeCatKey();
        if (pg <= 1)
        {
            string pick = HomeCats[Random.Shared.Next(HomeCats.Length)];
            hybridCache.Set(key, pick, cacheTime(720), true);
            return pick;
        }
        if (hybridCache.TryGetValue(key, out string prev) && !string.IsNullOrWhiteSpace(prev))
            return prev;
        return HomeCats[Random.Shared.Next(HomeCats.Length)];
    }

    [HttpGet, Staticache(manually: true)]
    [Route("supjav")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1) pg = 1;

        // Home (khong search, khong c) -> 1/3 the loai ngau nhien, giu cho
        // cac trang sau. `c` do thi nhanh danh sach binh thuong.
        string effC = c;
        if (string.IsNullOrWhiteSpace(c) && string.IsNullOrWhiteSpace(search))
            effC = HomeCategory(pg);

        // gọt sort về đúng tập của CONTEXT (search/__latest không sort được;
        // sort=week khi đang ở category vô nghĩa) — xem SupJavTo.ClampSort
        sort = SupJavTo.ClampSort(sort, search, effC);

        var menuTask = MenuAsync(search, sort, effC);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            // v3: + sort (checklist 9e: cache key phai du search/c/sort/pg).
            // v2: doi tu List<PlaylistItem> sang (list, total_pages). Phai doi key,
            // khong thi entry fdb cu (JSON array) bi doc thanh tuple -> JsonSerializationException.
            ipkey($"supjav:v3:{search}:{effC}:{sort}:{pg}"), 10, async e =>
        {
            string html = await GetPageAsync(SupJavTo.Uri(init.host, search, effC, sort, pg));
            var playlists = SupJavTo.Playlist("supjav/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, SupJavTo.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await menuTask,
            total_pages: cache.Value.total_pages
        );
    }

    // menu = head (phụ thuộc search/sort/c, dựng lại mỗi request — rất rẻ)
    //        + base (taxonomy ~1100 mục, cache đúng 1 lần, không phụ thuộc context)
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var menu = SupJavTo.MenuHead(host, search, sort, c);
        var baseGroups = await MenuBaseAsync();
        if (baseGroups != null && baseGroups.Count > 0)
            menu.AddRange(baseGroups);
        return menu;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("supjav:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        string hostLocal = host;
        if (!await menuLock.WaitAsync(2000))
            return SupJavTo.Menu(hostLocal, null, null, null);
        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;
            var build = BuildMenuAsync(hostLocal, memKey);
            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return SupJavTo.Menu(hostLocal, null, null, null);
            return await build;
        }
        finally { menuLock.Release(); }
    }

    // /tag phan 291 muc, /maker 808 muc theo /page/N/ (trang 1 khong lo link).
    // Crawl song song den khi het (toi da 12/4 trang), gop dedup.
    async Task<List<(string slug, string name)>> TagsAsync()
    {
        string memKey = ipkey("supjav:tax:tags");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit) && hit != null && hit.Count > 0)
            return hit;
        var tasks = new List<Task<string>>(5);
        tasks.Add(GetPageAsync($"{SupJavTo.SiteHost}/tag"));
        for (int p = 2; p <= 5; p++)
            tasks.Add(GetPageAsync($"{SupJavTo.SiteHost}/tag/page/{p}/"));
        await Task.WhenAll(tasks);
        for (int i = 0; i < tasks.Count; i++)
        {
            string html = await tasks[i];
            if (string.IsNullOrEmpty(html))
            {
                await Task.Delay(3000);
                tasks[i] = GetPageAsync(i == 0 ? $"{SupJavTo.SiteHost}/tag" : $"{SupJavTo.SiteHost}/tag/page/{i + 1}/");
            }
        }
        await Task.WhenAll(tasks);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(string slug, string name)>();
        foreach (var t in tasks)
        {
            string html = await t;
            if (string.IsNullOrEmpty(html)) continue;
            foreach (var it in SupJavTo.Tags(html))
                if (seen.Add(it.slug)) all.Add(it);
        }
        all.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        if (all.Count == 0) return all;
        hybridCache.Set(memKey, all, cacheTime(720), true);
        return all;
    }

    async Task<List<(string slug, string name)>> MakersAsync()
    {
        string memKey = ipkey("supjav:tax:makers");
        if (hybridCache.TryGetValue(memKey, out List<(string slug, string name)> hit2) && hit2 != null && hit2.Count > 0)
            return hit2;
        var tasks = new List<Task<string>>(12);
        tasks.Add(GetPageAsync($"{SupJavTo.SiteHost}/maker?sort=quantity"));
        for (int p = 2; p <= 12; p++)
            tasks.Add(GetPageAsync($"{SupJavTo.SiteHost}/maker/page/{p}/?sort=quantity"));
        await Task.WhenAll(tasks);
        // CF challenge theo dot: trang ve rong thi doi 3s thu lai 1 lan
        for (int i = 0; i < tasks.Count; i++)
        {
            string html = await tasks[i];
            if (string.IsNullOrEmpty(html))
            {
                await Task.Delay(3000);
                tasks[i] = GetPageAsync(i == 0 ? $"{SupJavTo.SiteHost}/maker?sort=quantity" : $"{SupJavTo.SiteHost}/maker/page/{i + 1}/?sort=quantity");
            }
        }
        await Task.WhenAll(tasks);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(string slug, string name)>();
        foreach (var t in tasks)
        {
            string html = await t;
            if (string.IsNullOrEmpty(html)) continue;
            foreach (var it in SupJavTo.Makers(html))
                if (seen.Add(it.slug)) all.Add(it);
        }
        // GIU nguyen thu tu site (sort=quantity: nhieu phim len truoc), khong sort A-Z
        if (all.Count == 0) return all;
        hybridCache.Set(memKey, all, cacheTime(720), true);
        return all;
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        long tr = Environment.TickCount64;
        try
        {
            var homeTask = GetPageAsync(SupJavTo.SiteHost + "/");
            var makerTask = MakersAsync();
            var tagTask = TagsAsync();
            await Task.WhenAll(homeTask, makerTask, tagTask);
            var cats = SupJavTo.Taxonomies(await homeTask ?? "");
            var makers = await makerTask;
            var tags = await tagTask;
            if (cats.Count > 0 || makers.Count > 0 || tags.Count > 0)
            {
                var menu = SupJavTo.Menu(hostLocal, cats, makers, tags);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine($"SupJav: menu cats={cats.Count} makers={makers.Count} tags={tags.Count} ({Environment.TickCount64 - tr}ms)");
                return menu;
            }
        }
        catch { }
        Console.WriteLine($"SupJav: menu fail ({Environment.TickCount64 - tr}ms)");
        return SupJavTo.Menu(hostLocal, null, null, null);
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 2) break;
            string page = await RaceGetAsync(url, (int)Math.Ceiling(Math.Min(6, left)));
            if (!string.IsNullOrEmpty(page) && page.Length >= 5000 && !page.Contains("Just a moment"))
                return page;
            await Task.Delay(1500);
        }
        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;
        var headers = HeadersModel.Init(
            ("User-Agent", SupJavTo.ChromeUA),
            ("Referer", SupJavTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        var t1 = Task.Run(async () =>
        {
            try { await httpHydra.GetSpan(url, span => { a = span.ToString(); }, addheaders: headers); }
            catch { }
        });
        var t2 = Task.Run(async () =>
        {
            try
            {
                b = await Http.Get(url, timeoutSeconds: Math.Max(8, seconds), httpversion: init.httpversion, proxy: proxy, headers: headers);
            }
            catch { }
        });
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrEmpty(a) && !a.Contains("Just a moment")) return a;
            if (!string.IsNullOrEmpty(b) && !b.Contains("Just a moment")) return b;
            if (t1.IsCompleted && t2.IsCompleted) break;
            await Task.Delay(300);
        }
        if (!string.IsNullOrEmpty(a) && !a.Contains("Just a moment")) return a;
        return !string.IsNullOrEmpty(b) && !b.Contains("Just a moment") ? b : null;
    }

    // ========== PLAYER DA SERVER: LAZY-RESOLVE (chuan 2026-10-07) ==========
    // /vidosik KHONG resolve: moi server ton 8-25s (VAS 15+15s, VOE 8s,
    // gateway 25s), resolve het 4 server mat ~11s, app timeout.
    // /vidosik chi fetch detail + suy kind tu label; /video resolve DUNG
    // 1 server user bam + cache 10p + warm nen VAS/VOE (Chrome 30s).
    //
    // Kind suy tu label, khong can fetch (site dat ten on dinh):
    //   ST = StreamTape -> mp4; con lai (VAS/VOE/FST/EVS/...) -> HLS.
    // Chu y FST chua "ST" nen phai so EQUALS, khong Contains.
    static string ServerKind(string label) =>
        string.Equals(label?.Trim(), "ST", StringComparison.OrdinalIgnoreCase)
            ? ".mp4" : ".m3u8";

    async Task<List<(string label, string link)>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : SupJavTo.SiteHost + "/" + uri.Trim('/');
        string memKey = ipkey($"supjav:servers:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<(string label, string link)> cached) && cached != null && cached.Count > 0)
            return cached;
        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page)) return null;
        var servers = SupJavTo.Servers(page);
        if (servers.Count == 0) return null;
        hybridCache.Set(memKey, servers, cacheTime(15));
        return servers;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("supjav/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : SupJavTo.SiteHost + "/" + uri.Trim('/');
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (label, _) in servers)
        {
            if (dict.ContainsKey(label))
                continue;
            dict[label] =
                $"{host}/supjav/video{ServerKind(label)}"
              + $"?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(label)}";
        }

        // Warm-up nen: VAS/VOE resolve bang Chrome (toi 30s), app cat
        // manifest sau ~10s. Warm truoc de bam an lien; cache 10p.
        var warm = servers.Where(x =>
            x.label.IndexOf("VAS", StringComparison.OrdinalIgnoreCase) >= 0 ||
            SupJavTo.IsVoeLabel(x.label)).ToList();
        if (warm.Count > 0)
        {
            string purl = pageUrl;
            _ = Task.Run(async () =>
            {
                try { await Task.WhenAll(warm.Select(x => WarmOne(purl, x.label, x.link))); }
                catch { }
            });
        }

        return Json(dict);
    }

    async Task WarmOne(string pageUrl, string label, string link)
    {
        string key = ipkey($"supjav:stream:{pageUrl}:{label}");
        if (hybridCache.TryGetValue(key, out string _))
            return;
        string packed = await ResolveOneAsync(pageUrl, label, link);
        if (!string.IsNullOrEmpty(packed))
            hybridCache.Set(key, packed, cacheTime(10));
    }

    // Resolve DUNG 1 server theo label. Tach tu ResolveAsync cu (da xoa
    // 2026-10-07: no resolve tuan tu HET server, 10-30s, app timeout).
    async Task<string> ResolveOneAsync(string pageUrl, string label, string link)
    {
        // VAS (Vidara): giai thang server-side, khong can Chrome
        if (label.IndexOf("VAS", StringComparison.OrdinalIgnoreCase) >= 0)
            return await ResolveVasAsync(pageUrl, link);
        // VOE: chuoi redirect `lk1 -> voe.sx -> host VOE` bi Chrome chan
        // (net::ERR_BLOCKED_BY_CLIENT) chi khi di theo redirect do, con
        // vao truc tiep host VOE thi OK nen quyet doan tai server roi dua
        // URL cuoi cho Chrome mo truc tiep.
        if (SupJavTo.IsVoeLabel(label) && label.IndexOf("VAS", StringComparison.OrdinalIgnoreCase) < 0)
        {
            string gw = SupJavTo.FinalUrl(link);
            string voe = await SupJavTo.VoeEmbedUrlAsync(gw, pageUrl, 8);
            if (string.IsNullOrEmpty(voe)) voe = gw;
            if (!string.IsNullOrEmpty(voe))
                return "chrome:" + voe + "\n" + pageUrl;
            return null;
        }
        return await ResolveServerAsync(pageUrl, label, link);
    }

    // LUC (LuluStream): tach master 2 level o server, tra playlist media
    // chat luong cao nhat — y nhu JavGuru STREAM LU (MasterVariants bang
    // curl + UA android). Dung `gw` cua chuoi gateway san co, KHONG fetch
    // lai. Referer tra kem = URL embed that (sau 302).
    async Task<string> ResolveLucAsync(string gw, string final, string pageUrl)
    {
        string master = SupJavTo.LuluFile(gw);
        if (string.IsNullOrEmpty(master))
            return null;

        string best = await SupJavTo.LuluBestVariant(master, final);
        string pick = best ?? master;
        if (!await VerifyLinkAsync(pick, final))
            return null;
        // Referer = URL embed that (sau 302), giong trinh duyet —
        // proxy chi gui UA + Referer nay (nhanh Video() LUC).
        string emb = final;
        try
        {
            string canon = await SupJavTo.CurlFinalUrl(final, pageUrl, 8);
            if (!string.IsNullOrEmpty(canon) && canon.StartsWith("http",
                StringComparison.OrdinalIgnoreCase))
                emb = canon;
        }
        catch { }
        if (!await VerifyLinkAsync(pick, emb))
            return pick + "\n" + final;
        return pick + "\n" + emb;
    }

    // VAS (Vidara): final 302 -> https://<host>/e/<filecode> -> POST /api/stream -> streaming_url (HLS)
    async Task<string> ResolveVasAsync(string pageUrl, string link)
    {
        string final = SupJavTo.FinalUrl(link);
        if (string.IsNullOrEmpty(final)) return null;
        var headers = HeadersModel.Init(
            ("User-Agent", SupJavTo.ChromeUA),
            ("Referer", pageUrl));
        string loc = null;
        try { loc = await Http.GetLocation(final, timeoutSeconds: 15, headers: headers, httpversion: init.httpversion); }
        catch { }
        if (string.IsNullOrEmpty(loc))
        {
            try { loc = await Http.GetLocation(final, timeoutSeconds: 15, headers: headers, proxy: proxy, httpversion: init.httpversion); }
            catch { return null; }
        }
        var (apiHost, filecode) = SupJavTo.VasTarget(loc ?? "");
        if (string.IsNullOrEmpty(apiHost) || string.IsNullOrEmpty(filecode)) return null;
        string emb = apiHost + "/e/" + filecode;
        string json = null;
        try
        {
            using var content = new System.Net.Http.StringContent(
                "{\"filecode\":\"" + filecode + "\",\"device\":\"web\"}",
                System.Text.Encoding.UTF8, "application/json");
            json = await Http.Post(apiHost + "/api/stream",
                content, timeoutSeconds: 15,
                headers: HeadersModel.Init(
                    ("User-Agent", SupJavTo.ChromeUA),
                    ("Referer", emb),
                    ("Origin", apiHost)),
                httpversion: init.httpversion, disposeData: true);
        }
        catch { }
        string master = SupJavTo.VasStreamingUrl(json ?? "");
        if (string.IsNullOrEmpty(master)) return null;
        return master + "\n" + emb;
    }

    async Task<string> ResolveServerAsync(string pageUrl, string label, string link)
    {        // Gateway chain (cookiejar rieng): ?l=<link> lay session -> ?l=&c=<rev>.
        // Backend VOE doi session cookie, di tat ?l=&c= ngay tra shell.
        string gateway = SupJavTo.GatewayUrl(link);
        string final = SupJavTo.FinalUrl(link);
        if (string.IsNullOrEmpty(final)) return null;
        var jar = new System.Net.CookieContainer();
        string gw = await SupJavTo.GetHtmlAsync(final, pageUrl, 25, proxy, init.httpversion);
        if (string.IsNullOrEmpty(gw) || SupJavTo.IsGatewayShell(gw))
        {
            try
            {
                var headers = HeadersModel.Init(
                    ("User-Agent", SupJavTo.ChromeUA),
                    ("Referer", pageUrl));
                await Http.Get(gateway, timeoutSeconds: 15, headers: headers, httpversion: init.httpversion, cookieContainer: jar);
                gw = await Http.Get(final, timeoutSeconds: 25, headers: HeadersModel.Init(
                    ("User-Agent", SupJavTo.ChromeUA),
                    ("Referer", gateway)), httpversion: init.httpversion, cookieContainer: jar);
            }
            catch { }
        }
        if (string.IsNullOrEmpty(gw)) return null;
        // LUC (LuluStream): unpack -> jwplayer sources file (master m3u8
        // tren *.tnmr.org) -> tach variant nhu JavGuru STREAM LU.
        // TINH TRANG 2026-10-07: resolve OK (HEAD 200), nhung GET qua
        // proxy bi edge wkw3dwshigvf.tnmr.org 403 (2 phim) trong khi LU
        // ben JavGuru 200 voi cung header — gate theo edge/file, can
        // phien Chrome that. Giu code vi dung khi edge khac.
        if (string.Equals(label?.Trim(), "LUC", StringComparison.OrdinalIgnoreCase))
            return await ResolveLucAsync(gw, final, pageUrl);
        // VAS (Vidara): 302 -> <host>/e/<filecode> -> POST /api/stream -> streaming_url
        if (label.IndexOf("VAS", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var vas = await ResolveVasAsync(final, pageUrl);
            if (!string.IsNullOrEmpty(vas)) return vas;
        }
        // FST (StreamHg): unpack -> hls2>hls3>hls4, thu tung bien cho den
        // khi co master that (host khac nhau co the da chet doc lap).
        var masters = SupJavTo.StreamHgMasters(gw);
        for (int i = 0; i < masters.Count; i++)
        {
            if (await VerifyLinkAsync(masters[i], final)) return masters[i] + "\n" + final;
        }
        // ST (StreamTape): gateway TRA SAN trang embed (co ca #robotlink), nen
        // uu tien boc token truc tiep tu `gw`. KHONG fetch lai
        // `streamtape.com/e/<id>`: URL ngan do tra 404 (StreamTape can
        // `/e/<id>/<slug>`) -> khong co robotlink -> ST chet.
        string stEmbed = SupJavTo.StreamTapeEmbedUrl(gw);
        string stId = SupJavTo.StreamTapeId(gw);
        string stHtml = gw;
        if (!SupJavTo.HasRobotLink(gw) && !string.IsNullOrEmpty(stId))
        {
            stEmbed = "https://streamtape.com/e/" + stId;
            stHtml = await SupJavTo.GetHtmlAsync(stEmbed, pageUrl, 20, proxy, init.httpversion) ?? "";
        }
        string stMp4 = SupJavTo.StreamTapeMp4(stHtml);
        if (!string.IsNullOrEmpty(stMp4))
        {
            // Referer = trang embed StreamTape (khong phai trang phim):
            // API `get_video` tra 302 sang CDN, app se follow.
            string stRef = string.IsNullOrEmpty(stEmbed)
                ? "https://streamtape.com/"
                : stEmbed;
            // Follow 302 lay URL CDN thuoc. App khong phai qua them 1 hop,
            // va VerifyLinkAsync (HEAD) se thay media thay vi 302.
            string stCdn = await SupJavTo.CurlFinalUrl(stMp4, stRef, 10);
            if (!string.IsNullOrEmpty(stCdn)) return stCdn + "\n" + stRef;
            return stMp4 + "\n" + stRef;
        }
        return null;
    }

    [HttpGet]
    [Route("supjav/video")]
    [Route("supjav/video.m3u8")]
    [Route("supjav/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q, string srv = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        // Cho phep ca `srv` (URL moi) va `q` (cache cu/bookmark).
        string label = !string.IsNullOrEmpty(srv) ? srv : q;
        if (string.IsNullOrEmpty(label))
            return OnError("stream_links", refresh_proxy: true);
        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : SupJavTo.SiteHost + "/" + (uri ?? "").Trim('/');

        // Cache resolve: nhan chrome: (VOE/VAS 30s Playwright) an ngay
        // neu da resolve trong 10 phut truoc.
        string streamKey = ipkey($"supjav:stream:{pageUrl}:{label}");
        string packed = null;
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw) && !string.IsNullOrEmpty(cachedRaw))
            packed = cachedRaw;
        else
        {
            var servers = await DetailServersAsync(uri);
            if (servers == null || servers.Count == 0)
                return OnError("stream_links", refresh_proxy: true);
            var pick = servers.FirstOrDefault(x =>
                string.Equals(x.label, label, StringComparison.OrdinalIgnoreCase));
            // KHONG fallback chain: bam nut nao phat dung nut do, con thi
            // bao loi (fallback lam sau tach mat phim giua cac nguon).
            if (pick.label == null)
                return OnError("stream_links", refresh_proxy: true);
            packed = await ResolveOneAsync(pageUrl, pick.label, pick.link);
            if (string.IsNullOrEmpty(packed))
                return OnError("stream_links", refresh_proxy: true);
            hybridCache.Set(streamKey, packed, cacheTime(10));
        }

        {
            string link = packed, referer = SupJavTo.SiteHost + "/";
            int nl = packed.IndexOf('\n');
            if (nl > 0) { link = packed.Substring(0, nl); referer = packed.Substring(nl + 1); }
            // nhan chrome: resolve that bang Playwright roi redirect
            if (link.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase))
            {
                string final = link.Substring(7);
                string voe = await SupJavTo.VoeSourceAsync(final, referer, 30000);
                if (string.IsNullOrEmpty(voe)) return OnError("stream_links", refresh_proxy: true);
                // BAN BUOC co User-Agent: CDN VOE (openresty) tra 403 neu thieu.
                // Rieng `headers` cho proxy = proxy chi gui cac header nay va
                // BO qua User-Agent mac dinh (ProxyAPI.Utilities CreateProxyHttpRequest).
                var h2 = httpHeaders(init, HeadersModel.Init(
                    ("user-agent", SupJavTo.ChromeUA),
                    ("referer", referer)));
                return Redirect(HostStreamProxy(voe, h2));
            }
            if (await VerifyLinkAsync(link, referer))
            {
                IReadOnlyList<HeadersModel> direct;
                if (string.Equals(label?.Trim(), "LUC", StringComparison.OrdinalIgnoreCase))
                    direct = HeadersModel.Init(("user-agent", SupJavTo.ChromeUA));
                else
                    direct = httpHeaders(init, HeadersModel.Init(("referer", referer)));
                return Redirect(HostStreamProxy(link, direct));
            }
        }
        return OnError("stream_links", refresh_proxy: true);
    }

    // HEAD kiem tra link song truoc khi redirect (6s, khong tai body).
    // Yeu cau content-type media (video/*, mpegurl, octet-stream): ST tra
    // HEAD 200 text/html nhung GET 500 -> phai loai.
    static bool IsMediaContentType(string ct)
    {
        if (string.IsNullOrEmpty(ct)) return false;
        ct = ct.ToLowerInvariant();
        return ct.Contains("mpegurl") || ct.Contains("mp2t") || ct.Contains("octet-stream")
            || ct.Contains("video/") || ct.Contains("application/vnd.apple");
    }

    async Task<bool> VerifyLinkAsync(string link, string referer)
    {
        if (string.IsNullOrEmpty(link)) return false;
        var headers = HeadersModel.Init(
            ("User-Agent", SupJavTo.ChromeUA),
            ("Referer", referer));
        try
        {
            using var resp = await Http.ResponseHeaders(link, timeoutSeconds: 6, headers: headers, httpversion: init.httpversion);
            if (resp != null && resp.IsSuccessStatusCode && IsMediaContentType(resp.Content?.Headers?.ContentType?.ToString()))
                return true;
        }
        catch { }
        try
        {
            using var resp = await Http.ResponseHeaders(link, timeoutSeconds: 6, headers: headers, proxy: proxy, httpversion: init.httpversion);
            return resp != null && resp.IsSuccessStatusCode && IsMediaContentType(resp.Content?.Headers?.ContentType?.ToString());
        }
        catch { return false; }
    }
}
