using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace HeoVl;

public static class HeoVlTo
{
    public static string SiteHost = "https://heovl.im";

    static string Slugify(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = Slugify(search);
            if (string.IsNullOrEmpty(slug))
                slug = HttpUtility.UrlEncode(search);
            string url = host + "/search/" + slug;
            if (pg > 1)
                url += "?page=" + pg;
            return url;
        }

        if (!string.IsNullOrEmpty(c))
        {
            string url = host + "/" + c.Trim('/');
            if (pg > 1)
                url += "?page=" + pg;
            return url;
        }

        if (pg > 1)
            return host + "/categories/viet-nam?page=" + pg;

        return host + "/";
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html, @"<a\s+href=""(https://heovl\.im/videos/[^""]+)""\s+title=""([^""]+)""", RegexOptions.Singleline))
        {
            string href = m.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            int start = m.Index;
            int len = Math.Min(3000, html.Length - start);
            if (len > 0)
            {
                string chunk = html.Substring(start, len);
                var img = Regex.Match(chunk, @"<img\s+src=""(https://heovl\.im/resize/[^""]+|https://heovl\.im/[^""]+\.(?:jpg|jpeg|png|webp)[^""]*)""");
                if (!img.Success)
                    img = Regex.Match(chunk, @"<img\s+[^>]*src=""([^""]+)""");
                if (img.Success)
                    poster = img.Groups[1].Value;
            }

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "heovl",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    public static List<(string label, string embed)> GetEmbeds(string html)
    {
        var list = new List<(string label, string embed)>();
        foreach (Match m in Regex.Matches(html, @"data-cdn-name=""([^""]*)""[^>]*data-source=""([^""]+)""", RegexOptions.Singleline))
        {
            string label = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
            string embed = m.Groups[2].Value.Replace("&amp;", "&");
            if (string.IsNullOrEmpty(embed))
                continue;
            if (string.IsNullOrEmpty(label))
                label = "Server " + (list.Count + 1);
            list.Add((label, embed));
        }

        if (list.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, @"data-source=""(https://[^""]+/videos/[^""]+/play[^""]*)""", RegexOptions.Singleline))
            {
                string embed = m.Groups[1].Value.Replace("&amp;", "&");
                list.Add(("Server " + (list.Count + 1), embed));
            }
        }

        return list;
    }

    public static (string embedHost, string vid) ParseEmbed(string embed)
    {
        try
        {
            var u = new Uri(embed);
            var m = Regex.Match(u.AbsolutePath, @"/videos/([^/]+)/play");
            if (!m.Success)
                return (null, null);
            return (u.Scheme + "://" + u.Host, m.Groups[1].Value);
        }
        catch
        {
            return (null, null);
        }
    }

    public static Dictionary<string, string> StreamLinksFromConfig(string configJson)
    {
        var links = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(configJson))
            return links;

        foreach (Match m in Regex.Matches(configJson, @"""file""\s*:\s*""(https?[^""]+)"""))
        {
            string file = m.Groups[1].Value.Replace("\\/", "/");
            if (file.Contains(".m3u8") || file.Contains(".mp4"))
            {
                string label = file.Contains(".m3u8") ? "HLS" : "MP4";
                string key = label + (links.Count == 0 ? "" : " " + (links.Count + 1));
                if (!links.ContainsValue(file))
                    links.TryAdd(key, file);
            }
        }

        if (links.Count == 0)
        {
            var m = Regex.Match(configJson, @"(https?[^""\\\s]+\.m3u8[^""\\\s]*)");
            if (m.Success)
                links.TryAdd("HLS", m.Groups[1].Value.Replace("\\/", "/"));
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
                playlist_url = host + "/heovl"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/heovl"),
                    new("Việt Nam", host + "/heovl?c=categories/viet-nam"),
                    new("Vietsub", host + "/heovl?c=categories/vietsub"),
                    new("JAV HD", host + "/heovl?c=categories/jav-hd"),
                    new("Không Che", host + "/heovl?c=categories/khong-che"),
                    new("Trung Quốc", host + "/heovl?c=categories/trung-quoc"),
                    new("Âu - Mỹ", host + "/heovl?c=categories/au-my"),
                    new("Gái Xinh", host + "/heovl?c=categories/gai-xinh"),
                    new("Nghiệp Dư", host + "/heovl?c=categories/nghiep-du"),
                    new("Tự Quay", host + "/heovl?c=categories/tu-quay"),
                    new("Doggy", host + "/heovl?c=categories/doggy"),
                    new("Xnxx", host + "/heovl?c=categories/xnxx"),
                }
            }
        };
    }
}
