using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services.HTML;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace Spankbang;

public static class SpankbangTo
{
    #region Uri
    public static string Uri(string host, string search, string sort, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("s/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/");
            url.Append(pg);
            url.Append("/");
        }
        else
        {
            url.Append($"{sort ?? "new_videos"}/{pg}/");

            if (sort == "most_popular")
                url.Append("?p=w");
        }

        return url.ToString();
    }

    // List theo DUONG DAN cua site (trang dien vien / kenh / tag) — chip popup.
    // Vi du path="/a1e/pornstar/big+tre/" pg=2 -> host/a1e/pornstar/big+tre/2/
    public static string UriPath(string host, string path, int pg)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        path = path.Trim();
        if (path.StartsWith("//") || path.Contains("://")) return null;   // chan SSRF
        if (!path.StartsWith('/')) path = "/" + path;
        if (!path.EndsWith('/')) path += "/";

        return $"{host}{path}{pg}/";
    }

    // uri noi bo (a5f9q/video/...) -> URL trang chi tiet
    public static string PageUri(string host, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        uri = uri.Trim();
        if (uri.Contains("://") || uri.StartsWith("//")) return null;

        return $"{host}/{(uri.StartsWith('/') ? uri.Substring(1) : uri)}";
    }
    #endregion

    // Nhan menu dong "Sap xep" — khong co CJK/Russian (LUAT 2026-11-07).
    public static string SortLabel(string sort) => sort switch
    {
        "trending_videos" => "Xu hướng",
        "most_popular" => "Phổ biến",
        _ => "mới"
    };

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        ReadOnlySpan<char> container = html.Contains("class=\"main-container\"", StringComparison.Ordinal)
            ? Rx.Split("class=\"main-container\"", html)[1].Span
            : html;

        var playlists = new List<PlaylistItem>(80);

        foreach (ReadOnlySpan<char> row in HtmlSpan.Nodes(container, "div", "data-testid", "video-item", HtmlSpanTargetType.Exact))
        {
            var g = Rx.Groups(row, "<a href=\"/(?<link>[^\"]+)\"(?:[^>]*title=\"(?<title>[^\"]+)\")?");
            string link = g["link"].Value;
            string name = g["title"].Value;

            // card o khoi "lien quan" khong co title -> lay tu alt cua <img>
            if (string.IsNullOrWhiteSpace(name))
                name = Rx.Match(row, "alt=\"([^\"]+)\"");

            if (!string.IsNullOrWhiteSpace(link) && !string.IsNullOrWhiteSpace(name))
            {
                string img = Rx.Match(row, "([\n\r\t ]+)src=\"([^\"]+)\"", 2) ?? string.Empty;
                if (!img.Contains("/w:"))
                    img = Rx.Match(row, "data-src=\"([^\"]+)\"");

                if (img == null)
                    continue;

                img = Regex.Replace(img, "/w:[0-9]00/", "/w:300/").Replace("http://", "https://");

                string preview = Rx.Match(row, "data-preview=\"([^\"]+)\"");
                if (preview == null)
                    preview = Rx.Match(row, "<source data-src=\"([^\"]+)\"");

                var pl = new PlaylistItem()
                {
                    name = name,
                    video = $"{route}?uri={link}",
                    quality = Rx.Match(row, "\"video-item-resolution\"[^>]*>\\s*([^<]+)</")?.Trim(),
                    picture = img,
                    preview = preview,
                    time = Rx.Match(row, "\"video-item-length\"[^>]*>\\s*([^<]+)</")?.Trim(),
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "sbg",
                        href = link,
                        image = img
                    }
                };

                if (onplaylist != null)
                    pl = onplaylist.Invoke(pl);

                playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion

    // LUAT menu: chi Viet/Anh -> bo chip ten tieng Nhat/Trung/Nga...
    public static bool HasCjk(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (c >= 0x3040 && c <= 0x30FF) return true; // kana
            if (c >= 0x3400 && c <= 0x9FFF) return true; // kanji / han
            if (c >= 0xAC00 && c <= 0xD7AF) return true; // hangul
            if (c >= 0x0E00 && c <= 0x0E7F) return true; // thai
            if (c >= 0x0400 && c <= 0x04FF) return true; // cyrillic
        }
        return false;
    }

    public class Chip
    {
        public string title { get; set; }
        public string path { get; set; }
    }

    public class InfoBox
    {
        public List<Chip> cast { get; set; }
        public List<Chip> channels { get; set; }
        public List<Chip> tags { get; set; }
    }

    // Trang chi tiet -> chip popup. Vung thong tin nam ngay sau <h1>:
    //   <a href="/mh/channel/pornstarplatinum/">  (kenh)
    //   <a href="/a1e/pornstar/big+tre/">         (dien vien)
    //   <a href="/s/anal/">                       (tag)
    // Cat truoc khoi video lien quan (data-testid="video-item").
    public static InfoBox Info(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        int h1 = html.IndexOf("<h1", StringComparison.Ordinal);
        if (h1 < 0) return null;

        string seg = html.Substring(h1);
        int rel = seg.IndexOf("data-testid=\"video-item\"", StringComparison.Ordinal);
        if (rel > 0) seg = seg.Substring(0, rel);

        var box = new InfoBox
        {
            cast = new List<Chip>(),
            channels = new List<Chip>(),
            tags = new List<Chip>()
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(seg, "<a[^>]+href=\"(/[a-z0-9]+/(pornstar|channel)/[^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string path = m.Groups[1].Value.Trim();
            string kind = m.Groups[2].Value.ToLowerInvariant();
            string title = Clean(m.Groups[3].Value);

            if (string.IsNullOrEmpty(title) || HasCjk(title) || !seen.Add(path)) continue;

            (kind == "channel" ? box.channels : box.cast).Add(new Chip() { title = title, path = path });
        }

        foreach (Match m in Regex.Matches(seg, "<a[^>]+href=\"(/s/[^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string path = m.Groups[1].Value.Trim();
            string title = Clean(m.Groups[2].Value);

            if (string.IsNullOrEmpty(title) || HasCjk(title) || !seen.Add(path)) continue;

            box.tags.Add(new Chip() { title = title, path = path });
            if (box.tags.Count >= 16) break;
        }

        return box;
    }

    static string Clean(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        string t = Regex.Replace(html, "<[^>]+>", " ");
        t = Regex.Replace(t, "\\s+", " ").Trim();
        return WebUtility.HtmlDecode(t);
    }

    #region Menu
    public static List<MenuItem> Menu(string host, string sort)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Spankbang_menu_{host}_{sort}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
        return menu;

        menu = new List<MenuItem>(2)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = $"{host}/sbg",
            },
            new MenuItem()
            {
                title = $"Sắp xếp: {SpankbangTo.SortLabel(sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(3)
                {
                    new("Mới", $"{host}/sbg"),
                    new("Xu hướng", $"{host}/sbg?sort=trending_videos"),
                    new("Phổ biến", $"{host}/sbg?sort=most_popular")
                }
            }
        };

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        return $"{host}/{HttpUtility.UrlEncode(url)}";
    }

    public static StreamItem StreamLinks(string route, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Matches("'([0-9]+)(p|k)': ?\\[\'(https?://[^']+)\'", html);
        if (rx.Count == 0)
            return null;

        var stream_links = new Dictionary<int, string>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups();
            if (string.IsNullOrEmpty(g[1].Value))
                continue;

            int q = $"{g[1].Value}{g[2].Value}" == "4k" ? 2160 : -1;
            if (q == -1)
                int.TryParse(g[1].Value, out q);

            stream_links.TryAdd(q, g[3].Value);
        }

        return new StreamItem()
        {
            qualitys = stream_links.OrderByDescending(i => i.Key).ToDictionary(k => $"{k.Key}p", v => v.Value),
            recomends = Playlist(route, html)
        };
    }
    #endregion
}
