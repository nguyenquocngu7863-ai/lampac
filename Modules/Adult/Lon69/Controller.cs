using Microsoft.AspNetCore.Mvc;
using System;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Lon69;

public class Lon69Controller : BaseSisiController
{
    public Lon69Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lon69")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (pg < 1) pg = 1;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        var menuTask = MenuAsync();

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"lon69:v3:{search}:{c}:{pg}"), 10, async e =>
        {
            string url = Lon69To.BuildUri(init.host, search, c, pg);
            string html = await GetPageAsync(url);
            var playlists = Lon69To.Playlist("lon69/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, Lon69To.Pages(html)));
        });

        if (rch?.enable == true) StatiCacheDisabled = true;
        if (!cache.IsSuccess) return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache, await menuTask, total_pages: cache.Value.total_pages);
    }

    async Task<List<MenuItem>> MenuAsync()
    {
        string memKey = ipkey("lon69:menu:v3");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync(Lon69To.SiteHost + "/");
        var menu = Lon69To.Menu(host, Lon69To.Categories(html ?? ""), Lon69To.Tags(html ?? ""));
        if (menu.Count > 1)
            hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    #region popup (long-press)
    // Trang detail (theme vlxx) có: div.category-tag (thể loại), div.actress-tag (tag),
    // aside.related_single (30 phim liên quan), iframe play.php (player).
    [HttpGet]
    [Route("lon69/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = new { items = new object[0], groups = new object[0] };

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string pageUrl = Lon69To.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(empty);

        string memKey = ipkey($"lon69:info:v3:{pageUrl}");
        if (!hybridCache.TryGetValue(memKey, out string pageHtml) || string.IsNullOrEmpty(pageHtml))
        {
            pageHtml = await GetPageAsync(pageUrl);
            if (string.IsNullOrEmpty(pageHtml))
                return Json(empty);
            hybridCache.Set(memKey, pageHtml, cacheTime(360));
        }

        var groups = new List<object>();

        var cats = Lon69To.Chips(pageHtml, false);
        if (cats.Count > 0)
            groups.Add(new
            {
                title = "Thể loại",
                items = cats.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/lon69?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        var tags = Lon69To.Chips(pageHtml, true);
        if (tags.Count > 0)
            groups.Add(new
            {
                title = "Thẻ",
                items = tags.Select(x => new
                {
                    title = x.name,
                    url = $"{host}/lon69?c={HttpUtility.UrlEncode(x.path)}"
                }).ToList()
            });

        if (Lon69To.RelatedBlock(pageHtml) != null)
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new
                    {
                        title = "Phim liên quan",
                        plain = true,
                        url = $"{host}/lon69?rel={HttpUtility.UrlEncode(pageUrl)}"
                    }
                }
            });

        if (groups.Count == 0)
            return Json(empty);

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        string pageUrl = Lon69To.NormalizePageUrl(rel);
        if (pg > 1 || string.IsNullOrEmpty(pageUrl))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult(
            ipkey($"lon69:rel:v3:{pageUrl}"), 180, jsonContext.ListPlaylistItem, async e =>
        {
            string pageHtml = await GetPageAsync(pageUrl);
            var playlists = Lon69To.Related("lon69/vidosik", pageHtml ?? "");
            if (playlists == null || playlists.Count == 0)
                return e.Success(new List<PlaylistItem>());
            return e.Success(playlists);
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        if (cache.Value == null || cache.Value.Count == 0)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        return PlaylistResult(cache, await MenuAsync(), total_pages: 1);
    }
    #endregion

    [HttpGet, Staticache(manually: true)]
    [Route("lon69/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var (src, hls) = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(src))
            return OnError("stream_links", refresh_proxy: true);

        var recomends = await RecomendsAsync(uri);
        if (related)
            return PlaylistResult(recomends, false, null, total_pages: 1);

        return Json(new
        {
            qualitys = new Dictionary<string, string>()
            {
                [hls ? "HLS" : "MP4"] = hls
                    ? $"{host}/lon69/video.m3u8?uri={HttpUtility.UrlEncode(uri)}"
                    : $"{host}/lon69/video.mp4?uri={HttpUtility.UrlEncode(uri)}"
            },
            recomends
        });
    }

    // Khoi "lien quan" trang detail -> playlist "tiep theo" trong player (cache 20 phut)
    async Task<List<PlaylistItem>> RecomendsAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return new List<PlaylistItem>();

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : Lon69To.SiteHost + "/" + uri.TrimStart('/');

        string memKey = ipkey($"lon69:rec:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out List<PlaylistItem> c) && c != null)
            return c;

        string html = await GetPageAsync(pageUrl);
        var rec = Lon69To.Related("lon69/vidosik", html ?? "");
        if (rec == null)
            rec = new List<PlaylistItem>();

        foreach (var pl in rec)
        {
            if (pl?.video != null && !pl.video.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                pl.video = $"{host}/{pl.video.TrimStart('/')}";
        }

        if (rec.Count > 0)
            hybridCache.Set(memKey, rec, cacheTime(20));

        return rec;
    }

    [HttpGet]
    [Route("lon69/video.m3u8")]
    [Route("lon69/video.mp4")]
    async public Task<ActionResult> Video(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (src, hls) = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(src))
            return OnError("stream_links", refresh_proxy: true);

        string referer = "https://lon69.ink/";
        try { referer = new Uri(src).GetLeftPart(UriPartial.Authority) + "/"; } catch { }

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", referer),
            ("User-Agent", Lon69To.ChromeUA)
        ));

        return Redirect(HostStreamProxy(src, direct));
    }

    // ================= resolve =================
    // detail -> iframe play.php -> let SRC = "…hls.php?...&p=master.m3u8" (hoặc .mp4)
    async Task<(string url, bool hls)> ResolveAsync(string uri)
    {
        string pageUrl = Lon69To.NormalizePageUrl(uri);
        if (string.IsNullOrEmpty(pageUrl)) return (null, false);

        string memKey = ipkey($"lon69:stream:v4:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out (string url, bool hls) hit) && !string.IsNullOrEmpty(hit.url))
            return hit;

        string detailHtml = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(detailHtml)) return (null, false);

        var (src, hls) = await ResolveFromDetailAsync(detailHtml, pageUrl);
        if (string.IsNullOrEmpty(src)) return (null, false);

        hybridCache.Set(memKey, (src, hls), cacheTime(120));
        return (src, hls);
    }

    async Task<(string url, bool hls)> ResolveFromDetailAsync(string detailHtml, string pageUrl)
    {
        // CHỈ khi trang detail nhúng thẳng SRC (nếu không, trang detail luôn có quảng cáo
        // sever3.playphe3x.space/tvc.mp4 -> nếu quét .mp4 bừa sẽ trả NHẦM ad 31 giây)
        if (Regex.IsMatch(detailHtml, @"\bSRC\s*=\s*""", RegexOptions.IgnoreCase))
        {
            string inline = Lon69To.SourceFromPlayer(detailHtml);
            if (!string.IsNullOrEmpty(inline)) return (inline, Lon69To.IsHls(inline));
        }

        string src = null;

        string player = Lon69To.PlayerUrl(detailHtml);
        if (string.IsNullOrEmpty(player)) return (null, false);

        string referer = pageUrl;
        foreach (string cu in Candidates(player).Concat(new[] { player + (player.Contains("?") ? "&" : "?") + "autoplay=1" }))
        {
            string playerHtml = await FetchExternalAsync(cu, referer);
            if (string.IsNullOrEmpty(playerHtml)) continue;

            src = Lon69To.SourceFromPlayer(playerHtml);
            if (!string.IsNullOrEmpty(src)) return (src, Lon69To.IsHls(src));
        }
        return (null, false);
    }

    // host player đổi luân phiên sever1..sever4; thử origin gốc trước rồi tới biến thể
    static IEnumerable<string> Candidates(string playerUrl)
    {
        yield return playerUrl;

        var m = Regex.Match(playerUrl, @"^(https?://sever)(\d)(\.playphe\d+x\.space)(.*)$", RegexOptions.IgnoreCase);
        if (!m.Success) yield break;

        string p = m.Groups[1].Value, d = m.Groups[3].Value, rest = m.Groups[4].Value;
        foreach (string n in new[] { "4", "2", "3", "1" })
        {
            string u = p + n + d + rest;
            if (!string.Equals(u, playerUrl, StringComparison.OrdinalIgnoreCase))
                yield return u;
        }
    }

    async Task<string> GetPageAsync(string url, string referer = null)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // Site dùng bot-detection theo header: thiếu sec-ch-ua / sec-fetch-* thì trả bản "lite".
        var headers = HeadersModel.Init(
            ("User-Agent", Lon69To.ChromeUA),
            ("Referer", string.IsNullOrEmpty(referer) ? Lon69To.SiteHost + "/" : referer),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"),
            ("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8"),
            ("sec-ch-ua", "\"Chromium\";v=\"120\", \"Android WebView\";v=\"120\", \"Not?A_Brand\";v=\"24\""),
            ("sec-ch-ua-mobile", "?1"),
            ("sec-ch-ua-platform", "\"Android\""),
            ("Sec-Fetch-Dest", "document"),
            ("Sec-Fetch-Mode", "navigate"),
            ("Sec-Fetch-Site", "same-origin"),
            ("Sec-Fetch-User", "?1"),
            ("Upgrade-Insecure-Requests", "1"));

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                string html = await Http.Get(url, timeoutSeconds: 20, headers: headers, httpversion: init.httpversion);
                if (!string.IsNullOrEmpty(html) && html.Length > 2000 && !html.Contains("Just a moment"))
                    return html;
            }
            catch { }
            try
            {
                string html = await Http.Get(url, timeoutSeconds: 20, headers: headers, proxy: proxy, httpversion: init.httpversion);
                if (!string.IsNullOrEmpty(html) && html.Length > 2000 && !html.Contains("Just a moment"))
                    return html;
            }
            catch { }
        }
        return null;
    }

    async Task<string> FetchExternalAsync(string url, string referer)
    {
        if (string.IsNullOrEmpty(url)) return null;

        string rf = string.IsNullOrEmpty(referer) ? "https://lon69.ink/" : referer;

        // UA mobile và desktop có thể nhận 2 biến thể player khác nhau (một bản giấu SRC thật)
        foreach (string ua in new[] { Lon69To.ChromeUA, Lon69To.DesktopUA })
        {
            var headers = HeadersModel.Init(
                ("User-Agent", ua),
                ("Referer", rf),
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));

            try
            {
                string html = await Http.Get(url, timeoutSeconds: 15, headers: headers, httpversion: init.httpversion);
                if (!string.IsNullOrEmpty(html) && html.Length > 500 && Lon69To.SourceFromPlayer(html) != null)
                    return html;
                if (!string.IsNullOrEmpty(html) && html.Length > 500)
                {
                    string viaProxy = null;
                    try
                    {
                        viaProxy = await Http.Get(url, timeoutSeconds: 15, proxy: proxy, httpversion: init.httpversion, headers: headers);
                    }
                    catch { }
                    if (!string.IsNullOrEmpty(viaProxy) && Lon69To.SourceFromPlayer(viaProxy) != null)
                        return viaProxy;
                    if (!string.IsNullOrEmpty(html)) return html;
                }
            }
            catch { }
        }
        return null;
    }
}
