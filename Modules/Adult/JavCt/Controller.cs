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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace JavCt;

public class JavCtController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10_000;
    public JavCtController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javct")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // Bang xep hang `?sort=` cua site KHONG phan trang duoc — moi trang
        // deu ra cung noi dung. Ep ve trang 1 va khoa total_pages de app
        // khong cuon tiep va lap phim.
        string sortUrl = JavCtTo.UriSort(init.host, c, sort);
        if (sortUrl != null)
            pg = 1;

        // Menu fetch chay SONG SONG voi playlist: tong thoi gian la MAX
        // thay vi TONG (JavCt: 15.9s -> xem log `JavCt: menu`).
        var menuTask = MenuAsync();

        var cache = await InvokeCacheResult(
            ipkey($"javct:{search}:{c}:{sort}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(
                sortUrl ?? JavCtTo.Uri(init.host, search, c, pg));
            var playlists = JavCtTo.Playlist("javct/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await menuTask,
            total_pages: sortUrl != null ? 1 : 0);
    }

    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("javct:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        // App cache response DAU CA PHIEN -> lan truy cap dau phai tra
        // menu THAT, khong duoc tra menu rut gon. Nhung `GetPageAsync` co
        // deadline 14s nen phai CO NGAN SACH: het 10s -> tra menu toi
        // thieu (fetch chay tiep cho request sau) thay vi treo app.
        if (!await menuLock.WaitAsync(2000))
            return JavCtTo.Menu(hostLocal, null, null);

        try
        {
            // Request truoc do da nap xong cache trong luc ta cho lock.
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2)
                && hit2 != null && hit2.Count > 0)
                return hit2;

            var build = BuildMenuAsync(hostLocal, memKey);

            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
                return JavCtTo.Menu(hostLocal, null, null);

            return await build;
        }
        finally
        {
            menuLock.Release();
        }
    }

    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        long tr = Environment.TickCount64;

        try
        {
            // 2 trang fetch SONG SONG. `/studios` = 1479 hang nen day la
            // phan ton thoi gian cua lan mo app dau tien.
            var catsTask = TaxonomiesAsync("/categories", "category");
            var studiosTask = TaxonomiesAsync("/studios", "studio");
            await Task.WhenAll(catsTask, studiosTask);

            var cats = await catsTask;
            var studios = await studiosTask;

            if (cats.Count > 0 || studios.Count > 0)
            {
                var menu = JavCtTo.Menu(hostLocal, cats, studios);
                hybridCache.Set(memKey, menu, cacheTime(720), true);
                Console.WriteLine(
                    $"JavCt: menu cats={cats.Count} studios={studios.Count}"
                    + $" ({Environment.TickCount64 - tr}ms)");

                return menu;
            }
        }
        catch { }

        Console.WriteLine(
            $"JavCt: menu that bai ({Environment.TickCount64 - tr}ms)");

        return JavCtTo.Menu(hostLocal, null, null);
    }

    async Task<List<(string name, string path)>> TaxonomiesAsync(string page, string kind, int top = int.MaxValue)
    {
        string memKey = ipkey($"javct:tax:{kind}");

        if (hybridCache.TryGetValue(memKey, out List<(string name, string path)> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync($"{JavCtTo.SiteHost}{page}");
        var res = JavCtTo.Taxonomies(html, kind, top);
        if (res.Count == 0)
            return res;

        hybridCache.Set(memKey, res, cacheTime(720), true);
        return res;
    }

    async Task<string> GetPageAsync(string url)
    {
        var deadline = DateTime.UtcNow.AddSeconds(14);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;

            string page = await RaceGetAsync(url,
                (int)Math.Ceiling(Math.Min(4, left)));

            if (!string.IsNullOrEmpty(page) && page.Length >= 1000)
                return page;
        }

        return null;
    }

    async Task<string> RaceGetAsync(string url, int seconds)
    {
        string a = null, b = null;

        var headers = HeadersModel.Init(
            ("User-Agent", JavCtTo.ChromeUA),
            ("Referer", JavCtTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));

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
                b = await Http.Get(
                    url,
                    timeoutSeconds: Math.Max(6, seconds),
                    httpversion: init.httpversion,
                    proxy: proxy,
                    headers: headers);
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

    [HttpGet, Staticache(manually: true)]
    [Route("javct/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // DD (mp4, song dai) len truoc lam mac dinh - app lay phan dau.
        // Cac nhanh chet da bi loai o Resolve (packed rong).
        var ordered = links
            .OrderBy(kv =>
            {
                string v = kv.Value;
                bool isMp4 = !(v.Contains(".m3u8") || v.Contains("/hls/") || v.Contains("master.txt"));
                return isMp4 ? 0 : 1;
            })
            .ThenBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return Json(ordered.ToDictionary(k => k.Key, k =>
        {
            // HLS di route .m3u8, mp4 di route .mp4 (lan lon la app bao
            // "no EXTM3U delimiter"). Playmate tra master .txt (HLS) nen
            // phai bat ca "/hls/" + "master.txt".
            string v = k.Value;
            string route = v.Contains(".m3u8") || v.Contains("/hls/") || v.Contains("master.txt")
                ? "video.m3u8" : "video.mp4";
            return $"{host}/javct/{route}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}";
        }));
    }

    // POST /ajax/player {episode=0, filmId=data-source, pt=__pt} ->
    // {player_enc xor __pk | player} -> iframe embed (playmogo/dood).
    // Giai doan 1: tra link embed; giai ma dood -> m3u8 lam sau.
    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : JavCtTo.SiteHost + "/v/" + uri.Trim('/');

        string memKey = ipkey($"javct:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return null;

        var ds = Regex.Match(page, @"data-source\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        var pt = Regex.Match(page, @"window\.__pt\s*=\s*[""']([^""']+)[""']");
        var pk = Regex.Match(page, @"window\.__pk\s*=\s*[""']([^""']+)[""']");

        if (!ds.Success || !pt.Success)
        {
            return null;
        }

        string pkVal = pk.Success ? pk.Groups[1].Value : "";

        // 3 nut server FL/US/PM: cung filmId (data-source), khac episode
        // (data-id). Resolve song song tung server, nhan theo ten nut.
        var servers = new List<(string label, string episode)>();
        foreach (Match b in Regex.Matches(page,
            @"<button\b[^>]*\bdata-id\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</button\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string label = Regex.Replace(b.Groups[2].Value, "<[^>]+>", " ").Trim();
            if (label.Length > 12)
                label = label.Substring(0, 12);
            if (string.IsNullOrEmpty(label))
                label = "S" + (servers.Count + 1);

            // Giu CA 2 nhanh a/b: nhan trung thi danh so (FL, FL 2...).
            // Moi POST xoay pt 1 vong nen resolve TUAN TU theo thu tu nut.
            string key = label;
            int dup = 2;
            while (servers.Exists(s => s.label == key))
                key = label + " " + (dup++);

            servers.Add((key, b.Groups[1].Value));
        }

        // Player mac dinh (fakeplayer playbox): episode = filmId, khong co data-id.
        // Thuong la DD (dood, song dai) - uu tien resolve TRUOC lam mac dinh.
        // Phim cu chet key se tra "We are updating" (khong iframe) -> tu skip.
        // episode=0 tra E_TOK_MISS, khong dung.
        // Moi POST xoay pt 1 vong nen resolve TUAN TU theo thu tu nut.
        for (int i = 0; i < servers.Count; i++)
        {
            if (string.Equals(servers[i].label, "DD", StringComparison.OrdinalIgnoreCase))
            {
                int n = 2;
                string nk = "DD " + (n++);
                while (servers.Exists(s => s.label == nk)) nk = "DD " + (n++);
                servers[i] = (nk, servers[i].episode);
            }
        }
        servers.Insert(0, ("DD", ds.Groups[1].Value));


        // pt/pk DUNG 1 LAN: response tra next_pt/next_pk cho request KE
        // TIEP. POST song song cung pt thi chi cai dau song (403
        // E_TOK_MISS). Nen resolve TUAN TU, chuyen pt/pk qua tung server.
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string curPt = pt.Groups[1].Value, curPk = pkVal;

        foreach (var (label, episode) in servers)
        {
            var (packed, nextPt, nextPk) = await ResolveServerAsync(
                pageUrl, ds.Groups[1].Value, episode, curPt, curPk, label);


            if (!string.IsNullOrEmpty(packed) && !links.ContainsKey(label))
                links.TryAdd(label, packed);

            if (!string.IsNullOrEmpty(nextPt))
                curPt = nextPt;
            if (!string.IsNullOrEmpty(nextPk))
                curPk = nextPk;
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(10));
        return links;
    }

    // 1 server: POST episode -> player_enc xor -> embed -> dood mp4.
    // Tra (packed, nextPt, nextPk) de noi pt/pk cho server ke tiep.
    async Task<(string packed, string nextPt, string nextPk)> ResolveServerAsync(
        string pageUrl, string filmId, string episode, string pt, string pk, string label)
    {
        // javct.net mo direct (detail + ajax deu 200 direct) - POST direct truoc,
        // proxy fallback sau (proxy SIN hay timeout).
        string api = null;
        var postHeaders = HeadersModel.Init(
            ("User-Agent", JavCtTo.ChromeUA),
            ("Referer", pageUrl),
            ("Origin", JavCtTo.SiteHost),
            ("X-Requested-With", "XMLHttpRequest"));
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["episode"] = episode,
                ["filmId"] = filmId,
                ["pt"] = pt
            });

            api = await Http.Post(
                JavCtTo.PlayerApi,
                content,
                timeoutSeconds: Math.Max(15, init.httptimeout),
                headers: postHeaders,
                httpversion: init.httpversion,
                statusCodeOK: true,
                disposeData: true);
        }
        catch { api = null; }
        if (string.IsNullOrEmpty(api))
        {
            try
            {
                using var content2 = new FormUrlEncodedContent(new Dictionary<string, string>()
                {
                    ["episode"] = episode,
                    ["filmId"] = filmId,
                    ["pt"] = pt
                });

                api = await Http.Post(
                    JavCtTo.PlayerApi,
                    content2,
                    timeoutSeconds: Math.Max(15, init.httptimeout),
                    headers: postHeaders,
                    proxy: proxy,
                    httpversion: init.httpversion,
                    statusCodeOK: true,
                    disposeData: true);
            }
            catch
            {
                return (null, null, null);
            }
        }

        if (string.IsNullOrEmpty(api))
        {
            return (null, null, null);
        }


        string playerHtml = null, nextPt = null, nextPk = null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(api);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err) &&
                err.ValueKind != System.Text.Json.JsonValueKind.Null &&
                err.ToString().Length > 0)
            {
                return (null, null, null);
            }

            if (root.TryGetProperty("next_pt", out var npt) &&
                npt.ValueKind == System.Text.Json.JsonValueKind.String)
                nextPt = npt.GetString();

            if (root.TryGetProperty("next_pk", out var npk) &&
                npk.ValueKind == System.Text.Json.JsonValueKind.String)
                nextPk = npk.GetString();

            if (root.TryGetProperty("player_enc", out var enc) &&
                enc.ValueKind == System.Text.Json.JsonValueKind.String &&
                enc.GetString().Length > 0)
            {
                // Giong JS: xor bang pk HIEN TAI (trang hoac next_pk cua
                // server truoc), khong phai next_pk cua response nay.
                playerHtml = JavCtTo.XorDecrypt(enc.GetString(), pk);
            }
            else if (root.TryGetProperty("player", out var pl))
            {
                playerHtml = pl.ToString();
            }
        }
        catch
        {
            return (null, null, null);
        }

        string embed = JavCtTo.EmbedUrl(playerHtml);
        if (string.IsNullOrEmpty(embed))
            return (null, nextPt, nextPk);

        // DoodStream: embed -> mp4 + referer per-video (F5). Link mp4 phai
        // di route video.mp4 (tra qua .m3u8 la "no EXTM3U delimiter").
        var (mp4, referer) = await JavCtTo.DoodSourceAsync(embed);
        if (!string.IsNullOrEmpty(mp4))
            return (mp4 + "\n" + referer, nextPt, nextPk);

        // Khong phai dood (ryderjet...): packer base36 -> hls (F1).
        // Embed fetch DIRECT truoc (token bind IP phai cung IP voi stream direct).
        // Truoc fetch qua proxy nhung stream direct -> lech IP/timeout.
        // Nhanh chet (nhu FL #B: 5KB, khong packer/links) -> skip, giu nhanh song.
        try
        {
            string embedHtml = await Http.Get(
                embed,
                timeoutSeconds: 12,
                headers: HeadersModel.Init(
                    ("User-Agent", JavCtTo.ChromeUA),
                    ("Referer", pageUrl)),
                httpversion: init.httpversion);

            string master = JavCtTo.StreamHgMaster(embedHtml, pageUrl);
            if (!string.IsNullOrEmpty(master))
                return (master + "\n" + embed, nextPt, nextPk);

            try
            {
                string embedHtml2 = await Http.Get(
                    embed,
                    timeoutSeconds: 12,
                    headers: HeadersModel.Init(
                        ("User-Agent", JavCtTo.ChromeUA),
                        ("Referer", pageUrl)),
                    proxy: proxy,
                    httpversion: init.httpversion);

                string master2 = JavCtTo.StreamHgMaster(embedHtml2, pageUrl);
                if (!string.IsNullOrEmpty(master2))
                    return (master2 + "\n" + embed, nextPt, nextPk);
            }
            catch { }
        }
        catch { }

        // UPN/PP (player.upn.one/#id): API hex + AES (F2).
        if (embed.IndexOf("upn.one", StringComparison.OrdinalIgnoreCase) >= 0 ||
            embed.IndexOf("strp2p.com", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var (upn, upnRef) = await JavCtTo.UpnSourceAsync(embed);
            if (!string.IsNullOrEmpty(upn))
                return (upn + "\n" + upnRef, nextPt, nextPk);
        }

        // Playmate (playmate.to/embed/id): POST /api/s -> sx (F4).
        if (embed.IndexOf("playmate.to", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string pm = await JavCtTo.PlaymateSourceAsync(embed);
            if (!string.IsNullOrEmpty(pm))
                return (pm + "\n" + embed, nextPt, nextPk);
        }

        return (null, nextPt, nextPk);
    }

    [HttpGet]
    [Route("javct/video")]
    [Route("javct/video.m3u8")]
    [Route("javct/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue(q, out string packed) || string.IsNullOrEmpty(packed))
            return OnError("stream_links", refresh_proxy: true);

        // Cache giu "url\nreferer" (F5): referer per-video, khong gop chung.
        string link = packed, referer = JavCtTo.SiteHost + "/";
        int nl = packed.IndexOf('\n');
        if (nl > 0)
        {
            link = packed.Substring(0, nl);
            referer = packed.Substring(nl + 1);
        }

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", referer)
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
