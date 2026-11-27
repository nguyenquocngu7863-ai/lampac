using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Web;

namespace Av01;

public static class Av01To
{
    public const string SiteHost = "https://av01.tv";
    public const string GeoUrl = "https://files.iw01.xyz/edge/geo.js?json";
    public const string ImgHost = "https://img1.iw01.xyz";
    public const string Lang = "vn";
    public const int Limit = 48;

    public const string ChromeUA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public static string ListUrl(string sort, int pg)
    {
        sort = sort == "hottest" ? "hottest" : "latest";
        return $"{SiteHost}/api/v1/videos/types/{sort}?page={pg}&limit={Limit}";
    }

    public static string TagUrl(string id, int pg)
        => $"{SiteHost}/api/v1/videos/tag/{id}?page={pg}&limit={Limit}";

    public static string ActressUrl(string id, int pg)
        => $"{SiteHost}/api/v1/videos/actress/{id}?page={pg}&limit={Limit}";

    public static string MakerUrl(string id, int pg)
        => $"{SiteHost}/api/v1/videos/maker/{id}?page={pg}&limit={Limit}";

    public static string SimilarsUrl(string id, int pg)
        => $"{SiteHost}/api/v1/videos/{id}/similars?page={pg}&limit={Limit}";

    public static string DetailApiUrl(string id)
        => $"{SiteHost}/api/v1/videos/{id}";

    public static string PlaylistUrl(string id, string expires, string ip, string token)
        => $"{SiteHost}/api/v1/videos/{id}/playlist?expires={expires}&ip={ip}&token_v2={token}";

    public static string ManifestUrl(string id, string file, string accessToken)
        => $"{SiteHost}/api/v1/videos/{id}/manifest/{file}?access_token={access_token_escape(accessToken)}";

    static string access_token_escape(string t) => t ?? "";

    public static string CatalogUrl(string kind)
    {
        string res = kind switch
        {
            "actresses" => "actresses",
            "makers" => "makers",
            _ => "tags"
        };
        int limit = kind switch
        {
            "actresses" => 60,
            "makers" => 30,
            _ => 80
        };
        return $"{SiteHost}/api/v1/{res}/by-video-count?page=1&limit={limit}";
    }

    // ---- helpers ----

    public static string VideoId(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        var m = System.Text.RegularExpressions.Regex.Match(uri, @"/video/(\d+)");
        if (m.Success)
            return m.Groups[1].Value;

        // uri co the chi la id
        return uri.Trim().All(char.IsDigit) ? uri.Trim() : null;
    }

    public static string DetailUrl(string id, string dvd)
    {
        string slug = string.IsNullOrWhiteSpace(dvd) ? "" : "/" + dvd.ToLowerInvariant();
        return $"{SiteHost}/{Lang}/video/{id}{slug}";
    }

    public static string Poster(string id) => $"{ImgHost}/media/videos/tmb/{id}/1.jpg";

    public static string Tr(JsonElement e)
    {
        if (e.TryGetProperty("name_translations", out var nt) && nt.ValueKind == JsonValueKind.Object)
        {
            foreach (string k in new[] { "vn", "en", "cn" })
                if (nt.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                    return v.GetString();
        }
        if (e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
            return n.GetString();
        return "";
    }

    public static string Title(JsonElement v)
    {
        if (v.TryGetProperty("title_translations", out var tt) && tt.ValueKind == JsonValueKind.Object)
        {
            foreach (string k in new[] { "vn", "en", "cn" })
                if (tt.TryGetProperty(k, out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                    return t.GetString();
        }
        if (v.TryGetProperty("title", out var x) && x.ValueKind == JsonValueKind.String)
            return x.GetString();
        return "";
    }

    static string Str(JsonElement v, string key)
    {
        if (v.TryGetProperty(key, out var x) && x.ValueKind == JsonValueKind.String)
            return x.GetString();
        return null;
    }

    static string Num(JsonElement v, string key)
    {
        if (!v.TryGetProperty(key, out var x))
            return null;
        if (x.ValueKind == JsonValueKind.Number)
            return x.GetRawText();
        if (x.ValueKind == JsonValueKind.String)
            return x.GetString();
        return null;
    }

    static string Duration(JsonElement v)
    {
        string d = Num(v, "duration");
        if (d == null || !int.TryParse(d, out int sec) || sec <= 0)
            return null;
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static List<PlaylistItem> Playlist(string json, string route)
    {
        var playlists = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(json))
            return playlists;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("videos", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return playlists;

            var seen = new HashSet<string>();

            foreach (var v in arr.EnumerateArray())
            {
                string id = Num(v, "id");
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    continue;

                string name = Title(v);
                if (string.IsNullOrEmpty(name))
                    continue;

                string href = DetailUrl(id, Str(v, "dvd_id"));

                playlists.Add(new PlaylistItem
                {
                    video = route + "?uri=" + HttpUtility.UrlEncode(href),
                    name = HttpUtility.HtmlDecode(name),
                    picture = Poster(id),
                    time = Duration(v),
                    quality = Uncen(v),
                    json = true,
                    bookmark = new Bookmark
                    {
                        site = "av01",
                        href = href,
                        image = Poster(id)
                    }
                });
            }
        }
        catch { }

        return playlists;
    }

    static string Uncen(JsonElement v)
    {
        string s = Str(v, "censorship_status");
        return s == "decensored" ? "UNCENSORED" : null;
    }

    public static int Pages(string json)
    {
        if (string.IsNullOrEmpty(json))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("pagination", out var p)
                && p.TryGetProperty("totalPages", out var tp))
            {
                if (tp.ValueKind == JsonValueKind.Number) return tp.GetInt32();
                if (tp.ValueKind == JsonValueKind.String && int.TryParse(tp.GetString(), out int n)) return n;
            }
        }
        catch { }

        return 0;
    }

    public static List<(string id, string name, string count)> Catalog(string json, string kind)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(json))
            return res;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(kind, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return res;

            foreach (var e in arr.EnumerateArray())
            {
                string id = Num(e, "id");
                string name = Tr(e);
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                    continue;
                res.Add((id, name, Num(e, "videos_count")));
            }
        }
        catch { }

        return res;
    }

    // Detail API -> chips (the loai / dien vien / nha san xuat) de lam popup.
    public static List<(string kind, string id, string name)> Chips(string json)
    {
        var chips = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(json))
            return chips;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("video", out var v) && v.ValueKind == JsonValueKind.Object)
                root = v;

            void add(string prop, string kind)
            {
                if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return;
                foreach (var e in arr.EnumerateArray())
                {
                    string id = Num(e, "id");
                    string name = Tr(e);
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        chips.Add((kind, id, name));
                }
            }

            add("tags", "tag");
            add("actresses", "actress");

            string makerId = Num(root, "maker_id");
            string maker = Str(root, "maker_translations") ?? Tr(root);
            if (!string.IsNullOrEmpty(makerId) && makerId != "0")
            {
                string mname = maker;
                if (root.TryGetProperty("maker_translations", out var mt) && mt.ValueKind == JsonValueKind.Object)
                {
                    foreach (string k in new[] { "vn", "en", "cn" })
                        if (mt.TryGetProperty(k, out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                        {
                            mname = t.GetString();
                            break;
                        }
                }
                if (string.IsNullOrEmpty(mname) && root.TryGetProperty("maker", out var mk) && mk.ValueKind == JsonValueKind.String)
                    mname = mk.GetString();
                if (!string.IsNullOrEmpty(mname))
                    chips.Add(("maker", makerId, mname));
            }
        }
        catch { }

        return chips;
    }

    // ---- menu ----

    public static string SortLabel(string sort) => sort == "hottest" ? "Xem nhiều" : "Mới nhất";

    public static string ClampSort(string sort)
        => sort == "hottest" ? "hottest" : "latest";

    public static List<MenuItem> Menu(
        string host,
        string sort,
        string c,
        List<(string id, string name)> tags = null,
        List<(string id, string name)> actresses = null,
        List<(string id, string name)> makers = null)
    {
        host = host.TrimEnd('/');
        string root = host + "/av01";

        string link(string cval, string s)
        {
            var q = new List<string>();
            if (!string.IsNullOrEmpty(cval)) q.Add("c=" + HttpUtility.UrlEncode(cval));
            if (!string.IsNullOrEmpty(s)) q.Add("sort=" + s);
            return q.Count == 0 ? root : root + "?" + string.Join("&", q);
        }

        var menu = new List<MenuItem>();

        // Sort that su: latest (moi dang) / hottest (xem nhieu)
        menu.Add(new MenuItem()
        {
            title = "Sắp xếp: " + SortLabel(sort),
            playlist_url = "submenu",
            submenu = new List<MenuItem>()
            {
                new("Mới nhất", link(c, "latest")),
                new("Xem nhiều", link(c, "hottest"))
            }
        });

        if (tags != null && tags.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = tags.Select(t => new MenuItem(t.name, link("tag:" + t.id, sort))).ToList()
            });
        }

        if (actresses != null && actresses.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Diễn viên",
                playlist_url = "submenu",
                submenu = actresses.Select(a => new MenuItem(a.name, link("actress:" + a.id, sort))).ToList()
            });
        }

        if (makers != null && makers.Count > 0)
        {
            menu.Add(new MenuItem()
            {
                title = "Nhà sản xuất",
                playlist_url = "submenu",
                submenu = makers.Select(m => new MenuItem(m.name, link("maker:" + m.id, sort))).ToList()
            });
        }

        return menu;
    }
}
