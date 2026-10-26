using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Shared.Models.Base;
using Shared.Models.SISI.Base;

namespace JAVSnap;

public static class JAVSnapTo
{
    public const string SiteHost = "https://javsnap.com";
    public const string PlayerHost = "https://vid-snap-player.com";
    public const string ChromeUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    #region host
    static string Host(string host) =>
        string.IsNullOrWhiteSpace(host) ? SiteHost : host.TrimEnd('/');

    public static string NormalizePageUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith("https://javsnap.com/", StringComparison.OrdinalIgnoreCase))
            return null;
        return WebUtility.HtmlDecode(value);
    }

    public static string NormalizeCover(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return value;
        if (value.StartsWith("//")) return "https:" + value;
        if (value.StartsWith("/")) return SiteHost + value;
        return value;
    }
    #endregion

    #region uri
    public static string Uri(string host, string search, string c, int pg)
    {
        host = Host(host);
        if (!string.IsNullOrWhiteSpace(search))
            return SearchUri(host, search, pg);

        if (!string.IsNullOrWhiteSpace(c))
        {
            string cat = c.Trim().Trim('/');
            int page = Math.Max(1, pg);
            return page == 1 ? host + "/" + cat : host + "/" + cat + "/pg-" + page.ToString(CultureInfo.InvariantCulture);
        }

        return host + "/en/";
    }

    // Tim kiem cua site la path-based: https://javsnap.com/en/search/<keyword>
    // (form action + ham searchPageUrl() trong /assets/client/js/main.js).
    // Dang query cu /search?q=... tra 500. Site khong phan trang ket qua tim kiem.
    public static string SearchUri(string host, string search, int pg)
        => Host(host) + "/en/search/" + System.Uri.EscapeDataString(search.Trim());
    #endregion

    #region parse playlist
    // Card cua site:
    //   <div class="item" data-movie-item="961">
    //     <div class="item__cover"><img src="https://pics.javsnap.com/.../x.webp" alt="TITLE">
    //       <a href="https://javsnap.com/en/<code>-<hash>" class="item__play" aria-label="TITLE">
    //     <div class="item__content"><h2 class="item__title">
    //       <a href="..." title="TITLE">TITLE</a></h2>
    // Cat theo moc data-movie-item de moi card la mot block, khong phai quet ca trang.
    // Nhom 1 = id phim (dung de tron 2 danh muc theo thu tu moi -> cu).
    static readonly Regex BlockRx = new Regex(
        "<div[^>]*\\bdata-movie-item\\s*=\\s*[\"'](\\d+)[\"']",
        RegexOptions.IgnoreCase);

    // BAY: the <img> cua site co onerror="this.src='/assets/client/img/default-cover.jpg'".
    // Regex cu "<img\\s+[^>]*src=" voi [^>]* THAM LAM nen khop vao src trong onerror
    // => moi phim deu ra anh default-cover. Phai:
    //   1) lay src DAU TIEN ((?<![\\w-]) de khong dinh data-src),
    //   2) uu tien anh trong khoi item__cover,
    //   3) bo qua default-cover.
    static readonly Regex CoverRx = new Regex(
        "item__cover\\b[^>]*>[^<]*<img[^>]*?(?<![\\w-])src\\s*=\\s*[\"']([^\"'<>]+)",
        RegexOptions.IgnoreCase);

    static readonly Regex ImgRx = new Regex(
        "<img[^>]*?(?<![\\w-])src\\s*=\\s*[\"']([^\"'<>]+)",
        RegexOptions.IgnoreCase);

    static readonly Regex TitleBlockRx = new Regex(
        "<h2[^>]*\\bitem__title\\b[^>]*>(.*?)</h2>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    static readonly Regex AnchorRx = new Regex(
        "<a\\s+[^>]*href\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    static readonly Regex AriaRx = new Regex(
        "\\baria-label\\s*=\\s*[\"']([^\"']*)[\"']",
        RegexOptions.IgnoreCase);

    // ma phim: <code>-<so tap>-<hash>, vd fthtd-199-k9krzozxqgl
    // va ban uncensored co them hau to: dv-563-mosaic-6tKYGKCfzFD
    // (regex cu ^[a-z0-9]+-\d+-[a-z0-9]+$ loai sach ca danh muc uncensored)
    static readonly Regex MovieSlugRx = new Regex(
        "^[a-z0-9]+-\\d+-[a-z0-9-]+$",
        RegexOptions.IgnoreCase);

    public static List<PlaylistItem> Playlist(string uri, string html)
        => PlaylistWithId(uri, html).Select(x => x.item).ToList();

    /// <summary>Nhu Playlist nhung giu them id phim de tron 2 danh muc dung thu tu.</summary>
    public static List<(string id, PlaylistItem item)> PlaylistWithId(string uri, string html)
    {
        var list = new List<(string id, PlaylistItem item)>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var blocks = BlockRx.Matches(html);

        for (int i = 0; i < blocks.Count; i++)
        {
            int start = blocks[i].Index;
            int end = i + 1 < blocks.Count ? blocks[i + 1].Index : Math.Min(html.Length, start + 8000);
            if (end <= start) continue;

            string block = html.Substring(start, end - start);

            string href = null, title = null;

            var h2 = TitleBlockRx.Match(block);
            if (h2.Success)
            {
                var a = AnchorRx.Match(h2.Groups[1].Value);
                if (a.Success)
                {
                    href = WebUtility.HtmlDecode(a.Groups[1].Value);
                    title = CleanText(a.Groups[2].Value);
                }
            }

            if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(title))
            {
                var a = AnchorRx.Match(block);
                if (a.Success && href == null)
                    href = WebUtility.HtmlDecode(a.Groups[1].Value);

                if (string.IsNullOrEmpty(title))
                {
                    var aria = AriaRx.Match(block);
                    if (aria.Success)
                        title = WebUtility.HtmlDecode(aria.Groups[1].Value).Trim();
                }
            }

            if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(title))
                continue;

            if (!MovieSlugRx.IsMatch(Slug(href)))
                continue;

            if (title.Length < 3)
                continue;

            if (!seen.Add(href))
                continue;

            list.Add((blocks[i].Groups[1].Value, new PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = title,
                picture = Poster(block),
                json = true,
            }));
        }

        return list;
    }

    static string Poster(string block)
    {
        var m = CoverRx.Match(block);
        if (!m.Success) m = ImgRx.Match(block);
        if (!m.Success) return null;

        string img = NormalizeCover(m.Groups[1].Value);
        if (img == null) return null;

        if (img.Contains("/assets/client/img/default-cover", StringComparison.OrdinalIgnoreCase))
            return null;

        return img;
    }

    /// <summary>Tron 2 danh muc (censored + uncensored) theo id giam dan = moi nhat truoc.</summary>
    public static List<PlaylistItem> MergeLatest(
        List<(string id, PlaylistItem item)> first,
        List<(string id, PlaylistItem item)> second)
    {
        var res = new List<PlaylistItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var all = new List<(long id, PlaylistItem item)>(first.Count + second.Count);

        foreach (var x in first)
            all.Add((ParseId(x.id), x.item));

        foreach (var x in second)
            all.Add((ParseId(x.id), x.item));

        all.Sort((a, b) => b.id.CompareTo(a.id));

        foreach (var x in all)
        {
            if (!seen.Add(x.item.video)) continue;
            res.Add(x.item);
        }

        return res;
    }

    static long ParseId(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0;


    static string CleanText(string value)
    {
        value = Regex.Replace(value, "<[^>]+>", " ");
        value = WebUtility.HtmlDecode(value);
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    static string Slug(string url)
    {
        try
        {
            string path = new Uri(url).AbsolutePath.Trim('/');
            int i = path.LastIndexOf('/');
            return i >= 0 ? path.Substring(i + 1) : path;
        }
        catch { return string.Empty; }
    }

    #endregion

    #region pagination
    // Trang danh muc co block <ul class="paginator"> chua link /<cat>/pg-<n> (trang cuoi ~247).
    // Trang chu /en/ khong co paginator (va /en/pg-N = 404) => chi 1 trang.
    static readonly Regex PgRx = new Regex(
        "/pg-(\\d+)",
        RegexOptions.IgnoreCase);

    public static int Pages(string html, bool search = false)
    {
        if (string.IsNullOrEmpty(html)) return 0;
        if (search) return 1;

        int max = 1;
        foreach (Match m in PgRx.Matches(html))
        {
            if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > max)
                max = v;
        }
        return max;
    }
    #endregion

    #region menu
    // ---- sort ----
    // Site javsnap.com KHONG co sort: khong co control sort nao trong HTML, va
    // ?sort=views|popular|... tra y nguyen list (da do 2026-10-26 tren /censored,
    // /en/genre/*, /en/studio/*, /en/label/*, /en/search/*).
    // Theo checklist 9g #4: context khong sort duoc => KHONG dung dong 2.
    // Van giu whitelist/normalize de gia tri la (tu app hoac URL) khong lot vao Uri/cache key.
    public static readonly string[] SortWhitelist = Array.Empty<string>();

    public static string NormalizeSort(string sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return null;
        sort = sort.Trim().ToLowerInvariant();
        return Array.IndexOf(SortWhitelist, sort) >= 0 ? sort : null;
    }

    public static (string name, string sort)[] SortsFor(string search, string c) => null;

    public static string ClampSort(string sort, string search, string c) => null;

    public static string SortLabel(string sort) => "mới nhất";

    // ---- head (phu thuoc context: dung lai moi request, khong cache) ----
    public static List<MenuItem> MenuHead(string host, string search, string sort, string c)
    {
        host = Host(host);

        return new List<MenuItem>
        {
            new MenuItem() { title = "Tìm kiếm", search_on = "search_on", playlist_url = host + "/javsnap" }
        };
    }

    // ---- base (taxonomy: cache 1 lan) ----
    // dong 3: danh sach toan cuc cua site (khong phu thuoc site fetch => luon co)
    public static MenuItem MenuLists(string host)
    {
        host = Host(host);
        string Link(string c) => host + "/javsnap?c=" + HttpUtility.UrlEncode(c);

        var lists = new List<MenuItem>
        {
            new MenuItem("Mới nhất", host + "/javsnap"),
            new MenuItem("Sắp ra mắt", Link("en/incoming")),
            new MenuItem("Censored", Link("en/censored")),
            new MenuItem("Uncensored", Link("en/uncensored")),
            new MenuItem("Có phụ đề", Link("en/subtitles")),
            new MenuItem("Amateur", Link("en/amateur")),
        };

        return new MenuItem() { title = "Danh sách", playlist_url = "submenu", submenu = lists };
    }

    // dong 4+: taxonomy lay dong tu site, chia nhom <= 250 muc
    public static List<MenuItem> MenuTaxonomy(
        string host,
        List<(string title, string c)> genres,
        List<(string title, string c)> studios,
        List<(string title, string c)> labels)
    {
        host = Host(host);
        string Link(string c) => host + "/javsnap?c=" + HttpUtility.UrlEncode(c);

        var menu = new List<MenuItem>();

        foreach (var (title, items) in new[]
        {
            ("Thể loại", genres),
            ("Hãng phim", studios),
            ("Nhãn phim", labels),
        })
        {
            foreach (var (t, part) in Chunk(title, items))
            {
                var sub = new List<MenuItem>(part.Count);
                foreach (var it in part)
                    sub.Add(new MenuItem(it.title, Link(it.c)));

                menu.Add(new MenuItem() { title = t, playlist_url = "submenu", submenu = sub });
            }
        }

        return menu;
    }

    // Trang taxonomy dang card:  <a href="https://javsnap.com/en/genre/<slug>"> ... <h3 class="category-card__name">Ten</h3>
    static readonly Regex TaxRx = new Regex(
        "<a[^>]+href\\s*=\\s*[\"'](?:https?://javsnap\\.com)?/en/(genre|studio|label)/([^\"'#?]+)[\"'][^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    static readonly Regex TaxNameRx = new Regex(
        "category-card__name[^>]*>(.*?)</h3>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static string TaxonomyUri(string host, string kind, int pg)
    {
        host = Host(host);
        string url = host + "/en/" + kind;
        return pg > 1 ? url + "/pg-" + pg.ToString(CultureInfo.InvariantCulture) : url;
    }

    public static List<(string title, string c)> Taxonomy(string html)
    {
        var list = new List<(string title, string c)>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in TaxRx.Matches(html))
        {
            string kind = m.Groups[1].Value.ToLowerInvariant();
            string slug = m.Groups[2].Value.Trim();
            if (slug.Length == 0) continue;

            var n = TaxNameRx.Match(m.Groups[3].Value);
            string title = n.Success ? CleanText(n.Groups[1].Value) : null;

            if (string.IsNullOrEmpty(title))
                title = slug;

            string c = "en/" + kind + "/" + slug;
            if (!seen.Add(c)) continue;

            list.Add((title, c));
        }

        return list;
    }

    /// <summary>Chia taxonomy thanh nhom <= 250 muc (gioi han cua menu SISI).</summary>
    public static List<(string title, List<(string title, string c)> items)> Chunk(
        string title, List<(string title, string c)> items, int max = 250)
    {
        var res = new List<(string title, List<(string title, string c)> items)>();
        if (items == null || items.Count == 0) return res;

        int parts = (items.Count + max - 1) / max;
        if (parts <= 1)
        {
            res.Add((title, items));
            return res;
        }

        for (int i = 0; i < parts; i++)
        {
            var part = items.GetRange(i * max, Math.Min(max, items.Count - i * max));
            res.Add((title + " (" + (i + 1).ToString(CultureInfo.InvariantCulture) + "/" + parts.ToString(CultureInfo.InvariantCulture) + ")", part));
        }

        return res;
    }
    #endregion

    #region detail servers
    public sealed class ServerItem
    {
        public string Label { get; set; }
        public string EmbedUrl { get; set; }
    }

    // <button class="ep-btn" data-url="https://..." onclick="changeEpisode(this)"> VIP#1 </button>
    static readonly Regex EpRx = new Regex(
        "<button(?=[^>]*\\bep-btn\\b)[^>]*\\bdata-url\\s*=\\s*[\"']([^\"']+)[\"'][^>]*>(.*?)</button>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public static List<ServerItem> Servers(string html)
    {
        var list = new List<ServerItem>();
        if (string.IsNullOrEmpty(html)) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in EpRx.Matches(html))
        {
            string url = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            string label = Regex.Replace(Regex.Replace(m.Groups[2].Value, "<[^>]+>", " "), @"\s+", " ").Trim();

            if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrEmpty(label))
                label = new Uri(url).Host;
            if (!seen.Add(label))
                continue;

            list.Add(new ServerItem() { Label = label, EmbedUrl = url });
        }

        return list;
    }

    /// <summary>Loai server: 1 = HLS (mac dinh), 2 = mp4 (doodstream family / streamtape).</summary>
    public static int Kind(string embedUrl)
    {
        if (embedUrl == null) return 0;
        if (embedUrl.Contains("playmogo.", StringComparison.OrdinalIgnoreCase) ||
            embedUrl.Contains("streamtape.", StringComparison.OrdinalIgnoreCase) ||
            embedUrl.Contains("dood.", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    public static bool Supported(string embedUrl)
        => VipUuid(embedUrl) != null
        || embedUrl.Contains("luluvid.", StringComparison.OrdinalIgnoreCase)
        || IsStreamtape(embedUrl)
        || Kind(embedUrl) == 2;

    // Streamtape (label STP): trang embed co 2 token — div#robotlink (token CU, API 500) va
    // script gan lai (token MOI). Cong thuc chung voi SupJav/SexTb/JavHDToday (verify 2026-10-06).
    public static bool IsStreamtape(string embedUrl)
        => !string.IsNullOrEmpty(embedUrl)
        && embedUrl.Contains("streamtape.", StringComparison.OrdinalIgnoreCase);

    public static string StreamtapeApi(string embedHtml)
    {
        if (string.IsNullOrEmpty(embedHtml)) return null;

        string query = null;

        foreach (Match m in Regex.Matches(embedHtml,
            @"robotlink'\)\.innerHTML\s*=\s*'([^']*)'\s*\+\s*\('([^']*)'\)((?:\s*\.substring\(\d+\)\s*)*)",
            RegexOptions.IgnoreCase))
        {
            string tail = m.Groups[2].Value;
            foreach (Match s in Regex.Matches(m.Groups[3].Value ?? "", @"substring\((\d+)\)"))
            {
                if (int.TryParse(s.Groups[1].Value, out int n) && n >= 0 && n <= tail.Length)
                    tail = tail.Substring(n);
            }
            query = m.Groups[1].Value + tail;
        }

        if (string.IsNullOrEmpty(query))
        {
            var div = Regex.Match(embedHtml, @"id\s*=\s*""robotlink""[^>]*>([^<]+)<", RegexOptions.IgnoreCase);
            if (div.Success) query = div.Groups[1].Value;
        }

        if (string.IsNullOrEmpty(query)) return null;

        query = WebUtility.HtmlDecode(query).Trim();

        if (query.StartsWith("//")) return "https:" + query;
        if (query.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return query;

        if (query.StartsWith("/streamtape.com/", StringComparison.OrdinalIgnoreCase))
            query = query.Substring("/streamtape.com".Length);

        return "https://streamtape.com/" + query.TrimStart('/');
    }
    #endregion

    #region deobfuscate

    /// <summary>UUID cua server VIP (player rieng cua site: /embed/&lt;uuid&gt;).</summary>
    public static string VipUuid(string embedUrl)
    {
        if (embedUrl == null) return null;
        if (!embedUrl.Contains("vid-snap-player.", StringComparison.OrdinalIgnoreCase)) return null;
        var m = Regex.Match(embedUrl, @"/embed/([0-9a-fA-F-]{8,})");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string VipToken(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var m = Regex.Match(json, "\"t\"\\s*:\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Giai P.A.C.K.E.R (eval(function(p,a,c,k,e,d)...)) — dung cho luluvid.</summary>
    public static string UnpackPacker(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var m = Regex.Match(html,
            @"\}\s*\(\s*'(?<p>(?:[^'\\]|\\.)*)'\s*,\s*(?<a>\d+)\s*,\s*(?<c>\d+)\s*,\s*'(?<k>(?:[^'\\]|\\.)*)'\s*\.\s*split\s*\(\s*'\|'\s*\)",
            RegexOptions.Singleline);

        if (!m.Success) return null;

        string p = Regex.Unescape(m.Groups["p"].Value);
        int a = int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
        int c = int.Parse(m.Groups["c"].Value, CultureInfo.InvariantCulture);
        string[] k = Regex.Unescape(m.Groups["k"].Value).Split('|');

        return Regex.Replace(p, @"[0-9a-zA-Z]+", mm =>
        {
            int idx = DecodeBase(mm.Value, a);
            if (idx >= 0 && idx < c && idx < k.Length && k[idx].Length > 0)
                return k[idx];
            return mm.Value;
        });
    }

    static int DecodeBase(string s, int a)
    {
        if (a < 2 || a > 62) return -1;
        long v = 0;
        foreach (char ch in s)
        {
            int d = ch >= '0' && ch <= '9' ? ch - '0'
                  : ch >= 'a' && ch <= 'z' ? ch - 'a' + 10
                  : ch >= 'A' && ch <= 'Z' ? ch - 'A' + 36 : -1;
            if (d < 0 || d >= a) return -1;
            v = v * a + d;
            if (v > int.MaxValue) return -1;
        }
        return (int)v;
    }

    /// <summary>Link m3u8 trong jwplayer setup cua luluvid (sau khi giai packer).</summary>
    public static string ExtractM3u8(string js)
    {
        if (string.IsNullOrEmpty(js)) return null;
        var m = Regex.Match(js, "file\\s*:\\s*[\"'](https?://[^\"']+)[\"']", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        string url = m.Groups[1].Value.Replace("\\/", "/");
        if (!url.Contains("m3u8", StringComparison.OrdinalIgnoreCase) && !url.Contains("master", StringComparison.OrdinalIgnoreCase))
            return null;
        return url;
    }

    /// <summary>DoodStream: pass_md5/&lt;hash&gt;/&lt;token&gt;</summary>
    public static string DoodPassPath(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, @"pass_md5/([^""'\s\\<>]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string DoodToken(string passPath)
    {
        if (string.IsNullOrEmpty(passPath)) return null;
        int i = passPath.LastIndexOf('/');
        return i > 0 ? passPath.Substring(i + 1) : null;
    }

    public static string DoodStream(string passBody, string token)
    {
        if (string.IsNullOrEmpty(passBody) || string.IsNullOrEmpty(token)) return null;
        passBody = passBody.Trim();
        if (!passBody.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

        var rnd = new StringBuilder(10);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var rand = new Random(Guid.NewGuid().GetHashCode());
        for (int i = 0; i < 10; i++)
            rnd.Append(alphabet[rand.Next(alphabet.Length)]);

        return passBody + rnd + "?token=" + token;
    }
    #endregion

    #region hls headers
    public static string RefererFor(string url)
    {
        if (url == null) return SiteHost + "/";
        if (url.Contains("vid-snap-player.", StringComparison.OrdinalIgnoreCase)) return PlayerHost + "/";
        if (url.Contains("luluvid.", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("lulucdn.", StringComparison.OrdinalIgnoreCase) ||
            url.Contains(".tnmr.", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("lulustream.", StringComparison.OrdinalIgnoreCase))
            return "https://luluvid.xyz/";
        if (url.Contains("playmogo.", StringComparison.OrdinalIgnoreCase))
            return "https://playmogo.com/";
        if (url.Contains("javsnap.com", StringComparison.OrdinalIgnoreCase)) return SiteHost + "/";
        return SiteHost + "/";
    }

    public static List<HeadersModel> StreamHeaders(string url)
    {
        string referer = RefererFor(url);
        // Chi UA + Referer + Accept. Them Accept-Language hoac Origin/Sec-Fetch-* la CDN
        // (vd *.tnmr.org cua luluvid) tra 403 ca manifest lan segment.
        return HeadersModel.Init(
            ("User-Agent", ChromeUA),
            ("Referer", referer),
            ("Accept", "*/*")
        );
    }
    #endregion
}
