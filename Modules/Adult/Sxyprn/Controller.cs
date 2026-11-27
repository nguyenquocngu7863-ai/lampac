using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Sxyprn;

public class SxyprnController : BaseSisiController
{
    public SxyprnController() : base(ModInit.conf) { }

    static readonly SemaphoreSlim menuLock = new(1, 1);

    [HttpGet, Staticache(manually: true)]
    [Route("sxyprn")]
    async public Task<ActionResult> Index(string search, string c, int pg = 1, string sort = null, string rel = null)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        if (!string.IsNullOrWhiteSpace(rel))
            return await RelatedList(rel, pg);

        if (pg < 1) pg = 1;

        string ctx = Context(c);

        if (!string.IsNullOrWhiteSpace(search))
            ctx = "search:" + search.Trim();

        sort = SxyprnTo.Sort(sort);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"sxyprn:list:{ctx}:{sort}:{pg}"), 10, async e =>
        {
            string html;
            int pages;

            if (ctx != null && ctx.StartsWith("search:", StringComparison.Ordinal))
            {
                string slug = await SearchSlugAsync(ctx.Substring(7)) ?? "All";
                html = await GetTextAsync(SxyprnTo.TagUrl(slug, "latest", 1));
                pages = 1;
            }
            else
            {
                string url = TargetUrl(ctx, sort, pg);
                if (url == null)
                    return e.Fail("playlists", refresh_proxy: true);

                html = await GetTextAsync(url);
                pages = SxyprnTo.Pages(html ?? "");
            }

            if (string.IsNullOrEmpty(html))
                return e.Fail("playlists", refresh_proxy: true);

            var playlists = SxyprnTo.Playlist(html, "sxyprn/vidosik");

            if (playlists.Count == 0)
                return pg > 1 ? e.Success((new List<PlaylistItem>(), 0))
                              : e.Fail("playlists", refresh_proxy: true);

            return e.Success((playlists, pages));
        });

        if (rch?.enable == true)
            StatiCacheDisabled = true;

        if (!cache.IsSuccess)
            return OnError(cache.ErrorMsg);

        return PlaylistResult(
            cache.Value.playlists,
            cache.ISingleCache,
            await MenuAsync(ctx, sort),
            total_pages: cache.Value.total_pages
        );
    }

    static bool IsSlug(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 80)
            return false;

        foreach (char ch in s)
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_')
                return false;

        return true;
    }

    static string Context(string c)
    {
        if (string.IsNullOrWhiteSpace(c))
            return null;

        c = c.Trim();

        if (c == "top" || c == "viewed")
            return c;

        if (c.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            string slug = c.Substring(4).Trim();
            return IsSlug(slug) ? "tag:" + slug : null;
        }

        return null;
    }

    static string TargetUrl(string ctx, string sort, int pg)
    {
        if (ctx == null)
            return SxyprnTo.TagUrl("All", sort, pg);

        if (ctx == "top")
            return SxyprnTo.TopUrl;

        if (ctx == "viewed")
            return SxyprnTo.ViewedUrl;

        if (ctx.StartsWith("tag:", StringComparison.Ordinal))
            return SxyprnTo.TagUrl(ctx.Substring(4), sort, pg);

        return null;
    }

    // ---- HTTP ----

    async Task<string> GetTextAsync(string url)
    {
        try
        {
            string res = null;
            await httpHydra.GetSpan(url, span => { res = span.ToString(); }, addheaders: HeadersModel.Init(
                ("User-Agent", SxyprnTo.ChromeUA),
                ("Referer", SxyprnTo.SiteHost + "/"),
                ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
            ));
            return res;
        }
        catch
        {
            return null;
        }
    }

    async Task<string> SearchSlugAsync(string key)
    {
        try
        {
            string json = null;
            var headers = HeadersModel.Init(
                ("User-Agent", SxyprnTo.ChromeUA),
                ("Referer", SxyprnTo.SiteHost + "/"),
                ("Accept", "application/json, text/plain, */*"),
                ("X-Requested-With", "XMLHttpRequest")
            );

            string body = "uid=&aid=all&key=" + HttpUtility.UrlEncode(key) + "&c=ls7";
            await httpHydra.PostSpan(SxyprnTo.SiteHost + "/php/livesearch.php", body, span => { json = span.ToString(); }, addheaders: headers);

            if (string.IsNullOrEmpty(json))
                return null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("searches", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in arr.EnumerateArray())
                {
                    if (!e.TryGetProperty("link", out var l) || l.ValueKind != JsonValueKind.String)
                        continue;

                    var m = System.Text.RegularExpressions.Regex.Match(l.GetString() ?? "", @"^/([^/]+?)\.html");
                    if (m.Success)
                        return m.Groups[1].Value;
                    break;
                }
            }
        }
        catch { }

        return null;
    }

    // ---- menu ----

    async Task<List<MenuItem>> MenuAsync(string ctx, string sort)
    {
        string root = host.TrimEnd('/') + "/sxyprn";
        var menu = new List<MenuItem>();

        bool sortable = ctx == null || ctx.StartsWith("tag:", StringComparison.Ordinal);
        string keep = ctx == null ? "" : "&c=" + HttpUtility.UrlEncode(ctx);

        // Site chi ho tro sap xep trong listing tag (sm=latest|trending|views|orgasmic)
        if (sortable)
        {
            menu.Add(new MenuItem()
            {
                title = "Sắp xếp: " + SxyprnTo.SortLabel(sort),
                playlist_url = "submenu",
                submenu = new List<MenuItem>()
                {
                    new("Mới nhất", root + "?sort=latest" + keep),
                    new("Thịnh hành", root + "?sort=trending" + keep),
                    new("Xem nhiều", root + "?sort=views" + keep),
                    new("Cực khoái", root + "?sort=orgasmic" + keep)
                }
            });
        }

        menu.Add(new("Mới nhất", root + "?sort=" + sort));
        menu.Add(new("Thịnh hành", root + "?c=top"));
        menu.Add(new("Xem nhiều", root + "?c=viewed"));

        var tags = await TagsAsync();
        if (tags != null && tags.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = tags.Select(t => new MenuItem(t.name,
                    root + "?c=" + HttpUtility.UrlEncode("tag:" + t.slug) + "&sort=" + sort)).ToList()
            });
        }

        return menu;
    }

    async Task<List<(string name, string slug)>> TagsAsync()
    {
        string key = ipkey("sxyprn:tags:v1");

        if (hybridCache.TryGetValue(key, out List<(string name, string slug)> hit) && hit != null && hit.Count > 0)
            return hit;

        if (!await menuLock.WaitAsync(8000))
            return null;

        try
        {
            if (hybridCache.TryGetValue(key, out List<(string name, string slug)> hit2) && hit2 != null && hit2.Count > 0)
                return hit2;

            string html = await GetTextAsync(SxyprnTo.SiteHost + "/");
            var tags = SxyprnTo.Tags(html ?? "", 60);

            if (tags.Count > 0)
                hybridCache.Set(key, tags, cacheTime(720));

            return tags;
        }
        catch
        {
            return null;
        }
        finally
        {
            menuLock.Release();
        }
    }

    // ---- popup (genres) ----

    [HttpGet, Staticache(manually: true)]
    [Route("sxyprn/genres")]
    async public Task<ActionResult> Genres(string uri)
    {
        string id = SxyprnTo.VideoId(uri);
        if (string.IsNullOrEmpty(id))
            return Json(new { items = new object[0], groups = new object[0] });

        string cacheKey = ipkey($"sxyprn:info:{id}");

        if (!hybridCache.TryGetValue(cacheKey, out List<(string kind, string name, string slug)> chips) || chips == null || chips.Count == 0)
        {
            string html = await GetTextAsync(SxyprnTo.DetailUrl(id));
            chips = SxyprnTo.Chips(html ?? "");

            if (chips.Count > 0)
                hybridCache.Set(cacheKey, chips, cacheTime(720));
        }

        return InfoResult(chips, SxyprnTo.DetailUrl(id));
    }

    static string KindLabel(string kind) => kind switch
    {
        "actor" => "Diễn viên",
        "tag" => "Thể loại",
        _ => "Khác"
    };

    static int KindRank(string kind) => kind switch
    {
        "actor" => 0,
        "tag" => 1,
        _ => 99
    };

    ActionResult InfoResult(List<(string kind, string name, string slug)> chips, string uri)
    {
        var groups = new List<object>();

        foreach (string kind in chips.Select(x => x.kind).Distinct().OrderBy(KindRank))
        {
            var items = chips.Where(x => x.kind == kind).Select(x => new
            {
                title = x.name,
                url = $"{host}/sxyprn?c={HttpUtility.UrlEncode("tag:" + x.slug)}"
            }).ToList();

            if (items.Count > 0)
                groups.Add(new { title = KindLabel(kind), items });
        }

        groups.Add(new
        {
            title = "Liên quan",
            items = new[]
            {
                new
                {
                    title = "Video liên quan",
                    plain = true,
                    url = $"{host}/sxyprn?rel={HttpUtility.UrlEncode(uri)}"
                }
            }
        });

        return Json(new { items = new object[0], groups });
    }

    async Task<ActionResult> RelatedList(string rel, int pg)
    {
        string id = SxyprnTo.VideoId(rel);
        if (string.IsNullOrEmpty(id))
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        if (pg < 1) pg = 1;
        if (pg > 1)
            return Json(new { total_pages = 1, list = new List<PlaylistItem>() });

        var rec = await RecomendsAsync(id);
        return PlaylistResult(rec, false, await MenuAsync(null, "latest"), total_pages: 1);
    }

    // ---- vidosik ----

    [HttpGet, Staticache(manually: true)]
    [Route("sxyprn/vidosik")]
    async public Task<ActionResult> Vidosik(string uri, bool related)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;

        string id = SxyprnTo.VideoId(uri);
        if (string.IsNullOrEmpty(id))
            return OnError("stream_links", refresh_proxy: true);

        var recomends = await RecomendsAsync(id);

        if (related)
            return PlaylistResult(recomends, false, null, total_pages: 1);

        string url = await StreamAsync(id);
        if (string.IsNullOrEmpty(url))
            return OnError("stream_links", refresh_proxy: true);

        return Json(new
        {
            qualitys = new Dictionary<string, string>()
            {
                ["MP4"] = url
            },
            recomends
        });
    }

    async Task<string> StreamAsync(string id)
    {
        string memKey = ipkey($"sxyprn:play:{id}");
        if (hybridCache.TryGetValue(memKey, out string hit) && !string.IsNullOrEmpty(hit))
            return hit;

        string html = await GetTextAsync(SxyprnTo.DetailUrl(id));
        string url = SxyprnTo.StreamUrl(html ?? "", id);

        if (!string.IsNullOrEmpty(url))
            hybridCache.Set(memKey, url, cacheTime(20));

        return url;
    }

    async Task<List<PlaylistItem>> RecomendsAsync(string id)
    {
        string memKey = ipkey($"sxyprn:rec:{id}");
        if (hybridCache.TryGetValue(memKey, out List<PlaylistItem> hit) && hit != null)
            return hit;

        string html = await GetTextAsync(SxyprnTo.DetailUrl(id));
        var rec = new List<PlaylistItem>();

        int cut = (html ?? "").IndexOf("Related by:", StringComparison.Ordinal);
        if (cut > 0)
        {
            rec = SxyprnTo.Playlist(html.Substring(cut), "sxyprn/vidosik")
                .Where(x => x.bookmark == null || !x.bookmark.href.Contains(id))
                .Take(20)
                .ToList();
        }

        if (rec.Count > 0)
            hybridCache.Set(memKey, rec, cacheTime(20));

        return rec;
    }
}
