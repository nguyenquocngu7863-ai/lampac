using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace SexVietop;

public static class SxvtTo
{
    public static string SiteHost = "https://sexvietop.com";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    // Site dung trong query `?d=` cua player.
    public static string Domain = "sexvietop.com";

    // ===== URL =====
    // SSR that, moi trang 24 phim, phan trang DUY NHAT: ?page=N
    // (da do: page > last -> site tra lai trang last, nen khong the vuot
    // qua tong; lay max page tu paginator).
    //
    // SORT — KHONG CO (do 2026-11-11, 4 lan do tren trang chu):
    //   ?sort=views  ?sort_by=views  ?order=views  ?sort=-id
    //   -> cung y 24 phim, cung thu tu => tham so bi BO QUA.
    //   Khong co UI sort trong HTML/JS (chỉ có sort mặc định theo id desc).
    //   Riêng /the-loai/<slug>?sort=views -> MOT TẬP khác (0 trùng, stable,
    //   paginator ?sort=views&page=N) => đó là FILTER (hot nhất) chứ không
    //   phải sort. Theo skill lampac-sort: site KHÔNG có sort thật -> BỎ HẲN
    //   dòng 2 menu; bộ lọc "hot" được đặt ở dòng 3.
    //
    //   /                      trang chu
    //   /the-loai/<slug>       35 thể loại (?page=categories)
    //   /tag/<slug>            2 từ khoá (xnxx, xvideos)
    //   /tim-kiem/<slug>       tìm kiếm (slug hoa hoa dấu; /tim-kiem?k= -> 404)
    //   /ajax/top/<1h|day|week>  bảng xếp hạng (JSON 1 trang)
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        if (pg < 1)
            pg = 1;

        string u, q = null;

        if (!string.IsNullOrWhiteSpace(search))
        {
            u = host + "/tim-kiem/" + Slug(search.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            // c co the mang filter "the-loai/x&sort=views"
            string p = c.Trim();
            int amp = p.IndexOf('&');
            if (amp > 0)
            {
                u = host + "/" + p.Substring(0, amp).Trim('/');
                q = p.Substring(amp + 1).TrimStart('?');
            }
            else
            {
                u = p.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? p
                    : host + "/" + p.Trim('/');
            }
        }
        else
        {
            u = host + "/";
        }

        if (pg > 1)
            q = (q == null ? "" : q + "&") + "page=" + pg;

        if (q != null)
            u += (u.Contains("?") ? "&" : "?") + q;

        return u;
    }

    // "Gái Xinh" -> "gai-xinh", "em mau" -> "em-mau" (site tự chuẩn hoá 2 kiểu
    // đều chạy, module chuẩn hoá luôn cho chắc).
    public static string Slug(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return "";

        string s = HttpUtility.HtmlDecode(q).Trim().Replace('Đ', 'd').Replace('đ', 'd').ToLowerInvariant();

        // Bang bo dau: moi chuỗi ký tự thay bằng ký tự đầu (không dấu)
        string[] keys =
        {
            "àáảãạăằắẳẵặâầấẩẫậ",
            "èéẻẽẹêềếểễệ",
            "ìíỉĩị",
            "òóỏõọôồốổỗộơờớởỡợ",
            "ùúủũụưừứửữự",
            "ỳýỷỹỵ"
        };

        foreach (string k in keys)
        {
            foreach (char ch in k)
                s = s.Replace(ch.ToString(), k[0].ToString());
        }

        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (ch == '-' || ch == ' ')
                sb.Append('-');
        }

        string r = sb.ToString().Trim('-');
        return Regex.Replace(r, "-{2,}", "-");
    }

    // ===== card SSR =====
    // <div class="item">
    //  <div class="item__thumbnail">
    //    <a href="https://sexvietop.com/phim-sex/<slug>" class="item__thumbnail_link" title="...">
    //      <img src=".../resize/800/..."> [ <div class="item__labels"><span class="item__label">Vú To</span>] </a>
    //  <div class="item__details"><a ... class="item__details__title">...</a>
    //    <span class="item__details__description">51 views</span>
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int start = 0;
        while (true)
        {
            int i = html.IndexOf("class=\"item\"", start, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                break;

            int j = html.IndexOf("class=\"item\"", i + 8, StringComparison.OrdinalIgnoreCase);
            string block = j < 0 ? html.Substring(i) : html.Substring(i, j - i);
            start = j < 0 ? html.Length : j;

            var mu = Regex.Match(block, "href=\"[^\"]*phim-sex/([a-z0-9-]+)\"", RegexOptions.IgnoreCase);
            if (!mu.Success)
                continue;

            string slug = mu.Groups[1].Value;
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            string name = "";
            var mt = Regex.Match(block, "title=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (mt.Success)
                name = Clean(HttpUtility.HtmlDecode(mt.Groups[1].Value));

            string poster = "";
            var mp = Regex.Match(block, "src\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (mp.Success)
                poster = Abs(mp.Groups[1].Value);

            string time = "";
            var ml = Regex.Match(block, "item__label[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            if (ml.Success)
                time = Clean(HttpUtility.HtmlDecode(ml.Groups[1].Value));

            // Card không có quality site -> để trống cho app tự tính.
            // video phải chứa URL ĐẦY ĐỦ trang detail (không chỉ slug) để
            // sisi.js regex /vidosik?uri=<uri> rồi gọi /genres?uri=<uri> hoạt
            // động (Genres yêu cầu uri có "/phim-sex/").
            string detail = SxvtTo.SiteHost.TrimEnd('/') + "/phim-sex/" + slug;
            list.Add(new PlaylistItem
            {
                name = name,
                video = route + "?uri=" + HttpUtility.UrlEncode(detail),
                picture = poster,
                time = time,
                bookmark = new Bookmark()
                {
                    site = "sexvietop",
                    href = slug,
                    image = poster
                },
                json = true
            });
        }

        return list;
    }

    // paginator: <a class="paginator__item__link" href="...?page=417">417</a>
    public static int MaxPage(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 0;

        int maxpage = 0;
        foreach (Match m in Regex.Matches(html, "class=\"paginator__item__link\"[^>]*href=\"[^\"]*[?&]page=(\\d+)", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(m.Groups[1].Value, out int pg) && pg > maxpage)
                maxpage = pg;
        }

        // Một page nhỏ (<5) thì có tới; lớn hơn để app tự gõ tiếp, site kẹp ở last.
        return maxpage <= 4 ? maxpage : 0;
    }

    // ===== /ajax/top/<1h|day|week> (JSON, 1 trang ~22-24 phim) =====
    // {"data":[{"id","title","slug","url","thumbnail_file_url","meta":{...,"duration"},"featured_labels":[]}]}
    public static List<PlaylistItem> Rankings(string route, string json)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(json))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in arr.EnumerateArray())
            {
                string slug = Str(item, "slug");
                if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                    continue;

                string name = Str(item, "title");
                string time = "";
                if (item.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                    && meta.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.String)
                {
                    if (int.TryParse(dur.GetString(), out int sec) && sec > 0)
                        time = (sec / 60) + ":" + (sec % 60).ToString("00");
                }

                string quality = null;
                if (item.TryGetProperty("featured_labels", out var lab) && lab.ValueKind == JsonValueKind.Array)
                {
                    foreach (var l in lab.EnumerateArray())
                    {
                        if (l.ValueKind == JsonValueKind.String)
                        {
                            quality = Clean(l.GetString());
                            break;
                        }
                    }
                }

                string poster = "";
                if (item.TryGetProperty("thumbnail_file_url", out var th) && th.ValueKind == JsonValueKind.String)
                    poster = Abs(th.GetString());

                list.Add(new PlaylistItem
                {
                    name = name,
                    video = route + "?uri=" + HttpUtility.UrlEncode(SxvtTo.SiteHost.TrimEnd('/') + "/phim-sex/" + slug),
                    picture = poster,
                    time = time,
                    quality = quality,
                    bookmark = new Bookmark()
                    {
                        site = "sexvietop",
                        href = slug,
                        image = poster
                    },
                    json = true
                });
            }
        }
        catch { }

        return list;
    }

    static string Str(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return Clean(HttpUtility.HtmlDecode(v.GetString()));
        return null;
    }

    // ===== /?page=categories =====
    // <div class="item"><a href="/the-loai/<slug>"><img></a>
    //   <a ... class="item__details__title">Tự Quay</a>
    //   <span class="item__details__description">4848 videos</span>
    public static List<(string name, string path, int count)> Categories(string html)
    {
        var list = new List<(string, string, int)>();
        if (string.IsNullOrEmpty(html))
            return list;

        int start = 0;
        while (true)
        {
            int i = html.IndexOf("class=\"item\"", start, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                break;

            int j = html.IndexOf("class=\"item\"", i + 8, StringComparison.OrdinalIgnoreCase);
            string block = j < 0 ? html.Substring(i) : html.Substring(i, j - i);
            start = j < 0 ? html.Length : j;

            var mh = Regex.Match(block, "href=\"[^\"]*?/((?:the-loai|tag)/[a-z0-9-]+)", RegexOptions.IgnoreCase);
            if (!mh.Success)
                continue;

            var mn = Regex.Match(block, "item__details__title[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            var mc = Regex.Match(block, "item__details__description[^0-9]*([0-9]+)", RegexOptions.IgnoreCase);

            list.Add((
                mn.Success ? Clean(HttpUtility.HtmlDecode(mn.Groups[1].Value)) : mh.Groups[1].Value,
                mh.Groups[1].Value,
                mc.Success ? int.Parse(mc.Groups[1].Value) : 0));
        }

        return list.OrderByDescending(x => x.Item3).ToList();
    }

    // Navbar/tag: <a href="/tag/xnxx" title="Xnxx">
    public static List<(string name, string path)> Tags(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        foreach (Match m in Regex.Matches(html, "href=\"[^\"]*?/tag/([a-z0-9-]+)\"[^>]*>\\s*([^<]{1,80})\\s*</a>", RegexOptions.IgnoreCase))
        {
            string name = Clean(HttpUtility.HtmlDecode(m.Groups[2].Value));
            string path = "tag/" + m.Groups[1].Value;
            if (string.IsNullOrEmpty(name) || list.Any(x => x.Item2.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;
            list.Add((name, path));
        }

        return list;
    }

    // ===== popup chips (trang chi tiet) =====
    // <a href=".../the-loai/<slug>" title="..." class="badge">
    //   <span class="badge__text">Tự Quay</span><span class="badge__label">4.9k</span>
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        foreach (Match m in Regex.Matches(html, "<a\\s+href=\"([^\"]+)\"[^>]*class=\"badge\"[^>]*>([\\s\\S]*?)</a>", RegexOptions.IgnoreCase))
        {
            var mh = Regex.Match(m.Groups[1].Value, "/(the-loai|tag)/([a-z0-9-]+)", RegexOptions.IgnoreCase);
            if (!mh.Success)
                continue;

            var mn = Regex.Match(m.Groups[2].Value, "badge__text[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            string name = mn.Success ? Clean(HttpUtility.HtmlDecode(mn.Groups[1].Value)) : mh.Groups[2].Value;

            string kind = mh.Groups[1].Value.ToLowerInvariant();
            if (list.Any(x => x.Item1 == kind && x.Item2.Equals(mh.Groups[2].Value, StringComparison.OrdinalIgnoreCase)))
                continue;

            list.Add((kind, mh.Groups[2].Value, name));
        }

        return list;
    }

    // ===== /ajax/suggestions/<slug> (JSON "phim liên quan", 24 phim) =====
    public static List<PlaylistItem> Related(string route, string json)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(json))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var item in arr.EnumerateArray())
            {
                string slug = Str(item, "slug");
                if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                    continue;

                string time = "";
                if (item.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                    && meta.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.String
                    && int.TryParse(dur.GetString(), out int sec) && sec > 0)
                    time = (sec / 60) + ":" + (sec % 60).ToString("00");

                string quality = null;
                if (item.TryGetProperty("featured_labels", out var lab) && lab.ValueKind == JsonValueKind.Array)
                {
                    foreach (var l in lab.EnumerateArray())
                    {
                        if (l.ValueKind == JsonValueKind.String)
                        {
                            quality = Clean(l.GetString());
                            break;
                        }
                    }
                }

                string poster = "";
                if (item.TryGetProperty("thumbnail_file_url", out var th) && th.ValueKind == JsonValueKind.String)
                    poster = Abs(th.GetString());

                list.Add(new PlaylistItem
                {
                    name = Str(item, "title"),
                    video = route + "?uri=" + HttpUtility.UrlEncode(SxvtTo.SiteHost.TrimEnd('/') + "/phim-sex/" + slug),
                    picture = poster,
                    time = time,
                    quality = quality,
                    bookmark = new Bookmark()
                    {
                        site = "sexvietop",
                        href = slug,
                        image = poster
                    },
                    json = true
                });
            }
        }
        catch { }

        return list;
    }

    // ===== player =====
    // 2 nút set-player-source:
    //   [1] "StreamQQ Plan VIP"    e.streamforester.name/videos/<id>/play?event_id=player-wrapper (302 -> wogplayer.top)
    //   [2] "StreamQQ Plan VIP Alias" vcast.name/videos/<id>/play?event_id=player-wrapper
    // (PlayHQ playheovl.xyz đã chết toàn cục, module hàng xóm SexVietDam cũng bỏ)
    public static List<(string name, string src)> Servers(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        foreach (Match m in Regex.Matches(html, "<button[^>]*set-player-source[^>]*>", RegexOptions.IgnoreCase))
        {
            string tag = m.Value;
            var ms = Regex.Match(tag, "data-source\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            var mn = Regex.Match(tag, "data-cdn-name\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (ms.Success)
                list.Add((mn.Success ? Clean(HttpUtility.HtmlDecode(mn.Groups[1].Value)) : "StreamQQ", ms.Groups[1].Value));
        }

        return list;
    }

    public static string VideoId(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var m = Regex.Match(url, "/videos/([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string FileUrl(string jsonOrHtml)
    {
        if (string.IsNullOrEmpty(jsonOrHtml))
            return null;

        var m = Regex.Match(jsonOrHtml, "\"file\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        string u = HttpUtility.UrlDecode(m.Groups[1].Value).Replace("\\/", "/");
        return u;
    }

    public static string Abs(string u)
    {
        if (string.IsNullOrEmpty(u))
            return u;

        u = HttpUtility.HtmlDecode(u).Trim();
        if (u.StartsWith("http://") || u.StartsWith("https://"))
            return u;

        if (u.StartsWith("//"))
            return "https:" + u;

        if (u.StartsWith("/"))
            return SiteHost.TrimEnd('/') + u;

        return SiteHost.TrimEnd('/') + "/" + u;
    }

    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";

        s = Regex.Replace(s, "\\s+", " ").Trim();
        return s.Length > 260 ? s.Substring(0, 260) : s;
    }

    // ===== menu 2 tầng =====
    public static List<MenuItem> Menu(string host, List<(string name, string path, int count)> cats, List<(string name, string path)> tags, string c)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        // LUẬT: sisi.js nap row cho MOI menu item (this.playlist loop) -> item
        // nào cũng có playlist_url thật thì màn hình chính lặp đúng list đó.
        // Nhóm chỉ có submenu phải dùng playlist_url = "submenu" (sentinel,
        // request fail -> row bi bo), xem QmhSex/Manko/MissAV.
        var menu = new List<MenuItem>
        {
            // Dòng 1 — tìm kiếm. search_on để bộ lọc có mục "Tìm"; row này
            // chính là danh sách mới nhất trên màn hình chính (convention repo).
            new()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = $"{host}/sexvietop"
            }
        };

        // Dòng 2: BỎ HẲN — site không có tham số sort thật (xem URI bên trên).

        // Dòng 3 — Danh sách: Mới nhất + Xem nhiều (chỉ khi đang mở 1 thể loại)
        var lists = new List<MenuItem>
        {
            new()
            {
                title = "Mới nhất",
                playlist_url = $"{host}/sexvietop"
            }
        };

        // Hot: đúng tập khác (filter) nên mới ở đây, KHÔNG phải sort
        string bare = c?.Split('&')[0];
        if (!string.IsNullOrWhiteSpace(bare) && bare.StartsWith("the-loai", StringComparison.OrdinalIgnoreCase))
        {
            lists.Add(new MenuItem
            {
                title = "Xem nhiều",
                playlist_url = $"{host}/sexvietop?c=" + HttpUtility.UrlEncode(bare + "&sort=views") + "&pg=0"
            });
        }

        menu.Add(new MenuItem
        {
            title = "Danh sách",
            playlist_url = "submenu",
            submenu = lists
        });

        menu.Add(new MenuItem
        {
            title = "Bảng xếp hạng",
            playlist_url = "submenu",
            submenu = new List<MenuItem>
            {
                new() {
                    title = "1 giờ qua",
                    playlist_url = $"{host}/sexvietop?rank=1h&pg=0"
                },
                new() {
                    title = "Hôm nay",
                    playlist_url = $"{host}/sexvietop?rank=day&pg=0"
                },
                new() {
                    title = "Tuần này",
                    playlist_url = $"{host}/sexvietop?rank=week&pg=0"
                }
            }
        });

        // Dòng 4 — taxonomy
        if (cats.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var (name, path, _) in cats)
            {
                sub.Add(new MenuItem
                {
                    title = name,
                    playlist_url = $"{host}/sexvietop?c=" + HttpUtility.UrlEncode(path) + "&pg=0"
                });
            }

            menu.Add(new MenuItem
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = sub
            });
        }

        if (tags.Count > 0)
        {
            var sub = new List<MenuItem>();
            foreach (var (name, path) in tags)
            {
                sub.Add(new MenuItem
                {
                    title = name,
                    playlist_url = $"{host}/sexvietop?c=" + HttpUtility.UrlEncode(path) + "&pg=0"
                });
            }

            menu.Add(new MenuItem
            {
                title = "Từ khoá",
                playlist_url = "submenu",
                submenu = sub
            });
        }

        return menu;
    }

    // Fallback khi /?page=categories fail: 35 thể loại (đo 2026-11-11).
    public static List<(string name, string path, int count)> FallbackCategories() => new()
    {
        ("Viet69", "the-loai/viet69", 6627),
        ("Tự Quay", "the-loai/tu-quay", 4848),
        ("Mông Đẹp", "the-loai/mong-dep", 2058),
        ("Doggy", "the-loai/doggy", 1953),
        ("Gái Xinh", "the-loai/gai-xinh", 1858),
        ("Bú Cu", "the-loai/bu-cu", 1662),
        ("Vú To", "the-loai/vu-to", 1644),
        ("Người Mẫu Đang Chuẩn", "the-loai/nguoi-mau-dang-chuan", 1499),
        ("Cowgirl - Cưỡi Ngựa", "the-loai/cowgirl-cuoi-ngua", 1232),
        ("Lồn Đẹp - Bím Hồng", "the-loai/lon-dep-bim-hong", 1091),
        ("Mông To", "the-loai/mong-to", 958),
        ("Tự Sướng", "the-loai/tu-suong", 829),
        ("Bú Lồn", "the-loai/bu-lon", 388),
        ("Ban Tình Đẩy Lồn", "the-loai/ban-tinh-day-lon", 328),
        ("Sextoy - Đồ Chơi Tình Dục", "the-loai/sextoy-do-choi-tinh-duc", 308),
        ("Tập Thể", "the-loai/tap-the", 249),
        ("Livestream", "the-loai/livestream", 171),
        ("Nghiệp Dư", "the-loai/nghiep-du", 168),
        ("Ngoại Tình", "the-loai/ngoai-tinh", 134),
        ("Vụng Trộm", "the-loai/vung-trom", 117),
        ("Học Sinh", "the-loai/hoc-sinh", 103),
        ("Bạo Dâm", "the-loai/bao-dam", 98),
        ("Hiếp Dâm", "the-loai/hiep-dam", 83),
        ("Tư Thế 69", "the-loai/tu-the-69", 79),
        ("Bắn Lên Mặt", "the-loai/ban-len-mat", 72),
        ("Chơi Lỗ Đít - Anal Sex", "the-loai/choi-lo-dit-anal-sex", 52),
        ("Quay Lén", "the-loai/quay-len", 51),
        ("Massage - Mát Xa", "the-loai/massage-mat-xa", 30),
        ("Phim Sex Hay", "the-loai/phim-sex-hay", 21),
        ("Loan Luân", "the-loai/loan-luan", 15),
        ("Bà Bầu", "the-loai/ba-bau", 11),
        ("Cô Giáo", "the-loai/co-giao", 8),
        ("Lesbian", "the-loai/lesbian", 7),
        ("Phim Cấp 3", "the-loai/phim-cap-3", 5),
        ("Y Tá", "the-loai/y-ta", 3)
    };

    public static List<(string name, string path)> FallbackTags() => new()
    {
        ("Xnxx", "tag/xnxx"),
        ("Xvideos", "tag/xvideos")
    };
}
