using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace JavHD;

public static class JavHDTo
{
    public static string SiteHost = "https://javhd.today";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    public static string Uri(string host, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;
        host = host.TrimEnd('/');

        string path = "recent/";
        if (!string.IsNullOrEmpty(c))
        {
            c = c.Trim('/');
            path = c.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? c : c;
            if (!path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                path = path.Trim('/') + "/";
        }

        string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path.TrimEnd('/') + "/" : host + "/" + path;
        return url + "?ajax=browse_videos&page=" + Math.Max(1, pg);
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string json)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(json))
            return playlists;

        string html = json;
        try
        {
            var m = Regex.Match(json, "\"html\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (m.Success)
                html = Regex.Unescape(m.Groups[1].Value);
        }
        catch { }

        var seen = new HashSet<string>();
        foreach (Match cm in Regex.Matches(html, "<li id=\"video-(\\d+)\">(.*?)</li>", RegexOptions.Singleline))
        {
            string b = cm.Groups[2].Value;
            if (b.Length > 3000)
                b = b.Substring(0, 3000);

            var hm = Regex.Match(b, "<a href=\"(/\\d+/[^\\\"]+/)\"[^>]*title=\"([^\"]{3,300})\"");
            if (!hm.Success)
                hm = Regex.Match(b, "<a href=\"(/\\d+/[^\\\"]+/)\"");
            if (!hm.Success)
                continue;
            string href = SiteHost + hm.Groups[1].Value;
            if (!seen.Add(href))
                continue;

            string name = hm.Groups.Count > 2 ? HttpUtility.HtmlDecode(hm.Groups[2].Value.Trim()) : ("Video " + cm.Groups[1].Value);
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var im = Regex.Match(b, "<img[^>]+src=\"(https?://[^\"]+)\"");
            if (im.Success)
                poster = im.Groups[1].Value;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "javhd",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // detail -> data-embed singles (base64) theo thu tu uu tien
    public static List<string> EmbedUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        var seen = new HashSet<string>();
        void Add(string u)
        {
            if (!string.IsNullOrEmpty(u) && u.StartsWith("http") && seen.Add(u))
            {
                if (u.Contains("cloudwish") || u.Contains("mycloudz"))
                    urls.Insert(0, u);
                else
                    urls.Add(u);
            }
        }

        foreach (Match m in Regex.Matches(html, "data-embed=\"([^\"]+)\""))
        {
            try { Add(Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value))); }
            catch { }
        }
        if (urls.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, "data-embeds=\"([^\"]+)\""))
            {
                try
                {
                    string arr = Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value));
                    foreach (Match u in Regex.Matches(arr, "\"(https?://[^\"\\\\]+)\""))
                        Add(u.Groups[1].Value.Replace("\\/", "/"));
                }
                catch { }
                if (urls.Count > 0)
                    break;
            }
        }

        return urls;
    }

    public static string TurboM3u8(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var m = Regex.Match(html, "data-hash=\"(https?://[^\"]+\\.m3u8[^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    // giai eval(p,a,c,k,e,d) kieu Dean Edwards (mycloudz/cloudwish) -> list m3u8
    static readonly Regex EvalRx = new(
        @"eval\(function\(p,a,c,k,e,d\)\{.*?\}\('((?:[^'\\]|\\.)*)',(\d+),(\d+),'((?:[^'\\]|\\.)*)'\.split\('\|'\)(?:,0,\{\})?\)",
        RegexOptions.Singleline);

    static string DecodePack(string p, int a, int c, string[] k)
    {
        string Unbase(int n)
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ+/";
            if (n < a)
                return chars[n].ToString();
            string s = "";
            while (n > 0)
            {
                s = chars[n % a] + s;
                n /= a;
            }
            return s;
        }
        while (c-- > 0)
        {
            string w = Unbase(c);
            if (!string.IsNullOrEmpty(w) && c < k.Length && !string.IsNullOrEmpty(k[c]))
                p = Regex.Replace(p, @"\b" + Regex.Escape(w) + @"\b", k[c]);
        }
        return p;
    }

    public static List<string> CloudHlsUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        string decoded = html;
        for (int i = 0; i < 3; i++)
        {
            var m = EvalRx.Match(decoded);
            if (!m.Success)
                break;
            try
            {
                string p = m.Groups[1].Value.Replace("\\'", "'");
                int a = int.Parse(m.Groups[2].Value);
                int c = int.Parse(m.Groups[3].Value);
                string[] k = m.Groups[4].Value.Split('|');
                string one = DecodePack(p, a, c, k);
                decoded = decoded.Substring(0, m.Index) + one + decoded.Substring(m.Index + m.Length);
            }
            catch { break; }
        }

        string flat = decoded.Replace("\\/", "/").Replace("\\u0026", "&");
        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(flat, "\"hls\\d*\"\\s*:\\s*\"(https?://[^\"]+?\\.m3u8[^\"]*?)\""))
        {
            string u = m.Groups[1].Value;
            if (seen.Add(u))
                urls.Add(u);
        }

        return urls;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        var cats = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Mới nhất", host + "/javhd?c=recent/"),
            new("Phổ biến hôm nay", host + "/javhd?c=popular/today/"),
            new("Phổ biến tuần", host + "/javhd?c=popular/week/"),
            new("Phổ biến tháng", host + "/javhd?c=popular/month/"),
            new("Ngày phát hành", host + "/javhd?c=releaseday/"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = host + "/javhd"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = cats
            }
        };
    }
}
