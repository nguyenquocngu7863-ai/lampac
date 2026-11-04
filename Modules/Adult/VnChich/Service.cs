using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Web;

namespace VnChich;

public static class VnChichTo
{
    public const string SiteHost = "https://vn.vnchich.vip";

    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36";

    public const int PageSize = 12;

    // ===== SORT (đo 2026-11-04) =====
    // API PeerTube: /api/v1/videos?sort=...
    // Đã đo từng giá trị ở home / danh mục / search -> tất cả đều trả 200.
    // ("-relevance" bị 400 -> không dùng)
    public static readonly (string name, string sort)[] Sorts =
    {
        ("Mới nhất", "-publishedAt"),
        ("Xem nhiều nhất", "-views"),
        ("Thích nhiều nhất", "-likes"),
        ("Dài nhất", "-duration"),
        ("Thịnh hành", "-trending"),
        ("Hot", "-hot"),
        ("Nhiều bình luận", "-comments"),
    };

    // ===== DANH MỤC (thẻ phổ biến của site) =====
    // Lọc bằng tagsOneOf, tự lùi về search nếu thẻ không tồn tại (Controller).
    public static readonly (string name, string slug)[] Categories =
    {
        ("Sinh viên", "sinh viên"),
        ("Học sinh", "học sinh"),
        ("Ngoại tình", "ngoại tình"),
        ("Vụng trộm", "vụng trộm"),
        ("Máy bay", "máy bay"),
        ("Gái gọi", "gái gọi"),
        ("Massage", "massage"),
        ("Cosplay", "cosplay"),
        ("Thủ dâm", "thủ dâm"),
        ("Xuất tinh", "xuất tinh"),
        ("Bạo dâm", "bạo dâm"),
        ("Lesbian", "lesbian"),
        ("Hậu môn", "hậu môn"),
        ("Vú to", "vú to"),
        ("Mông to", "mông to"),
        ("Quay lén", "quay lén"),
        ("Việt Nam", "việt nam"),
    };

    #region SORT
    public static string ClampSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
            return "-publishedAt";

        sort = sort.Trim().ToLowerInvariant();
        foreach (var (_, s) in Sorts)
            if (s == sort)
                return s;

        return "-publishedAt";
    }

    public static string SortLabel(string sort)
    {
        string s = ClampSort(sort);
        foreach (var (name, ss) in Sorts)
            if (ss == s)
                return name.ToLowerInvariant();

        return "mới nhất";
    }
    #endregion

    #region URL
    // c = "tag/<thẻ>" | "cat/<danh mục>"
    // asSearch = true -> lọc bằng search (dùng khi tagsOneOf không có kết quả)
    // Lưu ý (đo 2026-11-04): channelOneOf / accountOneOf bị API BỎ QUA (total luôn
    // bằng total toàn site) -> chỉ dùng tagsOneOf / search / categoryOneOf.
    public static string ApiUri(string host, string search, string c, int pg, string sort, bool asSearch = false)
    {
        host = string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');
        if (pg < 1)
            pg = 1;

        var q = new List<string>()
        {
            "start=" + ((pg - 1) * PageSize),
            "count=" + PageSize,
            "sort=" + ClampSort(sort)
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            q.Add("search=" + System.Uri.EscapeDataString(search.Trim()));
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string val = c.Trim('/');

            int slash = val.IndexOf('/');
            if (slash > 0)
                val = val.Substring(slash + 1);

            if (!string.IsNullOrWhiteSpace(val))
                q.Add((asSearch ? "search" : "tagsOneOf") + "=" + System.Uri.EscapeDataString(val.Trim()));
        }

        return host + "/api/v1/videos?" + string.Join("&", q);
    }

    public static string DetailUri(string host, string uuid)
    {
        host = string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');
        return host + "/api/v1/videos/" + uuid;
    }

    // Thẻ dùng để lấy "video liên quan": ưu tiên thẻ nằm trong danh mục
    // (nhóm lớn, liên quan thật), không có thì lấy thẻ đầu tiên.
    public static string RelatedTag(List<string> tags)
    {
        if (tags == null || tags.Count == 0)
            return null;

        foreach (var t in tags)
            foreach (var (_, slug) in Categories)
                if (string.Equals(t, slug, StringComparison.OrdinalIgnoreCase))
                    return t;

        return tags[0];
    }
    #endregion

    #region LIST
    // {"total": 19413, "data": [ {...video...} ]}
    public static List<PlaylistItem> Playlist(string json, string host, string exclude = null)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrWhiteSpace(json))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(json.Trim('\uFEFF', ' ', '\t', '\r', '\n'));
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var el in data.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;

                string uuid = GetStr(el, "uuid");
                if (string.IsNullOrEmpty(uuid))
                    continue;
                if (!string.IsNullOrEmpty(exclude) && uuid == exclude)
                    continue;

                string name = GetStr(el, "name");
                if (string.IsNullOrEmpty(name))
                    name = "VnChich";

                int duration = GetInt(el, "duration");
                string poster = Poster(el, host);

                list.Add(new PlaylistItem()
                {
                    name = name,
                    video = "vnchich/vidosik?uri=" + uuid,
                    picture = poster,
                    time = duration > 0 ? $"{duration / 60:D2}:{duration % 60:D2}" : null,
                    json = true,
                    bookmark = new Bookmark()
                    {
                        site = "vnchich",
                        href = uuid,
                        image = poster
                    }
                });
            }
        }
        catch { }

        return list;
    }

    public static int TotalPages(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(json.Trim('\uFEFF', ' ', '\t', '\r', '\n'));
            if (doc.RootElement.TryGetProperty("total", out var total)
                && total.ValueKind == JsonValueKind.Number
                && total.TryGetInt32(out int n))
                return (int)Math.Ceiling(n / (double)PageSize);
        }
        catch { }

        return 0;
    }
    #endregion

    #region DETAIL
    public class VideoDetail
    {
        public string Uuid { get; set; } = "";
        public string Name { get; set; } = "";
        public int Duration { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public string Channel { get; set; } = "";
        public string Account { get; set; } = "";
        public List<(string label, string url)> Streams { get; set; } = new List<(string, string)>();
    }

    // Video local  -> files[] = mp4 theo resolution (bỏ "Audio only")
    // Video remote -> không có mp4, chỉ có streamingPlaylists[] = HLS master
    public static VideoDetail Detail(string json)
    {
        var det = new VideoDetail();
        if (string.IsNullOrWhiteSpace(json))
            return det;

        try
        {
            using var doc = JsonDocument.Parse(json.Trim('\uFEFF', ' ', '\t', '\r', '\n'));
            var el = doc.RootElement;
            if (el.ValueKind != JsonValueKind.Object)
                return det;

            det.Uuid = GetStr(el, "uuid");
            det.Name = GetStr(el, "name");
            det.Duration = GetInt(el, "duration");

            if (el.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tags.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.String)
                        continue;

                    string v = (t.GetString() ?? "").Trim();
                    if (v.Length == 0)
                        continue;
                    if (!det.Tags.Any(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)))
                        det.Tags.Add(v);
                }
            }

            if (el.TryGetProperty("channel", out var channel) && channel.ValueKind == JsonValueKind.Object)
                det.Channel = GetStr(channel, "name");

            if (el.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
                det.Account = GetStr(account, "name");

            var files = new List<(int res, string label, string url)>();
            if (el.TryGetProperty("files", out var fl) && fl.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fl.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object)
                        continue;

                    string url = GetStr(f, "fileUrl");
                    if (string.IsNullOrEmpty(url))
                        continue;

                    int res = 0;
                    string label = "";
                    if (f.TryGetProperty("resolution", out var r) && r.ValueKind == JsonValueKind.Object)
                    {
                        res = GetInt(r, "id");
                        label = GetStr(r, "label");
                    }

                    // resolution id 0 = "Audio only" -> không dùng làm nguồn phát
                    if (res <= 0 || (label ?? "").IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    if (string.IsNullOrEmpty(label))
                        label = res + "p";

                    files.Add((res, label, url));
                }
            }

            foreach (var f in files.OrderByDescending(x => x.res))
                det.Streams.Add((f.label, f.url));

            if (el.TryGetProperty("streamingPlaylists", out var pls) && pls.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pls.EnumerateArray())
                {
                    if (p.ValueKind != JsonValueKind.Object)
                        continue;

                    string url = GetStr(p, "playlistUrl");
                    if (string.IsNullOrEmpty(url))
                        continue;
                    if (det.Streams.Any(x => x.url == url))
                        continue;

                    det.Streams.Add(("HLS", url));
                }
            }
        }
        catch { }

        return det;
    }
    #endregion

    #region MENU
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = host.TrimEnd('/');
        string root = host + "/vnchich";

        string Link(string s)
        {
            string q = "";
            if (!string.IsNullOrWhiteSpace(search))
                q = "search=" + HttpUtility.UrlEncode(search);
            else if (!string.IsNullOrWhiteSpace(c))
                q = "c=" + HttpUtility.UrlEncode(c);

            if (!string.IsNullOrEmpty(s))
                q += (q.Length == 0 ? "" : "&") + "sort=" + s;

            return q.Length == 0 ? root : root + "?" + q;
        }

        var res = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = root
            }
        };

        var sub = new List<MenuItem>();
        foreach (var (name, s) in Sorts)
            sub.Add(new MenuItem(name, Link(s)));

        res.Add(new MenuItem()
        {
            title = $"Sắp xếp: {SortLabel(sort)}",
            playlist_url = "submenu",
            submenu = sub
        });

        return res;
    }

    public static List<MenuItem> Menu(string host)
    {
        host = host.TrimEnd('/');

        var sub = new List<MenuItem>();
        foreach (var (name, slug) in Categories)
            sub.Add(new MenuItem(name, host + "/vnchich?c=cat/" + HttpUtility.UrlEncode(slug)));

        if (sub.Count == 0)
            return new List<MenuItem>();

        return new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = sub
            }
        };
    }
    #endregion

    #region JSON helpers
    static string GetStr(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    static int GetInt(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt32(out int i) ? i : 0;
        return 0;
    }

    static string Poster(JsonElement el, string host)
    {
        string best = "";
        int bestArea = 0;

        if (el.TryGetProperty("thumbnails", out var ths) && ths.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in ths.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object)
                    continue;

                string url = GetStr(t, "fileUrl");
                if (string.IsNullOrEmpty(url))
                    continue;

                int area = GetInt(t, "width") * GetInt(t, "height");
                if (area > bestArea)
                {
                    bestArea = area;
                    best = url;
                }
            }
        }

        if (!string.IsNullOrEmpty(best))
            return best;

        string path = GetStr(el, "thumbnailPath");
        if (!string.IsNullOrEmpty(path))
            return (string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/')) + "/" + path.TrimStart('/');

        return "";
    }
    #endregion
}
