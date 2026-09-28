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

namespace JavHD;

public class JavHDController : BaseSisiController
{
    static readonly HttpClient httpClient =
        FriendlyHttp.CreateHttpClient();

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

    static long Ms() => Environment.TickCount64;

    // Trang detail: tach data-embed (base64 url don) + data-embeds
    // (base64 json array du phong). Cache 15 phut de bam server thu
    // hai khong tai lai trang detail.
    async Task<List<JavHDServer>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string memKey = ipkey($"javhd:servers:{uri}");
        if (hybridCache.TryGetValue(memKey,
            out List<JavHDServer> cached)
            && cached != null && cached.Count > 0)
            return cached;

        string pageUrl = uri.StartsWith("/")
            ? JavHDTo.SiteHost + uri : uri;

        long deadline = Ms() + 20000;
        string detail = await JavHDTo.CurlGetRetry(pageUrl,
            JavHDTo.SiteHost + "/", "data-embed", 4, 4, deadline);
        if (string.IsNullOrEmpty(detail))
            return null;

        var servers = JavHDTo.Servers(detail);
        if (servers.Count == 0)
            return null;

        hybridCache.Set(memKey, servers, cacheTime(15));
        return servers;
    }

    // KHONG resolve tai day. /vidosik chi tra danh sach server de app
    // hien ra, server nao nguoi dung BAM moi resolve — chi mot server
    // nen 2-13s, luon duoi tran 30s cua client.
    [HttpGet, Staticache(manually: true)]
    [Route("javhd/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var all = await DetailServersAsync(uri);
        if (all == null || all.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Lay het server ma trang detail co, chi bo server chua xu ly.
        var servers = all.Where(x =>
            JavHDTo.IsSupported(x.Label)).ToList();
        if (servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Kind biet ngay tu host (Dood=mp4, Cloud=HLS). Rieng Turbo
        // DA DANG nen do song song, deadline 6s.
        int total = servers.Count;
        var kinds = new string[total];
        long kindDl = Ms() + 6000;

        async Task Probe(int i)
        {
            string u = servers[i].PageUrl;
            if (JavHDTo.IsDood(u) || JavHDTo.IsCloud(u)
                || JavHDTo.IsJavhdz(u))
            {
                kinds[i] = servers[i].Kind;
                return;
            }

            kinds[i] = await JavHDTo.ServerKindAsync(
                u, 4, kindDl);
        }

        await Task.WhenAll(
            Enumerable.Range(0, total).Select(Probe));

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < total; i++)
        {
            if (kinds[i].Length > 0)
                servers[i].Kind = kinds[i];

            // Nho dang da do de /video fallback khong do lai.
            hybridCache.Set(
                ipkey($"javhd:kind:{uri}:{servers[i].Label}"),
                servers[i].Kind ?? "",
                cacheTime(15));

            if (dict.ContainsKey(servers[i].Label))
                continue;

            string tail = servers[i].Kind == JavHDTo.KindMp4
                ? ".mp4" : ".m3u8";
            dict[servers[i].Label] = $"{host}/javhd/video{tail}"
              + $"?uri={HttpUtility.UrlEncode(uri)}"
              + $"&srv={HttpUtility.UrlEncode(servers[i].Label)}";
        }

        hybridCache.Set(
            ipkey($"javhd:servers:{uri}"), servers, cacheTime(15));

        return Json(dict);
    }

    // Resolve DUNG MOT server theo `srv`, roi chuyen tiep sang link.
    async Task<List<(string url, string tag, string referer)>> ResolveAsync(
        JavHDServer pick, long deadline)
    {
        var empty = new List<(string, string, string)>();

        // --- DoodStream: 301 sang host doi -> API pass_md5 -> mp4 ---
        if (JavHDTo.IsDood(pick.PageUrl))
        {
            var (src, refDood) = await JavHDTo.DoodSourceAsync(
                pick.PageUrl, 6, deadline);
            if (string.IsNullOrEmpty(src))
                return empty;

            return new List<(string, string, string)> { (src, "mp4", refDood) };
        }

        string player = await JavHDTo.CurlGetRetry(pick.PageUrl,
            JavHDTo.SiteHost + "/", null, 4, 5, deadline);
        if (string.IsNullOrEmpty(player))
            return empty;

        // --- Cloudwish / Mycloudz: packer -> var links hls4>hls3>hls2 ---
        if (JavHDTo.IsCloud(pick.PageUrl))
        {
            string pHost = null;
            try
            {
                var u = new Uri(pick.PageUrl);
                pHost = u.GetLeftPart(UriPartial.Authority);
            }
            catch { }

            string pref = string.IsNullOrEmpty(pHost)
                ? null : pHost + "/";
            var masters = JavHDTo.CloudMasters(player, pHost);

            foreach (string master in masters)
            {
                var variants = await JavHDTo.MasterVariants(
                    master, 3, 7, deadline, pref);
                if (variants.Count > 0)
                    return variants.Select(x =>
                        (x.url, x.tag, pHost)).ToList();
            }

            // Het master ma khong tach duoc variant nao -> tra
            // master dau, app tu xu ly.
            if (masters.Count > 0)
                return new List<(string, string, string)>
                    { (masters[0], "1080p", pHost) };

            return empty;
        }

        // --- Javhdz (Myserver/Topserver/Maxcloud/Bpserver):
        // embed Plyr -> FIRST.playlist (media 1 level, segment
        // absolute, tien to PNG do hls.js cat o client) ---
        if (JavHDTo.IsJavhdz(pick.PageUrl))
        {
            string pl = JavHDTo.JavhdzPlaylist(player, pick.PageUrl);
            if (string.IsNullOrEmpty(pl))
                return empty;

            return new List<(string, string, string)>
                { (pl, "", null) };
        }

        // --- Turbo: data-hash (m3u8) truoc, urlPlay (mp4) sau ---
        foreach (string media in JavHDTo.StreamUrls(player))
        {
            if (JavHDTo.IsDirectMp4(media))
                return new List<(string, string, string)>
                    { (media, "mp4", null) };

            var variants = await JavHDTo.MasterVariants(media, 3, 7, deadline);
            if (variants.Count > 0)
                return variants.Select(x =>
                    (x.url, x.tag, (string)null)).ToList();

            return new List<(string, string, string)>
                { (media, "1080p", null) };
        }

        return empty;
    }

    [HttpGet]
    [Route("javhdepick.js")]
    public ActionResult EpPick()
    {
        try
        {
            string path = System.IO.Path.Combine(
                ModInit.modpath, "javhdepick.js");
            if (System.IO.File.Exists(path))
                return Content(System.IO.File.ReadAllText(path),
                    "application/javascript; charset=utf-8");
        }
        catch { }
        return NotFound();
    }

    // Resolve server theo `srv` roi chuyen tiep sang link phat; server
    // do chet thi TU THU cac server con lai theo Rank (DoodStream ->
    // Turbo -> Cloudwish -> Mycloudz). Moi server mot sub-deadline nen
    // ca chuoi van duoi tran 30s cua client.
    [HttpGet]
    [Route("javhd/video")]
    [Route("javhd/video.mp4")]
    [Route("javhd/video.m3u8")]
    async public Task<ActionResult> Video(
        string uri, string srv, string q = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(srv))
            srv = q;

        // Cache resolve: an ngay neu da resolve trong 1 phut truoc.
        string streamKey = ipkey($"javhd:stream:{uri}:{srv}");
        if (hybridCache.TryGetValue(streamKey, out string cachedRaw)
            && !string.IsNullOrEmpty(cachedRaw))
        {
            var c = SplitCached(cachedRaw);
            if (!string.IsNullOrEmpty(c.url))
                return Redirect(StreamLink(c.url, c.referer));
        }

        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var pick = servers.FirstOrDefault(x =>
            string.Equals(x.Label, srv, StringComparison.OrdinalIgnoreCase)
            && JavHDTo.IsSupported(x.Label));

        var order = new List<JavHDServer>();
        if (pick != null)
            order.Add(pick);

        // App da chon player theo DUOI cua URL /vidosik. Tra ve nguon
        // KHAC dang thi ton player va app bao "no EXTM3U delimiter" —
        // fallback chi sang server CUNG dang.
        string wantKind = KindOf(uri, pick);
        foreach (var s in servers)
        {
            if (pick != null && s.Label == pick.Label)
                continue;

            string k = KindOf(uri, s);
            if (wantKind.Length == 0 || k.Length == 0 || k == wantKind)
                order.Add(s);
        }

        if (order.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Deadline 20s cho ca chuoi, 12s cho tung server.
        long deadline = Ms() + 20000;

        foreach (var s in order)
        {
            long sub = Math.Min(deadline, Ms() + 12000);
            var streams = await ResolveAsync(s, sub);
            Console.WriteLine(
                $"JavHD: srv={s.Label} n={streams.Count}");

            if (streams.Count == 0)
                continue;

            var best = streams.FirstOrDefault(
                x => !string.IsNullOrEmpty(x.tag));
            if (best.url == null)
                best = streams[0];

            Console.WriteLine(
                $"JavHD: chon srv={s.Label} tag={best.tag}");

            string raw = best.url + "\n" + (best.referer ?? "");
            hybridCache.Set(
                ipkey($"javhd:stream:{uri}:{s.Label}"),
                raw, cacheTime(1));
            hybridCache.Set(streamKey, raw, cacheTime(1));

            return Redirect(StreamLink(best.url, best.referer));
        }

        return OnError("stream_links", refresh_proxy: true);
    }

    // Dang phat cua mot server: uu tien ket qua /vidosik da do
    // (cache 15 phut), khong thi dung dang tinh theo host.
    string KindOf(string uri, JavHDServer s)
    {
        if (s == null)
            return "";

        string key = ipkey($"javhd:kind:{uri}:{s.Label}");
        if (hybridCache.TryGetValue(key, out string k)
            && !string.IsNullOrEmpty(k))
            return k;

        return s.Kind ?? "";
    }

    // Cache luu "url\nreferer" — URL khong bao gio chua '\n'.
    static (string url, string referer) SplitCached(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (null, null);

        int at = raw.IndexOf('\n');
        return at < 0
            ? (raw, null)
            : (raw[..at], raw[(at + 1)..]);
    }

    // DoodStream chi phuc vu khi URL co query VA request co Referer
    // khop host embed; thieu mot thi CDN 302 sang `*.dood.video`
    // (sinkhole 127.0.0.1 ca public DNS). Gan rieng Referer o day vi
    // headers_stream khong co Referer (CDN Turbo 429 khi thay Referer).
    string StreamLink(string url, string referer)
    {
        if (string.IsNullOrEmpty(referer))
            return HostStreamProxy(url, httpHeaders(init));

        var baseHeaders = HeadersModel.InitOrNull(init.headers_stream);
        var hs = new List<HeadersModel>(baseHeaders?.Count + 1 ?? 1);

        if (baseHeaders != null)
            hs.AddRange(baseHeaders);

        hs.Add(new("Referer", referer + "/"));

        return HostStreamProxy(url, hs);
    }
}
