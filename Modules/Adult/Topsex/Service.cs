using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using Shared.Models.SISI.Base;

namespace Topsex;

public static class TopsexTo
{
    public static string SiteHost = "https://s1.topsex.blog";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    public static string Uri(string host, string search, string c, int pg, string sort = null)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        string page = pg > 1 ? "?page=" + pg : "";

        if (!string.IsNullOrWhiteSpace(search))
        {
            return host + "/?search=" + HttpUtility.UrlEncode(search) + page;
        }

        if (!string.IsNullOrEmpty(c))
        {
            string url = c.StartsWith("http") ? c : host + (c.StartsWith("/") ? c : "/" + c);
            string q = pg > 1 ? (url.Contains("?") ? "&" : "?") + "page=" + pg : "";
            return url + q;
        }

        // home
        return host + page;
    }

    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();

        // Pattern: <a class="m-block movie-item" href="..." title="...">
        //          <div class="lazyload public-film-item-thumb ratio-content" data-original="...">
        // href có thể là full URL (https://s1.topsex.blog/phim/...) hoặc relative (/phim/...)
        foreach (Match m in Regex.Matches(html, 
            @"<a\s+class=""m-block\s+movie-item""\s+href=""(?:https://s1\.topsex\.blog)?(/phim/[^""]+)""\s+title=""([^""]+)"">[\s\S]{0,800}?data-original=""([^""]+)"""))
        {
            string href = m.Groups[1].Value;
            if (!href.StartsWith("/"))
                href = "/" + href;

            string fullHref = SiteHost + href;
            if (!seen.Add(fullHref))
                continue;

            string title = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            string thumb = m.Groups[3].Value;

            if (string.IsNullOrEmpty(title))
                continue;

            if (thumb.StartsWith("/"))
                thumb = SiteHost + thumb;

            var item = new PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = title,
                picture = thumb,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "topsex",
                    href = href,
                    image = thumb
                }
            };

            playlists.Add(item);
        }

        return playlists;
    }

    // Parse player từ trang detail
    // Pattern: <a onclick="chooseStreamingServer(this)" ... data-link="..." data-type="...">
    public static List<(string name, string link, string type)> StreamServers(string html)
    {
        var servers = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return servers;

        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html,
            @"<a\s+onclick=""chooseStreamingServer\(this\)""[^>]+data-link=""([^""]+)""[^>]+data-type=""([^""]+)"""))
        {
            string link = m.Groups[1].Value;
            string type = m.Groups[2].Value;

            if (string.IsNullOrEmpty(link) || !seen.Add(link))
                continue;

            string serverName = $"Server {servers.Count + 1}";
            servers.Add((serverName, link, type));
        }

        return servers;
    }

    // Lấy title, description, thumb từ trang detail
    public static (string title, string description, string thumb) DetailInfo(string html)
    {
        if (string.IsNullOrEmpty(html))
            return ("", "", "");

        string title = "";
        var tm = Regex.Match(html, @"<h1\s+class=""header-title""[^>]*>[\s\S]*?<a[^>]*title=""([^""]+)""");
        if (tm.Success)
            title = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());

        string thumb = "";
        var im = Regex.Match(html, @"<meta\s+property=""og:image""\s+content=""([^""]+)""");
        if (im.Success)
            thumb = im.Groups[1].Value;

        string desc = "";
        var dm = Regex.Match(html, @"<meta\s+property=""og:description""\s+content=""([^""]+)""");
        if (dm.Success)
            desc = HttpUtility.HtmlDecode(dm.Groups[1].Value);

        return (title, desc, thumb);
    }

    // Trích link stream thật (googlevideo.com/videoplayback mp4) từ trang embed blogger.
    // Blogger mã hóa entity: \u003d = '=', \u0026 = '&', link được bao bởi &quot;
    public static string ExtractMp4(string embedHtml)
    {
        if (string.IsNullOrEmpty(embedHtml))
            return null;

        var html = embedHtml.Replace("\\u003d", "=").Replace("\\u0026", "&");

        int idx = html.IndexOf("googlevideo.com/videoplayback");
        if (idx < 0)
            return null;

        int start = html.LastIndexOf("https://", idx);
        if (start < 0)
            start = html.LastIndexOf("http://", idx);
        if (start < 0)
            return null;

        int end = html.IndexOf("&quot;", idx);
        if (end < 0)
            end = html.IndexOf('"', idx);
        if (end < 0)
            end = html.Length;

        return html.Substring(start, end - start);
    }

    // Menu categories từ trang chủ
    public static List<(string name, string path)> Categories(string html)
    {
        var cats = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return cats;

        var seen = new HashSet<string>();

        // Pattern: <li><a href="/the-loai/...">Ten</a></li> hoặc <li><a href="/quoc-gia/...">Ten</a></li>
        foreach (Match m in Regex.Matches(html,
            @"<li><a\s+href=""(/(?:the-loai|quoc-gia)/[^""]+)""[^>]*>([^<]+)</a></li>"))
        {
            string path = m.Groups[1].Value;
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());

            if (string.IsNullOrEmpty(name) || !seen.Add(path))
                continue;

            cats.Add((name, path));
        }

        return cats;
    }
}
