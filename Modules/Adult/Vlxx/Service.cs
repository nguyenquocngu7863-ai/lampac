using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace Vlxx;

public static class VlxxTo
{
    public static string SiteHost = "https://vlxx.net";

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
            string url = host + "/search/" + slug + "/";
            if (pg > 1)
                url += pg + "/";
            return url;
        }

        if (!string.IsNullOrEmpty(c))
        {
            string url = host + "/" + c.Trim('/') + "/";
            if (pg > 1)
                url += pg + "/";
            return url;
        }

        if (pg > 1)
            return host + "/new/" + pg + "/";

        return host + "/";
    }

    static void AddPl(List<PlaylistItem> playlists, HashSet<string> seen, string uri, string name, string href)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(href))
            return;
        if (!seen.Add(href))
            return;

        string id = "";
        var idm = Regex.Match(href, @"/video/[^/]+/([0-9]+)/");
        if (idm.Success)
            id = idm.Groups[1].Value;

        string poster = "";
        if (!string.IsNullOrEmpty(id))
            poster = SiteHost + "/img/" + id + ".jpg";

        string pageUrl = href.StartsWith("http") ? href : SiteHost + href;

        playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
        {
            video = uri + "?uri=" + HttpUtility.UrlEncode(pageUrl),
            name = HttpUtility.HtmlDecode(name.Trim()),
            picture = poster,
            json = true,
            bookmark = new Shared.Models.SISI.Base.Bookmark()
            {
                site = "vlxx",
                href = pageUrl,
                image = poster
            }
        });
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        var seen = new HashSet<string>();

        // Card: <a title="..." href="/video/{slug}/{id}/"> (title truoc, ca "" lan '')
        foreach (Match m in Regex.Matches(html, @"<a\s+[^>]*?title\s*=\s*[""']([^""']+)[""'][^>]*?href\s*=\s*[""'](/video/[^""']+/)[^>]*>", RegexOptions.Singleline))
            AddPl(playlists, seen, uri, m.Groups[1].Value, m.Groups[2].Value);

        // Fallback: href truoc title sau
        if (playlists.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, @"<a\s+[^>]*?href\s*=\s*[""'](/video/[^""']+/)[^>]*?title\s*=\s*[""']([^""']+)[""'][^>]*>", RegexOptions.Singleline))
                AddPl(playlists, seen, uri, m.Groups[2].Value, m.Groups[1].Value);
        }

        return playlists;
    }

    // ========== POPUP ==========
    // Trang detail: <div class="video-tags"> gom
    //   <div class="actress-tag"><a href="/cast/<slug>/">Ten</a></div>
    //   <div class="category-tag"><a href="/<slug>/">Ten</a>...</div>
    // va khoi "Phim liên quan" = <div id="video-list">...
    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = HttpUtility.HtmlDecode(value.Trim());
        if (value.StartsWith("//")) value = "https:" + value;
        else if (value.StartsWith("/")) value = SiteHost + value;
        else if (!value.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
            value = SiteHost + "/" + value.Trim('/') + "/";
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return null;
        string host = parsed.Host.ToLowerInvariant();
        if (!host.EndsWith("vlxx.net") && !host.EndsWith("vlxx.phd")) return null;
        return value;
    }

    public static List<(string name, string path)> Chips(string html, bool actresses)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        string cls = actresses ? "actress-tag" : "category-tag";
        var box = Regex.Match(html, @"<div[^>]*class=""" + cls + @"""[^>]*>(.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!box.Success) return res;

        string pattern = actresses
            ? @"<a\b[^>]*href\s*=\s*[""']/cast/([^""'/]+)/[""'][^>]*>(.*?)</a>"
            : @"<a\b[^>]*href\s*=\s*[""']/([^""'/]+)/[""'][^>]*>(.*?)</a>";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(box.Groups[1].Value, pattern,
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim();
            string name = Regex.Replace(HttpUtility.HtmlDecode(m.Groups[2].Value), "<[^>]+>", " ").Trim();
            if (slug.Length == 0 || name.Length == 0 || name.Length > 60 || !seen.Add(slug)) continue;
            res.Add((name, actresses ? "cast/" + slug : slug));
        }
        return res;
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Related(string route, string html)
    {
        if (string.IsNullOrEmpty(html)) return new List<Shared.Models.SISI.Base.PlaylistItem>();
        var m = Regex.Match(html, @"<div[^>]*id=[""']video-list[""'][^>]*>", RegexOptions.IgnoreCase);
        if (!m.Success) return new List<Shared.Models.SISI.Base.PlaylistItem>();
        return Playlist(route, html.Substring(m.Index));
    }

    public static List<(string server, string vid)> GetServers(string html, string pageUrl)
    {
        var list = new List<(string server, string vid)>();

        string vid = "";
        var idm = Regex.Match(html ?? "", @"id=""video""[^>]*data-id=""([0-9]+)""");
        if (!idm.Success)
            idm = Regex.Match(html ?? "", @"data-id=""([0-9]+)""");
        if (idm.Success)
            vid = idm.Groups[1].Value;
        if (string.IsNullOrEmpty(vid))
        {
            var um = Regex.Match(pageUrl ?? "", @"/video/[^/]+/([0-9]+)/");
            if (um.Success)
                vid = um.Groups[1].Value;
        }

        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html ?? "", @"server\((\d+),\s*(\d+)\)"))
        {
            string key = m.Groups[1].Value + ":" + m.Groups[2].Value;
            if (seen.Add(key))
                list.Add((m.Groups[1].Value, m.Groups[2].Value));
        }

        if (list.Count == 0 && !string.IsNullOrEmpty(vid))
        {
            var svm = Regex.Match(html ?? "", @"data-sv=""(\d+)""");
            list.Add((svm.Success ? svm.Groups[1].Value : "1", vid));
        }

        return list;
    }

    public static Dictionary<string, string> StreamLinksFromEmbed(string embedHtml)
    {
        var links = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(embedHtml))
            return links;

        foreach (Match m in Regex.Matches(embedHtml, @"""file""\s*:\s*""(https?[^""]+)"""))
        {
            string file = m.Groups[1].Value.Replace("\\/", "/");
            int start = Math.Max(0, m.Index - 300);
            string before = embedHtml.Substring(start, m.Index - start);
            string label = "Auto";
            var lm = Regex.Matches(before, @"""label""\s*:\s*""([^""]*)""");
            if (lm.Count > 0)
                label = lm[lm.Count - 1].Groups[1].Value;
            if (string.IsNullOrEmpty(label))
                label = "Auto";
            string key = label;
            int dup = 2;
            while (links.ContainsKey(key))
                key = label + " " + (dup++);
            if (!links.ContainsValue(file))
                links.TryAdd(key, file);
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
                playlist_url = host + "/vlxx"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/vlxx"),
                    new("Phim sex hay", host + "/vlxx?c=phim-sex-hay"),
                    new("JAV", host + "/vlxx?c=jav"),
                    new("Vietsub", host + "/vlxx?c=vietsub"),
                    new("Không che", host + "/vlxx?c=khong-che"),
                    new("Học sinh", host + "/vlxx?c=hoc-sinh"),
                    new("Vụng trộm", host + "/vlxx?c=vung-trom"),
                    new("Cấp 3", host + "/vlxx?c=cap-3"),
                    new("Châu Âu", host + "/vlxx?c=chau-au"),
                    new("Xvideos", host + "/vlxx?c=xvideos"),
                    new("Xnxx", host + "/vlxx?c=xnxx"),
                    new("XXX", host + "/vlxx?c=xxx"),
                }
            }
        };
    }
}
