using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace Lon69;

public static class Lon69To
{
    public static string SiteHost = "https://lon69.ink";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
    public const string DesktopUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    static readonly string[] Hosts = { "lon69.ink", "www.lon69.ink", "lon69.site", "www.lon69.site", "lon69.com", "www.lon69.com" };

    // ========== Uri ==========
    public static string BuildUri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string b = pg > 1 ? $"{host}/page/{pg}/" : $"{host}/";
            return b + (b.Contains('?') ? "&" : "?") + "s=" + Uri.EscapeDataString(search.Trim());
        }
        if (string.IsNullOrWhiteSpace(c))
            return pg > 1 ? $"{host}/page/{pg}/" : host + "/";

        string raw = c.Trim().Trim('/');
        string baseUrl = host + "/" + raw;
        return pg > 1 ? baseUrl.TrimEnd('/') + "/page/" + pg + "/" : baseUrl;
    }

    // ========== Page ==========
    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim());
        int frag = value.IndexOf('#');
        if (frag >= 0) value = value.Substring(0, frag);
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = SiteHost + "/" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        if (Array.IndexOf(Hosts, parsed.Host.ToLowerInvariant()) < 0) return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 1;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"/page/(\d+)/"))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        return max;
    }

    // ========== Playlist ==========
    // <div id="video-36784" class="video-item">
    //   <a title="Tên" href="..."><span class="nv-thumb"><img class="video-image" src="...">
    //     <span class="nv-badge nv-q nv-q-fhd">1080p</span><span class="nv-badge nv-dur">31:58</span></span>
    //   <div class="video-name"><a href="...">Tên</a></div>
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;

        var hits = Regex.Matches(html, @"class=""video-item");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < hits.Count; i++)
        {
            int start = hits[i].Index;
            int end = i + 1 < hits.Count ? hits[i + 1].Index : Math.Min(html.Length, start + 4000);
            if (end <= start) continue;
            string block = html.Substring(start, end - start);

            var hm = Regex.Match(block, @"href=""(https?://[^""]+)""", RegexOptions.IgnoreCase);
            if (!hm.Success) continue;

            string pageUrl = NormalizePageUrl(hm.Groups[1].Value);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;

            var tm = Regex.Match(block, @"<div class=""video-name"">[\s\S]*?<a[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase);
            string name = tm.Success
                ? WebUtility.HtmlDecode(Regex.Replace(tm.Groups[1].Value, "<[^>]+>", " ")).Trim()
                : "";

            if (string.IsNullOrEmpty(name))
            {
                var at = Regex.Match(block, @"<a[^>]+title=""([^""]+)""", RegexOptions.IgnoreCase);
                if (at.Success) name = WebUtility.HtmlDecode(at.Groups[1].Value).Trim();
            }
            if (string.IsNullOrEmpty(name)) continue;

            var im = Regex.Match(block, @"<img[^>]+class=""[^""]*video-image[^""]*""[^>]*src=""([^""]+)""", RegexOptions.IgnoreCase);
            if (!im.Success) im = Regex.Match(block, @"<img[^>]+src=""([^""]+)""", RegexOptions.IgnoreCase);
            string poster = im.Success ? NormalizeMediaUrl(im.Groups[1].Value) : null;

            var dm = Regex.Match(block, @"nv-dur[^>]*>\s*([0-9:]{3,8})", RegexOptions.IgnoreCase);
            var qm = Regex.Match(block, @"nv-q[^>]*>\s*([0-9]{3,4}[pPkK])", RegexOptions.IgnoreCase);

            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                time = dm.Success ? dm.Groups[1].Value.Trim() : null,
                quality = qm.Success ? qm.Groups[1].Value.ToUpperInvariant() : null,
                json = true,
                bookmark = new Bookmark() { site = "lon69", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ========== Menu ==========
    // Site (theme vlxx mới) KHÔNG có sort -> không có dòng 2.
    public static List<(string slug, string name)> Categories(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a[^>]+href=""https?://(?:www\.)?lon69\.(?:ink|site|com)/([a-z0-9\-]+)/""[^>]*>([^<]{2,40})</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim().Trim('/');
            string name = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (slug.Length == 0 || name.Length == 0) continue;
            if (slug.StartsWith("page") || slug == "feed" || slug.StartsWith("tag")) continue;
            if (!seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    public static List<(string slug, string name)> Tags(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a[^>]+href=""https?://(?:www\.)?lon69\.(?:ink|site|com)/tag/([^""/]+)/""[^>]*>([^<]{1,40})</a>",
            RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim().ToLowerInvariant();
            string name = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (slug.Length == 0 || name.Length == 0 || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> cats, List<(string slug, string name)> tags)
    {
        string root = host.TrimEnd('/') + "/lon69";
        string url(string c) => root + "?c=" + HttpUtility.UrlEncode(c);

        var res = new List<MenuItem>
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "lon69", playlist_url = root }
        };

        if (cats != null && cats.Count > 0)
            res.Add(new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = cats.Select(x => new MenuItem(x.name, url(x.slug))).ToList()
            });

        if (tags != null && tags.Count > 0)
            res.Add(new MenuItem()
            {
                title = "Thẻ",
                playlist_url = "submenu",
                submenu = tags.Select(x => new MenuItem(x.name, url("tag/" + x.slug))).ToList()
            });

        return res;
    }

    // ========== Popup (long-press) ==========
    // <div class="category-tag"><a href="https://lon69.ink/phim-sex-viet-nam/" rel="category tag">Việt nam</a></div>
    // <div class="actress-tag"><a href="https://lon69.ink/tag/vlxx/" rel="tag">vlxx</a></div>   (nhiều block)
    public static List<(string name, string path)> Chips(string html, bool tags)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        string cls = tags ? "actress-tag" : "category-tag";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match box in Regex.Matches(html,
            @"<div[^>]*class=""[^""]*" + cls + @"[^""]*""[^>]*>([\s\S]{0,600}?)</div>", RegexOptions.IgnoreCase))
        {
            var am = Regex.Match(box.Groups[1].Value,
                @"<a[^>]+href=""https?://(?:www\.)?lon69\.(?:ink|site|com)/((?:tag/)?[^""/]+)/""[^>]*>([\s\S]*?)</a>",
                RegexOptions.IgnoreCase);
            if (!am.Success) continue;

            string path = am.Groups[1].Value.Trim().Trim('/');
            string name = WebUtility.HtmlDecode(Regex.Replace(am.Groups[2].Value, "<[^>]+>", " ")).Trim();
            if (path.Length == 0 || name.Length == 0 || name.Length > 60 || !seen.Add(path)) continue;

            res.Add((name, path));
        }
        return res;
    }

    // <aside id="related_single-3" class="widget related_single ..."> ... <div id="video-list"> ... 30 card
    public static string RelatedBlock(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var m = Regex.Match(html, @"<aside[^>]*class=""[^""]*related_single[^""]*""", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        string tail = html.Substring(m.Index);
        int end = tail.IndexOf("</aside>", StringComparison.OrdinalIgnoreCase);
        return end > 0 ? tail.Substring(0, end) : tail;
    }

    public static List<PlaylistItem> Related(string route, string html)
    {
        string block = RelatedBlock(html);
        return string.IsNullOrEmpty(block) ? new List<PlaylistItem>() : Playlist(route, block);
    }

    // ========== Player ==========
    // detail: <noscript><iframe src="https://sever1.playphe3x.space/play.php?id=...">
    public static string PlayerUrl(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var m = Regex.Match(html, @"<iframe[^>]+src=""(https?://[^""]+play[^""]*)""", RegexOptions.IgnoreCase);
        return m.Success ? NormalizeMediaUrl(m.Groups[1].Value) : null;
    }

    // player html: let SRC = "https://sever4.playphe3x.space/hls.php?id=...&p=master.m3u8&token=..."
    // (& bị escape thành \u0026). QUẢNG CÁO preroll sever3.../tvc.mp4 nằm cùng trang -> phải loại.
    public static bool IsAd(string url)
        => !string.IsNullOrEmpty(url) &&
           (url.Contains("tvc.mp4", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/tvc", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("advideo", StringComparison.OrdinalIgnoreCase));

    public static string SourceFromPlayer(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        static string unesc(string u) => u.Replace("\\u0026", "&").Replace("\\/", "/").Replace("&amp;", "&").Trim();

        // 1. biến SRC/MAIN của player
        var m = Regex.Match(html, @"\b(?:SRC|PLAY_SRC|MAIN_SRC)\s*=\s*""([^""]+)""");
        if (m.Success)
        {
            string u = unesc(m.Groups[1].Value);
            if (u.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !IsAd(u)) return u;
        }

        // 2. link hls.php (đuôi .m3u8 nằm trong query `p=master.m3u8`, không ở path)
        var h = Regex.Match(html, @"https?://[^""'\s]+hls\.php\?[^""'\s]+");
        if (h.Success && !IsAd(h.Value)) return unesc(h.Value);

        // 3. .m3u8 / .mp4 thật (bỏ qua quảng cáo)
        foreach (Match f in Regex.Matches(html, @"https?://[^""'\s]+?\.(?:m3u8|mp4)[^""'\s]*"))
        {
            string u = unesc(f.Value);
            if (!IsAd(u)) return u;
        }

        return null;
    }

    public static bool IsHls(string url)
        => !string.IsNullOrEmpty(url) &&
           (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("master.m3u8", StringComparison.OrdinalIgnoreCase));
}
