using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
namespace FapClub;
public class FapClubController : BaseSisiController
{
    static readonly HttpClient httpClient = FriendlyHttp.CreateHttpClient();
    static readonly System.Threading.SemaphoreSlim menuLock = new(1, 1);
    public FapClubController() : base(ModInit.conf)
    {
        requestInitialization += () =>
        {
            if (init.httpversion == 1)
                httpHydra.RegisterHttp(httpClient);
        };
    }

    [HttpGet, Staticache]
    [Route("fapclub")]
    async public Task<ActionResult> Index(string c, string search, int pg = 1, string rel = null, string list = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        // popup "Video liên quan" -> 1 trang duy nhất; pg>1 trả shape rỗng (không 503,
        // app thấy total_pages=1 + list rỗng nên không nạp lặp)
        if (!string.IsNullOrWhiteSpace(rel))
        {
            if (pg > 1)
                return EmptyRel();
            return await RelatedList(rel);
        }

        if (pg < 1) pg = 1;

        // list lạ -> bỏ (Clamp trước khi vào Uri/cache key)
        list = FapClubTo.ClampList(list, search, c);

        // menu dòng 1 phải có trong MỌI response playlist (sisi.js đọc json.menu)
        var menuTask = MenuAsync(list);

    rhubFallback:
        var cache = await InvokeCacheResult($"fapclub:{c}:{search}:{list}:{pg}", 60, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;
            await httpHydra.GetSpan(FapClubTo.Uri(init.host, c, search, pg, list), span =>
            {
                playlists = FapClubTo.Playlist(init.host, span);
            });
            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);
            return e.Success(playlists);
        });
        if (IsRhubFallback(cache))
            goto rhubFallback;
        return PlaylistResult(cache, await menuTask);
    }

    [HttpGet]
    [Route("fapclub/menu")]
    async public Task<ActionResult> Menu()
    {
        var categories = await CategoriesAsync();
        List<MenuItem> menu = new List<MenuItem>()
        {
            new MenuItem() { title = "Danh sách", playlist_url = "submenu", submenu = FapClubTo.ListMenu(host) }
        };
        foreach (var batch in ToChunks(categories, 25))
            menu.Add(new MenuItem { title = "Thể loại", playlist_url = "submenu", submenu = new List<MenuItem>(batch) });
        return Json(menu);
    }

    // menu trong playlist:
    //   dòng 1  Tìm kiếm
    //   dòng 2  KHÔNG CÓ — site không có sort (đo 2026-11-22: ?sort_by=* bị nuốt im lặng,
    //           sequence y hệt baseline ở cả 7 biến thể; site cũng không có UI sort nào).
    //           Theo luật "site không có sort thì bỏ hẳn dòng 2".
    //   dòng 3  Danh sách (Mới nhất · Top đánh giá · Phổ biến) — FILTER, tập phim khác nhau
    //   dòng 4  Thể loại (50 category)
    async Task<List<MenuItem>> MenuAsync(string list)
    {
        var menu = new List<MenuItem>()
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/fapclub" }
        };

        menu.Add(new MenuItem()
        {
            title = "Danh sách: " + FapClubTo.ListLabel(list ?? "latest-updates"),
            playlist_url = "submenu",
            submenu = FapClubTo.ListMenu(host)
        });

        var categories = await CategoriesAsync();
        if (categories.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(categories)
            });
        }

        return menu;
    }

    // 50 category lấy từ home, cache 12 giờ (không fetch lại mỗi request playlist)
    async Task<List<MenuItem>> CategoriesAsync()
    {
        const string memKey = "fapclub:menu:v1";

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;

        if (!await menuLock.WaitAsync(3000))
            return new List<MenuItem>();

        try
        {
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;

            List<MenuItem> categories = null;
            await httpHydra.GetSpan(FapClubTo.SiteHost + "/", span =>
            {
                categories = FapClubTo.Categories(host, span);
            });

            if (categories != null && categories.Count > 0)
            {
                hybridCache.Set(memKey, categories, cacheTime(720));
                return categories;
            }

            return new List<MenuItem>();
        }
        finally
        {
            menuLock.Release();
        }
    }

    static IEnumerable<List<MenuItem>> ToChunks(List<MenuItem> src, int size)
    {
        for (int i = 0; i < src.Count; i += size)
        {
            int n = System.Math.Min(size, src.Count - i);
            yield return src.GetRange(i, n);
        }
    }

    [HttpGet, Staticache(manually: true)]
    [Route("fapclub/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        string page = FapClubTo.DetailUrl(uri);
        if (page == null)
            return OnError("stream_links", refresh_proxy: true);
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
    rhubFallback:
        var cache = await InvokeCacheResult($"fapclub:view:{page}", 180, jsonContext.StreamItem, async e =>
        {
            StreamItem stream = null;
            await httpHydra.GetSpan(FapClubTo.SiteHost + page, span =>
            {
                stream = FapClubTo.Stream(span);
                if (stream != null)
                    stream.recomends = FapClubTo.Related(FapClubTo.SiteHost, span);
            });
            if (stream == null)
                return e.Fail("stream_links", refresh_proxy: true);
            return e.Success(stream);
        });
        if (related)
            return PlaylistResult(cache.Value?.recomends, cache.ISingleCache, null, total_pages: 1);

        if (IsRhubFallback(cache))
            goto rhubFallback;
        return OnResult(cache);
    }

    [HttpGet]
    [Route("fapclub/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        string page = FapClubTo.DetailUrl(uri);
        if (page == null)
            return Json(new { items = new object[0], groups = new object[0] });
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;
    rhubFallback:
        // CHỈ 1 fetch detail (không fetch home), cache 360 phút
        var cache = await InvokeCacheResult<FapClubTo.PopupResult>($"fapclub:info:{page}", 360, async e =>
        {
            string detailHtml = null;
            await httpHydra.GetSpan(FapClubTo.SiteHost + page, span =>
            {
                detailHtml = new string(span);
            });
            if (string.IsNullOrEmpty(detailHtml))
                return e.Fail("popup", refresh_proxy: true);
            var popup = FapClubTo.Popup(host, page, detailHtml.AsSpan());
            if (popup == null || popup.groups == null || popup.groups.Count == 0)
                return e.Fail("popup", refresh_proxy: true);
            return e.Success(popup);
        });
        if (IsRhubFallback(cache))
            goto rhubFallback;
        if (cache.IsSuccess && cache.Value != null)
            return Json(cache.Value);
        return Json(new { items = new object[0], groups = new object[0] });
    }

    // Danh sách "Video liên quan" cho popup — 1 trang duy nhất (total_pages = 1)
    async Task<ActionResult> RelatedList(string uri)
    {
        string page = FapClubTo.DetailUrl(uri);
        if (page == null)
            return EmptyRel();

    rhubFallback:
        var cache = await InvokeCacheResult($"fapclub:rel:{page}", 180, jsonContext.ListPlaylistItem, async e =>
        {
            List<PlaylistItem> playlists = null;
            await httpHydra.GetSpan(FapClubTo.SiteHost + page, span =>
            {
                playlists = FapClubTo.Related(init.host, span);
            });
            if (playlists == null || playlists.Count == 0)
                return e.Fail("playlists", refresh_proxy: true);
            return e.Success(playlists);
        });
        if (IsRhubFallback(cache))
            goto rhubFallback;
        if (!cache.IsSuccess)
            return EmptyRel();
        return PlaylistResult(cache, await MenuAsync(null), total_pages: 1);
    }

    // rel list rỗng: trả shape 200 + total_pages 1 (không 503, không để app nạp lặp)
    ActionResult EmptyRel()
        => Json(new { count = 0, total_pages = 1, menu = new object[0], list = new List<PlaylistItem>() });
}
