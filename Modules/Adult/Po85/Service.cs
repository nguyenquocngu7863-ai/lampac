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
    public static string Uri(string host, string search, string sort, string c, string t, int pg)
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
        else if (!string.IsNullOrEmpty(t))
        {
            url.Append("tags/");
            url.Append(t);
            url.Append("/?from=");
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
        else if (sort == "latest-updates")
        {
            url.Append("latest-updates/?from=");
            url.Append(pg);
        }
        else
        {
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
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
    public static List<MenuItem> Menu(string host, string search, string sort, string c, string t)
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
        string menuKey = $"Po85_menu_{host}_{sort}_{c}_{t}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        var tagmenu = new List<MenuItem>(27)
        {
            new("Tự chụp 自拍", $"{url}?t=zi-pai"),
            new("Tự sướng 自慰", $"{url}?t=zi-wei"),
            new("Nghèo nàn 貧乳", $"{url}?t=pin-ru"),
            new("Em gái 妹", $"{url}?t=mei"),
            new("Trên giường 床上", $"{url}?t=chuang-shang"),
            new("Bướm non 嫩逼", $"{url}?t=nen-bi"),
            new("Muội muội 妹妹", $"{url}?t=mei-mei"),
            new("Khỏa thân 全裸", $"{url}?t=quan-luo"),
            new("Rên rỉ 淫叫", $"{url}?t=yin-jiao"),
            new("Banh bướm 掰逼", $"{url}?t=bai-bi"),
            new("Bào ngư 鮑魚", $"{url}?t=bao-yu"),
            new("Mông đẹp 美臀", $"{url}?t=mei-tun"),
            new("Vú to 大奶", $"{url}?t=da-nai"),
            new("Quần lót 內褲", $"{url}?t=nei-ku2"),
            new("Lỗ đít 屁眼", $"{url}?t=pi-yan"),
            new("Em gái 妹子", $"{url}?t=mei-zi"),
            new("Nhật Bản 日本", $"{url}?t=ri-ben"),
            new("Bú cu 口交", $"{url}?t=kou-jiao"),
            new("Dễ thương 可愛", $"{url}?t=ke-ai"),
            new("Làm tình 做愛", $"{url}?t=zuo-ai"),
            new("Vú khủng 巨乳", $"{url}?t=ju-ru"),
            new("Cởi đồ 脫衣", $"{url}?t=tuo-yi"),
            new("Lên đỉnh 高潮", $"{url}?t=gao-chao"),
            new("Phun nước 噴水", $"{url}?t=pen-shui"),
            new("Đài Loan 台灣", $"{url}?t=tai-wan"),
            new("Học sinh 學生", $"{url}?t=xue-sheng"),
        };

        menu = new List<MenuItem>(4)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = url,
            },
            new MenuItem()
            {
                title = $"Sắp xếp: {(string.IsNullOrEmpty(sort) ? "Trang chủ" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(5)
                {
                    new("Trang chủ (Đang xem)", $"{url}?c={c}&t={t}"),
                    new("Mới nhất", $"{url}?c={c}&t={t}&sort=latest-updates"),
                    new("4K", $"{url}?c={c}&t={t}&sort=4k"),
                    new("Đánh giá cao", $"{url}?c={c}&t={t}&sort=top-rated"),
                    new("Xem nhiều nhất", $"{url}?c={c}&t={t}&sort=most-popular")
                }
            },
            new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = tagmenu
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

        // flashvars: video_url / video_alt_url / video_alt_url2 / video_alt_url3 (4K):
        // 'function/0/https://...get_file...mp4/?br=2847', kem (name)_text: '4K'
        string htmlStr = html.ToString();
        foreach (System.Text.RegularExpressions.Match vm in
            System.Text.RegularExpressions.Regex.Matches(htmlStr,
            @"(video_(?:alt_)?url\d*):\s*'([^']+)'"))
        {
            string varName = vm.Groups[1].Value;
            string vurl = vm.Groups[2].Value;
            if (vurl.StartsWith("function/"))
            {
                var fm = System.Text.RegularExpressions.Regex.Match(vurl, @"^function/\d+/(.+)$");
                if (!fm.Success)
                    continue;
                vurl = fm.Groups[1].Value;
            }
            if (!vurl.StartsWith("http"))
                continue;
            vurl = vurl.Replace("\\/", "/");
            string vlabel = System.Text.RegularExpressions.Regex.Match(htmlStr,
                varName + @"_text:\s*'([^']+)'").Groups[1].Value;
            if (string.IsNullOrEmpty(vlabel))
            {
                var brm = System.Text.RegularExpressions.Regex.Match(vurl, @"[?&]br=(\d+)");
                vlabel = brm.Success ? $"br{brm.Groups[1].Value}" : "mp4";
            }
            stream_links.TryAdd(vlabel, vurl);
        }

        // link download MP4 (dropdown): moi quality mot hash rieng
        // ("MP4 480p, ...", "MP4 720p, ...", "MP4 1080p, ...")
        foreach (System.Text.RegularExpressions.Match dlm in
            System.Text.RegularExpressions.Regex.Matches(html.ToString(),
            @"<a[^>]*href=""(https?://[^'""]+/get_file/[^'""]+download=true[^'""]*)""[^>]*>([^<]+)</a>"))
        {
            string dlurl = dlm.Groups[1].Value.Replace("&amp;", "&");
            string label = dlm.Groups[2].Value.Trim();
            if (string.IsNullOrEmpty(label))
                label = "download";
            stream_links.TryAdd(label, dlurl);
        }

        return stream_links.OrderByDescending(kv => StreamQualityRank(kv.Key + " " + kv.Value))
            .ToDictionary(k => k.Key, v => v.Value);
    }
    #endregion

    #region StreamQualityRank
    static int StreamQualityRank(string s)
    {
        string l = s.ToLowerInvariant();
        if (l.Contains("2160") || l.Contains("4k"))
            return 2160;
        if (l.Contains("1080"))
            return 1080;
        if (l.Contains("720"))
            return 720;
        if (l.Contains("480"))
            return 480;
        if (l.Contains("360"))
            return 360;
        return 0;
    }
    #endregion
}
