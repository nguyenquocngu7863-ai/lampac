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
using System.Threading.Tasks;
using System.Web;

namespace HeoVl;

public class HeoVlController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public HeoVlController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("heovl")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        // `?rel=` = danh sach "Video liên quan" cua popup long-press.
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"heovl:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(HeoVlTo.Uri(init.host, search, c, pg), span =>
            {
                playlists = HeoVlTo.Playlist("heovl/vidosik", span.ToString());
            }, addheaders: HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", "https://heovl.im/")
            ));

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, HeoVlTo.Menu(host));
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"heovl:view:{uri}";

        if (rch?.enable != true)
        {
            semaphore ??= new SemaphorManager(semaphoreKey, System.TimeSpan.FromSeconds(30));
            bool _acquired = await semaphore.WaitAsync();
            if (!_acquired)
                return (null, false);
        }

        try
        {
            string memKey = ipkey(semaphoreKey);
            if (!hybridCache.TryGetValue(memKey, out (Dictionary<string, string> links, bool userch) cache))
            {
                var links = new Dictionary<string, string>();

                // Get embed URL from heovl page
                string pageUrl = uri;
                if (uri.StartsWith("/"))
                    pageUrl = "https://heovl.im" + uri;
                else if (!uri.StartsWith("http"))
                    pageUrl = "https://heovl.im/videos/" + uri.Trim('/');

                string pageHtml = null;
                await httpHydra.GetSpan(pageUrl, span =>
                {
                    pageHtml = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                    ("Referer", "https://heovl.im/")
                ));

                if (string.IsNullOrEmpty(pageHtml))
                    return (null, false);

                var embeds = HeoVlTo.GetEmbeds(pageHtml);
                if (embeds.Count == 0)
                    return (null, false);

                foreach (var (label, embed) in embeds)
                {
                    // `vid` la id cua chinh video nay. Bat buoc phai loc theo no:
                    // trang player nap quang cao chay song song va cac request
                    // m3u8 cua quang cao cung di qua trinh bat network, nen
                    // khong loc thi app nhan nham phim voi quang cao.
                    var (_, vid) = HeoVlTo.ParseEmbed(embed);

                    var streamLinks = await GetStreamFromPlaywrightAsync(embed, pageUrl, vid);
                    if (streamLinks != null)
                    {
                        foreach (var kv in streamLinks)
                        {
                            string key = label;
                            if (streamLinks.Count > 1 || links.ContainsKey(key))
                                key = label + " " + kv.Key;
                            if (!links.ContainsValue(kv.Value))
                                links.TryAdd(key, kv.Value);
                        }
                        if (links.Count > 0)
                            break;
                    }
                }

                if (links.Count == 0)
                    return (null, false);

                cache.links = links;
                proxyManager?.Success();
                cache.userch = rch?.enable == true;
                hybridCache.Set(memKey, cache, cacheTime(20));
            }

            return (cache.links, cache.userch);
        }
        finally
        {
            semaphore?.Release();
        }
    }

    async Task<Dictionary<string, string>> GetStreamFromPlaywrightAsync(string embedUrl, string referer, string vid)
    {
        try
        {
            using (var browser = new Shared.PlaywrightCore.PlaywrightBrowser())
            {
                var page = await browser.NewPageAsync(init.plugin, new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
                    ["Referer"] = referer
                });

                if (page == null)
                    return null;

                var links = new Dictionary<string, string>();

                // PHẢI xét đuôi đường dẫn, không xét `Contains(".m3u8")`. JW
                // Player bắn ping analytics dạng
                // `prd.jwpltx.com/.../ping.gif?...&mu=<...master.m3u8...>`,
                // chuỗi `.m3u8` nằm trong query nen Contains nhận nhầm, app
                // nhận 1 pixel 204 No Content làm link chết.
                bool IsMedia(string url)
                {
                    try
                    {
                        var path = new Uri(url).AbsolutePath;
                        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                }

                // Chi nhan m3u8 cua dung video nay. Khong co `vid` (URL embed
                // doi hinh dang) thi bo qua chuc nhan, vi luc do khong phan biet
                // duoc video nao voi quang cao nao.
                bool Accept(string url)
                {
                    if (!IsMedia(url))
                        return false;

                    if (!string.IsNullOrEmpty(vid) && !url.Contains(vid))
                        return false;

                    if (links.ContainsValue(url))
                        return false;

                    // So chay tu 1 cho het, khong bo qua link dau tien.
                    links.TryAdd((url.Contains(".m3u8") ? "HLS" : "MP4") + " " + (links.Count + 1), url);
                    return true;
                }

                // Intercept network requests to find m3u8/mp4
                page.Request += (_, request) =>
                {
                    var url = request.Url;
                    Accept(url);
                };

                // Also intercept API config response
                string configJson = null;
                page.Response += (_, response) =>
                {
                    var url = response.Url;
                    if (url.Contains("/config"))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                configJson = await response.TextAsync();
                            }
                            catch { }
                        });
                    }
                };

                await page.GotoAsync(embedUrl, new PageGotoOptions
                {
                    Timeout = 15000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });

                // Wait for JW Player to load
                await Task.Delay(3000);

                // Try to click play button
                try
                {
                    var playButton = await page.QuerySelectorAsync("#play-btn");
                    if (playButton != null)
                    {
                        await playButton.ClickAsync();
                        await Task.Delay(5000);
                    }
                }
                catch { }

                // Try to get sources from JW Player API
                try
                {
                    var sources = await page.EvaluateAsync<object>(@"() => {
                        try {
                            var p = jwplayer('player');
                            if (p && p.getPlaylist) {
                                var pl = p.getPlaylist();
                                if (pl && pl[0] && pl[0].sources) {
                                    return pl[0].sources;
                                }
                            }
                        } catch(e) {}
                        return null;
                    }");

                    if (sources != null)
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(sources);
                        var matches = Regex.Matches(json, @"""file""\s*:\s*""(https?[^""]+)""");
                        foreach (Match m in matches)
                        {
                            string file = HeoVlTo.NormalizeStreamUrl(m.Groups[1].Value);
                            Accept(file);
                        }
                    }
                }
                catch { }

                // Also check config JSON
                if (links.Count == 0 && !string.IsNullOrEmpty(configJson))
                {
                    var parsed = HeoVlTo.StreamLinksFromConfig(configJson);
                    foreach (var kv in parsed)
                    {
                        if (!string.IsNullOrEmpty(vid) && !kv.Value.Contains(vid))
                            continue;

                        Accept(kv.Value);
                    }
                }

                return links.Count > 0 ? links : null;
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"HeoVl: playwright error {ex.Message}");
            return null;
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("heovl/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var (links, userch) = await ResolveLinksAsync(uri);

        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        if (userch)
            return OnResult(links);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/heovl/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("heovl/video")]
    [Route("heovl/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://heovl.im/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }

    [HttpGet]
    [Route("heovl/strem")]
    async public Task<ActionResult> Strem(string link, string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (!string.IsNullOrEmpty(uri) && !string.IsNullOrEmpty(q))
        {
            var (links, _) = await ResolveLinksAsync(uri);
            if (links == null || !links.TryGetValue(q, out link) || string.IsNullOrEmpty(link))
                return OnError("stream_links", refresh_proxy: true);
        }

        if (string.IsNullOrEmpty(link))
            return OnError("link");

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://heovl.im/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }

    #region popup long-press (skill lampac-popup)

    const string PopupUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    // Cache RAM: 1 fetch detail cho ca chip lan "lien quan".
    static readonly Dictionary<string,
        (DateTime at, List<(string kind, string slug, string name)> chips)> _infoCache = new();

    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    async Task<string> FetchHtmlAsync(string url)
    {
        string html = null;
        await httpHydra.GetSpan(url, span =>
        {
            html = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", PopupUA),
            ("Referer", "https://heovl.im/")
        ));
        return html;
    }

    [HttpGet]
    [Route("heovl/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var pageUrl = HeoVlTo.PageUrl(uri);

        // uri rac / host la -> popup rong, KHONG 500 (skill lampac-popup §2).
        if (pageUrl == null)
            return Json(new { items = new object[0], groups = new object[0] });

        var chips = await ChipsAsync(pageUrl);

        // Warm nen: bam "Video lien quan" la co cache ngay.
        _ = Task.Run(async () =>
        {
            try { await WarmRelated(pageUrl); } catch { }
        });

        return InfoResult(chips, pageUrl);
    }

    async Task<List<(string kind, string slug, string name)>> ChipsAsync(string pageUrl)
    {
        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(pageUrl, out var hit)
                && DateTime.UtcNow - hit.at < InfoTtl && hit.chips != null)
                return hit.chips;
        }

        string html = await FetchHtmlAsync(pageUrl);
        var chips = HeoVlTo.Chips(html ?? "");

        lock (_infoCache)
            _infoCache[pageUrl] = (DateTime.UtcNow, chips);

        return chips;
    }

    ActionResult InfoResult(
        List<(string kind, string slug, string name)> chips, string pageUrl)
    {
        var groups = new List<object>();

        foreach (var (kind, title) in new[] { ("cat", "Thể loại"), ("tag", "Thẻ") })
        {
            var items = chips?.Where(x => x.kind == kind)
                .Select(x => new
                {
                    title = x.name,
                    url = $"{host}/heovl?c="
                        + HttpUtility.UrlEncode(kind == "tag" ? "tag/" + x.slug : "categories/" + x.slug)
                }).ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title, items });
        }

        // "Xem them" cua site = /ajax/suggestions/<slug>, warm o tren.
        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = $"{host}/heovl?rel={HttpUtility.UrlEncode(pageUrl)}"
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    static string SuggestionUrl(string pageUrl)
    {
        string slug = pageUrl.Substring(pageUrl.LastIndexOf('/') + 1);
        return HeoVlTo.SiteHost + "/ajax/suggestions/" + slug;
    }

    // Chi 1 trang -> total_pages = 1 (0 = "con trang vo han" voi SISI,
    // app se nap lai dung list cu mai).
    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var pageUrl = HeoVlTo.PageUrl(rel);
        if (pageUrl == null)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"heovl:rel:{pageUrl}"), 180, async e =>
        {
            string json = await FetchHtmlAsync(SuggestionUrl(pageUrl));
            var items = HeoVlTo.Suggestions(json, "heovl/vidosik");
            items.RemoveAll(x => SamePage(x, pageUrl));
            return e.Success((items, 1));
        });

        // PlaylistResult coi list rong = loi (503) -> tra JSON rong thang.
        if (!cache.IsSuccess || cache.Value.playlists == null || cache.Value.playlists.Count == 0)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            HeoVlTo.Menu(host), total_pages: 1);
    }

    async Task WarmRelated(string pageUrl)
    {
        await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"heovl:rel:{pageUrl}"), 180, async e =>
        {
            string json = await FetchHtmlAsync(SuggestionUrl(pageUrl));
            var items = HeoVlTo.Suggestions(json, "heovl/vidosik");
            items.RemoveAll(x => SamePage(x, pageUrl));
            return e.Success((items, 1));
        });
    }

    static bool SamePage(PlaylistItem x, string pageUrl)
    {
        string href = x?.bookmark?.href;
        if (string.IsNullOrEmpty(href))
            return false;

        string u = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? href
            : HeoVlTo.SiteHost + "/" + href.Trim('/');

        return string.Equals(u.TrimEnd('/'), pageUrl.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
