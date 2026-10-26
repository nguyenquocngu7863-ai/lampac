using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.PlaywrightCore;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace XHeoVL;

public static class XHeoVLTo
{
    public static string SiteHost = "https://xheovl.site";
    public const string PlayerHost = "https://player.vstream.uk";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    // ============================================================
    // LIST — site CSR (AlpineJS) → phải render bằng Playwright.
    // m3u8/segments ở b2.m3u8.fyi (S3 signed) chạy được direct,
    // không cần fetch qua Chrome.
    // ============================================================

    static readonly SemaphoreSlim _renderLock = new SemaphoreSlim(1, 1);

    public static async Task<string> RenderPageAsync(string url)
    {
        if (!await _renderLock.WaitAsync(TimeSpan.FromSeconds(30))) return null;
        try
        {
            using var browser = new PlaywrightBrowser();
            var page = await browser.NewPageAsync("XHeoVL", new Dictionary<string, string>
            {
                ["User-Agent"] = ChromeUA,
                ["Referer"] = SiteHost + "/"
            }, keepopen: true);
            if (page == null) return null;

            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    Timeout = 20000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
                await page.WaitForSelectorAsync(".vb-card, .vb-mini", new() { Timeout = 10000 });
                return await page.ContentAsync();
            }
            finally
            {
                try { await page.CloseAsync(); } catch { }
            }
        }
        catch { return null; }
        finally { _renderLock.Release(); }
    }

    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            @"<a[^>]*href=""(/video/[^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.Singleline))
        {
            string href = m.Groups[1].Value;
            string inner = m.Groups[2].Value;

            string videoUrl = SiteHost + href;
            if (!seen.Add(videoUrl)) continue;

            var titleMatch = Regex.Match(inner, @"class=""vb-card__title""[^>]*>([^<]+)");
            string title = titleMatch.Success ? HttpUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim()) : "";
            if (string.IsNullOrEmpty(title)) continue;

            var thumbMatch = Regex.Match(inner, @"src=""([^""]+)""");
            string poster = thumbMatch.Success ? thumbMatch.Groups[1].Value : null;
            // Chuẩn hóa ảnh thành URL tuyệt đối (site dùng relative /uploads/...)
            if (!string.IsNullOrEmpty(poster) && poster.StartsWith("/"))
                poster = SiteHost + poster;

            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(videoUrl),
                name = title,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "xheovl", href = videoUrl, image = poster }
            });
        }
        return list;
    }

    public static int Pages(string html)
    {
        var m = Regex.Match(html, @"trang \d+/(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 1;
    }

    public static string UriMethod(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
            return host + "/search/" + HttpUtility.UrlEncode(search.Trim()) + "/";

        string baseUrl = host;
        if (!string.IsNullOrWhiteSpace(c))
        {
            if (c.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                baseUrl = c.TrimEnd('/');
            else
                baseUrl = host + c;
        }
        if (pg > 1)
            baseUrl += "/page/" + pg + "/";
        else if (!baseUrl.EndsWith("/"))
            baseUrl += "/";
        return baseUrl;
    }

    // Categories cho menu 2 tầng (site có sẵn /the-loai/<slug>).
    // host = proxy host (ví dụ http://127.0.0.1:9118) để category URL đi qua app, không trực tiếp ra site.
    public static List<MenuItem> Categories(string host)
    {
        host = host.TrimEnd('/');
        string root = host + "/xheovl";
        string url(string c) => root + "?c=" + HttpUtility.UrlEncode(c);
        return new List<MenuItem>
        {
            new("Mới nhất", root),
            new("Gái Xinh", url("/the-loai/gai-xinh")),
            new("Sinh Viên", url("/the-loai/sinh-vien")),
            new("Cô Giáo", url("/the-loai/co-giao")),
            new("Doggy", url("/the-loai/doggy")),
            new("Ngoại Tình", url("/the-loai/ngoai-tinh")),
            new("Tập thể", url("/the-loai/tap-the")),
            new("Âu Mỹ", url("/the-loai/au-my")),
            new("JAV", url("/the-loai/jav")),
            new("Hack Cam", url("/the-loai/hack-cam")),
        };
    }

    public static List<MenuItem> Menu(string host)
    {
        host = host.TrimEnd('/');
        string root = host + "/xheovl";
        string url(string c) => root + "?c=" + HttpUtility.UrlEncode(c);
        return new List<MenuItem>(1)
        {
            new MenuItem(){ title = "Danh mục", playlist_url = "submenu", submenu = Categories(host) }
        };
    }
    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith("//")) value = "https:" + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            value = SiteHost + "/" + value.TrimStart('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        if (!parsed.Host.EndsWith("xheovl.site", StringComparison.OrdinalIgnoreCase)) return null;
        return value;
    }

    // ============================================================
    // STREAM — thuần HTTP, không cần Playwright.
    //
    // Flow (đã verify ngoài module, 2026-10-26):
    //   1. GET /video/<slug> (SSR) → JSON-LD "embedUrl":"https://player.vstream.uk/e/<uuid>"
    //      fallback: <iframe src="/play/?token=...">  → fetch /play/?token → "const url = "..."
    //   2. GET embed page → <link rel="preload" as="fetch" href="https://player.vstream.uk/hls/<token>">
    //   3. m3u8 đó là MEDIA playlist, segments S3 signed → client fetch trực tiếp.
    //      Host vstream.uk 200 mà KHÔNG cần referer/origin (đã thử None).
    // ============================================================

    static string EmbedUrl(string detailHtml)
    {
        // 1) JSON-LD (đường tắt, luôn có trên SSR)
        string p1 = @"""embedUrl"":\s*:""(https://player\.vstream\.uk/e/[^""]+)""";
        var m = Regex.Match(detailHtml, p1);
        if (m.Success) return m.Groups[1].Value;
        // 2) iframe /play/?token=... trên detail
        m = Regex.Match(detailHtml, @"<iframe[^>]*src=""(/play/\?token=[^""]+)""");
        if (m.Success)
        {
            string t = m.Groups[1].Value;
            return SiteHost + t;
        }
        return null;
    }

    static string HlsUrlFromEmbed(string embedHtml)
    {
        // 1) <link rel="preload" as="fetch" href=".../hls/...">
        var m = Regex.Match(embedHtml, @"<link[^>]*rel=""preload""[^>]*href=""(https://player\.vstream\.uk/hls/[^""]+)""");
        if (m.Success) return m.Groups[1].Value;
        // 2) fallback: const url / hls proxy / raw hls link
        m = Regex.Match(embedHtml, @"(https://player\.vstream\.uk/hls/[A-Za-z0-9+/=_-]+)");
        if (m.Success) return m.Groups[1].Value;
        return null;
    }

    static async Task<string> GetHtml(string url, string referer, WebProxy proxy = null, int httpversion = 1, int timeoutSeconds = 25)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", ChromeUA),
            ("Referer", referer ?? SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        string html = null;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, httpversion: httpversion); } catch { }
        if (!string.IsNullOrEmpty(html) && html.Length > 2000) return html;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, proxy: proxy, httpversion: httpversion); } catch { return null; }
        return html;
    }

    // Trả m3u8 + referer (newline) cho player, hoặc null.
    public static async Task<string> ResolveStreamAsync(string pageUrl, WebProxy proxy = null, int httpversion = 1, int timeoutSeconds = 25)
    {
        if (string.IsNullOrWhiteSpace(pageUrl)) return null;
        string detail = await GetHtml(pageUrl, SiteHost + "/", proxy, httpversion, timeoutSeconds);
        if (string.IsNullOrEmpty(detail)) return null;

        string embed = EmbedUrl(detail);
        if (string.IsNullOrEmpty(embed)) return null;

        if (embed.Contains("/play/?token="))
        {
            string ph = await GetHtml(embed, pageUrl, proxy, httpversion, timeoutSeconds);
            if (string.IsNullOrEmpty(ph)) return null;
            var mm = Regex.Match(ph, @"const\s+url\s*=\s*""(https://player\.vstream\.uk/e/[^""]+)""");
            if (!mm.Success) return null;
            embed = mm.Groups[1].Value;
        }

        string pageHtml = await GetHtml(embed, pageUrl, proxy, httpversion, timeoutSeconds);
        if (string.IsNullOrEmpty(pageHtml)) return null;

        string hls = HlsUrlFromEmbed(pageHtml);
        if (string.IsNullOrEmpty(hls)) return null;
        return hls + "\n" + pageUrl;
    }

    // ============================================================
    // MENU
    // ============================================================

    public static List<MenuItem> MenuHead(string host, string search)
    {
        host = host.TrimEnd('/');
        string root = host + "/xheovl";
        return new List<MenuItem>(1)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
    }
}
