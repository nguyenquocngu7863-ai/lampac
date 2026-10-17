using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace TangThon;

public static class TangThonTo
{
    public static string SiteHost = "https://tangthon.com";

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string url = host + "/phim-sex-tim-kiem?q=" + HttpUtility.UrlEncode(search.Trim());
            if (pg > 1)
                url += "&page=" + pg;
            return url;
        }

        if (!string.IsNullOrEmpty(c))
        {
            string url = host + "/" + c.Trim('/');
            if (pg > 1)
                url += (url.Contains("?") ? "&" : "?") + "page=" + pg;
            return url;
        }

        if (pg > 1)
            return host + "/phim-sex-moi?page=" + pg;

        return host + "/phim-sex-moi";
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        var seen = new HashSet<string>();

        foreach (Match card in Regex.Matches(html, @"<article\s+class=""video-card"">(.*?)</article>", RegexOptions.Singleline))
        {
            string block = card.Groups[1].Value;

            var a = Regex.Match(block, @"<a\s+class=""video-thumb""\s+href=""([^""]+)""");
            if (!a.Success)
                continue;

            string href = a.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(href) || !seen.Add(href))
                continue;

            string poster = "";
            string name = "";
            var img = Regex.Match(block, @"<img\s+src=""([^""]+)""\s+alt=""([^""]*)""");
            if (img.Success)
            {
                poster = img.Groups[1].Value.Trim();
                name = img.Groups[2].Value.Trim();
            }

            if (string.IsNullOrEmpty(name))
            {
                var t = Regex.Match(block, @"<h3\s+class=""video-title"">.*?<a[^>]+title=""([^""]+)""", RegexOptions.Singleline);
                if (t.Success)
                    name = t.Groups[1].Value.Trim();
            }

            // Site double-escapes quotes in attributes (&amp;quot;), decode twice
            name = HttpUtility.HtmlDecode(HttpUtility.HtmlDecode(name));
            if (string.IsNullOrEmpty(name))
                continue;

            string quality = "";
            var q = Regex.Match(block, @"<span\s+class=""video-quality"">([^<]*)</span>");
            if (q.Success)
                quality = q.Groups[1].Value.Trim();

            string time = "";
            var tm = Regex.Match(block, @"<time\s+class=""video-badge""[^>]*>([^<]*)</time>");
            if (tm.Success)
                time = tm.Groups[1].Value.Trim();

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                quality = quality,
                time = time,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "tangthon",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    /// <summary>
    /// Lấy URL iframe embed từ trang detail. Host embed đổi theo domain của site
    /// (tung la embed.tangthon.com, nay la embed.billygreyguitar.com) nen KHONG duoc
    /// hardcode host — bam theo chinh src cua iframe.
    /// </summary>
    public static string GetEmbedUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"(?:https?:)?//[A-Za-z0-9.\-]+/embed/[A-Za-z0-9]+/p\d+", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string url = HttpUtility.HtmlDecode(m.Value).Trim();
        if (url.StartsWith("//"))
            url = "https:" + url;

        return url;
    }

    /// <summary>
    /// m3u8_file_url trong embed la duong dan relative -> noi vao host cua chinh embed.
    /// </summary>
    public static string GetStreamPath(string embedHtml, string embedUrl)
    {
        if (string.IsNullOrEmpty(embedHtml))
            return null;

        var m = Regex.Match(embedHtml, @"m3u8_file_url\s*=\s*['""]([^'""]+)['""]");
        if (!m.Success)
            return null;

        string path = HttpUtility.HtmlDecode(m.Groups[1].Value).Replace("\\/", "/").Trim();
        if (string.IsNullOrEmpty(path))
            return null;

        if (path.StartsWith("http"))
            return path;

        string baseHost = SiteHost;
        if (!string.IsNullOrEmpty(embedUrl) && System.Uri.TryCreate(embedUrl, UriKind.Absolute, out System.Uri u))
            baseHost = u.Scheme + "://" + u.Host;

        if (path.StartsWith("//"))
            return "https:" + path;

        return baseHost + (path.StartsWith("/") ? path : "/" + path);
    }

    /// <summary>Referer = host cua URL (player CDN doi hoi phai co Referer, thieu la 403).</summary>
    public static string RefererOf(string url)
    {
        if (!string.IsNullOrEmpty(url) && System.Uri.TryCreate(url, UriKind.Absolute, out System.Uri u))
            return u.Scheme + "://" + u.Host + "/";

        return SiteHost + "/";
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/tangthon"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/tangthon"),
                    new("Phim hot", host + "/tangthon?c=phim-sex-hot"),
                    new("Nổi bật", host + "/tangthon?c=phim-sex-noi-bat"),
                    new("Tất cả phim", host + "/tangthon?c=phim-sex"),
                    new("Phim Sex Việt Nam", host + "/tangthon?c=phim-sex-danh-muc/phim-sex-viet-nam"),
                    new("Vietsub", host + "/tangthon?c=phim-sex-danh-muc/phim-sex-vietsub"),
                    new("Jav HD", host + "/tangthon?c=phim-sex-danh-muc/javhd"),
                    new("Trung Quốc", host + "/tangthon?c=phim-sex-danh-muc/phim-sex-trung-quoc"),
                    new("Châu Âu", host + "/tangthon?c=phim-sex-danh-muc/sex-chau-au"),
                    new("Hentai", host + "/tangthon?c=phim-sex-danh-muc/hentai-sex"),
                }
            }
        };
    }
}
