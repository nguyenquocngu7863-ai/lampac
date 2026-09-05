using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Stripchat;

public static class StripchatTo
{
    #region Uri
    public static string Uri(string host, string sort, int pg)
    {
        // sort = primaryTag: girls|boys|couples|trans  (null = girls default)
        string primaryTag = string.IsNullOrWhiteSpace(sort) ? "girls" : sort;
        var url = StringBuilderPool.ThreadInstance;
        url.Append(host);
        url.Append("/api/front/models?limit=90&filterGroup=presets&primaryTag=");
        url.Append(primaryTag);
        if (pg > 1)
        {
            url.Append("&offset=");
            url.Append((pg - 1) * 90);
        }
        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> json, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (json.IsEmpty)
            return null;

        JObject root;
        try { root = JObject.Parse(json.ToString()); }
        catch { return null; }

        var arr = root["models"] as JArray;
        if (arr == null || arr.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(arr.Count);
        foreach (var t in arr)
        {
            string username = t.Value<string>("username");
            string hls = t.Value<string>("hlsPlaylist");
            if (string.IsNullOrWhiteSpace(username))
                continue;

            // filter non-public shows - keep online but allow all
            // isLive check optional, many SISI modules filter live only
            // keep all for now

            string img = t.Value<string>("previewUrlThumbSmall") ?? t.Value<string>("avatarUrl") ?? "";
            if (!string.IsNullOrEmpty(img))
                img = img.Replace("\\", "");

            string status = t.Value<string>("status") ?? "";
            string topic = t.Value<string>("groupShowTopic") ?? "";

            string name = username;
            if (!string.IsNullOrWhiteSpace(topic))
                name = $"{username} — {topic}";
            name = name.Trim();
            if (name.Length > 80) name = name.Substring(0, 80);

            var pl = new PlaylistItem()
            {
                name = name,
                // encode hls directly so potok can return without second fetch
                video = string.IsNullOrWhiteSpace(hls)
                    ? $"{route}?baba={HttpUtility.UrlEncode(username)}"
                    : $"{route}?hls={HttpUtility.UrlEncode(hls)}&baba={HttpUtility.UrlEncode(username)}",
                picture = img,
                json = true,
                time = status
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }
    #endregion

    #region Menu
    static string ListUrl(string host, string sort)
    {
        var url = StringBuilderPool.ThreadInstance;
        url.Append(host);
        url.Append("/strp");
        if (!string.IsNullOrWhiteSpace(sort))
        {
            url.Append("?sort=");
            url.Append(sort);
        }
        return url.ToString();
    }

    public static List<MenuItem> Menu(string host, string sort)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Stripchat_menu_{host}_{sort}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        var sortmenu = new List<MenuItem>(4)
        {
            new("Nữ", ListUrl(host, "girls")),
            new("Nam", ListUrl(host, "boys")),
            new("Cặp đôi", ListUrl(host, "couples")),
            new("Chuyển giới", ListUrl(host, "trans"))
        };

        string title = sort switch
        {
            "boys" => "Nam",
            "couples" => "Cặp đôi",
            "trans" => "Chuyển giới",
            _ => "Nữ"
        };

        menu = new List<MenuItem>(1)
        {
            new MenuItem()
            {
                title = $"Giới tính: {title}",
                playlist_url = "submenu",
                submenu = sortmenu
            }
        };

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string baba, string hls)
    {
        if (!string.IsNullOrWhiteSpace(hls))
            return hls;

        // fallback: try model detail API via list api with search by username
        // 418 on direct username endpoint, so reuse list filter
        if (!string.IsNullOrWhiteSpace(baba))
            return $"{host}/api/front/models?limit=1&filterGroup=presets&primaryTag=girls";

        return null;
    }

    public static Dictionary<string, string> StreamLinks(ReadOnlySpan<char> json, string baba, string hls)
    {
        if (!string.IsNullOrWhiteSpace(hls))
            return new Dictionary<string, string>() { ["auto"] = hls };

        if (json.IsEmpty)
            return null;

        try
        {
            var root = JObject.Parse(json.ToString());
            var arr = root["models"] as JArray;
            if (arr != null)
            {
                foreach (var t in arr)
                {
                    if (t.Value<string>("username") != baba) continue;
                    string h = t.Value<string>("hlsPlaylist");
                    if (!string.IsNullOrWhiteSpace(h))
                        return new Dictionary<string, string>() { ["auto"] = h };
                }
            }
        }
        catch { }

        return null;
    }
    #endregion
}
