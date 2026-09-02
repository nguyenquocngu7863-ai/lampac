using Newtonsoft.Json.Linq;
using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace OneJav;

public static class OneJavTo
{
    public static List<MenuItem> Menu(string host, string search)
    {
        string url = $"{host}/oj";

        var menu = new List<MenuItem>
        {
            new MenuItem
            {
                title = "Поиск",
                search_on = "search_on",
                playlist_url = url
            },
            new MenuItem("Новинки", url),
            new MenuItem("Uncensored", $"{url}?c=uncensored"),
            new MenuItem("Big Tits", $"{url}?c=big-tits"),
            new MenuItem("Creampie", $"{url}?c=creampie"),
            new MenuItem("Amateur", $"{url}?c=amateur"),
            new MenuItem("Cosplay", $"{url}?c=cosplay"),
            new MenuItem("4K", $"{url}?c=4k")
        };

        return menu;
    }

    public static string Abs(string u, string host)
    {
        if (string.IsNullOrEmpty(u)) return "";
        u = WebUtility.HtmlDecode(u);
        if (u.StartsWith("//")) return "https:" + u;
        if (u.StartsWith("http")) return u;
        host = (host ?? "").TrimEnd('/');
        if (u.StartsWith('/')) return host + u;
        return host + "/" + u;
    }

    static readonly string[] VideoExt = { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v", ".ts", ".flv" };
    static readonly string[] BadNames = { "sample", "trailer", "opening", "ending", "preview", "menu", "extra", "bonus" };

    /// <summary>Đọc JSON stat của TorrServer, chọn file video lớn nhất (bỏ sample). Trả về id hoặc -1.</summary>
    public static int PickVideoIndex(string statJson)
    {
        if (string.IsNullOrWhiteSpace(statJson)) return -1;
        try
        {
            var stat = JObject.Parse(statJson);
            var files = stat["file_stats"] as JArray;
            if (files == null || files.Count == 0) return -1;

            int best = -1;
            long bestLen = -1;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string path = (f["path"]?.ToString() ?? "").ToLowerInvariant();
                string ext = System.IO.Path.GetExtension(path);
                if (!VideoExt.Contains(ext)) continue;

                string baseName = System.IO.Path.GetFileName(path);
                if (BadNames.Any(b => baseName.Contains(b))) continue;

                long len = f["length"]?.Value<long?>() ?? 0;
                if (len > bestLen) { bestLen = len; best = f["id"]?.Value<int?>() ?? i; }
            }
            return best;
        }
        catch { return -1; }
    }
}
