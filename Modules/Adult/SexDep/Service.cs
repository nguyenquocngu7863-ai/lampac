using Shared;
using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace SexDep;

public static class SexDepTo
{
    public static string SiteHost = "https://x.sexdep.co.uk";

    public static string Slugify(string s)
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
            string url = host + "/?search=" + HttpUtility.UrlEncode(search);
            if (pg > 1)
                url += "&page=" + pg;
            return url;
        }

        if (!string.IsNullOrEmpty(c))
        {
            string url = host + "/" + c.Trim('/');
            if (pg > 1)
                url += "?page=" + pg;
            return url;
        }

        // Home pg>1 di thang vao danh sach phim-moi (trang chu bo qua ?page)
        if (pg > 1)
            return host + "/danh-sach/phim-moi?page=" + pg;

        return host + "/";
    }

    // Home chi lay section dau (Phim Sex Moi Cap Nhat), cat tu section thu 2
    public static string FirstSectionOnly(string html)
    {
        if (string.IsNullOrEmpty(html))
            return html;
        int first = html.IndexOf("header-list-index");
        if (first < 0)
            return html;
        int second = html.IndexOf("header-list-index", first + 1);
        if (second < 0)
            return html;
        return html.Substring(0, second);
    }

    public static List<PlaylistItem> Playlist(string uri, string html)
    {
        var playlists = new List<PlaylistItem>();
        var seen = new HashSet<string>();
        
        // Strip \r to handle Windows line endings
        html = html.Replace("\r", "");

        // Chi card chinh (m-block movie-item) - BO sidebar top-film (list-top-movie-link)
        // vi sidebar lap lai tren moi trang gay trung noi dung khi phan trang
        foreach (Match m in Regex.Matches(html, @"<a\s+[^>]*class=""[^""]*movie-item[^""]*""[^>]*href=""(https://x\.sexdep\.co\.uk/phim/[^""]+)""[^>]*title=""([^""]+)""", RegexOptions.Singleline))
        {
            string slug = m.Groups[1].Value;
            string href = slug;
            if (href.StartsWith("https://x.sexdep.co.uk"))
                href = href.Substring("https://x.sexdep.co.uk".Length);
            
            if (!seen.Add(href))
                continue;

            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            string poster = "";
            int start = m.Index;
            int len = Math.Min(3000, html.Length - start);
            if (len > 0)
            {
                string chunk = html.Substring(start, len);
                // Poster nam trong div lazyload data-original (relative) hoac og:image
                var thumbLazy = Regex.Match(chunk, @"data-original=""(/storage/[^""]+\.(?:webp|jpg|jpeg|png|avif))""");
                if (!thumbLazy.Success)
                    thumbLazy = Regex.Match(chunk, @"data-original=""(/storage/[^""]+)""");
                var thumbImg = Regex.Match(chunk, @"<img[^>]*src=""(/storage/[^""]+)""");
                if (!thumbImg.Success)
                    thumbImg = Regex.Match(chunk, @"<img[^>]*src=""([^""]+)""");

                if (thumbLazy.Success)
                    poster = thumbLazy.Groups[1].Value;
                else if (thumbImg.Success)
                    poster = thumbImg.Groups[1].Value;
                else
                {
                    // Sidebar top-film-week dung background-image:url('...') thay vi img
                    var bgImg = Regex.Match(chunk, @"background-image\s*:\s*url\(['""]?(/storage/[^'""\)]+)['""]?\)");
                    if (bgImg.Success)
                        poster = bgImg.Groups[1].Value;
                }

                // Relative -> absolute (app khong load duoc relative)
                if (poster.StartsWith("/"))
                    poster = SiteHost + poster;
            }

            playlists.Add(new PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "sexdep",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    public static Dictionary<string, string> StreamLinks(string html)
    {
        var stream_links = new Dictionary<string, string>(3);
        if (string.IsNullOrEmpty(html))
            return stream_links;

        // Pattern: data-link="/storage/m3u8/{slug}/index.m3u8" (relative, can kem host)
        var m3u8 = Regex.Match(html, @"data-link\s*=\s*""/storage/m3u8/([^""]+)""");
        if (!m3u8.Success)
            m3u8 = Regex.Match(html, @"link\s*=\s*""/storage/m3u8/([^""]+)""");
        if (m3u8.Success)
        {
            string path = m3u8.Groups[1].Value;
            stream_links.TryAdd("HLS", SiteHost + "/storage/m3u8/" + path.TrimStart('/'));
        }

        // Fallback: try data-source
        if (stream_links.Count == 0)
        {
            var dataSource = Regex.Match(html, @"data-source\s*=\s*['""]([^'""]+)['""]");
            if (dataSource.Success)
            {
                string embed = dataSource.Groups[1].Value.Replace("&amp;", "&");
                stream_links.TryAdd("HLS", embed);
            }
        }

        // Fallback: try iframe
        if (stream_links.Count == 0)
        {
            var iframe = Regex.Match(html, @"<iframe[^>]+src=[""']([^""']+)[""']");
            if (iframe.Success)
            {
                string src = iframe.Groups[1].Value;
                if (src.StartsWith("//"))
                    src = "https:" + src;
                else if (src.StartsWith("/"))
                    src = SiteHost + src;
                stream_links.TryAdd("embed", src);
            }
        }

        // Fallback: try file: pattern
        if (stream_links.Count == 0)
        {
            var filePattern = Regex.Match(html, @"""file""\s*:\s*""(https?://[^""]+)""");
            if (filePattern.Success)
            {
                string file = filePattern.Groups[1].Value.Replace("\\/", "/");
                string label = file.Contains(".m3u8") ? "HLS" : "MP4";
                stream_links.TryAdd(label, file);
            }
        }

        return stream_links.Count > 0 ? stream_links : null;
    }

    public static List<MenuItem> Menu(string host)
    {
        return new List<MenuItem>()
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/sexdep"
            },
            new MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = new List<MenuItem>()
                {
                    new("Mới nhất", host + "/sexdep"),
                    new("Việt Nam", host + "/sexdep?c=quoc-gia/viet-nam"),
                    new("JAV HD", host + "/sexdep?c=the-loai/jav-hd"),
                    new("JAV", host + "/sexdep?c=the-loai/jav"),
                    new("Không Che", host + "/sexdep?c=the-loai/sex-khong-che"),
                    new("Vietsub", host + "/sexdep?c=the-loai/vietsub"),
                    new("Trung Quốc", host + "/sexdep?c=quoc-gia/trung-quoc"),
                    new("Âu Mỹ", host + "/sexdep?c=quoc-gia/au-my"),
                    new("Hàn Quốc", host + "/sexdep?c=quoc-gia/han-quoc"),
                    new("Gái Xinh", host + "/sexdep?c=the-loai/gai-xinh"),
                }
            }
        };
    }
}