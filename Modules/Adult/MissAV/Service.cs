using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace MissAV;

public static class MissAVTo
{
    public static string SiteHost = "https://missav.live";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    static string SlugifyVi(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim().ToLowerInvariant();
        string[][] map = new string[][]
        {
            new[] { "a", "àáạảãâầấậẩẫăằắặẳẵ" },
            new[] { "e", "èéẹẻẽêềếệểễ" },
            new[] { "i", "ìíịỉĩ" },
            new[] { "o", "òóọỏõôồốộổỗơờớợởỡ" },
            new[] { "u", "ùúụủũưừứựửữ" },
            new[] { "y", "ỳýỵỷỹ" },
            new[] { "d", "đ" },
        };
        foreach (var pair in map)
        {
            foreach (char ch in pair[1])
                s = s.Replace(ch.ToString(), pair[0]);
        }
        s = Regex.Replace(s, @"[^a-z0-9\s-]", " ");
        s = Regex.Replace(s, @"[\s_]+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return s;
    }

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        string page = pg > 1 ? "?page=" + pg : "";

        if (!string.IsNullOrWhiteSpace(search))
        {
            string slug = SlugifyVi(search);
            if (string.IsNullOrEmpty(slug))
                slug = HttpUtility.UrlEncode(search);
            return host + "/vi/search/" + slug + page;
        }

        if (!string.IsNullOrEmpty(c))
        {
            // c co the la full URL missav (menu giu nguyen /dmNN/ cua site) hoac path sau /vi/
            string url = c.StartsWith("http") ? c : host + "/vi/" + c.Trim('/');
            return url + (pg > 1 ? (url.Contains("?") ? "&" : "?") + "page=" + pg : "");
        }

        return host + "/vi" + page;
    }

    // tilesJson: [{u,t,p}] do Playwright EvaluateAsync tra ve sau khi Alpine render
    public static List<Shared.Models.SISI.Base.PlaylistItem> Playlist(string uri, string tilesJson)
    {
        var playlists = new List<Shared.Models.SISI.Base.PlaylistItem>();
        if (string.IsNullOrEmpty(tilesJson))
            return playlists;

        var seen = new HashSet<string>();
        try
        {
            using (var doc = System.Text.Json.JsonDocument.Parse(tilesJson))
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    string href = el.TryGetProperty("u", out var pu) ? pu.GetString() : "";
                    string name = el.TryGetProperty("t", out var pt) ? pt.GetString() : "";
                    string poster = el.TryGetProperty("p", out var pp) ? pp.GetString() : "";
                    string alt = el.TryGetProperty("a", out var pa) ? pa.GetString() : "";
                    AddTile(playlists, seen, uri, href, name, poster, alt);
                }
            }
        }
        catch { }

        return playlists;
    }

    static void AddTile(List<Shared.Models.SISI.Base.PlaylistItem> playlists, HashSet<string> seen, string uri, string href, string name, string poster, string alt)
    {
            if (string.IsNullOrEmpty(href) || href == "#")
                return;
            int fi = href.IndexOf('#');
            if (fi >= 0)
                href = href.Substring(0, fi);
            if (href.StartsWith("/"))
                href = SiteHost + href;
            if (!href.StartsWith("http") || !seen.Add(href))
                return;
            // fallback: tile chua hydrate title (name rong hoac chi la duration)
            // -> dung alt (ma phim, vd mond-287)
            string display = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
            if ((string.IsNullOrEmpty(display) || Regex.IsMatch(display, @"^\d{1,3}:\d{2}(:\d{2})?$")) && !string.IsNullOrWhiteSpace(alt))
                display = alt.Trim();
            if (string.IsNullOrEmpty(display))
                return;
            if (!string.IsNullOrEmpty(poster) && poster.StartsWith("//"))
                poster = "https:" + poster;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = System.Net.WebUtility.HtmlDecode(display),
                picture = poster,
                json = true,
                bookmark = new Shared.Models.SISI.Base.Bookmark()
                {
                    site = "missav",
                    href = href,
                    image = poster
                }
            });
    }

    // giai eval(p,a,c,k,e,d) long nhau tren trang video -> list m3u8 surrit (master truoc)
    static readonly Regex EvalRx = new(
        @"eval\(function\(p,a,c,k,e,d\)\{.*?\}\('((?:[^'\\]|\\.)*)',(\d+),(\d+),'((?:[^'\\]|\\.)*)'\.split\('\|'\),0,\{\}\)\)",
        RegexOptions.Singleline);

    static string DecodePack(string p, int a, int c, string[] k)
    {
        string Unbase(int n)
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ+/";
            if (n < a)
                return chars[n].ToString();
            string s = "";
            while (n > 0)
            {
                s = chars[n % a] + s;
                n /= a;
            }
            return s;
        }
        while (c-- > 0)
        {
            string w = Unbase(c);
            if (!string.IsNullOrEmpty(w) && c < k.Length && !string.IsNullOrEmpty(k[c]))
                p = Regex.Replace(p, @"\b" + Regex.Escape(w) + @"\b", k[c]);
        }
        return p;
    }

    public static List<string> StreamUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        string decoded = html;
        for (int i = 0; i < 5; i++)
        {
            var m = EvalRx.Match(decoded);
            if (!m.Success)
                break;
            try
            {
                string p = m.Groups[1].Value.Replace("\\'", "'");
                int a = int.Parse(m.Groups[2].Value);
                int c = int.Parse(m.Groups[3].Value);
                string[] k = m.Groups[4].Value.Split('|');
                string one = DecodePack(p, a, c, k);
                decoded = decoded.Substring(0, m.Index) + one + decoded.Substring(m.Index + m.Length);
            }
            catch { break; }
        }

        // m3u8 da giai ma (co the \\/ escaped) + ca dang raw trong JSON seek
        string flat = decoded.Replace("\\/", "/");
        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(flat, @"'(https?://[^']+?\.m3u8[^']*?)'"))
        {
            string u = m.Groups[1].Value;
            if (seen.Add(u))
                urls.Add(u);
        }
        foreach (Match m in Regex.Matches(flat, @"""(https?://[^""]+?\.m3u8[^""]*?)"""))
        {
            string u = m.Groups[1].Value;
            if (seen.Add(u))
                urls.Add(u);
        }

        // master playlist.m3u8 len truoc, variant (1080p/720p/360p) sau
        urls.Sort((x, y) =>
        {
            int sx = x.Contains("playlist.m3u8") ? 0 : 1;
            int sy = y.Contains("playlist.m3u8") ? 0 : 1;
            if (sx != sy)
                return sx.CompareTo(sy);
            return string.Compare(y, x, StringComparison.Ordinal);
        });

        return urls;
    }

    public static bool IsStreamHost(string url)
    {
        return !string.IsNullOrEmpty(url) && url.Contains("surrit.com");
    }

    // surrit/fourhoi/missav API: curl --http2 + full Chrome UA + referer = 200
    // (.NET HttpClient stall/403 tren cac host nay)
    public static async Task<string> CurlGet(string url, string referer)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/curl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-sL");
            psi.ArgumentList.Add("--http2");
            psi.ArgumentList.Add("--compressed");
            psi.ArgumentList.Add("--connect-timeout");
            psi.ArgumentList.Add("10");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add("25");
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add(ChromeUA);
            if (!string.IsNullOrEmpty(referer))
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(referer);
            }
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    return null;
                string stdout = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return p.ExitCode == 0 ? stdout : null;
            }
        }
        catch { return null; }
    }

    public static async Task<byte[]> CurlGetBytes(string url, string referer)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/curl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-sL");
            psi.ArgumentList.Add("--http2");
            psi.ArgumentList.Add("--compressed");
            psi.ArgumentList.Add("--connect-timeout");
            psi.ArgumentList.Add("10");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add("40");
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add(ChromeUA);
            if (!string.IsNullOrEmpty(referer))
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(referer);
            }
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using (var p = Process.Start(psi))
            using (var ms = new System.IO.MemoryStream())
            {
                if (p == null)
                    return null;
                await p.StandardOutput.BaseStream.CopyToAsync(ms);
                await p.WaitForExitAsync();
                return p.ExitCode == 0 && ms.Length > 0 ? ms.ToArray() : null;
            }
        }
        catch { return null; }
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        string cat(string missavUrl) => host + "/missav?c=" + HttpUtility.UrlEncode(missavUrl);
        const string H = "https://missav.live";

        var genres = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("VR", cat(H + "/vi/genres/VR")),
            new("Nghiệp dư", cat(H + "/vi/genres/nghi%E1%BB%87p%20d%C6%B0")),
            new("Nữ sinh", cat(H + "/vi/genres/n%E1%BB%AF%20sinh")),
            new("Phụ nữ trưởng thành", cat(H + "/vi/genres/ph%E1%BB%A5%20n%E1%BB%AF%20tr%C6%B0%E1%BB%9Fng%20th%C3%A0nh")),
            new("Ngực to", cat(H + "/vi/genres/ng%E1%BB%B1c%20to")),
            new("Ngực đẹp", cat(H + "/vi/genres/ng%E1%BB%B1c%20%C4%91%E1%BA%B9p")),
            new("Loạn luân", cat(H + "/vi/genres/lo%E1%BA%A1n%20lu%C3%A2n")),
            new("Bắn tinh", cat(H + "/vi/genres/b%E1%BA%AFn%20tinh")),
            new("Thổi kèn", cat(H + "/vi/genres/th%E1%BB%95i%20k%C3%A8n")),
            new("Thủ dâm", cat(H + "/vi/genres/th%E1%BB%A7%20d%C3%A2m")),
            new("Mảnh khảnh", cat(H + "/vi/genres/m%E1%BA%A3nh%20kh%E1%BA%A3nh")),
            new("Cô gái xinh đẹp", cat(H + "/vi/genres/c%C3%B4%20g%C3%A1i%20xinh%20%C4%91%E1%BA%B9p")),
            new("Phim tài liệu", cat(H + "/vi/genres/phim%20t%C3%A0i%20li%E1%BB%87u")),
            new("Nampa", cat(H + "/vi/genres/Nampa")),
            new("Gonzo", cat(H + "/vi/genres/Gonzo")),
            new("4K", cat(H + "/vi/genres/4K")),
            new("Hi-vision", cat(H + "/vi/genres/hi-vision")),
            new("Paizuri", cat(H + "/vi/genres/paizuri")),
            new("Không kiểm duyệt", cat(H + "/vi/genres/Lo%E1%BA%A1i%20tr%E1%BB%AB")),
        };

        var makers = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("SIRO", cat(H + "/dm36/vi/siro")),
            new("LUXU", cat(H + "/dm34/vi/luxu")),
            new("GANA", cat(H + "/dm34/vi/gana")),
            new("ARA", cat(H + "/dm34/vi/ara")),
            new("FC2", cat(H + "/dm597/vi/fc2")),
            new("HEYZO", cat(H + "/dm2208642/vi/heyzo")),
            new("Tokyo Hot", cat(H + "/dm42/vi/tokyohot")),
            new("1pondo", cat(H + "/dm5199603/vi/1pondo")),
            new("Caribbeancom", cat(H + "/dm7704788/vi/caribbeancom")),
            new("10musume", cat(H + "/dm7208981/vi/10musume")),
            new("Madou", cat(H + "/dm63/vi/madou")),
            new("Gachinco", cat(H + "/dm150/vi/gachinco")),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/missav"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới / Hot",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/missav"),
                    new("Recent update", cat(H + "/dm539/vi/new")),
                    new("Bản phát hành mới", cat(H + "/dm635/vi/release")),
                    new("Rò rỉ không kiểm duyệt", cat(H + "/dm817/vi/uncensored-leak")),
                    new("Xem nhiều hôm nay", cat(H + "/dm301/vi/today-hot")),
                    new("Xem nhiều tuần này", cat(H + "/dm170/vi/weekly-hot")),
                    new("Xem nhiều tháng này", cat(H + "/dm273/vi/monthly-hot")),
                    new("Phụ đề tiếng Anh", cat(H + "/dm23/vi/english-subtitle")),
                }
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Hãng phim",
                playlist_url = "submenu",
                submenu = makers
            }
        };
    }
}
