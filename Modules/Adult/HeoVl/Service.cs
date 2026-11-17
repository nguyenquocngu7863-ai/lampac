using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace HeoVl;

public static class HeoVlTo
{
    public static string SiteHost = "https://heovl.im";

    static string Slugify(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = Slugify(search);
            if (string.IsNullOrEmpty(slug))
                slug = HttpUtility.UrlEncode(search);
            string url = host + "/search/" + slug;
            if (pg > 1)
                url += "?page=" + pg;
            return url;
        }

        if (!string.IsNullOrEmpty(c))
        {
            string url = host + "/" + c.Trim('/');
            if (pg > 1)
                url += "?page=" + pg;
            return url;
        }

        if (pg > 1)
            return host + "/categories/viet-nam?page=" + pg;

        return host + "/";
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html, @"<a\s+href=""(https://heovl\.im/videos/[^""]+)""\s+title=""([^""]+)""", RegexOptions.Singleline))
        {
            string href = m.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            int start = m.Index;
            int len = Math.Min(3000, html.Length - start);
            if (len > 0)
            {
                string chunk = html.Substring(start, len);
                var img = Regex.Match(chunk, @"<img\s+src=""(https://heovl\.im/resize/[^""]+|https://heovl\.im/[^""]+\.(?:jpg|jpeg|png|webp)[^""]*)""");
                if (!img.Success)
                    img = Regex.Match(chunk, @"<img\s+[^>]*src=""([^""]+)""");
                if (img.Success)
                    poster = img.Groups[1].Value;
            }

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "heovl",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    public static List<(string label, string embed)> GetEmbeds(string html)
    {
        var list = new List<(string label, string embed)>();
        foreach (Match m in Regex.Matches(html, @"data-cdn-name=""([^""]*)""[^>]*data-source=""([^""]+)""", RegexOptions.Singleline))
        {
            string label = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
            string embed = m.Groups[2].Value.Replace("&amp;", "&");
            if (string.IsNullOrEmpty(embed))
                continue;
            if (string.IsNullOrEmpty(label))
                label = "Server " + (list.Count + 1);
            list.Add((label, embed));
        }

        if (list.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, @"data-source=""(https://[^""]+/videos/[^""]+/play[^""]*)""", RegexOptions.Singleline))
            {
                string embed = m.Groups[1].Value.Replace("&amp;", "&");
                list.Add(("Server " + (list.Count + 1), embed));
            }
        }

        return list;
    }

    public static (string embedHost, string vid) ParseEmbed(string embed)
    {
        try
        {
            var u = new Uri(embed);
            var m = Regex.Match(u.AbsolutePath, @"/videos/([^/]+)/play");
            if (!m.Success)
                return (null, null);
            return (u.Scheme + "://" + u.Host, m.Groups[1].Value);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Chuẩn hoá URL lấy ra từ JSON của player.
    /// <para>
    /// `JsonSerializer.Serialize` mặc định escape ký tự HTML nhạy cảm, nên `&`
    /// thành `\u0026`. Regex `"file": "(https?...)"` lấy đúng chuỗi đã escape,
    /// chỉ `Replace("\\/", "/")` thì URL còn sót dạng
    /// `master.m3u8?d=heovl.im\u0026e=...` — CDN đọc sai hết tham số truy vấn
    /// và trả 401, app báo link chết.
    /// </para>
    /// </summary>
    public static string NormalizeStreamUrl(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var url = value.Replace("\\/", "/");
        return Regex.Replace(url, @"\\u([0-9a-fA-F]{4})",
            m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
    }

    public static Dictionary<string, string> StreamLinksFromConfig(string configJson)
    {
        var links = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(configJson))
            return links;

        foreach (Match m in Regex.Matches(configJson, @"""file""\s*:\s*""(https?[^""]+)"""))
        {
            string file = NormalizeStreamUrl(m.Groups[1].Value);

            // Xet duoi duong dan, khong xet Contains: mot so URL analytics cua
            // JW Player mang `.m3u8` trong query string, Contains se nhan nham
            // va dua pixel 1x1 vao danh sach link.
            bool isMedia;
            try
            {
                var path = new Uri(file).AbsolutePath;
                isMedia = path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
            }
            catch { isMedia = false; }

            if (!isMedia)
                continue;

            if (links.ContainsValue(file))
                continue;

            // So chay tu 1 cho het, khong bo qua link dau tien. Truoc day
            // `links.Count == 0 ? ""` bo trong so o link 1, nen app hien
            // "StreamQQ Plan VIP HLS" roi den "... HLS 2" den "... HLS 7" -
            // nhan ra dau moi la so 1. So sanh bo truoc `Continue` nen khong
            // bao gio bi skip va khong bao gio trung key.
            string label = file.Contains(".m3u8") ? "HLS" : "MP4";
            links.TryAdd(label + " " + (links.Count + 1), file);
        }

        if (links.Count == 0)
        {
            var m = Regex.Match(configJson, @"(https?[^""\\\s]+\.m3u8[^""\\\s]*)");
            if (m.Success)
                links.TryAdd("HLS 1", m.Groups[1].Value.Replace("\\/", "/"));
        }

        return links;
    }

    /// <summary>
    /// Chuan hoa uri detail cua popup: chi nhan trang /videos/ thuoc heovl.im.
    /// uri rac hoac host la -> null (popup tra rong, khong fetch ra ngoai site).
    /// </summary>
    public static string PageUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        uri = uri.Trim();
        string abs = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? uri
            : SiteHost + "/" + uri.TrimStart('/');

        try
        {
            var u = new System.Uri(abs);
            if (!u.Host.Equals("heovl.im", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!u.AbsolutePath.StartsWith("/videos/", StringComparison.OrdinalIgnoreCase))
                return null;
            return SiteHost + u.AbsolutePath.TrimEnd('/');
        }
        catch
        {
            return null;
        }
    }

    static bool HasCjk(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        foreach (char c in s)
        {
            if (c >= 0x3040 && c <= 0x30ff) return true; // Hiragana/Katakana
            if (c >= 0x3400 && c <= 0x9fff) return true; // CJK
            if (c >= 0xac00 && c <= 0xd7af) return true; // Hangul
            if (c >= 0x0400 && c <= 0x04ff) return true; // Cyrillic
            if (c >= 0x0e00 && c <= 0x0e7f) return true; // Thai
        }
        return false;
    }

    /// <summary>
    /// Chip cua popup: the loai lay tu breadcrumb cua trang detail, the lay tu
    /// khoi "Tags" (chi ton tai khi video co tag). Ca hai deu la ten site in.
    /// </summary>
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var chips = new List<(string kind, string slug, string name)>();
        if (string.IsNullOrEmpty(html))
            return chips;

        var seen = new HashSet<string>();

        // The loai: breadcrumb (dung 1 the loai / video).
        foreach (Match m in Regex.Matches(html, @"<a href=""https://heovl\.im/categories/([^""]+)"" class=""breadcrumb__item__link"">\s*(.+?)\s*</a>", RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (slug.Length == 0 || name.Length == 0 || HasCjk(name))
                continue;
            if (!seen.Add("cat:" + slug))
                continue;
            chips.Add(("cat", slug, name));
        }

        // The: khoi "Tags" chi chua tag cua chinh video nay (khac featured-list
        // "The loai" o sidebar, do la danh sach the loai toan site).
        int start = html.IndexOf("<!-- Tags -->", StringComparison.Ordinal);
        if (start >= 0)
        {
            int end = html.IndexOf("<!-- Body -->", start, StringComparison.Ordinal);
            string region = end > start
                ? html.Substring(start, end - start)
                : html.Substring(start);

            foreach (Match m in Regex.Matches(region, @"<a href=""https://heovl\.im/tag/([^""]+)"" class=""featured-list__desktop__list__item__body"">.*?<h3 class=""featured-list__desktop__list__item__title"">\s*(.+?)\s*</h3>", RegexOptions.Singleline))
            {
                string slug = m.Groups[1].Value.Trim();
                string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
                if (slug.Length == 0 || name.Length == 0 || HasCjk(name))
                    continue;
                if (!seen.Add("tag:" + slug))
                    continue;
                chips.Add(("tag", slug, name));
            }
        }

        return chips;
    }

    /// <summary>
    /// "Xem them" cua site = GET /ajax/suggestions/&lt;slug&gt; tra JSON.
    /// </summary>
    public static List<Shared.Models.SISI.Base.PlaylistItem> Suggestions(string json, string vidosikUrl)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrWhiteSpace(json))
            return playlists;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var arr))
                return playlists;

            var seen = new HashSet<string>();
            foreach (var e in arr.EnumerateArray())
            {
                string href = e.TryGetProperty("url", out var u) ? u.GetString() : null;
                string name = e.TryGetProperty("title", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(name))
                    continue;

                if (!System.Uri.TryCreate(href, System.UriKind.Absolute, out var pu)
                    || !pu.Host.Equals("heovl.im", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.Add(href))
                    continue;

                string poster = e.TryGetProperty("thumbnail_file_url", out var th)
                    ? th.GetString() : "";

                playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
                {
                    video = vidosikUrl + "?uri=" + HttpUtility.UrlEncode(href),
                    name = name.Trim(),
                    picture = poster,
                    json = true,
                    bookmark = new Shared.Models.SISI.Base.Bookmark()
                    {
                        site = "heovl",
                        href = href,
                        image = poster
                    }
                });
            }
        }
        catch
        {
            return playlists;
        }

        return playlists;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/heovl"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/heovl"),
                    new("Việt Nam", host + "/heovl?c=categories/viet-nam"),
                    new("Vietsub", host + "/heovl?c=categories/vietsub"),
                    new("JAV HD", host + "/heovl?c=categories/jav-hd"),
                    new("Không Che", host + "/heovl?c=categories/khong-che"),
                    new("Trung Quốc", host + "/heovl?c=categories/trung-quoc"),
                    new("Âu - Mỹ", host + "/heovl?c=categories/au-my"),
                    new("Gái Xinh", host + "/heovl?c=categories/gai-xinh"),
                    new("Nghiệp Dư", host + "/heovl?c=categories/nghiep-du"),
                    new("Tự Quay", host + "/heovl?c=categories/tu-quay"),
                    new("Doggy", host + "/heovl?c=categories/doggy"),
                    new("Xnxx", host + "/heovl?c=categories/xnxx"),
                }
            }
        };
    }
}
