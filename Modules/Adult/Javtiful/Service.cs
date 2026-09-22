using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace Javtiful;

public static class JavtifulTo
{
    public static string SiteHost = "https://javtiful.com";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

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
        s = Regex.Replace(s, @"[^a-z0-9\s-]", " ");
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        string page = pg > 1 ? "?page=" + pg : "";

        if (!string.IsNullOrWhiteSpace(search))
        {
            // search HOA tung tra 502 -> slugify lowercase luon
            string slug = SlugifyVi(search);
            if (string.IsNullOrEmpty(slug))
                slug = HttpUtility.UrlEncode(search);
            return host + "/vn/search?q=" + slug;
        }

        if (!string.IsNullOrEmpty(c))
        {
            string url = c.StartsWith("http") ? c : host + "/vn/" + c.Trim('/');
            return url + (pg > 1 ? (url.Contains("?") ? "&" : "?") + "page=" + pg : "");
        }

        // home /vn khong phan trang (?page= bi bo qua) -> dung feed /vn/foryou
        return host + "/vn/foryou" + page;
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();
        foreach (string raw in html.Split(new[] { "<article class=\"front-video-card\">" }, StringSplitOptions.None))
        {
            string b = raw.Length > 5000 ? raw.Substring(0, 5000) : raw;

            var hm = Regex.Match(b, @"<a\s+href=""(/vn/video/[^""]+)""\s+class=""front-video-thumb""");
            if (!hm.Success)
                continue;
            string href = SiteHost + hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            var tm = Regex.Match(b, @"class=""front-video-title"">([^<]{4,200})<");
            if (!tm.Success)
                continue;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, @"data-front-lazy-src=""([^""]+)""");
            if (im.Success)
            {
                poster = im.Groups[1].Value;
                if (poster.StartsWith("/"))
                    poster = SiteHost + poster;
            }

            string preview = "";
            var pv = Regex.Match(b, @"data-front-video-preview-src=""([^""]+)""");
            if (pv.Success)
                preview = pv.Groups[1].Value;

            var item = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javtiful",
                    href = href,
                    image = poster
                }
            };
            if (!string.IsNullOrEmpty(preview))
                item.preview = preview;

            playlists.Add(item);
        }

        return playlists;
    }

    // video page -> <source src="...mp4"> direct (fast-stream.jav.si)
    public static List<string> StreamUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, @"<source[^>]*src=""(https?://[^""]+)"""))
        {
            string u = m.Groups[1].Value;
            if (seen.Add(u))
                urls.Add(u);
        }

        return urls;
    }

    public static string VideoPoster(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var m = Regex.Match(html, @"<video[^>]*poster=""([^""]+)""");
        if (!m.Success)
            return "";
        string p = m.Groups[1].Value;
        if (p.StartsWith("/"))
            p = SiteHost + p;
        return p;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        var genres = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Ngoại tình", host + "/javtiful?c=category/affair"),
            new("Nghiệp dư", host + "/javtiful?c=category/amateur"),
            new("Gái xinh", host + "/javtiful?c=category/beautiful-girl"),
            new("Ngực to", host + "/javtiful?c=category/big-tits"),
            new("BBW", host + "/javtiful?c=category/bbw"),
            new("AV Trung Quốc", host + "/javtiful?c=category/chinese-av"),
            new("Cosplay", host + "/javtiful?c=category/cosplay"),
            new("Kịch", host + "/javtiful?c=category/drama"),
            new("Nữ sếp", host + "/javtiful?c=category/female-boss"),
            new("Nữ điều tra", host + "/javtiful?c=category/female-investigator"),
            new("Nữ sinh", host + "/javtiful?c=category/female-student"),
            new("Cô giáo", host + "/javtiful?c=category/female-teacher"),
            new("Học sinh", host + "/javtiful?c=category/school-girls"),
            new("Chị dâu", host + "/javtiful?c=category/sister-in-law"),
            new("Giúp việc", host + "/javtiful?c=category/housekeeper"),
            new("Y tá", host + "/javtiful?c=category/nurse"),
            new("Dân văn phòng", host + "/javtiful?c=category/office-lady"),
            new("Thôi miên", host + "/javtiful?c=category/hypnosis"),
            new("Vợ người ta", host + "/javtiful?c=category/married-woman"),
            new("Quý bà", host + "/javtiful?c=category/mature-woman"),
            new("MILF", host + "/javtiful?c=category/milf"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/javtiful"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javtiful"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Có che",
                playlist_url = host + "/javtiful?c=censored"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Không che",
                playlist_url = host + "/javtiful?c=uncensored"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Giảm mosaic",
                playlist_url = host + "/javtiful?c=reducing-mosaic"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres
            }
        };
    }
}
