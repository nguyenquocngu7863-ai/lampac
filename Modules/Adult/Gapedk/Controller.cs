using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace Gapedk;

public class GapedkController : BaseSisiController
{
    public GapedkController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("gapedk")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        // Site khong phan trang trang search (42 ket qua, khong co pager)
        if (!string.IsNullOrWhiteSpace(search) && pg > 1)
            return PlaylistResult(new List<PlaylistItem>(), false, await MenuAsync());

        var cache = await InvokeCacheResult(ipkey($"gapedk:{search}:{c}:{pg}"), 10, jsonContext.ListPlaylistItem, async e =>
        {
            string html = await FetchHtmlAsync(GapedkTo.Uri(init.host, search, c, pg));

            var playlists = GapedkTo.Playlist("gapedk/vidosik", html ?? "");

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success(playlists);
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache, await MenuAsync());
    }

    async Task<string> FetchHtmlAsync(string url, string referer = null)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        try
        {
            return await httpHydra.Get(url, addheaders: HeadersModel.Init(
                ("User-Agent", GapedkTo.ChromeUA),
                ("Referer", referer ?? GapedkTo.SiteHost + "/"),
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"),
                ("Accept-Language", "en-US,en;q=0.9")
            ), statusCodeOK: false);
        }
        catch
        {
            return null;
        }
    }

    // ===== menu: taxonomy lay dong tu trang chu, cache 6 gio =====
    async Task<List<MenuItem>> MenuAsync()
    {
        string key = ipkey("gapedk:tax");

        if (!hybridCache.TryGetValue(key, out (List<(string slug, string name)> genres, List<(string slug, string name)> studios) tax)
            || tax.genres == null)
        {
            string html = await FetchHtmlAsync(GapedkTo.SiteHost + "/");

            tax.genres = GapedkTo.Taxonomy(html, "genre");
            tax.studios = GapedkTo.Taxonomy(html, "director");

            if (tax.genres.Count > 0)
                hybridCache.Set(key, tax, cacheTime(360));
        }

        return GapedkTo.Menu(host, tax.genres, tax.studios);
    }

    // ===== /vidosik: chi liet ke server, KHONG resolve =====
    async Task<Dictionary<string, string>> ServersAsync(string uri)
    {
        string memKey = ipkey($"gapedk:servers:v3:{uri}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache))
            return cache;

        string pageUrl = uri;
        if (pageUrl.StartsWith("/"))
            pageUrl = GapedkTo.SiteHost + pageUrl;

        string html = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return null;

        var servers = new Dictionary<string, string>();
        foreach (var (fn, id) in GapedkTo.Players(html))
        {
            string label = GapedkTo.PlayerLabel(fn);
            string embed = GapedkTo.EmbedUrl(fn, id);
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(embed))
                continue;   // server chua resolve duoc -> an

            servers.TryAdd(label, embed);
        }

        if (servers.Count == 0)
            return null;

        hybridCache.Set(memKey, servers, cacheTime(30));
        return servers;
    }

    // ===== /video: resolve dung 1 server user bam =====
    async Task<string> StreamAsync(string uri, string q)
    {
        string memKey = ipkey($"gapedk:stream:v3:{uri}:{q}");
        if (hybridCache.TryGetValue(memKey, out string cache))
            return cache;

        var servers = await ServersAsync(uri);
        if (servers == null || !servers.TryGetValue(q, out string embed))
            return null;

        string link;

        if (embed.Contains("playmogo.com", StringComparison.OrdinalIgnoreCase))
            link = await DoodCffiAsync(embed);
        else if (embed.Contains("voe.sx", StringComparison.OrdinalIgnoreCase))
            link = await VoeResolveAsync(embed);
        else
            link = null;

        if (string.IsNullOrEmpty(link))
            return null;

        hybridCache.Set(memKey, link, cacheTime(5));
        return link;
    }

    // DoodStream chan TLS cua .NET (Cloudflare "Just a moment...") — di bang
    // curl_cffi (TLS Chrome that) qua 1 process python: embed -> pass_md5 -> mp4.
    async Task<string> DoodCffiAsync(string embedUrl)
    {
        try
        {
            string script = Path.Combine(Path.GetTempPath(), "gapedk_dood.py");

            if (!System.IO.File.Exists(script) || new FileInfo(script).Length < 200)
                System.IO.File.WriteAllText(script, DoodSource);

            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            psi.Environment.Remove("LD_PRELOAD");
            psi.Environment.Remove("LD_LIBRARY_PATH");

            string py = "/data/data/com.termux/files/usr/bin/python3";
            psi.FileName = System.IO.File.Exists(py) ? py : "python3";
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add(embedUrl);
            psi.ArgumentList.Add(GapedkTo.SiteHost + "/");

            using var p = Process.Start(psi);
            if (p == null)
                return null;

            string stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();

            stdout = stdout?.Trim();
            if (p.ExitCode != 0 || string.IsNullOrEmpty(stdout)
                || !stdout.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return null;

            return stdout;
        }
        catch
        {
            return null;
        }
    }

    // VOE: HTML bi jscrambler hoa, khong decode tinh duoc. Player (jwplayer)
    // chi goi master.m3u8 SAU khi JS chay -> mo bang Chrome that roi bat request
    // m3u8 dau tien. VOE tu redirect voe.sx -> mirror (arianathankspecial...).
    async Task<string> VoeResolveAsync(string embedUrl)
    {
        try
        {
            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = GapedkTo.ChromeUA,
                ["Referer"] = GapedkTo.SiteHost + "/"
            };

            using var browser = new Shared.PlaywrightCore.PlaywrightBrowser();
            IPage page = await browser.NewPageAsync("Gapedk", headers, keepopen: false);
            if (page == null)
                return null;

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            page.Request += (_, req) =>
            {
                string u = req.Url;
                if (!string.IsNullOrEmpty(u)
                    && u.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
                    && !u.Contains("jwplayer", StringComparison.OrdinalIgnoreCase))
                {
                    tcs.TrySetResult(u);
                }
            };

            await page.GotoAsync(embedUrl, new PageGotoOptions
            {
                Timeout = 25000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            if (await Task.WhenAny(tcs.Task, Task.Delay(18000)) == tcs.Task)
                return tcs.Task.Result;

            // co trang phai bam play moi tai manifest
            try { await page.Mouse.ClickAsync(280, 260); } catch { }

            if (await Task.WhenAny(tcs.Task, Task.Delay(14000)) == tcs.Task)
                return tcs.Task.Result;

            return null;
        }
        catch
        {
            return null;
        }
    }

    const string DoodSource = """
import sys, re, time, random, string
from curl_cffi import requests

embed = sys.argv[1]
site = sys.argv[2] if len(sys.argv) > 2 else embed

try:
    s = requests.Session(impersonate="chrome124")
    r = s.get(embed, headers={"Referer": site}, timeout=20)
    h = r.text or ""

    m = re.search(r"get\('(/pass_md5/[^']+)'", h)
    t = re.search(r"\?token=([A-Za-z0-9]+)&expiry", h)
    if not m or not t:
        sys.exit(1)

    root = re.match(r"^https?://[^/]+", embed).group(0)
    d = s.get(root + m.group(1), headers={"Referer": embed}, timeout=20).text.strip()
    if not d.startswith("http"):
        sys.exit(1)

    rnd = "".join(random.choice(string.ascii_letters + string.digits) for _ in range(10))
    print(d.rstrip("/") + rnd + "?token=" + t.group(1) + "&expiry=" + str(int(time.time() * 1000)))
except Exception:
    sys.exit(1)
""";

    [HttpGet, Staticache(manually: true)]
    [Route("gapedk/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var servers = await ServersAsync(uri);
        if (servers == null || servers.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(servers.ToDictionary(k => k.Key, v =>
            $"{host}/gapedk/video?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("gapedk/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        if (string.IsNullOrEmpty(q))
            return OnError("stream_links", refresh_proxy: true);

        string link = await StreamAsync(uri, q);
        if (string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var headers = link.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? httpHeaders(init, HeadersModel.Init(
                ("User-Agent", GapedkTo.ChromeUA),
                ("Referer", "https://voe.sx/")
            ))
            : httpHeaders(init, HeadersModel.Init(
                ("User-Agent", GapedkTo.ChromeUA),
                ("Referer", "https://playmogo.com/")
            ));
        return Redirect(HostStreamProxy(link, headers));
    }

    // ===== popup menu (long-press) =====
    [HttpGet]
    [Route("gapedk/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = Json(new { items = new object[0], groups = new object[0] });
        if (string.IsNullOrEmpty(uri))
            return empty;

        string cacheKey = ipkey($"gapedk:info:{uri}");
        if (hybridCache.TryGetValue(cacheKey, out (List<(string kind, string slug, string name)> chips, bool related) hit)
            && hit.chips != null)
        {
            return InfoResult(hit.chips, hit.related, uri);
        }

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? uri : init.host + uri;
        string html = await FetchHtmlAsync(pageUrl);
        if (string.IsNullOrEmpty(html))
            return empty;

        var chips = new List<(string kind, string slug, string name)>();

        foreach (var g in GapedkTo.Links(html, "genre", "peligen"))
            chips.Add(("genre", g.slug, g.name));

        foreach (var s in GapedkTo.Links(html, "director", "pelinfoe"))
            chips.Add(("director", s.slug, s.name));

        foreach (var p in GapedkTo.Links(html, "pornstar", "pelinfop"))
            chips.Add(("pornstar", p.slug, p.name));

        bool related = GapedkTo.Playlist("gapedk/vidosik", html).Count > 0;

        if (chips.Count > 0)
            hybridCache.Set(cacheKey, (chips, related), cacheTime(360));

        return InfoResult(chips, related, uri);
    }

    static string KindLabel(string kind) => kind switch
    {
        "genre" => "Thể loại",
        "director" => "Hãng phim",
        "pornstar" => "Diễn viên",
        _ => kind
    };

    static int KindRank(string kind) => kind switch
    {
        "genre" => 0,
        "director" => 1,
        "pornstar" => 2,
        _ => 99
    };

    ActionResult InfoResult(List<(string kind, string slug, string name)> chips, bool related, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct()
                     .OrderBy(KindRank).ThenBy(x => x))
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/gapedk?c={HttpUtility.UrlEncode(kind + "/" + x.slug)}"
            }).ToList();

            if (items.Count == 0) continue;

            groups.Add(new { title = KindLabel(kind), items });
        }

        if (related)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new
                    {
                        title = "Phim liên quan",
                        plain = true,
                        url = $"{host}/gapedk?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"gapedk:rel:{rel}"), 180, async e =>
        {
            string url = rel.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? rel : init.host + rel;
            string html = await FetchHtmlAsync(url);

            var playlists = GapedkTo.Playlist("gapedk/vidosik", html);
            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 0));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(), total_pages: 1);
    }
}
