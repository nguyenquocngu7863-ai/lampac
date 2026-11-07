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
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Jav18;

public class Jav18Controller : BaseSisiController
{
    public Jav18Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("jav18")]
    async public Task<ActionResult> Index(
        string search, string c, string rel, int pg = 1, string q = null, string sort = null)
    {
        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        return await Core(search ?? q, c, pg, sort);
    }

    // Duong dan thay cho query — app diem manh hon, khong lo query bi
    // mat/chen khi no ghep them `pg=1`:
    //   /jav18/p/latest-updates   /jav18/p/hot
    //   /jav18/p/categories/<slug>[/2]
    //   /jav18/p/tags/<slug>[/2]
    //   /jav18/p/models/<slug>    /jav18/p/search/<q>
    [HttpGet, Staticache(manually: true)]
    [Route("jav18/p/{**rest}")]
    async public Task<ActionResult> Path(string rest, int pg = 1, string sort = null)
    {
        if (pg < 1)
            pg = 1;

        string search = null, c = null;

        // App phan trang bang QUERY `?pg=N` (sisi.js addUrlComponent),
        // khong phai duong dan -> bat buoc doc pg tu query.
        string[] seg = (rest ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (seg.Length > 0 && seg[seg.Length - 1].All(char.IsDigit)
            && int.TryParse(seg[seg.Length - 1], out int dp) && dp > 1)
        {
            pg = dp;
            seg = seg.Take(seg.Length - 1).ToArray();
        }

        if (seg.Length >= 2 && seg[0].Equals("search", StringComparison.OrdinalIgnoreCase))
        {
            search = string.Join("/", seg.Skip(1));
        }
        else if (seg.Length >= 1)
        {
            c = string.Join("/", seg).Trim('/');
        }

        return await Core(search, c, pg, sort);
    }

    async Task<ActionResult> Core(string search, string c, int pg, string sort = null)
    {
        Req("CORE s=" + (search ?? "-") + " c=" + (c ?? "-") + " sort=" + (sort ?? "-") + " pg=" + pg);

        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (pg < 1)
            pg = 1;

        // Clamp sort ve dung tap cua context TRUOC khi vao cache key
        // (sort la o context nay co the chet o context kia).
        sort = Jav18To.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"jav18:{search}:{c}:{sort}:{pg}"),
            10, async e =>
        {
            string html = await GetPageAsync(Jav18To.Uri(init.host, search, c, pg, sort));

            // html == null (mạng lỗi) -> Fail; html == "" (404) -> Playlist
            // trả list rỗng -> Success (slug bịa / hết kết quả).
            var playlists = html == null
                ? null
                : Jav18To.Playlist(html);

            // Tim khong co ket qua / trang rong: site tra block rong thay
            // vi loi. Day KHONG phai loi -> tra SUCCESS voi list rong cho
            // app hien trong.
            if (playlists != null && playlists.Count == 0)
                return e.Success((playlists, 1));

            if (playlists == null && !string.IsNullOrWhiteSpace(search))
                return e.Success((new List<PlaylistItem>(), 1));

            if (playlists == null)
                return e.Fail("playlists", refresh_proxy: true);

            return e.Success((playlists, Jav18To.Pages(html)));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        // Site KHÔNG co đường dẫn này (slug bịa) hoặc tìm kiếm không có kết
        // quả -> trả SUCCESS với list rỗng. PlaylistResult của framework tự
        // báo lỗi khi list rỗng nên phải tự viết JSON (đúng shape
        // {count,total_pages,menu,list} như PlaylistResult).
        if (cache.Value.playlists == null || cache.Value.playlists.Count == 0)
        {
            return Json(new
            {
                count = 0,
                total_pages = 1,
                menu = await menuTask,
                list = new List<PlaylistItem>()
            });
        }

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await menuTask,
            total_pages: cache.Value.total_pages);
    }

    // App CACHE response DAU CA PHIEN -> lan truy cap dau ma tra menu
    // rut gon (chi Tim kiem + Sap xep) thi user restart app cung thay,
    // phai xoa cache app. Nen:
    //   1. await fetch trong gioi han ~10s de menu that co ngay;
    //   2. tran han / fetch loi -> tra FALLBACK TINH (14 the loai +
    //      113 tu khoa) — van day du, khong bao gio rut gon.
    static readonly SemaphoreSlim menuLock = new(1, 1);
    const int MenuFetchBudget = 10_000;

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        // HEAD (dong 1+2) phu thuoc context -> dung moi request, re.
        var menu = Jav18To.MenuHead(host, search, sort, c);

        // BASE (dong 3+) cache dung 1 lan.
        string memKey = ipkey("jav18:menu");

        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) &&
            hit != null && hit.Count > 0)
        {
            menu.AddRange(hit);
            return menu;
        }

        string hostLocal = host;

        // Request khac dang giu lock: tra head + fallback ngay, khong
        // cho app treo them.
        if (!await menuLock.WaitAsync(3000))
        {
            menu.AddRange(FallbackBase(hostLocal));
            return menu;
        }

        try
        {
            // Request truoc do da nap xong cache trong luc ta cho lock.
            if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit2) &&
                hit2 != null && hit2.Count > 0)
            {
                menu.AddRange(hit2);
                return menu;
            }

            var build = BuildMenuAsync(hostLocal, memKey);

            if (build != await Task.WhenAny(build, Task.Delay(MenuFetchBudget)))
            {
                // Het gio: head + fallback, fetch chay tiep nen lan sau co menu dong.
                menu.AddRange(FallbackBase(hostLocal));
                return menu;
            }

            menu.AddRange(await build);
            return menu;
        }
        finally
        {
            menuLock.Release();
        }
    }

    List<MenuItem> FallbackBase(string hostLocal)
        => Jav18To.Menu(hostLocal, Jav18To.FallbackCats, Jav18To.FallbackTags);

    // LUON tra menu day du: dynamic neu fetch duoc, nguoc lai fallback.
    // Khong bao gio nem ra list rong.
    async Task<List<MenuItem>> BuildMenuAsync(string hostLocal, string memKey)
    {
        IReadOnlyList<(string name, string path)> cats = Jav18To.FallbackCats;
        IReadOnlyList<(string name, string path)> tags = Jav18To.FallbackTags;
        List<(string name, string path)> gotCats, gotTags, gotModels = null;
        IReadOnlyList<(string name, string path)> models = null;

        try
        {
            // TUẦN TỰ + nghỉ 1.2s giữa các lần: 3 request song song cũng đủ
            // làm site rate-limit (mỗi lần chỉ có 1 request trên đường).
            gotCats = await TaxonomiesAsync("/categories/", "categories");
            await Task.Delay(1200);
            gotTags = await TaxonomiesAsync("/tags/", "tags");
            await Task.Delay(1200);
            gotModels = await TaxonomiesAsync("/models", "models");

            if (gotCats.Count > 0)
                cats = gotCats;
            if (gotTags.Count > 0)
                tags = gotTags;
            if (gotModels.Count > 0)
                models = gotModels;
        }
        catch { }

        var menu = Jav18To.Menu(hostLocal, cats, tags, models);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    async Task<List<(string name, string path)>> TaxonomiesAsync(
        string page, string kind, int top = int.MaxValue)
    {
        string memKey = ipkey($"jav18:tax:{kind}");

        if (hybridCache.TryGetValue(memKey, out List<(string name, string path)> hit) &&
            hit != null && hit.Count > 0)
            return hit;

        string html = await GetPageAsync($"{Jav18To.SiteHost}{page}");
        var res = kind switch
        {
            "categories" => Jav18To.Categories(html),
            "tags" => Jav18To.Tags(html),
            _ => Jav18To.Models(html)
        };

        if (res.Count == 0)
            return res;

        if (res.Count > top)
            res = res.Take(top).ToList();

        hybridCache.Set(memKey, res, cacheTime(720), true);
        return res;
    }

    // Site 18jav.tv rate-limit MANH (do truc tiep: ~10 request trong vai
    // giay -> tra ve rong/429). Nen fetch TOI THIEU: 1 request, that bai
    // thi cho nghi 3s roi thu lai 1 lan nua. KHONG race hai duong (hydra +
    // Http.Get) vi x2 so request moi lan.
    async Task<string> GetPageAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var (html, code) = await OneGetAsync(url, 15);
        if (!string.IsNullOrEmpty(html) && html.Length >= 1000)
            return html;

        // 404 = trang khong ton tai (slug bịa) -> khong thu lại, tra body
        // (hoac rỗng) cho parser rơi vào nhánh "0 kết quả = success".
        if (code == 404)
            return html ?? "";

        await Task.Delay(3000);

        (html, code) = await OneGetAsync(url, 15);
        if (!string.IsNullOrEmpty(html) && html.Length >= 1000)
            return html;

        if (code == 404)
            return html ?? "";

        return null;
    }

    async Task<(string html, int code)> OneGetAsync(string url, int seconds)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", Jav18To.ChromeUA),
            ("Accept-Language", "en-US,en;q=0.9"),
            ("Referer", Jav18To.SiteHost + "/"));

        try
        {
            // statusCodeOK: false -> lay ca body cua trang lỗi (404) thay vì
            // null, để phân biệt "không tìm thấy" với "mạng lỗi".
            var (html, response) = await Http.BaseGet(
                url,
                timeoutSeconds: seconds,
                headers: headers,
                proxy: proxy,
                httpversion: init.httpversion,
                statusCodeOK: false);

            int code = response != null ? (int)response.StatusCode : 0;

            Req("GET " + code + " " + (html?.Length ?? -1) + " " + url);

            return (html, code);
        }
        catch (Exception ex)
        {
            Req("GET EX " + ex.GetType().Name + ": " + ex.Message);
            return (null, 0);
        }
    }

    static void Req(string m)
    {
        try
        {
            System.IO.File.AppendAllText("/data/data/com.termux/files/home/lampac-run/jav18-req.log",
                DateTime.Now.ToString("HH:mm:ss") + " " + m + Environment.NewLine);
        }
        catch { }
    }

    // ===== long-press: nữ diễn viên / thể loại / từ khoá của 1 video =====
    // Tra { items, groups } cho SISI/plugins/sisi.js (menu nhan giu).
    [HttpGet]
    [Route("jav18/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return Json(new { items = new object[0], groups = new object[0] });

        string pageUrl = uri.Trim();
        pageUrl = HttpUtility.HtmlDecode(pageUrl);
        if (!pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            pageUrl = init.host + pageUrl;
        int qm = pageUrl.IndexOf('?');
        if (qm >= 0)
            pageUrl = pageUrl.Substring(0, qm);

        string memKey = ipkey($"jav18:genres:v2:{pageUrl}");
        if (!hybridCache.TryGetValue(memKey,
            out List<(string kind, string slug, string name)> info) || info == null)
        {
            string html = await GetPageAsync(pageUrl);
            info = Jav18To.Info(html);
            if (info.Count > 0)
                hybridCache.Set(memKey, info, cacheTime(360));
        }

        var groups = new List<object>();

        void AddGroup(string title, string kind, Func<string, string> urlOf)
        {
            var items = info.Where(x => x.kind == kind)
                .Select(x => new { title = Jav18To.Translate(x.name), url = urlOf(x.slug) }).ToList();
            if (items.Count > 0)
                groups.Add(new { title, items });
        }

        string list(string c) => host + "/jav18?c=" + HttpUtility.UrlEncode(c);

        AddGroup("Nữ diễn viên", "model", s => list("models/" + s));
        AddGroup("Thể loại", "cat", s => list("categories/" + s));
        AddGroup("Từ khoá", "tag", s => list("tags/" + s));

        if (groups.Count > 0)
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
                        url = host + "/jav18?rel=" + HttpUtility.UrlEncode(pageUrl)
                    }
                }
            });
        }

        return Json(new { items = new object[0], groups });
    }

    // "推薦 / 猜你喜歡" cua trang detail. Chi 1 trang (site tra ve mot lo ~12).
    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        string pageUrl = rel.Trim();
        pageUrl = HttpUtility.HtmlDecode(pageUrl);
        if (!pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            pageUrl = init.host + pageUrl;
        int qm = pageUrl.IndexOf('?');
        if (qm >= 0)
            pageUrl = pageUrl.Substring(0, qm);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"jav18:rel:{pageUrl}"), 180, async e =>
        {
            string html = await GetPageAsync(pageUrl);

            var pl = Jav18To.Related(html);
            if (pl == null || pl.Count == 0)
                return e.Success((new List<PlaylistItem>(), 1));

            return e.Success((pl, 1));
        });

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache,
            await MenuAsync(null, null, null), total_pages: 1);
    }

    [HttpGet, Staticache(manually: true)]
    [Route("jav18/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || links.Count == 0)
            return OnError("stream_links", refresh_proxy: true);

        return Json(links.ToDictionary(k => k.Key, k =>
            $"{host}/jav18/video.m3u8?uri={HttpUtility.UrlEncode(uri)}&q={HttpUtility.UrlEncode(k.Key)}"));
    }

    // Trang phim chi co MOT nguon: <script> var hlsUrl = 'https://
    // s8.hshdkshd.com/hls/<token>/<ts>/storage/<id>/<md5>.m3u8';
    // Token doi moi lan load nen phai lay nguyen tu trang.
    async Task<Dictionary<string, string>> ResolveAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string pageUrl = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : Jav18To.SiteHost + "/videos/" + uri.Trim('/') + "/";

        string memKey = ipkey($"jav18:view:{pageUrl}");
        if (hybridCache.TryGetValue(memKey, out Dictionary<string, string> cache) &&
            cache != null && cache.Count > 0)
            return cache;

        string page = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(page))
            return null;

        string hls = Jav18To.HlsUrl(page);
        if (string.IsNullOrEmpty(hls))
            return null;

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HLS"] = hls
        };

        hybridCache.Set(memKey, links, cacheTime(10));
        return links;
    }

    [HttpGet]
    [Route("jav18/video")]
    [Route("jav18/video.m3u8")]
    [Route("jav18/video.mp4")]
    async public Task<ActionResult> Video(string uri, string q)
    {
        if (await IsRequestBlocked(rch: true))
            return badInitMsg;

        var links = await ResolveAsync(uri);
        if (links == null || !links.TryGetValue(q, out string link) || string.IsNullOrEmpty(link))
            return OnError("stream_links", refresh_proxy: true);

        var direct = httpHeaders(init, HeadersModel.Init(
            ("Referer", Jav18To.SiteHost + "/")
        ));
        return Redirect(HostStreamProxy(link, direct));
    }
}
