using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace JavQuick;

public static class JavQuickTo
{
    public static string SiteHost = "https://javquick.com";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    // ===== player ma hoa (decryptor.wasm) =====
    // Trang nap /decryptor.wasm; Rust crate `decryptor` co AES-128 hardcode trong data
    // section. Key do duoc o offset 29580 cua wasm: ASCII "7ExuG3hz8CGdVZ5s".
    // Cipher = AES-128-CTR: counter 128-bit big-endian, base = IV (16 ky tu ASCII lay
    // tu field thu 2 cua /watch payload), reset ve IV moi 6 MB (BlockSize).
    //   counter = IV + ((localOffset % BlockSize) / 16)
    public static readonly byte[] EncKey = Encoding.ASCII.GetBytes("7ExuG3hz8CGdVZ5s");

    // 6 MB — moc reset keystream (do bang wasm). Part cua CDN luon la boi so cua moc
    // nay (chunk size cua session = 0x78000000 = 320 block) nen offset trong part
    // cung chinh la offset trong chunk.
    public const long BlockSize = 6291456L;

    public static string Abs(string host, string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (url.StartsWith("http")) return url;
        return (string.IsNullOrEmpty(host) ? SiteHost : host).TrimEnd('/') + url;
    }

    // c: "" | home | release | upcoming | genre/<id>/<slug> | maker/<id>/<slug>
    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host)) host = SiteHost;

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = "/search?q=" + HttpUtility.UrlEncode(search.Trim());
            if (pg > 1) q += "&page=" + pg;
            return host + q;
        }

        if (string.IsNullOrWhiteSpace(c) || c == "home")
            return host + "/" + (pg > 1 ? "?page=" + pg : "");

        if (c == "release" || c == "upcoming")
            return host + "/" + c + (pg > 1 ? "?page=" + pg : "");

        if (c.StartsWith("genre/"))
            return host + "/genres/" + c.Substring(6).Trim('/') + (pg > 1 ? "?page=" + pg : "");

        if (c.StartsWith("maker/"))
            return host + "/maker/" + c.Substring(6).Trim('/') + (pg > 1 ? "?page=" + pg : "");

        return host + "/" + (pg > 1 ? "?page=" + pg : "");
    }

    // The phim: <article ...> <a href="/movie/<id>/<slug>"> ... <h2|h3 title="TEN">
    public static List<PlaylistItem> Playlist(string route, string html)
    {
        var playlists = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html)) return playlists;

        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html, "<article[^>]*>(.*?)</article>", RegexOptions.Singleline))
        {
            string b = m.Groups[1].Value;

            var hm = Regex.Match(b, "href=\"(/movie/[^\"]+)\"");
            if (!hm.Success) continue;
            string href = SiteHost + hm.Groups[1].Value;
            if (!seen.Add(href)) continue;

            var tm = Regex.Match(b, "<h[23][^>]*\\btitle=\"([^\"]{3,400})\"");
            if (!tm.Success) continue;
            string name = HttpUtility.HtmlDecode(tm.Groups[1].Value.Trim());
            if (name.Length == 0) continue;

            string poster = "";
            var pm = Regex.Match(b, "data-srcset=\"(https?://[^\"\\s]+)\"");
            if (pm.Success)
                poster = pm.Groups[1].Value;
            else
            {
                var sm = Regex.Match(b, "\\bsrc=\"(https?://[^\"]+)\"");
                if (sm.Success) poster = sm.Groups[1].Value;
            }

            playlists.Add(new PlaylistItem()
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(href),
                name = name,
                picture = poster,
                json = true,
                bookmark = new Bookmark()
                {
                    site = "javquick",
                    href = href,
                    image = poster
                }
            });
        }

        return playlists;
    }

    // /genres va /makers: <a href="/(genres|maker)/<id>/<slug>" ...>Ten</a>
    public static List<(string name, string path)> Taxonomy(string html, string kind)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(html,
            "<a\\s+href=\"(/" + kind + "/[A-Za-z0-9]+/[a-z0-9\\-]+)\"[^>]*>\\s*([^<]{1,120}?)\\s*</a>",
            RegexOptions.Singleline))
        {
            string path = m.Groups[1].Value;
            string name = HttpUtility.HtmlDecode(m.Groups[2].Value.Trim());
            if (name.Length == 0 || !seen.Add(path)) continue;
            res.Add((name, path));
        }

        return res;
    }

    // Trang detail -> data-id cua <video id="video-player" data-id="...">
    public static string DataId(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = Regex.Match(html, "id=\"video-player\"[^>]*?data-id=\"([A-Za-z0-9_-]+)\"", RegexOptions.Singleline);
        if (!m.Success)
            m = Regex.Match(html, "data-id=\"([A-Za-z0-9_-]+)\"", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    // Danh sach format + part cua trang detail (part dang "?type=FULLHD&p=2")
    public static List<(string fmt, int part, string href)> Sources(string html)
    {
        var res = new List<(string, int, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        string fmt = null;
        foreach (Match m in Regex.Matches(html,
            "video-format-header[^>]*>\\s*([^<]{1,40}?)\\s*</h3>"
            + "|<a class=\"video-source-btn[^\"]*\"[^>]*title=\"([^\"]*)\"[^>]*href=\"([^\"]*)\"",
            RegexOptions.Singleline))
        {
            if (m.Groups[1].Success)
            {
                fmt = HttpUtility.HtmlDecode(m.Groups[1].Value.Trim());
                continue;
            }

            var pm = Regex.Match(m.Groups[2].Value, "part\\s+(\\d+)");
            if (!pm.Success) continue;
            if (!int.TryParse(pm.Groups[1].Value, out int part)) continue;

            res.Add((string.IsNullOrEmpty(fmt) ? "Video" : fmt, part, m.Groups[3].Value));
        }

        return res;
    }

    // ===== /watch payload =====
    //  - "https://...mp4"                          -> URL mp4 truc tiep (khong ma hoa)
    //  - "<ticket>|<iv>|<url1>|<url2>|..."         -> nhieu part da ma hoa
    public static Payload ParsePayload(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        raw = raw.Trim();

        if (raw.StartsWith("http://") || raw.StartsWith("https://"))
            return new Payload() { direct = true, url = raw };

        if (!raw.Contains('|'))
        {
            try
            {
                string dec = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
                if (dec.Contains('|') || dec.StartsWith("http"))
                    return ParsePayload(dec);
            }
            catch { }
            return null;
        }

        var parts = raw.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        int first = parts.FindIndex(x => x.StartsWith("http"));
        if (first < 1) return null;

        var urls = parts.Skip(first).Where(x => x.StartsWith("http")).ToList();
        if (urls.Count == 0) return null;

        string iv = parts[first - 1];
        if (Encoding.ASCII.GetByteCount(iv) != 16) return null;

        return new Payload() { direct = false, iv = iv, urls = urls };
    }

    public sealed class Payload
    {
        public bool direct;
        public string url;
        public string iv;
        public List<string> urls;
    }

    // ===== chips popup (the loai / hang / nhan) =====
    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var res = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html)) return res;

        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html,
            "<a[^>]*href=\"/(genres|maker|label)/([A-Za-z0-9]+/[a-z0-9\\-]+)\"[^>]*>\\s*([^<]{1,120}?)\\s*</a>",
            RegexOptions.Singleline))
        {
            string kind = m.Groups[1].Value == "genres" ? "genre" : m.Groups[1].Value;
            string slug = m.Groups[2].Value;
            string name = HttpUtility.HtmlDecode(m.Groups[3].Value.Trim());
            if (name.Length == 0 || !seen.Add(kind + "/" + slug)) continue;
            res.Add((kind, slug, name));
        }

        return res;
    }

    // ===== AES-CTR (counter 128-bit big-endian) =====
    public static void AddCounter(byte[] c, long add)
    {
        ulong hi = 0, lo = 0;
        for (int i = 0; i < 8; i++) hi = (hi << 8) | c[i];
        for (int i = 8; i < 16; i++) lo = (lo << 8) | c[i];

        ulong nlo = lo + (ulong)add;
        if (nlo < lo) hi++;

        for (int i = 15; i >= 8; i--) { c[i] = (byte)nlo; nlo >>= 8; }
        for (int i = 7; i >= 0; i--) { c[i] = (byte)hi; hi >>= 8; }
    }
}
