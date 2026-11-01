using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace PhimSexVN88;

public class DetailInfo
{
    public string videoId;
    public List<(string id, string name)> servers = new();
}

public static class PhimSexVN88To
{
    public static string SiteHost = "https://phimsexvn88.com";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrWhiteSpace(host)) host = SiteHost;
        host = host.TrimEnd('/');
        string baseUrl;
        if (!string.IsNullOrWhiteSpace(search))
            baseUrl = host + "/search/" + Slugify(search) + "/";
        else if (!string.IsNullOrWhiteSpace(c))
        {
            string raw = c.Trim().Trim('/');
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                baseUrl = raw.TrimEnd('/') + "/";
            else
                baseUrl = host + "/" + raw + "/";
        }
        else
            baseUrl = host + "/";
        if (pg > 1) baseUrl += "trang/" + pg + "/";
        return baseUrl;
    }

    public static string Slugify(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[àáạảãâầấậẩẫăằắặẳẵ]", "a");
        s = Regex.Replace(s, @"[èéẹẻẽêềếệểễ]", "e");
        s = Regex.Replace(s, @"[ìíịỉĩ]", "i");
        s = Regex.Replace(s, @"[òóọỏõôồốộổỗơờớợởỡ]", "o");
        s = Regex.Replace(s, @"[ùúụủũưừứựửữ]", "u");
        s = Regex.Replace(s, @"[ỳýỵỷỹ]", "y");
        s = s.Replace("đ", "d");
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"[^a-z0-9\-]", "");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 1;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"/trang/(\d+)"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        }
        return max;
    }

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = WebUtility.HtmlDecode(value.Trim());
        int frag = value.IndexOf('#');
        if (frag >= 0) value = value[..frag];
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) value = SiteHost + "/" + value.TrimStart('/');
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        if (!parsed.Host.EndsWith("phimsexvn88.com", StringComparison.OrdinalIgnoreCase)) return null;
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

    // ===== Playlist: <li class="item-movie"> =====
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<li\s+class=""item-movie""[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string block = m.Groups[1].Value;
            var am = Regex.Match(block, @"<a\b([^>]*)>", RegexOptions.IgnoreCase);
            if (!am.Success) continue;
            string attrs = am.Groups[1].Value;
            string href = Regex.Match(attrs, @"\bhref\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase).Groups[1].Value;
            string pageUrl = NormalizePageUrl(href);
            if (string.IsNullOrEmpty(pageUrl) || !seen.Add(pageUrl)) continue;
            string name = Clean(Regex.Match(attrs, @"\btitle\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase).Groups[1].Value);
            if (string.IsNullOrEmpty(name))
                name = Clean(Regex.Match(block, @"<div\s+class=""title-movie""[^>]*>(.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value);
            if (string.IsNullOrEmpty(name)) continue;
            string poster = null;
            var pm = Regex.Match(block, @"\bdata-original\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (pm.Success) poster = NormalizeMediaUrl(pm.Groups[1].Value);
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(pageUrl),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark() { site = "phimsexvn88", href = pageUrl, image = poster }
            });
        }
        return list;
    }

    // ===== Detail: data-video-id + servers =====
    public static string VideoId(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"data-video-id=""(\d+)""");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static List<(string id, string name)> Servers(string html)
    {
        var res = new List<(string id, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, @"<li\s+class=""video-server[^""]*""[^>]*data-server-id=""(\d+)""[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string id = m.Groups[1].Value;
            if (!seen.Add(id)) continue;
            string name = Clean(Regex.Replace(m.Groups[2].Value, "<[^>]+>", " "));
            if (string.IsNullOrEmpty(name)) name = "Server " + id;
            res.Add((id, name));
        }
        return res;
    }

    // ===== api/player JSON =====
    public static (string type, string url) ParsePlayer(string json)
    {
        if (string.IsNullOrEmpty(json)) return (null, null);
        var tm = Regex.Match(json, @"""type""\s*:\s*""([^""]+)""");
        string type = tm.Success ? tm.Groups[1].Value : null;
        if (string.Equals(type, "jwplayer", StringComparison.OrdinalIgnoreCase))
        {
            var fm = Regex.Match(json, @"""file""\s*:\s*""([^""]+)""");
            if (fm.Success) return (type, WebUtility.HtmlDecode(fm.Groups[1].Value).Replace("\\/", "/"));
        }
        else
        {
            var sm = Regex.Match(json, @"""source""\s*:\s*""([^""]*)""");
            if (sm.Success) return (type, WebUtility.HtmlDecode(sm.Groups[1].Value).Replace("\\/", "/"));
        }
        return (type, null);
    }

    public static string FindMedia(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"https?://[^""'\s]+\.(?:m3u8|mp4)[^""'\s]*", RegexOptions.IgnoreCase);
        return m.Success ? m.Value.Replace("\\/", "/") : null;
    }

    // ===== Menu =====
    public static List<MenuItem> MenuHead(string host, string search)
    {
        host = host.TrimEnd('/');
        string root = host + "/phimsexvn88";
        return new List<MenuItem>(1)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
    }

    public static List<MenuItem> Menu(string host, List<(string slug, string name)> hotTags)
    {
        host = host.TrimEnd('/');
        string root = host + "/phimsexvn88";
        string url(string c) => root + "?c=" + HttpUtility.UrlEncode(c);
        var rootList = new List<MenuItem>(2)
        {
            new MenuItem(){ title = "Danh sách", playlist_url = "submenu", submenu = new List<MenuItem>(){
                new("Mới nhất", root),
                new("Sex Việt", url("phim-sex-viet-nam")),
                new("Sex Nhật", url("phim-sex-nhat-ban")),
                new("Sex Trung", url("phim-sex-trung-quoc")),
                new("Sex Âu", url("phim-sex-chau-au")),
                new("Vietsub", url("phim-sex-vietsub")),
            }},
        };
        if (hotTags != null && hotTags.Count > 0)
        {
            if (hotTags.Count > 300)
            {
                int n = 0;
                for (int i = 0; i < hotTags.Count; i += 300)
                {
                    var chunk = hotTags.Skip(i).Take(300)
                        .Select(x => new MenuItem(x.name, url("tag/" + x.slug))).ToList();
                    n++;
                    rootList.Add(new MenuItem() { title = $"Tag hot ({n})", playlist_url = "submenu", submenu = chunk });
                }
            }
            else rootList.Add(new MenuItem()
            {
                title = "Tag hot",
                playlist_url = "submenu",
                submenu = hotTags.Select(x => new MenuItem(x.name, url("tag/" + x.slug))).ToList()
            });
        }
        return rootList;
    }

    public static List<(string slug, string name)> HotTags(string html)
    {
        var res = new List<(string slug, string name)>();
        if (string.IsNullOrEmpty(html)) return res;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*""/tag/([a-z0-9\-]+)/""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = Clean(m.Groups[1].Value);
            string name = Clean(Regex.Replace(HttpUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " "));
            if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name)) continue;
            if (name.Length > 40 || !seen.Add(slug)) continue;
            res.Add((slug, name));
        }
        return res;
    }
}
