namespace PhimSexVi;
public class PhimSexViController : BaseSisiController
{
    public PhimSexViController() : base(ModInit.conf) { }
    [HttpGet, Staticache(manually: true)]
    [Route("phimsexvi")]
    async public Task<ActionResult> Index(
        string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (pg < 1)
            pg = 1;
        var cache = await InvokeCacheResult(
            ipkey($"phimsexvi:{search}:{c}:{pg}"),
            10, jsonContext.ListPlaylistItem, async e =>
        {
            string url = PhimSexViTo.Uri(init.host, search, c, pg);
            string html = await FetchHtmlAsync(url);
            var playlists = PhimSexViTo.Playlist(
                "phimsexvi/vidosik", html ?? "");
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
        string key = ipkey("phimsexvi:nav");
        if (hybridCache.TryGetValue(key,
            out List<(string name, string path)> nav)
            && nav != null && nav.Count > 0)
            return PhimSexViTo.Menu(host, nav);
        string hostLocal = host;
        try
        {
            string html = await FetchHtmlAsync(PhimSexViTo.SiteHost + "/");
            var list = PhimSexViTo.NavList(html);
            if (list.Count > 0)
            {
                hybridCache.Set(key, list, cacheTime(360), true);
                return PhimSexViTo.Menu(host, list);
            }
        }
        catch { }
        _ = Task.Run(async () =>
        {
            try
            {
                string html = await FetchHtmlAsync(
                    PhimSexViTo.SiteHost + "/");
                var list = PhimSexViTo.NavList(html);
                if (list.Count > 0)
                    hybridCache.Set(key, list, cacheTime(360), true);
            }
            catch { }
        });
        return PhimSexViTo.Menu(hostLocal, new List<(string, string)>());
    }
    async Task<string> FetchHtmlAsync(string url, int seconds = 12)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            double left = (deadline - DateTime.UtcNow).TotalSeconds;
            if (left < 1)
                break;
            int cap = (int)Math.Ceiling(Math.Min(4, left));
            string html = null;
            try
            {
                await httpHydra.GetSpan(url, span =>
                {
                    html = span.ToString();
                }, addheaders: HFor(url));
            }
            catch { }
            if (!string.IsNullOrEmpty(html))
                return html;
            await Task.Delay(200);
        }
        return null;
    }
    static IReadOnlyList<HeadersModel> HFor(string url)
        => HeadersModel.Init(
            ("User-Agent", PhimSexViTo.ChromeUA),
            ("Referer", PhimSexViTo.Origin(url) + "/"));
    async Task<string> ResolveAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;
        string memKey = ipkey($"phimsexvi:stream:{uri}");
        if (hybridCache.TryGetValue(memKey, out string cached)
            && !string.IsNullOrEmpty(cached))
            return cached;
        string detailHtml = await FetchHtmlAsync(uri);
        if (string.IsNullOrEmpty(detailHtml))
            return null;
        string videoId = PhimSexViTo.ExtractVideoId(detailHtml);
        if (string.IsNullOrEmpty(videoId))
            return null;
        string embedUrl = $"{PhimSexViTo.EmbedHost}/embed/{videoId}/p1?logo=10&autoplay=1";
        string embedHtml = await FetchHtmlAsync(embedUrl);
        if (string.IsNullOrEmpty(embedHtml))
            return null;
        string m3u8Path = PhimSexViTo.ExtractM3u8Path(embedHtml);
        if (string.IsNullOrEmpty(m3u8Path))
            return null;
        string streamUrl = PhimSexViTo.EmbedHost + "/" + m3u8Path;
        if (!await HasSegmentsAsync(streamUrl))
            return null;
        hybridCache.Set(memKey, streamUrl, cacheTime(20));
        return streamUrl;
    }
    async Task<bool> HasSegmentsAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;
        string body = await FetchHtmlAsync(url, seconds: 5);
        return !string.IsNullOrEmpty(body)
            && body.Contains("#EXTINF");
    }
    [HttpGet, Staticache(manually: true)]
    [Route("phimsexvi/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        var url = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(url))
            return OnError("stream_links", refresh_proxy: true);
        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", PhimSexViTo.ChromeUA),
            ("Referer", PhimSexViTo.Origin(url) + "/")));
        return Json(new Dictionary<string, string>()
        {
            ["HLS"] = $"{host}/phimsexvi/video.m3u8"
                + $"?uri={HttpUtility.UrlEncode(uri)}"
        });
    }
    [HttpGet]
    [Route("phimsexvi/video.m3u8")]
    async public Task<ActionResult> Video(string uri)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
        var url = await ResolveAsync(uri);
        if (string.IsNullOrEmpty(url))
            return OnError("stream_links", refresh_proxy: true);
        var headers = httpHeaders(init, HeadersModel.Init(
            ("User-Agent", PhimSexViTo.ChromeUA),
            ("Referer", PhimSexViTo.Origin(url) + "/")));
        return Redirect(HostStreamProxy(url, headers));
    }
}
