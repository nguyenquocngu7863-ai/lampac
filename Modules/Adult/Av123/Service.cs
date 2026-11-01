using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Shared.Models.SISI.Base;

namespace Av123;

// rec2.123av.com — khu "You may like" cua site (POST co chu ky)
public static class Av123Rec
{
    public const string Base = "https://rec2.123av.com";
    public const string Key = "e8bcc533-bc38-4a33-af0e-678d38237754";

    public static string Sign(string timestamp, string ua)
    {
        unchecked
        {
            int hash = 0;
            string input = Key + timestamp + ua;
            foreach (char c in input)
                hash = (hash << 5) - hash + c;
            return ((uint)hash).ToString("x");
        }
    }

    public static string Url(string path, string ua, string timestamp)
        => Base + path + "?timestamp=" + timestamp + "&signature=" + Sign(timestamp, ua);

    public static string Cover(string code)
    {
        string lower = code.ToLowerInvariant();
        byte[] md5 = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(lower));
        string hex = Convert.ToHexString(md5).ToLowerInvariant();
        return "https://icdn.123av.me/img2/s360/" + hex.Substring(0, 2) + "/" + lower + "/cover.jpg";
    }

    public static string Duration(string seconds)
    {
        if (!int.TryParse(seconds, out int total) || total <= 0) return null;
        return (total / 3600).ToString() + ":" + (total % 3600 / 60).ToString("00") + ":" + (total % 60).ToString("00");
    }
}

public static class Av123To
{
    public static readonly string SiteHost = "https://123av.com";
    public static readonly string ChromeUA = Shared.Services.Http.UserAgent;

    public static string SortLabel(string sort) => sort switch
    {
        "recent" => "Recently added",
        "hot" => "Hot",
        "today" => "Today views",
        "week" => "Weekly views",
        "month" => "Monthly views",
        "views" => "Most viewed",
        "follows" => "Most followed",
        "longest" => "Longest",
        _ => "Release date"
    };

    static readonly Regex _cardRe = new Regex(
        "<a class=\"card__cover\" href=\"/en/v/([^\"]+)\">.*?<img class=\"card__img\" src=\"([^\"]+)\"[^>]*>.*?<span class=\"card__dur\">([^<]*)</span>.*?<h3 class=\"card__title\"><a class=\"card__link\"[^>]*>(.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var items = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return items;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in _cardRe.Matches(html))
        {
            string slug = m.Groups[1].Value.Trim();
            if (!seen.Add(slug)) continue;
            string poster = WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
            string dur = m.Groups[3].Value.Trim();
            string title = WebUtility.HtmlDecode(Regex.Replace(m.Groups[4].Value, @"\s+", " ").Trim());
            if (title.Length > 150) title = title.Substring(0, 147) + "...";

            string detail = "/en/v/" + slug;
            items.Add(new PlaylistItem
            {
                name = title,
                video = route + "?uri=" + HttpUtility.UrlEncode(detail),
                picture = poster,
                time = dur,
                json = true,
                bookmark = new Bookmark { site = "av123", href = SiteHost + detail, image = poster }
            });
        }
        return items;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html)) return 1;
        var mt = Regex.Match(html, @"pager__total\x22>\s*/\s*(\d+)");
        if (mt.Success && int.TryParse(mt.Groups[1].Value, out int tp) && tp > 0) return tp;
        int max = 1;
        foreach (Match m in Regex.Matches(html, @"(?:[?&]|&amp;)page=(\d+)"))
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max) max = p;
        return max;
    }

    // detail -> episode embed url (https://<host>/e/<hash>?poster=...)
    public static string ExtractEpisode(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        int i = html.IndexOf("JSON.parse(", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        int s = i + "JSON.parse(".Length;
        if (s < html.Length && html[s] == '\\') s++;
        if (s >= html.Length || html[s] != '\'') return null;
        s++;
        int j = html.IndexOf("\\')", s, StringComparison.Ordinal);
        if (j < 0) j = html.IndexOf("')", s, StringComparison.Ordinal);
        if (j < 0) return null;
        string inner = html.Substring(s, j - s);
        string un = JsUnescape(inner).Replace("\\/", "/");
        var m = Regex.Match(un, @"https?://[^\x22'\s]+/e/[a-z0-9_]+[^\x22'\s]*", RegexOptions.IgnoreCase);
        return m.Success ? m.Value : null;
    }

    public static string ExtractStream(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var m = Regex.Match(json, @"\x22stream\x22\s*:\s*\x22([^\x22]+)\x22", RegexOptions.IgnoreCase);
        return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
    }

    static string JsUnescape(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 < s.Length && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, null, out int cp))
                    { sb.Append((char)cp); i += 4; }
                    break;
                default: sb.Append(n); break;
            }
        }
        return sb.ToString();
    }

    // detail page -> tat ca chip thong tin: (kind, slug, ten)
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match row in Regex.Matches(html, "<div class=\"watch__info-row\">(.*?)</div>", RegexOptions.Singleline))
        {
            foreach (Match m in Regex.Matches(row.Groups[1].Value,
                "<a class=\"chip\" href=\"/en/([a-z-]+)/([^\"]+)\">([^<]+)</a>", RegexOptions.IgnoreCase))
            {
                string kind = m.Groups[1].Value.ToLowerInvariant();
                string slug = m.Groups[2].Value.Trim();
                string name = WebUtility.HtmlDecode(m.Groups[3].Value.Trim());
                if (name.Length == 0) continue;
                if (!seen.Add(kind + "/" + slug)) continue;
                list.Add((kind, slug, name));
            }
        }
        return list;
    }

    // detail page -> video lien quan (khoi vside)
    public static List<(string slug, string title, string dur)> Related(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a class=\"vside\" href=\"/en/v/([^\"]+)\">.*?background-image:url\\('([^']+)'\\)[^>]*>.*?<span class=\"vside__dur\">([^<]*)</span>.*?<span class=\"vside__title\">(.*?)</span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim();
            if (!seen.Add(slug)) continue;
            string dur = WebUtility.HtmlDecode(m.Groups[3].Value.Trim());
            string title = WebUtility.HtmlDecode(Regex.Replace(m.Groups[4].Value, "<[^>]+>", "")).Trim();
            if (title.Length == 0) continue;
            list.Add((slug, title, dur));
        }
        return list;
    }

    // danh sach "Related videos" (aside) -> playlist
    public static List<PlaylistItem> PlaylistRelated(string route, string html)
    {
        var items = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return items;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html,
            "<a class=\"vside\" href=\"/en/v/([^\"]+)\">.*?background-image:url\\('([^']+)'\\)[^>]*>.*?<span class=\"vside__dur\">([^<]*)</span>.*?<span class=\"vside__title\">(.*?)</span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim();
            if (!seen.Add(slug)) continue;

            string poster = WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
            string dur = WebUtility.HtmlDecode(m.Groups[3].Value.Trim());
            string title = WebUtility.HtmlDecode(Regex.Replace(m.Groups[4].Value, "<[^>]+>", "")).Trim();
            if (title.Length == 0) continue;
            if (title.Length > 150) title = title.Substring(0, 147) + "...";

            string detail = "/en/v/" + slug;
            items.Add(new PlaylistItem
            {
                name = title,
                video = route + "?uri=" + HttpUtility.UrlEncode(detail),
                picture = poster,
                time = dur,
                json = true,
                bookmark = new Bookmark { site = "av123", href = SiteHost + detail, image = poster }
            });
        }
        return items;
    }

    // detail page -> danh sach the loai (chi lay khoi Genres, khong lan Maker/Series/Tags)
    public static List<(string title, string slug)> Genres(string html)
    {
        var list = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return list;

        int i = html.IndexOf(">Genres</dt>", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return list;

        int s = html.IndexOf("chips", i, StringComparison.OrdinalIgnoreCase);
        if (s < 0) return list;
        int e = html.IndexOf("</dd>", s, StringComparison.OrdinalIgnoreCase);
        if (e < 0) e = Math.Min(html.Length, s + 4000);

        string block = html.Substring(s, e - s);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(block, "<a[^>]+href=\"/en/genres/([^\"]+)\"[^>]*>([^<]+)</a>", RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim();
            if (!seen.Add(slug)) continue;
            string title = WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (title.Length == 0) continue;
            list.Add((title, slug));
        }
        return list;
    }

    // taxonomy index -> (name, slug, count)
    public static List<(string name, string slug, int count)> Taxo(string html, string kind)
    {
        var list = new List<(string, string, int)>();
        if (string.IsNullOrEmpty(html)) return list;
        string cls = kind == "genres" ? "gchip" : "mcard";
        string cntCls = kind == "genres" ? "gchip__count" : "mcard__meta";
        var re = new Regex(
            "<a class=\"" + cls + "\" href=\"/en/" + kind + "/([^\"]+)\"[^>]*>.*?<span class=\"" + cls + "__name\">([^<]+)</span>.*?<span class=\"" + cntCls + "\">([^<]+)</span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in re.Matches(html))
        {
            string slug = m.Groups[1].Value.Trim();
            if (!seen.Add(slug)) continue;
            string name = WebUtility.HtmlDecode(m.Groups[2].Value.Trim());
            int count = ParseCount(m.Groups[3].Value);
            list.Add((name, slug, count));
        }
        return list;
    }

    static int ParseCount(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var m = Regex.Match(s, @"([\d.,]+)\s*(M|K)?");
        if (!m.Success) return 0;
        string num = m.Groups[1].Value.Replace(",", "").Replace(".", "");
        if (!double.TryParse(num, out double v)) return 0;
        string suf = m.Groups[2].Value.ToUpperInvariant();
        if (suf == "M") v *= 1_000_000;
        else if (suf == "K") v *= 1_000;
        return (int)v;
    }

    static string MenuUrl(string host, string c, string sort)
    {
        var q = new List<string>();
        if (!string.IsNullOrEmpty(c)) q.Add("c=" + HttpUtility.UrlEncode(c));
        if (!string.IsNullOrEmpty(sort)) q.Add("sort=" + sort);
        return host + "/av123" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    public static List<MenuItem> Menu(string host, string c, string sort,
        List<(string name, string slug, int count)> genres,
        List<(string name, string slug, int count)> makers)
    {
        var menu = new List<MenuItem>();

        menu.Add(new MenuItem { title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/av123" });

        menu.Add(new MenuItem
        {
            title = "Sắp xếp: " + SortLabel(sort),
            playlist_url = "submenu",
            submenu = new List<MenuItem>
            {
                new MenuItem { title = "Release date", playlist_url = MenuUrl(host, c, "release_date") },
                new MenuItem { title = "Recently added", playlist_url = MenuUrl(host, c, "recent") },
                new MenuItem { title = "Hot", playlist_url = MenuUrl(host, c, "hot") },
                new MenuItem { title = "Today views", playlist_url = MenuUrl(host, c, "today") },
                new MenuItem { title = "Weekly views", playlist_url = MenuUrl(host, c, "week") },
                new MenuItem { title = "Monthly views", playlist_url = MenuUrl(host, c, "month") },
                new MenuItem { title = "Most viewed", playlist_url = MenuUrl(host, c, "views") },
                new MenuItem { title = "Most followed", playlist_url = MenuUrl(host, c, "follows") },
                new MenuItem { title = "Longest", playlist_url = MenuUrl(host, c, "longest") }
            }
        });

        menu.Add(new MenuItem
        {
            title = "Mới & Hot",
            playlist_url = "submenu",
            submenu = new List<MenuItem>
            {
                new MenuItem { title = "New", playlist_url = MenuUrl(host, "/en/new", null) },
                new MenuItem { title = "Hot", playlist_url = MenuUrl(host, "/en/hot", null) },
                new MenuItem { title = "Recent", playlist_url = MenuUrl(host, "/en/recent", null) }
            }
        });

        menu.Add(new MenuItem
        {
            title = "Trending",
            playlist_url = "submenu",
            submenu = new List<MenuItem>
            {
                new MenuItem { title = "Today", playlist_url = MenuUrl(host, "/en/all", "today") },
                new MenuItem { title = "This week", playlist_url = MenuUrl(host, "/en/all", "week") },
                new MenuItem { title = "This month", playlist_url = MenuUrl(host, "/en/all", "month") }
            }
        });

        menu.Add(new MenuItem
        {
            title = "Types",
            playlist_url = "submenu",
            submenu = new List<MenuItem>
            {
                new MenuItem { title = "Censored", playlist_url = MenuUrl(host, "/en/censored", null) },
                new MenuItem { title = "Uncensored", playlist_url = MenuUrl(host, "/en/uncensored", null) },
                new MenuItem { title = "Uncensored Leaked", playlist_url = MenuUrl(host, "/en/uncensored-leaked", null) }
            }
        });

        menu.Add(new MenuItem
        {
            title = "Amateur",
            playlist_url = "submenu",
            submenu = new List<MenuItem>
            {
                new MenuItem { title = "SIRO", playlist_url = MenuUrl(host, "/en/tags/siro", null) },
                new MenuItem { title = "LUXU", playlist_url = MenuUrl(host, "/en/tags/259luxu", null) },
                new MenuItem { title = "200GANA", playlist_url = MenuUrl(host, "/en/tags/200gana", null) },
                new MenuItem { title = "PRESTIGE", playlist_url = MenuUrl(host, "/en/tags/prestige-premium", null) },
                new MenuItem { title = "ORECO", playlist_url = MenuUrl(host, "/en/tags/230oreco", null) },
                new MenuItem { title = "S-CUTE", playlist_url = MenuUrl(host, "/en/makers/s-cute", null) },
                new MenuItem { title = "ARA", playlist_url = MenuUrl(host, "/en/tags/261ara", null) },
                new MenuItem { title = "390JAC", playlist_url = MenuUrl(host, "/en/tags/390jac", null) }
            }
        });

        menu.Add(new MenuItem
        {
            title = "Uncensored",
            playlist_url = "submenu",
            submenu = new List<MenuItem>
            {
                new MenuItem { title = "FC2", playlist_url = MenuUrl(host, "/en/makers/fc2", null) },
                new MenuItem { title = "HEYZO", playlist_url = MenuUrl(host, "/en/makers/heyzo", null) },
                new MenuItem { title = "1pondo", playlist_url = MenuUrl(host, "/en/makers/1pondo", null) },
                new MenuItem { title = "Caribbeancom", playlist_url = MenuUrl(host, "/en/makers/caribbeancom", null) },
                new MenuItem { title = "10musume", playlist_url = MenuUrl(host, "/en/makers/10musume", null) },
                new MenuItem { title = "Pacopacomama", playlist_url = MenuUrl(host, "/en/makers/pacopacomama", null) },
                new MenuItem { title = "Tokyo Hot", playlist_url = MenuUrl(host, "/en/makers/tokyo-hot", null) },
                new MenuItem { title = "XXX-AV", playlist_url = MenuUrl(host, "/en/makers/xxx-av", null) }
            }
        });

        AddChunks(menu, host, "Thể loại", "/en/genres/", genres);
        AddChunks(menu, host, "Hãng", "/en/makers/", makers);

        return menu;
    }

    static void AddChunks(List<MenuItem> menu, string host, string title, string pathPrefix,
        List<(string name, string slug, int count)> list)
    {
        if (list == null || list.Count == 0) return;
        const int chunk = 300;
        int total = (list.Count + chunk - 1) / chunk;

        for (int i = 0; i < total; i++)
        {
            var part = list.Skip(i * chunk).Take(chunk).ToList();
            string label = total == 1
                ? title + " 1-" + list.Count
                : title + " " + (i * chunk + 1) + "-" + (i * chunk + part.Count);

            menu.Add(new MenuItem
            {
                title = label,
                playlist_url = "submenu",
                submenu = part.Select(x => new MenuItem
                {
                    title = x.name,
                    playlist_url = MenuUrl(host, pathPrefix + x.slug, null)
                }).ToList()
            });
        }
    }
}
