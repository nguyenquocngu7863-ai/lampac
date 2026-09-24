using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace JavGuru;

// Parse tinh (list / trang phim / menu). Khong phu thuoc HttpContext de test doc lap.
public static class JavGuruTo
{
    public const string SiteHost = "https://jav.guru";

    public static string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

    const RegexOptions RxOpt = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    #region Uri
    public static string Uri(string host, string search, string c, int pg)
    {
        host = string.IsNullOrEmpty(host) ? SiteHost : host.TrimEnd('/');
        pg = Math.Max(1, pg);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = HttpUtility.UrlEncode(search.Trim());
            return pg > 1 ? $"{host}/page/{pg}/?s={q}" : $"{host}/?s={q}";
        }

        if (!string.IsNullOrWhiteSpace(c))
        {
            string path = c.Trim().Trim('/');
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return path.TrimEnd('/') + "/" + (pg > 1 && !IsRank(c) ? $"page/{pg}/" : "");

            // BXH xem nhieu: 1 trang duy nhat, cat trang o Playlist
            if (IsRank(path))
                return $"{host}/{path}/";

            return $"{host}/{path}/" + (pg > 1 ? $"page/{pg}/" : "");
        }

        return pg > 1 ? $"{host}/page/{pg}/" : $"{host}/";
    }

    public static bool IsRank(string c)
        => !string.IsNullOrEmpty(c) && c.Contains("most-watched-rank", StringComparison.OrdinalIgnoreCase);
    #endregion

    #region Poster
    // cdn.javmiku.com dinh Cloudflare challenge; cung path /wp-content tren jav.guru tra 200
    public static string FixPoster(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        url = HttpUtility.HtmlDecode(url.Trim());
        if (url.StartsWith("//"))
            url = "https:" + url;
        else if (url.StartsWith("/"))
            url = SiteHost + url;
        else if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = SiteHost + "/" + url;

        return Regex.Replace(url, @"^https?://cdn\.javmiku\.com/", SiteHost + "/", RegexOptions.IgnoreCase);
    }
    #endregion

    #region Playlist
    static readonly Regex PostHrefRx = new(@"href\s*=\s*[""'](?<u>(?:https?://(?:www\.)?jav\.guru)?/(?<id>\d{4,})/(?!\d{2}/)[^""'#?\s]*)[""']", RxOpt);
    static readonly Regex ImgTagRx = new(@"<img\b[^>]*>", RxOpt);
    static readonly Regex H2LinkRx = new(@"<h[1-4][^>]*>\s*<a\b[^>]*>(?<t>.*?)</a>", RxOpt);
    static readonly Regex TagRx = new(@"<[^>]+>", RxOpt);

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, @"\s" + Regex.Escape(name) + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RxOpt);
        return m.Success ? m.Groups["v"].Value : null;
    }

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return null;
        s = HttpUtility.HtmlDecode(TagRx.Replace(s, " "));
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length == 0 ? null : s;
    }

    static string AbsPost(string href)
    {
        href = HttpUtility.HtmlDecode(href);
        if (href.StartsWith("/"))
            href = SiteHost + href;
        href = Regex.Replace(href, @"^https?://(?:www\.)?jav\.guru", SiteHost, RegexOptions.IgnoreCase);
        return href.EndsWith("/") ? href : href + "/";
    }

    static Shared.Models.SISI.Base.PlaylistItem Card(string uri, string block, HashSet<string> seen)
    {
        var hm = PostHrefRx.Match(block);
        if (!hm.Success)
            return null;

        string href = AbsPost(hm.Groups["u"].Value);
        if (!seen.Add(hm.Groups["id"].Value))
            return null;

        string poster = null, alt = null;
        var im = ImgTagRx.Match(block);
        if (im.Success)
        {
            string tag = im.Value;
            poster = FixPoster(Attr(tag, "data-src") ?? Attr(tag, "data-lazy-src") ?? Attr(tag, "data-original") ?? Attr(tag, "src"));
            alt = Attr(tag, "alt");
        }

        string name = null;
        var h2 = H2LinkRx.Match(block);
        if (h2.Success)
            name = Clean(h2.Groups["t"].Value);

        if (string.IsNullOrEmpty(name))
        {
            foreach (Match am in Regex.Matches(block, @"<a\b[^>]*>", RxOpt))
            {
                string t = Attr(am.Value, "title");
                if (!string.IsNullOrWhiteSpace(t) && PostHrefRx.IsMatch(am.Value))
                {
                    name = Clean(t);
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            // BXH: <a href=post>ten phim</a> -> lay text dai nhat
            foreach (Match am in Regex.Matches(block, @"<a\b(?<a>[^>]*)>(?<t>.*?)</a>", RxOpt))
            {
                if (!PostHrefRx.IsMatch(am.Groups["a"].Value))
                    continue;
                string t = Clean(am.Groups["t"].Value);
                if (t != null && t.Length >= 5 && (name == null || t.Length > name.Length))
                    name = t;
            }
        }

        if (string.IsNullOrEmpty(name))
            name = Clean(alt);

        if (string.IsNullOrEmpty(name) || name.Length < 3)
            return null;

        return new Shared.Models.SISI.Base.PlaylistItem()
        {
            video = uri + "?uri=" + HttpUtility.UrlEncode(href),
            name = name,
            picture = poster ?? "",
            json = true,
            bookmark = new Shared.Models.SISI.Base.Bookmark()
            {
                site = "javguru",
                href = href,
                image = poster ?? ""
            }
        };
    }

    static IEnumerable<string> Blocks(string html, string marker)
    {
        int idx = 0;
        var starts = new List<int>();
        while ((idx = html.IndexOf(marker, idx, StringComparison.Ordinal)) >= 0)
        {
            starts.Add(idx);
            idx += marker.Length;
        }

        for (int i = 0; i < starts.Count; i++)
        {
            int end = i + 1 < starts.Count ? starts[i + 1] : Math.Min(html.Length, starts[i] + 6000);
            int len = Math.Min(end - starts[i], 6000);
            yield return html.Substring(starts[i], len);
        }
    }

    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string html, bool rank = false, int pg = 1)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return playlists;

        var seen = new HashSet<string>();

        // trang chu/search/category: div.inside-article ; BXH: .rank-item
        string[] markers = rank
            ? new[] { "rank-item", "inside-article" }
            : new[] { "class=\"inside-article\"", "class='inside-article'", "inside-article", "rank-item" };

        foreach (string marker in markers)
        {
            foreach (string block in Blocks(html, marker))
            {
                // "nothing found" card cua WP
                if (block.Contains("Nothing Found", StringComparison.OrdinalIgnoreCase) && !PostHrefRx.IsMatch(block))
                    continue;

                var item = Card(uri, block, seen);
                if (item != null)
                    playlists.Add(item);
            }

            if (playlists.Count > 0)
                break;
        }

        if (rank)
        {
            // BXH tra ~100 item tren 1 trang -> cat 24/trang
            const int per = 24;
            playlists = playlists.Skip((Math.Max(1, pg) - 1) * per).Take(per).ToList();
        }

        return playlists;
    }

    // phim lien quan tren trang xem (div.woo-sc-related-posts)
    public static List<Shared.Models.SISI.Base.PlaylistItem> Related(string uri, string html)
    {
        var list = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        int i = html.IndexOf("woo-sc-related-posts", StringComparison.Ordinal);
        if (i < 0)
            return list;

        string part = html.Substring(i, Math.Min(html.Length - i, 30000));
        var seen = new HashSet<string>();
        foreach (string block in Blocks(part, "<li"))
        {
            var item = Card(uri, block, seen);
            if (item != null)
                list.Add(item);
            if (list.Count >= 12)
                break;
        }

        return list;
    }
    #endregion

    #region Mirrors
    public class Mirror
    {
        public string key { get; set; }

        public string label { get; set; }

        public string url { get; set; }
    }

    static string B64(string s)
    {
        if (string.IsNullOrEmpty(s))
            return null;
        try
        {
            s = s.Replace("\\/", "/").Replace('-', '+').Replace('_', '/').Trim();
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch
        {
            return null;
        }
    }

    // trang phim -> danh sach /searcho/?xd=..|ud=..|td=..|cd=..|hd=..|od=..
    public static List<Mirror> Mirrors(string html)
    {
        var list = new List<Mirror>();
        if (string.IsNullOrEmpty(html))
            return list;

        var buttons = new List<string>();
        foreach (Match m in Regex.Matches(html, @"<a\b[^>]*wp-btn-iframe__shortcode[^>]*>(?<t>.*?)</a>", RxOpt))
            buttons.Add(Clean(m.Groups["t"].Value));

        var seen = new HashSet<string>();
        void Add(string b64, string fallbackLabel)
        {
            string u = B64(b64);
            if (string.IsNullOrEmpty(u))
                return;
            u = u.Trim();
            if (u.StartsWith("//"))
                u = "https:" + u;
            else if (u.StartsWith("/"))
                u = SiteHost + u;
            if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase) || !seen.Add(u))
                return;

            var km = Regex.Match(u, @"[?&](?<k>[a-z])d=", RegexOptions.IgnoreCase);
            string key = km.Success ? km.Groups["k"].Value.ToLowerInvariant() : "";
            string label = list.Count < buttons.Count && !string.IsNullOrEmpty(buttons[list.Count]) ? buttons[list.Count] : fallbackLabel;

            list.Add(new Mirror() { key = key, label = label ?? ("Server " + (list.Count + 1)), url = u });
        }

        foreach (Match m in Regex.Matches(html, @"""iframe_url""\s*:\s*""(?<b>[^""]+)""", RxOpt))
            Add(m.Groups["b"].Value, null);

        return list;
    }
    #endregion

    #region Menu
    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        string u = host + "/javguru";

        var cats = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Không che (Uncensored)", u + "?c=category/jav-uncensored"),
            new("Giải mã (Decensored)", u + "?c=category/decensored"),
            new("Vietsub/Engsub", u + "?c=category/english-subbed"),
            new("Nghiệp dư (Amateur)", u + "?c=category/amateur"),
            new("Idol", u + "?c=category/idol"),
            new("FC2", u + "?c=category/fc2"),
            new("4K", u + "?c=category/4k"),
            new("1080p", u + "?c=category/1080p"),
            new("VR", u + "?c=category/vr-av"),
        };

        var tags = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("Vợ người ta", u + "?c=tag/married-woman"),
            new("Quý bà", u + "?c=tag/mature-woman"),
            new("Ngực to", u + "?c=tag/big-tits"),
            new("Mẹ kế", u + "?c=tag/stepmother"),
            new("Ngoại tình", u + "?c=tag/cheating-wife"),
            new("Nữ sinh", u + "?c=tag/school-girls"),
            new("Cô giáo", u + "?c=tag/female-teacher"),
            new("Văn phòng", u + "?c=tag/office-lady"),
            new("Y tá", u + "?c=tag/nurse"),
            new("Cosplay", u + "?c=tag/cosplay"),
            new("Creampie", u + "?c=tag/creampie"),
            new("Bukkake", u + "?c=tag/bukkake"),
            new("Gangbang", u + "?c=tag/gangbang"),
            new("Cưỡi ngựa (Cowgirl)", u + "?c=tag/cowgirl"),
            new("Pantyhose", u + "?c=tag/pantyhose"),
            new("Giúp việc (Maid)", u + "?c=tag/maid"),
            new("Cô dâu", u + "?c=tag/bride"),
            new("Gal", u + "?c=tag/gal"),
            new("Hardcore", u + "?c=tag/hardcore"),
            new("Nasty", u + "?c=tag/nasty"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = u
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới nhất",
                playlist_url = u
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Xem nhiều",
                playlist_url = u + "?c=most-watched-rank"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Danh mục",
                playlist_url = "submenu",
                submenu = cats
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = tags
            }
        };
    }
    #endregion
}

public class JavGuruStream
{
    public string label { get; set; }

    public string url { get; set; }

    public string referer { get; set; }

    public bool hls { get; set; }
}

// Chuoi resolve: /searcho/ (token data-* dao nguoc) -> 302 embed -> m3u8/mp4.
// Dung HttpClient rieng AllowAutoRedirect=false de tu doc Location tung hop.
public class JavGuruResolver
{
    const RegexOptions RxOpt = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    readonly HttpClient http;
    readonly string ua;
    public Action<string> log;

    public JavGuruResolver(HttpClient http, string ua = null, Action<string> log = null)
    {
        this.http = http;
        this.ua = ua ?? JavGuruTo.ChromeUA;
        this.log = log;
    }

    public static HttpClient CreateClient(WebProxy proxy = null)
    {
        var handler = new SocketsHttpHandler()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions()
            {
                RemoteCertificateValidationCallback = (a, b, c, d) => true
            }
        };

        if (proxy != null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }

        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }

    void Log(string msg)
    {
        try { log?.Invoke(msg); } catch { }
    }

    #region http
    public class Page
    {
        public int status;
        public string url;
        public string location;
        public string html;
    }

    static string Origin(string url)
    {
        try
        {
            var u = new System.Uri(url);
            return u.GetLeftPart(UriPartial.Authority);
        }
        catch
        {
            return null;
        }
    }

    static string Abs(string baseUrl, string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return null;
        link = link.Trim().Replace("\\/", "/");
        if (link.StartsWith("//"))
            return "https:" + link;
        if (link.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return link;
        try
        {
            return new System.Uri(new System.Uri(baseUrl), link).ToString();
        }
        catch
        {
            return null;
        }
    }

    HttpRequestMessage Req(HttpMethod method, string url, string referer)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("User-Agent", ua);
        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        if (!string.IsNullOrEmpty(referer))
            req.Headers.TryAddWithoutValidation("Referer", referer);
        return req;
    }

    // 1 request, khong follow redirect
    public async Task<Page> GetOnce(string url, string referer, CancellationToken ct)
    {
        try
        {
            using var req = Req(HttpMethod.Get, url, referer);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

            string location = null;
            if (resp.Headers.TryGetValues("Location", out var locs))
                location = locs.FirstOrDefault();
            if (string.IsNullOrEmpty(location))
                location = resp.Headers.Location?.OriginalString;

            string html = null;
            int code = (int)resp.StatusCode;
            if (code < 300 || code >= 400)
                html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return new Page() { status = code, url = url, location = string.IsNullOrEmpty(location) ? null : Abs(url, location), html = html };
        }
        catch (Exception ex)
        {
            Log($"GET fail {url}: {ex.Message}");
            return null;
        }
    }

    static readonly Regex JsRedirectRx = new(@"(?:window\.)?location(?:\.href)?\s*=\s*['""](?<u>https?://[^'""]+)['""]|<meta[^>]+http-equiv\s*=\s*['""]?refresh['""]?[^>]+url=(?<u>[^'"">\s]+)", RxOpt);

    // follow HTTP 30x + JS/meta redirect (toi da maxHops)
    public async Task<Page> GetFollow(string url, string referer, CancellationToken ct, int maxHops = 6, bool jsRedirect = true)
    {
        Page page = null;
        string current = url, refr = referer;
        for (int hop = 0; hop <= maxHops; hop++)
        {
            page = await GetOnce(current, refr, ct).ConfigureAwait(false);
            if (page == null)
                return null;

            string next = null;
            if (page.status >= 300 && page.status < 400)
                next = page.location;
            else if (jsRedirect && page.status == 200 && !string.IsNullOrEmpty(page.html) && page.html.Length < 20000)
            {
                var m = JsRedirectRx.Match(page.html);
                if (m.Success && !HasStream(page.html))
                    next = Abs(current, m.Groups["u"].Value);
                if (next != null && next.Contains("/sandbox"))
                    next = null;
            }

            if (string.IsNullOrEmpty(next) || next == current)
                return page;

            Log($"  -> {next}");
            refr = current;
            current = next;
        }

        return page;
    }

    async Task<string> PostJson(string url, string json, string referer, CancellationToken ct)
    {
        try
        {
            using var req = Req(HttpMethod.Post, url, referer);
            req.Headers.Remove("Accept");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            string origin = Origin(referer ?? url);
            if (origin != null)
                req.Headers.TryAddWithoutValidation("Origin", origin);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"POST fail {url}: {ex.Message}");
            return null;
        }
    }
    #endregion

    #region searcho
    static readonly Regex CfgCidRx = new(@"\bcid\s*:\s*['""](?<v>[^'""]+)['""]", RxOpt);
    static readonly Regex CfgBaseRx = new(@"\bbase\s*:\s*['""](?<v>[^'""]+)['""]", RxOpt);
    static readonly Regex CfgRtypeRx = new(@"\brtype\s*:\s*['""](?<v>[^'""]+)['""]", RxOpt);
    static readonly Regex CfgKeysRx = new(@"\bkeys\s*:\s*\[(?<v>[^\]]+)\]", RxOpt);

    static string Reverse(string s)
    {
        var arr = s.ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }

    // searcho loader -> url ?{r}r=<token dao nguoc>
    public static string SearchoTarget(string searchoUrl, string html)
    {
        if (!string.IsNullOrEmpty(html))
        {
            var cid = CfgCidRx.Match(html);
            var bas = CfgBaseRx.Match(html);
            var keys = CfgKeysRx.Match(html);
            if (cid.Success && bas.Success && keys.Success)
            {
                var rt = CfgRtypeRx.Match(html);
                string rtype = rt.Success ? rt.Groups["v"].Value : "x";
                var div = Regex.Match(html, @"<[a-z]+\b[^>]*\sid\s*=\s*['""]" + Regex.Escape(cid.Groups["v"].Value) + @"['""][^>]*>", RxOpt);
                if (div.Success)
                {
                    var token = new StringBuilder();
                    foreach (Match k in Regex.Matches(keys.Groups["v"].Value, @"['""](?<k>[^'""]+)['""]"))
                    {
                        var am = Regex.Match(div.Value, @"\s" + Regex.Escape(k.Groups["k"].Value) + @"\s*=\s*['""](?<v>[^'""]*)['""]", RxOpt);
                        if (am.Success)
                            token.Append(am.Groups["v"].Value);
                    }

                    if (token.Length > 0)
                    {
                        string baseUrl = Abs(searchoUrl, bas.Groups["v"].Value) ?? bas.Groups["v"].Value;
                        return baseUrl.TrimEnd('/') + "/?" + rtype + "r=" + Reverse(token.ToString());
                    }
                }
            }
        }

        // kieu cu: dao nguoc chinh gia tri ?xd= tren url
        var lm = Regex.Match(searchoUrl, @"^(?<base>[^?]+)\?(?:.*&)?(?<k>[a-z])d=(?<v>[^&]+)", RegexOptions.IgnoreCase);
        if (lm.Success)
            return lm.Groups["base"].Value + "?" + lm.Groups["k"].Value + "r=" + Reverse(HttpUtility.UrlDecode(lm.Groups["v"].Value));

        return null;
    }

    // tra ve (embedUrl, streamTrucTiep)
    public async Task<(string embed, JavGuruStream direct)> ResolveSearcho(string searchoUrl, string pageUrl, CancellationToken ct)
    {
        var loader = await GetOnce(searchoUrl, pageUrl, ct).ConfigureAwait(false);
        if (loader == null)
            return default;

        if (loader.status >= 300 && loader.status < 400 && !string.IsNullOrEmpty(loader.location) && !loader.location.Contains("/searcho/"))
            return (loader.location, null);

        string html = loader.html ?? "";

        // mot so loader nhung thang MIRROR/embed_src hoac m3u8
        var direct = ExtractStream(html, searchoUrl);
        if (direct != null)
            return (null, direct);

        var em = Regex.Match(html, @"(?:MIRROR|embed_src)\s*=\s*['""]?(?<u>https?://[^'""\s<>]+)", RxOpt);
        if (em.Success)
            return (em.Groups["u"].Value, null);

        var targets = new List<string>();
        string t1 = SearchoTarget(searchoUrl, html);
        if (t1 != null)
            targets.Add(t1);
        string t2 = SearchoTarget(searchoUrl, null);
        if (t2 != null && !targets.Contains(t2))
            targets.Add(t2);

        foreach (string target in targets)
        {
            Log($"  searcho target {target}");
            var r = await GetOnce(target, searchoUrl, ct).ConfigureAwait(false);
            if (r == null)
                continue;

            if (!string.IsNullOrEmpty(r.location))
            {
                if (r.location.Contains("/searcho/"))
                {
                    var f = await GetFollow(r.location, target, ct).ConfigureAwait(false);
                    if (f != null && !f.url.Contains("/searcho/"))
                        return (f.url, null);
                    continue;
                }
                return (r.location, null);
            }

            if (!string.IsNullOrEmpty(r.html))
            {
                var js = JsRedirectRx.Match(r.html);
                if (js.Success)
                {
                    string next = Abs(target, js.Groups["u"].Value);
                    if (next != null && !next.Contains("/searcho/"))
                        return (next, null);
                }

                var s = ExtractStream(r.html, target);
                if (s != null)
                    return (null, s);
            }
        }

        return default;
    }
    #endregion

    #region extract
    static readonly Regex M3u8Rx = new(@"(?<u>https?:(?:\\?/){2}[^""'\s<>]+?\.m3u8(?:\?[^""'\s<>]*)?)(?=[""'\s<>]|$)", RxOpt);
    static readonly Regex Mp4Rx = new(@"(?:file|src|source|urlPlay|video_url)\s*[:=]\s*['""](?<u>https?://[^'""]+?\.mp4(?:\?[^'""]*)?)['""]", RxOpt);
    static readonly Regex UrlPlayRx = new(@"urlPlay\s*=\s*['""](?<u>[^'""]+)['""]", RxOpt);
    static readonly Regex DataHashRx = new(@"data-hash\s*=\s*['""](?<u>https?://[^'""]+\.m3u8[^'""]*)['""]", RxOpt);
    static readonly Regex HlsKeyRx = new(@"[""']?(?<k>hls\d*)[""']?\s*:\s*[""'](?<u>[^""']+)[""']", RxOpt);
    static readonly Regex FileRx = new(@"file\s*:\s*[""'](?<u>[^""']+)[""']", RxOpt);
    static readonly Regex SourceTagRx = new(@"<source[^>]+src\s*=\s*['""](?<u>https?://[^'""]+)['""]", RxOpt);

    static bool HasStream(string html)
        => !string.IsNullOrEmpty(html) && (html.Contains(".m3u8") || html.Contains("urlPlay") || html.Contains("eval(function(p,a,c,k,e,d)"));

    static bool Junk(string u)
        => string.IsNullOrEmpty(u) || u.Contains("test-videos.co.uk") || u.Contains("/sandbox") || u.Contains("{");

    static JavGuruStream Make(string url, string pageUrl)
    {
        url = Abs(pageUrl, HttpUtility.HtmlDecode(url.Replace("\\u0026", "&").Replace("\\/", "/")));
        if (Junk(url))
            return null;
        string origin = Origin(pageUrl);
        return new JavGuruStream()
        {
            url = url,
            referer = origin != null ? origin + "/" : null,
            hls = url.Contains(".m3u8") || url.Contains("/hls") || url.Contains("master.txt")
        };
    }

    // Dean Edwards packer (p,a,c,k,e,d) -> source
    static readonly Regex PackRx = new(@"eval\(function\(p,a,c,k,e,[dr]\)\{.*?\}\(\s*'(?<p>(?:[^'\\]|\\.)*)'\s*,\s*(?<a>\d+)\s*,\s*(?<c>\d+)\s*,\s*'(?<k>(?:[^'\\]|\\.)*)'\.split\('\|'\)", RxOpt);

    static string ToBase(int n, int a)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (a < 2 || a > 62)
            a = Math.Clamp(a, 2, 62);
        if (n == 0)
            return "0";
        var sb = new StringBuilder();
        while (n > 0)
        {
            sb.Insert(0, chars[n % a]);
            n /= a;
        }
        return sb.ToString();
    }

    public static string Unpack(string html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("eval(function(p,a,c,k,e,"))
            return null;

        var sb = new StringBuilder();
        foreach (Match m in PackRx.Matches(html))
        {
            try
            {
                string p = m.Groups["p"].Value.Replace("\\'", "'").Replace("\\\\", "\\");
                int a = int.Parse(m.Groups["a"].Value);
                int c = int.Parse(m.Groups["c"].Value);
                string[] k = m.Groups["k"].Value.Split('|');

                var dict = new Dictionary<string, string>();
                for (int i = 0; i < c && i < k.Length; i++)
                {
                    if (!string.IsNullOrEmpty(k[i]))
                        dict[ToBase(i, a)] = k[i];
                }

                sb.Append(Regex.Replace(p, @"\b\w+\b", w => dict.TryGetValue(w.Value, out string v) ? v : w.Value));
                sb.Append('\n');
            }
            catch { }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    // tim stream trong html (literal / urlPlay / data-hash / packed / source)
    public static JavGuruStream ExtractStream(string html, string pageUrl)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var up = UrlPlayRx.Match(html);
        if (up.Success && (up.Groups["u"].Value.Contains(".mp4") || up.Groups["u"].Value.Contains(".m3u8")))
        {
            var s = Make(up.Groups["u"].Value, pageUrl);
            if (s != null)
                return s;
        }

        var dh = DataHashRx.Match(html);
        if (dh.Success)
        {
            var s = Make(dh.Groups["u"].Value, pageUrl);
            if (s != null)
                return s;
        }

        string unpacked = Unpack(html);
        if (!string.IsNullOrEmpty(unpacked))
        {
            // streamwish/javclan: var links={"hls4":"/stream/..","hls2":"https://..m3u8"}
            var hls = HlsKeyRx.Matches(unpacked).Cast<Match>()
                .Select(x => (k: x.Groups["k"].Value, u: x.Groups["u"].Value))
                .Where(x => x.u.Contains(".m3u8") || x.u.Contains("master") || x.u.StartsWith("/stream"))
                .OrderBy(x => x.u.StartsWith("http") ? 0 : 1)
                .ThenBy(x => x.k == "hls2" ? 0 : x.k == "hls4" ? 1 : 2)
                .ToList();
            foreach (var h in hls)
            {
                var s = Make(h.u, pageUrl);
                if (s != null)
                    return s;
            }

            var fm = FileRx.Match(unpacked);
            if (fm.Success && (fm.Groups["u"].Value.Contains(".m3u8") || fm.Groups["u"].Value.Contains(".mp4")))
            {
                var s = Make(fm.Groups["u"].Value, pageUrl);
                if (s != null)
                    return s;
            }

            var um = M3u8Rx.Match(unpacked);
            if (um.Success)
            {
                var s = Make(um.Groups["u"].Value, pageUrl);
                if (s != null)
                    return s;
            }
        }

        foreach (Match m in M3u8Rx.Matches(html))
        {
            var s = Make(m.Groups["u"].Value, pageUrl);
            if (s != null)
                return s;
        }

        var mp = Mp4Rx.Match(html);
        if (mp.Success)
        {
            var s = Make(mp.Groups["u"].Value, pageUrl);
            if (s != null)
                return s;
        }

        var st = SourceTagRx.Match(html);
        if (st.Success && (st.Groups["u"].Value.Contains(".mp4") || st.Groups["u"].Value.Contains(".m3u8")))
            return Make(st.Groups["u"].Value, pageUrl);

        return null;
    }

    // VOE: <script type="application/json">["..."]</script> -> rot13, bo junk, b64, -3, dao, b64 -> json
    public static JavGuruStream Voe(string html, string pageUrl)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"<script[^>]+type\s*=\s*['""]application/json['""][^>]*>\s*\[\s*""(?<v>[^""]+)""\s*\]\s*</script>", RxOpt);
        if (!m.Success)
            return null;

        try
        {
            var sb = new StringBuilder();
            foreach (char ch in m.Groups["v"].Value)
            {
                int x = ch;
                if (x >= 'A' && x <= 'Z')
                    x = (x - 'A' + 13) % 26 + 'A';
                else if (x >= 'a' && x <= 'z')
                    x = (x - 'a' + 13) % 26 + 'a';
                sb.Append((char)x);
            }

            string txt = sb.ToString();
            foreach (string junk in new[] { "@$", "^^", "~@", "%?", "*~", "!!", "#&" })
                txt = txt.Replace(junk, "");

            string step = Encoding.Latin1.GetString(Convert.FromBase64String(txt.PadRight(txt.Length + (4 - txt.Length % 4) % 4, '=')));
            var sh = new StringBuilder(step.Length);
            foreach (char ch in step)
                sh.Append((char)(ch - 3));
            string rev = Reverse(sh.ToString());
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(rev.PadRight(rev.Length + (4 - rev.Length % 4) % 4, '=')));

            var src = Regex.Match(json, @"""source""\s*:\s*""(?<u>[^""]+)""");
            if (src.Success)
                return Make(Regex.Unescape(src.Groups["u"].Value), pageUrl);

            var dau = Regex.Match(json, @"""direct_access_url""\s*:\s*""(?<u>[^""]+)""");
            if (dau.Success)
                return Make(Regex.Unescape(dau.Groups["u"].Value), pageUrl);
        }
        catch { }

        return null;
    }

    static readonly Random rnd = new Random();

    async Task<JavGuruStream> Dood(string html, string pageUrl, CancellationToken ct)
    {
        var pm = Regex.Match(html ?? "", @"/pass_md5/[^'""\s]+");
        if (!pm.Success)
            return null;

        string origin = Origin(pageUrl);
        var r = await GetOnce(origin + pm.Value, pageUrl, ct).ConfigureAwait(false);
        string baseUrl = r?.html?.Trim();
        if (string.IsNullOrEmpty(baseUrl) || !baseUrl.StartsWith("http"))
            return null;

        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var sb = new StringBuilder();
        lock (rnd)
        {
            for (int i = 0; i < 10; i++)
                sb.Append(chars[rnd.Next(chars.Length)]);
        }

        string token = pm.Value.TrimEnd('/').Split('/').Last();
        string url = baseUrl + sb + "?token=" + token + "&expiry=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new JavGuruStream() { url = url, referer = origin + "/", hls = false };
    }

    // vidara (+ mirror): POST /api/stream {filecode, device} -> streaming_url
    async Task<JavGuruStream> FilecodeApi(string embedUrl, CancellationToken ct)
    {
        var m = Regex.Match(embedUrl ?? "", @"^(?<o>https?://[^/]+)/(?:e|v|embed|d)/(?<c>[A-Za-z0-9]+)", RegexOptions.IgnoreCase);
        if (!m.Success)
            return null;

        foreach (string device in new[] { "web", "android" })
        {
            string json = await PostJson(m.Groups["o"].Value + "/api/stream", "{\"filecode\":\"" + m.Groups["c"].Value + "\",\"device\":\"" + device + "\"}", embedUrl, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
                continue;

            var sm = Regex.Match(json, @"""streaming_url""\s*:\s*""(?<u>[^""]+)""");
            if (sm.Success)
            {
                string u = Regex.Unescape(sm.Groups["u"].Value);
                return new JavGuruStream() { url = u, referer = embedUrl, hls = u.Contains(".m3u8") || !u.Contains(".mp4") };
            }
        }

        return null;
    }
    #endregion

    #region embed
    public static string HostLabel(string url)
    {
        string u = (url ?? "").ToLowerInvariant();
        if (Regex.IsMatch(u, @"turbovid|turboviplay|emturbo|etvp\.cc"))
            return "Turbo";
        if (Regex.IsMatch(u, @"vid[a]{1,2}ra|vidarax|vidavaca|streamup"))
            return "Vidara";
        if (Regex.IsMatch(u, @"javclan|javplaya|streamwish|wishembed|swdyu|hglink|hgcloud"))
            return "JavClan";
        if (Regex.IsMatch(u, @"voe|javlesbians|johnfullwonder|eugenemakedraw"))
            return "VOE";
        if (Regex.IsMatch(u, @"dood|vide0|d000d|ds2play|d0o0d|dooood"))
            return "Dood";
        if (u.Contains("maxstream"))
            return "MaxStream";
        if (u.Contains("streamtape"))
            return "Streamtape";
        if (u.Contains("mixdrop"))
            return "Mixdrop";

        try
        {
            string h = new System.Uri(url).Host;
            var parts = h.Split('.');
            string name = parts.Length >= 2 ? parts[parts.Length - 2] : h;
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
        catch
        {
            return null;
        }
    }

    public async Task<JavGuruStream> ResolveEmbed(string embedUrl, string referer, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(embedUrl) || embedUrl.Contains("/sandbox"))
            return null;

        string lower = embedUrl.ToLowerInvariant();

        // vidara hoi API truoc (trang embed la SPA)
        if (HostLabel(embedUrl) == "Vidara")
        {
            var api = await FilecodeApi(embedUrl, ct).ConfigureAwait(false);
            if (api != null)
                return api;
        }

        var page = await GetFollow(embedUrl, referer, ct).ConfigureAwait(false);
        string finalUrl = page?.url ?? embedUrl;
        string html = page?.html;

        if (!string.IsNullOrEmpty(html) && !finalUrl.Contains("/sandbox"))
        {
            var s = ExtractStream(html, finalUrl) ?? Voe(html, finalUrl);
            if (s != null)
                return s;

            var d = await Dood(html, finalUrl, ct).ConfigureAwait(false);
            if (d != null)
                return d;
        }

        // emturbovid /t/<id> -> thu truc tiep trang player turbovidhls
        var tm = Regex.Match(embedUrl, @"/t/(?<id>[A-Za-z0-9]+)");
        if (tm.Success && Regex.IsMatch(lower, "emturbo|turbovid"))
        {
            foreach (string h in new[] { "https://turbovidhls.com/t/", "https://emturbovid.com/t/" })
            {
                string alt = h + tm.Groups["id"].Value;
                if (alt.Equals(embedUrl, StringComparison.OrdinalIgnoreCase))
                    continue;
                var p2 = await GetFollow(alt, "https://jav.guru/", ct, maxHops: 3, jsRedirect: false).ConfigureAwait(false);
                var s2 = ExtractStream(p2?.html, p2?.url ?? alt);
                if (s2 != null)
                    return s2;
            }
        }

        // /e/<filecode> kieu vidara tren domain la
        if (HostLabel(embedUrl) != "Vidara")
        {
            var api = await FilecodeApi(finalUrl, ct).ConfigureAwait(false) ?? (finalUrl != embedUrl ? await FilecodeApi(embedUrl, ct).ConfigureAwait(false) : null);
            if (api != null)
                return api;
        }

        return null;
    }

    public async Task<JavGuruStream> ResolveMirror(JavGuruTo.Mirror mirror, string pageUrl, CancellationToken ct)
    {
        try
        {
            Log($"[{mirror.key}] {mirror.url}");
            var (embed, direct) = await ResolveSearcho(mirror.url, pageUrl, ct).ConfigureAwait(false);
            if (direct != null)
            {
                direct.label = HostLabel(direct.url) ?? mirror.label;
                return direct;
            }

            if (string.IsNullOrEmpty(embed))
            {
                Log($"[{mirror.key}] searcho: khong ra embed");
                return null;
            }

            Log($"[{mirror.key}] embed {embed}");
            var s = await ResolveEmbed(embed, mirror.url, ct).ConfigureAwait(false);
            if (s == null)
            {
                Log($"[{mirror.key}] embed: khong ra stream");
                return null;
            }

            s.label = HostLabel(embed) ?? mirror.label;
            Log($"[{mirror.key}] OK {s.label} {s.url}");
            return s;
        }
        catch (Exception ex)
        {
            Log($"[{mirror.key}] loi {ex.Message}");
            return null;
        }
    }

    static int Priority(string label) => label switch
    {
        "Turbo" => 0,
        "Vidara" => 1,
        "JavClan" => 2,
        "VOE" => 3,
        "MaxStream" => 4,
        "Dood" => 5,
        _ => 9
    };

    // resolve song song moi mirror, tra ve label -> stream (thu tu uu tien)
    public async Task<Dictionary<string, JavGuruStream>> ResolveAll(List<JavGuruTo.Mirror> mirrors, string pageUrl, TimeSpan timeout, CancellationToken ct = default, TimeSpan? grace = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var tasks = mirrors.Take(8).Select(m => ResolveMirror(m, pageUrl, cts.Token)).ToList();
        var all = Task.WhenAll(tasks);

        // co server dau tien chay duoc -> cho them grace roi tra ve, khong doi mirror cham
        var firstOk = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var t in tasks)
            _ = t.ContinueWith(x => { if (x.IsCompletedSuccessfully && x.Result != null) firstOk.TrySetResult(true); }, TaskScheduler.Default);

        try
        {
            await Task.WhenAny(all, firstOk.Task, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);
            if (!all.IsCompleted && firstOk.Task.IsCompleted)
                await Task.WhenAny(all, Task.Delay(grace ?? TimeSpan.FromSeconds(5), cts.Token)).ConfigureAwait(false);
        }
        catch { }

        var results = tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToArray();
        try { cts.Cancel(); } catch { }

        var links = new Dictionary<string, JavGuruStream>();
        var seenUrl = new HashSet<string>();
        foreach (var s in results.Where(x => x != null && !string.IsNullOrEmpty(x.url)).OrderBy(x => Priority(x.label)))
        {
            if (!seenUrl.Add(s.url))
                continue;

            string key = string.IsNullOrEmpty(s.label) ? "Server" : s.label;
            int n = 2;
            while (links.ContainsKey(key))
                key = s.label + " " + (n++);

            s.label = key;
            links[key] = s;
        }

        return links;
    }
    #endregion
}
