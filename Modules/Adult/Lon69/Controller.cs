using Microsoft.AspNetCore.Mvc;
using System;
using Shared;
using Shared.Attributes;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Lon69;

public class Lon69Controller : BaseSisiController
{
    public Lon69Controller() : base(ModInit.conf) { }

    [HttpGet, Staticache(manually: true)]
    [Route("lon69")]
    async public Task<ActionResult> Index(string search, string c, string sort, int pg = 1)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (pg < 1) pg = 1;

        sort = Lon69To.ClampSort(sort, search, c);

        var menuTask = MenuAsync(search, sort, c);

        var cache = await InvokeCacheResult<(List<PlaylistItem> playlists, int total_pages)>(
            ipkey($"lon69:v2:{search}:{c}:{sort}:{pg}"), 10, async e =>
        {
            string url = Lon69To.BuildUri(init.host, search, c, sort, pg);
            string html = await GetPageAsync(url);
            var playlists = Lon69To.Playlist("lon69/vidosik", html ?? "");
            if (playlists == null || playlists.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(search) || pg > 1)
                    return e.Success((new List<PlaylistItem>(), 0));
                return e.Fail("playlists", refresh_proxy: true);
            }
            return e.Success((playlists, Lon69To.Pages(html)));
        });

        if (rch?.enable == true) StatiCacheDisabled = true;
        if (!cache.IsSuccess) return OnError(cache.ErrorMsg);

        return PlaylistResult(cache.Value.playlists, cache.ISingleCache, await menuTask, total_pages: cache.Value.total_pages);
    }

    async Task<List<MenuItem>> MenuAsync(string search, string sort, string c)
    {
        var head = Lon69To.MenuHead(host, search, sort, c);
        var baseMenu = await MenuBaseAsync();
        if (baseMenu != null && baseMenu.Count > 0) head.AddRange(baseMenu);
        return head;
    }

    async Task<List<MenuItem>> MenuBaseAsync()
    {
        string memKey = ipkey("lon69:menu");
        if (hybridCache.TryGetValue(memKey, out List<MenuItem> hit) && hit != null && hit.Count > 0)
            return hit;
        // Fetch home để lấy categories
        string html = await GetPageAsync(Lon69To.SiteHost + "/");
        var cats = Lon69To.Categories(html ?? "");
        // Tags từ nav menu (giới hạn 4 tag)
        var tags = Lon69To.NavTags(html ?? "");
        var menu = Lon69To.Menu(host, cats, tags);
        hybridCache.Set(memKey, menu, cacheTime(720), true);
        return menu;
    }

    async Task<string> GetPageAsync(string url, string referer = null)
    {
        if (string.IsNullOrEmpty(url)) return null;
        // Site dùng bot-detection theo header: thiếu sec-ch-ua / sec-fetch-* thì trả bản
        // "lite" KHÔNG chứa <template id="crl-tvc-player-template"> -> không lấy được source.
        // Gửi header giống browser thật để luôn nhận HTML đầy đủ có player.
        var headers = HeadersModel.Init(
            ("User-Agent", Lon69To.ChromeUA),
            ("Referer", string.IsNullOrEmpty(referer) ? Lon69To.SiteHost + "/" : referer),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"),
            ("Accept-Language", "vi-VN,vi;q=0.9,en;q=0.8"),
            ("sec-ch-ua", "\"Chromium\";v=\"120\", \"Android WebView\";v=\"120\", \"Not?A_Brand\";v=\"24\""),
            ("sec-ch-ua-mobile", "?1"),
            ("sec-ch-ua-platform", "\"Android\""),
            ("Sec-Fetch-Dest", "document"),
            ("Sec-Fetch-Mode", "navigate"),
            ("Sec-Fetch-Site", "same-origin"),
            ("Sec-Fetch-User", "?1"),
            ("Upgrade-Insecure-Requests", "1"));
        // Site trả HTML ngẫu nhiên: lúc đầy đủ có player, lúc "lite" chỉ có quảng cáo.
        // Thử lại vài lượt để tăng cơ hội nhận bản có player.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                string html = await Http.Get(url, timeoutSeconds: 20, headers: headers, httpversion: init.httpversion);
                if (!string.IsNullOrEmpty(html) && html.Length > 2000 && !html.Contains("Just a moment"))
                    return html;
            }
            catch { }
            try
            {
                string html = await Http.Get(url, timeoutSeconds: 20, headers: headers, proxy: proxy, httpversion: init.httpversion);
                if (!string.IsNullOrEmpty(html) && html.Length > 2000 && !html.Contains("Just a moment"))
                    return html;
            }
            catch { }
        }
        return null;
    }

    [HttpGet, Staticache(manually: true)]
    [Route("lon69/vidosik")]
    async public Task<ActionResult> Vidosik(string uri)
    {
        if (await IsRequestBlocked(rch: true, rch_keepalive: -1))
            return badInitMsg;
        if (string.IsNullOrWhiteSpace(uri))
            return OnError("stream_links", refresh_proxy: true);

        string pageUrl = uri.StartsWith("http", System.StringComparison.OrdinalIgnoreCase) ? uri : Lon69To.SiteHost + "/" + uri.Trim('/');
        string detailHtml = await GetPageAsync(pageUrl);
        if (string.IsNullOrEmpty(detailHtml))
            return OnError("stream_links", refresh_proxy: true);

        string source = await ResolveSourceAsync(detailHtml);
        if (string.IsNullOrEmpty(source))
            return OnError("stream_links", refresh_proxy: true);

        var qualitys = new Dictionary<string, string>();
        qualitys["Server 1"] = source;

        return OnResult(new StreamItem() { qualitys = qualitys });
    }

    // Tách URL video thật từ HTML detail. Site có 2 biến thể player:
    //  - Variant A: <template id="crl-tvc-player-template"> -> <source/src="...mp4">
    //  - Variant B (trang cũ): template chứa <iframe src=".../play.php?id=...">,
    //    fetch iframe đó -> <video id="main"><source src=".../stream.php?id=...&token=...">
    // Quảng cáo preroll (crl-tvc-video src="...tvc.mp4") nằm NGOÀI template -> luôn bị loại.
    async Task<string> ResolveSourceAsync(string detailHtml)
    {
        var tpl = Regex.Match(detailHtml, @"<template id=""crl-tvc-player-template"">([\s\S]*?)</template>", RegexOptions.IgnoreCase);
        string scope = tpl.Success ? tpl.Groups[1].Value : detailHtml;

        // Variant A: source/mp4 trực tiếp trong template
        var m = Regex.Match(scope, @"(?:<source[^>]+src|<video[^>]+src)=""(https?://[^""]+\.mp4)""", RegexOptions.IgnoreCase);
        if (m.Success)
            return DecodeUrl(m.Groups[1].Value);

        // Variant B/C: template chứa iframe player -> fetch rồi tìm source trong <video id="main">
        // Variant B: iframe -> <video id="main"><source src="...stream.php?token=..."> (mp4 trực tiếp)
        // Variant C: iframe -> JWPlayer + API -> m3u8 (HLS). Nếu không tìm thấy mp4, thử tìm m3u8.
        var ifr = Regex.Match(scope, @"<iframe[^>]+src=""(https?://[^""]+)""", RegexOptions.IgnoreCase);
        if (ifr.Success)
        {
            string iframeUrl = ifr.Groups[1].Value;
            string referer = "";
            try { referer = new Uri(iframeUrl).GetLeftPart(UriPartial.Authority) + "/"; } catch { }
            // Host playvdphe3x.xyz thực tế được rewrite thành play.vstreamplay.xyz (xem crl-iframe-domain.js)
            var candidates = new List<string> { iframeUrl };
            try
            {
                var u = new Uri(iframeUrl);
                if (u.Host.IndexOf("playvdphe3x", StringComparison.OrdinalIgnoreCase) >= 0)
                    candidates.Add(u.Scheme + "://play.vstreamplay.xyz" + u.PathAndQuery);
            }
            catch { }
            foreach (string cu in candidates)
            {
                string iframeHtml = await FetchExternalAsync(cu, referer);
                if (string.IsNullOrEmpty(iframeHtml)) continue;
                var main = Regex.Match(iframeHtml, @"<video[^>]+id=""main""[^>]*>([\s\S]*?)</video>", RegexOptions.IgnoreCase);
                string block = main.Success ? main.Groups[1].Value : iframeHtml;
                var sm = Regex.Match(block, @"(?:<source[^>]+src|<video[^>]+src)=""(https?://[^""]+\.mp4)""", RegexOptions.IgnoreCase);
                if (!sm.Success)
                    sm = Regex.Match(block, @"(?:<source[^>]+src|<video[^>]+src)=""(https?://[^""]+\.mp4[^""]*)""", RegexOptions.IgnoreCase);
                if (sm.Success)
                    return DecodeUrl(sm.Groups[1].Value);
                // Variant C: không có mp4 trực tiếp -> tìm m3u8 (HLS)
                var m3u8 = Regex.Match(iframeHtml, @"https?://[^""']+\.m3u8");
                if (m3u8.Success)
                    return DecodeUrl(m3u8.Groups[1].Value);
            }
        }
        return null;
    }

    // Fetch host ngoài (player embed). Bounded timeout + 1 lần proxy fallback, không retry dài
    // (host này thường chậm/bị chặn ở runtime, không nên treo 120s).
    async Task<string> FetchExternalAsync(string url, string referer)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var headers = HeadersModel.Init(
            ("User-Agent", Lon69To.ChromeUA),
            ("Referer", referer),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        try
        {
            string html = await Http.Get(url, timeoutSeconds: 15, headers: headers, httpversion: init.httpversion);
            if (!string.IsNullOrEmpty(html) && html.Length > 500) return html;
        }
        catch { }
        try
        {
            string html = await Http.Get(url, timeoutSeconds: 15, headers: headers, proxy: proxy, httpversion: init.httpversion);
            return html;
        }
        catch { return null; }
    }

    static string DecodeUrl(string url)
    {
        return url.Replace("&amp;", "&").Replace("\\u0026", "&").Replace("\\/", "/").Replace("&lt;", "").Replace("&gt;", "");
    }
}
