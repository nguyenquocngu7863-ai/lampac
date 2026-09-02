using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Chaturbate;

public static class ChaturbateTo
{
    static readonly Regex SafeTag = new("^[a-z0-9_-]+$", RegexOptions.Compiled);

    #region Uri
    public static string Uri(string host, string sort, string tag, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/api/ts/roomlist/room-list/?enable_recommendations=false&limit=90");

        if (!string.IsNullOrWhiteSpace(sort))
        {
            url.Append("&genders=");
            url.Append(sort);
        }

        if (!string.IsNullOrWhiteSpace(tag) && SafeTag.IsMatch(tag))
        {
            url.Append("&hashtags=");
            url.Append(tag);
        }

        if (pg > 1)
        {
            url.Append("&offset=");
            url.Append((pg - 1) * 90);
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("display_age", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            if (!row.Contains("\"current_show\":\"public\""))
                continue;

            string baba = row.Match("\"username\":\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(baba))
                continue;

            string img = row.Match("\"img\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(img))
                continue;

            var pl = new PlaylistItem()
            {
                name = baba.Trim(),
                video = $"{route}?baba={baba}",
                picture = img.Replace("\\", ""),
                json = true
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }
    #endregion

    #region Menu
    static string ListUrl(string host, string sort, string tag)
    {
        var url = StringBuilderPool.ThreadInstance;
        url.Append(host);
        url.Append("/chu");

        bool hasQuery = false;
        void append(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            url.Append(hasQuery ? '&' : '?');
            hasQuery = true;
            url.Append(key);
            url.Append('=');
            url.Append(value);
        }

        append("sort", sort);
        append("c", tag);
        return url.ToString();
    }

    public static List<MenuItem> Menu(string host, string sort, string tag)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Chaturbate_menu_{host}_{sort}_{tag}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        var sortmenu = new List<MenuItem>(5)
        {
            new("Tốt nhất", ListUrl(host, null, tag)),
            new("Nữ", ListUrl(host, "f", tag)),
            new("Cặp đôi", ListUrl(host, "c", tag)),
            new("Nam", ListUrl(host, "m", tag)),
            new("Chuyển giới", ListUrl(host, "t", tag))
        };

        var catmenu = new List<MenuItem>(34)
        {
            new("Tất cả", ListUrl(host, sort, null)),
            new("Á châu", ListUrl(host, sort, "asian")),
            new("Nhật Bản", ListUrl(host, sort, "japanese")),
            new("Hàn Quốc", ListUrl(host, sort, "korean")),
            new("Ấn Độ", ListUrl(host, sort, "indian")),
            new("Ả Rập", ListUrl(host, sort, "arab")),
            new("Nga", ListUrl(host, sort, "russian")),
            new("Latinh", ListUrl(host, sort, "latina")),
            new("Da đen", ListUrl(host, sort, "ebony")),
            new("Da trắng", ListUrl(host, sort, "white")),
            new("Tóc vàng", ListUrl(host, sort, "blonde")),
            new("Tóc nâu", ListUrl(host, sort, "brunette")),
            new("Tóc đỏ", ListUrl(host, sort, "redhead")),
            new("MILF", ListUrl(host, sort, "milf")),
            new("Trưởng thành", ListUrl(host, sort, "mature")),
            new("Nhỏ nhắn", ListUrl(host, sort, "petite")),
            new("Ngực lớn", ListUrl(host, sort, "bigtits")),
            new("Mông lớn", ListUrl(host, sort, "bigass")),
            new("Mới lên sóng", ListUrl(host, sort, "new")),
            new("Lovense", ListUrl(host, sort, "lovense")),
            new("Tương tác", ListUrl(host, sort, "interactive")),
            new("Anal", ListUrl(host, sort, "anal")),
            new("Squirt", ListUrl(host, sort, "squirt")),
            new("Lesbian", ListUrl(host, sort, "lesbian")),
            new("Tự sướng", ListUrl(host, sort, "masturbation")),
            new("Đồ chơi", ListUrl(host, sort, "toys")),
            new("Ngoài trời", ListUrl(host, sort, "outdoor")),
            new("Xăm", ListUrl(host, sort, "tattoo")),
            new("Hút thuốc", ListUrl(host, sort, "smoking")),
            new("Chân", ListUrl(host, sort, "feet")),
            new("BDSM", ListUrl(host, sort, "bdsm")),
            new("Có lông", ListUrl(host, sort, "hairy")),
            new("Cạo", ListUrl(host, sort, "shaven"))
        };

        string genderTitle = string.IsNullOrWhiteSpace(sort)
            ? "Tốt nhất"
            : sortmenu.FirstOrDefault(i => i.playlist_url.Contains($"sort={sort}"))?.title ?? "Tốt nhất";

        string catTitle = string.IsNullOrWhiteSpace(tag)
            ? "tất cả"
            : catmenu.FirstOrDefault(i => i.playlist_url.Contains($"c={tag}"))?.title ?? tag;

        menu = new List<MenuItem>(2)
        {
            new MenuItem()
            {
                title = $"Giới tính: {genderTitle}",
                playlist_url = "submenu",
                submenu = sortmenu
            },
            new MenuItem()
            {
                title = $"Danh mục: {catTitle}",
                playlist_url = "submenu",
                submenu = catmenu
            }
        };

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string baba)
    {
        if (string.IsNullOrWhiteSpace(baba))
            return null;

        return $"{host}/{baba}/";
    }

    public static Dictionary<string, string> StreamLinks(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        string hls =
            Rx.Match(html, "(https?://[^ ]+/playlist\\.m3u8)") ??
            Rx.Match(html, @"\\u0022hls_source\\u0022: \\u0022([^, ]+)\\u0022,");

        if (hls == null)
            return null;

        return new Dictionary<string, string>()
        {
            ["auto"] = Regex.Unescape(hls).Replace("\\", "")
        };
    }
    #endregion
}
