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

        // Taxonomy: genres tu nav home; studios/labels tu trang list
        // day du (/list-studios 168, /list-labels 150). Fetch song song.
        _ = Task.Run(async () =>
        {
            try
            {
                var homeTask = GetPageAsync(SexTbTo.SiteHost + "/");
                var studiosTask = GetPageAsync(SexTbTo.SiteHost + "/list-studios");
                var labelsTask = GetPageAsync(SexTbTo.SiteHost + "/list-labels");
                await Task.WhenAll(homeTask, studiosTask, labelsTask);

                var cats = SexTbTo.Taxonomies(await homeTask, "genre");
                var studios = SexTbTo.Taxonomies(await studiosTask, "studio");
                var labels = SexTbTo.Taxonomies(await labelsTask, "label");

                if (cats.Count > 0 || studios.Count > 0 || labels.Count > 0)
                    hybridCache.Set(memKey,
                        SexTbTo.Menu(hostLocal, cats, studios, labels), cacheTime(720), true);
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
            @"<button\b(?=[^>]*\bepisode-(?:group-item|part)\b)[^>]*\bdata-id\s*=\s*[""']([0-9]+)[""'][^>]*>(.*?)</button\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
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
                timeoutSeconds: Math.Max(15, init.httptimeout),
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
                playerHtml = SexTbTo.XorDecrypt(enc.GetString(), pk);
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

        string embed = SexTbTo.EmbedUrl(playerHtml);
        if (string.IsNullOrEmpty(embed))
            return (null, nextPt, nextPk);

        // DoodStream: embed -> mp4 + referer per-video (F5). Link mp4 phai
        // di route video.mp4 (tra qua .m3u8 la "no EXTM3U delimiter").
        var (mp4, referer) = await SexTbTo.DoodSourceAsync(embed);
        if (!string.IsNullOrEmpty(mp4))
            return (mp4 + "\n" + referer, nextPt, nextPk);

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
                return (master + "\n" + embed, nextPt, nextPk);
        }
        catch { }

        // UPN/PP (player.upn.one/#id): API hex + AES (F2).
        if (embed.IndexOf("upn.one", StringComparison.OrdinalIgnoreCase) >= 0 ||
            embed.IndexOf("strp2p.com", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var (upn, upnRef) = await SexTbTo.UpnSourceAsync(embed);
            if (!string.IsNullOrEmpty(upn))
                return (upn + "\n" + upnRef, nextPt, nextPk);
        }

        // Playmate (playmate.to/embed/id): POST /api/s -> sx (F4).
        if (embed.IndexOf("playmate.to", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string pm = await SexTbTo.PlaymateSourceAsync(embed);
            if (!string.IsNullOrEmpty(pm))
                return (pm + "\n" + embed, nextPt, nextPk);
        }

        return (null, nextPt, nextPk);
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
