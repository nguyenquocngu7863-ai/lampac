using Shared;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Sexgenz2;

public static class Sexgenz2To
{
    private static List<(string slug, string title)> _cats;
    private static DateTime _catsTime;

    public static string Uri(string host, string search, string sort, string c, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        bool hasQuery = false;

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append(host.TrimEnd('/'));
            url.Append("/?search=");
            url.Append(HttpUtility.UrlEncode(search));
            hasQuery = true;
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append(host.TrimEnd('/'));
            url.Append("/the-loai/");
            url.Append(c.TrimStart('/').TrimEnd('/'));
            url.Append("/");
        }
        else
        {
            url.Append(host.TrimEnd('/'));
            url.Append("/danh-sach/phim-moi");
        }

        if (pg > 1)
        {
            url.Append(hasQuery ? "&page=" : "?page=");
            url.Append(pg);
        }

        return url.ToString();
    }

    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty) return null;

        var rx = Rx.Split("<div class=\"flw-item\"", html);
        if (rx.Count == 0) return new List<PlaylistItem>();

        var playlists = new List<PlaylistItem>(rx.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rx.Rows())
        {
            if (row.Contains("/phim/") == false)
                continue;

            string link = row.Match("<a href=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(link) || !link.Contains("/phim/"))
                continue;

            if (link.StartsWith("/"))
                link = "https://x.sexgenz.blog" + link;

            if (!seen.Add(link))
                continue;

            string title = row.Match("title=\"([^\"]+)\"") ?? row.Match("<h3[^>]*>([^<]+)</h3>") ?? row.Match("alt=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(title))
                continue;

            string img = row.Match("data-original=\"([^\"]+)\"") ?? row.Match("data-src=\"([^\"]+)\"") ?? row.Match("src=\"([^\"]+\\.(?:jpg|jpeg|png|webp)[^\"]*)\"");
            if (!string.IsNullOrWhiteSpace(img) && img.StartsWith("/"))
                img = "https://x.sexgenz.blog" + img;

            var pl = new PlaylistItem()
            {
                name = WebUtility.HtmlDecode(title).Trim(),
                video = $"{uri}?uri={WebUtility.UrlEncode(link)}",
                picture = img,
                json = true,
                related = true,
                bookmark = new Bookmark()
                {
                    site = "sgz2",
                    href = link,
                    image = img
                }
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }

    public static List<(string slug, string title)> GetCategories()
    {
        var now = DateTime.UtcNow;
        if (_cats != null && (now - _catsTime).TotalMinutes < 720)
            return _cats;

        try
        {
            var html = Http.Get("https://x.sexgenz.blog/", timeoutSeconds: 20).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(html))
            {
                var cats = new List<(string slug, string title)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var matches = Regex.Matches(html, "<a[^>]*href=\"(?:/the-loai/|https://x\\.sexgenz\\.blog/the-loai/)([^\"]+)\"[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match m in matches)
                {
                    string slug = WebUtility.HtmlDecode(m.Groups[1].Value.Trim().TrimEnd('/'));
                    string title = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<[^>]+>", "")).Trim();
                    if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(title))
                        continue;
                    if (!seen.Add(slug))
                        continue;
                    cats.Add((slug, title));
                }

                if (cats.Count > 0)
                {
                    _cats = cats;
                    _catsTime = now;
                    return cats;
                }
            }
        }
        catch { }

        return GetFallbackCategories();
    }

    private static List<(string slug, string title)> GetFallbackCategories()
    {
        return new List<(string, string)>
        {
            ("jav-hd", "JAV HD"),
            ("loan-luan", "Loạn Luân"),
            ("tap-the", "Tập Thể"),
            ("hiep-dam", "Hiếp Dâm"),
            ("sexhd", "SexHD"),
            ("xvideos", "Xvideos"),
            ("khong-che", "Không Che"),
            ("vung-trom", "Vùng Trộm"),
            ("vietsub", "Vietsub"),
            ("pornhub", "Pornhub"),
            ("chau-au", "Châu Âu"),
            ("hoc-sinh", "Học Sinh"),
            ("han-quoc-18-", "Hàn Quốc 18+"),
            ("viet-nam-clip", "Việt Nam Clip"),
            ("hentai", "Hentai"),
        };
    }

    public static List<MenuItem> Menu(string host, string sort, string c)
    {
        string url = $"{host.TrimEnd('/')}/sgz2";
        var menu = new List<MenuItem>
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "sgz2",
                playlist_url = url,
            },
            new MenuItem()
            {
                title = $"Sắp xếp: {(string.IsNullOrEmpty(sort) ? "Mặc định" : sort)}",
                playlist_url = url,
            }
        };

        var cats = GetCategories();
        var submenu = new List<MenuItem>();
        foreach (var (slug, title) in cats)
        {
            submenu.Add(new MenuItem()
            {
                title = title,
                playlist_url = $"{url}?c={HttpUtility.UrlEncode(slug)}",
            });
        }

        menu.Add(new MenuItem()
        {
            title = "Thể loại",
            playlist_url = "submenu",
            submenu = submenu,
        });

        return menu;
    }

    public static StreamItem StreamLinks(string html, string ehost)
    {
        var stream_links = new StreamItem() { qualitys = new Dictionary<string, string>() };

        if (string.IsNullOrEmpty(html)) return stream_links;

        var matches = Regex.Matches(html, "data-link=\"(/storage/m3u8/[^\"]*?\\.m3u8[^\"]*)\"", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            string m3u8 = m.Groups[1].Value;
            if (m3u8.StartsWith("/"))
                m3u8 = "https://x.sexgenz.blog" + m3u8;

            if (!stream_links.qualitys.ContainsKey("720p"))
                stream_links.qualitys["720p"] = m3u8;
        }

        if (stream_links.qualitys.Count == 0)
        {
            var m = Regex.Match(html, "https?://[^\">\\s]+\\.m3u8[^\"]*", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                string m3u8 = m.Value;
                stream_links.qualitys["720p"] = m3u8;
            }
        }

        stream_links.recomends = new List<PlaylistItem>();
        return stream_links;
    }
}
