using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace Txxx;

public static class TxxxTo
{
    public static string SiteHost = "https://txxx.com";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // ===== URL =====
    // API list (do bang Chrome that): /api/json/videos2/14400/str/{sort}/60/{section}.{object_id}.{page}.all...json
    //   home     -> section=""          object_id=""      => ..{page}.all...json
    //   category -> section="categories" object_id="<dir>" => categories.<dir>.{page}.all...json
    public static string ListUrl(string host, string c, string sort, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        string section = "", objectId = "";
        if (!string.IsNullOrWhiteSpace(c))
        {
            string cc = c.Trim().Trim('/');
            if (cc.StartsWith("categories/", StringComparison.OrdinalIgnoreCase))
            {
                section = "categories";
                objectId = cc.Substring("categories/".Length);
            }
        }

        string s = string.IsNullOrWhiteSpace(sort) ? "latest-updates" : sort;
        return $"{host}/api/json/videos2/14400/str/{s}/60/{section}.{objectId}.{pg}.all...json";
    }

    // search: /api/videos2.php?params=259200/str/relevance/60/search..{pg}.all..&s=<kw>
    public static string SearchUrl(string host, string search, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        if (pg < 1)
            pg = 1;

        return $"{host.TrimEnd('/')}/api/videos2.php?params=259200/str/relevance/60/search..{pg}.all..&s={HttpUtility.UrlEncode(search.Trim())}";
    }

    public static string CategoriesUrl(string host)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        return host.TrimEnd('/') + "/api/json/categories/14400/str.all.en.json";
    }

    public static string VideoInfoUrl(string host, string videoId)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        return $"{host.TrimEnd('/')}/api/json/video/86400/{Low(videoId)}/{Mid(videoId)}/{videoId}.json";
    }

    public static string RelatedUrl(string host, string videoId, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        return $"{host.TrimEnd('/')}/api/json/videos_related2/432000/50/{Low(videoId)}/{Mid(videoId)}/{videoId}.all.{pg}.json";
    }

    public static string StreamUrl(string host, string videoId)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        return $"{host.TrimEnd('/')}/api/videofile.php?video_id={videoId}&lifetime=8640000";
    }

    public static string DetailPath(string videoId, string dir)
        => $"/videos/{videoId}/{dir}/";

    // id 18145559 -> low 18000000, mid 18145000 (dung trong path API)
    static string Low(string id)
        => (long.Parse(id) / 1000000 * 1000000).ToString();

    static string Mid(string id)
        => (long.Parse(id) / 1000 * 1000).ToString();

    // ===== SORT =====
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất", "latest-updates"),
        ("Xem nhiều", "most-popular"),
        ("Đánh giá cao", "top-rated"),
        ("Dài nhất", "longest")
    };

    public static string ClampSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return null;

        foreach (var o in Sorts)
            if (o.sort == sort.Trim().ToLowerInvariant())
                return o.sort;

        return null;
    }

    public static string SortLabel(string sort)
    {
        foreach (var o in Sorts)
            if (o.sort == sort)
                return o.name.ToLowerInvariant();

        return "mới nhất";
    }

    // ===== LIST =====
    public static (List<PlaylistItem> items, int pages) Playlist(string route, string json)
    {
        var items = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(json))
            return (items, 0);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int pages = 1;
            if (root.TryGetProperty("pages", out var pj) && pj.ValueKind == JsonValueKind.Number)
                pages = pj.GetInt32();

            if (!root.TryGetProperty("videos", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return (items, 0);

            foreach (var v in arr.EnumerateArray())
            {
                string id = Str(v, "video_id");
                string dir = Str(v, "dir");
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(dir))
                    continue;

                string title = Str(v, "title");
                string picture = Str(v, "scr");

                var item = new PlaylistItem()
                {
                    video = route + "?uri=" + HttpUtility.UrlEncode(DetailPath(id, dir)),
                    name = HttpUtility.HtmlDecode(title),
                    picture = picture,
                    time = Str(v, "duration"),
                    json = true,
                    bookmark = new Bookmark()
                    {
                        site = "txxx",
                        href = DetailPath(id, dir),
                        image = picture
                    }
                };

                string pv = Str(v, "pv");
                if (!string.IsNullOrEmpty(pv))
                    item.preview = pv.StartsWith("//") ? "https:" + pv : (pv.StartsWith("http") ? pv : "https://" + pv);

                items.Add(item);
            }

            return (items, pages);
        }
        catch
        {
            return (items, 0);
        }
    }

    // ===== TAXONOMY =====
    public static List<(string name, string slug)> Categories(string json)
    {
        var res = new List<(string name, string slug)>();
        if (string.IsNullOrEmpty(json))
            return res;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("categories", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return res;

            foreach (var c in arr.EnumerateArray())
            {
                string name = Str(c, "title");
                string dir = Str(c, "dir");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dir))
                    continue;

                // hd/4k/vr la FILTER chat luong, khong phai category:
                // /categories/hd/ qua API tra 0 video (da do).
                dir = dir.Trim().ToLowerInvariant();
                if (dir is "hd" or "4k" or "vr")
                    continue;

                res.Add((HttpUtility.HtmlDecode(name), dir));
            }
        }
        catch { }

        return res;
    }

    // ===== DETAIL / POPUP =====
    // video.categories = { "4": { "title": "Anal", "dir": "anal" }, ... }
    public static List<(string kind, string slug, string name)> Chips(string json)
    {
        var res = new List<(string kind, string slug, string name)>();
        if (string.IsNullOrEmpty(json))
            return res;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("video", out var v))
                return res;

            if (v.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Object)
            {
                foreach (var c in cats.EnumerateObject())
                {
                    if (c.Value.ValueKind != JsonValueKind.Object)
                        continue;

                    string title = Str(c.Value, "title");
                    string dir = Str(c.Value, "dir");
                    if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(dir))
                        res.Add(("category", dir, HttpUtility.HtmlDecode(title)));
                }
            }
        }
        catch { }

        return res;
    }

    // ===== STREAM =====
    // [{"format":"_hq.mp4","video_url":"<base64 voi chu Cyrillic thay cho ASCII>","is_default":1}]
    public static Dictionary<string, string> StreamLinks(string host, string json)
    {
        var qualitys = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(json))
            return qualitys;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return qualitys;

            foreach (var f in doc.RootElement.EnumerateArray())
            {
                string encoded = Str(f, "video_url");
                if (string.IsNullOrEmpty(encoded))
                    continue;

                string path = DecodeUrl(encoded);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("/"))
                    continue;

                if (path.StartsWith("//"))
                    path = "https:" + path;

                if (qualitys.Count == 0)
                    qualitys["HD"] = host.TrimEnd('/') + path;
            }
        }
        catch { }

        return qualitys;
    }

    // tubecup: base64 nhung thay ky tu ASCII bang chu Cyrillic dong dang (А B C E M H O P T Y X K)
    static readonly Dictionary<char, char> Homoglyphs = new()
    {
        { 'А', 'A' }, { 'В', 'B' }, { 'С', 'C' }, { 'Е', 'E' }, { 'М', 'M' }, { 'Н', 'H' },
        { 'О', 'O' }, { 'Р', 'P' }, { 'Т', 'T' }, { 'У', 'Y' }, { 'Х', 'X' }, { 'К', 'K' },
        { 'а', 'a' }, { 'е', 'e' }, { 'о', 'o' }, { 'р', 'p' }, { 'с', 'c' }, { 'х', 'x' }
    };

    public static string DecodeUrl(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;

        try
        {
            var sb = new StringBuilder(encoded.Length);
            foreach (char ch in encoded)
            {
                // '~' = padding bi an, ',' = '/' bi thay (upornia)
                if (ch == '~')
                    continue;

                if (ch == ',')
                {
                    sb.Append('/');
                    continue;
                }

                sb.Append(Homoglyphs.TryGetValue(ch, out char ascii) ? ascii : ch);
            }

            string b64 = sb.ToString();
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }
        catch
        {
            return null;
        }
    }

    // ===== MENU =====
    public static List<MenuItem> MenuHead(string host, string search, string c, string sort)
    {
        host = host.TrimEnd('/');
        string root = host + "/txxx";

        string link(string s)
        {
            string q;
            if (!string.IsNullOrWhiteSpace(search))
                q = "search=" + HttpUtility.UrlEncode(search);
            else if (!string.IsNullOrWhiteSpace(c))
                q = "c=" + HttpUtility.UrlEncode(c);
            else
                q = "";

            if (!string.IsNullOrEmpty(s))
                q += (q.Length == 0 ? "" : "&") + "sort=" + HttpUtility.UrlEncode(s);

            return q.Length == 0 ? root : root + "?" + q;
        }

        var res = new List<MenuItem>(2)
        {
            new MenuItem()
            {
                title = "Tìm kiếm", search_on = "search_on", playlist_url = root
            }
        };

        // API search LUON ep sort=relevance -> dong "Sap xep" o day la no-op, bo han
        if (string.IsNullOrWhiteSpace(search))
        {
            var sub = new List<MenuItem>(Sorts.Length);
            foreach (var o in Sorts)
                sub.Add(new MenuItem(o.name, link(o.sort)));

            res.Add(new MenuItem()
            {
                title = $"Sắp xếp: {SortLabel(sort)}",
                playlist_url = "submenu",
                submenu = sub
            });
        }

        return res;
    }

    public static List<MenuItem> Menu(string host, List<(string name, string slug)> categories)
    {
        host = host.TrimEnd('/');
        var menu = new List<MenuItem>();

        if (categories != null && categories.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = categories.Select(x =>
                    new MenuItem(x.name, host + "/txxx?c=" + HttpUtility.UrlEncode("categories/" + x.slug))
                ).ToList()
            });
        }

        return menu;
    }

    static string Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }

    // txxx.com chan TLS fingerprint cua .NET va hay dong ket noi ngau nhien
    // voi curl_cffi (do 2026-11-28: 1/3 request SSL_connect closed) -> phai
    // gia lap chrome124 va retry. Fallback duy nhat chay duoc tren may Termux.
    public static async Task<string> CffiGet(string url, string referer, int maxTime = 15)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        string code =
            "import sys\n"
            + "from curl_cffi import requests as rq\n"
            + "u=sys.argv[1];ref=sys.argv[2];to=int(sys.argv[3])\n"
            + "r=rq.get(u,impersonate='chrome124',timeout=to,headers={'Referer':ref,'Accept':'application/json, text/plain, */*','Accept-Language':'en-US,en;q=0.9'})\n"
            + "sys.stdout.write(r.text)\n";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.Environment.Remove("LD_PRELOAD");
                psi.Environment.Remove("LD_LIBRARY_PATH");
                string py = "/data/data/com.termux/files/usr/bin/python3";
                psi.FileName = File.Exists(py) ? py : "python3";
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(code);
                psi.ArgumentList.Add(url);
                psi.ArgumentList.Add(referer ?? "");
                psi.ArgumentList.Add(Math.Max(5, maxTime).ToString());

                using var p = Process.Start(psi);
                if (p == null)
                    return null;

                string stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                    return stdout;
            }
            catch { }

            await Task.Delay(600);
        }

        return null;
    }

    // Theo redirect cua link mp4 (/get_file/... tren txxx.com) -> URL CDN that
    // (txxx.ahcdn.com, .NET tai duoc). curl_cffi vi host goc chan TLS .NET.
    public static async Task<string> CffiFinalUrl(string url, string referer, int maxTime = 15)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        string code =
            "import sys\n"
            + "from curl_cffi import requests as rq\n"
            + "u=sys.argv[1];ref=sys.argv[2];to=int(sys.argv[3])\n"
            + "r=rq.get(u,impersonate='chrome124',timeout=to,allow_redirects=True,headers={'Referer':ref})\n"
            + "sys.stdout.write(r.url)\n";

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.Environment.Remove("LD_PRELOAD");
                psi.Environment.Remove("LD_LIBRARY_PATH");
                string py = "/data/data/com.termux/files/usr/bin/python3";
                psi.FileName = File.Exists(py) ? py : "python3";
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(code);
                psi.ArgumentList.Add(url);
                psi.ArgumentList.Add(referer ?? "");
                psi.ArgumentList.Add(Math.Max(5, maxTime).ToString());

                using var p = Process.Start(psi);
                if (p == null)
                    return null;
                string stdout = (await p.StandardOutput.ReadToEndAsync())?.Trim();
                await p.WaitForExitAsync();
                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout) && stdout.StartsWith("http"))
                    return stdout;
            }
            catch { }

            await Task.Delay(600);
        }

        return null;
    }
}
