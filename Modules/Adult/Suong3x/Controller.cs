using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Suong3x;

public class Suong3xController : BaseSisiController
{
    static readonly SemaphoreSlim menuLock = new(1, 1);

    // cache RAM (object custom / ValueTuple không serialize lại được qua hybridCache)
    static readonly ConcurrentDictionary<string, (DateTime at, string m3u8)> _playerCache = new();
    static readonly ConcurrentDictionary<string, (DateTime at, List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> _detailCache = new();

    static readonly TimeSpan PlayerTtl = TimeSpan.FromMinutes(30);
    static readonly TimeSpan DetailTtl = TimeSpan.FromHours(6);

    public Suong3xController() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("suong3x")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // danh sách "Phim liên quan" (mở từ popup long-press)
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel);

        if (pg < 1)
            pg = 1;

        sort = Suong3xTo.ClampSort(sort);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"suong3x:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            // trang hết dữ liệu (search rỗng / page ngoài phân trang) -> list rỗng, không Fail
            bool soft = !string.IsNullOrWhiteSpace(search)
                || !string.IsNullOrWhiteSpace(c) || pg > 1;

            string body;
            List<PlaylistItem> playlists;
            int pages = 0;

            if (Suong3xTo.IsRestSort(sort))
            {
                // sort thật (modified/title) chỉ có ở WP REST -> cần category/tag id
                int taxId = 0;
                if (!string.IsNullOrWhiteSpace(c))
                {
                    var (kind, slug) = ParseC(c);
                    taxId = await TaxIdAsync(kind, slug);
                    if (taxId == 0)
                        return e.Success((new List<PlaylistItem>(), 0));
                }

                var rest = await GetRestAsync(Suong3xTo.RestUri(init.host, search, c, pg, sort, taxId));
                body = rest.body;
                playlists = Suong3xTo.RestPlaylist("suong3x/vidosik", body);
                // header thiếu (proxy/cache lạ) thì vẫn cho đi tiếp nếu trang đầy
                pages = rest.pages > 0 ? rest.pages
                    : (playlists.Count >= Suong3xTo.RestPageSize ? pg + 1 : 0);            }
            else
            {
                body = await GetBodyAsync(Suong3xTo.Uri(init.host, search, c, pg));
                playlists = Suong3xTo.Playlist("suong3x/vidosik", body);
                pages = Suong3xTo.Pages(body);
            }

            if (body == null)
            {
                return soft
                    ? e.Success((new List<PlaylistItem>(), 0))
                    : e.Fail("playlists", refresh_proxy: true);
            }

            return e.Success((playlists, pages));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await menuTask, total_pages: cache.Value.total_pages);
    }

    static (string kind, string slug) ParseC(string c)
    {
        c = c.Trim('/');
        int slash = c.IndexOf('/');
        if (slash <= 0)
            return ("cat", c);

        return (c.Substring(0, slash).ToLowerInvariant(), c.Substring(slash + 1));
    }

    async Task<string> GetBodyAsync(string url, string referer = null)
    {
        try
        {
            string body = null;
            await httpHydra.GetSpan(url, span =>
            {
                body = span.ToString();
            }, addheaders: HeadersModel.Init(
                ("User-Agent", Suong3xTo.ChromeUA),
                ("Referer", referer ?? (Suong3xTo.SiteHost + "/"))
            ));

            return body;
        }
        catch { return null; }
    }

    // REST chỉ trả 20 item/trang -> tổng số trang nằm ở header X-WP-TotalPages
    // (httpHydra.GetSpan không đọc được header nên phải dùng BaseGetReaderAsync)
    async Task<(string body, int pages)> GetRestAsync(string url)
    {
        try
        {
            string body = null;

            var (ok, resp) = await Http.BaseGetReaderAsync(async e =>
            {
                using var reader = new System.IO.StreamReader(e.stream, System.Text.Encoding.UTF8);
                body = await reader.ReadToEndAsync();
            }, url, timeoutSeconds: 20, headers: HeadersModel.Init(
                ("User-Agent", Suong3xTo.ChromeUA),
                ("Referer", Suong3xTo.SiteHost + "/")
            ), statusCodeOK: true, proxy: proxy);

            if (!ok || resp == null)
                return (null, 0);

            int pages = 0;
            if (resp.Headers.TryGetValues("X-WP-TotalPages", out var v) && int.TryParse(v.FirstOrDefault(), out int p))
                pages = p;
            else if (resp.Headers.TryGetValues("X-WP-Total", out var t) && int.TryParse(t.FirstOrDefault(), out int total))
                pages = (int)Math.Ceiling(total / (double)Suong3xTo.RestPageSize);

            return (body, pages);
        }
        catch { return (null, 0); }
    }

    #region MENU
    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c, bool related = false)
    {
        // "related" = danh sách gợi ý -> chỉ dòng 1 (Tìm kiếm) + menu base, ẩn dòng sắp xếp
        var res = related
            ? new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/suong3x"
                }
            }
            : Suong3xTo.MenuHead(host, search, sort, c);

        res.AddRange(await MenuBaseAsync());
        return res;
    }

    // Danh mục + thẻ lấy động từ WP REST (cache 12 giờ)
    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("suong3x:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        if (!await menuLock.WaitAsync(3000))
            return new List<MenuItem>();

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;

            var bodies = await Task.WhenAll(
                GetBodyAsync(Suong3xTo.RestCategoriesUri(init.host)),
                GetBodyAsync(Suong3xTo.RestTagsUri(init.host)));

            var cats = Suong3xTo.Taxonomy(bodies[0])
                .Where(x => !string.Equals(x.slug, "uncategorized", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var tags = Suong3xTo.Taxonomy(bodies[1]);

            var menu = Suong3xTo.Menu(host, cats, tags);
            if (menu.Count > 0)
                hybridCache.Set(memKey, menu, cacheTime(720));

            Console.WriteLine($"Suong3x: menu cats={cats.Count} tags={tags.Count}");
            return menu;
        }
        catch { return new List<MenuItem>(); }
        finally { menuLock.Release(); }
    }

    // slug -> taxonomy id (cho sort qua REST)
    async Task<int> TaxIdAsync(string kind, string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return 0;

        string memKey = ipkey($"suong3x:taxid:{kind}:{slug}");
        if (hybridCache.TryGetValue(memKey, out int cached) && cached > 0)
            return cached;

        string body = await GetBodyAsync(Suong3xTo.RestTaxUri(init.host, kind, slug));
        int id = Suong3xTo.TaxId(body);

        if (id > 0)
            hybridCache.Set(memKey, id, cacheTime(720));

        return id;
    }
    #endregion

    #region PLAYER
    static bool IsDetailUrl(string uri)
        => !string.IsNullOrWhiteSpace(uri)
            && uri.Contains("phimsexsuong3x.net/", StringComparison.OrdinalIgnoreCase);

    // detail -> meta embedURL -> trang embed (kèm Referer) -> "file":"...m3u8"
    async Task<string> M3u8Async(string uri)
    {
        if (!IsDetailUrl(uri))
            return null;

        if (_playerCache.TryGetValue(uri, out var hit)
            && DateTime.UtcNow - hit.at < PlayerTtl && !string.IsNullOrEmpty(hit.m3u8))
            return hit.m3u8;

        string html = await GetBodyAsync(uri);
        string embed = Suong3xTo.EmbedUrl(html);
        if (string.IsNullOrEmpty(embed))
            return null;

        string embedHtml = await GetBodyAsync(embed, Suong3xTo.SiteHost + "/");
        string m3u8 = Suong3xTo.StreamFromEmbed(embedHtml);
        if (string.IsNullOrEmpty(m3u8))
            return null;

        _playerCache[uri] = (DateTime.UtcNow, m3u8);
        return m3u8;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("suong3x/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string m3u8 = await M3u8Async(uri);
        if (string.IsNullOrEmpty(m3u8))
            return OnError("stream_links", refresh_proxy: true);

        return Json(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "HLS", $"{host}/suong3x/video?uri={HttpUtility.UrlEncode(uri)}&q=HLS" }
        });
    }

    [HttpGet]
    [Route("suong3x/video")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        string m3u8 = await M3u8Async(uri);
        if (string.IsNullOrEmpty(m3u8))
            return OnError("stream_links", refresh_proxy: true);

        return Redirect(HostStreamProxy(m3u8));
    }
    #endregion

    #region POPUP (long-press)
    [HttpGet]
    [Route("suong3x/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (!IsDetailUrl(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        var (chips, rel) = await DetailInfoAsync(uri);
        if (chips == null)
            return Json(new { items = new object[0], groups = new object[0] });

        var groups = new List<object>();

        foreach (string kind in new[] { "cat", "tag" })
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/suong3x?c={kind}/{HttpUtility.UrlEncode(x.slug)}"
            }).ToList();

            if (items.Count == 0)
                continue;

            groups.Add(new { title = kind == "cat" ? "Danh mục" : "Thẻ", items });
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
                        url = $"{host}/suong3x?rel={HttpUtility.UrlEncode(uri)}"
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    // 1 fetch detail -> chips (cat/tag) + "Phim Liên Quan" (cache 6h)
    async Task<(List<(string kind, string slug, string name)> chips, List<PlaylistItem> rel)> DetailInfoAsync(string uri)
    {
        if (_detailCache.TryGetValue(uri, out var hit)
            && DateTime.UtcNow - hit.at < DetailTtl && hit.chips != null)
            return (hit.chips, hit.rel);

        string html = await GetBodyAsync(uri);
        var chips = Suong3xTo.Chips(html ?? "");
        var rel = Suong3xTo.PlaylistRelated("suong3x/vidosik", html ?? "");

        if (html != null)
            _detailCache[uri] = (DateTime.UtcNow, chips, rel);

        return (chips, rel);
    }

    async Task<ActionResult> RelatedList(string rel)
    {
        var (_, playlists) = await DetailInfoAsync(rel);

        if (playlists == null || playlists.Count == 0)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        return PlaylistResult(playlists, false,
            await MenuAsync(null, null, null, related: true), total_pages: 1);
    }
    #endregion
}
