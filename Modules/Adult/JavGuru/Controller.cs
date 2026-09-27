using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace JavGuru;

public class JavGuruController : BaseSisiController
{
    public JavGuruController() : base(ModInit.conf) { }

    // Cloudflare ben jav.guru chan client khong giong trinh duyet (HttpClient .NET
    // va urllib deu 403). Thu httpHydra truoc cho qua proxy, khong co marker thi
    // lui ve curl — curl la thu duoc xac nhan chay duoc tren may nay.
    static long Ms() => Environment.TickCount64;

    async Task<string> FetchHtmlAsync(string url, string marker, int attempts = 3, int maxTime = 25, long deadline = 0)
    {
        long tr = Ms();
        string html = null;

        try
        {
            await httpHydra.GetSpan(url, span =>
            {
                html = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", JavGuruTo.ChromeUA),
                ("Referer", "https://jav.guru/")
            ));
        }
        catch { }

        Console.WriteLine($"JavGuru: hydra ({Ms() - tr}ms) len={html?.Length ?? 0}");

        if (!string.IsNullOrEmpty(html) && (string.IsNullOrEmpty(marker) || html.Contains(marker)))
            return html;

        tr = Ms();
        string body = await JavGuruTo.CurlGetRetry(url, "https://jav.guru/", marker, attempts, maxTime, deadline);
        Console.WriteLine($"JavGuru: curl ({Ms() - tr}ms) len={body?.Length ?? 0}");

        return body;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("javguru")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var cache = await InvokeCacheResult(ipkey($"javguru:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(JavGuruTo.Uri(init.host, search, c, pg), "<div class=\"inside-article\">");
            if (string.IsNullOrEmpty(html))
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            var playlists = JavGuruTo.Playlist("javguru/vidosik", html);
            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, JavGuruTo.Menu(host));
    }

    // Danh sach server cua video, doc tu trang detail. Cache 15 phut de khi
    // nguoi dung bam server thu hai thi khong tai lai trang detail.
    async Task<List<JavGuruServer>> DetailServersAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string memKey = ipkey($"javguru:servers:{uri}");
        if (hybridCache.TryGetValue(memKey, out List<JavGuruServer> cached) && cached != null && cached.Count > 0)
            return cached;

        // Timeout ngan + nhieu lan thu: lan thanh cong chi 0.3-0.5s, con lan
        // treo an het --max-time. Deadline 20s de con duoi tran 30s cua client.
        long deadline = Ms() + 20000;
        string detail = await FetchHtmlAsync(JavGuruTo.NormalizePageUrl(uri), "wp-btn-iframe", 4, 4, deadline);
        if (string.IsNullOrEmpty(detail))
            return null;

        var servers = JavGuruTo.Servers(detail);
        if (servers.Count == 0)
            return null;

        // Thu tu uu tien: TV (turbo) da xac nhan chay duoc; JK (maxstream) va
        // LU (lulustream) du phong. SB/VO/DD xep sau — chua kiem chung.
        servers = servers
            .Select((s, i) => new { s, i })
            .OrderBy(x => Priority(x.s.Label))
            .ThenBy(x => x.i)
            .Select(x => x.s)
            .ToList();

        hybridCache.Set(memKey, servers, cacheTime(15));
        return servers;
    }

    static int Priority(string label)
    {
        if (label.IndexOf("TV", StringComparison.OrdinalIgnoreCase) >= 0)
            return 0;
        if (label.IndexOf("JK", StringComparison.OrdinalIgnoreCase) >= 0)
            return 1;
        if (label.IndexOf("LU", StringComparison.OrdinalIgnoreCase) >= 0)
            return 2;
        return 3;
    }

    // KHONG resolve tai day. Truoc day thu 2-3 server lien tiep ton 20-30s, app
    // bao het thoi gian va popup khong bao gio mo. Nay /vidosik chi tra danh
    // sach server de app hien ra, server nao nguoi dung BAM moi resolve — chi
    // mot server nen 2-13s, luon duoi tran 30s cua client.
    [HttpGet, Staticache(manually: true)]
    [Route("javguru/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Route co duoi .m3u8: app chi bat hls.js khi URL item chua ".m3u8".
        return Json(servers.ToDictionary(
            s => s.Label,
            s => $"{host}/javguru/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&srv={HttpUtility.UrlEncode(s.Label)}"));
    }

    // Resolve DUNG MOT server theo `srv`, roi chuyen tiep sang link phat.
    // Khong thu server khac: mot server resolve het 2-13s la du, thu them se
    // vuot 30s cua client.
    [HttpGet]
    [Route("javguru/video")]
    [Route("javguru/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string srv, string q = null)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        // Cho phep q = ten server (tu cache cu) de URL da bookmark/da mo van chay
        if (string.IsNullOrEmpty(srv))
            srv = q;

        var servers = await DetailServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        var pick = servers.FirstOrDefault(x => string.Equals(x.Label, srv, StringComparison.OrdinalIgnoreCase));
        if (pick == null)
            return OnError("stream_links", refresh_proxy: true);

        // Deadline 26s: chi mot server nen chua het han client 30s.
        long started = Ms();
        long deadline = started + 26000;

        string gateway = JavGuruTo.GatewayUrl(pick.PageUrl);
        if (string.IsNullOrEmpty(gateway))
            return OnError("stream_links", refresh_proxy: true);

        long tr = Ms();
        var streams = await JavGuruTo.Streams(gateway, "https://jav.guru/", deadline, JavGuruTo.ServerReferer(pick.Label));
        Console.WriteLine($"JavGuru: srv={pick.Label} n={streams.Count} ({Ms() - tr}ms)");

        if (streams.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        // Uu tien variant cao nhat (da duoc tach master o server).
        var best = streams.OrderByDescending(x => x.tag.Length).First();
        Console.WriteLine($"JavGuru: chon srv={pick.Label} tag={best.tag} host={new Uri(best.url).Host}");

        // Header theo server: JK (maxstream) can Referer cua no, turbo tra 429
        // neu thay Referer jav.guru.
        var headers = httpHeaders(init, JavGuruTo.StreamHeaders(pick.Label));

        return Redirect(HostStreamProxy(best.url, headers));
    }
}
