using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
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

namespace SexTb;

public class SexTbController : BaseSisiController
{
    public SexTbController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("sextb")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        var cache = await InvokeCacheResult(
            ipkey($"sextb:{search}:{c}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(SexTbTo.Uri(init.host, search, c, pg));
            var playlists = SexTbTo.Playlist("sextb/vidosik", html ?? "");

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
        string memKey = ipkey("sextb:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        // Taxonomy: genres tu nav home; studios HET 27 trang
        // (/list-studios + /a..z, ~2000 hang) sap theo so phim roi top 100.
        // Nhan 4000 muc — khong lay. Fetch song song.
        _ = Task.Run(async () =>
        {
            try
            {
                var homeTask = GetPageAsync(SexTbTo.SiteHost + "/");

                var studioTasks = new List<Task<string>>
                {
                    GetPageAsync(SexTbTo.SiteHost + "/list-studios")
                };
                for (char ch = 'a'; ch <= 'z'; ch++)
                    studioTasks.Add(GetPageAsync(SexTbTo.SiteHost + "/list-studios/" + ch));

                await Task.WhenAll(studioTasks.Prepend(homeTask));

                var cats = SexTbTo.Taxonomies(await homeTask, "genre");

                var pool = new List<(string name, string path, int count)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in studioTasks)
                {
                    foreach (var r in SexTbTo.StudioList(await t))
                    {
                        if (seen.Add(r.path))
                            pool.Add(r);
                    }
                }

                pool.Sort((x, y) => y.count.CompareTo(x.count));

                var studios = new List<(string name, string path)>();
                foreach (var r in pool)
                {
                    if (studios.Count >= 100)
                        break;
                    studios.Add((r.name + " (" + r.count + ")", r.path));
                }

                if (cats.Count > 0 || studios.Count > 0)
                    hybridCache.Set(memKey,
                        SexTbTo.Menu(hostLocal, cats, studios), cacheTime(720), true);
            }
            catch { }
        });

        return SexTbTo.Menu(hostLocal, null, null, null);
    }

    async Task<List<(string name, string path)>> TaxonomiesAsync(string page, string kind, int top = int.MaxValue)
    {
        string memKey = ipkey($"sextb:tax:{kind}");

        if (hybridCache.TryGetValue(memKey, out List<(string name, string path)> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync($"{SexTbTo.SiteHost}{page}");
        var res = SexTbTo.Taxonomies(html, kind, top);
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
            ("User-Agent", SexTbTo.ChromeUA),
            ("Referer", SexTbTo.SiteHost + "/"),
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
    [Route("sextb/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
        {
            // HLS di route .m3u8, mp4 di route .mp4 (lan lon la app bao
            // "no EXTM3U delimiter"). Playmate tra master .txt (HLS) nen
            // phai bat ca "/hls/" + "master.txt".
            string v = k.Value;
            string route = v.Contains(".m3u8") || v.Contains("/hls/") || v.Contains("master.txt")
                ? "video.m3u8" : "video.mp4";
            return $"{host}/sextb/{route}?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}";
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
            : SexTbTo.SiteHost + "/v/" + uri.Trim('/');

        string memKey = ipkey($"sextb:view:{pageUrl}");
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
        // Nut episode da co ten day du (PM #A, DD #B...): chi lay nut
        // `episode-group-item` trong menu group, + nut `episode-part`
        // dang active (tap hien tai). Nut favorite/VIP ngoai episode-list
        // tu rot vi khong co 2 class nay.
        var servers = new List<(string label, string episode)>();

        int listAt = page.IndexOf("episode-list", StringComparison.OrdinalIgnoreCase);
        string scope = listAt >= 0 ? page.Substring(listAt) : page;

        // Cat scope o het khoi episode-list: lay toi `</div></div></div>`
        // dau tien sau nut cuoi de khong lan sang favorite/VIP o footer.
        // Don gian hon: chi match class episode-group-item / episode-part.
        foreach (Match b in Regex.Matches(scope,
            @"<button\b(?=[^>]*\bepisode\b)[^>]*\bdata-id\s*=\s*[""']([0-9]+)[""'][^>]*>(.*?)</button\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            // Bo nut download/vip/favorite: data-id la filmId hoac an
            // khong phai episode, POST vo nghia. episode that la so id
            // rieng (vd 4220574), khac filmId (vd 16943661).
            if (b.Value.IndexOf("btn-download", StringComparison.OrdinalIgnoreCase) >= 0
                || b.Value.IndexOf("vip", StringComparison.OrdinalIgnoreCase) >= 0
                || b.Value.IndexOf("favorite", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            // Bo nut download (btn-download-vip): data-id la filmId,
            // POST episode=filmId tra loi vo nghia.
            if (b.Value.IndexOf("btn-download", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            string label = Regex.Replace(b.Groups[2].Value, "<[^>]+>", " ").Trim();
            label = Regex.Replace(label, @"\s+", " ");
            if (label.Length == 0)
                label = "S" + (servers.Count + 1);
            if (label.Length > 12)
                label = label.Substring(0, 12);

            if (!servers.Exists(s => s.label == label))
                servers.Add((label, b.Groups[1].Value));
        }

        // Khong thay nut episode: episode = filmId (data-source).
        // episode=0 tra E_TOK_MISS o template nay (do tay xac nhan).
        if (servers.Count == 0)
        {
            var dsm = Regex.Match(page, @"data-source\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);
            servers.Add(("MP4", dsm.Success ? dsm.Groups[1].Value : "0"));
        }


        // PHA 1 (tuan tu, nhanh): POST tung episode lay embed + noi
        // next_pt/next_pk. PHA 2 (song song): resolve tung embed -> link.
        // Tach ra vi Playwright 1 server cham (20-40s) se chan ca chuoi.
        var embeds = new List<(string label, string embed)>();
        string curPt = pt.Groups[1].Value, curPk = pkVal;

        foreach (var (label, episode) in servers)
        {
            var (embed, nextPt, nextPk) = await PostEpisodeAsync(
                pageUrl, ds.Groups[1].Value, episode, curPt, curPk);

            if (!string.IsNullOrEmpty(embed) && !embeds.Exists(x => x.label == label))
                embeds.Add((label, embed));

            if (!string.IsNullOrEmpty(nextPt))
                curPt = nextPt;
            if (!string.IsNullOrEmpty(nextPk))
                curPk = nextPk;
        }

        var tasks = new List<Task<(string label, string packed, bool slow)>>();
        foreach (var (label, embed) in embeds)
            tasks.Add(ResolveEmbedAsync(embed, pageUrl, label));

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<(string label, string embed)>();
        foreach (var t in tasks)
        {
            var (label, packed, slow) = await t;
            if (!string.IsNullOrEmpty(packed) && !links.ContainsKey(label))
                links.TryAdd(label, packed);
            else if (slow)
                pending.Add((label, embeds.Find(x => x.label == label).embed));
        }

        // Server nhanh ve truoc de app khong timeout; server cham
        // (Playwright loader) warm nen, lan mo sau co du.
        if (links.Count > 0)
            hybridCache.Set(memKey, links, cacheTime(10));

        if (pending.Count > 0)
        {
            string mk = memKey;
            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var (label, embed) in pending)
                    {
                        var (_, packed, _) = await ResolveEmbedAsync(embed, pageUrl, label, true);
                        if (string.IsNullOrEmpty(packed))
                            continue;

                        if (hybridCache.TryGetValue(mk, out Dictionary<string, string> cur) && cur != null)
                        {
                            if (!cur.ContainsKey(label))
                                cur.TryAdd(label, packed);
                            hybridCache.Set(mk, cur, cacheTime(10), true);
                        }
                    }
                }
                catch { }
            });
        }

        if (links.Count == 0)
            return null;

        return links;
    }

    // POST episode -> player_enc xor -> embed + next tokens.
    async Task<(string embed, string nextPt, string nextPk)> PostEpisodeAsync(
        string pageUrl, string filmId, string episode, string pt, string pk)
    {
        string api;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["episode"] = episode,
                ["filmId"] = filmId,
                ["pt"] = pt
            });

            api = await Http.Post(
                SexTbTo.PlayerApi,
                content,
                timeoutSeconds: Math.Max(10, init.httptimeout),
                headers: HeadersModel.Init(
                    ("User-Agent", SexTbTo.ChromeUA),
                    ("Referer", pageUrl),
                    ("Origin", SexTbTo.SiteHost),
                    ("X-Requested-With", "XMLHttpRequest")),
                proxy: proxy,
                httpversion: init.httpversion,
                statusCodeOK: true,
                disposeData: true);
        }
        catch
        {
            return (null, null, null);
        }

        if (string.IsNullOrEmpty(api))
            return (null, null, null);

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(api);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err) &&
                err.ValueKind != System.Text.Json.JsonValueKind.Null &&
                err.ToString().Length > 0)
                return (null, null, null);

            string nextPt = null, nextPk = null;
            if (root.TryGetProperty("next_pt", out var npt) &&
                npt.ValueKind == System.Text.Json.JsonValueKind.String)
                nextPt = npt.GetString();
            if (root.TryGetProperty("next_pk", out var npk) &&
                npk.ValueKind == System.Text.Json.JsonValueKind.String)
                nextPk = npk.GetString();

            string playerHtml = null;
            if (root.TryGetProperty("player_enc", out var enc) &&
                enc.ValueKind == System.Text.Json.JsonValueKind.String &&
                enc.GetString().Length > 0)
            {
                playerHtml = SexTbTo.XorDecrypt(enc.GetString(), pk);
            }
            else if (root.TryGetProperty("player", out var pl))
            {
                playerHtml = pl.ToString();
            }

            return (SexTbTo.EmbedUrl(playerHtml), nextPt, nextPk);
        }
        catch
        {
            return (null, null, null);
        }
    }

    // Resolve 1 embed -> packed "url\nreferer": dood (F5) -> packer (F1) ->
    // UPN (F2) -> Playmate (F4) -> Playwright loader. Goi song song.
    // Resolve 1 embed. withBrowser=false (foreground): chi HTTP, gap
    // loader thi danh dau slow de warm nen. withBrowser=true: them Playwright.
    // Tra (label, packed, slow).
    async Task<(string label, string packed, bool slow)> ResolveEmbedAsync(
        string embed, string pageUrl, string label, bool withBrowser = false)
    {
        // DoodStream: embed -> mp4 + referer per-video (F5).
        var (mp4, referer) = await SexTbTo.DoodSourceAsync(embed);
        if (!string.IsNullOrEmpty(mp4))
            return (label, mp4 + "\n" + referer, false);

        // Khong phai dood (ryderjet...): packer base36 -> hls (F1).
        // DoodSourceAsync da fetch embed 1 lan; fetch lai de giai packer.
        try
        {
            string embedHtml = await Http.Get(
                embed,
                timeoutSeconds: 12,
                headers: HeadersModel.Init(
                    ("User-Agent", SexTbTo.ChromeUA),
                    ("Referer", pageUrl)),
                proxy: proxy,
                httpversion: init.httpversion);

            string master = SexTbTo.StreamHgMaster(embedHtml, pageUrl);
            if (!string.IsNullOrEmpty(master))
                return (label, master + "\n" + embed, false);
        }
        catch { }

        // UPN/PP (player.upn.one/#id): API hex + AES (F2).
        if (embed.IndexOf("upn.one", StringComparison.OrdinalIgnoreCase) >= 0 ||
            embed.IndexOf("strp2p.com", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var (upn, upnRef) = await SexTbTo.UpnSourceAsync(embed);
            if (!string.IsNullOrEmpty(upn))
                return (label, upn + "\n" + upnRef, false);
        }

        // Playmate (playmate.to/embed/id): POST /api/s -> sx (F4).
        if (embed.IndexOf("playmate.to", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string pm = await SexTbTo.PlaymateSourceAsync(embed);
            if (!string.IsNullOrEmpty(pm))
                return (label, pm + "\n" + embed, false);
        }

        // Cuoi cung: trang loader JS (hglink.to...): URL stream sinh trong
        // trinh duyet, HTTP khong thay. Dung Playwright doc video/src.
        // Loader hglink: NEU gui Referer trang phim thi tra trang rong
        // (title audinifer.com, khong video). Khong Referer thi tu chay
        // sau ~4s. Nen Playwright KHONG gui referer.
        string js = await PlaywrightEmbedAsync(embed, null);
        if (!string.IsNullOrEmpty(js))
            return (label, js + "\n" + embed, false);

        // Trang loader JS (hglink.to...): foreground bo qua (slow=true) de
        // app khong timeout; background warm se chay Playwright.
        if (!withBrowser)
            return (label, null, true);

        return (label, null, false);
    }

    // Embed loader JS: mo trang, doi 15s, doc video/src hoac m3u8/mp4 dau
    // tien trong DOM. Dung chung 1 context (keepopen) + khoa render de do
    // RAM, dong page ngay sau khi xong.
    static SemaphoreSlim _embedLock = new SemaphoreSlim(1, 1);

    async Task<string> PlaywrightEmbedAsync(string embedUrl, string referer)
    {
        IPage page = null;
        if (!await _embedLock.WaitAsync(TimeSpan.FromSeconds(30)))
            return null;

        try
        {
            using var browser = new Shared.PlaywrightCore.PlaywrightBrowser();
            var hdrs = new Dictionary<string, string>
            {
                ["User-Agent"] = SexTbTo.ChromeUA
            };
            if (!string.IsNullOrEmpty(referer))
                hdrs["Referer"] = referer;
            page = await browser.NewPageAsync(init.plugin, hdrs, keepopen: true);

            if (page == null)
                return null;

            string netHit = null;
            page.Request += (_, request) =>
            {
                try
                {
                    var u = request.Url;
                    if (netHit == null && (u.Contains(".m3u8") || u.Contains(".mp4")) &&
                        !u.Contains("ping.") && !u.Contains("jwpltx"))
                    {
                        netHit = u;
                        Console.WriteLine($"SXDBG nethit {u.Substring(0, Math.Min(80, u.Length))}");
                    }
                }
                catch { }
            };

            try
            {
                await page.GotoAsync(embedUrl, new PageGotoOptions
                {
                    Timeout = 20000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
                Console.WriteLine("SXDBG goto ok");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SXDBG goto {ex.GetType().Name}");
            }

            // Loader co Referer thi doi click moi chay video (khong Referer
            // tu chay sau 4s). Bam thu cac nut play pho bien truoc khi poll.
            try
            {
                foreach (var sel in new[] {
                    "#play-btn", ".fakeplayer", ".play-button", ".playbox",
                    ".playbtm", "video", ".jwplayer", "#player" })
                {
                    try
                    {
                        var el = await page.QuerySelectorAsync(sel);
                        if (el != null)
                        {
                            await el.ClickAsync();
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            for (int i = 0; i < 20; i++)
            {
                if (!string.IsNullOrEmpty(netHit))
                    return netHit;

                await Task.Delay(1000);

                string found = null;
                try
                {
                    found = await page.EvaluateAsync<string>(@"() => {
                        const v = document.querySelector('video');
                        if (v && (v.currentSrc || v.src)) return v.currentSrc || v.src;
                        const h = document.documentElement.innerHTML;
                        const m = h.match(/https?:[^'""\s<>]+\.m3u8[^'""\s<>]*/i)
                            || h.match(/https?:[^'""\s<>]+\.mp4[^'""\s<>]*/i);
                        return m ? m[0] : null;
                    }");
                }
                catch { }

                if (!string.IsNullOrEmpty(found))
                    return found;
            }

            return netHit;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { await page?.CloseAsync(); } catch { }
            _embedLock.Release();
        }
    }

    [HttpGet]
    [Route("sextb/video")]
    [Route("sextb/video.m3u8")]
    [Route("sextb/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue(q, out string packed) || string.IsNullOrEmpty(packed))
            return OnError("stream_links", refresh_proxy: true);

        // Cache giu "url\nreferer" (F5): referer per-video, khong gop chung.
        string link = packed, referer = SexTbTo.SiteHost + "/";
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
