using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace Phe69;

public static class Phe69To
{
    public static string Uri(string host, string search, string c, int pg)
    {
        var url = new System.Text.StringBuilder();
        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("?s=");
            url.Append(HttpUtility.UrlEncode(search));
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append(c.Trim('/'));
            url.Append("/");
            if (pg > 1)
            {
                url.Append("page/");
                url.Append(pg);
                url.Append("/");
            }
        }
        else if (pg > 1)
        {
            url.Append("page/");
            url.Append(pg);
            url.Append("/");
        }

        return url.ToString();
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();

        var blocks = Regex.Matches(html, @"<article.*?</article>", RegexOptions.Singleline);
        foreach (Match block in blocks)
        {
            string b = block.Value;
            var aMatch = Regex.Match(b, @"<a\s+href=""(https://phe69\.shop/[^""]+/)"".*?title=""([^""]+)""");
            if (!aMatch.Success)
                continue;

            string href = aMatch.Groups[1].Value;
            string name = HttpUtility.HtmlDecode(aMatch.Groups[2].Value);

            string poster = "";
            var imgMatch = Regex.Match(b, @"<img[^>]+class=""[^""]*video-main-thumb[^""]*""[^>]+src=""([^""]+)""");
            if (!imgMatch.Success)
                imgMatch = Regex.Match(b, @"<img[^>]+src=""([^""]+)""");
            if (imgMatch.Success)
                poster = imgMatch.Groups[1].Value;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = $"{uri}?uri={HttpUtility.UrlEncode(href)}",
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "phe69",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    public static string GetIframe(string html)
    {
        var m = Regex.Match(html, @"<iframe\s+src=""(https://play\.phe69\.shop/data/[^""]+)""");
        return m.Success ? m.Groups[1].Value : null;
    }

    static string Base62(int n)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (n < 62)
            return chars[n].ToString();
        return Base62(n / 62) + chars[n % 62];
    }

    public static string UnpackDeanEdwards(string html)
    {
        var m = Regex.Match(html, @"\}\('(.*)',(\d+),(\d+),'(.*)'\.split\('\|'\)", RegexOptions.Singleline);
        if (!m.Success)
            return null;

        string p = m.Groups[1].Value;
        int a = int.Parse(m.Groups[2].Value);
        int c = int.Parse(m.Groups[3].Value);
        string[] k = m.Groups[4].Value.Split('|');
        if (a != 62 || k.Length < c)
            return null;

        for (int i = c - 1; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(k[i]))
                continue;
            p = Regex.Replace(p, @"\b" + Regex.Escape(Base62(i)) + @"\b", k[i]);
        }

        return p;
    }

    public static Dictionary<string, string> StreamLinks(string unpacked)
    {
        var links = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(unpacked))
            return links;

        foreach (Match m in Regex.Matches(unpacked, @"'label':'([^']+)','type':'[^']+','file':'(https?[^']+\.mp4)'"))
        {
            string file = m.Groups[2].Value.Replace("\\/", "/");
            links.TryAdd(m.Groups[1].Value, file);
        }

        if (links.Count == 0)
        {
            var m = Regex.Match(unpacked, @"'(https?[^']+\.mp4)'");
            if (m.Success)
                links.TryAdd("Auto", m.Groups[1].Value.Replace("\\/", "/"));
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
                playlist_url = $"{host}/phe69"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Phim sex mới", $"{host}/phe69?c=phim-sex-moi"),
                    new("Sinh viên", $"{host}/phe69?c=phim-sex-sinh-vien"),
                    new("Gái dâm", $"{host}/phe69?c=gai-dam"),
                    new("Check hàng", $"{host}/phe69?c=check-hang"),
                    new("Tự quay", $"{host}/phe69?c=tu-quay"),
                    new("Thủ dâm", $"{host}/phe69?c=thu-dam"),
                    new("Doggy", $"{host}/phe69?c=doggy"),
                    new("BDSM", $"{host}/phe69?c=bdsm"),
                    new("JAV HD", $"{host}/phe69?c=jav-hd"),
                    new("Không che", $"{host}/phe69?c=sex-khong-che"),
                    new("Máy bay", $"{host}/phe69?c=may-bay-ba-gia"),
                    new("Camera", $"{host}/phe69?c=camera"),
                }
            }
        };
    }
}
