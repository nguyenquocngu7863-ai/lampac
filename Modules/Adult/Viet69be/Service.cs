using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Viet69be;

public static class Viet69beTo
{
    public static readonly string SiteHost = "https://viet69.be";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
    public const string EmbHost = "https://emb.cd-vs.com";

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host))
            host = SiteHost;

        host = host.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = SearchSlug(search);
            if (string.IsNullOrEmpty(slug))
                return host + "/";
            string url = host + "/search/" + slug + "/";
            if (pg > 1)
                url += "page/" + pg + "/";
            return url;
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string url = c.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? c.TrimEnd('/')
                : host + "/" + c.Trim('/');
            if (pg > 1)
                url += "/page/" + pg + "/";
            return url.EndsWith("/") ? url : url + "/";
        }

        if (pg > 1)
            return host + "/page/" + pg + "/";

        return host + "/";
    }

    // Canonical search cua site: /search/bu+cu/ (tu noi bang `+`).
    public static string SearchSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var words = value.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("+", words.Select(w => System.Uri.EscapeDataString(w)));
    }

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim());
        int fragment = value.IndexOf('#');
        if (fragment >= 0)
            value = value.Substring(0, fragment);

        int query = value.IndexOf('?');
        if (query >= 0)
            value = value.Substring(0, query);

        if (value.StartsWith("//"))
            value = "https:" + value;
        else if (value.StartsWith("/"))
            value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            value = SiteHost + "/" + value;

        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed))
            return null;

        if (!parsed.Host.Equals("viet69.be", StringComparison.OrdinalIgnoreCase) &&
            !parsed.Host.EndsWith(".viet69.be", StringComparison.OrdinalIgnoreCase))
            return null;

        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//"))
            value = "https:" + value;
        else if (value.StartsWith("/"))
            value = SiteHost + value;

        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    // Tile: <a class="clip-link" ... title="..." href="..."><img src="...">
    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<PlaylistItem>();
        if (string.IsNullOrWhiteSpace(html))
            return playlists;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocks = Regex.Matches(html,
            @"<a\b[^>]*\bclass\s*=\s*[""']clip-link[""'][^>]*>.*?</a\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match blockMatch in blocks)
        {
            string block = blockMatch.Value;
            var hrefMatch = Regex.Match(block, @"\bhref\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!hrefMatch.Success)
                continue;

            string href = NormalizePageUrl(hrefMatch.Groups[1].Value);
            if (string.IsNullOrEmpty(href) || !seen.Add(href))
                continue;

            var titleMatch = Regex.Match(block, @"\btitle\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            string name = titleMatch.Success
                ? WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[1].Value, "<[^>]+>", "").Trim())
                : "";
            if (string.IsNullOrEmpty(name))
                continue;

            playlists.Add(new PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = GetPoster(block),
                json = true,
                bookmark = new Bookmark()
                {
                    site = "viet69be",
                    href = href,
                    image = GetPoster(block)
                }
            });
        }

        return playlists;
    }

    static string GetPoster(string block)
    {
        Match match = Regex.Match(block, @"<img\b[^>]*\bdata-src\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(block, @"<img\b[^>]*\bdata-original\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(block, @"<img\b[^>]*\bsrc\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);

        string poster = NormalizeMediaUrl(match.Success ? match.Groups[1].Value : null);
        if (!string.IsNullOrEmpty(poster) && poster.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        return poster;
    }

    // Nav chinh: ul#menu-home > li.menu-item > a[title][href]. Bo muc lien he.
    public static List<(string name, string path)> NavCats(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        int start = html.IndexOf("id=\"menu-home\"", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return res;

        string box = html.Substring(start, Math.Min(html.Length - start, 6000));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(box,
            @"<li\b[^>]*\bclass\s*=\s*[""'][^""']*\bmenu-item\b[^""']*[""'][^>]*>\s*<a\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string tag = m.Value;
            var hrefMatch = Regex.Match(tag, @"\bhref\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!hrefMatch.Success)
                continue;

            string href = WebUtility.HtmlDecode(hrefMatch.Groups[1].Value.Trim());
            if (href.IndexOf("lienhe", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            string path = href.StartsWith(SiteHost, StringComparison.OrdinalIgnoreCase)
                ? href.Substring(SiteHost.Length).Trim('/')
                : href.Trim('/');
            if (string.IsNullOrEmpty(path) || !seen.Add(path))
                continue;

            var titleMatch = Regex.Match(tag, @"\btitle\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            string name = titleMatch.Success
                ? WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[1].Value, "<[^>]+>", "").Trim())
                : "";
            if (string.IsNullOrEmpty(name))
                continue;

            res.Add((name.Replace(':', '-'), path));
        }

        return res;
    }

    // Tag cloud: <a href="/tag/slug/" class="tag-link-N" title="N topics">name</a>.
    public static List<(string name, string path)> TagList(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            @"<a\b[^>]*\bhref\s*=\s*[""'](?:https?://viet69\.be)?/tag/([^""'/]+)/?[""'][^>]*\btitle\s*=\s*[""']([\d,]+)\s+topics?[""'][^>]*>(.*?)</a\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug))
                continue;

            string name = WebUtility.HtmlDecode(Regex.Replace(m.Groups[3].Value, "<[^>]+>", "").Trim());
            if (string.IsNullOrEmpty(name))
                name = slug;

            if (!int.TryParse(m.Groups[2].Value.Replace(",", ""), out int count))
                count = 0;

            res.Add(($"{name} ({count})", "tag/" + slug));
        }

        return res;
    }

    // Player: div.movieLoader[data-movie][data-type] (mac dinh) + nut
    // button.video2-btn[data-video][data-type] (Server #N). data-video la
    // base64 UUID; type=10 -> get.xvideo.php, con lai get.video.php.
    public static List<(string movie, string type, string label)> VideoServers(string html)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string movie, string type, string label)
        {
            if (string.IsNullOrEmpty(movie) || string.IsNullOrEmpty(type))
                return;
            if (!seen.Add(movie + "|" + type))
                return;
            res.Add((movie.Trim(), type.Trim(), string.IsNullOrEmpty(label) ? "Server " + (res.Count + 1) : label.Trim()));
        }

        var def = Regex.Match(html,
            @"<div\b[^>]*\bclass\s*=\s*[""']movieLoader[""'][^>]*>",
            RegexOptions.IgnoreCase);
        if (def.Success)
        {
            string movie = Attr(def.Value, "data-movie");
            Add(movie, Attr(def.Value, "data-type"), "Server 1");
        }

        foreach (Match m in Regex.Matches(html,
            @"<button\b[^>]*\bclass\s*=\s*[""']video2-btn[""'][^>]*>(.*?)</button\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string label = WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "").Trim());
            Add(Attr(m.Value, "data-video"), Attr(m.Value, "data-type"), label);
        }

        return res;
    }

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, @"\b" + name + @"\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : "";
    }

    public static string PlayerEndpoint(string type)
        => type == "10" ? SiteHost + "/get.xvideo.php" : SiteHost + "/get.video.php";

    // Tra ve uuid trong iframe emb.cd-vs.com/embed/<uuid>.
    public static string EmbedUuid(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var m = Regex.Match(html, @"emb\.cd-vs\.com/embed/([0-9a-fA-F-]{30,})", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // JSON /api/get-video: {"url": "https://www.blogger.com/video.g?token=..."}.
    public static string EmbVideoUrl(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("url", out var u) &&
                u.ValueKind == JsonValueKind.String)
                return u.GetString();
        }
        catch { }
        return null;
    }

    public static string BloggerToken(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;
        int at = url.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return null;
        string token = url.Substring(at + 6);
        int amp = token.IndexOf('&');
        if (amp >= 0)
            token = token.Substring(0, amp);
        return string.IsNullOrEmpty(token) ? null : token;
    }

    // Parse response batchexecute -> [(itag, url)]. itag 22 = 720p, 18 = 360p.
    public static List<(int itag, string url)> BloggerLinks(string text)
    {
        var res = new List<(int itag, string url)>();
        if (string.IsNullOrEmpty(text))
            return res;
        try
        {
            int s = text.IndexOf('[');
            if (s < 0)
                return res;
            using var doc = JsonDocument.Parse(text.Substring(s));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return res;
            var inner = root[0][2].GetString();
            using var doc2 = JsonDocument.Parse(inner);
            foreach (var row in doc2.RootElement[2].EnumerateArray())
            {
                string url = row[0].GetString();
                int itag = row[1][0].GetInt32();
                if (!string.IsNullOrEmpty(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    res.Add((itag, url));
            }
        }
        catch { }
        return res;
    }

    public static string ItagLabel(int itag)
        => itag == 22 ? "720p" : itag == 18 ? "360p" : "itag" + itag;

    public static bool IsMedia(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;
        string u = url.Split('?')[0].Split('#')[0].ToLowerInvariant();
        return u.EndsWith(".m3u8") || u.EndsWith(".m3u") || u.EndsWith(".mp4") ||
            u.EndsWith(".m4v") || u.EndsWith(".webm");
    }

    public static bool IsHls(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;
        string u = url.Split('?')[0].Split('#')[0].ToLowerInvariant();
        return u.EndsWith(".m3u8") || u.EndsWith(".m3u");
    }

    static readonly (string name, string c)[] CatsFallback =
    {
        ("Phim sex Việt mới", "sex-viet"),
        ("Video của thành viên", "bai-viet"),
        ("Phim sex sinh viên", "sinh-vien"),
        ("Teen", "teen"),
        ("Check hàng", "check-hang"),
        ("Camera", "camera"),
        ("BDSM", "bdsm"),
        ("Máy bay bà già", "may-bay-ba-gia"),
        ("Thủ dâm", "thu-dam"),
    };

    static readonly (string name, string path)[] TagsFallback =
    {
        ("teen (11512)", "tag/teen"), ("sinh viên (7171)", "tag/sinh-vien"),
        ("khẩu dâm (7078)", "tag/khau-dam"), ("doggy (6272)", "tag/doggy"),
        ("da trắng (6186)", "tag/da-trang"), ("không bao (5531)", "tag/khong-bao"),
        ("BJ (5288)", "tag/bj"), ("blowjob (5101)", "tag/blowjob"),
        ("mông to (4634)", "tag/mong-to"), ("cưỡi ngựa (4610)", "tag/cuoi-ngua"),
        ("em dâm (4289)", "tag/em-dam"), ("bú cu (4262)", "tag/bu-cu"),
        ("vú to (3864)", "tag/vu-to"), ("sugar baby (3855)", "tag/sugar-baby"),
        ("sgbb (3657)", "tag/sgbb"), ("Clip sex (2802)", "tag/clip-sex"),
        ("bướm non (2769)", "tag/buom-non"), ("xuất tinh (2431)", "tag/xuat-tinh"),
        ("buom mup (1903)", "tag/buom-mup"), ("cực phẩm (1830)", "tag/cuc-pham"),
    };

    // Cong thuc 9g: Tim kiem + Sap xep + The loai + Tu khoa.
    public static List<MenuItem> Menu(string host,
        List<(string name, string path)> cats, List<(string name, string path)> tags)
    {
        string cat(string path) => host + "/viet69be?c=" + HttpUtility.UrlEncode(path);

        var root = new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/viet69be"
            },
            new MenuItem()
            {
                title = "Sắp xếp",
                playlist_url = "submenu",
                submenu = new List<MenuItem>()
                {
                    new("Mới nhất", host + "/viet69be"),
                    new("Xem nhiều", cat("xem-nhieu")),
                    new("Yêu thích", cat("yeu-thich")),
                    new("Bình luận", cat("binh-luan")),
                }
            },
        };

        var catSub = new List<MenuItem>();
        if (cats != null && cats.Count > 0)
        {
            foreach (var (name, path) in cats)
                catSub.Add(new(name, cat(path)));
        }
        else
        {
            foreach (var (name, c) in CatsFallback)
                catSub.Add(new(name, cat(c)));
        }
        root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = catSub });

        var tagSub = new List<MenuItem>();
        if (tags != null && tags.Count > 0)
        {
            foreach (var (name, path) in tags)
                tagSub.Add(new(name, cat(path)));
        }
        else
        {
            foreach (var (name, path) in TagsFallback)
                tagSub.Add(new(name, cat(path)));
        }
        root.Add(new MenuItem() { title = "Từ khoá", playlist_url = "submenu", submenu = tagSub });

        return root;
    }
}
