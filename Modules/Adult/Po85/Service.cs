using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
                // trang chu "Dang xem" tren web khong phan trang bang ?from= (lap)
                // dung latest-updates de trang 2+ ra phim moi nhat, offset KVS 60/ph
                int from = (pg - 1) * 60 + 1;
                url.Append($"latest-updates/?from={from}");
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

    /// <summary>
    /// Ban /vi/ cua trang phim: flashvars URL co prefix /vi/get_file/
    /// (ban thuong khong co) — chi URL /vi/ moi mo duoc file 2160p.
    /// </summary>
    public static string ViPage(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return uri;

        try
        {
            var u = new Uri(uri, UriKind.Absolute);
            string path = u.AbsolutePath;
            if (path.StartsWith("/vi/"))
                return uri;

            if (path.StartsWith("/v/") || path.StartsWith("/watch/"))
                return $"{u.Scheme}://{u.Authority}/vi{path}{u.Query}";
        }
        catch { }

        return uri;
    }

    /// <summary>
    /// Tach URL 2160p tu flashvars (video_alt_url3, co the boc function/0/).
    /// </summary>
    public static string ParseUhd(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        string s = html.ToString();
        var m = System.Text.RegularExpressions.Regex.Match(s,
            @"video_alt_url3:\s*'(?:function/\d+/)?([^']+)'");
        if (!m.Success)
            return null;

        string uhd = m.Groups[1].Value.Replace("\\/", "/");
        if (!uhd.StartsWith("http"))
            return null;

        if (!uhd.Contains("2160") && !uhd.ToLowerInvariant().Contains("4k"))
            return null;

        return uhd;
    }

    static readonly System.Net.Http.HttpClient uhdClient = FriendlyHttp.CreateHttpClient();

    /// <summary>
    /// Nho node resolver (127.0.0.1:9196, chay bang Chrome that) mo file 4K,
    /// tra ve signed CDN URL. Cache 15 phut theo id phim.
    /// </summary>
    public static async Task<string> ResolveUhd(string pageUrl, string fileUrl)
    {
        try
        {
            var idm = System.Text.RegularExpressions.Regex.Match(pageUrl ?? "", @"/v/([0-9]+)/");
            string vid = idm.Success ? idm.Groups[1].Value : (pageUrl ?? fileUrl);

            var memoryCache = HybridCache.GetMemory();
            string cacheKey = $"Po85_uhd_{vid}";

            if (memoryCache.TryGetValue(cacheKey, out string cached))
                return string.IsNullOrEmpty(cached) ? null : cached;

            string req = "http://127.0.0.1:9196/r?page=" + HttpUtility.UrlEncode(pageUrl)
                + "&file=" + HttpUtility.UrlEncode(fileUrl);

            string signed = null;
            using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(45)))
            {
                string json = await uhdClient.GetStringAsync(req, cts.Token);
                var jm = System.Text.RegularExpressions.Regex.Match(json ?? "", "\"location\"\\s*:\\s*\"([^\"]+)\"");
                if (jm.Success)
                    signed = jm.Groups[1].Value;
            }

            if (!string.IsNullOrEmpty(signed) && signed.Contains("2160p"))
            {
                if (CoreInit.conf.lowMemoryMode == false)
                    memoryCache.Set(cacheKey, signed, TimeSpan.FromMinutes(15));
                return signed;
            }

            memoryCache.Set(cacheKey, "", TimeSpan.FromMinutes(3));
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string> StreamLinks(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        var stream_links = new Dictionary<string, string>(2);

        // Link flashvars (video_url / video_alt_url*) bi Cloudflare chan khi tai
        // server-side (403/404, hash gan voi phien xem tren trinh duyet that),
        // chi dung link download dropdown (toi da 1080p) de phat duoc.

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
            return 1050;
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
