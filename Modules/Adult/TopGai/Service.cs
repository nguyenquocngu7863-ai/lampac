using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace TopGai;

public static class TopGaiTo
{
    public static string SiteHost = "https://topgai.net";

    static string SlugifyVi(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim().ToLowerInvariant();
        string[][] map = new string[][]
        {
            new[] { "a", "àáạảãâầấậẩẫăằắặẳẵ" },
            new[] { "e", "èéẹẻẽêềếệểễ" },
            new[] { "i", "ìíịỉĩ" },
            new[] { "o", "òóọỏõôồốộổỗơờớợởỡ" },
            new[] { "u", "ùúụủũưừứựửữ" },
            new[] { "y", "ỳýỵỷỹ" },
            new[] { "d", "đ" },
        };
        foreach (var pair in map)
        {
            foreach (char ch in pair[1])
                s = s.Replace(ch.ToString(), pair[0]);
        }
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
            string slug = SlugifyVi(search);
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
            return host + "/?page=" + pg;

        return host + "/";
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html, @"<a\s+href=""([^""]+)""\s+class=""video-item__thumb""\s+title=""([^""]+)""", RegexOptions.Singleline))
        {
            string href = m.Groups[1].Value;
            if (href.StartsWith("/"))
                href = SiteHost + href;
            if (!href.StartsWith("http") || !seen.Add(href))
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
                var img = Regex.Match(chunk, @"<img\s+src=""([^""]+)""");
                if (img.Success)
                {
                    poster = img.Groups[1].Value;
                    if (poster.StartsWith("/"))
                        poster = SiteHost + poster;
                }
            }

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "topgai",
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
            if (string.IsNullOrEmpty(embed) || !embed.StartsWith("http"))
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

    static readonly string[] AdMarks = new[]
    {
        "vast", "adtag", "imasdk", "/ima", "doubleclick",
        "googlesyndication", "pubads", "adservice", "imasdk",
        "exoclick", "adcash", "popads", "trafficjunky",
        "track", "pixel", "banner", "/ads", "ads.",
        "playhubconnect", "/ssp/"
    };

    public static bool IsAdUrl(string u)
    {
        if (string.IsNullOrEmpty(u))
            return true;
        string l = u.ToLowerInvariant();
        foreach (var m in AdMarks)
        {
            if (l.Contains(m))
                return true;
        }
        return false;
    }

    public static string UnescapeUrl(string u)
    {
        if (string.IsNullOrEmpty(u))
            return u;
        return u.Replace("\\/", "/").Replace("\\u0026", "&").Replace("\\u003d", "=");
    }

    public static Dictionary<string, string> StreamLinksFromConfig(string configJson)
    {
        var links = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(configJson))
            return links;

        foreach (Match m in Regex.Matches(configJson, @"""file""\s*:\s*""(https?[^""]+)"""))
        {
            string file = UnescapeUrl(m.Groups[1].Value);
            if (IsAdUrl(file))
                continue;
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
            {
                string f = UnescapeUrl(m.Groups[1].Value);
                if (!IsAdUrl(f))
                    links.TryAdd("HLS", f);
            }
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
                playlist_url = host + "/topgai"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/topgai"),
                    new("Vietsub", host + "/topgai?c=sex/phim-sex-vietsub"),
                    new("Không Che", host + "/topgai?c=sex/khong-che"),
                    new("Hiếp Dâm", host + "/topgai?c=sex/phim-sex-hiep-dam"),
                    new("Học Sinh", host + "/topgai?c=sex/phim-sex-hoc-sinh"),
                    new("Vụng Trộm", host + "/topgai?c=sex/phim-sex-vung-trom"),
                    new("Cấp 3", host + "/topgai?c=sex/phim-cap-3"),
                    new("Châu Âu", host + "/topgai?c=sex/phim-sex-chau-au"),
                    new("Xvideos", host + "/topgai?c=sex/xvideos"),
                    new("Xnxx", host + "/topgai?c=sex/xnxx"),
                    new("JAV HD", host + "/topgai?c=sex/jav-hd"),
                }
            }
        };
    }
}
