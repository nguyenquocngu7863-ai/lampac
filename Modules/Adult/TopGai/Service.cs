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
            // chip popup trả URL tuyệt đối (badge của site) -> dùng nguyên
            string url = c.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? c.Trim()
                : host + "/" + c.Trim('/');
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

    // ===== POPUP: chip thể loại (badge) + phim liên quan (ajax JSON) =====
    // Detail KHONG co block the-loai/tag rieng; chi co "badge" the loai chung:
    //   <a href="https://topgai.net/sex/jav-hd" class="badge"> <svg …/> <span>JAV</span> </a>
    // Danh sach "Phim liên quan" render bang Alpine tu /ajax/suggestions/<slug>
    // (x-data="list('/ajax/suggestions/<slug>')") -> phai lay JSON, khong parse HTML.
    public static List<(string kind, string url, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html, "<a\\s+href=\"([^\"]+)\"\\s+class=\"badge\">([\\s\\S]{0,9000}?)</a>", RegexOptions.IgnoreCase))
        {
            string url = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
            // chỉ giữ /sex/<slug> (mục /trang/<slug> trên site đã chết -> 503)
            if (!url.Contains("/sex/"))
                continue;

            var n = Regex.Match(m.Groups[2].Value, "<span>([^<]{1,60})</span>");
            if (!n.Success)
                continue;

            string name = HttpUtility.HtmlDecode(n.Groups[1].Value).Trim();
            if (name.Length == 0 || !seen.Add(url))
                continue;

            list.Add(("cat", url, name));
        }

        return list;
    }

    public static string SuggestionsUrl(string pageUrl)
    {
        if (string.IsNullOrEmpty(pageUrl))
            return null;

        string path = pageUrl;
        if (pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!System.Uri.TryCreate(pageUrl, UriKind.Absolute, out var u))
                return null;
            path = u.AbsolutePath;
        }

        int i = path.IndexOf("/phim-sex/", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return null;

        string slug = path.Substring(i + 10).Trim('/');
        if (slug.Length == 0)
            return null;

        return SiteHost + "/ajax/suggestions/" + slug;
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> PlaylistFromJson(string route, string json)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(json))
            return playlists;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != System.Text.Json.JsonValueKind.Array)
                return playlists;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var it in data.EnumerateArray())
            {
                string url = Str(it, "url");
                string name = Str(it, "title");
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name) || !seen.Add(url))
                    continue;

                string poster = Str(it, "thumbnail_file_url");

                playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
                {
                    video = route + "?uri=" + HttpUtility.UrlEncode(url),
                    name = name,
                    picture = poster,
                    json = true,
                    bookmark = new Shared.Models.SISI.Base.Bookmark()
                    {
                        site = "topgai",
                        href = url,
                        image = poster
                    }
                });
            }
        }
        catch { }

        return playlists;
    }

    static string Str(System.Text.Json.JsonElement it, string key)
        => it.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? (v.GetString() ?? "").Trim()
            : "";

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
