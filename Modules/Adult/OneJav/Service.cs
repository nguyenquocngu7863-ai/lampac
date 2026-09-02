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
        };

        void Tag(string title, string slug) =>
            menu.Add(new MenuItem(title, $"{url}?c={slug}"));

        // Tag phổ biến trên onejav (slug lấy từ /tag/<slug>)
        Tag("Uncensored", "uncensored");
        Tag("Big Tits", "big-tits");
        Tag("Creampie", "creampie");
        Tag("Anal", "anal");
        Tag("Amateur", "amateur");
        Tag("Blowjob", "blowjob");
        Tag("Cosplay", "cosplay");
        Tag("Solowork", "solowork");
        Tag("Lesbian", "lesbian");
        Tag("Gangbang", "gangbang");
        Tag("Cowgirl", "cowgirl");
        Tag("4K", "4k");
        Tag("Mature", "mature-woman");
        Tag("School Girl", "school-girls");
        Tag("Small Tits", "small-tits");
        Tag("Huge Butt", "huge-butt");
        Tag("Deep Throat", "deep-throating");
        Tag("Married Woman", "married-woman");
        Tag("Bukkake", "bukkake");
        Tag("Pregnant", "pregnant-woman");

        return menu;
    }

    /// <summary>Sinh các biến thể truy vấn từ mã JAV (SSIS-123 → SSIS123, SSIS 123, FC2...).</summary>
    public static List<string> SearchQueries(string code)
    {
        var q = new List<string>();
        void Add(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            s = s.Trim();
            if (!q.Contains(s)) q.Add(s);
        }

        Add(code);
        string upper = code.ToUpperInvariant();
        string lower = code.ToLowerInvariant();

        if (code.Contains('-'))
        {
            var parts = code.Split('-');
            Add(string.Join("", parts));
            Add(string.Join(" ", parts));
            Add(string.Join(" - ", parts));
        }
        else
        {
            var m = System.Text.RegularExpressions.Regex.Match(code, "^([A-Za-z]+)(\\d+)$");
            if (m.Success)
            {
                Add($"{m.Groups[1].Value}-{m.Groups[2].Value}");
                Add($"{m.Groups[1].Value} {m.Groups[2].Value}");
            }
        }

        if (upper.StartsWith("FC2") || upper.Contains("FC2PPV"))
        {
            string num = System.Text.RegularExpressions.Regex.Replace(code, "fc2ppv", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Replace("-", "");
            Add($"FC2-PPV-{num}");
            Add($"FC2 PPV {num}");
            Add($"fc2ppv{num}");
        }

        Add(upper);
        Add(lower);
        return q;
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
