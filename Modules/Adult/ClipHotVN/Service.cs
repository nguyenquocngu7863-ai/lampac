using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;

namespace ClipHotVN;

public static class ClipHotVNTo
{
    public static readonly string SiteHost = "https://cliphotvn.forum";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
    public const string EmbHost = "https://emb.cd-vs.com";

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = SearchSlug(search);
            if (string.IsNullOrEmpty(slug)) return host + "/";
            var url = host + "/search/" + slug + "/";
            if (pg > 1) url += "page/" + pg + "/";
            return url;
        }
        if (!string.IsNullOrWhiteSpace(c))
        {
            string url = c.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? c.TrimEnd('/') : host + "/" + c.Trim('/');
            if (pg > 1) url += "/page/" + pg + "/";
            return url.EndsWith("/") ? url : url + "/";
        }
        if (pg > 1) return host + "/page/" + pg + "/";
        return host + "/";
    }

    public static string SearchSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var words = value.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("+", Array.ConvertAll(words, System.Uri.EscapeDataString));
    }

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim());
        int frag = value.IndexOf('#'); if (frag >= 0) value = value.Substring(0, frag);
        int qry = value.IndexOf('?'); if (qry >= 0) value = value.Substring(0, qry);
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = SiteHost + "/" + value;
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        if (!parsed.Host.Equals("cliphotvn.forum", StringComparison.OrdinalIgnoreCase) &&
            !parsed.Host.EndsWith(".cliphotvn.forum", StringComparison.OrdinalIgnoreCase))
            return null;
        return value;
    }

    public static string NormalizeMediaUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim()).Replace("\\/", "/");
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        return value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? value : null;
    }

    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrWhiteSpace(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocks = Regex.Matches(html, @"<article[^>]*class=[""'].*?loop-video.*?[""'][^>]*>.*?</article>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match m in blocks)
        {
            var hrefM = Regex.Match(m.Value, @"href=[""']([^""']+\.html)[""']", RegexOptions.IgnoreCase);
            if (!hrefM.Success) continue;
            string href = NormalizePageUrl(hrefM.Groups[1].Value);
            if (string.IsNullOrEmpty(href) || !seen.Add(href)) continue;
            var titleM = Regex.Match(m.Value, @"title=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            string name = titleM.Success ? WebUtility.HtmlDecode(titleM.Groups[1].Value).Trim() : "";
            var imgM = Regex.Match(m.Value, @"src=[""']([^""']+)(?:\?\d+)?[""']", RegexOptions.IgnoreCase);
            string pic = NormalizeMediaUrl(imgM.Success ? imgM.Groups[1].Value : null);
            list.Add(new PlaylistItem
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = pic,
                json = true,
                bookmark = new Bookmark { site = "cliphotvn", href = href, image = pic }
            });
        }
        return list;
    }

    public static List<(string name, string path)> NavCats(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(html)) return res;
        int idx = html.IndexOf("id=\"menu-home\"", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return res;
        string slice = html.Substring(idx, Math.Min(8000, html.Length - idx));
        foreach (Match m in Regex.Matches(slice, @"<li[^>]*class=[""'][^""']*menu-item[^""']*[""'][^>]*>\s*<a[^>]*href=[""']([^""']+)[""'][^>]*>([^<]*)</a>", RegexOptions.IgnoreCase))
        {
            string href = m.Groups[1].Value;
            string name = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (href.IndexOf("lienhe", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (href.IndexOf("cliphotvn.org.uk", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (href.IndexOf("xxvn.top", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            string path = href.StartsWith(SiteHost, StringComparison.OrdinalIgnoreCase) ? href.Substring(SiteHost.Length).Trim('/') : href.Trim('/');
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(name)) res.Add((name, path));
        }
        return res;
    }

    public static List<(string name, string path)> TagList(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href=[""'](?:https?://cliphotvn\.forum)?/tag/([^""'/]+)/?[""'][^>]*>([^<]*)</a>", RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(slug) || !seen.Add(slug)) continue;
            string name = WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(name)) name = slug;
            res.Add((name, "tag/" + slug));
        }
        return res;
    }

    public static List<(string movie, string type, string label)> VideoServers(string html)
    {
        var ret = new List<(string, string, string)>();
        if (string.IsNullOrWhiteSpace(html)) return ret;
        foreach (Match m in Regex.Matches(html, @"source=['""](https?://[^'""]+)['""]", RegexOptions.IgnoreCase))
        {
            string url = m.Groups[1].Value.Trim();
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (url.Contains("qooglevideo") || url.Contains("gun1.lat") || url.Contains("/stream/") || url.Contains("/x/"))
            {
                if (!ret.Any(x => x.Item1 == url)) ret.Add((url, "0", $"Server {ret.Count + 1}"));
            }
        }
        return ret;
    }

    public static string PlayerEndpoint(string type) => SiteHost + "/get.video.php";

    public static string EmbedUuid(string html)
    {
        var m = Regex.Match(html, @"emb\.cd-vs\.com/embed/([0-9a-fA-F-]{30,})", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string EmbVideoUrl(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("url", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.String)
                return u.GetString();
        }
        catch { }
        return null;
    }

    public static string BloggerToken(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        int at = url.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        string token = url.Substring(at + 6);
        int amp = token.IndexOf('&');
        if (amp >= 0) token = token.Substring(0, amp);
        return string.IsNullOrEmpty(token) ? null : token;
    }

    public static bool IsMedia(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        string u = url.ToLowerInvariant();
        if (u.Contains("gethls.php")) return true;
        string base_ = u.Split('?')[0].Split('#')[0];
        if (base_.EndsWith(".m3u8") || base_.EndsWith(".m3u") || base_.EndsWith(".mp4") || base_.EndsWith(".m4v") || base_.EndsWith(".webm"))
            return true;
        return u.Contains(".m3u8") || u.Contains(".mp4");
    }

    public static bool IsHls(string url) => IsMedia(url) && url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);

    public static List<(string file, string label)> JwPlayerSources(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(html)) return res;
        foreach (Match m in Regex.Matches(html, @"sources:\s*\[(.*?)\]", RegexOptions.Singleline))
        {
            foreach (Match s in Regex.Matches(m.Groups[1].Value, @"""file""\s*:\s*""([^""]+)"""))
            {
                string file = s.Groups[1].Value.Replace("\\/", "/");
                res.Add((file, ""));
            }
        }
        return res;
    }

    public static List<(int itag, string url)> BloggerLinks(string text)
    {
        var res = new List<(int, string)>();
        if (string.IsNullOrWhiteSpace(text)) return res;
        try
        {
            int s = text.IndexOf('[');
            if (s < 0) return res;
            using var root = System.Text.Json.JsonDocument.Parse(text.Substring(s));
            var inner = root.RootElement[0][2].GetString();
            using var innerDoc = System.Text.Json.JsonDocument.Parse(inner);
            foreach (var row in innerDoc.RootElement[2].EnumerateArray())
            {
                string url = row[0].GetString();
                int it = row[1][0].GetInt32();
                if (!string.IsNullOrEmpty(url)) res.Add((it, url));
            }
        }
        catch { }
        return res;
    }

    static readonly (string name, string path)[] CatsFallback = new[]
    {
        ("Sex Việt Nam", "sex-viet-nam"),
        ("Sex Trung Quốc", "sex-trung-quoc"),
        ("Sex Nhật Bản", "sex-nhat-ban"),
        ("Sex Mỹ", "sex-my"),
        ("Sex Vietsub", "sex-vietsub"),
        ("Clip Phốt", "clip-phot"),
        ("Hot Live", "hot-live"),
        ("YY Live", "yy-live"),
        ("MM Live", "mm-live"),
        ("Bigo Live", "bigo-live")
    };

    static readonly (string name, string path)[] TagsFallback = new[]
    {
        ("clip-hot (1200)", "tag/clip-hot"),
        ("xxvn (800)", "tag/xxvn"),
        ("hot-live (600)", "tag/hot-live")
    };

    public static List<MenuItem> Menu(string host, List<(string name, string path)> cats, List<(string name, string path)> tags)
    {
        string cat(string path) => host + "/cliphotvn?c=" + HttpUtility.UrlEncode(path);
        var root = new List<MenuItem>
        {
            new MenuItem { title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/cliphotvn" },
            new MenuItem
            {
                title = "Sắp xếp",
                playlist_url = "submenu",
                submenu = new List<MenuItem>
                {
                    new MenuItem("Mới nhất", host + "/cliphotvn"),
                    new MenuItem("Xem nhiều", cat("xem-nhieu")),
                    new MenuItem("Yêu thích", cat("yeu-thich"))
                }
            }
        };
        var catSub = new List<MenuItem>();
        if (cats != null && cats.Count > 0)
            foreach (var (n, p) in cats) catSub.Add(new MenuItem(n, cat(p)));
        else
            foreach (var (n, p) in CatsFallback) catSub.Add(new MenuItem(n, cat(p)));
        root.Add(new MenuItem { title = "Thể loại", playlist_url = "submenu", submenu = catSub });
        var tagSub = new List<MenuItem>();
        if (tags != null && tags.Count > 0)
            foreach (var (n, p) in tags) tagSub.Add(new MenuItem(n, cat(p)));
        else
            foreach (var (n, p) in TagsFallback) tagSub.Add(new MenuItem(n, cat(p)));
        root.Add(new MenuItem { title = "Từ khoá", playlist_url = "submenu", submenu = tagSub });
        return root;
    }
}
