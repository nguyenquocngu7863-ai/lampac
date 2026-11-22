using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Web;
namespace FapClub;
public static class FapClubTo
{
    public const string SiteHost = "https://xxx.fapclub.vip";

    // home: / ; page: /latest-updates/{pg}/ ; category: /categories/{slug}/[{pg}/]
    // search: /search/?q={q} ; search page: /search/{pg}/?q={q}
    public static string Uri(string host, string category, string search, int pg)
    {
        string uri;
        if (!string.IsNullOrEmpty(search))
            uri = pg > 1 ? $"{host}/search/{pg}/?q={HttpUtility.UrlEncode(search)}"
                         : $"{host}/search/?q={HttpUtility.UrlEncode(search)}";
        else if (!string.IsNullOrEmpty(category))
            uri = pg > 1 ? $"{host}/categories/{category}/{pg}/"
                         : $"{host}/categories/{category}/";
        else
            uri = pg > 1 ? $"{host}/latest-updates/{pg}/" : $"{host}/";
        return uri;
    }

    public static List<PlaylistItem> Playlist(string host, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;
        var cards = Rx.Split("<div class=\"video\">", html, 1);
        if (cards.Count == 0)
            return null;
        var playlists = new List<PlaylistItem>(cards.Count);
        foreach (var card in cards.Rows())
        {
            string href = card.Match("<a href=\"([^\"]+/videos/[^\"]+)\"");
            if (string.IsNullOrEmpty(href))
                continue;
            string title = card.Match("<h2>\\s*<a href=\"[^\"]+\" title=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(title))
                title = card.Match("alt=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            string img = card.Match("<img src=\"([^\"]+)\"");
            string time = card.Match("<div class=\"time\">([^<]+)</div>", trim: true);
            string uri = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : host + (href.StartsWith("/") ? "" : "/") + href;
            uri = uri.Replace(SiteHost, "");
            playlists.Add(new PlaylistItem()
            {
                name = HttpUtility.HtmlDecode(title),
                video = "fapclub/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                picture = img,
                time = time,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "fapclub",
                    href = uri,
                    image = img
                }
            });
        }
        return playlists.Count > 0 ? playlists : null;
    }

    // Chỉ nhận /videos/{id}/{slug}/ — chặn SSRF/host lạ
    public static string DetailUrl(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;
        uri = uri.Trim();
        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!uri.StartsWith(SiteHost + "/", StringComparison.OrdinalIgnoreCase))
                return null;
            uri = uri.Substring(SiteHost.Length);
        }
        if (!uri.StartsWith("/videos/", StringComparison.OrdinalIgnoreCase))
            return null;
        if (uri.IndexOfAny(new[] { '?', '#', ' ', '\r', '\n' }) >= 0)
            return null;
        return uri;
    }

    // data-q = "1080p;hash;FHD 1080p;2027750;1795333475;iCL9...,720p;..."
    // stream  = https://{n}.vstor.top/whpvid/{ts}/{token}/{folder}/{id}/{id}_{quality}.mp4
    // folder  = floor(id/1000)*1000
    public static StreamItem Stream(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;
        string dataQ = Rx.Match(html, "id=\"player\"[^>]*data-q=\"([^\"]+)\"");
        string dataN = Rx.Match(html, "id=\"player\"[^>]*data-n=\"([^\"]+)\"");
        string dataId = Rx.Match(html, "id=\"player\"[^>]*data-id=\"([^\"]+)\"");
        if (string.IsNullOrEmpty(dataQ) || string.IsNullOrEmpty(dataN) || string.IsNullOrEmpty(dataId))
            return null;
        if (!int.TryParse(dataId, out int id) || id <= 0)
            return null;
        string folder = (id / 1000 * 1000).ToString();
        var qualitys = new Dictionary<string, string>();
        string lastKey = null;
        foreach (string part in dataQ.Replace("&nbsp;", " ").Split(','))
        {
            string[] f = part.Split(';');
            if (f.Length < 6)
                continue;
            string quality = f[0].Trim();
            string ts = f[4].Trim();
            string token = f[5].Trim();
            if (string.IsNullOrEmpty(quality) || string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(token))
                continue;
            string label = quality.Equals("1080p", StringComparison.OrdinalIgnoreCase) ? "1080p" :
                           quality.Equals("720p", StringComparison.OrdinalIgnoreCase) ? "720p" :
                           quality.Equals("480p", StringComparison.OrdinalIgnoreCase) ? "480p" : quality;
            string url = $"https://{dataN}.vstor.top/whpvid/{ts}/{token}/{folder}/{id}/{id}_{quality}.mp4";
            qualitys[label] = url;
            lastKey = label;
        }
        if (qualitys.Count == 0)
            return null;
        if (!qualitys.ContainsKey("auto"))
        {
            foreach (string q in new[] { "1080p", "720p", "480p", "240p" })
            {
                if (qualitys.TryGetValue(q, out string u)) { qualitys["auto"] = u; break; }
            }
        }
        return new StreamItem() { qualitys = qualitys };
    }

    // Menu: 50 category, lấy trực tiếp từ trang chủ (đều tiếng Anh — đạt luật cứng)
    public static List<MenuItem> Categories(ReadOnlySpan<char> html)
    {
        var result = new List<MenuItem>();
        if (html.IsEmpty)
            return result;
        var rx = Rx.Split("href=\"https://xxx.fapclub.vip/categories/", html, 1);
        if (rx.Count == 0)
            return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rx.Rows())
        {
            string slug = row.Match("^([a-z0-9-]+)/\"");
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;
            string title = row.Match("\"[^>]*>([^<]+)</a>");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            result.Add(new MenuItem(HttpUtility.HtmlDecode(title), "fapclub?c=" + slug));
        }
        return result;
    }
}
