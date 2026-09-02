using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Services.Hybrid;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;

namespace Runetki;

public static class RunetkiTo
{
    public static string Uri(string host, string sort, int pg)
    {
        return $"{host}/tools/listing_v3.php?livetab={sort ?? "all"}&offset={(pg > 1 ? ((pg - 1) * 72) : 0)}&limit=72";
    }

    public static List<PlaylistItem> Playlist(ReadOnlySpan<char> html, out int total_pages, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        total_pages = 0;

        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("\"gender\"", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            string baba = row.Match("\"username\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(baba))
                continue;

            string esid = row.Match("\"esid\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(esid))
                continue;

            string img = row.Match("\"thumb_image\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(img))
                continue;

            string title = row.Match("\"display_name\":\"([^\"]+)\"");
            if (string.IsNullOrEmpty(title))
                title = baba;

            string cleanImg = img.Replace("\\", "").Replace("{ext}", "jpg");

            var pl = new PlaylistItem()
            {
                name = title,
                quality = row.Match("\"vq\":\"([^\"]+)\""),
                video = $"https://{esid}.bcvcdn.com/hls/stream_{baba}/playlist.m3u8",
                picture = cleanImg.StartsWith("http") ? cleanImg : $"https:{cleanImg}"
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        string total_count = Rx.Match(html, "\"total_count\":([0-9]+),");
        if (total_count != null && int.TryParse(total_count, out int total) && total > 0)
        {
            if (72 >= total)
                total_pages = 1;
            else
                total_pages = (total / 72) + 1;
        }

        return playlists;
    }

    public static List<MenuItem> Menu(string host, string sort)
        => LiveCamMenu.Build(host, "runetki", sort);

    static class LiveCamMenu
    {

        public static List<MenuItem> Build(string host, string route, string sort)
        {
            var memoryCache = HybridCache.GetMemory();
            string menuKey = $"LiveCam_menu_{host}_{route}_{sort}";

            if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
                return menu;

            var genderMenu = new List<MenuItem>(6)
            {
                new("Tốt nhất", ListUrl(host, route, null)),
                new("Mới nhất", ListUrl(host, route, "new")),
                new("Nữ", ListUrl(host, route, "female")),
                new("Cặp đôi", ListUrl(host, route, "couples")),
                new("Nam", ListUrl(host, route, "male")),
                new("Chuyển giới", ListUrl(host, route, "transsexual"))
            };

            // The live providers use a female/tags/<tag> listing for their
            // category pages. Keep the tags in one place so both cam sources have
            // the same useful browsing experience as Chaturbate.
            var categoryMenu = new List<MenuItem>(34)
            {
                new("Tất cả", CategoryAllUrl(host, route, sort)),
                new("Á châu", TagUrl(host, route, "asian")),
                new("Nhật Bản", TagUrl(host, route, "japanese")),
                new("Hàn Quốc", TagUrl(host, route, "korean")),
                new("Ấn Độ", TagUrl(host, route, "indian")),
                new("Ả Rập", TagUrl(host, route, "arab")),
                new("Nga", TagUrl(host, route, "russian")),
                new("Latinh", TagUrl(host, route, "latina")),
                new("Da đen", TagUrl(host, route, "ebony")),
                new("Da trắng", TagUrl(host, route, "white")),
                new("Tóc vàng", TagUrl(host, route, "blonde")),
                new("Tóc nâu", TagUrl(host, route, "brunette")),
                new("Tóc đỏ", TagUrl(host, route, "redhead")),
                new("MILF", TagUrl(host, route, "milf")),
                new("Trưởng thành", TagUrl(host, route, "mature")),
                new("Nhỏ nhắn", TagUrl(host, route, "petite")),
                new("Ngực lớn", TagUrl(host, route, "bigtits")),
                new("Mông lớn", TagUrl(host, route, "bigass")),
                new("Mới lên sóng", TagUrl(host, route, "new")),
                new("Lovense", TagUrl(host, route, "lovense")),
                new("Tương tác", TagUrl(host, route, "interactive")),
                new("Anal", TagUrl(host, route, "anal")),
                new("Squirt", TagUrl(host, route, "squirt")),
                new("Lesbian", TagUrl(host, route, "lesbian")),
                new("Tự sướng", TagUrl(host, route, "masturbation")),
                new("Đồ chơi", TagUrl(host, route, "toys")),
                new("Ngoài trời", TagUrl(host, route, "outdoor")),
                new("Xăm", TagUrl(host, route, "tattoo")),
                new("Hút thuốc", TagUrl(host, route, "smoking")),
                new("Chân", TagUrl(host, route, "feet")),
                new("BDSM", TagUrl(host, route, "bdsm")),
                new("Có lông", TagUrl(host, route, "hairy")),
                new("Cạo", TagUrl(host, route, "shaven"))
            };

            string genderTitle = GenderTitle(sort);
            string categoryTitle = CategoryTitle(sort, categoryMenu);

            menu = new List<MenuItem>(2)
            {
                new MenuItem()
                {
                    title = $"Giới tính: {genderTitle}",
                    playlist_url = "submenu",
                    submenu = genderMenu
                },
                new MenuItem()
                {
                    title = $"Danh mục: {categoryTitle}",
                    playlist_url = "submenu",
                    submenu = categoryMenu
                }
            };

            if (CoreInit.conf.lowMemoryMode == false)
                memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

            return menu;
        }

        static string ListUrl(string host, string route, string sort)
        {
            return string.IsNullOrWhiteSpace(sort)
                ? $"{host}/{route}"
                : $"{host}/{route}?sort={sort}";
        }

        static string TagUrl(string host, string route, string tag)
            => ListUrl(host, route, $"female/tags/{tag}");

        static string CategoryAllUrl(string host, string route, string sort)
        {
            if (!string.IsNullOrWhiteSpace(sort) && sort.StartsWith("female/tags/", StringComparison.OrdinalIgnoreCase))
                sort = "female";

            return ListUrl(host, route, sort);
        }

        static string GenderTitle(string sort)
        {
            if (string.IsNullOrWhiteSpace(sort))
                return "Tốt nhất";

            if (sort.StartsWith("female/tags/", StringComparison.OrdinalIgnoreCase))
                return "Nữ";

            return sort switch
            {
                "new" => "Mới nhất",
                "female" => "Nữ",
                "couples" => "Cặp đôi",
                "male" => "Nam",
                "transsexual" => "Chuyển giới",
                _ => sort
            };
        }

        static string CategoryTitle(string sort, List<MenuItem> categoryMenu)
        {
            const string prefix = "female/tags/";
            if (string.IsNullOrWhiteSpace(sort) || !sort.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return "tất cả";

            string tag = sort[prefix.Length..];
            foreach (var item in categoryMenu)
            {
                if (item.playlist_url.EndsWith($"sort={sort}", StringComparison.OrdinalIgnoreCase))
                    return item.title;
            }

            return tag;
        }
    }
}
