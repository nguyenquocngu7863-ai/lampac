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

namespace XNhau;

public static class XNhauTo
{
    #region Uri
    public static string Uri(string host, string search, string sort, string c, string t, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search) && search.Contains("/members/"))
        {
            // Feed nguoi dang: dan link member (https://xnhau.cab/members/16566/)
            // vao o tim kiem — lay thang trang member
            string murl = search.Trim();
            if (murl.StartsWith("/"))
                murl = $"https://xnhau.cab{murl}";
            url.Clear();
            url.Append(murl.TrimEnd('/'));
            url.Append("/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (!string.IsNullOrWhiteSpace(search) && search.StartsWith("member:"))
        {
            url.Append("members/");
            url.Append(search.Substring(7).Trim().Trim('/'));
            url.Append("/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("search/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append("the-loai/");
            url.Append(c);
            url.Append("/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (!string.IsNullOrEmpty(t))
        {
            url.Append("tags/");
            url.Append(t);
            url.Append("/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (sort == "clip-sex-moi")
        {
            url.Append("clip-sex-moi/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (sort == "clip-sex-hot")
        {
            url.Append("clip-sex-hot/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (sort == "clip-sex-hay")
        {
            url.Append("clip-sex-hay/");
            if (pg > 1)
            {
                url.Append("?from=");
                url.Append(pg);
            }
        }
        else if (pg > 1)
        {
            url.Append("clip-sex-moi/?from=");
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

        var rx = Rx.Split("<div\\s+class=\"item", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups("<a href=\"((?:https?://[^/]+)?/video/[0-9]+/[^\\\"]+)\"[^>]*title=\"([^\\\"]+)\"");

            if (string.IsNullOrWhiteSpace(g[1].Value) || string.IsNullOrWhiteSpace(g[2].Value))
                continue;

            string href = g[1].Value;
            if (href.StartsWith("/"))
                href = $"https://xnhau.cab{href}";

            var img = row.Groups("data-original=\"([^\"]+)\"");
            string picture = img[1].Value;
            if (string.IsNullOrEmpty(picture))
                picture = row.Match("data-webp=\"([^\"]+)\"");
            if (!string.IsNullOrEmpty(picture) && picture.StartsWith("/"))
                picture = $"https://xnhau.cab{picture}";

            string time = row.Match("<span class=\"duration\"[^>]*>(.*?)</span>", trim: true);
            if (string.IsNullOrEmpty(time))
                time = row.Match("duration\">([^<]+)<", trim: true);

            string quality = row.Match("<span class=\"hd[^\\\"]*\\\">([^<]+)</span>", trim: true);

            var idm = System.Text.RegularExpressions.Regex.Match(href, @"/video/([0-9]+)/");
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
                    site = "xnhau",
                    href = vid,
                    image = picture
                }
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        // Loại bỏ item trùng theo video URI
        return playlists.DistinctBy(p => p.video).ToList();
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string search, string sort, string c, string t)
    {
        string url = $"{host}/xnhau";

        #region search menu
        if (!string.IsNullOrEmpty(search))
        {
            string encodesearch = HttpUtility.UrlEncode(search);

            return new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm (dán link member để xem feed người đăng)",
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
                    }
                }
            };
        }
        #endregion

        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"XNhau_menu_{host}_{sort}_{c}_{t}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        var tagmenu = new List<MenuItem>(26)
        {
            new("Việt Nam", $"{url}?c=vietnam"),
            new("Gái xinh", $"{url}?c=gai-xinh"),
            new("Gái teen", $"{url}?c=gai-teen"),
            new("Học sinh", $"{url}?c=hoc-sinh"),
            new("Sinh viên", $"{url}?c=sinh-vien"),
            new("Máy bay MBG", $"{url}?c=may-bay-mbbg"),
            new("Vợ chồng", $"{url}?c=vo-chong"),
            new("Ngoại tình vụng trộm", $"{url}?c=ngoai-tinh-vung-trom"),
            new("Gái gọi", $"{url}?c=gai-goi"),
            new("Rau FWB", $"{url}?c=rau-fwb"),
            new("Loạn luân", $"{url}?c=loan-luan"),
            new("Doggy", $"{url}?c=doggy"),
            new("Blowjob bú cu", $"{url}?c=blowjob-bu-cu"),
            new("Bú lồn vét máng", $"{url}?c=bu-lon-vet-mang"),
            new("Vú bự", $"{url}?c=vu-bu"),
            new("Thủ dâm", $"{url}?c=thu-dam"),
            new("Tự quay", $"{url}?c=tu-quay"),
            new("Quay lén", $"{url}?c=quay-len"),
            new("Xuất trong", $"{url}?c=xuat-trong"),
            new("Nhân trần", $"{url}?c=nhan-tran"),
            new("Trung Quốc", $"{url}?c=trung-quoc"),
            new("Âu Mỹ", $"{url}?c=sex-au-my"),
            new("JAV Nhật Bản", $"{url}?c=phim-sex-nhat-ban-jav"),
            new("Hàn Quốc", $"{url}?c=phim-sex-han-quoc"),
            new("Anime", $"{url}?c=anime"),
            new("Phim sex", $"{url}?c=phim-sex"),
        };

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
                title = $"Sắp xếp: {(string.IsNullOrEmpty(sort) ? "Trang chủ" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(4)
                {
                    new("Trang chủ", $"{url}?c={c}&t={t}"),
                    new("Mới nhất", $"{url}?c={c}&t={t}&sort=clip-sex-moi"),
                    new("Hot", $"{url}?c={c}&t={t}&sort=clip-sex-hot"),
                    new("Hay", $"{url}?c={c}&t={t}&sort=clip-sex-hay")
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

        uri = uri.Trim();

        if (System.Text.RegularExpressions.Regex.IsMatch(uri, @"^[0-9]+$"))
            return uri;

        if (uri.StartsWith("/"))
            return $"https://xnhau.cab{uri}";

        return uri;
    }

    public static Dictionary<string, string> StreamLinks(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        string s = html.ToString();
        var stream_links = new Dictionary<string, string>(3);

        var m480 = System.Text.RegularExpressions.Regex.Match(s, @"video_url:\s*'([^']+)'");
        var t480 = System.Text.RegularExpressions.Regex.Match(s, @"video_url_text:\s*'([^']+)'");
        if (m480.Success && m480.Groups[1].Value.StartsWith("http"))
            stream_links.TryAdd(t480.Success ? t480.Groups[1].Value : "480p", m480.Groups[1].Value);

        var m720 = System.Text.RegularExpressions.Regex.Match(s, @"video_alt_url:\s*'(https?://[^']+)'");
        var t720 = System.Text.RegularExpressions.Regex.Match(s, @"video_alt_url_text:\s*'([^']+)'");
        if (m720.Success)
            stream_links.TryAdd(t720.Success ? t720.Groups[1].Value : "720p", m720.Groups[1].Value);

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
