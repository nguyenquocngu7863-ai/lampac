using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Vjav;

public static class VjavTo
{
    public static string SiteHost = "https://vjav.com";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) "
        + "AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/152.0.0.0 Mobile Safari/537.36";

    // Site CSR: HTML chi co `<div id="app"></div>` (Vue), KHONG du list.
    // Phai goi JSON API, khong can Chrome:
    //   list      /api/json/videos2/{lt}/{gender}/{sort}/{count}/{sec}.{obj}.{pg}.{type}.{dur}.{date}.{ext}
    //   search    /api/videos2.php?params={lt}/{gender}/relevance/{count}/search.{obj}.{pg}.{type}.{dur}.{date}&s=<kw>
    //   categories /api/json/categories/{lt}/{gender}.{sec}.{lang}.{ext}
    //   videofile /api/videofile.php?video_id=X&lifetime=Y  -> video_url MA HOA
    // `section` PHAI rong cho list thuong; "categories" cho danh muc.
    // Gan `all` cho section se tra {"error":"invalid_params_section"}.
    public const string Lt = "86400";
    public const string Count = "60";

    // 5 kieu xep hang THAT (do: 5 phim dau moi sort deu khac nhau).
    // `most-viewed` bi loai vi trung 5/5 voi `most-popular`.
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất",        "latest-updates"),
        ("Đánh giá cao",    "top-rated"),
        ("Phổ biến",        "most-popular"),
        ("Dài nhất",        "longest"),
        ("Bình luận nhiều", "most-commented"),
    };

    static string H(string host)
        => (string.IsNullOrEmpty(host) ? SiteHost : host).TrimEnd('/');

    // URL JSON API cho list. `c` rong = list thuong; `cat/<dir>` = danh muc.
    public static string ApiUrl(string host, string search, string c, int pg, string sort)
    {
        host = H(host);
        if (pg < 1)
            pg = 1;

        string so = string.IsNullOrWhiteSpace(sort) ? "latest-updates" : sort;

        if (!string.IsNullOrWhiteSpace(search))
            return host + "/api/videos2.php?params=" + Lt + "/str/relevance/"
                + Count + "/search.." + pg + ".all..&s="
                + HttpUtility.UrlEncode(search.Trim());

        if (string.IsNullOrWhiteSpace(c))
            return host + "/api/json/videos2/" + Lt + "/str/" + so + "/"
                + Count + "/.." + pg + ".all...json";

        string obj = c.StartsWith("cat/", StringComparison.OrdinalIgnoreCase)
            ? c.Substring(4).Trim('/')
            : c.Trim('/');

        return host + "/api/json/videos2/" + Lt + "/str/" + so + "/"
            + Count + "/categories." + obj + "." + pg + ".all...json";
    }

    // Poster: `scr` THUONG da la file anh day du
    //   .../240x180/16.jpg     (khung chon)
    // Chi gan "/1.jpg" khi `scr` van la thu muc (het bang "/").
    static string Poster(JsonElement v, string id)
    {
        string scr = Str(v, "scr").Trim();
        if (scr.Length > 0)
        {
            if (scr.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || scr.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || scr.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                || scr.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
                return scr;
            return scr.TrimEnd('/') + "/1.jpg";
        }

        // fallback khi site bo `scr`
        if (!long.TryParse(id, out long n))
            return "";
        long id2 = n / 1000 * 1000;
        return "https://tn.vjav.com/contents/videos_screenshots/"
            + id2 + "/" + n + "/240x180/1.jpg";
    }

    static string Str(JsonElement o, string key)
    {
        if (!o.TryGetProperty(key, out var v))
            return "";
        if (v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        if (v.ValueKind == JsonValueKind.Number)
            return v.ToString();
        return "";
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return Regex.Replace(s.Trim(), @"\s+", " ");
    }

    // JSON list -> playlist. KHONG dung regex tren JSON (title co the chua
    // dau " phan tan).
    public static List<PlaylistItem> Playlist(string json)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrWhiteSpace(json))
            return list;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return list; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return list;
            if (!root.TryGetProperty("videos", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in arr.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object)
                    continue;

                string id = Str(v, "video_id");
                string dir = Str(v, "dir");
                string name = Clean(HttpUtility.HtmlDecode(Str(v, "title")));

                if (id.Length == 0 || name.Length == 0)
                    continue;

                // url chinh: /videos/<id>/<dir>/ (lay tu window.location.host)
                string uri = SiteHost + "/videos/" + id + "/" + dir + "/";
                if (!seen.Add(uri))
                    continue;

                string poster = Poster(v, id);
                string time = Clean(Str(v, "duration"));

                list.Add(new PlaylistItem()
                {
                    video = "vjav/vidosik?uri=" + HttpUtility.UrlEncode(uri),
                    name = name,
                    picture = poster,
                    time = time,
                    json = true,
                    bookmark = new Bookmark()
                    {
                        site = "vjav",
                        href = uri,
                        image = poster
                    }
                });
            }
        }

        return list;
    }

    // /api/json/categories/14400/str.all.en.json -> 156 muc
    public static List<(string name, string path)> Categories(string json)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(json))
            return res;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return res; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("categories", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return res;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in arr.EnumerateArray())
            {
                if (v.ValueKind != JsonValueKind.Object)
                    continue;

                string dir = Str(v, "dir").Trim('/');
                string name = Clean(HttpUtility.HtmlDecode(Str(v, "title")));

                if (dir.Length == 0 || name.Length == 0)
                    continue;
                if (dir.All(char.IsDigit))
                    continue;
                if (!seen.Add(dir))
                    continue;

                res.Add((name, "cat/" + dir));
            }
        }

        return res;
    }

    // FALLBACK TINH — chi dung khi mang loi, chu khong menu se co 2 dong
    // (app cache ca phien). 11 muc nay la cac href `/categories/<dir>/`
    // doc tu trang chu, da kiem la slug that.
    public static readonly IReadOnlyList<(string name, string path)> FallbackCats =
        new (string, string)[]
        {
            ("Asian",             "cat/asian"),
            ("Japanese",          "cat/japanese"),
            ("HD",                "cat/hd"),
            ("Censored",          "cat/jav-censored"),
            ("Uncensored",        "cat/jav-uncensored"),
            ("Brunette",          "cat/brunette"),
            ("Blowjob",           "cat/blowjob"),
            ("Big Tits",          "cat/big-tits"),
            ("Female Orgasm",     "cat/female-orgasm"),
            ("Hairy",             "cat/hairy"),
            ("Compilation",       "cat/compilation"),
            ("Amateur",           "cat/amateur"),
        };

    // ───────── MA HOA video_url ─────────
    // BANG 65 KY TU, chi muc 64 = '~' la PADDING (tuong '=' cua base64).
    // Nhung ky tu trong sach dung VIET HOA CYRILLIC gia dong Latin:
    //   'М'(U+041C) thay M, 'Е'(U+0415) thay E, 'А'(U+0410) thay A,
    //   'В'(U+0412) thay B, 'С'(U+0421) thay C.
    // -> MAI MAI dung bo ky tu nay, dung `Convert.FromBase64String`
    //    (noi dung `М` la ky tu la -> loi "Invalid base64").
    // Regex loc cua site: [^АВСЕМA-Za-z0-9.,~] (chi giu 5 cyrillic + latin).
    public const string B65 =
        "АВСDЕFGHIJKLМNOPQRSTUVWXYZ"
        + "abcdefghijklmnopqrstuvwxyz0123456789.,~";

    static readonly HashSet<char> Keep = new HashSet<char>(
        "АВСЕМABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,~");

    public static string DecodeVideoUrl(string enc)
    {
        if (string.IsNullOrEmpty(enc))
            return null;

        var sb = new StringBuilder(enc.Length);
        foreach (char ch in enc)
            if (Keep.Contains(ch))
                sb.Append(ch);

        string e = sb.ToString();
        if (e.Length < 4)
            return null;

        var outb = new StringBuilder(e.Length);
        int p = 0;

        while (p < e.Length)
        {
            int o = B65.IndexOf(e[p++]);
            int i = p < e.Length ? B65.IndexOf(e[p++]) : -1;
            int l = p < e.Length ? B65.IndexOf(e[p++]) : -1;
            int n = p < e.Length ? B65.IndexOf(e[p++]) : -1;

            if (o < 0 || i < 0 || l < 0 || n < 0)
                break;

            o = (o << 2) | (i >> 4);
            i = ((15 & i) << 4) | (l >> 2);
            int r = ((3 & l) << 6) | n;

            outb.Append((char)(o & 0xFF));
            if (l != 64)
                outb.Append((char)(i & 0xFF));
            if (n != 64)
                outb.Append((char)(r & 0xFF));
        }

        string raw = outb.ToString().Trim();
        if (raw.Length == 0)
            return null;
        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return raw;
        if (raw.StartsWith("/"))
            return SiteHost + raw;
        return SiteHost + "/" + raw;
    }

    // Nhan dien chat luong tu `format` cua videofile
    //   _hq.mp4 -> 720p   _tr.mp4 -> 216p
    public static string Quality(string format)
    {
        string f = (format ?? "").ToLowerInvariant();
        if (f.Contains("_hq")) return "720p";
        if (f.Contains("_tr")) return "216p";
        if (f.Contains("_sd")) return "480p";
        if (f.Contains("_ld")) return "360p";
        return "MP4";
    }

    // CÔNG THỨC MENU SISI (skill lampac-adult-module muc 9g):
    //   dong 1 : Tim kiem  (search_on, duoc phep o root)
    //   dong 2 : Sap xep   (submenu)
    //   dong 3+: taxonomy  (The loai)
    // Moi muc dieu huong PHAI co submenu — root khong submenu thi bam
    // vao chi mo lai menu (chet im, muc 9c).
    public static List<MenuItem> Menu(
        string host,
        IReadOnlyList<(string name, string path)> cats = null)
    {
        host = H(host);

        // QUERY — app phan trang bang `?pg=N` (skill 9d), khong dung duong dan.
        var root = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/vjav"
            }
        };

        var sortSub = new List<MenuItem>();
        foreach (var s in Sorts)
            sortSub.Add(new MenuItem(s.name, host + "/vjav?sort=" + s.sort));
        root.Add(new MenuItem()
        {
            title = "Sắp xếp",
            playlist_url = "submenu",
            submenu = sortSub
        });

        if (cats != null && cats.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var c in cats)
                sub.Add(new MenuItem(c.name,
                    host + "/vjav?c=" + HttpUtility.UrlEncode(c.path)));
            root.Add(new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = sub
            });
        }

        return root;
    }
}
