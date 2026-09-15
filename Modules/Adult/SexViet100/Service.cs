using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace SexViet100;

public static class SexViet10To
{
    public static string Uri(string host, string search, string sort, string c, string t, int pg)
    {
        var url = new System.Text.StringBuilder();
        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("search/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/");
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append(c);
            url.Append("/");
            if (pg > 1)
            {
                url.Append("trang/");
                url.Append(pg);
                url.Append("/");
            }
        }
        else if (pg > 1)
        {
            url.Append("trang/");
            url.Append(pg);
            url.Append("/");
        }

        return url.ToString();
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html, System.Func<Shared.Models.SISI.Base.PlaylistItem, Shared.Models.SISI.Base.PlaylistItem> onplaylist = null)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();

        var blocks = Regex.Matches(html, @"<li\s+class=""item-movie"">.*?</li>", RegexOptions.Singleline);
        foreach (Match block in blocks)
        {
            string b = block.Value;
            var aMatch = Regex.Match(b, @"<a\s+title=""([^""]+)""\s+href=""(https://sexviet100\.com/[^""]+\.html)""");
            if (!aMatch.Success) continue;

            string name = HttpUtility.HtmlDecode(aMatch.Groups[1].Value);
            string href = aMatch.Groups[2].Value;

            string poster = "";
            var imgMatch = Regex.Match(b, @"data-original=""([^""]+)""");
            if (imgMatch.Success)
                poster = imgMatch.Groups[1].Value;

            var pl = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = $"{uri}?uri={HttpUtility.UrlEncode(href)}",
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "sexviet100",
                    href = href,
                    image = poster
                }
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }

    public static string GetVideoId(string html)
    {
        int pos = html.IndexOf("data-video-id=");
        if (pos >= 0)
        {
            int quoteStart = html.IndexOf('"', pos + "data-video-id=".Length);
            if (quoteStart >= 0)
            {
                int quoteEnd = html.IndexOf('"', quoteStart + 1);
                if (quoteEnd > quoteStart)
                    return html.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            }
        }
        return null;
    }

    public static Dictionary<string, string> StreamLinks(string json)
    {
        var links = new Dictionary<string, string>();
        int m3u8Pos = json.IndexOf(".m3u8");
        if (m3u8Pos > 0)
        {
            int urlStart = json.LastIndexOf("https", m3u8Pos);
            if (urlStart >= 0)
            {
                int urlEnd = json.IndexOf('"', m3u8Pos);
                if (urlEnd > urlStart)
                {
                    string file = json.Substring(urlStart, urlEnd - urlStart);
                    file = file.Replace("\\/", "/");
                    links.TryAdd("Auto", file);
                }
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
                playlist_url = $"{host}/sexviet100"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Việt Nam", $"{host}/sexviet100?c=phim-sex-viet-nam"),
                    new("Nhật Bản", $"{host}/sexviet100?c=phim-sex-nhat-ban"),
                    new("Trung Quốc", $"{host}/sexviet100?c=phim-sex-trung-quoc"),
                    new("Châu Âu", $"{host}/sexviet100?c=phim-sex-chau-au"),
                    new("Vietsub", $"{host}/sexviet100?c=phim-sex-vietsub"),
                }
            }
        };
    }
}
