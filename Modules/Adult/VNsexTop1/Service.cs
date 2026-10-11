using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace VNsexTop1;

public static class VNsexTop1To
{
    public static string SiteHost = "https://vnsextop1.com";

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

    // vnsextop1.com: search = /?s=kw (&page=N), category = /the-loai/slug (?page=N),
    // home = / (?page=N). Da verify overlap 0 ca 3 tuyen.
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string url = host + "/?s=" + System.Uri.EscapeDataString(search.Trim());
            if (pg > 1)
                url += "&page=" + pg;
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

    // vnsextop1: <div class="item"><a href="/phim-sex/slug" title=".." class="item__thumbnail"><img src="/resize/800/..">
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html, @"<a\s+href=""([^""]+/phim-sex/[^""]+)""[^>]*class=""item__thumbnail""[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string href = m.Groups[1].Value;
            if (href.StartsWith("/"))
                href = SiteHost + href;
            if (!href.StartsWith("http") || !seen.Add(href))
                continue;

            string name = "";
            var tm = Regex.Match(m.Value, @"title=""([^""]+)""", RegexOptions.IgnoreCase);
            if (tm.Success)
                name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
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
                    site = "vnsextop1",
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

    // vnsextop1.com KHONG co sort (HTML 0 hit sort/sap-xep/orderby) -> theo quy tac
    // 9g#4: bo han dong 2, chi head (Tim kiem) + base (The loai). ClampSort giu de
    // Index nhan sort van an toan (tra null), khong bia sort.
    public static readonly string[] SortWhitelist = { };
    public static string NormalizeSort(string sort) => null;
    public static string SortLabel(string sort) => "mới nhất";
    public static (string name, string sort)[] SortsFor(string search, string c) => null;
    public static string ClampSort(string sort, string search, string c) => null;

    // ===== head: Tim kiem (dung moi request, re) =====
    public static List<Shared.Models.SISI.Base.MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/vnsextop1";
        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };
    }

    // ===== base: 18 the-loai boc that tu trang chu (khong bia slug).
    // Chi 18 muc -> khong can cache/fallback nhu SupJav (1100 muc).
    public static List<Shared.Models.SISI.Base.MenuItem> MenuBase(string host)
    {
        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/vnsextop1"),
                    new("Phim Sex Hay", host + "/vnsextop1?c=the-loai/phim-sex-hay"),
                    new("Phim Sex HD", host + "/vnsextop1?c=the-loai/phim-sex-hd"),
                    new("Việt Sub", host + "/vnsextop1?c=the-loai/phim-sex-vietsub"),
                    new("Không Che", host + "/vnsextop1?c=the-loai/phim-sex-khong-che"),
                    new("Loạn Luân", host + "/vnsextop1?c=the-loai/phim-sex-loan-luan"),
                    new("Hiếp Dâm", host + "/vnsextop1?c=the-loai/phim-sex-hiep-dam"),
                    new("Học Sinh", host + "/vnsextop1?c=the-loai/phim-sex-hoc-sinh"),
                    new("Gái Xinh", host + "/vnsextop1?c=the-loai/phim-sex-gai-xinh"),
                    new("Vú To", host + "/vnsextop1?c=the-loai/vu-to"),
                    new("Người Mẫu", host + "/vnsextop1?c=the-loai/nguoi-mau-dang-chuan"),
                    new("Bạn Tình Dậy Lớn", host + "/vnsextop1?c=the-loai/ban-tinh-day-lon"),
                    new("Nhật Bản", host + "/vnsextop1?c=the-loai/phim-sex-nhat-ban"),
                    new("Trung Quốc", host + "/vnsextop1?c=the-loai/phim-sex-trung-quoc"),
                    new("Châu Âu", host + "/vnsextop1?c=the-loai/phim-sex-chau-au"),
                    new("JAV", host + "/vnsextop1?c=the-loai/jav"),
                    new("Vlxx", host + "/vnsextop1?c=the-loai/vlxx"),
                    new("Xvideos", host + "/vnsextop1?c=the-loai/xvideos"),
                    new("Xnxx", host + "/vnsextop1?c=the-loai/xnxx"),
                }
            }
        };
    }
}
