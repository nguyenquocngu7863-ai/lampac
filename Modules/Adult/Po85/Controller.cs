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
using System.Threading.Tasks;
using System.Web;

namespace Po85;

public class Po85Controller : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public Po85Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("po85")]
    async public Task<ActionResult> Index(string search, string sort, string c, string t, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        rhubFallback:
        var cache = await InvokeCacheResult(ipkey($"po85:{search}:{sort}:{c}:{t}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(Po85To.Uri(init.host, search, sort, c, t, pg), span =>
            {
                playlists = Po85To.Playlist("po85/vidosik", span);
            });

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache,
            Po85To.Menu(host, search, sort, c, t)
        );
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"po85:view:{uri}";

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
                if (init.httpversion == 1)
                    httpHydra.RegisterHttp(httpClient);

                string url = Po85To.StreamLinksUri(uri);

                // 85po moi: player nam o /embed/{id}/ (trang /video/ chi co template).
                // Bookmark so -> thang embed; URL day du -> doi qua EmbedPage.
                string embedUrl;
                if (!string.IsNullOrEmpty(url) && System.Text.RegularExpressions.Regex.IsMatch(url, @"^[0-9]+$"))
                    embedUrl = $"https://www.85po.com/embed/{url}/";
                else
                {
                    if (url == null)
                        return (null, false);
                    embedUrl = Po85To.EmbedPage(url);
                }

                string embedHtml = null;
                await httpHydra.GetSpan(embedUrl, span =>
                {
                    embedHtml = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"),
                    ("Referer", "https://www.85po.com/")
                ));

                if (string.IsNullOrEmpty(embedHtml))
                    return (null, false);

                // Giu cookie phien embed: upstream doi cookie moi mo duoc link flashvars
                try
                {
                    string ckVid = System.Text.RegularExpressions.Regex.Match(embedUrl ?? "", @"(?:/video/|/embed/)([0-9]+)").Groups[1].Value;
                    if (!string.IsNullOrEmpty(ckVid))
                    {
                        var ckReq = new HttpRequestMessage(HttpMethod.Get, embedUrl);
                        ckReq.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                        ckReq.Headers.TryAddWithoutValidation("Referer", "https://www.85po.com/");
                        using (var ckResp = await httpClient.SendAsync(ckReq, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (ckResp.Headers.TryGetValues("Set-Cookie", out var ckSc))
                            {
                                string ck = string.Join("; ", ckSc.Select(v => v.Split(';')[0]));
                                if (!string.IsNullOrEmpty(ck))
                                    hybridCache.Set(ipkey($"po85:cookie:{ckVid}"), ck, cacheTime(20));
                            }
                        }
                    }
                }
                catch { }

                cache.links = Po85To.StreamLinks(embedHtml.AsSpan());
                string uhdFile = Po85To.ParseUhd(embedHtml.AsSpan());

                // video_alt_url2/3 dang trang locale (khong phai file): mo tiep
                // lay flashvars day du (1080p/4K). Chi mo khi con thieu quality.
                if (!cache.links.ContainsKey("1080p") || string.IsNullOrEmpty(uhdFile))
                {
                    foreach (string altPage in Po85To.AltPages(embedHtml))
                    {
                        try
                        {
                            string altHtml = null;
                            await httpHydra.GetSpan(altPage, span => { altHtml = span.ToString(); }, addheaders: HeadersModel.Init(
                                ("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"),
                                ("Referer", "https://www.85po.com/")
                            ));
                            if (string.IsNullOrEmpty(altHtml))
                                continue;
                            var sub = Po85To.StreamLinks(altHtml.AsSpan());
                            if (sub != null)
                            {
                                foreach (var kv in sub)
                                {
                                    if (cache.links.ContainsValue(kv.Value))
                                        continue;
                                    string key = kv.Key;
                                    int dup = 2;
                                    while (cache.links.ContainsKey(key))
                                        key = kv.Key + " " + (dup++);
                                    cache.links.TryAdd(key, kv.Value);
                                }
                            }
                            if (string.IsNullOrEmpty(uhdFile))
                                uhdFile = Po85To.ParseUhd(altHtml.AsSpan());
                            if (cache.links.ContainsKey("1080p") && !string.IsNullOrEmpty(uhdFile))
                                break;
                        }
                        catch { }
                    }
                }

                // 4K: video_alt_url3 can node resolver mo bang Chrome that
                // (link tho phien trinh duyet, server tai truc tiep bi chan)
                if (!string.IsNullOrEmpty(uhdFile))
                {
                    string signed = await Po85To.ResolveUhd(embedUrl, uhdFile);
                    if (!string.IsNullOrEmpty(signed) && !cache.links.ContainsValue(signed))
                        cache.links["4K UHD"] = signed;
                }

                if (cache.links == null || cache.links.Count == 0)
                    return (null, false);

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

    [HttpGet, Staticache(manually: true)]
    [Route("po85/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        reset:
        var (links, userch) = await ResolveLinksAsync(uri);

        if (links == null || links.Count == 0)
        {
            if (IsRhubFallback())
                goto reset;

            return OnError("stream_links", refresh_proxy: true);
        }

        if (userch)
            return OnResult(links);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/po85/strem?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("po85/video")]
    [Route("po85/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var videoHeaders = HeadersModel.Init(
            ("referer", "https://www.85po.com/"),
            ("origin", "https://www.85po.com")
        );
        string videoVid = uri ?? "";
        var videoVm = System.Text.RegularExpressions.Regex.Match(videoVid, @"(?:/v/|/video/|/embed/)([0-9]+)");
        if (videoVm.Success)
            videoVid = videoVm.Groups[1].Value;
        if (hybridCache.TryGetValue(ipkey($"po85:cookie:{videoVid}"), out string videoCk) && !string.IsNullOrEmpty(videoCk))
            videoHeaders.Add(new HeadersModel("cookie", videoCk));
        var direct = httpHeaders(init, videoHeaders);
        return Redirect(HostStreamProxy(link, direct));
    }

    [HttpGet]
    [Route("po85/strem")]
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

        var stremHeaders = HeadersModel.Init(
            ("referer", "https://www.85po.com/"),
            ("origin", "https://www.85po.com")
        );
        // Kem cookie phien embed (neu co) de mo link flashvars
        string stremVid = uri ?? "";
        var stremVm = System.Text.RegularExpressions.Regex.Match(stremVid, @"(?:/v/|/video/|/embed/)([0-9]+)");
        if (stremVm.Success)
            stremVid = stremVm.Groups[1].Value;
        if (hybridCache.TryGetValue(ipkey($"po85:cookie:{stremVid}"), out string savedCk) && !string.IsNullOrEmpty(savedCk))
            stremHeaders.Add(new HeadersModel("cookie", savedCk));

        // Code cu (xem duoc): theo redirect ra URL CDN cuoi truoc khi proxy.
        // Link get_file la route trung gian, proxy thang route se hong.
        SemaphorManager stremSem = null;
        string stremSemKey = $"po85:strem:{link}";

        if (rch?.enable != true)
        {
            stremSem ??= new SemaphorManager(stremSemKey, System.TimeSpan.FromSeconds(30));
            bool stremAcq = await stremSem.WaitAsync();
            if (!stremAcq)
                return OnError();
        }

        try
        {
            string stremMem = ipkey(stremSemKey);
            if (!hybridCache.TryGetValue(stremMem, out string location))
            {
                var headers = httpHeaders(init, HeadersModel.Init(
                    ("sec-fetch-dest", "document"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "none"),
                    ("referer", "https://www.85po.com/")
                ));

                if (rch?.enable == true)
                {
                    var res = await rch.Headers(init.cors(link, headers, requestInfo), null, headers);
                    location = res.currentUrl;
                }
                else
                {
                    location = await Http.GetLocation(init.cors(link, headers, requestInfo), timeoutSeconds: init.httptimeout, httpversion: init.httpversion, proxy: proxy, headers: headers);
                }

                if (string.IsNullOrEmpty(location) || link == location)
                {
                    proxyManager?.Success();
                    var direct = httpHeaders(init, stremHeaders);
                    hybridCache.Set(stremMem, link, cacheTime(40));
                    return Redirect(HostStreamProxy(link, direct));
                }

                proxyManager?.Success();
                hybridCache.Set(stremMem, location, cacheTime(40));
            }

            return Redirect(HostStreamProxy(location));
        }
        finally
        {
            stremSem?.Release();
        }
    }

}