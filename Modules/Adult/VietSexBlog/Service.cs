using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace VietSexBlog;

public static class VietSexBlogTo
{
    public static string SiteHost = "https://x.vietsex.blog";

    public static string Uri(string host, string search, string sort, string c, string t, int pg)
    {
        var url = new System.Text.StringBuilder();
        url.Append(SiteHost);

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("/?search=");
            url.Append(HttpUtility.UrlEncode(search));
            if (pg > 1)
            {
                url.Append("&page=");
                url.Append(pg);
            }
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append("/");
            url.Append(c);
            if (pg > 1)
            {
                url.Append("?page=");
                url.Append(pg);
            }
        }
        else
        {
            url.Append("/danh-sach/phim-moi");
            if (pg > 1)
            {
                url.Append("?page=");
                url.Append(pg);
            }
        }

        return url.ToString();
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html, System.Func<Shared.Models.SISI.Base.PlaylistItem, Shared.Models.SISI.Base.PlaylistItem> onplaylist = null)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();

        var blocks = Regex.Matches(html, "<a\\s+class=\"m-block movie-item\".*?</a>", RegexOptions.Singleline);
        foreach (Match block in blocks)
        {
            string b = block.Value;

            var hm = Regex.Match(b, "href=\"(https://x\\.vietsex\\.blog/phim/[a-z0-9-]+)\"");
            var tm = Regex.Match(b, "title=\"([^\"]+)\"");
            if (!hm.Success || !tm.Success) continue;

            string href = hm.Groups[1].Value;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value);

            string poster = "";
            var imgMatch = Regex.Match(b, "data-original=\"([^\"]+)\"");
            if (imgMatch.Success)
            {
                poster = imgMatch.Groups[1].Value;
                if (poster.StartsWith("/"))
                    poster = SiteHost + poster;
            }

            string label = "";
            var labelMatch = Regex.Match(b, "<div\\s+class=\"label\">([^<]+)</div>");
            if (labelMatch.Success)
                label = labelMatch.Groups[1].Value.Trim();

            var pl = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "vietsexblog",
                    href = href,
                    image = poster
                }
            };

            if (!string.IsNullOrEmpty(label))
                pl.quality = label;

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }

    public static Dictionary<string, string> StreamLinks(string html)
    {
        var links = new Dictionary<string, string>();

        var servers = Regex.Matches(html, "data-link=\"([^\"]+)\"\\s+data-type=\"([a-zA-Z0-9]+)\"");
        int n = 0;
        foreach (Match m in servers)
        {
            n++;
            string link = m.Groups[1].Value;
            string type = m.Groups[2].Value;
            if (link.StartsWith("/"))
                link = SiteHost + link;
            if (!link.StartsWith("http"))
                continue;
            string label = servers.Count > 1 ? ("Server " + n + " " + type) : type;
            if (!links.ContainsKey(label))
                links.TryAdd(label, link);
        }

        return links;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/vietsexblog"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Phim mới",
                playlist_url = host + "/vietsexblog?c=danh-sach/phim-moi"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Việt Nam", host + "/vietsexblog?c=the-loai/phim-sex-viet-nam"),
                    new("JAV", host + "/vietsexblog?c=the-loai/jav"),
                    new("JAV Vietsub", host + "/vietsexblog?c=the-loai/jav-vietsub"),
                    new("Không che", host + "/vietsexblog?c=the-loai/khong-che"),
                    new("Trung Quốc", host + "/vietsexblog?c=the-loai/phim-sex-trung-quoc"),
                    new("Âu Mỹ", host + "/vietsexblog?c=the-loai/phim-sex-au-my"),
                    new("Hentai", host + "/vietsexblog?c=the-loai/hentai"),
                    new("Gái xinh", host + "/vietsexblog?c=the-loai/gai-xinh"),
                }
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Quốc gia",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Việt Nam", host + "/vietsexblog?c=quoc-gia/viet-nam"),
                    new("Nhật Bản", host + "/vietsexblog?c=quoc-gia/nhat-ban"),
                    new("Trung Quốc", host + "/vietsexblog?c=quoc-gia/trung-quoc"),
                    new("Hàn Quốc", host + "/vietsexblog?c=quoc-gia/han-quoc"),
                    new("Âu Mỹ", host + "/vietsexblog?c=quoc-gia/au-my"),
                }
            }
        };
    }
}
