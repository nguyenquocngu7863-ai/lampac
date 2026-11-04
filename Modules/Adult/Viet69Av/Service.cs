using Shared;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Viet69Av;

public static class Viet69AvTo
{
    public const string SiteHost = "https://viet69av.com";

    public const string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    #region Uri
    // Site slugify (assets/js/media.js) truoc khi gui search: bo dau, space -> '-'.
    // /search/thu%20dam/ tra 404 vi server so khop path nguyen van => phai slugify
    // o phia module (search "thử dâm" -> "thu-dam").
    public static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sb = new System.Text.StringBuilder(text.Length + 8);

        foreach (char ch in text.Trim().Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            char c = ch;

            if (c == 'đ' || c == 'Đ')
                c = 'd';

            if (char.IsWhiteSpace(c))
            {
                sb.Append('-');
                continue;
            }

            sb.Append(c);
        }

        string s = sb.ToString().ToLowerInvariant();
        s = Regex.Replace(s, "-{2,}", "-");

        return s.Trim('-');
    }

    // Cấu trúc site:
    //   home        /                       -> /trang/N/
    //   category    /phim-sex-viet-nam/     -> /phim-sex-viet-nam/trang/N/
    //   search      /search/<từ khoá>/      -> /search/<từ khoá>/trang/N/
    // Từ khoá search giữ nguyên dấu cách (site tự nhận cả %20), KHÔNG slugify:
    // slugify làm mất kết quả của từ khoá có dấu.
    public static string Uri(string host, string search, string c, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;
        string h = (host ?? SiteHost).TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append(h).Append("/search/")
               .Append(System.Uri.EscapeDataString(Slugify(search))).Append('/');
        }
        else if (!string.IsNullOrWhiteSpace(c))
        {
            url.Append(h).Append('/').Append(c.Trim().Trim('/')).Append('/');
        }
        else
        {
            url.Append(h).Append('/');
        }

        if (pg > 1)
            url.Append("trang/").Append(pg).Append('/');

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("class=\"item-movie\"", html);
        if (rx.Count == 0)
            return new List<PlaylistItem>();

        var playlists = new List<PlaylistItem>(rx.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rx.Rows())
        {
            string link = row.Match("href=\"(https?://[^\"]+?\\.html)\"");
            if (string.IsNullOrWhiteSpace(link))
                continue;

            if (!seen.Add(link))
                continue;

            string title = row.Match("class=\"title-movie\"[^>]*>([^<]+)<");
            if (string.IsNullOrWhiteSpace(title))
                title = row.Match("alt=\"([^\"]+)\"");

            if (string.IsNullOrWhiteSpace(title))
                continue;

            string img = row.Match("data-original=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(img))
                img = row.Match("data-src=\"([^\"]+)\"");
            if (string.IsNullOrWhiteSpace(img))
                img = row.Match("src=\"(https?://[^\"]+\\.(?:jpg|jpeg|png|webp)[^\"]*)\"");

            playlists.Add(new PlaylistItem()
            {
                name = WebUtility.HtmlDecode(title).Trim(),
                video = $"{uri}?uri={WebUtility.UrlEncode(link)}",
                picture = img,
                json = true,
                related = true,
                bookmark = new Bookmark()
                {
                    site = "viet69av",
                    href = link,
                    image = img
                }
            });
        }

        return playlists;
    }

    // Tong so trang lay tu khoi phan trang: .../trang/132/
    public static int Pages(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 0;

        int max = 0;
        foreach (Match m in Regex.Matches(html, "/trang/(\\d+)/"))
        {
            if (int.TryParse(m.Groups[1].Value, out int n) && n > max)
                max = n;
        }

        return max;
    }
    #endregion

    #region Menu
    private static List<(string title, string url)> _cats;
    private static List<(string title, string url)> _recent;
    private static DateTime _catsTime = DateTime.MinValue;

    public static List<MenuItem> Menu(string host, string search)
    {
        string url = $"{(host ?? SiteHost).TrimEnd('/')}/viet69av";

        var menu = new List<MenuItem>
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "viet69av",
                playlist_url = url
            }
        };

        var (cats, recent) = GetTaxonomy();

        if (cats != null && cats.Count > 0)
            menu.Add(SubMenu("Thể loại", url, cats, "c"));

        if (recent != null && recent.Count > 0)
            menu.Add(SubMenu("Tìm kiếm gần đây", url, recent, "search"));

        return menu;
    }

    static MenuItem SubMenu(string title, string url, List<(string title, string url)> items, string param)
    {
        var submenu = new List<MenuItem>(items.Count);
        foreach (var (t, v) in items)
        {
            submenu.Add(new MenuItem()
            {
                title = t,
                playlist_url = $"{url}?{param}={HttpUtility.UrlEncode(v)}"
            });
        }

        return new MenuItem()
        {
            title = title,
            playlist_url = "submenu",
            submenu = submenu
        };
    }

    static (List<(string title, string url)> cats, List<(string title, string url)> recent) GetTaxonomy()
    {
        if (_cats != null && (DateTime.UtcNow - _catsTime).TotalMinutes < 720)
            return (_cats, _recent);

        var cats = new List<(string, string)>();
        var recent = new List<(string, string)>();

        try
        {
            string html = Http.Get(SiteHost + "/", timeoutSeconds: 12, httpversion: 1,
                headers: HeadersModel.Init(
                    ("User-Agent", ChromeUA),
                    ("Referer", SiteHost + "/"))).GetAwaiter().GetResult();

            if (!string.IsNullOrEmpty(html))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // menu ngang: <nav class="list-menu"> ... <a href="https://viet69av.com/<slug>/" title="...">
                foreach (Match m in Regex.Matches(html,
                    "<a href=\"https?://viet69av\\.com/([a-z0-9\\-]+)/\" title=\"([^\"]+)\"",
                    RegexOptions.IgnoreCase))
                {
                    string slug = m.Groups[1].Value;
                    string title = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();

                    if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(title))
                        continue;

                    if (!seen.Add(slug))
                        continue;

                    cats.Add((title, slug));
                }

                // khoi "Tim kiem gan day": <div class="recent-search"> <a href="/search/<q>/">
                int ri = html.IndexOf("recent-search", StringComparison.OrdinalIgnoreCase);
                if (ri > 0)
                {
                    int end = html.IndexOf("</div>", ri, StringComparison.OrdinalIgnoreCase);
                    string block = end > ri ? html.Substring(ri, end - ri) : html.Substring(ri, Math.Min(4000, html.Length - ri));

                    var seenQ = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match m in Regex.Matches(block,
                        "<a href=\"[^\"]*?/search/([^/\"]+)/\"[^>]*>([^<]+)</a>",
                        RegexOptions.IgnoreCase))
                    {
                        string q = HttpUtility.UrlDecode(m.Groups[1].Value).Trim();
                        string title = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();

                        if (string.IsNullOrEmpty(q) || string.IsNullOrEmpty(title))
                            continue;

                        if (!seenQ.Add(q))
                            continue;

                        recent.Add((title, q));
                    }
                }
            }
        }
        catch { }

        if (cats.Count == 0)
        {
            // fallback tinh: 5 the loai co dinh tren menu ngang
            cats.AddRange(new (string, string)[]
            {
                ("Sex Việt", "phim-sex-viet-nam"),
                ("Sex Nhật", "phim-sex-nhat-ban"),
                ("Sex Trung", "phim-sex-trung-quoc"),
                ("Sex Âu", "phim-sex-chau-au"),
                ("Vietsub", "phim-sex-vietsub")
            });
        }

        _cats = cats;
        _recent = recent;
        _catsTime = DateTime.UtcNow;

        return (cats, recent);
    }
    #endregion

    #region Stream
    // Trang detail khong tra link san: <div id="video" data-video-id="4112">
    // + danh sach server <li class="video-server" data-server-id="1">
    // Link nam sau POST /api/player { id, server } -> JSON:
    //   { status:"success", data:{ type:"jwplayer", sources:[{file, type:"hls"}] } }
    // hoac { type:"iframe", source:"https://..." } (khong resolve duoc -> bo qua)
    //
    // Luu y CDN: segment nam tren *.tiktokcdn.com dat ten *.image va bi boc
    // header PNG o dau TS. hls.js tu dong "backtrack to sync byte" nen van phat
    // binh thuong (da do bang Chrome that: FRAG_PARSED, currentTime > 0).
    public static async Task<StreamItem> StreamLinks(string detailUrl, string html, WebProxy proxy = null, int timeoutSeconds = 12)
    {
        var res = new StreamItem()
        {
            qualitys = new Dictionary<string, string>(),
            recomends = new List<PlaylistItem>()
        };

        if (string.IsNullOrEmpty(html))
            return res;

        string id = Regex.Match(html, "id=\"video\"[^>]*data-video-id=\"(\\d+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(id))
            id = Regex.Match(html, "data-video-id=\"(\\d+)\"").Groups[1].Value;

        if (string.IsNullOrEmpty(id))
            return res;

        var servers = new List<string>();
        foreach (Match m in Regex.Matches(html, "class=\"video-server[^\"]*\"[^>]*data-server-id=\"(\\d+)\"", RegexOptions.IgnoreCase))
        {
            if (!servers.Contains(m.Groups[1].Value))
                servers.Add(m.Groups[1].Value);
        }

        if (servers.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, "data-server-id=\"(\\d+)\""))
            {
                if (!servers.Contains(m.Groups[1].Value))
                    servers.Add(m.Groups[1].Value);
            }
        }

        if (servers.Count == 0)
            servers.Add("1");

        // API phai goi CUNG origin voi trang detail. Site doi domain (viet69av.com ->
        // viet69hi.com): link detail tro sang host moi, con /api/player o host cu tra
        // 404 HTML -> parse JSON fail -> 503 stream_links. Lay origin tu detailUrl
        // thay vi hardcode SiteHost de con doi domain lan nua van chay.
        string origin = SiteHost;
        if (System.Uri.TryCreate(detailUrl, System.UriKind.Absolute, out var detailUri))
            origin = detailUri.Scheme + "://" + detailUri.Authority;

        var api = origin + "/api/player";

        foreach (string server in servers)
        {
            string json = null;

            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>()
                {
                    ["id"] = id,
                    ["server"] = server
                });

                json = await Http.Post(
                    api,
                    content,
                    timeoutSeconds: Math.Max(8, timeoutSeconds),
                    headers: HeadersModel.Init(
                        ("User-Agent", ChromeUA),
                        ("Referer", detailUrl),
                        ("Origin", origin),
                        ("X-Requested-With", "XMLHttpRequest"),
                        ("Accept", "application/json, text/javascript, */*; q=0.01")),
                    proxy: proxy,
                    httpversion: 1,
                    statusCodeOK: true,
                    disposeData: true);
            }
            catch { json = null; }

            if (string.IsNullOrEmpty(json))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var st)
                    && st.ValueKind == JsonValueKind.String
                    && st.GetString() != "success")
                    continue;

                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    continue;

                string type = data.TryGetProperty("type", out var tp) && tp.ValueKind == JsonValueKind.String
                    ? tp.GetString() : null;

                if (type == "jwplayer"
                    && data.TryGetProperty("sources", out var sources)
                    && sources.ValueKind == JsonValueKind.Array)
                {
                    int i = 0;
                    int total = sources.GetArrayLength();

                    foreach (var src in sources.EnumerateArray())
                    {
                        string file = src.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String
                            ? f.GetString() : null;

                        if (string.IsNullOrWhiteSpace(file))
                            continue;

                        string key = total > 1 ? $"server{server}-{i + 1}" : "auto";

                        if (!res.qualitys.ContainsKey(key))
                            res.qualitys[key] = file;

                        i++;
                    }
                }
            }
            catch { }
        }

        return res;
    }
    #endregion
}
