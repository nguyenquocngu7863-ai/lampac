using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace JavTsunami;

public static class JavTsunamiTo
{
    public static string SiteHost = "https://javtsunami.com";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = HttpUtility.UrlEncode(search.Trim());
            if (pg > 1)
                return host + "/page/" + pg + "/?s=" + q;
            return host + "/?s=" + q;
        }

        if (!string.IsNullOrEmpty(c))
        {
            c = c.Trim('/');
            string url;
            if (c.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = c;
            }
            else if (c.StartsWith("?") || c.StartsWith("filter="))
            {
                string q = c.TrimStart('?');
                if (pg > 1)
                    return host + "/page/" + pg + "?" + q;
                return host + "/?" + q;
            }
            else
            {
                url = host + "/" + c;
            }
            if (pg > 1)
                return url.TrimEnd('/') + "/page/" + pg;
            return url;
        }

        if (pg > 1)
            return host + "/page/" + pg + "?filter=latest";
        return host + "/?filter=latest";
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();
        foreach (Match art in Regex.Matches(html, @"<article[^>]+thumb-block[^>]*>(.*?)</article>", RegexOptions.Singleline))
        {
            string b = art.Groups[1].Value;
            if (b.Length > 6000)
                b = b.Substring(0, 6000);

            var hm = Regex.Match(b, @"<a\s+href=""(https?://javtsunami\.com/[^""]+\.html)""\s+title=""([^""]{2,150})""");
            if (!hm.Success)
                continue;
            string href = hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            string name = HttpUtility.HtmlDecode(hm.Groups[2].Value.Trim().Replace("_", " "));
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(art.Value.Length > 7000 ? art.Value.Substring(0, 7000) : art.Value, @"data-main-thumb=""([^""]+)""");
            if (!im.Success)
                im = Regex.Match(b, @"data-lazy-src=""(https?://[^""]+)""");
            if (!im.Success)
            {
                var sm = Regex.Match(b, @"<img[^>]+src=""(https?://[^""]+)""");
                if (sm.Success && !sm.Groups[1].Value.StartsWith("data:"))
                    im = sm;
            }
            if (im.Success)
                poster = im.Groups[1].Value;

            var dm = Regex.Match(b, @"<span class=""duration"">.*?([\d:]{4,9})</span>", RegexOptions.Singleline);
            if (dm.Success)
                name = name + " [" + dm.Groups[1].Value + "]";

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javtsunami",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // trang post -> iframe embed theo thu tu uu tien
    public static List<string> EmbedUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, @"<iframe[^>]+src=""(https?://[^""]+)"""))
        {
            string u = m.Groups[1].Value;
            if (!seen.Add(u))
                continue;
            // turbo truoc, dood/vide0 sau, con lai cuoi
            if (u.Contains("turbovidhls.com") || u.Contains("turboviplay.com"))
                urls.Insert(0, u);
            else
                urls.Add(u);
        }

        return urls;
    }

    // embed turbovidhls -> data-hash m3u8 truc tiep
    public static string TurboM3u8(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var m = Regex.Match(html, @"data-hash=""(https?://[^""]+\.m3u8[^""]*)""");
        return m.Success ? m.Groups[1].Value : "";
    }

    public static string VideoTitle(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var m = Regex.Match(html, @"<meta itemprop=""name"" content=""([^""]+)""");
        return m.Success ? HttpUtility.HtmlDecode(m.Groups[1].Value.Trim()) : "";
    }

    public static string VideoPoster(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var m = Regex.Match(html, @"<meta itemprop=""thumbnailUrl"" content=""([^""]+)""");
        return m.Success ? m.Groups[1].Value : "";
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        var cats = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Có che", host + "/javtsunami?c=category/jav-censored"),
            new("Không che", host + "/javtsunami?c=category/jav-uncensored"),
            new("JAV Sub", host + "/javtsunami?c=category/jav-sub"),
            new("Sub Eng", host + "/javtsunami?c=tag/jav-eng-sub"),
            new("Sub Indo", host + "/javtsunami?c=tag/jav-sub-indo"),
            new("Mới ra mắt", host + "/javtsunami?c=category/new-release"),
            new("Đang hot", host + "/javtsunami?c=category/hot-jav"),
            new("Trending", host + "/javtsunami?c=category/trending"),
            new("Sắp ra mắt", host + "/javtsunami?c=category/upcoming"),
            new("FC2", host + "/javtsunami?c=category/fc2"),
            new("Nghiệp dư", host + "/javtsunami?c=category/amateur"),
            new("Trung Quốc", host + "/javtsunami?c=category/chinese"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/javtsunami"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javtsunami"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Xem nhiều",
                playlist_url = host + "/javtsunami?c=filter=most-viewed"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Video dài",
                playlist_url = host + "/javtsunami?c=filter=longest"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Ngẫu nhiên",
                playlist_url = host + "/javtsunami?c=filter=random"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = cats
            }
        };
    }
}
