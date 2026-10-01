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

namespace JavCt;

public class JavCtController : BaseSisiController
{
    public JavCtController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javct")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        var cache = await InvokeCacheResult(
            ipkey($"javct:{search}:{c}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await GetPageAsync(JavCtTo.Uri(init.host, search, c, pg));
            var playlists = JavCtTo.Playlist("javct/vidosik", html ?? "");

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
        string memKey = ipkey("javct:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string hostLocal = host;

        _ = Task.Run(async () =>
        {
            try
            {
                var catsTask = TaxonomiesAsync("/categories", "category");
                var studiosTask = TaxonomiesAsync("/studios", "studio");
                await Task.WhenAll(catsTask, studiosTask);

                var cats = await catsTask;
                var studios = await studiosTask;

                if (cats.Count > 0 || studios.Count > 0)
                    hybridCache.Set(memKey,
                        JavCtTo.Menu(hostLocal, cats, studios), cacheTime(720), true);
            }
            catch { }
        });

        return JavCtTo.Menu(hostLocal, null, null);
    }

    async Task<List<(string name, string path)>> TaxonomiesAsync(string page, string kind)
    {
        string memKey = ipkey($"javct:tax:{kind}");

        if (hybridCache.TryGetValue(memKey, out List<(string name, string path)> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync($"{JavCtTo.SiteHost}{page}");
        var res = JavCtTo.Taxonomies(html, kind);
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

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/javct/video.mp4?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
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

        string api;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["episode"] = "0",
                ["filmId"] = ds.Groups[1].Value,
                ["pt"] = pt.Groups[1].Value
            });

            api = await Http.Post(
                JavCtTo.PlayerApi,
                content,
                timeoutSeconds: Math.Max(15, init.httptimeout),
                headers: HeadersModel.Init(
                    ("User-Agent", JavCtTo.ChromeUA),
                    ("Referer", pageUrl),
                    ("Origin", JavCtTo.SiteHost),
                    ("X-Requested-With", "XMLHttpRequest")),
                proxy: proxy,
                httpversion: init.httpversion,
                statusCodeOK: true,
                disposeData: true);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(api))
        {
            return null;
        }


        string playerHtml = null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(api);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err) &&
                err.ValueKind != System.Text.Json.JsonValueKind.Null &&
                err.ToString().Length > 0)
                return null;

            if (root.TryGetProperty("player_enc", out var enc) &&
                enc.ValueKind == System.Text.Json.JsonValueKind.String &&
                enc.GetString().Length > 0)
            {
                // Giong JS: xor bang __pk CUA TRANG (pk.Success), khong phai
                // next_pk (khoa cho request KE TIEP).
                string key = pk.Success ? pk.Groups[1].Value : "";
                playerHtml = JavCtTo.XorDecrypt(enc.GetString(), key);
            }
            else if (root.TryGetProperty("player", out var pl))
            {
                playerHtml = pl.ToString();
            }
        }
        catch
        {
            return null;
        }

        string embed = JavCtTo.EmbedUrl(playerHtml);
        if (string.IsNullOrEmpty(embed))
            return null;

        // DoodStream: embed -> mp4 + referer per-video (F5). Link mp4 phai
        // di route video.mp4 (tra qua .m3u8 la "no EXTM3U delimiter").
        // Token dung 1 lan nen cache ngan (10 phut theo chuan module).
        var (mp4, referer) = await JavCtTo.DoodSourceAsync(embed);
        if (string.IsNullOrEmpty(mp4))
            return null;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MP4"] = mp4 + "\n" + referer
        };

        hybridCache.Set(memKey, links, cacheTime(10));
        return links;
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
