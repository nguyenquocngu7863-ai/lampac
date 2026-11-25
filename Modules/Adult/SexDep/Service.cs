using Shared;
using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace SexDep;

public static class SexDepTo
{
    // 2026-11-25: site doi domain `z.sexdep.co.uk` -> `sexngot.blog` (va `z.` da chet
    // TLS server-side: m3u8 + anh deu fail). Phong mirror doi tiep: poster lay theo
    // host cua card, con stream lay theo host cua trang detail (Playlist/StreamLinks).
    public static string SiteHost = "https://sexngot.blog";

    // Do 5 lan thi chi 1-2 lan tra 200, con lai treo het 15s. Can 4 lan
    // thu moi chac co phim. Mot lan la khong du — 2/3 so lan manh hinh trong.
    public const int FetchAttempts = 4;

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
            // Phai co dau `/` CUOI: site tra 404/redirect neu thieu.
            // `the-loai/jav-hd` -> `/the-loai/jav-hd/`
            string url = host + "/" + c.Trim('/') + "/";
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
        // KHONG hardcode host va KHONG dua thuoc tinh vao dung
        // thu tu class -> href -> title. Site render 2 template khac nhau:
        // trang chu hop, trang the-loai /danh-sach thi doi thu tu hoac them
        // `data-*` o giua => regex cu tra 0 phim, manh hinh trong.
        // Quay tung the <a> roi doc tung thuoc tinh, khong phu thuoc thu tu.
        var tagRe = new Regex(@"<a\s[^>]*>", RegexOptions.Singleline);
        var hrefRe = new Regex(@"href\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        var classRe = new Regex(@"class\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        var titleRe = new Regex(@"title\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);

        foreach (Match tag in tagRe.Matches(html))
        {
            string tagHtml = tag.Value;

            var cm = classRe.Match(tagHtml);
            if (!cm.Success || cm.Groups[1].Value.IndexOf("movie-item", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var hm = hrefRe.Match(tagHtml);
            if (!hm.Success)
                continue;

            // `/phim/...` la duong dan phim. Cho phep domain bat ky (site doi
            // `x.`/`z.` giua cac luc) va ca duong dan tuyet doi.
            string href = hm.Groups[1].Value;
            int slash = href.IndexOf("/phim/", StringComparison.OrdinalIgnoreCase);
            if (slash < 0)
                continue;

            Match tm = titleRe.Match(tagHtml);
            if (!tm.Success)
                continue;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(name))
                continue;

            if (!seen.Add(href))
                continue;

            string poster = "";
            int start = tag.Index;
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

                // Relative -> absolute (app khong load duoc relative).
                // Base lay theo host cua chinh card (mirror `s.`/`z.` doi nhau), khong
                // dung SiteHost co dinh — `z.` co luc chet TLS lam anh trang.
                string baseHost = SiteHost;
                if (href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && System.Uri.TryCreate(href, System.UriKind.Absolute, out System.Uri cardUri))
                    baseHost = cardUri.Scheme + "://" + cardUri.Host;

                if (poster.StartsWith("/"))
                    poster = baseHost + poster;
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

    static string Origin(string url)
    {
        if (string.IsNullOrEmpty(url))
            return SiteHost;
        url = url.TrimEnd('/');
        int i = url.IndexOf("://", StringComparison.Ordinal);
        if (i < 0)
            return SiteHost;
        int slash = url.IndexOf('/', i + 3);
        return slash > 0 ? url.Substring(0, slash) : url;
    }

    // Danh sach server: <a class="server" data-id data-link data-type="m3u8|embed">
    //  - `/storage/m3u8/...`  -> HLS cua site (host theo trang detail, mirror nao cung vay)
    //  - `blogger.com/video.g`-> embed Blogger, phai boc them 1 lop (BloggerMp4)
    public static Dictionary<string, string> StreamLinks(string html, string origin = null)
    {
        var stream_links = new Dictionary<string, string>(3);
        if (string.IsNullOrEmpty(html))
            return stream_links;

        origin = Origin(origin);

        foreach (Match tag in Regex.Matches(html, @"<a\s[^>]*data-link\s*=[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var lm = Regex.Match(tag.Value, @"data-link\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!lm.Success)
                continue;

            string link = HttpUtility.HtmlDecode(lm.Groups[1].Value.Trim());
            if (string.IsNullOrEmpty(link) || link.Contains("${"))
                continue;

            if (link.StartsWith("/storage/m3u8/", StringComparison.OrdinalIgnoreCase))
                stream_links.TryAdd("HLS", origin + link);
            else if (link.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0)
                stream_links.TryAdd("HLS", link.StartsWith("/", StringComparison.Ordinal) ? origin + link : link);
            else if (link.IndexOf("blogger.com/video.g", StringComparison.OrdinalIgnoreCase) >= 0)
                stream_links.TryAdd("Blogger", link);
            else if (link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                stream_links.TryAdd("embed", link);
        }

        // Fallback: data-source
        if (stream_links.Count == 0)
        {
            var dataSource = Regex.Match(html, @"data-source\s*=\s*['""]([^'""]+)['""]");
            if (dataSource.Success && !dataSource.Groups[1].Value.Contains("${"))
            {
                string embed = dataSource.Groups[1].Value.Replace("&amp;", "&");
                stream_links.TryAdd("embed", embed.StartsWith("//") ? "https:" + embed : embed);
            }
        }

        // Fallback: iframe that (bo qua template `${link}` cua site)
        if (stream_links.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, @"<iframe[^>]+src=[""']([^""']+)[""']", RegexOptions.IgnoreCase))
            {
                string src = m.Groups[1].Value;
                if (string.IsNullOrEmpty(src) || src.Contains("${"))
                    continue;
                if (src.StartsWith("//", StringComparison.Ordinal))
                    src = "https:" + src;
                else if (src.StartsWith("/", StringComparison.Ordinal))
                    src = origin + src;
                if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    stream_links.TryAdd("embed", src);
                    break;
                }
            }
        }

        // Fallback: jwplayer "file": "..."
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

    // https://www.blogger.com/video.g?token=<TOKEN>&origin=  ->  <TOKEN>
    public static string BloggerToken(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var m = Regex.Match(url, @"[?&]token=([^&]+)", RegexOptions.IgnoreCase);
        return m.Success ? HttpUtility.UrlDecode(m.Groups[1].Value) : null;
    }

    public const string BatchUrl =
        "https://www.blogger.com/_/BloggerVideoPlayerUi/data/batchexecute"
        + "?rpcids=WcwnYd&source-path=%2Fvideo.g&hl=en-US&rt=c";

    // f.req=[[[["WcwnYd","[\"<TOKEN>\",null,0]",null,"generic"]]]]&
    public static string BatchBody(string token)
    {
        string inner = "[\"" + token + "\",null,0]";
        string fReq = "[[[\"WcwnYd\"," + JsonSerializer.Serialize(inner) + ",null,\"generic\"]]]";
        return "f.req=" + HttpUtility.UrlEncode(fReq) + "&";
    }

    // batchexecute -> URL googlevideo **tai duoc bang HTTP thuong** (do 2026-11-25:
    // 206 video/mp4). Khac han URL nhung trong HTML trang video.g: ban do chi qua
    // duoc TLS gia lap trinh duyet (curl_cffi), con .NET bi 403.
    //
    // resp la XSSI:  )]}'\n\n<len>\n[["wrb.fr","WcwnYd","<json dang string>"],…]
    // inner = [1,null,[[url,[itag]],…],thumb,"BLOGGER-video-…",id]
    //   itag 22 = 720p, 18 = 360p (ca hai la MP4 goc).
    public static string BloggerBestUrl(string resp)
    {
        if (string.IsNullOrEmpty(resp))
            return null;

        try
        {
            int p = resp.IndexOf(")]}'", StringComparison.Ordinal);
            string body = p >= 0 ? resp.Substring(p + 4) : resp;

            int s = body.IndexOf('[');
            if (s < 0)
                return null;
            body = body.Substring(s);

            // ParseValue dung reader (khong phai Parse) vi phia sau con du lieu thua
            var rd = new Utf8JsonReader(Encoding.UTF8.GetBytes(body));
            using var root = JsonDocument.ParseValue(ref rd);

            if (root.RootElement.ValueKind != JsonValueKind.Array
                || root.RootElement.GetArrayLength() == 0)
                return null;

            string inner = root.RootElement[0][2].GetString();
            if (string.IsNullOrEmpty(inner))
                return null;

            var rd2 = new Utf8JsonReader(Encoding.UTF8.GetBytes(inner));
            using var doc = JsonDocument.ParseValue(ref rd2);

            string best = null;
            int bestItag = 0;

            foreach (var it in doc.RootElement[2].EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Array || it.GetArrayLength() < 2)
                    continue;

                string url = it[0].GetString();
                int itag = it[1][0].GetInt32();

                if (string.IsNullOrEmpty(url))
                    continue;

                // 720p > 360p > bat ky
                int rank = itag == 22 ? 2 : itag == 18 ? 1 : 0;
                if (best == null || rank > bestItag)
                {
                    best = url;
                    bestItag = rank;
                }
            }

            return best;
        }
        catch { return null; }
    }

    // ===== POPUP (genres + ?rel=) =====

    // Host hop le cho uri detail (chan SSRF). Site doi domain/mirror lien tuc:
    // `z.`/`s.sexdep.co.uk` -> `sexngot.blog` (2026-11-25), nen nhan ca host cu de
    // bookmark/cache cu con mo duoc.
    static readonly string[] DetailHosts = { "sexngot.blog", "sexdep.co.uk" };

    public static bool IsDetailHost(string hostName)
    {
        if (string.IsNullOrEmpty(hostName))
            return false;
        hostName = hostName.ToLowerInvariant();
        foreach (var h in DetailHosts)
        {
            if (hostName == h || hostName.EndsWith("." + h, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // uri -> URL detail day du; null = khong hop le (host la / khong phai trang phim)
    public static string DetailUrl(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string u = uri.Trim();

        if (u.StartsWith("//", StringComparison.Ordinal))
            u = "https:" + u;

        if (u.StartsWith("/", StringComparison.Ordinal))
            return SiteHost + u;

        if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || !System.Uri.TryCreate(u, System.UriKind.Absolute, out System.Uri parsed))
            return null;

        if (!IsDetailHost(parsed.Host))
            return null;

        if (!parsed.AbsolutePath.StartsWith("/phim/", StringComparison.OrdinalIgnoreCase))
            return null;

        return u;
    }

    public static string DetailPath(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)
            || !System.Uri.TryCreate(uri.Trim(), System.UriKind.Absolute, out System.Uri parsed))
            return null;
        return parsed.AbsolutePath.TrimEnd('/');
    }

    static void AddLinks(string block, string kind, List<(string name, string path)> into, HashSet<string> seen)
    {
        foreach (Match tag in Regex.Matches(block, @"<a\s[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var hm = Regex.Match(tag.Value, @"href\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!hm.Success)
                continue;

            string href = HttpUtility.HtmlDecode(hm.Groups[1].Value);
            int i = href.IndexOf("/" + kind + "/", StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                continue;

            string slug = href.Substring(i + kind.Length + 2).Trim('/');
            if (string.IsNullOrEmpty(slug) || slug.IndexOf('/') >= 0 || slug.IndexOf('?') >= 0)
                continue;

            var tm = Regex.Match(tag.Value, @"title\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
            string name = tm.Success ? HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim()) : "";

            if (string.IsNullOrEmpty(name))
                name = slug.Replace('-', ' ');

            if (!seen.Add(kind + "/" + slug))
                continue;

            into.Add((name, kind + "/" + slug));
        }
    }

    // 1 lan fetch detail -> chips: Quoc gia (BreadcrumbList JSON-LD, trang detail
    // khong in khoi "Quoc gia" nao khac) + The loai / Tu khoa / Dien vien (div#extras).
    public static (List<(string name, string path)> countries,
                   List<(string name, string path)> genres,
                   List<(string name, string path)> tags)
        Chips(string html)
    {
        var countries = new List<(string, string)>();
        var genres = new List<(string, string)>();
        var tags = new List<(string, string)>();

        if (string.IsNullOrEmpty(html))
            return (countries, genres, tags);

        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html, @"<div id=""extras"">(.*?)</div>", RegexOptions.Singleline))
        {
            string block = m.Groups[1].Value;
            string label = Regex.Replace(block, @"<[^>]+>", " ").Trim();

            if (label.IndexOf("Thể loại", StringComparison.OrdinalIgnoreCase) >= 0)
                AddLinks(block, "the-loai", genres, seen);
            else if (label.IndexOf("Từ khóa", StringComparison.OrdinalIgnoreCase) >= 0
                  || label.IndexOf("Từ khoá", StringComparison.OrdinalIgnoreCase) >= 0)
                AddLinks(block, "tu-khoa", tags, seen);
            else if (label.IndexOf("Diễn viên", StringComparison.OrdinalIgnoreCase) >= 0)
                AddLinks(block, "dien-vien", tags, seen);
        }

        var bc = Regex.Match(html, @"<script type=""application/ld\+json"">[^<]*BreadcrumbList.*?</script>", RegexOptions.Singleline);
        if (bc.Success)
        {
            string json = bc.Value.Replace("\\/", "/");
            foreach (Match m in Regex.Matches(json, @"""name""\s*:\s*""([^""]*)""\s*,\s*""item""\s*:\s*""([^""]*)"""))
            {
                string name = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
                string item = m.Groups[2].Value;
                int i = item.IndexOf("/quoc-gia/", StringComparison.OrdinalIgnoreCase);
                if (i < 0)
                    continue;
                string slug = item.Substring(i + 10).Trim('/');
                if (string.IsNullOrEmpty(slug) || slug.IndexOf('/') >= 0 || string.IsNullOrEmpty(name))
                    continue;
                if (!seen.Add("quoc-gia/" + slug))
                    continue;
                countries.Add((name, "quoc-gia/" + slug));
            }
        }

        return (countries, genres, tags);
    }

    // Khoi "Co the ban thich?" = ul#more-movie cuoi trang detail (site KHONG co block
    // class related/goi y nao khac). Bo chinh phim dang xem.
    public static List<PlaylistItem> Related(string html, string selfUri)
    {
        if (string.IsNullOrEmpty(html))
            return new List<PlaylistItem>();

        int box = -1;

        int mark = html.IndexOf("Có thể bạn thích", StringComparison.Ordinal);
        if (mark >= 0)
            box = html.IndexOf("<ul", mark, StringComparison.OrdinalIgnoreCase);

        if (box < 0)
        {
            int id = html.IndexOf("more-movie", StringComparison.OrdinalIgnoreCase);
            if (id < 0)
                return new List<PlaylistItem>();
            box = html.LastIndexOf("<ul", id, StringComparison.OrdinalIgnoreCase);
        }

        if (box < 0)
            return new List<PlaylistItem>();

        int end = html.IndexOf("</ul>", box, StringComparison.OrdinalIgnoreCase);
        string block = end > box ? html.Substring(box, end - box) : html.Substring(box);

        var items = Playlist("sexdep/vidosik", block) ?? new List<PlaylistItem>();

        string self = DetailPath(selfUri);
        if (!string.IsNullOrEmpty(self))
            items.RemoveAll(x => string.Equals(DetailPath(x.bookmark?.href), self, StringComparison.OrdinalIgnoreCase));

        return items;
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