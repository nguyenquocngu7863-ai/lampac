using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

namespace JavHD;

public class JavHDController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public JavHDController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("javhd")]
    async public Task<ActionResult> Index(string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"javhd:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;

            for (int t = 0; t < 3 && (playlists == null || playlists.Count == 0); t++)
            {
                if (t > 0)
                    await Task.Delay(1200);
                await httpHydra.GetSpan(JavHDTo.Uri(init.host, c, pg), span =>
                {
                    var pl = JavHDTo.Playlist("javhd/vidosik", span.ToString());
                    if (pl.Count > 0)
                        playlists = pl;
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", JavHDTo.ChromeUA),
                    ("Referer", "https://javhd.today/"),
                    ("X-Requested-With", "XMLHttpRequest")
                ));
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, JavHDTo.Menu(host));
    }

    async Task<string> GetHtmlAsync(string url, string referer, int tries = 3, string mustContain = null)
    {
        string html = null;
        for (int t = 0; t < tries && string.IsNullOrEmpty(html); t++)
        {
            if (t > 0)
                await Task.Delay(1500);
            try
            {
                await httpHydra.GetSpan(url, span =>
                {
                    string s = span.ToString();
                    if (s.Length > 2000 && (mustContain == null || s.Contains(mustContain)))
                        html = s;
                }, addheaders: HeadersModel.Init(
                    ("User-Agent", JavHDTo.ChromeUA),
                    ("Referer", referer)
                ));
            }
            catch { }
        }
        return html;
    }

    async Task<Dictionary<string, string>> ResolveLinksAsync(string uri)
    {
        string memKey = ipkey($"javhd:view:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = "https://javhd.today" + pageUrl;

        var links = new Dictionary<string, string>();

        string pageHtml = await GetHtmlAsync(pageUrl, "https://javhd.today/", 3, "data-embed");
        if (!string.IsNullOrEmpty(pageHtml))
        {
            int n = 0;
            foreach (string embed in JavHDTo.EmbedUrls(pageHtml))
            {
                if (n++ >= 5 || links.Count >= 6)
                    break;

                string stream = null;
                string label = "Server";

                if (embed.Contains("turbovid"))
                {
                    string embHtml = await GetHtmlAsync(embed, pageUrl, 3, "data-hash");
                    stream = JavHDTo.TurboM3u8(embHtml);
                    label = "Turbo";
                }
                else if (embed.Contains("cloudwish") || embed.Contains("mycloudz"))
                {
                    string embHtml = await GetHtmlAsync(embed, pageUrl, 3, "eval(function");
                    foreach (string hls in JavHDTo.CloudHlsUrls(embHtml))
                    {
                        stream = hls;
                        label = embed.Contains("cloudwish") ? "Cloudwish" : "Mycloudz";
                        break;
                    }
                }
                else
                {
                    string embHtml = await GetHtmlAsync(embed, pageUrl, 2);
                    if (!string.IsNullOrEmpty(embHtml))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(embHtml, "(https?://[^\\s\"']+\\.m3u8[^\\s\"']*)");
                        if (m.Success)
                        {
                            stream = m.Groups[1].Value;
                            label = "Server HLS";
                        }
                        else
                        {
                            var mp = System.Text.RegularExpressions.Regex.Match(embHtml, "(https?://[^\\s\"']+\\.mp4[^\\s\"']*)");
                            if (mp.Success)
                            {
                                stream = mp.Groups[1].Value;
                                label = "Server MP4";
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(stream))
                    continue;

                if (!links.ContainsKey(label))
                    links.TryAdd(label, stream);
                string pkey = label + " (proxy)";
                if (!links.ContainsKey(pkey))
                    links.TryAdd(pkey, stream);
            }
        }

        if (links.Count == 0)
            return null;

        hybridCache.Set(memKey, links, cacheTime(20));
        return links;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javhd/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, v =>
            $"{host}/javhd/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("javhd/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        if (!q.Contains("(proxy)"))
            return Redirect(link);

        string referer = "https://turbovid.vip/";
        if (link.Contains("cloudwish.xyz") || link.Contains("cdn-centaurus.com"))
            referer = "https://cloudwish.xyz/";
        else if (link.Contains("mycloudz.cc") || link.Contains("acek-cdn.com"))
            referer = "https://mycloudz.cc/";

        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", JavHDTo.ChromeUA),
            ("Referer", referer),
            ("Origin", referer.TrimEnd('/'))
        ));
        return Redirect(HostStreamProxy(link, headers));
    }
}
