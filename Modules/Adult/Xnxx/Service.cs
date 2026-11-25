using System;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services.RxEnumerate;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web;

namespace Xnxx;

public static class XnxxTo
{
    public static string Uri(string host, string search, int pg)
        => Uri(host, search, null, pg);

    public static string Uri(string host, string search, string c, int pg)
    {
        if (!string.IsNullOrWhiteSpace(search))
            return $"{host}/search/{HttpUtility.UrlEncode(search)}/{pg}";

        if (!string.IsNullOrWhiteSpace(c))
        {
            // chip popup: "tag/<slug>" | "model/<tên>"
            string kw = c.Trim('/');

            if (kw.StartsWith("tag/", StringComparison.OrdinalIgnoreCase))
                kw = kw.Substring(4);
            else if (kw.StartsWith("model/", StringComparison.OrdinalIgnoreCase))
                kw = kw.Substring(6);

            return $"{host}/search/{HttpUtility.UrlEncode(kw)}/{pg}";
        }

        return $"{host}/best/{DateTime.Today.AddMonths(-1):yyyy-MM}/{pg}";
    }

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("[\t ]id=\"video(-|_)", html, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            var g = row.Groups(" href=\"/(video-[^\"]+)\" title=\"([^\"]+)\"");

            if (!string.IsNullOrEmpty(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
            {
                string img = row.Match("data-src=\"([^\"]+)\"").Replace(".THUMBNUM.", ".1.") ?? string.Empty;

                string preview =
                    row.Match("\"previewVideo\":\"([^\"]+)\"")?.Replace("\\", "") ??
                    row.Match("data-pvv=\"([^\"]+)\"");

                if (string.IsNullOrEmpty(preview))
                    preview = Regex.Replace(img, "/[^/]+$", "/preview.mp4");

                var pl = new PlaylistItem()
                {
                    name = g[2].Value,
                    video = $"{route}?uri={g[1].Value}",
                    picture = img,
                    preview = preview,
                    time = row.Match("</span>([^<]+)<span class=\"video-hd\">", trim: true),
                    quality = row.Match("<span class=\"superfluous\"> - </span>([^<]+)</span>"),
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "xnx",
                        href = g[1].Value,
                        image = img
                    }
                };

                if (onplaylist != null)
                    pl = onplaylist.Invoke(pl);

                playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion

    #region Popup (chips -> /xnx/genres?uri= + "Похожие видео")
    // Detail (mirror xnxx-ru.com) nhúng config JS:
    //   "video_tags":["anal","black",…]                 -> chip Thẻ (mở bằng /search/<tag>/<pg>)
    //   "video_models":{"id":{"name":"…","profile":"…"}} -> chip Diễn viên (mở bằng /search/<name>/<pg>)
    //   var video_related=[{"u":"/video-…","t":"tên","d":"11min","i":"thumb"}] -> "Похожие видео"
    public static string DetailUrl(string host, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string baseHost = (host ?? "https://www.xnxx-ru.com").TrimEnd('/');

        if (!uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return baseHost + "/" + uri.Trim('/');

        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return null;

        string h = u.Host.ToLowerInvariant();
        if (h == "www.xnxx-ru.com" || h.EndsWith(".xnxx-ru.com") || h == "xnxx.com" || h.EndsWith(".xnxx.com"))
            return baseHost + u.AbsolutePath;

        return null;
    }

    static string PrettySlug(string slug)
    {
        string s = Regex.Replace(slug, "^[0-9]+-", "").Replace('-', ' ').Replace('_', ' ');
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }

    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Thẻ
        int i = html.IndexOf("\"video_tags\":", StringComparison.OrdinalIgnoreCase);
        if (i > 0)
        {
            int end = html.IndexOf("]", i);
            string region = end > i ? html.Substring(i, end - i) : string.Empty;

            foreach (Match m in Regex.Matches(region, "\"([a-z0-9][a-z0-9\\-_]{1,60})\"", RegexOptions.IgnoreCase))
            {
                string slug = m.Groups[1].Value.Trim();
                if (slug.Length == 0 || slug == "video_tags" || !seen.Add("tag/" + slug))
                    continue;

                list.Add(("tag", slug, PrettySlug(slug)));
            }
        }

        // Diễn viên
        i = html.IndexOf("\"video_models\":", StringComparison.OrdinalIgnoreCase);
        if (i > 0)
        {
            int end = html.IndexOf("}}", i);
            string region = end > i ? html.Substring(i, end + 2 - i) : string.Empty;

            foreach (Match m in Regex.Matches(region, "\"name\":\"([^\"]{1,80})\"", RegexOptions.IgnoreCase))
            {
                string name = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
                if (name.Length == 0 || !seen.Add("model/" + name))
                    continue;

                list.Add(("model", name, name));
            }
        }

        return list;
    }

    static string JsonUnescape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        s = Regex.Replace(s, "\\\\u([0-9a-fA-F]{4})", m =>
            ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

        return s.Replace("\\/", "/").Replace("\\\"", "\"");
    }

    public static List<PlaylistItem> PlaylistRelated(string route, string host, string html, string self)
    {
        if (string.IsNullOrEmpty(html))
            return new List<PlaylistItem>();

        int i = html.IndexOf("var video_related=[", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<PlaylistItem>();

        int end = html.IndexOf("];", i);
        if (end < 0)
            return new List<PlaylistItem>();

        string region = html.Substring(i, end - i);
        var list = new List<PlaylistItem>();
        string selfPath = self?.Trim('/') ?? "";

        foreach (var row in Rx.Split("{\"id\":", region).Rows())
        {
            string link = row.Match("\"u\":\"([^\"]+)\"", trim: true);
            if (string.IsNullOrWhiteSpace(link))
                continue;

            link = JsonUnescape(link).TrimStart('/');
            if (link.Length == 0 || link.Trim('/').Equals(selfPath, StringComparison.OrdinalIgnoreCase))
                continue;

            string name = row.Match("\"t\":\"([^\"]+)\"", trim: true);
            if (string.IsNullOrWhiteSpace(name))
                name = row.Match("\"tf\":\"([^\"]+)\"", trim: true);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string pic = JsonUnescape(row.Match("\"i\":\"([^\"]+)\"", trim: true));

            list.Add(new PlaylistItem()
            {
                name = HttpUtility.HtmlDecode(JsonUnescape(name)),
                video = $"{route}?uri={link}",
                picture = pic,
                time = row.Match("\"d\":\"([^\"]+)\"", trim: true),
                json = true,
                related = true,
                bookmark = new Bookmark()
                {
                    site = "xnx",
                    href = link,
                    image = pic
                }
            });
        }

        return list;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host)
    {
        return new List<MenuItem>(1)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = $"{host}/xnx"
            }
        };
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return $"{host}/{Regex.Replace(url ?? string.Empty, "^([^/]+)/.*", "$1/_")}";
    }

    public static StreamItem StreamLinks(ReadOnlySpan<char> html, string route, Func<string, Task<string>> onm3u = null)
    {
        if (html.IsEmpty)
            return null;

        string stream_link = Rx.Match(html, "html5player\\.setVideoHLS\\('([^']+)'\\);");
        if (string.IsNullOrWhiteSpace(stream_link))
            return null;

        #region getRelated
        List<PlaylistItem> getRelated(ReadOnlySpan<char> html)
        {
            string json = Rx.Match(html, "video_related=([^\n\r]+);window");
            if (string.IsNullOrWhiteSpace(json) || !json.StartsWith("[") || !json.EndsWith("]"))
                return new List<PlaylistItem>();

            var related = new List<PlaylistItem>(40);

            try
            {
                foreach (var r in JsonSerializer.Deserialize<List<Related>>(json, new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                }))
                {
                    if (string.IsNullOrEmpty(r.tf) || string.IsNullOrEmpty(r.u) || string.IsNullOrEmpty(r.i))
                        continue;

                    related.Add(new PlaylistItem()
                    {
                        name = r.tf,
                        video = $"{route}?uri={r.u.Remove(0, 1)}",
                        picture = r.i,
                        json = true,
                        related = true,
                        bookmark = new Bookmark()
                        {
                            site = "xnx",
                            href = r.u.Remove(0, 1),
                            image = r.i
                        }
                    });
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "{Class} {CatchId}", "XnxxTo", "id_h1jfd3x2");
            }

            return related;
        }
        #endregion

        return new StreamItem()
        {
            qualitys = new Dictionary<string, string>()
            {
                ["auto"] = stream_link
            },
            recomends = getRelated(html)
        };

        //string m3u8 = onm3u == null ? null : await onm3u.Invoke(stream_link);
        //if (m3u8 == null)
        //{
        //    return new StreamItem()
        //    {
        //        qualitys = new Dictionary<string, string>()
        //        {
        //            ["auto"] = stream_link
        //        },
        //        recomends = getRelated()
        //    };
        //}

        //var stream_links = new Dictionary<int, string>();
        //foreach (Match m in Regex.Matches(m3u8, "(hls-(2160|1440|1080|720|480|360)p[^\n\r\t ]+)"))
        //{
        //    string hls = m.Groups[1].Value;
        //    if (string.IsNullOrEmpty(hls))
        //        continue;

        //    hls = $"{Regex.Replace(stream_link, "/hls\\.m3u.*", "")}/{hls}".Replace("https:", "http:");
        //    stream_links.Add(int.Parse(m.Groups[2].Value), hls);
        //}

        //return new StreamItem()
        //{
        //    qualitys = stream_links.OrderByDescending(i => i.Key).ToDictionary(k => $"{k.Key}p", v => v.Value),
        //    recomends = getRelated()
        //};
    }
    #endregion
}
