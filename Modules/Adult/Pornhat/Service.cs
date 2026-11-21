using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace Pornhat;

public static class PornhatTo
{
    public const string SiteHost = "https://www.pornhat.one";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36";

    static string Decode(string value) => HttpUtility.HtmlDecode(value ?? "").Trim();

    // SISI menu/popup labels are restricted to Vietnamese/English. The site mixes
    // Latin and translated tag names, so discard labels from other scripts.
    public static bool IsAllowedLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return !Regex.IsMatch(value, @"[\u0400-\u04FF\u0370-\u03FF\u4E00-\u9FFF\u3040-\u30FF\uAC00-\uD7AF\u0E00-\u0E7F\u0600-\u06FF\u0590-\u05FF]");
    }

    public static string Uri(string host, string search, string c, int pg)
    {
        host = string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');
        if (pg < 1) pg = 1;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = Regex.Replace(search.Trim(), @"\s+", "-");
            slug = System.Uri.EscapeDataString(slug).Replace("%2D", "-");
            return host + "/search/" + slug + "/" + (pg > 1 ? pg + "/" : "");
        }

        string path = (c ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(path))
            return pg > 1 ? host + "/" + pg + "/" : host + "/";

        return host + "/" + path + (pg > 1 ? "/" + pg + "/" : "/");
    }

    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match card in Regex.Matches(html,
            @"<div\b[^>]*class=""[^""]*\bitem\b[^""]*\bthumb-bl-video\b[^""]*""[^>]*>(.*?)(?=<div\b[^>]*class=""[^""]*\bitem\b[^""]*\bthumb-bl-video\b|<div class=""pagination"")",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string block = card.Groups[1].Value;
            var link = Regex.Match(block, @"<a\b[^>]*href=""(/video/[^""]+)""[^>]*title=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!link.Success)
                link = Regex.Match(block, @"<a\b[^>]*href=""(/video/[^""]+)""[^>]*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!link.Success) continue;

            string href = link.Groups[1].Value;
            string name = link.Groups[2].Success ? Decode(link.Groups[2].Value) : "";
            if (string.IsNullOrEmpty(name))
            {
                var title = Regex.Match(block, @"<a\b[^>]*title=""([^""]+)""[^>]*href=""(/video/[^""]+)""", RegexOptions.IgnoreCase);
                if (title.Success)
                {
                    name = Decode(title.Groups[1].Value);
                    href = title.Groups[2].Value;
                }
            }
            if (string.IsNullOrEmpty(name) || !seen.Add(href)) continue;

            string poster = null;
            var image = Regex.Match(block, @"<img\b[^>]*\bdata-original\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!image.Success)
                image = Regex.Match(block, @"<img\b[^>]*src=""([^""]+)""", RegexOptions.IgnoreCase);
            if (image.Success && image.Groups[1].Value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                poster = image.Groups[1].Value;

            string duration = null;
            var time = Regex.Match(block, @"<ul\s+class=""video-meta""[^>]*>(.*?)</ul>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (time.Success)
            {
                var items = Regex.Matches(time.Groups[1].Value, @"<li\b[^>]*>.*?<span>\s*([0-9]{1,2}:[0-9]{2}(?::[0-9]{2})?)\s*</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (items.Count > 0) duration = Decode(items[0].Groups[1].Value);
            }

            list.Add(new PlaylistItem
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                time = duration,
                json = true
            });
        }
        return list;
    }

    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html)) return 1;
        var block = Regex.Match(html, @"<div\s+class=""pagination""[^>]*>(.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!block.Success) return 1;
        int max = 1;
        foreach (Match m in Regex.Matches(block.Groups[1].Value, @"href=""/(?:[^""?]*/)?(\d+)/"""))
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max) max = n;
        return max;
    }

    public static List<(string slug, string name)> Tags(string html)
    {
        var tags = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return tags;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html,
            @"<a\b[^>]*class=""item""[^>]*href=""(/tags/[^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value.Trim('/');
            string name = Decode(Regex.Replace(m.Groups[2].Value, @"<[^>]+>", " "));
            name = Regex.Replace(name, @"\s*\(\s*[0-9,.]+\s*\)\s*$", "").Trim();
            name = Regex.Replace(name, @"\s+", " ");
            if (!IsAllowedLabel(name) || !seen.Add(slug)) continue;
            tags.Add((slug, name));
        }
        return tags;
    }

    public static List<MenuItem> TagMenu(string host, List<(string slug, string name)> tags)
    {
        var result = new List<MenuItem>();
        var groups = tags
            .Where(x => !string.IsNullOrWhiteSpace(x.name) && !string.IsNullOrWhiteSpace(x.slug))
            .GroupBy(x => char.ToUpperInvariant(x.name[0]).ToString())
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            int part = 0;
            foreach (var batch in group.Chunk(240))
            {
                part++;
                var items = batch.Select(x => new MenuItem(x.name, host.TrimEnd('/') + "/pornhat?c=" + HttpUtility.UrlEncode(x.slug))).ToList();
                string title = group.Count() <= 240 ? "Thẻ " + group.Key : "Thẻ " + group.Key + " " + part;
                result.Add(new MenuItem { title = title, playlist_url = "submenu", submenu = items });
            }
        }
        return result;
    }

    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var chips = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html)) return chips;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int start = html.IndexOf("<div class=\"video-info\"", StringComparison.OrdinalIgnoreCase);
        string section = start >= 0 ? html.Substring(start) : html;
        int end = section.IndexOf("<div class=\"right-player-col\"", StringComparison.OrdinalIgnoreCase);
        if (end > 0) section = section.Substring(0, end);

        foreach (Match m in Regex.Matches(section,
            @"<a\b[^>]*href=""(/(?:tags|models|sites)/[^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string path = m.Groups[1].Value;
            string kind = path.StartsWith("/tags/", StringComparison.OrdinalIgnoreCase) ? "tag"
                : path.StartsWith("/models/", StringComparison.OrdinalIgnoreCase) ? "model" : "channel";
            string slug = path.Trim('/');
            string name = Decode(Regex.Replace(m.Groups[2].Value, @"<[^>]+>", " "));
            name = Regex.Replace(name, @"\s+", " ").Trim();
            if (!IsAllowedLabel(name) || !seen.Add(kind + ":" + slug)) continue;
            chips.Add((kind, slug, name));
        }
        return chips;
    }

    public static List<string> StreamUrls(string html)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(html)) return result;

        foreach (Match source in Regex.Matches(html,
            @"<source\b[^>]*\bsrc\s*=\s*""(https?://[^""]+)""[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string url = Decode(source.Groups[1].Value);
            if (!url.Contains("/get_file/", StringComparison.OrdinalIgnoreCase))
                continue;

            string tag = source.Value;
            var label = Regex.Match(tag, @"\b(?:title|label)\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
            string quality = label.Success ? Decode(label.Groups[1].Value) : "Auto";
            if (string.Equals(quality, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                int last = url.LastIndexOf('/');
                string file = last >= 0 ? url.Substring(0, last) : url;
                quality = Regex.IsMatch(file, @"_\d+p$", RegexOptions.IgnoreCase)
                    ? file.Substring(file.LastIndexOf('_') + 1) : quality;
            }
            if (!result.Contains(url))
                result.Add(url + "|" + quality);
        }
        return result;
    }

    public static string QualityLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "MP4";
        var m = Regex.Match(raw, @"\|([^|]+)$");
        return m.Success ? m.Groups[1].Value.Trim() : "MP4";
    }

    public static string QualityUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        int i = raw.LastIndexOf('|');
        return i > 0 ? raw.Substring(0, i) : raw;
    }
}
