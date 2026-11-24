using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace Viet69z;

public static class Viet69To
{
    public static string Uri(string host, string search, string sort, string c, string t, int pg)
    {
        var url = new System.Text.StringBuilder();
        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("?s=");
            url.Append(HttpUtility.UrlEncode(search));
            if (pg > 1)
            {
                url.Append("&paged=");
                url.Append(pg);
            }
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
        else if (pg > 1)
        {
            url.Append("page/");
            url.Append(pg);
            url.Append("/");
        }

        return url.ToString();
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html, System.Func<Shared.Models.SISI.Base.PlaylistItem, Shared.Models.SISI.Base.PlaylistItem> onplaylist = null)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();

        var blocks = Regex.Matches(html, @"<article[^>]*class=""[^""]*entry-video[^""]*""[^>]*>.*?</article>", RegexOptions.Singleline);
        foreach (Match block in blocks)
        {
            string b = block.Value;
            var aMatch = Regex.Match(b, @"<a\s+href=""(https?://[^""]+)""[^>]*title=""([^""]+)""");
            if (!aMatch.Success)
                aMatch = Regex.Match(b, @"<a\s+[^>]*href=""(https?://[^""]+)""");
            if (!aMatch.Success) continue;

            string href = aMatch.Groups[1].Value;
            string name = aMatch.Groups.Count > 2 ? HttpUtility.HtmlDecode(aMatch.Groups[2].Value) : "";
            if (string.IsNullOrEmpty(name))
            {
                var h2 = Regex.Match(b, @"<h2[^>]*>.*?>([^<>]+)</a>");
                if (h2.Success)
                    name = HttpUtility.HtmlDecode(h2.Groups[1].Value.Trim());
            }
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            var imgMatch = Regex.Match(b, @"<img[^>]*src=""(https?://[^""]+)""");
            if (imgMatch.Success)
                poster = imgMatch.Groups[1].Value;

            var pl = new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = $"{uri}?uri={HttpUtility.UrlEncode(href)}",
                name = name,
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "viet69z",
                    href = href,
                    image = poster
                }
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }

    public static List<string> GetServerUuids(string html)
    {
        var list = new List<string>();
        foreach (Match m in Regex.Matches(html, @"data-url=""([A-Za-z0-9+/=]+)"""))
        {
            string b64 = m.Groups[1].Value;
            int pad = b64.Length % 4;
            if (pad > 0)
                b64 += new string('=', 4 - pad);
            try
            {
                string uuid = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Trim();
                if (!string.IsNullOrEmpty(uuid) && !list.Contains(uuid))
                    list.Add(uuid);
            }
            catch { }
        }
        return list;
    }

    public static Dictionary<string, string> StreamLinks(string json)
    {
        var links = new Dictionary<string, string>();
        var m = Regex.Match(json, @"""url""\s*:\s*""([^""]+)""");
        if (m.Success)
        {
            string file = m.Groups[1].Value.Replace("\\/", "/");
            if (file.StartsWith("http"))
                links.TryAdd("Auto", file);
        }
        return links;
    }

    public static string Slugify(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        s = s.ToLowerInvariant().Trim();
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s.Normalize(System.Text.NormalizationForm.FormD))
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            if (ch == 'đ')
                sb.Append('d');
            else if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                sb.Append(ch);
            else if (ch == ' ' || ch == '-' || ch == '_')
                sb.Append('-');
        }
        string o = sb.ToString();
        while (o.Contains("--"))
            o = o.Replace("--", "-");
        return o.Trim('-');
    }


    public const string SiteHost = "https://viet69z.to";

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = HttpUtility.HtmlDecode(value.Trim());
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
            value = SiteHost + "/" + value.Trim('/') + "/";
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        string h = parsed.Host.ToLowerInvariant();
        if (h != "viet69z.to" && !h.EndsWith(".viet69z.to")) return null;
        return value;
    }

    static string Pretty(string slug)
    {
        slug = (slug ?? "").Replace('-', ' ').Replace('_', ' ').Trim();
        if (slug.Length == 0) return slug;
        var words = slug.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
        return string.Join(" ", words);
    }

    static string AnchorText(string html, string href)
    {
        var m = Regex.Match(html, $@"<a[^>]*href=""{Regex.Escape(href)}""[^>]*>([^<]{{1,60}})</a>");
        if (m.Success)
        {
            string t = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
            if (t.Length > 0) return t;
        }
        return null;
    }

    public static List<(string name, string path)> Chips(string html)
    {
        var list = new List<(string name, string path)>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>();

        // The loai: class "category-<slug>" tren <article> cua bai -> permalink /<slug>/
        var artClass = Regex.Match(html, @"<article[^>]*class=""([^""]*\bhentry\b[^""]*)""");
        string cls = artClass.Success ? artClass.Groups[1].Value : "";
        foreach (Match m in Regex.Matches(cls, @"(?:^|\s)category-([a-z0-9][a-z0-9\-]*)"))
        {
            string slug = m.Groups[1].Value;
            if (slug == "uncategorized") continue;
            if (!seen.Add("c:" + slug)) continue;
            string name = AnchorText(html, SiteHost + "/" + slug + "/");
            list.Add((string.IsNullOrEmpty(name) ? Pretty(slug) : name, slug));
        }

        // The (tag): href /tag/<slug>/
        foreach (Match m in Regex.Matches(html, @"href=""https?://viet69z\.to/tag/([a-z0-9\-]+)/?"""))
        {
            string slug = m.Groups[1].Value;
            if (!seen.Add("t:" + slug)) continue;
            list.Add((Pretty(slug), "tag/" + slug));
        }

        return list;
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Related(string uri, string html, string pageUrl)
    {
        if (string.IsNullOrEmpty(html)) return new List<Shared.Models.SISI.Base.PlaylistItem>();

        int idx = html.IndexOf("Có thể bạn sẽ thích", StringComparison.Ordinal);
        if (idx < 0) idx = 0;

        var list = Playlist(uri, html.Substring(idx));
        if (list.Count == 0) return list;

        string self = (pageUrl ?? "").TrimEnd('/');
        list.RemoveAll(x => (x.bookmark?.href ?? "").TrimEnd('/') == self);
        return list;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = $"{host}/viet69z"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Sinh viên", $"{host}/viet69z?c=tag/sinh-vien"),
                    new("Máy bay", $"{host}/viet69z?c=tag/may-bay-ba-gia"),
                    new("Teen", $"{host}/viet69z?c=tag/teen"),
                    new("Check hàng", $"{host}/viet69z?c=tag/check-hang"),
                    new("Thủ dâm", $"{host}/viet69z?c=tag/thu-dam"),
                }
            }
        };
    }
}
