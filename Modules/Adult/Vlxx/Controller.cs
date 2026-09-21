using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Vlxx;

public class VlxxController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public VlxxController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("vlxx")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"vlxx:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            await httpHydra.GetSpan(VlxxTo.Uri(init.host, search, c, pg), span =>
            {
                playlists = VlxxTo.Playlist("vlxx/vidosik", span.ToString());
            }, addheaders: HeadersModel.Init(
                ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                ("Referer", "https://vlxx.phd/")
            ));

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, VlxxTo.Menu(host));
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"vlxx:view:{uri}";

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
                string pageUrl = uri;
                if (uri.StartsWith("/"))
                    pageUrl = "https://vlxx.phd" + uri;
                else if (!uri.StartsWith("http"))
                    pageUrl = "https://vlxx.phd/" + uri.Trim('/');

                string pageHtml = null;

                await httpHydra.GetSpan(pageUrl, span =>
                {
                    pageHtml = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"),
                    ("Referer", "https://vlxx.phd/")
                ));

                if (string.IsNullOrEmpty(pageHtml))
                    return (null, false);

                var links = new Dictionary<string, string>();
                var ua = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

                // Trang video -> POST /ajax.php lay iframe embed -> mo embed lay file
                foreach (var (server, vid) in VlxxTo.GetServers(pageHtml, pageUrl))
                {
                    foreach (int vs in new int[] { 1, 2 })
                    {
                        try
                        {
                            var req = new HttpRequestMessage(HttpMethod.Post, "https://vlxx.phd/ajax.php");
                            req.Content = new StringContent($"vlxx_server={vs}&id={HttpUtility.UrlEncode(vid)}&server={HttpUtility.UrlEncode(server)}", Encoding.UTF8, "application/x-www-form-urlencoded");
                            req.Headers.TryAddWithoutValidation("Referer", pageUrl);
                            req.Headers.TryAddWithoutValidation("User-Agent", ua);
                            req.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

                            var resp = await httpClient.SendAsync(req);
                            string apiJson = resp != null ? await resp.Content.ReadAsStringAsync() : null;
                            if (string.IsNullOrEmpty(apiJson))
                                continue;

                            var pm = Regex.Match(apiJson, @"""player""\s*:\s*""((?:[^""\\]|\\.)*)""");
                            if (!pm.Success)
                                continue;

                            string playerHtml = pm.Groups[1].Value.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\n", "\n");
                            var im = Regex.Match(playerHtml, @"<iframe[^>]+src=[""']([^""']+/embed/[^""']+)[""']");
                            if (!im.Success)
                                continue;

                            string embedUrl = im.Groups[1].Value;
                            if (embedUrl.StartsWith("//"))
                                embedUrl = "https:" + embedUrl;

                            string embedHtml = null;
                            await httpHydra.GetSpan(embedUrl, span =>
                            {
                                embedHtml = span.ToString();
                            }, addheaders: HeadersModel.Init(
                                ("User-Agent", ua),
                                ("Referer", pageUrl)
                            ));

                            var got = VlxxTo.StreamLinksFromEmbed(embedHtml ?? "");
                            foreach (var kv in got)
                            {
                                string key = kv.Key;
                                int dup = 2;
                                while (links.ContainsKey(key))
                                    key = kv.Key + " " + (dup++);
                                if (!links.ContainsValue(kv.Value))
                                    links.TryAdd(key, kv.Value);
                            }

                            if (links.Count > 0)
                                break;
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    if (links.Count > 0)
                        break;
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

    [HttpGet, Staticache(manually: true)]
    [Route("vlxx/vidosik")]
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
            $"{host}/vlxx/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("vlxx/video")]
    [Route("vlxx/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://vlxx.phd/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }

    [HttpGet]
    [Route("vlxx/strem")]
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
            ("referer", "https://vlxx.phd/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
