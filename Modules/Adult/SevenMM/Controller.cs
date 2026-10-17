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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace SevenMM;

public class SevenMMController : BaseSisiController
{
    public SevenMMController() : base(ModInit.conf) { }

    // Render bang Chrome (giong MissAV). 7mmtv.sx sau Cloudflare.
    static readonly SemaphoreSlim _pwLock = new SemaphoreSlim(1, 1);

    static List<HeadersModel> SiteHeaders()
        => HeadersModel.Init(
            ("User-Agent", SevenMMTo.ChromeUA),
            ("Referer", SevenMMTo.SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));

    static string PageUrl(string uri)
        => !string.IsNullOrEmpty(uri) && uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : SevenMMTo.SiteHost + "/" + (uri ?? "").TrimStart('/');

    // Cloudflare challenge co the tra ve theo ngon ngu ("Just a moment...",
    // "Один момент…", "Attendez…" ...) -> nhan dien theo marker ky thuat.
    //
    // CANH BAO: trang THAT (~50KB) van chua "/cdn-cgi/challenge-platform/scripts/jsd/main.js"
    // (script JSD Cloudflare chen vao moi trang) => KHONG duoc lay chuoi do lam dau hieu
    // challenge, neu khong moi trang tot deu bi coi la challenge -> 503 "playlists".
    // Trang challenge that su thi rat ngan -> chi xet khi s.Length nho.
    static bool IsChallenge(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;

        if (s.Contains("Just a moment") || s.Contains("Один момент") || s.Contains("Attention Required")
            || s.Contains("Enable JavaScript and cookies to continue") || s.Contains("Checking your browser")
            || s.Contains("challenge-error-text") || s.Contains("cf-mitigated"))
            return true;

        return s.Length < 15000 &&
               (s.Contains("/cdn-cgi/challenge-platform") || s.Contains("cf-chl") || s.Contains("challenges.cloudflare.com"));
    }

    // 7mmtv.sx hay drop TLS handshake (TCP connect OK nhung TLS treo -> 000):
    // do duoc ~60% moi handshake va doc lap nhau. Thu SONG SONG 3 lan (httpHydra + 2 direct),
    // lay cai xong truoc. .NET pool connection theo handler (PooledConnectionLifetime 10 phut)
    // nen sau khi co 1 ket noi tot thi cac request sau tai dung ket noi do, khong handshake lai.
    async Task<string> GetPageAsync(string url, int seconds = 8)
    {
        string a = null, b = null, c = null;
        var headers = SiteHeaders();

        var t1 = Task.Run(async () =>
        {
            try { await httpHydra.GetSpan(url, span => { a = span.ToString(); }, addheaders: headers); } catch { }
        });

        var t2 = Task.Run(async () =>
        {
            try { b = await Http.Get(url, timeoutSeconds: seconds, httpversion: init.httpversion, proxy: proxy, headers: headers); } catch { }
        });

        var t3 = Task.Run(async () =>
        {
            try { c = await Http.Get(url, timeoutSeconds: seconds, httpversion: init.httpversion, proxy: proxy, headers: headers); } catch { }
        });

        // Http.BaseGet ep timeout toi thieu 8s (Math.Max(8, timeoutSeconds)) -> deadline phai >= 8s.
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(8, seconds) + 3);
        while (DateTime.UtcNow < deadline)
        {
            if (Ok(a)) return a;
            if (Ok(b)) return b;
            if (Ok(c)) return c;
            if (t1.IsCompleted && t2.IsCompleted && t3.IsCompleted) break;
            await Task.Delay(200);
        }

        if (Ok(a)) return a;
        if (Ok(b)) return b;
        if (Ok(c)) return c;

        return await PlaywrightPageAsync(url);

        static bool Ok(string s)
            => !string.IsNullOrEmpty(s) && s.Length > 2000 && !IsChallenge(s);
    }

    async Task<string> PlaywrightPageAsync(string url)
    {
        if (!await _pwLock.WaitAsync(TimeSpan.FromSeconds(60)))
            return null;

        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync("SevenMM", new Dictionary<string, string>
                {
                    ["User-Agent"] = SevenMMTo.ChromeUA,
                    ["Referer"] = SevenMMTo.SiteHost + "/"
                }, keepopen: true);

                if (page == null)
                    return null;

                var resp = await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = 30000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });

                if (resp == null)
                    return null;

                // Cho CF managed challenge giai (toi ~20s). Neu khong xong
                // (challenge tuong tac / IP bi danh dau) -> tra null, khong parse rac.
                for (int i = 0; i < 20; i++)
                {
                    string content = null;
                    try { content = await page.ContentAsync(); } catch { }

                    if (!string.IsNullOrEmpty(content) && content.Length > 2000 && !IsChallenge(content))
                        return content;

                    await Task.Delay(1000);
                }

                return null;
            }
        }
        catch { return null; }
        finally
        {
            _pwLock.Release();
        }
    }

    [HttpGet, Staticache]
    [Route("sevenmm")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"sevenmm:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists;

            if (!string.IsNullOrEmpty(search))
            {
                // Form tim kiem (do that qua jina): POST {host}/en/searchform_search/all/index.html
                //   <input name="search_keyword">, <select name="search_type"> default "searchall",
                //   <input type="hidden" name="op" value="search">
                string body = "search_keyword=" + HttpUtility.UrlEncode(search) + "&search_type=searchall&op=search";
                string html = await httpHydra.Post(SevenMMTo.SearchUrl(init.host), body, addheaders: SiteHeaders());
                playlists = SevenMMTo.Playlist("sevenmm/vidosik", html);
            }
            else
            {
                string html = await GetPageAsync(SevenMMTo.ListUrl(init.host, c, pg));
                playlists = SevenMMTo.Playlist("sevenmm/vidosik", html);
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync());
    }

    // Menu 2 tang: Danh muc (muc cha + sub lay dong tu nav) + The loai (genre).
    // Harvest genre UNCENSORED. Site khong co trang index -> phai doc pill Genre tung trang detail,
    // nen PHAI co ngan sach: moi lan toi da ~25s / 3 luong, ket qua CONG DON (cache 7 ngay).
    // Vai lan build la du ~200 genre ma khong doi mot lan (site siet khi bi goi don dap).
    async Task<Dictionary<string, string>> UncenSetAsync()
    {
        string setKey = ipkey("sevenmm:uncenset");
        var acc = new Dictionary<string, string>(StringComparer.Ordinal);

        if (hybridCache.TryGetValue(setKey, out List<string> prev) && prev != null)
            foreach (string row in prev)
            {
                int t = row.IndexOf('\t');
                if (t > 0) acc[row.Substring(0, t)] = row.Substring(t + 1);
            }

        if (acc.Count >= 200)
            return acc;

        string poolKey = ipkey("sevenmm:uncenpool");
        if (!hybridCache.TryGetValue(poolKey, out List<string> pool) || pool == null || pool.Count == 0)
        {
            pool = new List<string>();
            for (int pg = 1; pg <= 12 && pool.Count < 240; pg++)
            {
                string h = await GetPageAsync($"{SevenMMTo.SiteHost}/en/uncensored_list/all/{pg}.html");
                foreach (string u in SevenMMTo.DetailUrls(h))
                    if (!pool.Contains(u)) pool.Add(u);
            }
            if (pool.Count > 0)
                hybridCache.Set(poolKey, pool, cacheTime(360));
        }
        if (pool.Count == 0)
            return acc;

        var rnd = new Random();
        var deadline = DateTime.UtcNow.AddSeconds(25);
        var sem = new SemaphoreSlim(3);
        var tasks = new List<Task>();

        foreach (string u in pool.OrderBy(_ => rnd.Next()))
        {
            if (acc.Count >= 200 || DateTime.UtcNow > deadline) break;

            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync();
                try
                {
                    if (acc.Count >= 200 || DateTime.UtcNow > deadline) return;

                    var pairs = SevenMMTo.UncenGenres(await GetPageAsync(u));
                    if (pairs.Count == 0) return;

                    lock (acc)
                        foreach (var (id, name) in pairs)
                            if (!acc.ContainsKey(id)) acc[id] = name;
                }
                catch { }
                finally { sem.Release(); }
            }));
        }

        try { await Task.WhenAll(tasks); } catch { }
        sem.Dispose();

        Console.WriteLine($"SevenMM: uncen pool={pool?.Count} genres={acc.Count}");

        if (acc.Count > 0)
            hybridCache.Set(setKey, acc.Select(x => x.Key + "\t" + x.Value).ToList(), cacheTime(7 * 24 * 60));

        return acc;
    }

    async Task<List<MenuItem>> MenuAsync()
    {
        string key = ipkey("sevenmm:menu");
        if (hybridCache.TryGetValue(key, out List<MenuItem> cached) && cached != null && cached.Count > 0)
            return cached;

        var menu = new List<MenuItem>()
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = $"{host}/sevenmm" }
        };

        try
        {
            string home = await GetPageAsync($"{SevenMMTo.SiteHost}/en/");
            var nav = SevenMMTo.Nav(home);

            if (nav.Count > 0)
            {
                var sections = new List<MenuItem>();

                foreach (var (p, title, subs) in nav)
                {
                    var sub = new List<MenuItem>() { new("Mới nhất", SevenMMTo.MenuUrl(host, p)) };

                    foreach (var (sp, st) in subs)
                    {
                        if (sp.Contains("_categorylist")) continue; // genre lay rieng ben duoi
                        sub.Add(new MenuItem(st, SevenMMTo.MenuUrl(host, sp)));
                    }

                    // Khong co sub dung duoc -> chinh muc CHA thanh mot muc don, giu TEN THAT.
                    // (Truoc day lay sub[0] = "Moi nhat" nen moi muc cha deu hien "Moi nhat" trung nhau.)
                    if (sub.Count == 1)
                    {
                        if (!string.IsNullOrWhiteSpace(title) &&
                            !sections.Any(x => x.title == title || x.playlist_url == sub[0].playlist_url))
                            sections.Add(new MenuItem(title, SevenMMTo.MenuUrl(host, p)));
                    }
                    else
                        sections.Add(new MenuItem() { title = title, playlist_url = "submenu", submenu = sub });
                }

                menu.Add(new MenuItem() { title = "Danh mục", playlist_url = "submenu", submenu = sections });
            }

        var genres = new List<MenuItem>();

        // UNCENSORED: khong co taxonomy tren site -> harvest tu detail (xem UncenSetAsync).
        var uncen = await UncenSetAsync();
        if (uncen.Count > 0)
        {
            var sub = uncen.OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .Select(x => new MenuItem(x.Value,
                    SevenMMTo.MenuUrl(host, $"uncensored_category/{x.Key}/{Uri.EscapeDataString(x.Value)}")))
                .ToList();

            genres.Add(new MenuItem() { title = "Thể loại Uncen", playlist_url = "submenu", submenu = sub });
        }

        // Client SISI chi ho tro submenu MOT TANG (skill lampac-adult-module §9):
        //   => menu dung DUNG 2 TANG, moi loai danh muc la MOT MUC TANG 1 rieng,
        //      submenu PHANG, bam tang 2 ra thang list phim.
        // Site co 3 bo genre KHAC NHAU (do 2026-10-17, ten do chinh site dat):
        //   censored_categorylist   -> "Censored Genre" -> censored_category/{id}/{name}
        //   amateurjav_categorylist -> "Amateur Genre"  -> amateurjav_category/{id}/{name}
        //   uncensored              -> site KHONG co trang index -> harvest tu pill detail
        // Nguong 300 muc/submenu; vuot nguong moi cat chunk <= 300, moi chunk 1 muc tang 1.
        const int MenuBucket = 300;

        foreach (string catPath in new[] { "amateurjav_categorylist/list/index.html", "censored_categorylist/list/index.html" })
        {
            string html = await GetPageAsync($"{SevenMMTo.SiteHost}/en/{catPath}");
            string prefix = catPath.Split('_')[0];          // "amateurjav" | "censored"
            string label = prefix == "censored" ? "Cen" : "Amateur";

            // Dedupe theo TEN hien thi (mot so ten xuat hien 2 lan trong cung trang).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var one = new List<MenuItem>();

            foreach (var (gp, gt) in SevenMMTo.Genres(html, prefix))
            {
                if (string.IsNullOrEmpty(gt) || !seen.Add(gt)) continue;
                one.Add(new MenuItem(gt, SevenMMTo.MenuUrl(host, gp)));
            }

            if (one.Count == 0) continue;

            if (one.Count <= MenuBucket)
            {
                // Ten tang 1 de tran, khong gan so muc.
                genres.Add(new MenuItem() { title = $"Thể loại {label}", playlist_url = "submenu", submenu = one });
            }
            else
            {
                int total = (one.Count + MenuBucket - 1) / MenuBucket;
                for (int i = 0, n = 1; i < one.Count; i += MenuBucket, n++)
                {
                    var chunk = one.Skip(i).Take(MenuBucket).ToList();
                    // Cat giua mot bo -> BAT BUOC hau to (1/2), (2/2), neu khong se co 2 dong trung ten.
                    genres.Add(new MenuItem() { title = $"Thể loại {label} ({n}/{total})", playlist_url = "submenu", submenu = chunk });
                }
            }
        }

        foreach (var g in genres)
            menu.Add(g);
        }
        catch { }

        if (menu.Count <= 1)
            menu = SevenMMTo.MenuFallback(host);

        hybridCache.Set(key, menu, cacheTime(360));
        return menu;
    }

    // Trang detail + mvarr + inline key. Cache ngan (20s) vi Vidosik/Video co the
    // goi lien tiep. Tra ve: html, vars (key/xor/aesKey/aesIv) va mvarr.
    async Task<(string html, SevenMMTo.EncVars vars, Dictionary<string, (string enc, string prefix)> arr)> DetailAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return (null, null, null);

        string pageUrl = PageUrl(uri);
        string key = ipkey($"sevenmm:detailhtml:{pageUrl}");
        if (hybridCache.TryGetValue(key, out string cachedHtml) && !string.IsNullOrEmpty(cachedHtml))
            return (cachedHtml, SevenMMTo.ReadEncVars(cachedHtml), SevenMMTo.Mvarr(cachedHtml));

        string html = await GetPageAsync(pageUrl);
        if (!string.IsNullOrEmpty(html))
            hybridCache.Set(key, html, cacheTime(20));

        return (html, SevenMMTo.ReadEncVars(html), SevenMMTo.Mvarr(html));
    }

    // Chon embed URL theo srv (SW/VH/DD) + Referer dung cho CDN cua server do.
    //   SW -> mvarr['37_1'] giai ma -> https://mmsi02.com/e/<id>  (Referer mmsi02)
    //   VH -> mvarr['38_1'] giai ma -> https://mmvh02.com/v/<id>  (Referer 7mmtv.sx)
    //   DD -> JSON-LD contentUrl (playmogo)                       (Referer host DD)
    async Task<(string embed, string referer)> StreamEmbedAsync(string uri, string srv)
    {
        var (html, vars, arr) = await DetailAsync(uri);
        if (string.IsNullOrEmpty(html))
            return (null, null);

        string jsonld = SevenMMTo.EmbedUrl(html);
        srv = (srv ?? "").Trim().ToUpperInvariant();

        // Mac dinh: uu tien SP (nhanh + on dinh nhat) -> SW -> VH -> DD.
        if (string.IsNullOrEmpty(srv))
        {
            if (arr != null && arr.ContainsKey("42_1")) srv = "SP";
            else if (arr != null && arr.ContainsKey("37_1")) srv = "SW";
            else if (arr != null && arr.ContainsKey("38_1")) srv = "VH";
            else srv = "DD";
        }

        if (srv == "DD")
            return (jsonld, SevenMMTo.SiteHost + "/");

        string encKey = srv == "SW" ? "37_1" : srv == "SP" ? "42_1" : "38_1";

        if (arr != null && vars != null && vars.ok && arr.TryGetValue(encKey, out var e))
        {
            string id = SevenMMTo.DecryptEnc(e.enc, vars);
            if (!string.IsNullOrEmpty(id))
            {
                // prefix cua SP la "//7mmtv.sx/..." (protocol-relative) -> phai them https:
                // neu khong, Http.Get() fail va Vidosik tra "stream_links".
                string pfx = e.prefix;
                if (!string.IsNullOrEmpty(pfx) && pfx.StartsWith("//")) pfx = "https:" + pfx;
                string embed = pfx + id;
                string referer = SevenMMTo.OriginOf(e.prefix) ?? (SevenMMTo.SiteHost + "/");
                // VH (VidHide) CDN da do: bat buoc Referer = 7mmtv.sx
                if (srv == "VH" || srv == "SP") referer = SevenMMTo.SiteHost + "/";
                return (embed, referer);
            }
        }

        // fallback: JSON-LD (VH) — dung cho phim cu / khi mvarr thieu
        return (jsonld, SevenMMTo.SiteHost + "/");
    }

    // Lay body cua trang embed (vidhide/streamhide/playmogo/...), cache lai de
    // DoodResolve va Video() khong fetch 2 lan.
    async Task<string> EmbedBodyAsync(string embedUrl, string referer = null)
    {
        if (string.IsNullOrWhiteSpace(embedUrl))
            return null;

        string key = ipkey($"sevenmm:embedbody:{embedUrl}");
        if (hybridCache.TryGetValue(key, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var headers = HeadersModel.Init(
            ("User-Agent", SevenMMTo.ChromeUA),
            ("Referer", string.IsNullOrEmpty(referer) ? SevenMMTo.SiteHost + "/" : referer),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));

        string body = await Http.Get(embedUrl, timeoutSeconds: 12, httpversion: init.httpversion, proxy: proxy, headers: headers);
        if (!string.IsNullOrEmpty(body))
            hybridCache.Set(key, body, cacheTime(20));

        return body;
    }

    // DoodStream (playmogo.com va cac host cung ho): body embed chua "pass_md5/".
    // Tra ve (mp4 url, host API) hoac (null,null) neu khong phai DoodStream.
    async Task<(string mp4, string referer)> DoodResolveAsync(string embedUrl)
    {
        if (string.IsNullOrWhiteSpace(embedUrl))
            return (null, null);

        string body = await EmbedBodyAsync(embedUrl);
        if (string.IsNullOrEmpty(body))
            return (null, null);

        var m = Regex.Match(body, @"(pass_md5/[A-Za-z0-9_\-]+/[A-Za-z0-9]+)");
        if (!m.Success)
            return (null, null);

        string hostApi = embedUrl;
        int at = hostApi.IndexOf("/e/", StringComparison.OrdinalIgnoreCase);
        if (at > 8) hostApi = hostApi.Substring(0, at);

        string passPath = m.Groups[1].Value;
        string src = null;
        for (int attempt = 0; attempt < 2 && string.IsNullOrEmpty(src); attempt++)
        {
            src = await Http.Get($"{hostApi}/{passPath}?referer=7mmtv.sx",
                timeoutSeconds: 12, httpversion: 1, proxy: proxy,
                headers: HeadersModel.Init(
                    ("User-Agent", SevenMMTo.ChromeUA),
                    ("Referer", SevenMMTo.SiteHost + "/")));
            src = (src ?? "").Trim();
            if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                src = null;
        }
        if (string.IsNullOrEmpty(src))
            return (null, null);

        string token = passPath.Substring(passPath.LastIndexOf('/') + 1);
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string url = src + (src.Contains('?') ? "&" : "?") + $"token={token}&expiry={expiry}";
        return (url, hostApi);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("sevenmm/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var (html, vars, arr) = await DetailAsync(uri);
        if (string.IsNullOrEmpty(html))
            return OnError("stream_links", refresh_proxy: true);

        string jsonld = SevenMMTo.EmbedUrl(html);
        string encUri = HttpUtility.UrlEncode(uri);
        var dict = new Dictionary<string, string>();

        // SP (SV3 / play.php): mvarr['42_1'] -> //7mmtv.sx/assets/js/play/play.php?id=<id>
        if (arr != null && vars != null && vars.ok && arr.TryGetValue("42_1", out var sp))
        {
            string spid = SevenMMTo.DecryptEnc(sp.enc, vars);
            if (!string.IsNullOrEmpty(spid))
                dict["SP"] = $"{host}/sevenmm/video.m3u8?uri={encUri}&srv=SP";
        }

        // SW (StreamHide): mvarr['37_1'] -> giai ma ENC -> https://mmsi02.com/e/<id>
        if (arr != null && vars != null && vars.ok && arr.TryGetValue("37_1", out var sw))
        {
            string id = SevenMMTo.DecryptEnc(sw.enc, vars);
            if (!string.IsNullOrEmpty(id))
                dict["SW"] = $"{host}/sevenmm/video.m3u8?uri={encUri}&srv=SW";
        }

        // VH (VidHide): mvarr['38_1'] hoac JSON-LD contentUrl
        bool hasVH = (arr != null && arr.ContainsKey("38_1"))
                     || (!string.IsNullOrEmpty(jsonld) && !SevenMMTo.IsDood(jsonld));
        if (hasVH)
            dict["VH"] = $"{host}/sevenmm/video.m3u8?uri={encUri}&srv=VH";

        // DD (DoodStream/playmogo): chi phim cu moi co
        if (!string.IsNullOrEmpty(jsonld) && SevenMMTo.IsDood(jsonld))
        {
            var dood = await DoodResolveAsync(jsonld);
            if (!string.IsNullOrEmpty(dood.mp4))
            {
                string dkey = ipkey($"sevenmm:dood:{jsonld}");
                hybridCache.Set(dkey, dood.mp4 + "\n" + dood.referer, cacheTime(10));

                var streamHeaders = httpHeaders(init, HeadersModel.Init(
                    ("User-Agent", SevenMMTo.ChromeUA),
                    ("Referer", dood.referer.TrimEnd('/') + "/")));
                dict["DD"] = HostStreamProxy(dood.mp4, streamHeaders);
            }
        }

        if (dict.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(dict);
    }

    [HttpGet]
    [Route("sevenmm/video")]
    [Route("sevenmm/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string srv = null, string q = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (embed, referer) = await StreamEmbedAsync(uri, srv);
        if (string.IsNullOrEmpty(embed))
            return OnError("stream_links", refresh_proxy: true);

        // ===== DD (DoodStream) -> file .mp4 =====
        if (SevenMMTo.IsDood(embed))
        {
            string dkey = ipkey($"sevenmm:dood:{embed}");
            if (!hybridCache.TryGetValue(dkey, out string dval) || string.IsNullOrEmpty(dval))
            {
                var dood = await DoodResolveAsync(embed);
                if (!string.IsNullOrEmpty(dood.mp4))
                {
                    dval = dood.mp4 + "\n" + dood.referer;
                    hybridCache.Set(dkey, dval, cacheTime(10));
                }
            }

            if (string.IsNullOrEmpty(dval))
                return OnError("stream_links", refresh_proxy: true);

            string[] dp = dval.Split('\n');
            var dHeaders = httpHeaders(init, HeadersModel.Init(
                ("User-Agent", SevenMMTo.ChromeUA),
                ("Referer", dp.Length > 1 && !string.IsNullOrEmpty(dp[1]) ? dp[1].TrimEnd('/') + "/" : SevenMMTo.SiteHost + "/")));

            return Redirect(HostStreamProxy(dp[0], dHeaders));
        }

        // ===== SP (SV3 / play.php) -> master HLS tu sources[] trong HTML =====
        if ((srv ?? "").Trim().ToUpperInvariant() == "SP" || embed.Contains("play.php"))
        {
            string spKey = ipkey($"sevenmm:sp:{embed}");

            if (!hybridCache.TryGetValue(spKey, out string spMaster) || string.IsNullOrEmpty(spMaster))
            {
                string spHtml = await EmbedBodyAsync(embed, referer);
                var sources = SevenMMTo.SpSources(spHtml);
                if (sources.Count == 0)
                    return OnError("stream_links", refresh_proxy: true);

                // SpSources da sort size giam dan -> 720p truoc 480p
                spMaster = sources[0].url;
                hybridCache.Set(spKey, spMaster, cacheTime(120));
            }

            if (string.IsNullOrEmpty(spMaster))
                return OnError("stream_links", refresh_proxy: true);

            var spHeaders = httpHeaders(init, HeadersModel.Init(
                ("User-Agent", SevenMMTo.ChromeUA),
                ("Referer", referer)));

            return Redirect(HostStreamProxy(spMaster, spHeaders));
        }

        // ===== SW / VH (StreamHide / VidHide) -> master HLS =====
        string key = ipkey($"sevenmm:stream:{embed}");

        if (!hybridCache.TryGetValue(key, out string master) || string.IsNullOrEmpty(master))
        {
            string embedHtml = await EmbedBodyAsync(embed, referer);
            var masters = SevenMMTo.Masters(embedHtml, SevenMMTo.OriginOf(embed));
            if (masters.Count == 0)
                return OnError("stream_links", refresh_proxy: true);

            var headers = HeadersModel.Init(
                ("User-Agent", SevenMMTo.ChromeUA),
                ("Referer", referer));

            // Probe SONG SONG, lay master dau tien tra #EXTM3U (khong cam masters[0]).
            var tasks = masters.ToDictionary(m => m, m => Task.Run(async () =>
            {
                try { return await Http.Get(m, timeoutSeconds: 12, httpversion: 1, headers: headers); }
                catch { return null; }
            }));

            await Task.WhenAll(tasks.Values);

            foreach (string m in masters)
            {
                string probe = tasks[m].Result;
                if (!string.IsNullOrEmpty(probe) && probe.StartsWith("#EXTM3U"))
                {
                    master = m;
                    break;
                }
            }

            if (string.IsNullOrEmpty(master))
                master = masters[0];

            hybridCache.Set(key, master, cacheTime(8));
        }

        if (string.IsNullOrEmpty(master))
            return OnError("stream_links", refresh_proxy: true);

        // CDN tra 403 neu thieu Referer -> stream phai di qua HostStreamProxy kem header.
        var streamHeaders = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", SevenMMTo.ChromeUA),
            ("Referer", referer)));

        return Redirect(HostStreamProxy(master, streamHeaders));
    }
}
