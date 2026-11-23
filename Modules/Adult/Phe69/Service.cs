using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace Phe69;

public static class Phe69To
{
    public static string Uri(string host, string search, string c, int pg)
    {
        var url = new System.Text.StringBuilder();
        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            if (pg > 1)
            {
                url.Append("page/");
                url.Append(pg);
                url.Append("/");
            }
            url.Append("?s=");
            url.Append(HttpUtility.UrlEncode(search));
        }
        else if (!string.IsNullOrEmpty(c))
        {
            url.Append(c.Trim('/'));
            url.Append("/");
            if (pg > 1)
            {
                url.Append("page/");
                url.Append(pg);
                url.Append("/");
            }
        }
        else
        {
            // Root `/` va `/page/N` tra ve challenge cho http client cua app (503),
            // trong khi category /phim-sex-moi/ chay binh thuong (do 2026-11-23).
            // Home = category "Phim sex moi" -> list + phan trang deu hoat dong.
            url.Append("phim-sex-moi/");
            if (pg > 1)
            {
                url.Append("page/");
                url.Append(pg);
                url.Append("/");
            }
        }

        return url.ToString();
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();

        var blocks = Regex.Matches(html, @"<article.*?</article>", RegexOptions.Singleline);
        foreach (Match block in blocks)
        {
            string b = block.Value;
            var aMatch = Regex.Match(b, @"<a\s+href=""([^""]+/)""[^>]*\btitle=""([^""]+)""");
            if (!aMatch.Success)
                continue;

            string href = aMatch.Groups[1].Value;
            string name = HttpUtility.HtmlDecode(aMatch.Groups[2].Value);

            string poster = "";
            var imgMatch = Regex.Match(b, @"<img[^>]+class=""[^""]*video-main-thumb[^""]*""[^>]+src=""([^""]+)""");
            if (!imgMatch.Success)
                imgMatch = Regex.Match(b, @"<img[^>]+src=""([^""]+)""");
            if (imgMatch.Success)
                poster = imgMatch.Groups[1].Value;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = $"{uri}?uri={HttpUtility.UrlEncode(href)}",
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "phe69",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // ========== Phan trang: /page/N/ (site khong co sort) ==========
    public static int Pages(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 1;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"/page/(\d+)/", RegexOptions.IgnoreCase))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        return max;
    }

    // ========== POPUP (long-press) ==========
    // Breadcrumb: <div class="breadcrumbs"><a href="...">Trang chủ</a> … <a href="<cat>">Tên</a>
    // Tag: <a href="https://phe69.shop/tag/<slug>/">Tên</a>  (~15 link + 15 meta article:tag)
    public static List<(string kind, string path, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrWhiteSpace(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Category — chi lay breadcrumb, KHONG lay nav (nav co toan bo category).
        int bc = html.IndexOf("breadcrumbs\"><a", StringComparison.OrdinalIgnoreCase);
        if (bc >= 0)
        {
            string seg = html.Substring(bc, Math.Min(600, html.Length - bc));
            foreach (Match m in Regex.Matches(seg,
                @"<a\s+href=""([^""]+)""[^>]*>([^<]+)</a>", RegexOptions.IgnoreCase))
            {
                string path = PathOf(m.Groups[1].Value);
                string name = HttpUtility.HtmlDecode(m.Groups[2].Value).Trim();
                if (string.IsNullOrEmpty(path) || path == "/"
                    || string.IsNullOrEmpty(name) || name == "Trang chủ")
                    continue;
                if (!seen.Add("cat/" + path)) continue;
                list.Add(("cat", path, name));
            }
        }

        // Tag
        foreach (Match m in Regex.Matches(html,
            @"<a[^>]*href=""([^""]*/tag/[^""]+/?)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string path = PathOf(m.Groups[1].Value);
            if (string.IsNullOrEmpty(path) || !path.Contains("/tag/", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = HttpUtility.HtmlDecode(
                Regex.Replace(m.Groups[2].Value, "<[^>]+>", " ")).Trim();
            if (string.IsNullOrEmpty(name) || !seen.Add("tag/" + path)) continue;

            list.Add(("tag", path, name));
        }

        return list;
    }

    public static string PathOf(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        string p = href.Trim();
        int scheme = p.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            int slash = p.IndexOf('/', scheme + 3);
            if (slash < 0) return null;
            p = p.Substring(slash);
        }
        if (!p.StartsWith("/")) return null;
        int cut = p.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) p = p.Substring(0, cut);
        p = p.TrimEnd('/');
        return p.Length > 1 ? p : null;
    }

    // URI do client gui vao: chi host site + khong phai root (chong SSRF).
    public static string SafeDetailUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        string url = uri.Trim();
        if (url.StartsWith("//")) url = "https:" + url;
        else if (url.StartsWith("/")) url = "https://phe69.shop" + url;
        else if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!System.Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != System.Uri.UriSchemeHttp && u.Scheme != System.Uri.UriSchemeHttps) return null;
        if (!u.Host.EndsWith("phe69.shop", StringComparison.OrdinalIgnoreCase)) return null;

        string path = u.AbsolutePath.Trim('/');
        if (path.Length < 4 || path.StartsWith("page/", StringComparison.OrdinalIgnoreCase))
            return null;

        return u.GetLeftPart(global::System.UriPartial.Path);
    }

    // "Video lien quan" nam truoc div.show-more-related. Khong cat -> se lan ca nav.
    public static List<Shared.Models.SISI.Base.PlaylistItem> Related(string route, string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return new List<Shared.Models.SISI.Base.PlaylistItem>();

        // Bat dau tu <div class="under-video-block"><h2>Video lien quan</h2>,
        // ket thuc o .show-more-related. Cat tu 0 se lan ca nav/category articles.
        int from = html.IndexOf("under-video-block", StringComparison.OrdinalIgnoreCase);
        int to = html.IndexOf("show-more-related", StringComparison.OrdinalIgnoreCase);
        if (from < 0 || to <= from) return new List<Shared.Models.SISI.Base.PlaylistItem>();

        return Playlist(route, html.Substring(from, to - from));
    }

    public static string GetIframe(string html)
    {
        var m = Regex.Match(html, @"<iframe\s+src=""(https://play\.phe69\.shop/data/[^""]+)""");
        return m.Success ? m.Groups[1].Value : null;
    }

    static string Base62(int n)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (n < 62)
            return chars[n].ToString();
        return Base62(n / 62) + chars[n % 62];
    }

    public static string UnpackDeanEdwards(string html)
    {
        var m = Regex.Match(html, @"\}\('(.*)',(\d+),(\d+),'(.*)'\.split\('\|'\)", RegexOptions.Singleline);
        if (!m.Success)
            return null;

        string p = m.Groups[1].Value;
        int a = int.Parse(m.Groups[2].Value);
        int c = int.Parse(m.Groups[3].Value);
        string[] k = m.Groups[4].Value.Split('|');
        if (a != 62 || k.Length < c)
            return null;

        for (int i = c - 1; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(k[i]))
                continue;
            string word = Base62(i);
            string repl = k[i];
            p = Regex.Replace(p, @"\b" + Regex.Escape(word) + @"\b", m2 => repl);
        }

        return p;
    }

    // Co chế MỚI (do 2026-11-23): trang /data/<token> khong con packer a=62,
    // gan thang `const hlsSource = "https://hlshd.phe69.shop/hls_videos/<slug>/index.m3u8"`.
    public static Dictionary<string, string> PlayerSources(string html)
    {
        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(html)) return links;

        var m = Regex.Match(html,
            @"hlsSource\s*=\s*""([^""]+\.m3u8[^""]*)""",
            RegexOptions.IgnoreCase);
        if (m.Success)
            links.TryAdd("Auto", m.Groups[1].Value.Replace("\\/", "/"));

        if (links.Count == 0)
        {
            var mp4 = Regex.Match(html, @"""([^""]+\.mp4[^""]*)""", RegexOptions.IgnoreCase);
            if (mp4.Success)
                links.TryAdd("Auto", mp4.Groups[1].Value.Replace("\\/", "/"));
        }

        return links;
    }

    public static Dictionary<string, string> StreamLinks(string unpacked)
    {
        var links = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(unpacked))
            return links;

        unpacked = unpacked.Replace("\\'", "'");
        foreach (Match m in Regex.Matches(unpacked, @"'label':'([^']+)','type':'[^']+','file':'(https?[^']+\.mp4)'"))
        {
            string file = m.Groups[2].Value.Replace("\\/", "/");
            links.TryAdd(m.Groups[1].Value, file);
        }

        if (links.Count == 0)
        {
            var m = Regex.Match(unpacked, @"'(https?[^']+\.mp4)'");
            if (m.Success)
                links.TryAdd("Auto", m.Groups[1].Value.Replace("\\/", "/"));
        }

        return links;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = $"{host}/phe69"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Phim sex mới", $"{host}/phe69?c=phim-sex-moi"),
                    new("Sinh viên", $"{host}/phe69?c=phim-sex-sinh-vien"),
                    new("Gái dâm", $"{host}/phe69?c=gai-dam"),
                    new("Check hàng", $"{host}/phe69?c=check-hang"),
                    new("Tự quay", $"{host}/phe69?c=tu-quay"),
                    new("Thủ dâm", $"{host}/phe69?c=thu-dam"),
                    new("Doggy", $"{host}/phe69?c=doggy"),
                    new("BDSM", $"{host}/phe69?c=bdsm"),
                    new("JAV HD", $"{host}/phe69?c=jav-hd"),
                    new("Không che", $"{host}/phe69?c=sex-khong-che"),
                    new("Máy bay", $"{host}/phe69?c=may-bay-ba-gia"),
                    new("Camera", $"{host}/phe69?c=camera"),
                }
            }
        };
    }
}
