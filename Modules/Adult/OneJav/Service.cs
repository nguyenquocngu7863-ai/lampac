using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OneJav;

public static class OneJavTo
{
    /// <summary>Các tag phổ biến để hiện trong plugin (slug dùng cho /tag/&lt;slug&gt;).</summary>
    public static readonly (string title, string slug)[] Tags =
    {
        ("Uncensored", "uncensored"),
        ("Big Tits", "big-tits"),
        ("Creampie", "creampie"),
        ("Anal", "anal"),
        ("Amateur", "amateur"),
        ("Blowjob", "blowjob"),
        ("Cosplay", "cosplay"),
        ("Solowork", "solowork"),
        ("Lesbian", "lesbian"),
        ("Gangbang", "gangbang"),
        ("Cowgirl", "cowgirl"),
        ("4K", "4k"),
        ("Mature", "mature-woman"),
        ("School Girl", "school-girls"),
        ("Small Tits", "small-tits"),
        ("Huge Butt", "huge-butt"),
        ("Deep Throat", "deep-throating"),
        ("Married Woman", "married-woman"),
        ("Bukkake", "bukkake"),
        ("Pregnant", "pregnant-woman")
    };

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
            var m = Regex.Match(code, "^([A-Za-z]+)(\\d+)$");
            if (m.Success)
            {
                Add($"{m.Groups[1].Value}-{m.Groups[2].Value}");
                Add($"{m.Groups[1].Value} {m.Groups[2].Value}");
            }
        }

        if (upper.StartsWith("FC2") || upper.Contains("FC2PPV"))
        {
            string num = Regex.Replace(code, "fc2ppv", "", RegexOptions.IgnoreCase).Replace("-", "");
            Add($"FC2-PPV-{num}");
            Add($"FC2 PPV {num}");
            Add($"fc2ppv{num}");
        }

        Add(upper);
        Add(lower);
        return q;
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
