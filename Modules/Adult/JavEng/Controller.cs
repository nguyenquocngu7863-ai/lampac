using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.PlaywrightCore;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace JavEng;

public class JavEngController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();
    static readonly HttpClient mediaClient = new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public JavEngController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javeng")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        async Task<CacheResult<List<PlaylistItem>>> GetPageAsync(string search, string c, int page, bool allowRefresh)
        {
            return await InvokeCacheResult(ipkey($"javeng:{search}:{c}:{page}"), 10, jsonContext.ListPlaylistItem, async e =>
            {
                List<PlaylistItem> playlists = null;

                await httpHydra.GetSpan(JavEngTo.Uri(init.host, search, c, page), span =>
                {
                    playlists = JavEngTo.Playlist("javeng/vidosik", span.ToString());
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", JavEngTo.ChromeUA),
                    ("Referer", "https://javeng.tv/")
                ));

                if (playlists == null || playlists.Count == 0)
                    return e.Fail("playlists", refresh_proxy: allowRefresh && string.IsNullOrEmpty(search));

                return e.Success(playlists);
            });
        }

        var cache = await GetPageAsync(search, c, pg, true);

        // lam nong trang ke de Lampa flip trang khong bi treo (infinity load)
        if (cache.IsSuccess)
        {
            string nsearch = search, nc = c;
            int npg = pg + 1;
            _ = Task.Run(async () =>
            {
                try { await GetPageAsync(nsearch, nc, npg, false); }
                catch { }
            });
        }

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, JavEngTo.Menu(host));
    }

    async Task<string> GetDetailHtmlAsync(string uri)
    {
        string memKey = ipkey($"javeng:html:{uri}");
        if (hybridCache.TryGetValue(memKey, out string cachedHtml) && !string.IsNullOrEmpty(cachedHtml))
            return cachedHtml;

        string pageUrl = uri.StartsWith("/") ? JavEngTo.SiteHost + uri : uri;

        string pageHtml = null;
        await httpHydra.GetSpan(pageUrl, span =>
        {
            pageHtml = span.ToString();
        }, addheaders: HeadersModel.Init(
            ("User-Agent", JavEngTo.ChromeUA),
            ("Referer", JavEngTo.SiteHost + "/")
        ));

        if (string.IsNullOrEmpty(pageHtml))
            return null;

        hybridCache.Set(memKey, pageHtml, cacheTime(10));
        return pageHtml;
    }

    // Popup server: chi doc markup, KHONG goi player API o day.
    // Player API + Playwright resolve chi chay sau khi nguoi dung chon server.
    async Task<Dictionary<string, string>> ResolveServersAsync(string uri)
    {
        string pageHtml = await GetDetailHtmlAsync(uri);
        if (string.IsNullOrEmpty(pageHtml))
            return null;

        var options = JavEngTo.PlayerOptions(pageHtml);
        if (options.Count == 0)
            return null;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var opt in options)
        {
            string key = string.IsNullOrEmpty(opt.Label) ? "Server " + opt.Nume : opt.Label;
            string unique = key;
            int n = 2;
            while (links.ContainsKey(unique))
                unique = key + " " + (n++);
            links.TryAdd(unique, $"{host}/javeng/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(opt.Nume)}");
        }

        return links;
    }

    async Task<JavEngTo.PlayerOption> FindOptionAsync(string pageHtml, string q)
    {
        var options = JavEngTo.PlayerOptions(pageHtml);
        if (options.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(q))
        {
            foreach (var opt in options)
            {
                if (string.Equals(opt.Nume, q, StringComparison.OrdinalIgnoreCase))
                    return opt;
            }
        }

        return options[0];
    }

    // Cloudflare tra 522 cho httpHydra tren wp-json, nhan API phai chay trong
    // context trinh duyet that (da co cookie/JS cua Cloudflare).
    async Task<string> FetchPlayerEmbedAsync(string pageUrl, JavEngTo.PlayerOption opt)
    {
        // embed URL on dinh, cache de lan phat sau khong phai goi player API lai
        string memKey = ipkey($"javeng:embed:{opt.Post}:{opt.Type}:{opt.Nume}");
        if (hybridCache.TryGetValue(memKey, out string embedCached) && !string.IsNullOrEmpty(embedCached))
            return embedCached;

        string api = JavEngTo.PlayerApiUrl(opt.Post, opt.Type, opt.Nume);

        var browser = await GetMobileBrowserAsync();
        if (browser == null)
            return null;

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = JavEngTo.ChromeUA,
            IsMobile = true,
            HasTouch = true,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["Referer"] = pageUrl }
        });

        var page = await context.NewPageAsync();
        if (page == null)
            return null;

        try
        {
            await page.GotoAsync(pageUrl, new PageGotoOptions
            {
                Timeout = 20000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });
        }
        catch { }

        try
        {
            string body = await page.EvaluateAsync<string>(@"(url) => {
                try {
                    return fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' } })
                        .then(function (r) { return r.text(); })
                        .catch(function () { return ''; });
                } catch (e) { return ''; }
            }", api);

            string embed = JavEngTo.PlayerEmbed(body);
            if (!string.IsNullOrEmpty(embed))
                hybridCache.Set(memKey, embed, cacheTime(30));
            return embed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavEng: player api browser error {ex.Message}");
            return null;
        }
    }

    async Task<List<string>> ResolveMediaAsync(string embedUrl, string referer)
    {
        string embedHtml = null;
        try
        {
            await httpHydra.GetSpan(embedUrl, span =>
            {
                embedHtml = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", JavEngTo.ChromeUA),
                ("Referer", referer)
            ));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavEng: embed fetch error {ex.Message}");
        }

        var media = JavEngTo.StaticMedia(embedHtml);
        if (media.Count == 0 && !string.IsNullOrEmpty(embedUrl) && init.priorityBrowser != "http")
            media = await GetEmbedMediaWithPlaywrightAsync(embedUrl, referer);
        if (media.Count == 0 && !string.IsNullOrEmpty(embedHtml))
        {
            foreach (string u in JavEngTo.JwplayerFiles(embedHtml))
            {
                if (!media.Contains(u))
                    media.Add(u);
            }
        }

        if (media.Count > 1)
        {
            media = media.OrderByDescending(JavEngTo.IsHls).ToList();
        }

        return media;
    }

    const string JwSourcesJs = @"() => {
        try {
            var out = [];
            var vids = document.querySelectorAll('video source[src], video[src]');
            for (var i = 0; i < vids.length; i++) {
                var s = vids[i].getAttribute('src') || vids[i].src;
                if (s) out.push(s);
            }
            try {
                if (typeof jwplayer !== 'undefined') {
                    var p = null;
                    try { p = jwplayer('mediaplayer'); } catch (e) {}
                    if (!p || !p.getPlaylist) { try { p = jwplayer(); } catch (e) {} }
                    if (!p || !p.getPlaylist) { try { p = jwplayer('player'); } catch (e) {} }
                    if (p && p.getPlaylist) {
                        var pl = p.getPlaylist();
                        if (pl && pl[0] && pl[0].sources) {
                            for (var j = 0; j < pl[0].sources.length; j++) {
                                if (pl[0].sources[j].file) out.push(pl[0].sources[j].file);
                            }
                        }
                    }
                }
            } catch (e) {}
            return out;
        } catch (e) { return []; }
    }";

    static IBrowser _mobileBrowser;

    static async Task<IBrowser> GetMobileBrowserAsync()
    {
        if (_mobileBrowser != null && _mobileBrowser.IsConnected)
            return _mobileBrowser;

        var chromium = CoreInit.conf.chromium;
        if (chromium == null || !chromium.enable)
            return null;

        var args = new List<string>();
        if (chromium.Args != null)
        {
            foreach (string a in chromium.Args)
            {
                if (!string.IsNullOrEmpty(a))
                    args.Add(a);
            }
        }
        foreach (string extra in new[] { "--no-sandbox", "--disable-dev-shm-usage", "--disable-blink-features=AutomationControlled" })
        {
            if (!args.Contains(extra))
                args.Add(extra);
        }

        _mobileBrowser = await Chromium.playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = chromium.executablePath,
            Headless = chromium.Headless,
            Args = args
        }).ConfigureAwait(false);

        return _mobileBrowser;
    }

    async Task<List<string>> GetEmbedMediaWithPlaywrightAsync(string embedUrl, string referer)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sync = new object();
        void Add(string url, bool playerFile = false)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            url = url.Trim();
            if (url.StartsWith("/") && System.Uri.TryCreate(embedUrl, System.UriKind.Absolute, out var baseUri))
                url = new System.Uri(baseUri, url).ToString();
            url = JavEngTo.NormalizeMediaUrl(url);
            if (string.IsNullOrEmpty(url))
                return;
            // playkrx playlist URLs carry no file extension (/m3u8/<quality>/.../<token>)
            bool hlsPath = url.Contains("/m3u8/");
            if (!playerFile && !hlsPath && !JavEngTo.IsMedia(url))
                return;
            lock (sync)
            {
                if (seen.Add(url))
                    found.Add(url);
            }
        }

        void Collect(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 2000000)
                return;
            foreach (Match m in Regex.Matches(text, "(https?://[^\\s\"'<>]+?\\.m3u8[^\\s\"'<>]*)", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, "(https?://[^\\s\"'<>]+?\\.mp4[^\\s\"'<>]*)", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, "\"file\"\\s*:\\s*\"(https?:[^\"\\\\]+)\"", RegexOptions.IgnoreCase))
                Add(m.Groups[1].Value.Replace("\\/", "/"), playerFile: true);
        }

        int FoundCount()
        {
            lock (sync)
                return found.Count;
        }

        string embedHost = "";
        try { embedHost = new System.Uri(embedUrl).Host; } catch { }

        try
        {
            var browser = await GetMobileBrowserAsync();
            if (browser == null)
                return found;

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = JavEngTo.ChromeUA,
                IsMobile = true,
                HasTouch = true,
                ExtraHTTPHeaders = new Dictionary<string, string> { ["Referer"] = referer }
            });

            await context.AddInitScriptAsync("try { window.open = function () { return { focus: function () {}, close: function () {}, closed: true }; }; } catch (e) {}");

            var page = await context.NewPageAsync();
            if (page == null)
                return found;

                page.Request += (_, request) =>
                {
                    try
                    {
                        string url = request.Url;
                        if (!string.IsNullOrEmpty(url) && (url.Contains(".m3u8") || url.Contains("/m3u8/") || url.Contains(".mp4")))
                            Add(url);
                    }
                    catch { }
                };

                page.Response += async (_, response) =>
                {
                    try
                    {
                        string url = response?.Url;
                        if (string.IsNullOrEmpty(url))
                            return;
                        string l = url.ToLowerInvariant();
                        string ct = "";
                        try
                        {
                            if (response.Headers != null && response.Headers.TryGetValue("content-type", out var v))
                                ct = (v ?? "").ToLowerInvariant();
                        }
                        catch { }
                        bool manifest = ct.Contains("mpegurl") || ct.Contains("mp2t") || l.Contains(".m3u8") || l.Contains("/m3u8/");
                        bool interesting = manifest || l.Contains(".mp4") || l.Contains("tp1rd") || l.Contains("/api/") || l.Contains("playlist") || l.Contains("master") || l.Contains("config") || l.Contains("jwplayer") || l.Contains("source");
                        if (!interesting)
                            return;
                        if (l.EndsWith(".ts") || l.EndsWith(".m4s") || l.Contains(".ts?") || l.Contains(".m4s?") || l.Contains(".ts&"))
                            return;
                        if (manifest)
                            Add(url, playerFile: true);
                        string text = await response.TextAsync();
                        Collect(text);
                    }
                    catch { }
                };

                try
                {
                    await page.GotoAsync(embedUrl, new PageGotoOptions
                    {
                        Timeout = 15000,
                        WaitUntil = WaitUntilState.DOMContentLoaded
                    });
                }
                catch { }

                await Task.Delay(2500);

                string[] selectors = new[] { "#overlay", "#playback", ".jw-display", ".jw-icon-display", "#play-btn", "video" };
                int clicks = 0;
                var deadline = DateTime.UtcNow.AddSeconds(14);
                while (DateTime.UtcNow < deadline)
                {
                    if (FoundCount() > 0)
                        break;

                    var frames = new List<IFrame>();
                    try
                    {
                        foreach (var f in page.Frames)
                        {
                            try
                            {
                                string fu = f.Url ?? "";
                                if (string.IsNullOrEmpty(embedHost) || fu.Contains(embedHost))
                                    frames.Add(f);
                            }
                            catch { }
                        }
                    }
                    catch { }

                    if (clicks < 3)
                    {
                        foreach (string sel in selectors)
                        {
                            bool doneClick = false;
                            try
                            {
                                var el = await page.QuerySelectorAsync(sel);
                                if (el != null)
                                {
                                    await el.ClickAsync(new ElementHandleClickOptions { Timeout = 3000 });
                                    doneClick = true;
                                }
                            }
                            catch { }
                            if (!doneClick)
                            {
                                foreach (var f in frames)
                                {
                                    try
                                    {
                                        var el = await f.QuerySelectorAsync(sel);
                                        if (el != null)
                                        {
                                            await el.ClickAsync();
                                            doneClick = true;
                                            break;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            if (doneClick)
                            {
                                clicks++;
                                break;
                            }
                        }
                    }

                    try
                    {
                        var sources = await page.EvaluateAsync<object>(JwSourcesJs);
                        if (sources != null)
                            Collect(System.Text.Json.JsonSerializer.Serialize(sources));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"JavEng: jwplayer evaluate error {ex.Message}");
                    }

                    foreach (var f in frames)
                    {
                        try
                        {
                            var sources = await f.EvaluateAsync<object>(JwSourcesJs);
                            if (sources != null)
                                Collect(System.Text.Json.JsonSerializer.Serialize(sources));
                        }
                        catch { }
                    }

                    if (FoundCount() > 0)
                        break;

                    await Task.Delay(2000);
                }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavEng: playwright error {ex.Message}");
        }

        lock (sync)
        {
            found.Sort((a, b) =>
            {
                bool ah = JavEngTo.IsHls(a);
                bool bh = JavEngTo.IsHls(b);
                if (ah == bh)
                    return 0;
                return ah ? -1 : 1;
            });
            return new List<string>(found);
        }
    }

    static string MediaReferer(string url)
    {
        try { return new System.Uri(url).GetLeftPart(UriPartial.Authority) + "/"; }
        catch { return JavEngTo.SiteHost + "/"; }
    }

    async Task<(byte[] data, string contentType, bool ok)> FetchMediaAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", JavEngTo.ChromeUA);
            req.Headers.TryAddWithoutValidation("Referer", MediaReferer(url));
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            using var resp = await mediaClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            var data = await resp.Content.ReadAsByteArrayAsync();
            return (data, resp.Content?.Headers?.ContentType?.ToString() ?? "", resp.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavEng: media fetch error {ex.Message}");
            return (null, "", false);
        }
    }

    // Resolve media URL cua server da chon, cache ngan de segment khong resolve lai tung lan.
    async Task<string> ResolveStreamUrlAsync(string uri, string q)
    {
        string memKey = ipkey($"javeng:stream:{uri}:{q}");
        if (hybridCache.TryGetValue(memKey, out string cachedLink) && !string.IsNullOrEmpty(cachedLink))
            return cachedLink;

        string pageUrl = uri.StartsWith("/") ? JavEngTo.SiteHost + uri : uri;
        string pageHtml = await GetDetailHtmlAsync(uri);
        if (string.IsNullOrEmpty(pageHtml))
            return null;

        var opt = await FindOptionAsync(pageHtml, q);
        if (opt == null)
            return null;

        string embed = await FetchPlayerEmbedAsync(pageUrl, opt);
        if (string.IsNullOrEmpty(embed))
            return null;

        var media = await ResolveMediaAsync(embed, pageUrl);
        string link = media != null && media.Count > 0 ? media[0] : embed;
        if (string.IsNullOrEmpty(link))
            return null;

        hybridCache.Set(memKey, link, cacheTime(10));
        return link;
    }

    // Doc danh sach segment cua playlist HLS upstream.
    async Task<List<string>> FetchPlaylistSegmentsAsync(string playlistUrl)
    {
        var segments = new List<string>();
        var (data, _, ok) = await FetchMediaAsync(playlistUrl);
        if (!ok || data == null || data.Length == 0)
            return segments;

        string text = System.Text.Encoding.UTF8.GetString(data);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            string abs = JavEngTo.NormalizeMediaUrl(line);
            if (!string.IsNullOrEmpty(abs))
                segments.Add(abs);
        }

        return segments;
    }

    [HttpGet]
    [Route("javeng/hls.m3u8")]
    async public Task<ActionResult> Hls(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(uri))
            return OnError("stream_links");

        // Khong nhung URL upstream (co token het han) vao URL client.
        // Client chi biet (uri, q, index); server tu resolve lai khi can.
        string streamUrl = await ResolveStreamUrlAsync(uri, q);
        if (string.IsNullOrEmpty(streamUrl))
            return OnError("stream_links", refresh_proxy: true);

        var segments = await FetchPlaylistSegmentsAsync(streamUrl);

        // Token upstream het han: bo cache va resolve lai mot lan.
        if (segments.Count == 0)
        {
            string staleKey = ipkey($"javeng:stream:{uri}:{q}");
            hybridCache.Set(staleKey, "", TimeSpan.FromSeconds(-1));
            streamUrl = await ResolveStreamUrlAsync(uri, q);
            if (!string.IsNullOrEmpty(streamUrl))
                segments = await FetchPlaylistSegmentsAsync(streamUrl);
        }

        if (segments.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var sb = new System.Text.StringBuilder();
        sb.Append("#EXTM3U\n");
        sb.Append("#EXT-X-VERSION:3\n");
        sb.Append("#EXT-X-TARGETDURATION:6\n");
        sb.Append("#EXT-X-PLAYLIST-TYPE:VOD\n");
        for (int i = 0; i < segments.Count; i++)
        {
            sb.Append("#EXTINF:6.0,\n");
            sb.Append(host).Append("/javeng/hls.ts?uri=").Append(HttpUtility.UrlEncode(uri))
              .Append("&q=").Append(HttpUtility.UrlEncode(q ?? "1"))
              .Append("&n=").Append(i).Append('\n');
        }
        sb.Append("#EXT-X-ENDLIST\n");

        return Content(sb.ToString(), "application/vnd.apple.mpegurl");
    }

    [HttpGet]
    [Route("javeng/hls.ts")]
    async public Task<ActionResult> HlsSegment(string uri, string q, int n)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(uri))
            return OnError("stream_links");

        string streamUrl = await ResolveStreamUrlAsync(uri, q);
        if (string.IsNullOrEmpty(streamUrl))
            return OnError("stream_links", refresh_proxy: true);

        var segments = await FetchPlaylistSegmentsAsync(streamUrl);

        if (segments.Count == 0)
        {
            string staleKey = ipkey($"javeng:stream:{uri}:{q}");
            hybridCache.Set(staleKey, "", TimeSpan.FromSeconds(-1));
            streamUrl = await ResolveStreamUrlAsync(uri, q);
            if (!string.IsNullOrEmpty(streamUrl))
                segments = await FetchPlaylistSegmentsAsync(streamUrl);
        }

        if (segments.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        if (n < 0 || n >= segments.Count)
            return OnError("stream_links");

        // Forward Range de player seek duoc (ExoPlayer gui Range khi tua).
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, segments[n]);
            req.Headers.TryAddWithoutValidation("User-Agent", JavEngTo.ChromeUA);
            req.Headers.TryAddWithoutValidation("Referer", MediaReferer(segments[n]));
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            if (Request.Headers.TryGetValue("Range", out var range) && range.Count > 0)
                req.Headers.TryAddWithoutValidation("Range", range.ToString());

            using var resp = await mediaClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
            if (!resp.IsSuccessStatusCode)
                return OnError("stream_links", refresh_proxy: true);

            int code = (int)resp.StatusCode;
            string ct = resp.Content?.Headers?.ContentType?.MediaType ?? "";
            if (ct.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0 || ct.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                return OnError("stream_links", refresh_proxy: true);

            Response.StatusCode = code;
            Response.ContentType = "video/mp2t";
            if (resp.Content.Headers.ContentLength.HasValue)
                Response.ContentLength = resp.Content.Headers.ContentLength.Value;
            if (resp.Content.Headers.ContentRange != null)
                Response.Headers["Content-Range"] = resp.Content.Headers.ContentRange.ToString();
            Response.Headers["Accept-Ranges"] = "bytes";

            if (Microsoft.AspNetCore.Http.HttpMethods.IsHead(Request.Method))
                return new EmptyResult();

            await using var input = await resp.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
            await input.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"JavEng: segment error {ex.Message}");
            return OnError("stream_links");
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javeng/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var servers = await ResolveServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(servers);
    }

    [HttpGet]
    [Route("javeng/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string pageUrl = uri.StartsWith("/") ? JavEngTo.SiteHost + uri : uri;
        string pageHtml = await GetDetailHtmlAsync(uri);
        if (string.IsNullOrEmpty(pageHtml))
            return OnError("stream_links", refresh_proxy: true);

        var opt = await FindOptionAsync(pageHtml, q);
        if (opt == null)
            return OnError("stream_links", refresh_proxy: true);

        string embed = await FetchPlayerEmbedAsync(pageUrl, opt);
        if (string.IsNullOrEmpty(embed))
            return OnError("stream_links", refresh_proxy: true);

        var media = await ResolveMediaAsync(embed, pageUrl);
        if (media != null && media.Count > 0)
        {
            string link = media[0];
            if (JavEngTo.IsHls(link))
                return Redirect($"{host}/javeng/hls.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(q ?? "1")}");

            var streamHeaders = httpHeaders(init, HeadersModel.Init(
                ("User-Agent", JavEngTo.ChromeUA),
                ("Referer", MediaReferer(link))
            ));
            return Redirect(HostStreamProxy(link, streamHeaders));
        }

        var proxyHeaders = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JavEngTo.ChromeUA),
            ("Referer", "https://javeng.tv/"),
            ("Origin", "https://javeng.tv")
        ));
        return Redirect(HostStreamProxy(embed, proxyHeaders));
    }
}
