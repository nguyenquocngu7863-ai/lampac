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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Po85;

public static class Po85To
{
    #region Uri
    public static string Uri(string host, string search, string sort, string c, string t, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        // Sort that kieu KVS `?sort_by=` (do live 2026-10-08: ca 6 doi list
        // that tren home/genre/tag/search). Sort cu (latest-updates/
        // top-rated/most-popular/4k) GIU NGUYEN path rieng de bookmark cu
        // khong doi noi dung (/top-rated/ != /?sort_by=rating, do live).
        string s = NormalizeSort(sort);
        bool legacy = IsLegacySort(sort);

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("search/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/");
            string sq = string.IsNullOrEmpty(s) ? "" : "?sort_by=" + s;
            if (pg > 1)
            {
                // Search phan trang qua AJAX (giong web): ?mode=async&function=get_block&...
                url.Append(sq.Length == 0 ? "?" : sq + "&");
                url.Append("mode=async&function=get_block&block_id=list_videos_videos_list_search_result&q=");
                url.Append(HttpUtility.UrlEncode(search));
                url.Append("&from_videos=");
                url.Append(pg);
                url.Append("&from_albums=");
                url.Append(pg);
            }
            else if (sq.Length > 0)
                url.Append(sq);
        }
        else if (!string.IsNullOrEmpty(t))
        {
            url.Append("tags/");
            url.Append(t);
            url.Append("/");
            string tq = string.IsNullOrEmpty(s) ? "" : "?sort_by=" + s;
            if (pg > 1)
            {
                url.Append(tq.Length == 0 ? "?from=" : tq + "&from=");
                url.Append(pg);
            }
            else if (tq.Length > 0)
                url.Append(tq);
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append("categories/");
            url.Append(c);
            url.Append("/");
            string cq = string.IsNullOrEmpty(s) ? "" : "?sort_by=" + s;
            if (pg > 1)
            {
                url.Append(cq.Length == 0 ? "?from=" : cq + "&from=");
                url.Append(pg);
            }
            else if (cq.Length > 0)
                url.Append(cq);
        }
        else if (legacy)
        {
            if (sort == "4k")
            {
                // trang /4k/ chet 404 tren design moi -> rot ve moi nhat
                url.Append("latest-updates/");
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
        }
        else if (!string.IsNullOrEmpty(s))
        {
            // Home + sort_by (thay path rieng).
            url.Append("?sort_by=");
            url.Append(s);
            if (pg > 1)
            {
                url.Append("&from=");
                url.Append(pg);
            }
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

        // New HTML: <div class="item "> <a href="https://www.85po.com/video/{id}/{slug}/" title="...">
        var rx = Rx.Split("<div class=\"item", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups("<a\\s+href=\"(https://www\\.85po\\.com/video/([0-9]+)/([^\"]+))\"\\s+title=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(g[1].Value) || string.IsNullOrWhiteSpace(g[4].Value))
                continue;

            string href = "/video/" + g[2].Value + "/" + g[3].Value.Trim('/') + "/";
            string vid = g[2].Value;
            string name = HttpUtility.HtmlDecode(g[4].Value.Trim());

            // Extract poster from data-original
            string picture = "";
            var img = row.Groups("data-original=\"([^\"]+)\"");
            if (!string.IsNullOrEmpty(img[1].Value))
                picture = img[1].Value;
            if (string.IsNullOrEmpty(picture))
                picture = row.Match("data-webp=\"([^\"]+)\"");
            if (!string.IsNullOrEmpty(picture) && picture.StartsWith("/"))
                picture = $"https://www.85po.com{picture}";

            // Extract quality (4K, 2K, HD) - Match() tra ve capture group 1
            // nen pattern phai co ngoac (ban cu khong ngoac -> luon rong).
            // PHAI check rieng tung muc theo thu tu 4k > 2k > hd vi class
            // tren web la "is-hd is-2k" (is-hd dung truoc) - gop chung 1
            // regex se khop is-hd truoc va moi card deu ra HD.
            string quality = row.Match("(is-4k)");
            if (string.IsNullOrEmpty(quality))
                quality = row.Match("(is-2k)");
            if (string.IsNullOrEmpty(quality))
                quality = row.Match("(is-hd)");
            if (!string.IsNullOrEmpty(quality))
                quality = quality.Substring(3).ToUpperInvariant();

            // Extract duration
            string time = row.Match("<div class=\"duration\">([^<]+)</div>", trim: true);

            var pl = new PlaylistItem()
            {
                video = $"{uri}?uri={HttpUtility.UrlEncode(href)}",
                name = name,
                picture = picture,
                quality = quality,
                time = Regex.Replace(time ?? "", "<[^>]+>", "").Trim(),
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
    // Sort that kieu KVS `?sort_by=` (do live 2026-10-08: ca 6 doi list that
    // tren home/genre/tag/search). Mac dinh (khong sort): list = Mới nhất,
    // search = Liên quan.
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất",            "post_date"),
        ("Xem nhiều nhất",      "video_viewed"),
        ("Hay nhất",            "rating"),
        ("Dài nhất",            "duration"),
        ("Bình luận nhiều nhất","most_commented"),
        ("Được yêu thích nhất", "most_favourited"),
    };

    static readonly string[] SortWhitelist =
        { "post_date", "video_viewed", "rating", "duration", "most_commented", "most_favourited" };

    static readonly string[] LegacySorts =
        { "4k", "top-rated", "most-popular", "latest-updates" };

    public static bool IsLegacySort(string sort)
        => !string.IsNullOrWhiteSpace(sort)
            && System.Array.IndexOf(LegacySorts, sort.Trim()) >= 0;

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return "";
        sort = sort.Trim();
        if (System.Array.IndexOf(SortWhitelist, sort) >= 0) return sort;
        foreach (var (_, s) in Sorts)
            if (s.Equals(sort, System.StringComparison.OrdinalIgnoreCase))
                return s;
        return "";
    }

    public static string SortLabel(string search, string sort)
    {
        string s = NormalizeSort(sort);
        if (string.IsNullOrEmpty(s))
        {
            if (IsLegacySort(sort)) return sort;
            return string.IsNullOrWhiteSpace(search) ? "Trang chủ" : "Liên quan";
        }
        foreach (var (name, v) in Sorts)
            if (v == s) return name;
        return s;
    }

    static string SortLink(string url, string search, string sort, string c, string t, string s)
    {
        var q = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(search))
            q.Add("search=" + HttpUtility.UrlEncode(search));
        if (!string.IsNullOrWhiteSpace(c))
            q.Add("c=" + HttpUtility.UrlEncode(c));
        if (!string.IsNullOrWhiteSpace(t))
            q.Add("t=" + HttpUtility.UrlEncode(t));
        string ns = NormalizeSort(s);
        if (!string.IsNullOrEmpty(ns))
            q.Add("sort=" + ns);
        return q.Count == 0 ? url : url + "?" + string.Join("&", q);
    }

    public static List<MenuItem> Menu(string host, string search, string sort, string c, string t)
    {
        string url = $"{host}/po85";

        #region search menu
        if (!string.IsNullOrEmpty(search))
        {
            string encodesearch = HttpUtility.UrlEncode(search);

            var ssub = new List<MenuItem>(Sorts.Length);
            foreach (var (name, s) in Sorts)
                ssub.Add(new MenuItem()
                {
                    title = name,
                    playlist_url = $"{url}?search={encodesearch}&sort={s}"
                });

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
                    title = $"Sắp xếp: {SortLabel(search, sort)}",
                    playlist_url = "submenu",
                    submenu = ssub
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
                title = $"Sắp xếp: {SortLabel(search, sort)}",
                playlist_url = "submenu",
                submenu = SortSubmenu(url, search, sort, c, t)
            },
            new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(6)
                {
                    // /categories/ chi co 6 vung (do live 2026-10-08, kem
                    // so phim). Site it doi -> hardcode, khoi fetch.
                    new("Hồng Kông (8)", $"{url}?c=xiang-gang"),
                    new("Malaysia (382)", $"{url}?c=ma-lai-xi-ya"),
                    new("Singapore (427)", $"{url}?c=xin-jia-po"),
                    new("Nhật Bản (67)", $"{url}?c=ri-ben"),
                    new("Đài Loan (287)", $"{url}?c=tai-wan"),
                    new("Trung Quốc (56)", $"{url}?c=zhong-guo"),
                }
            }
        };

        // Tags = 27 muc curated (tieng Viet). 2711 tags tren /tags/ toan
        // tieng Trung va KHONG co count → khong loc top duoc.
        menu.Add(new MenuItem()
        {
            title = "Tags",
            playlist_url = "submenu",
            submenu = tagmenu
        });

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }

    static List<MenuItem> SortSubmenu(
        string url, string search, string sort, string c, string t)
    {
        var sub = new List<MenuItem>(Sorts.Length + 1);
        // Mac dinh (khong sort) = noi dung goc context (Trang chủ/list,
        // relevance/search) — giu link sach de phan biet voi sort chon.
        var q0 = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(search))
            q0.Add("search=" + HttpUtility.UrlEncode(search));
        if (!string.IsNullOrWhiteSpace(c))
            q0.Add("c=" + HttpUtility.UrlEncode(c));
        if (!string.IsNullOrWhiteSpace(t))
            q0.Add("t=" + HttpUtility.UrlEncode(t));
        string defName = string.IsNullOrWhiteSpace(search) ? "Trang chủ" : "Liên quan";
        sub.Add(new(defName, q0.Count == 0 ? url : url + "?" + string.Join("&", q0)));
        foreach (var (name, s) in Sorts)
            sub.Add(new(name, SortLink(url, search, sort, c, t, s)));
        return sub;
    }

    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        uri = uri.Trim();

        // Bookmark/history cua client chi luu id so (vd 20818):
        // giu nguyen de ResolveLinksAsync mo trang embed doi ra URL day du.
        if (System.Text.RegularExpressions.Regex.IsMatch(uri, @"^[0-9]+$"))
            return uri;

        if (uri.StartsWith("/"))
            return $"https://www.85po.com{uri}";

        return uri;
    }

    /// <summary>
    /// Tạo URL từ video page sang embed page (nơi có flashvars).
    /// </summary>
    public static string EmbedPage(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return uri;

        try
        {
            // Nếu đã là embed page thì giữ nguyên
            if (uri.Contains("/embed/"))
                return uri;

            // Lấy video ID từ URL (pattern: /video/{id}/)
            var m = System.Text.RegularExpressions.Regex.Match(uri, @"/video/([0-9]+)/");
            if (m.Success)
                return $"https://www.85po.com/embed/{m.Groups[1].Value}/";

            // Fallback: thay /video/ bằng /embed/
            return System.Text.RegularExpressions.Regex.Replace(uri, @"/video/\d+/[^/]+/", "/embed/");
        }
        catch
        {
            return uri;
        }
    }

    /// <summary>
    /// Trang video locale (vd video_alt_url2) chua player day du - fetch tiep de lay file.
    /// Chi tra URL dang trang (http, khong /get_file/).
    /// </summary>
    public static List<string> AltPages(string html)
    {
        var pages = new List<string>(2);
        if (string.IsNullOrEmpty(html))
            return pages;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(html, @"video_alt_url[23]:\s*'([^']+)'"))
        {
            string u = m.Groups[1].Value.Replace("\\/", "/").Replace("&amp;", "&");
            if (u.StartsWith("http") && !u.Contains("/get_file/") && !pages.Contains(u))
                pages.Add(u);
            if (pages.Count >= 2)
                break;
        }
        return pages;
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
            var idm = System.Text.RegularExpressions.Regex.Match(pageUrl ?? "", @"(?:/v/|/video/|/embed/)([0-9]+)");
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

        string s = html.ToString();
        var stream_links = new Dictionary<string, string>(4);

        // 85po moi: flashvars nam tren /embed/{id}/ (trang /video/ chi co
        // template, khong con dropdown download). video_url=SD, alt=720p,
        // alt2=1080p, alt_url3=2160p file truc tiep (resolver giu lam fallback
        // cho dang boc function/0/ cu).
        string[] fvNames = new string[] { "video_url", "video_alt_url", "video_alt_url2", "video_alt_url3" };
        string[] fvDefault = new string[] { "480p", "720p", "1080p", "2160p" };
        for (int i = 0; i < fvNames.Length; i++)
        {
            var fvm = System.Text.RegularExpressions.Regex.Match(s, fvNames[i] + @":\s*'([^']+)'");
            if (!fvm.Success)
                continue;
            string furl = fvm.Groups[1].Value.Replace("\\/", "/").Replace("&amp;", "&");
            if (!furl.StartsWith("http") || !furl.Contains("/get_file/"))
                continue;
            string flabel = fvDefault[i];
            var tagm = System.Text.RegularExpressions.Regex.Match(furl, @"_(2160p|1080p|720p|480p|360p)\.");
            if (tagm.Success)
                flabel = tagm.Groups[1].Value;
            else if (furl.Contains("2160") || furl.ToLowerInvariant().Contains("4k"))
                flabel = "4K";
            if (stream_links.ContainsValue(furl))
                continue;
            string key = flabel;
            int dup = 2;
            while (stream_links.ContainsKey(key))
                key = flabel + " " + (dup++);
            stream_links.TryAdd(key, furl);
        }

        // Fallback: link download MP4 (dropdown, design cu): moi quality mot hash
        // ("MP4 480p, ...", "MP4 720p, ...", "MP4 1080p, ...")
        foreach (System.Text.RegularExpressions.Match dlm in
            System.Text.RegularExpressions.Regex.Matches(s,
            @"<a[^>]*href=""(https?://[^'""]+/get_file/[^'""]+download=true[^'""]*)""[^>]*>([^<]+)</a>"))
        {
            string dlurl = dlm.Groups[1].Value.Replace("&amp;", "&");
            string label = dlm.Groups[2].Value.Trim();
            if (string.IsNullOrEmpty(label))
                label = "download";
            if (!stream_links.ContainsValue(dlurl))
                stream_links.TryAdd(label, dlurl);
        }

        // Fallback: data-preview attribute (signed URL)
        if (stream_links.Count == 0)
        {
            var previewMatch = System.Text.RegularExpressions.Regex.Match(s, @"data-preview=""(https://www\.85po\.com/get_file/[^""]+)""");
            if (previewMatch.Success)
                stream_links.TryAdd("MP4", previewMatch.Groups[1].Value);
        }

        // Fallback: them &download=true vao video_url
        if (stream_links.Count == 0)
        {
            var videoUrl = System.Text.RegularExpressions.Regex.Match(s, @"video_url:\s*'([^']+)'");
            if (videoUrl.Success)
            {
                string url = videoUrl.Groups[1].Value.Replace("\\/", "/");
                if (url.StartsWith("http") && url.Contains("/get_file/"))
                {
                    url += (url.Contains("?") ? "&" : "?") + "download=true";
                    stream_links.TryAdd("MP4", url);
                }
            }
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
