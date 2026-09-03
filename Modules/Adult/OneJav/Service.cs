using Shared.Models.SISI.Base;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace OneJav;

public static class OneJavTo
{
    #region Uri
    public static string Uri(string host, string search, string c, int pg)
    {
        var url = new System.Text.StringBuilder();
        url.Append(host);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // onejav exposes a per-tag listing; code/actress search is wired later.
            url.Append("/tag/");
            url.Append(HttpUtility.UrlEncode(search).Replace("%20", "-"));
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            url.Append("/tag/");
            url.Append(HttpUtility.UrlEncode(c).Replace("%20", "-"));
        }
        else
        {
            url.Append("/");
        }

        if (pg > 1)
            url.Append(url.ToString().Contains('?') ? "&" : "?").Append("page=").Append(pg);

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        // A card on onejav is an <a href="/torrent/<code>">…</a> wrapping the
        // poster <img> and a "CODE title (x.x GB)" label. Iterate each anchor.
        var matches = Rx.Matches("<a[^>]+href=\"/torrent/([^\"?#]+)\"[^>]*>(.*?)</a>", html);
        if (matches.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(matches.Count);
        var seen = new HashSet<string>();

        foreach (var row in matches.Rows())
        {
            var g = row.Groups("<a[^>]+href=\"/torrent/([^\"?#]+)\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
            string code = g[1].Value.Trim();
            string body = g[2].Value;
            string href = $"/torrent/{code}";

            if (string.IsNullOrWhiteSpace(code) || !seen.Add(href))
                continue;

            string img = Rx.Match(body, "(?:src|data-src)=\"(https?://[^\"]+\\.(?:jpg|jpeg|png|webp))\"");
            if (string.IsNullOrWhiteSpace(img))
                continue;

            // Label after the image: "CODE name (x.x GB)" — take the code + size.
            string size = Rx.Match(body, "\\(([0-9]+(?:\\.[0-9]+)?\\s*(?:GB|MB))\\)");
            string name = string.IsNullOrWhiteSpace(size) ? code : $"{code}  ({size})";

            playlists.Add(new PlaylistItem()
            {
                name = name,
                // Detail/play route is a later step; keep the card pointing at
                // the onejav detail handler so tapping does nothing harmful yet.
                video = $"ojv/view?uri={HttpUtility.UrlEncode(href)}",
                picture = img,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "ojv",
                    href = href,
                    image = img
                }
            });
        }

        return playlists;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string url)
    {
        var menu = new List<MenuItem>(4)
        {
            new MenuItem()
            {
                title = "Mới nhất",
                playlist_url = url
            },
            new MenuItem()
            {
                title = "Tìm theo mã",
                search_on = "search_on",
                playlist_url = url
            }
        };

        // Common onejav /tag/<Name> categories (JAV genres + sources).
        var cats = new (string title, string tag)[]
        {
            ("Uncensored", "Uncensored"),
            ("FC2", "FC2"),
            ("Amateur", "Amateur"),
            ("Creampie", "Creampie"),
            ("Blowjob", "Blow"),
            ("POV", "POV"),
            ("Cowgirl", "Cowgirl"),
            ("Anal", "Anal"),
            ("Big Tits", "Big Tits"),
            ("Beautiful Girl", "Beautiful Girl"),
            ("School Girl", "School Girl"),
            ("MILF", "MILF"),
            ("Shaved", "Shaved"),
            ("Threesome", "Threesome"),
            ("Lesbian", "Lesbian"),
            ("Bondage", "Bondage"),
        };

        var catmenu = new List<MenuItem>(cats.Length + 1)
        {
            new("Tất cả", $"{url}")
        };
        foreach (var (title, tag) in cats)
            catmenu.Add(new MenuItem(title, $"{url}?c={HttpUtility.UrlEncode(tag)}"));

        menu.Add(new MenuItem()
        {
            title = "Danh mục",
            playlist_url = "submenu",
            submenu = catmenu
        });

        return menu;
    }
    #endregion
}
