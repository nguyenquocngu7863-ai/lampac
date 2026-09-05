using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Po85;

public static class Po85To
{
    #region Uri
    public static string Uri(string host, string search, string sort, string c, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("search/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/?from_videos=");
            url.Append(pg);
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append("categories/");
            url.Append(c);
            url.Append("/?from=");
            url.Append(pg);
        }
        else if (sort == "4k")
        {
            url.Append("4k/?from=");
            url.Append(pg);
        }
        else if (sort == "top-rated")
        {
            url.Append("top-rated/?from=");
            url.Append(pg);
        }
        else if (sort == "most-popular")
        {
            url.Append("most-popular/?from=");
            url.Append(pg);
        }
        else
        {
            url.Append("latest-updates/?from=");
            url.Append(pg);
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("<div class=\"thumb", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups("<a href=\"((?:https?://[^/]+)?/v/[0-9]+/[^\\\"]+)\"[^>]*title=\"([^\\\"]+)\"");

            if (string.IsNullOrWhiteSpace(g[1].Value) || string.IsNullOrWhiteSpace(g[2].Value))
                continue;

            string href = g[1].Value;
            if (href.StartsWith("/"))
                href = $"https://www.85po.com{href}";

            var img = row.Groups("data-original=\"([^\"]+)\"");
            string picture = img[1].Value;
            if (string.IsNullOrEmpty(picture))
                picture = row.Match("data-webp=\"([^\"]+)\"");
            if (!string.IsNullOrEmpty(picture) && picture.StartsWith("/"))
                picture = $"https://www.85po.com{picture}";

            // class "qualtiy" (sic) tren 85po, fallback "quality"
            string quality = row.Match("<div class=\"qualtiy[^\"]*\">([^<]+)</div>", trim: true);
            if (string.IsNullOrEmpty(quality))
                quality = row.Match("<div class=\"quality[^\"]*\">([^<]+)</div>", trim: true);

            string time = row.Match("<div class=\"time\"[^>]*>(.*?)</div>", trim: true);

            var idm = System.Text.RegularExpressions.Regex.Match(href, @"/v/([0-9]+)/");
            string vid = idm.Success ? idm.Groups[1].Value : href;

            var pl = new PlaylistItem()
            {
                video = $"{uri}?uri={HttpUtility.UrlEncode(href)}",
                name = HttpUtility.HtmlDecode(g[2].Value),
                picture = picture,
                quality = quality,
                time = System.Text.RegularExpressions.Regex.Replace(time ?? "", "<[^>]+>", "").Trim(),
                json = true,
                bookmark = new Bookmark()
                {
                    site = "po85",
                    href = vid,
                    image = picture
                }
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string search, string sort, string c)
    {
        string url = $"{host}/po85";

        #region search menu
        if (!string.IsNullOrEmpty(search))
        {
            string encodesearch = HttpUtility.UrlEncode(search);

            return new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm",
                    search_on = "search_on",
                    playlist_url = url,
                },
                new MenuItem()
                {
                    title = $"Sắp xếp: {(string.IsNullOrEmpty(sort) ? "Mới nhất" : sort)}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>()
                    {
                        new MenuItem()
                        {
                            title = "Mới nhất",
                            playlist_url = $"{url}?c={c}&search={encodesearch}"
                        },
                        new MenuItem()
                        {
                            title = "Xem nhiều nhất",
                            playlist_url = $"{url}?c={c}&sort=most-popular&search={encodesearch}"
                        }
                    }
                }
            };
        }
        #endregion

        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Po85_menu_{host}_{sort}_{c}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        menu = new List<MenuItem>(3)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = url,
            },
            new MenuItem()
            {
                title = $"Sắp xếp: {(string.IsNullOrEmpty(sort) ? "Mới nhất" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(4)
                {
                    new("Mới nhất", $"{url}?c={c}"),
                    new("4K", $"{url}?c={c}&sort=4k"),
                    new("Đánh giá cao", $"{url}?c={c}&sort=top-rated"),
                    new("Xem nhiều nhất", $"{url}?c={c}&sort=most-popular")
                }
            }
        };

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        return uri;
    }

    public static Dictionary<string, string> StreamLinks(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        var stream_links = new Dictionary<string, string>(2);

        // flashvars: video_url: 'function/0/https://...get_file...mp4/?br=446'
        string vurl = Rx.Match(html, @"video_url:\s*'function/\d+/([^']+)'");
        if (string.IsNullOrEmpty(vurl))
            vurl = Rx.Match(html, @"video_url:\s*'([^']+)'");

        if (!string.IsNullOrEmpty(vurl))
        {
            vurl = vurl.Replace("\\/", "/");
            string label = "mp4";
            var brm = System.Text.RegularExpressions.Regex.Match(vurl, @"[?&]br=(\d+)");
            if (brm.Success)
                label = $"br{brm.Groups[1].Value}";
            stream_links.TryAdd(label, vurl);
        }

        // link download MP4 (dropdown): lay ca nhan "MP4 480p, 18.93 Mb" lam label
        var dlm = System.Text.RegularExpressions.Regex.Match(html.ToString(),
            @"<a[^>]*href=""(https?://[^'""]+/get_file/[^'""]+download=true[^'""]*)""[^>]*>([^<]+)</a>");
        if (dlm.Success)
        {
            string dlurl = dlm.Groups[1].Value.Replace("&amp;", "&");
            string label = dlm.Groups[2].Value.Trim();
            if (string.IsNullOrEmpty(label))
                label = "download";
            stream_links.TryAdd(label, dlurl);
        }
        else
        {
            string dl = Rx.Match(html, @"(https?://[^'""]+/get_file/[^'""]+download=true[^'""]*)");
            if (!string.IsNullOrEmpty(dl))
                stream_links.TryAdd("download", dl.Replace("&amp;", "&"));
        }

        return stream_links.Reverse().ToDictionary(k => k.Key, v => v.Value);
    }
    #endregion
}
