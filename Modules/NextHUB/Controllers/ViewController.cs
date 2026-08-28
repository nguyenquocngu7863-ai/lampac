using HtmlAgilityPack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Shared.Attributes;
using Shared.Models.CSharpGlobals;
using Shared.Models.SISI.NextHUB;
using Shared.PlaywrightCore;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Web;

namespace NextHUB;

public class ViewController : BaseSisiController<NxtSettings>
{
    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ViewController>();
    static readonly ConcurrentDictionary<string, (System.Net.Cookie[] cookies, DateTime expiry)> _playwrightCookies = new();

    IQueryCollection evalQuery = QueryCollection.Empty;
    string evalQueryCacheKey = string.Empty;
    string kvsLastError;

    public ViewController() : base(default) { }

    [HttpGet]
    [Staticache(manually: true)]
    [Route("nexthub/vidosik")]
    async public Task<ActionResult> Index(string uri, bool related)
    {
        uri = DecryptQuery(uri);
        if (string.IsNullOrEmpty(uri))
            return OnError("uri", rcache: false);

        int separator = uri.IndexOf("_-:-_", StringComparison.Ordinal);
        if (separator <= 0 || separator + 5 >= uri.Length)
            return OnError("uri", rcache: false);

        string plugin = uri.Substring(0, separator);
        string url = uri.Substring(separator + 5);

        var _nxtInit = Root.goInit(plugin);
        if (_nxtInit == null)
            return OnError("init not found", rcache: false);

        if (await IsRequestBlocked(_nxtInit, rch: _nxtInit.rch_access != null))
            return badInitMsg;

        (evalQuery, evalQueryCacheKey) = Root.getEvalQuery(HttpContext.Request.Query, init);

        if (init.view.initUrlEval != null)
            url = CSharpEval.Execute<string>(init.view.initUrlEval, new NxtUrlRequest(init.host, plugin, url, evalQuery, related));

        if (!Root.isSafeHttpUrl(url))
            return OnError("url", rcache: false);

        SemaphorManager semaphore = null;
        string semaphoreKey = $"nexthub:InvkSemaphore:{url}{evalQueryCacheKey}";

        try
        {
            VideoModel video = null;

            if ((init.view.priorityBrowser ?? init.priorityBrowser) == "http" && init.view.viewsource &&
                (init.view.nodeFile != null || init.view.eval != null || init.view.regexMatch != null) &&
                 init.view.routeEval == null && init.cookies == null && init.view.evalJS == null)
            {
            reset:
                if (rch?.enable != true)
                {
                    semaphore ??= new SemaphorManager(semaphoreKey, TimeSpan.FromSeconds(30));
                    bool _acquired = await semaphore.WaitAsync();
                    if (!_acquired)
                        return OnError();
                }

                video = await goVideoToHttp(plugin, url);
                if (string.IsNullOrEmpty(video?.file))
                {
                    if (IsRhubFallback())
                        goto reset;

                    return OnError("file", rcache: !init.debug);
                }
            }
            else
            {
                if (rch?.enable == true)
                    return OnError("rch not supported", rcache: false);

                semaphore ??= new SemaphorManager(semaphoreKey, TimeSpan.FromSeconds(30));
                bool _acquired = await semaphore.WaitAsync();
                if (!_acquired)
                    return OnError();

                video = await goVideoToBrowser(plugin, url, init);
                if (string.IsNullOrEmpty(video?.file))
                    return OnError("file", rcache: !init.debug);
            }

            var stream_links = new StreamItem()
            {
                qualitys = new Dictionary<string, string>()
                {
                    ["auto"] = video.file
                },
                recomends = video.recomends
            };

            semaphore?.Release();

            if (related)
                return PlaylistResult(stream_links?.recomends, false, null, total_pages: 1);

            if (!string.IsNullOrEmpty(video.file) && video.file.Contains("/get_file/", StringComparison.OrdinalIgnoreCase))
            {
                string videoUrl = video.file;
                string licenseCode = null;
                int licSep = video.file.IndexOf("||||", StringComparison.Ordinal);
                if (licSep > 0)
                {
                    videoUrl = video.file.Substring(0, licSep);
                    licenseCode = video.file.Substring(licSep + 4);
                }
                string token = EncryptQuery($"{plugin}_-:-_{url}_-:-_{videoUrl}");
                string stremUrl = $"{host}/nexthub/strem.mp4?u={HttpUtility.UrlEncode(token)}&amp;referer={HttpUtility.UrlEncode(url)}";
                if (!string.IsNullOrEmpty(licenseCode))
                    stremUrl += $"&amp;license={HttpUtility.UrlEncode(licenseCode)}";
                stream_links.qualitys["auto"] = stremUrl;

                bool savedProxy = init.streamproxy;
                bool savedReserve = init.url_reserve;
                var savedHeaders = init.headers_stream;
                init.streamproxy = false;
                init.url_reserve = false;
                init.headers_stream = null;
                try
                {
                    return OnResult(stream_links);
                }
                finally
                {
                    init.streamproxy = savedProxy;
                    init.url_reserve = savedReserve;
                    init.headers_stream = savedHeaders;
                }
            }

            return OnResult(stream_links);
        }
        finally
        {
            semaphore?.Release();
        }
    }


    #region goVideoToBrowser
    async Task<VideoModel> goVideoToBrowser(string plugin, string url, NxtSettings init)
    {
        if (string.IsNullOrEmpty(url))
            return default;

        try
        {
            string memKey = $"nexthub:view18:goVideo:{url}{evalQueryCacheKey}";
            if (init.view.bindingToIP && proxyManager != null)
                memKey += $":{proxyManager.CurrentProxyIp}";

            var entryCache = await hybridCache.EntryAsync<VideoModel>(memKey);
            if (entryCache.success)
                return entryCache.value;

            var headers = httpHeaders(init);
            string targetHost = init.cors(url);
            if (!Root.isSafeHttpUrl(targetHost))
                return default;

            var cache = new VideoModel();

            using (var browser = new PlaywrightBrowser(init.view.priorityBrowser ?? init.priorityBrowser))
            {
                var page = await browser.NewPageAsync(init.plugin, headers?.ToDictionary(), proxy_data, keepopen: init.view.keepopen, deferredDispose: init.view.playbtn != null).ConfigureAwait(false);
                if (page == default)
                    return default;

                if (init.cookies != null)
                    await page.Context.AddCookiesAsync(init.cookies).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(init.view.addInitScript))
                    await page.AddInitScriptAsync(init.view.addInitScript).ConfigureAwait(false);

                string routeEval = init.view.routeEval;
                if (!string.IsNullOrEmpty(routeEval) && routeEval.EndsWith(".cs"))
                    routeEval = FileCache.ReadAllText($"{ModInit.modpath}/sites/{routeEval}");

                #region RouteAsync
                await page.RouteAsync("**/*", async route =>
                {
                    try
                    {
                        if (!Root.isSafeHttpUrl(route.Request.Url))
                        {
                            await route.AbortAsync();
                            return;
                        }

                        if (browser.IsCompleted || (init.view.patternAbort != null && Regex.IsMatch(route.Request.Url, init.view.patternAbort, RegexOptions.IgnoreCase)))
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                            await route.AbortAsync();
                            return;
                        }

                        #region routeEval
                        if (routeEval != null)
                        {
                            bool _next = await CSharpEval.ExecuteAsync<bool>(routeEval, new NxtRoute(route, evalQuery, targetHost, null, null, null, null, 0), Root.routeOptions);
                            if (!_next)
                                return;
                        }
                        #endregion

                        #region patternFile
                        if (init.view.patternFile != null && Regex.IsMatch(route.Request.Url, init.view.patternFile, RegexOptions.IgnoreCase))
                        {
                            if (init.view.waitForResponse)
                            {
                                string result = null;
                                await browser.ClearContinueAsync(route, page);
                                var response = await page.WaitForResponseAsync(route.Request.Url);
                                if (response != null)
                                    result = await response.TextAsync().ConfigureAwait(false);

                                PlaywrightBase.ConsoleLog(() => $"\nPlaywright: {result}\n");
                                browser.SetPageResult(result);
                            }
                            else
                            {
                                void setHeaders(Dictionary<string, string> _headers)
                                {
                                    if (_headers != null && _headers.Count > 0)
                                    {
                                        cache.headers = new List<HeadersModel>(_headers.Count);
                                        foreach (var item in _headers)
                                        {
                                            string hk = item.Key.ToLower();
                                            if (hk == "host" || hk == "accept-encoding" || hk == "connection" || hk == "range")
                                                continue;

                                            cache.headers.Add(new HeadersModel(item.Key, item.Value.ToString()));
                                        }
                                    }
                                }

                                setHeaders(route.Request.Headers);

                                if (init.view.waitLocationFile)
                                {
                                    await browser.ClearContinueAsync(route, page);
                                    string setUri = route.Request.Url;

                                    var response = await page.WaitForResponseAsync(route.Request.Url);
                                    if (response != null && response.Headers.ContainsKey("location"))
                                    {
                                        setHeaders(response.Request.Headers);
                                        setUri = response.Headers["location"];
                                    }

                                    if (setUri.StartsWith("//"))
                                        setUri = $"{(init.host.StartsWith("https") ? "https" : "http")}:{setUri}";

                                    PlaywrightBase.ConsoleLog(() => $"\nPlaywright: SET {setUri}\n{JsonConvert.SerializeObject(cache.headers.ToDictionary(), Formatting.Indented)}\n");
                                    browser.SetPageResult(setUri);
                                }
                                else
                                {
                                    PlaywrightBase.ConsoleLog(() => $"\nPlaywright: SET {route.Request.Url}\n{JsonConvert.SerializeObject(cache.headers.ToDictionary(), Formatting.Indented)}\n");
                                    browser.SetPageResult(route.Request.Url);
                                    await route.AbortAsync();
                                }
                            }

                            return;
                        }
                        #endregion

                        #region patternAbortEnd
                        if (init.view.patternAbortEnd != null && Regex.IsMatch(route.Request.Url, init.view.patternAbortEnd, RegexOptions.IgnoreCase))
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                            await route.AbortAsync();
                            return;
                        }
                        #endregion

                        #region patternWhiteRequest
                        if (init.view.patternWhiteRequest != null && route.Request.Url != targetHost && !Regex.IsMatch(route.Request.Url, init.view.patternWhiteRequest, RegexOptions.IgnoreCase))
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: Abort {route.Request.Url}");
                            await route.AbortAsync();
                            return;
                        }
                        #endregion

                        #region abortMedia
                        if (init.view.abortMedia || init.view.fullCacheJS)
                        {
                            if (await PlaywrightBase.AbortOrCache(page, route, abortMedia: init.view.abortMedia, fullCacheJS: init.view.fullCacheJS))
                                return;
                        }
                        else
                        {
                            PlaywrightBase.ConsoleLog(() => $"Playwright: {route.Request.Method} {route.Request.Url}");
                        }
                        #endregion

                        await browser.ClearContinueAsync(route, page);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_r9ux5f8f");
                    }
                });
                #endregion

                #region GotoAsync
            resetGotoAsync:
                if (!Root.isSafeHttpUrl(targetHost))
                    return default;

                string html = null;
                var responce = await page.GotoAsync(init.view.viewsource ? $"view-source:{targetHost}" : targetHost, new PageGotoOptions()
                {
                    Timeout = 10_000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                }).ConfigureAwait(false);

                if (responce != null)
                    html = await responce.TextAsync().ConfigureAwait(false);
                #endregion

                if (init.view.waitForResponse)
                    html = await browser.WaitPageResult().ConfigureAwait(false);

                #region WaitForSelector
                if (!string.IsNullOrEmpty(init.view.waitForSelector) || !string.IsNullOrEmpty(init.view.playbtn))
                {
                    try
                    {
                        await page.WaitForSelectorAsync(init.view.waitForSelector ?? init.view.playbtn, new PageWaitForSelectorOptions
                        {
                            Timeout = init.view.waitForSelector_timeout

                        }).ConfigureAwait(false);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error(ex, "CatchId={CatchId}", "id_mnr5650j");
                    }

                    html = await page.ContentAsync().ConfigureAwait(false);
                }
                #endregion

                #region iframe
                if (init.view.iframe != null && targetHost.Contains(init.host))
                {
                    string iframeUrl = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.iframe), new NxtRegexMatch(html, init.view.iframe));
                    if (!string.IsNullOrEmpty(iframeUrl) && iframeUrl != targetHost)
                    {
                        targetHost = init.view.iframe.format != null ? init.view.iframe.format.Replace("{value}", iframeUrl) : iframeUrl;
                        goto resetGotoAsync;
                    }
                }
                #endregion

                if (!string.IsNullOrEmpty(init.view.playbtn))
                    await page.ClickAsync(init.view.playbtn).ConfigureAwait(false);

                if (init.view.nodeFile != null)
                {
                    #region nodeFile
                    string goFile(string _content)
                    {
                        if (!string.IsNullOrEmpty(_content))
                        {
                            var doc = new HtmlDocument();
                            doc.LoadHtml(_content);
                            var videoNode = doc.DocumentNode.SelectSingleNode(init.view.nodeFile.node);
                            if (videoNode != null)
                                return (!string.IsNullOrEmpty(init.view.nodeFile.attribute) ? videoNode.GetAttributeValue(init.view.nodeFile.attribute, null) : videoNode.InnerText)?.Trim();
                        }

                        return null;
                    }

                    if (init.view.NetworkIdle)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            cache.file = goFile(await page.ContentAsync().ConfigureAwait(false));
                            if (!string.IsNullOrEmpty(cache.file))
                                break;

                            PlaywrightBase.ConsoleLog(() => "ContentAsync: " + (i + 1));
                            await Task.Delay(800).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        cache.file = goFile(html);
                    }

                    PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                    #endregion
                }
                else if (init.view.regexMatch != null)
                {
                    #region regexMatch
                    if (init.view.NetworkIdle)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            cache.file = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.regexMatch), new NxtRegexMatch(html, init.view.regexMatch));
                            if (!string.IsNullOrEmpty(cache.file) && init.view.regexMatch.format != null)
                                cache.file = init.view.regexMatch.format.Replace("{value}", cache.file).Replace("{host}", init.host);

                            if (!string.IsNullOrEmpty(cache.file))
                                break;

                            PlaywrightBase.ConsoleLog(() => "ContentAsync: " + (i + 1));
                            await Task.Delay(800).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        cache.file = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.regexMatch), new NxtRegexMatch(html, init.view.regexMatch));
                        if (!string.IsNullOrEmpty(cache.file) && init.view.regexMatch.format != null)
                            cache.file = init.view.regexMatch.format.Replace("{value}", cache.file).Replace("{host}", init.host);
                    }

                    PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                    #endregion
                }
                else if (string.IsNullOrEmpty(init.view.eval ?? init.view.evalJS))
                {
                    cache.file = await browser.WaitPageResult().ConfigureAwait(false);
                }

                cache.file = cache.file?
                    .Replace("\\", "")?
                    .Replace("&amp;", "&")?
                    .Replace("u0026", "&");

                #region eval
                if (!string.IsNullOrEmpty(init.view.eval ?? init.view.evalJS))
                {
                    Task<string> goFile(string _content)
                    {
                        if (!string.IsNullOrEmpty(_content))
                        {
                            string infile = $"{ModInit.modpath}/sites/{init.view.eval ?? init.view.evalJS}";
                            if ((infile.EndsWith(".cs") || infile.EndsWith(".js")) && System.IO.File.Exists(infile))
                            {
                                string evaluate = FileCache.ReadAllText(infile);

                                if (infile.EndsWith(".js"))
                                    return page.EvaluateAsync<string>($"(html, plugin, url, file) => {{ {evaluate} }}", new { _content, plugin, targetHost, cache.file });

                                var nxt = new NxtEvalView(init, evalQuery, _content, plugin, targetHost, cache.file, cache.headers, proxyManager);
                                return CSharpEval.ExecuteAsync<string>(goEval(evaluate), nxt, Root.evalOptionsFull);
                            }
                            else
                            {
                                if (init.view.evalJS != null)
                                    return page.EvaluateAsync<string>($"(html, plugin, url, file) => {{ {init.view.evalJS} }}", new { _content, plugin, targetHost, cache.file });

                                var nxt = new NxtEvalView(init, evalQuery, _content, plugin, targetHost, cache.file, cache.headers, proxyManager);
                                return CSharpEval.ExecuteAsync<string>(goEval(init.view.eval), nxt, Root.evalOptionsFull);
                            }
                        }

                        return null;
                    }

                    if (init.view.NetworkIdle)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            cache.file = await goFile(await page.ContentAsync().ConfigureAwait(false)).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(cache.file))
                                break;

                            PlaywrightBase.ConsoleLog(() => "ContentAsync: " + (i + 1));
                            await Task.Delay(800).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        cache.file = await goFile(html).ConfigureAwait(false);
                    }

                    PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                }
                #endregion

                if (string.IsNullOrEmpty(cache.file))
                {
                    proxyManager?.Refresh();
                    return default;
                }

                if (cache.file.StartsWith("GotoAsync:"))
                {
                    targetHost = cache.file.Replace("GotoAsync:", "").Trim();
                    goto resetGotoAsync;
                }

                if (!Root.isSafeHttpUrl(cache.file))
                    return default;

                #region related
                if (init.view.related && cache.recomends == null)
                {
                    if (init.view.NetworkIdle)
                    {
                        string contetnt = await page.ContentAsync().ConfigureAwait(false);
                        cache.recomends = ListController.goPlaylist(requestInfo, host, init.view.relatedParse ?? init.contentParse, init, contetnt, null, plugin);
                    }
                    else
                    {
                        cache.recomends = ListController.goPlaylist(requestInfo, host, init.view.relatedParse ?? init.contentParse, init, html, null, plugin);
                    }
                }
                #endregion

                // Extract Playwright cookies so Strem can use them for CDN streaming
                if (!string.IsNullOrEmpty(cache.file) && cache.file.Contains("/get_file/"))
                {
                    try
                    {
                        var pwCookies = await page.Context.CookiesAsync(new[] { targetHost }).ConfigureAwait(false);
                        if (pwCookies.Count > 0)
                        {
                            var cookieArray = pwCookies
                                .Where(c => !string.IsNullOrEmpty(c.Name))
                                .Select(c => new System.Net.Cookie(c.Name, c.Value, c.Domain, c.Path))
                                .ToArray();
                            if (cookieArray.Length > 0)
                                _playwrightCookies[cache.file] = (cookieArray, DateTime.UtcNow.AddMinutes(10));
                        }
                    }
                    catch { }
                }
            }

            proxyManager?.Success();
            hybridCache.Set(memKey, cache, cacheTime(init.view.cache_time));

            return cache;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_7049566f");
            if (init.debug)
                Console.WriteLine(ex);

            return default;
        }
    }
    #endregion

    #region goVideoToHttp
    async Task<VideoModel> goVideoToHttp(string plugin, string url)
    {
        if (string.IsNullOrEmpty(url))
            return default;

        try
        {
            string memKey = $"nexthub:view18:goVideo:{url}{evalQueryCacheKey}";

            if (init.view.bindingToIP)
                memKey = ipkey(memKey);

            var entryCache = await hybridCache.EntryAsync<VideoModel>(memKey);
            if (entryCache.success)
                return entryCache.value;

        resetGotoAsync:
            if (!Root.isSafeHttpUrl(url))
                return default;

            string html = await httpHydra.Get(url);
            if (string.IsNullOrEmpty(html))
                return default;

            #region iframe
            if (init.view.iframe != null && url.Contains(init.host))
            {
                string iframeUrl = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.iframe), new NxtRegexMatch(html, init.view.iframe));
                if (!string.IsNullOrEmpty(iframeUrl) && iframeUrl != url)
                {
                    url = init.view.iframe.format != null ? init.view.iframe.format.Replace("{value}", iframeUrl) : iframeUrl;
                    goto resetGotoAsync;
                }
            }
            #endregion

            var cache = new VideoModel();

            if (init.view.nodeFile != null)
            {
                #region nodeFile
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var videoNode = doc.DocumentNode.SelectSingleNode(init.view.nodeFile.node);
                if (videoNode != null)
                    cache.file = (!string.IsNullOrEmpty(init.view.nodeFile.attribute) ? videoNode.GetAttributeValue(init.view.nodeFile.attribute, null) : videoNode.InnerText)?.Trim();

                PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                #endregion
            }
            else if (init.view.regexMatch != null)
            {
                #region regexMatch
                cache.file = CSharpEval.Execute<string>(evalCodeToRegexMatch(init.view.regexMatch), new NxtRegexMatch(html, init.view.regexMatch));
                if (!string.IsNullOrEmpty(cache.file) && init.view.regexMatch.format != null)
                    cache.file = init.view.regexMatch.format.Replace("{value}", cache.file).Replace("{host}", init.host);

                PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
                #endregion
            }

            cache.file = cache.file?
                .Replace("\\", "")?
                .Replace("&amp;", "&")?
                .Replace("u0026", "&");

            #region eval
            if (!string.IsNullOrEmpty(init.view.eval))
            {
                var nxt = new NxtEvalView(init, evalQuery, html, plugin, url, cache.file, cache.headers, proxyManager);
                cache.file = await CSharpEval.ExecuteAsync<string>(goEval(init.view.eval), nxt, Root.evalOptionsFull).ConfigureAwait(false);

                PlaywrightBase.ConsoleLog(() => $"Playwright: SET {cache.file}");
            }
            #endregion

            if (string.IsNullOrEmpty(cache.file))
            {
                proxyManager?.Refresh();
                return default;
            }

            if (cache.file.StartsWith("GotoAsync:"))
            {
                url = cache.file.Replace("GotoAsync:", "").Trim();
                goto resetGotoAsync;
            }

            cache.file = CleanKvsMedia(cache.file);

            if (!Root.isSafeHttpUrl(cache.file))
                return default;

            if (init.view.related && cache.recomends == null)
            {
                string memKeyRelated = $"related:{memKey}";
                var cacheRelated = await hybridCache.EntryAsync<List<PlaylistItem>>(memKeyRelated);
                if (cacheRelated.success)
                    cache.recomends = cacheRelated.value;
                else
                {
                    cache.recomends = ListController.goPlaylist(requestInfo, host, init.view.relatedParse ?? init.contentParse, init, html, null, plugin);
                    if (cache.recomends != null && cache.recomends.Count > 0)
                        hybridCache.Set(memKeyRelated, cache.recomends, TimeSpan.FromHours(36));
                }
            }

            proxyManager?.Success();

            hybridCache.Set(memKey, cache, cacheTime(init.view.cache_time));

            return cache;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "CatchId={CatchId}", "id_5f5d9c49");
            if (init.debug)
                Console.WriteLine(ex);

            return default;
        }
    }
    #endregion


    [HttpGet]
    [HttpHead]
    [Route("nexthub/strem.mp4")]
    [Route("nexthub/strem")]
    async public Task<ActionResult> Strem(string u, string src, string plugin, string referer, string license)
    {
        StatiCacheDisabled = true;

        if (HttpMethods.IsHead(Request.Method))
        {
            Response.StatusCode = 200;
            Response.ContentType = "video/mp4";
            Response.Headers["Accept-Ranges"] = "bytes";
            Response.Headers["Cache-Control"] = "no-store";
            return _emptyResult;
        }

        if (string.IsNullOrEmpty(src))
            src = Request.Query["amp;src"].ToString();
        if (string.IsNullOrEmpty(plugin))
            plugin = Request.Query["amp;plugin"].ToString();
        if (string.IsNullOrEmpty(referer))
            referer = Request.Query["amp;referer"].ToString();
        if (string.IsNullOrEmpty(u))
            u = Request.Query["amp;u"].ToString();
        if (string.IsNullOrEmpty(license))
            license = Request.Query["amp;license"].ToString();
        if (string.IsNullOrEmpty(license))
            license = Request.Query["license"].ToString();

        string file = null;

        if (!string.IsNullOrEmpty(src) || (!string.IsNullOrEmpty(referer) && string.IsNullOrEmpty(u)))
        {
            if (string.IsNullOrEmpty(plugin))
                plugin = "85po";
            file = src;
        }
        else
        {
            u = DecryptQuery(u);
            if (string.IsNullOrEmpty(u))
                return OnError("uri", rcache: false);

            int first = u.IndexOf("_-:-_", StringComparison.Ordinal);
            int second = first > 0 ? u.IndexOf("_-:-_", first + 5, StringComparison.Ordinal) : -1;
            if (first <= 0 || second <= 0 || second + 5 >= u.Length)
                return OnError("uri", rcache: false);

            plugin = u.Substring(0, first);
            referer = u.Substring(first + 5, second - (first + 5));
            file = u.Substring(second + 5);
        }

        var _nxtInit = Root.goInit(plugin);
        if (_nxtInit == null)
            return OnError("init not found", rcache: false);

        if (await IsRequestBlocked(_nxtInit, rch: _nxtInit.rch_access != null, rch_check: false))
            return badInitMsg;

        file = CleanKvsMedia(file);
        if (!string.IsNullOrEmpty(file) && !Root.isSafeHttpUrl(file))
            return OnError("url", rcache: false);

        var cookies = new CookieContainer();

        // Retrieve Playwright cookies cached by goVideoToBrowser (bypasses Cloudflare)
        if (!string.IsNullOrEmpty(file) && _playwrightCookies.TryGetValue(file, out var pwCookieEntry) && pwCookieEntry.expiry > DateTime.UtcNow)
        {
            foreach (var c in pwCookieEntry.cookies)
                cookies.Add(c);
            // Evict after use
            _playwrightCookies.TryRemove(file, out _);
        }

        var handler = new HttpClientHandler();
        handler.AllowAutoRedirect = true;
        handler.MaxAutomaticRedirections = 8;
        handler.UseCookies = true;
        handler.CookieContainer = cookies;
        handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        handler.ServerCertificateCustomValidationCallback = Http.AlwaysAllowCertificate;
        handler.UseProxy = proxy != null;
        handler.Proxy = proxy;

        using (handler)
        using (var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromHours(2) })
        {
            // Phase 1 — use license passed from Index (bypasses Cloudflare)
            // Also try fetching license from referer page as fallback
            if (string.IsNullOrEmpty(license) && Root.isSafeHttpUrl(referer))
            {
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Get, referer))
                    {
                        req.Headers.TryAddWithoutValidation("Accept", "text/html");
                        req.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
                        using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted).ConfigureAwait(false))
                        {
                            if (resp.Content != null)
                            {
                                string html = await resp.Content.ReadAsStringAsync(HttpContext.RequestAborted).ConfigureAwait(false);
                                if (!string.IsNullOrEmpty(html) && html.Length > 32)
                                    license = Regex.Match(html, "license_code:[\\n\\r\\t ]+'([^']+)'", RegexOptions.IgnoreCase).Groups[1].Value;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            if (!string.IsNullOrEmpty(license) && !string.IsNullOrEmpty(file))
            {
                string rehashed = RehashKvs(file, license);
                if (rehashed != file)
                {
                    string download = rehashed;
                    if (download.IndexOf("download=true", StringComparison.OrdinalIgnoreCase) < 0)
                        download = download + (download.IndexOf('?') >= 0 ? "&" : "?") + "download=true";
                    if (await WriteKvsMedia(client, download, referer).ConfigureAwait(false))
                        return _emptyResult;
                    if (await WriteKvsMedia(client, rehashed, referer).ConfigureAwait(false))
                        return _emptyResult;
                }
            }

            // Phase 1b — try the original URL + download=true
            if (!string.IsNullOrEmpty(file))
            {
                string download = file;
                if (download.IndexOf("download=true", StringComparison.OrdinalIgnoreCase) < 0)
                    download = download + (download.IndexOf('?') >= 0 ? "&" : "?") + "download=true";
                if (await WriteKvsMedia(client, download, referer).ConfigureAwait(false))
                    return _emptyResult;
                if (await WriteKvsMedia(client, file, referer).ConfigureAwait(false))
                    return _emptyResult;
            }

            // Phase 2 — warm page, extract fresh URLs, retry up to 2 rounds
            for (int round = 0; round < 2; round++)
            {
                string html = await WarmKvsPage(client, referer).ConfigureAwait(false);

                var candidates = new List<string>();
                if (!string.IsNullOrEmpty(html))
                {
                    List<string> live = ExtractKvsMedia(html);
                    for (int i = 0; i < live.Count; i++)
                        AddKvsCandidate(candidates, live[i]);
                }
                AddKvsCandidate(candidates, file);

                kvsLastError = string.IsNullOrEmpty(html) ? "file:page" : "file";

                if (await TryKvsCandidates(client, candidates, referer).ConfigureAwait(false))
                    return _emptyResult;

                // Extract license from fresh HTML for rehash
                if (!string.IsNullOrEmpty(html) && string.IsNullOrEmpty(license))
                    license = Regex.Match(html, "license_code:[\\n\\r\\t ]+'([^']+)'", RegexOptions.IgnoreCase).Groups[1].Value;

                if (!string.IsNullOrEmpty(license) && candidates.Count > 0)
                {
                    var rehashed = new List<string>();
                    for (int i = 0; i < candidates.Count; i++)
                        AddKvsCandidate(rehashed, RehashKvs(candidates[i], license));

                    if (await TryKvsCandidates(client, rehashed, referer).ConfigureAwait(false))
                        return _emptyResult;
                }

                // Wait briefly before retry
                if (round == 0)
                    await Task.Delay(300, HttpContext.RequestAborted).ConfigureAwait(false);
            }
        }

        return OnError(string.IsNullOrEmpty(kvsLastError) ? "file" : kvsLastError, refresh_proxy: true);
    }


    #region CleanKvsMedia
    static string CleanKvsMedia(string file)
    {
        if (string.IsNullOrEmpty(file))
            return file;

        int hash = file.IndexOf('#');
        if (hash >= 0)
            file = file.Substring(0, hash);

        file = file.Replace("\\", "").Replace("&amp;", "&").Replace("u0026", "&").Replace("function/0/", "");
        file = Regex.Replace(file, "\\.mp4/+(\\?|$)", ".mp4$1", RegexOptions.IgnoreCase);
        file = Regex.Replace(file, "([?&])download(_filename)?=[^&]*", "$1", RegexOptions.IgnoreCase);
        file = Regex.Replace(file, "[?&]{2,}", m => m.Value.IndexOf('?') >= 0 ? "?" : "&");
        file = file.Replace("?&", "?");
        file = Regex.Replace(file, "[?&]$", "");
        return file;
    }
    #endregion

    #region AddKvsCandidate
    static void AddKvsCandidate(List<string> list, string file)
    {
        file = CleanKvsMedia(file);
        if (string.IsNullOrEmpty(file) || !Root.isSafeHttpUrl(file))
            return;

        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], file, StringComparison.OrdinalIgnoreCase))
                return;
        }

        list.Add(file);
    }
    #endregion

    #region ExtractKvsMedia
    static List<string> ExtractKvsMedia(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        string[] qualities = new string[] { "_1080p", "_720p", "_480p", "_360p" };
        for (int i = 0; i < qualities.Length; i++)
        {
            Match m = Regex.Match(html, "(https?://[^/]+/get_file/[^'\\\"\\s]+" + qualities[i] + "\\.mp4[^'\\\"\\s]*)", RegexOptions.IgnoreCase);
            if (m.Success)
                AddKvsCandidate(urls, m.Groups[1].Value);
        }

        string[] keys = new string[] { "alt_url5", "alt_url4", "alt_url3", "alt_url2", "alt_url", "url" };
        for (int i = 0; i < keys.Length; i++)
        {
            Match m = Regex.Match(html, "video_" + keys[i] + ":\\s*'([^']+)'", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Value.IndexOf("/get_file/", StringComparison.OrdinalIgnoreCase) >= 0)
                AddKvsCandidate(urls, m.Groups[1].Value);
        }

        MatchCollection all = Regex.Matches(html, "https?://[^'\\\"\\s]+/get_file/[^'\\\"\\s]+\\.mp4[^'\\\"\\s]*", RegexOptions.IgnoreCase);
        for (int i = 0; i < all.Count; i++)
            AddKvsCandidate(urls, all[i].Value);

        if (urls.Count > 4)
            urls.RemoveRange(4, urls.Count - 4);

        return urls;
    }
    #endregion

    #region RehashKvs
    static string RehashKvs(string file, string license)
    {
        if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(license))
            return file;

        try
        {
            string[] arr = file.Split('/');
            int gi = -1;
            for (int i = 0; i < arr.Length; i++)
            {
                if (string.Equals(arr[i], "get_file", StringComparison.OrdinalIgnoreCase))
                {
                    gi = i;
                    break;
                }
            }

            if (gi < 0 || gi + 2 >= arr.Length || arr[gi + 2].Length < 32)
                return file;

            string hashed = CalcKvsHash(arr[gi + 2], license);
            if (string.IsNullOrEmpty(hashed) || hashed == arr[gi + 2])
                return file;

            arr[gi + 2] = hashed;
            return string.Join("/", arr);
        }
        catch
        {
            return file;
        }
    }

    static string CalcKvsHash(string fakeHash, string licenseCode)
    {
        string h = fakeHash.Substring(0, 32);
        string i = KvsLicenseKey(licenseCode);
        string j = h;

        for (int k = h.Length - 1; k >= 0; k--)
        {
            int l = k;
            for (int m = k; m < i.Length; m++)
                l += i[m] - '0';

            while (l >= h.Length)
                l -= h.Length;

            char[] n = new char[h.Length];
            for (int o = 0; o < h.Length; o++)
            {
                if (o == k)
                    n[o] = h[l];
                else if (o == l)
                    n[o] = h[k];
                else
                    n[o] = h[o];
            }
            h = new string(n);
        }

        return fakeHash.Replace(j, h);
    }

    static string KvsLicenseKey(string licenseCode)
    {
        string d = licenseCode;
        string f = "";

        for (int g = 1; g < d.Length; g++)
        {
            if (char.IsDigit(d[g]))
            {
                int digit = d[g] - '0';
                f += digit != 0 ? digit.ToString() : "1";
            }
            else
            {
                f += "1";
            }
        }

        int j = f.Length / 2;
        long k = long.Parse(f.Substring(0, j + 1));
        long l = long.Parse(f.Substring(j));

        long g1 = l - k;
        if (g1 < 0) g1 = -g1;
        long g2 = k - l;
        if (g2 < 0) g2 = -g2;

        f = ((g1 + g2) * 2).ToString();

        int cap = 10;
        string m = "";
        for (int g = 0; g < j + 1; g++)
        {
            for (int h = 1; h <= 4; h++)
            {
                int charValue1 = 0;
                if (g + h < d.Length && char.IsDigit(d[g + h]))
                    charValue1 = d[g + h] - '0';

                int charValue2 = 0;
                if (g < f.Length && char.IsDigit(f[g]))
                    charValue2 = f[g] - '0';

                int n = charValue1 + charValue2;
                if (n >= cap)
                    n -= cap;
                m += n.ToString();
            }
        }

        return m;
    }
    #endregion

    #region TryKvsCandidates
    async Task<bool> TryKvsCandidates(HttpClient client, List<string> files, string referer)
    {
        int max = files.Count < 3 ? files.Count : 3;
        for (int i = 0; i < max; i++)
        {
            string play = files[i];
            if (await WriteKvsMedia(client, play, referer).ConfigureAwait(false))
                return true;

            string download = play;
            if (download.IndexOf("download=true", StringComparison.OrdinalIgnoreCase) < 0)
                download = download + (download.IndexOf('?') >= 0 ? "&" : "?") + "download=true";

            if (download != play && await WriteKvsMedia(client, download, referer).ConfigureAwait(false))
                return true;
        }

        return false;
    }
    #endregion

    #region WarmKvsPage
    async Task<string> WarmKvsPage(HttpClient client, string referer)
    {
        if (!Root.isSafeHttpUrl(referer))
            return null;

        try
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, referer))
            {
                req.Version = HttpVersion.Version11;
                req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
                req.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
                req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted).ConfigureAwait(false))
                {
                    if (resp.Content == null)
                        return null;

                    string html = await resp.Content.ReadAsStringAsync(HttpContext.RequestAborted).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(html) || html.Length < 32)
                        return null;
                    if (html.Length > 1500000)
                        html = html.Substring(0, 1500000);
                    return html;
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return null;
        }
    }
    #endregion

    #region WriteKvsMedia
    async Task<bool> WriteKvsMedia(HttpClient client, string file, string referer)
    {
        if (string.IsNullOrEmpty(file) || !Root.isSafeHttpUrl(file))
            return false;

        string refer = !string.IsNullOrEmpty(referer) ? referer : (init.host + "/");
        string origin = null;
        Uri ru;
        if (!string.IsNullOrEmpty(refer) && Uri.TryCreate(refer, UriKind.Absolute, out ru))
            origin = ru.GetLeftPart(UriPartial.Authority);

        var headers = httpHeaders(init.host ?? init.apihost, HeadersModel.InitOrNull(init.headers_stream));
        bool started = false;

        using (var req = new HttpRequestMessage(HttpMethod.Get, file))
        {
            req.Version = HttpVersion.Version11;
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            req.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
            req.Headers.TryAddWithoutValidation("Referer", refer);
            if (!string.IsNullOrEmpty(origin))
                req.Headers.TryAddWithoutValidation("Origin", origin);
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "video");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");

            if (headers != null)
            {
                foreach (var h in headers)
                {
                    if (h.name.Equals("referer", StringComparison.OrdinalIgnoreCase) ||
                        h.name.Equals("host", StringComparison.OrdinalIgnoreCase) ||
                        h.name.Equals("range", StringComparison.OrdinalIgnoreCase) ||
                        h.name.Equals("origin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    req.Headers.TryAddWithoutValidation(h.name, h.val);
                }
            }

            if (Request.Headers.TryGetValue("Range", out var range) && range.Count > 0)
                req.Headers.TryAddWithoutValidation("Range", range.ToString());

            try
            {
                using (var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted).ConfigureAwait(false))
                {
                    int code = (int)response.StatusCode;
                    if (code != 200 && code != 206)
                    {
                        kvsLastError = "file:" + code;
                        return false;
                    }

                    string mediaType = response.Content != null && response.Content.Headers.ContentType != null
                        ? (response.Content.Headers.ContentType.MediaType ?? string.Empty)
                        : string.Empty;
                    if (mediaType.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                    {
                        kvsLastError = "file:html";
                        return false;
                    }

                    using (var input = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted).ConfigureAwait(false))
                    {
                        byte[] probe = new byte[16];
                        int probed = 0;
                        while (probed < 12)
                        {
                            int read = await input.ReadAsync(probe, probed, probe.Length - probed, HttpContext.RequestAborted).ConfigureAwait(false);
                            if (read <= 0)
                                break;
                            probed += read;
                        }

                        if (probed == 0)
                        {
                            kvsLastError = "file:empty";
                            return false;
                        }

                        byte b0 = probe[0];
                        if (b0 == (byte)'<' || b0 == (byte)'{' || b0 == (byte)'[')
                        {
                            kvsLastError = "file:html";
                            return false;
                        }

                        Response.StatusCode = code;
                        started = true;
                        Response.ContentType = mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                            ? mediaType
                            : "video/mp4";

                        if (response.Content.Headers.ContentLength.HasValue)
                            Response.ContentLength = response.Content.Headers.ContentLength.Value;

                        Response.Headers["Accept-Ranges"] = "bytes";
                        if (response.Content.Headers.ContentRange != null)
                            Response.Headers["Content-Range"] = response.Content.Headers.ContentRange.ToString();

                        Response.Headers["Cache-Control"] = "no-store";
                        Response.Headers.Remove("Content-Disposition");

                        if (HttpMethods.IsHead(Request.Method))
                            return true;

                        if (probed > 0)
                            await Response.Body.WriteAsync(probe, 0, probed, HttpContext.RequestAborted).ConfigureAwait(false);

                        await input.CopyToAsync(Response.Body, HttpContext.RequestAborted).ConfigureAwait(false);
                    }

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                return started;
            }
            catch (Exception ex)
            {
                if (started)
                    return true;

                string msg = ex.Message ?? string.Empty;
                if (msg.IndexOf("SSL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("TLS", StringComparison.OrdinalIgnoreCase) >= 0)
                    kvsLastError = "file:ssl";
                else
                    kvsLastError = "file:ex";

                Log.Error(ex, "CatchId={CatchId}", "id_kvsstrem1");
                return false;
            }
        }
    }
    #endregion

    #region evalCodeToRegexMatch
    static string evalCodeToRegexMatch(RegexMatchSettings rm)
    {
        return @"if (m.matches != null && m.matches.Length > 0)
            {
                foreach (string q in m.matches)
                {
                    string file = Regex.Match(html, m.pattern.Replace(""{value}"", $""{q}""), RegexOptions.IgnoreCase).Groups[m.index].Value;
                    if (!string.IsNullOrEmpty(file))
                        return file;
                }
                return null;
            }

            return Regex.Match(html, m.pattern, RegexOptions.IgnoreCase).Groups[m.index].Value;";
    }
    #endregion

    #region goEval
    static string goEval(string evalcode)
    {
        string infile = $"{ModInit.modpath}/sites/{evalcode}";
        if (infile.EndsWith(".eval") && System.IO.File.Exists(infile))
            evalcode = FileCache.ReadAllText(infile);

        if (evalcode.Contains("{include:"))
        {
            string includePattern = @"{include:(?<file>[^}]+)}";
            var matches = Regex.Matches(evalcode, includePattern);
            foreach (Match match in matches)
            {
                string file = match.Groups["file"].Value.Trim();
                if (System.IO.File.Exists($"{ModInit.modpath}/utils/{file}"))
                {
                    string includeCode = FileCache.ReadAllText($"{ModInit.modpath}/utils/{file}");
                    evalcode = evalcode.Replace(match.Value, includeCode);
                }
            }
        }

        return evalcode;
    }
    #endregion
}

public class VideoModel
{
    public VideoModel() { }

    public VideoModel(string file, List<HeadersModel> headers, List<PlaylistItem> recomends)
    {
        this.file = file;
        this.headers = headers;
        this.recomends = recomends;
    }

    public string file { get; set; }
    public List<HeadersModel> headers { get; set; }
    public List<PlaylistItem> recomends { get; set; }
}
