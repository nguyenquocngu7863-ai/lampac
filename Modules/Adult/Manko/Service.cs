using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Manko;

public static class MankoTo
{
    public static string SiteHost = "https://manko.fun";
    public static string ApiHost = "https://healertanker.com";
    public static string ApiFallback = "https://robustx.shop";
    public const string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";

    // Server2 (upload18/helvid): F18 - URL bind dung UA luc tao (chi tiet skill lampac-deobfuscate).
    public const string HelvidUA = Shared.Services.Http.UserAgent;

    static readonly char[] Hidden = { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(c => !Hidden.Contains(c)).ToArray()).Trim();
    }

    // ========== Uri ==========
    // Home (giong web /home): most_popular, size=24, page=N.
    // Search keyword: keyword=...&size=24&page=N (builder va, by=Title).
    // Search actor: actor=...&size=24&page=N (builder va, by!=Title).
    // Category (builder Rr/br, da do total that):
    //   popular / top_rated / new / subtitle / random / censored / uncensored
    // Genre: genre=<ten> (115 muc tu /common/list-genre, da do Creampie=96506).
    // Studio: country=<ten> (1000 muc tu /common/country-list, da do MOODYZ=7317).
    // type=genre|country + name (route /filter/:type/:name, builder ma).
    const int Size = 24;

    public static string Uri(string host, string search, string c, int pg, bool byActor = false)
    {
        if (pg < 1) pg = 1;
        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = System.Uri.EscapeDataString(search.Trim());
            string key = byActor ? "actor=" : "keyword=";
            return ApiHost + "/swx/movie/search?" + key + s + "&size=" + Size + "&page=" + pg + "&can18=true";
        }
        string cat = (c ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(cat))
            return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&most_popular=true&can18=true";
        if (cat.StartsWith("genre/", StringComparison.OrdinalIgnoreCase))
            return ApiHost + "/swx/movie/search?size=" + Size + "&page=" + pg + "&can18=true&genre=" + System.Uri.EscapeDataString(cat.Substring(6));
        if (cat.StartsWith("country/", StringComparison.OrdinalIgnoreCase))
            return ApiHost + "/swx/movie/search?size=" + Size + "&page=" + pg + "&can18=true&country=" + System.Uri.EscapeDataString(cat.Substring(8));
        if (cat.StartsWith("type/", StringComparison.OrdinalIgnoreCase))
        {
            // type/genre/Creampie -> genre=Creampie (builder ma)
            var parts = cat.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
                return ApiHost + "/swx/movie/search?size=" + Size + "&page=" + pg + "&can18=true&"
                    + System.Uri.EscapeDataString(parts[1]) + "=" + System.Uri.EscapeDataString(string.Join("/", parts.Skip(2)));
            return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&most_popular=true&can18=true";
        }
        switch (cat.ToLowerInvariant())
        {
            case "popular": return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&can18=true&most_popular=true";
            case "top_rated": return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&can18=true&top_rated=true";
            case "new": return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&can18=true";
            case "subtitle": return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&can18=true&subtitle_link=true";
            case "random": return ApiHost + "/swx/movie/random-list?page=" + pg + "&size=" + Size + "&can18=true";
            case "censored": return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&category=Censored&can18=true";
            case "uncensored": return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&category=Uncensored&can18=true";
            default: return ApiHost + "/swx/movie/search?page=" + pg + "&size=" + Size + "&most_popular=true&can18=true";
        }
    }

    // ========== xn decode (port tu bundle, da verify: search/detail/genre deu giai dung) ==========
    const string Alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!#$%&()*+,./:;<=>?@[]^_`{|}~\"";
    public static string XnDecode(string e)
    {
        if (string.IsNullOrEmpty(e)) return "";
        var bytes = new List<byte>(e.Length);
        int a = 0, i = 0, o = -1;
        foreach (char ch in e)
        {
            int l = Alpha.IndexOf(ch);
            if (l < 0) continue;
            if (o < 0) { o = l; continue; }
            o = o + 91 * l;
            a |= o << i;
            i += ((8191 & o) > 88 ? 13 : 14);
            while (i > 7) { bytes.Add((byte)(255 & a)); a >>= 8; i -= 8; }
            o = -1;
        }
        if (o > -1) bytes.Add((byte)(255 & (a | o << i)));
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public static (List<JsonElement> items, int total) ParseList(string json)
    {
        var res = new List<JsonElement>();
        if (string.IsNullOrWhiteSpace(json)) return (res, 0);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int total = 0;
            if (root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number)
                total = t.GetInt32();
            if (!root.TryGetProperty("data", out var d)) return (res, total);
            string raw = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
            string dec = XnDecode(raw ?? "");
            if (string.IsNullOrEmpty(dec)) return (res, total);
            using var arr = JsonDocument.Parse(dec);
            if (arr.RootElement.ValueKind != JsonValueKind.Array) return (res, total);
            foreach (var el in arr.RootElement.EnumerateArray())
                res.Add(el.Clone());
            return (res, total);
        }
        catch { return (res, 0); }
    }

    static string Str(JsonElement o, string key)
    {
        try
        {
            if (o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
        }
        catch { }
        return "";
    }

    // ========== Playlist ==========
    // Item list chi co _id/title/thumbnail/wasabi_url_1 (poster that).
    // Detail moi co cover_image/tags/subtitle_link.
    public static List<PlaylistItem> Playlist(string route, List<JsonElement> items)
    {
        var list = new List<PlaylistItem>();
        if (items == null) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            string id = Str(it, "_id");
            if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
            string title = Str(it, "title");
            if (string.IsNullOrEmpty(title)) continue;
            title = Clean(title);
            string poster = Str(it, "thumbnail_image");
            if (string.IsNullOrEmpty(poster)) poster = Str(it, "wide_thumbnail");
            if (string.IsNullOrEmpty(poster)) poster = Str(it, "thumbnail");
            if (string.IsNullOrEmpty(poster)) poster = Str(it, "cover_url");
            string detailUrl = ApiHost + "/swx/movie/detail/" + id;
            list.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(detailUrl),
                name = title,
                picture = string.IsNullOrEmpty(poster) ? null : poster,
                json = true,
                bookmark = new Bookmark() { site = "manko", href = detailUrl, image = poster }
            });
        }
        return list;
    }

    public static int TotalPages(int total) => total <= 0 ? 1 : Math.Max(1, (total + Size - 1) / Size);

    // ========== Detail ==========
    public static JsonElement? ParseDetail(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var d)) return null;
            string raw = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
            string dec = XnDecode(raw ?? "");
            if (string.IsNullOrEmpty(dec)) return null;
            using var o = JsonDocument.Parse(dec);
            return o.RootElement.Clone();
        }
        catch { return null; }
    }

    public static string DetailTitle(JsonElement d)
    {
        string t = Str(d, "title");
        return Clean(t);
    }

    public static string DetailPoster(JsonElement d)
    {
        string p = Str(d, "cover_image");
        return string.IsNullOrEmpty(p) ? null : p;
    }

    // server1 = wasabi_embed_url (/e/<hash>), server2 = upload18_player[0]
    // (PLAYER_CONFIG m3u8 helvid, IP-bound). server3 mmtv: CF challenge -> bo.
    public static List<(string label, string kind, string url)> DetailServers(JsonElement d)
    {
        var res = new List<(string, string, string)>();
        string w = Str(d, "wasabi_embed_url");
        if (!string.IsNullOrEmpty(w) && w.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            res.Add(("Server 1", "wasabi", w));
        try
        {
            if (d.TryGetProperty("upload18_player", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in arr.EnumerateArray())
                {
                    string u = x.ValueKind == JsonValueKind.String ? x.GetString() : null;
                    if (!string.IsNullOrEmpty(u) && u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    { res.Add(("Server 2", "upload18", u)); break; }
                }
            }
        }
        catch { }
        return res;
    }

    // wasabi embed JS: buildStreamUrl = /stream + query cu + id=hashId
    // -> {"media":{"stream":".../video.m3u8"}}. Da do: 200 voi Referer embed.
    public static string WasabiHashId(string embedUrl)
    {
        if (string.IsNullOrEmpty(embedUrl)) return null;
        var m = Regex.Match(embedUrl, @"/e/([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string WasabiStreamUrl(string embedUrl, string hashId)
    {
        if (string.IsNullOrEmpty(embedUrl) || string.IsNullOrEmpty(hashId)) return null;
        try
        {
            var u = new Uri(embedUrl);
            string api = u.GetLeftPart(UriPartial.Authority) + "/stream" + u.Query;
            api += (u.Query.Contains("?") ? "&" : "?") + "id=" + hashId;
            return api;
        }
        catch { return null; }
    }

    public static string ParseWasabiStream(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("media", out var m)
                && m.TryGetProperty("stream", out var s))
            {
                string u = s.GetString()?.Trim();
                if (!string.IsNullOrEmpty(u) && u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return u;
            }
        }
        catch { }
        return null;
    }

    // upload18 play page: PLAYER_CONFIG = {"m3u8":"https://helvid.com/m/..."}
    public static string ExtractPlayerConfigM3u8(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        try
        {
            var m = Regex.Match(html, @"PLAYER_CONFIG\s*=\s*(\{.*?\});", RegexOptions.Singleline);
            if (!m.Success) return null;
            using var doc = JsonDocument.Parse(m.Groups[1].Value);
            if (doc.RootElement.TryGetProperty("m3u8", out var el))
            {
                string u = el.GetString()?.Trim();
                if (!string.IsNullOrEmpty(u) && u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return u;
            }
        }
        catch { }
        return null;
    }

    public static string ExtractVideoKey(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        try
        {
            var m = Regex.Match(html, @"window\.PLAYER_CONFIG\s*=\s*(\{.*?\});", RegexOptions.Singleline);
            if (!m.Success)
                m = Regex.Match(html, @"PLAYER_CONFIG\s*=\s*(\{.*?\});", RegexOptions.Singleline);
            if (!m.Success) return null;
            using var doc = JsonDocument.Parse(m.Groups[1].Value);
            if (doc.RootElement.TryGetProperty("videoKey", out var el))
            {
                string v = el.GetString()?.Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }
        catch { }
        return null;
    }

    // ========== Taxonomy: /common/list-genre (plain array) + /common/country-list ==========
    public static List<string> ParsePlainArray(string json)
    {
        var res = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return res;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var d)) return res;
            if (d.ValueKind != JsonValueKind.Array) return res;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in d.EnumerateArray())
            {
                string s = x.ValueKind == JsonValueKind.String ? Clean(x.GetString() ?? "") : "";
                if (string.IsNullOrEmpty(s) || s.Length > 60 || !seen.Add(s)) continue;
                res.Add(s);
            }
        }
        catch { }
        return res;
    }

    // ========== Menu (chuan 2 tang, §9a) ==========
    // Site khong co sort (thu tu nam trong category) -> khong hien dong 2.
    public static List<MenuItem> MenuHead(string host, string search)
    {
        host = host.TrimEnd('/');
        string root = host + "/manko";
        return new List<MenuItem>(1)
        {
            new MenuItem(){ title = "Tìm kiếm", search_on = "search_on", playlist_url = root }
        };
    }

    public static List<MenuItem> Menu(string host, List<string> genres, List<string> countries)
    {
        host = host.TrimEnd('/');
        string url(string c) => host + "/manko?c=" + HttpUtility.UrlEncode(c);
        var root = new List<MenuItem>(4)
        {
            new MenuItem(){ title = "Danh sách", playlist_url = "submenu", submenu = new List<MenuItem>(){
                new("Mới nhất", url("new")),
                new("Phổ biến", host + "/manko"),
                new("Top đánh giá", url("top_rated")),
                new("Vietsub", url("subtitle")),
                new("Ngẫu nhiên", url("random")),
                new("Có che", url("censored")),
                new("Không che", url("uncensored")),
            }},
        };
        if (genres != null && genres.Count > 0)
        {
            var sub = genres.Select(g => new MenuItem(g, url("genre/" + g))).ToList();
            root.Add(new MenuItem() { title = "Thể loại", playlist_url = "submenu", submenu = sub });
        }
        if (countries != null && countries.Count > 0)
        {
            var all = countries.Select(n => (name: n, path: "country/" + n)).ToList();
            if (all.Count > 300)
                root.AddRange(DirBuckets(host, "Hãng phim", all));
            else
                root.Add(new MenuItem()
                {
                    title = "Hãng phim",
                    playlist_url = "submenu",
                    submenu = all.Select(x => new MenuItem(x.name, url(x.path))).ToList()
                });
        }
        return root;
    }

    public const int MaxPerBucket = 300;
    public static List<MenuItem> DirBuckets(
        string host, string title,
        IReadOnlyList<(string name, string path)> all, int maxPer = MaxPerBucket)
    {
        host = host.TrimEnd('/');
        var res = new List<MenuItem>();
        if (all == null || all.Count == 0)
            return res;

        var groups = new Dictionary<char, List<(string name, string path)>>();
        foreach (var it in all)
        {
            if (string.IsNullOrWhiteSpace(it.name))
                continue;
            char c = char.ToUpperInvariant(it.name.Trim()[0]);
            if (c < 'A' || c > 'Z')
                c = '#';
            if (!groups.TryGetValue(c, out var lst))
            {
                lst = new List<(string, string)>();
                groups[c] = lst;
            }
            lst.Add(it);
        }

        var keys = groups.Keys.OrderBy(CharRank).ToList();

        var flat = new List<(char key, string name, string path)>(all.Count);
        foreach (var k in keys)
            foreach (var it in groups[k])
                flat.Add((k, it.name, it.path));

        var from = new List<char>();
        var to = new List<char>();
        var subs = new List<List<MenuItem>>();
        for (int i = 0; i < flat.Count; i += maxPer)
        {
            int n = Math.Min(maxPer, flat.Count - i);
            from.Add(flat[i].key);
            to.Add(flat[i + n - 1].key);
            subs.Add(flat.Skip(i).Take(n).Select(x =>
                new MenuItem(
                    x.name, host + "/manko?c=" + HttpUtility.UrlEncode(x.path))).ToList());
        }

        for (int k = 0; k < from.Count; k++)
        {
            string name = title + " " + from[k]
                + (to[k] == from[k] ? "" : "–" + to[k]);
            if (to[k] == from[k])
            {
                int parts = from.Count(c => c == from[k]);
                if (parts > 1)
                {
                    int nth = 1;
                    for (int q = 0; q < k; q++)
                        if (from[q] == from[k])
                            nth++;
                    name += $" ({nth}/{parts})";
                }
            }
            res.Add(new MenuItem(name, "submenu")
            { submenu = subs[k] });
        }

        return res;
    }

    static int CharRank(char c)
    {
        if (c == '#') return -1;
        if (c >= '0' && c <= '9') return c;
        return 1000 + c;
    }

    // ========== Fetch helpers: direct-first + retry ==========
    public static async Task<string> GetHtmlAsync(string url, string referer, int timeoutSeconds = 25, System.Net.WebProxy proxy = null, int httpversion = 1, string ua = null)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", string.IsNullOrEmpty(ua) ? ChromeUA : ua),
            ("Referer", referer ?? SiteHost + "/"),
            ("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"));
        string html = null;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, httpversion: httpversion); } catch { }
        if (!string.IsNullOrEmpty(html) && html.Length > 200) return html;
        try { html = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, proxy: proxy, httpversion: httpversion); } catch { return null; }
        if (string.IsNullOrEmpty(html)) return null;
        return html;
    }

    public static async Task<string> GetApiAsync(string url, System.Net.WebProxy proxy = null, int httpversion = 1, int timeoutSeconds = 15, string ua = null)
    {
        var headers = HeadersModel.Init(
            ("User-Agent", string.IsNullOrEmpty(ua) ? ChromeUA : ua),
            ("Referer", SiteHost + "/home"),
            ("Accept", "application/json"));
        string body = null;
        try { body = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, httpversion: httpversion); } catch { }
        if (!string.IsNullOrEmpty(body)) return body;
        try { body = await Http.Get(url, timeoutSeconds: timeoutSeconds, headers: headers, proxy: proxy, httpversion: httpversion); } catch { return null; }
        return body;
    }
}
