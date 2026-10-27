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
        // 1) Site đã SSR: HTML thô chứa đủ card + href slug ĐẦY ĐỦ (đúng dữ liệu server).
        //    Không phụ thuộc DOM sau JS → tránh biến thể slug bị cắt (44 ký tự) từng làm
        //    item category 404 dù trang chủ vẫn phát được. Nhanh hơn render rất nhiều.
        string raw = await GetHtml(url, SiteHost + "/", null, 1, 20);
        if (!string.IsNullOrEmpty(raw) && raw.Length > 5000 &&
            (raw.Contains("vb-card") || raw.Contains("/video/")))
            return raw;

        // 2) Fallback: site quay lại CSR thì mới render bằng Playwright.
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
            new("Cưỡi Ngựa", url("/the-loai/cuoi-ngua")),
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
    // Flow (verify 2026-10-26, cập nhật 2026-10-27):
    //   1. GET /video/<slug> (SSR) → JSON-LD "embedUrl": (có thể bị escape \u0026)
    //        - https://player.vstream.uk/e/<uuid>   → HLS
    //        - https://play.avcl.uk/zpic/?id=...    → MP4 (ArtPlayer, host mới site đang chuyển)
    //      fallback: <iframe src="/play/?token=..."> → fetch /play/ → "const url = "..."
    //   2. GET embed page → <link rel=preload href=".../hls/..."> (vstream)
    //      hoặc "const url = \"...mp4\"" (avcl) — cả 2 đều trả thẳng, không cần render JS.
    //   3. m3u8 (vstream) là MEDIA playlist, segments S3 signed → client fetch trực tiếp;
    //      vstream.uk 200 KHÔNG cần referer/origin. MP4 avcl: CDN zpicdn.xyz (2026-10-27
    //      vhost còn rỗng, chưa có file → các video dùng player này chưa phát được ở đâu cả).
    // ============================================================

    // JSON trong HTML có thể escape: \u0026 (&), \u002f (/), \/ (/) — phải bỏ trước khi dùng URL
    static string JsonUnescape(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("\\u0026", "&").Replace("\\u002f", "/").Replace("\\u003d", "=")
                .Replace("\\u003a", ":").Replace("\\/", "/");
    }

    static string EmbedUrl(string detailHtml)
    {
        // 1) JSON-LD (đường tắt, luôn có trên SSR). Chấp nhận MỌI player host:
        //    player.vstream.uk (HLS) lẫn play.avcl.uk/zpic (MP4, ArtPlayer) — site đổi host liên tục.
        var m = Regex.Match(detailHtml, @"""embedUrl"":\s*""(https?://[^""]+)""");
        if (m.Success) return JsonUnescape(m.Groups[1].Value);
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
        // 1) vstream: <link rel="preload" as="fetch" href=".../hls/...">
        var m = Regex.Match(embedHtml, @"<link[^>]*rel=""preload""[^>]*href=""(https?://[^""]+/hls/[^""]+)""");
        if (m.Success) return m.Groups[1].Value;
        // 2) const url = "..." (vstream HLS hoặc avcl MP4) — đúng biến mà artconfig.js/player dùng
        m = Regex.Match(embedHtml, @"const\s+url\s*=\s*""([^""]+)""");
        if (m.Success)
        {
            string u = JsonUnescape(m.Groups[1].Value);
            if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return u;
        }
        // 3) fallback cuối: link m3u8/mp4 trực tiếp (loại URL quảng cáo tvc trên chính site)
        m = Regex.Match(embedHtml, @"(https?://[^""'\s]+\.m3u8)");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(embedHtml, @"(https?://(?!xheovl\.site)[^""'\s]+\.mp4)");
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
            // const url = "https://player.vstream.uk/e/..." HOẶC "https://play.avcl.uk/zpic/?id=..."
            var mm = Regex.Match(ph, @"const\s+url\s*=\s*""(https?://[^""]+)""");
            if (!mm.Success) return null;
            embed = JsonUnescape(mm.Groups[1].Value);
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
