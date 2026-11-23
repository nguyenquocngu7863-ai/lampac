using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Phe69;

public class Phe69Controller : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();

    public Phe69Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("phe69")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // Danh sach "Video lien quan" tu popup long-press.
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

    rhubFallback:
        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"phe69:v2:{search}:{c}:{pg}"), 10, async e =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);

            List<PlaylistItem> playlists = null;
            int pages = 1;

            string url = Phe69To.Uri(init.host, search, c, pg);

            await httpHydra.GetSpan(url, span =>
            {
                string html = span.ToString();
                playlists = Phe69To.Playlist("phe69/vidosik", html);
                pages = Phe69To.Pages(html);
            });

            // Lan dau sau luc restart httpHydra doi khi tra trang that bai -> cache loi
            // 10s. Thu lai 1 lan ngay trong lambda truoc khi e.Fail.
            if (playlists == null || playlists.Count == 0)
            {
                await Task.Delay(800);
                playlists = null;
                await httpHydra.GetSpan(url, span =>
                {
                    string html = span.ToString();
                    playlists = Phe69To.Playlist("phe69/vidosik", html);
                    pages = Phe69To.Pages(html);
                });
            }

            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: string.IsNullOrEmpty(search));

            return e.Success((playlists, pages));
        });

        if (IsRhubFallback(cache))
            goto rhubFallback;

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            Phe69To.Menu(host), total_pages: cache.Value.total_pages);
    }

    async Task<(Dictionary<string, string> links, bool userch)> ResolveLinksAsync(string uri)
    {
        SemaphorManager semaphore = null;
        string semaphoreKey = $"phe69:view:v3:{uri}";

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

                string url = uri;
                if (uri.StartsWith("/"))
                    url = $"https://phe69.shop{uri}";
                else if (!uri.StartsWith("http"))
                    url = $"https://phe69.shop/{uri}";

                string iframe = null;

                await httpHydra.GetSpan(url, span =>
                {
                    iframe = Phe69To.GetIframe(span.ToString());
                });

                if (string.IsNullOrEmpty(iframe))
                {
                    // httpHydra doi khi nhan ban trang KHONG co iframe (khac curl_cffi).
                    // Fallback HLS theo slug (da doi tren 2 trang /data/<token> that).
                    string slugFb = uri?.TrimEnd('/').Split('/')[^1];
                    if (!string.IsNullOrEmpty(slugFb) && slugFb.Length > 4)
                    {
                        cache.links = new Dictionary<string, string>()
                        {
                            ["Auto"] = $"https://hlshd.phe69.shop/hls_videos/{slugFb}/index.m3u8"
                        };
                        proxyManager?.Success();
                        cache.userch = rch?.enable == true;
                        hybridCache.Set(memKey, cache, cacheTime(20));
                        return (cache.links, cache.userch);
                    }
                    return (null, false);
                }

                string playerHtml = null;

                await httpHydra.GetSpan(iframe, span =>
                {
                    playerHtml = span.ToString();
                }, addheaders: HeadersModel.Init(
                    ("referer", "https://phe69.shop/")
                ));

                if (string.IsNullOrEmpty(playerHtml))
                    return (null, false);

                System.Console.WriteLine($"Phe69: playerHtml len={playerHtml.Length} haspack={playerHtml.Contains("eval(function(p,a,c")} iframe={iframe}");
                // Uu tien co che moi (`hlsSource`), fallback packer a=62 cua cu.
                cache.links = Phe69To.PlayerSources(playerHtml);
                if (cache.links == null || cache.links.Count == 0)
                    cache.links = Phe69To.StreamLinks(Phe69To.UnpackDeanEdwards(playerHtml));

                if ((cache.links == null || cache.links.Count == 0) && !string.IsNullOrEmpty(uri))
                {
                    string slug = uri.TrimEnd('/').Split('/')[^1];
                    if (!string.IsNullOrEmpty(slug))
                    {
                        System.Console.WriteLine($"Phe69: unpack empty, fallback slug guess {slug}");
                        cache.links = new Dictionary<string, string>()
                        {
                            ["Auto"] = $"https://phim.phe69.uk/{slug}.mp4"
                        };
                    }
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
    [Route("phe69/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

    reset:
        var (links, userch) = await ResolveLinksAsync(uri);

        // httpHydra doi khi nhan ban trang chi tiet KHONG co iframe (khac curl_cffi),
        // → GetIframe null. Fallback: URL HLS suy ra slug, da doi chinh xac tren
        // 2 trang /data/<token> that cua site (hlshd.phe69.shop/hls_videos/<slug>/index.m3u8).
        if ((links == null || links.Count == 0) && !string.IsNullOrWhiteSpace(uri))
        {
            string slug = uri.TrimEnd('/').Split('/')[^1];
            if (slug.Length > 4)
                links = new Dictionary<string, string>()
                {
                    ["Auto"] = $"https://hlshd.phe69.shop/hls_videos/{slug}/index.m3u8"
                };
        }

        if (links == null || links.Count == 0)
        {
            if (IsRhubFallback())
                goto reset;

            return OnError("stream_links", refresh_proxy: true);
        }

        if (userch)
            return OnResult(links);

        // Link gốc là mp4 trực tiếp, KHÔNG phải HLS: trả qua route /video
        // Co chế moi cua site tra HLS (hlshd.../index.m3u8) -> bat buoc duoi .m3u8.
        // (Ghi chu cu: "khong duoi .mp4 de app phat native" chi dung khi fallback mp4,
        //  hien khong con dung.)
        return Json(links.ToDictionary(k => k.Key, v =>
            // Bat buoc co duoi .m3u8: app chon player theo duoi URL.
            // Route /phe69/video (body HLS) bi coi la file thuong -> "no supported source".
            $"{host}/phe69/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(v.Key)}"));
    }

    [HttpGet]
    [Route("phe69/video")]
    [Route("phe69/video.m3u8")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var (links, _) = await ResolveLinksAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("referer", "https://phe69.shop/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }

    #region info (popup long-press) — chip breadcrumb/tag + video lien quan
    static readonly Dictionary<string, (DateTime at, List<(string kind, string path, string name)> chips, List<PlaylistItem> rel)> _infoCache = new();
    static readonly TimeSpan InfoTtl = TimeSpan.FromHours(6);

    [HttpGet]
    [Route("phe69/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        var empty = Json(new { items = new object[0], groups = new object[0] });

        string pageUrl = Phe69To.SafeDetailUrl(uri);
        if (string.IsNullOrEmpty(pageUrl))
            return empty;

        lock (_infoCache)
        {
            if (_infoCache.TryGetValue(pageUrl, out var hit) && DateTime.UtcNow - hit.at < InfoTtl)
                return InfoResult(hit.chips, hit.rel, uri);
        }

        string html = null;
        try
        {
            await httpHydra.GetSpan(pageUrl, span => { html = span.ToString(); });
        }
        catch { }

        if (string.IsNullOrEmpty(html))
            return empty;

        var chips = Phe69To.Chips(html);
        var rel = Phe69To.Related("phe69/vidosik", html);

        lock (_infoCache)
        {
            _infoCache[pageUrl] = (DateTime.UtcNow, chips, rel);
            if (_infoCache.Count > 300)
            {
                var cutoff = DateTime.UtcNow - InfoTtl;
                foreach (string k in _infoCache.Where(x => x.Value.at < cutoff).Select(x => x.Key).ToList())
                    _infoCache.Remove(k);
            }
        }

        return InfoResult(chips, rel, uri);
    }

    ActionResult InfoResult(
        List<(string kind, string path, string name)> chips,
        List<PlaylistItem> rel, string uri)
    {
        var groups = new List<object>();

        foreach (var (kind, title) in new[]
        {
            ("actor", "Diễn viên"), ("cat", "Thể loại"), ("tag", "Thẻ")
        })
        {
            var items = chips?.Where(x => x.kind == kind)
                .Select(x => new
                {
                    title = x.name,
                    url = $"{host}/phe69?c={HttpUtility.UrlEncode(x.path.Trim('/'))}"
                }).ToList();

            if (items == null || items.Count == 0)
                continue;

            groups.Add(new { title, items });
        }

        if (rel != null && rel.Count > 0)
        {
            groups.Add(new
            {
                title = "Liên quan",
                items = new[]
                {
                    new
                    {
                        title = "Video liên quan",
                        plain = true,
                        url = $"{host}/phe69?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        // SISI hieu total_pages:0 = "con trang vo han" -> phai tra 1.
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = Phe69To.SafeDetailUrl(rel);
        if (string.IsNullOrEmpty(pageUrl))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"phe69:rel:{pageUrl}"), 180, async e =>
        {
            string html = null;
            try
            {
                await httpHydra.GetSpan(pageUrl, span => { html = span.ToString(); });
            }
            catch { }

            var playlists = Phe69To.Related("phe69/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((playlists, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            Phe69To.Menu(host), total_pages: 1);
    }
    #endregion
}
