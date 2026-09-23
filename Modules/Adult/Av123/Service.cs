using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Av123;

public static class Av123To
{
    public static string SiteHost = "https://123av.com";

    // /vi neu co, khong thi /en (da verify /vi 200)
    public static string Lang = "vi";

    public static string ChromeUA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Mobile Safari/537.36";

    public static string Uri(string host, string search, string c, int pg)
    {
        if (string.IsNullOrEmpty(host))
            host = SiteHost;

        string page = pg > 1 ? "?page=" + pg : "";

        if (!string.IsNullOrWhiteSpace(search))
            return host + "/" + Lang + "/search?keyword=" + HttpUtility.UrlEncode(search.Trim());

        if (!string.IsNullOrEmpty(c))
        {
            string url = c.StartsWith("http") ? c : host + "/" + Lang + "/" + c.Trim('/');
            return url + (pg > 1 ? (url.Contains("?") ? "&" : "?") + "page=" + pg : "");
        }

        // home /vi tu lap ~70% giua cac page (pool recommendation xoay vong)
        // -> mac dinh dung /vi/new (phan trang chuan, overlap 0)
        return host + "/" + Lang + "/new" + page;
    }

    // tilesJson: [{u,t,p,d}] do Playwright EvaluateAsync tra ve sau khi Alpine render
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
                    string dur = el.TryGetProperty("d", out var pd) ? pd.GetString() : "";
                    AddTile(playlists, seen, uri, href, name, poster, dur);
                }
            }
        }
        catch { }

        return playlists;
    }

    static void AddTile(List<Shared.Models.SISI.Base.PlaylistItem> playlists, HashSet<string> seen, string uri, string href, string name, string poster, string dur)
    {
        if (string.IsNullOrEmpty(href) || href == "#")
            return;
        int fi = href.IndexOf('#');
        if (fi >= 0)
            href = href.Substring(0, fi);
        if (href.StartsWith("/"))
            href = SiteHost + href;
        if (!href.StartsWith("http"))
            return;
        // dedupe theo slug (site link ca /vi/v/X lan /en/v/X cho cung 1 phim)
        {
            var sm = Regex.Match(href.ToLowerInvariant(), @"/v/([a-z0-9\-]+)/?$");
            string key = sm.Success ? sm.Groups[1].Value : href.ToLowerInvariant();
            if (!seen.Add(key))
                return;
        }
        string display = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
        if (string.IsNullOrEmpty(display))
        {
            // fallback ma phim tu slug
            var m = Regex.Match(href, @"/v/([a-z0-9\-]+)/?$");
            if (m.Success)
                display = m.Groups[1].Value.ToUpperInvariant();
        }
        if (string.IsNullOrEmpty(display))
            return;

        // duration giay -> h:mm:ss
        string dd = "";
        if (!string.IsNullOrEmpty(dur) && Regex.IsMatch(dur.Trim(), @"^\d+$") && long.TryParse(dur.Trim(), out long sec) && sec > 0)
        {
            long h = sec / 3600, mm = (sec % 3600) / 60, ss = sec % 60;
            dd = h > 0 ? $"{h}:{mm:D2}:{ss:D2}" : $"{mm}:{ss:D2}";
        }
        if (!string.IsNullOrEmpty(dd))
            display = dd + "\n" + display;

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
                site = "av123",
                href = href,
                image = poster
            }
        });
    }

    // trang video: iframe javplayer.cc/e/{hash} (mp4 bkcdn tren trang la QUANG CAO, bo qua)
    // flow that: GET /stream?id={hash}&poster=... -> {"media":{"stream":"...m3u8","vtt":"..."}}
    public static (string hash, string poster) EmbedInfo(string html)
    {
        if (string.IsNullOrEmpty(html))
            return (null, null);

        var m = Regex.Match(html, @"<iframe[^>]*src=""(https?://javplayer\.cc/e/([a-zA-Z0-9_]+)[^""]*)""");
        if (!m.Success)
            return (null, null);

        string embed = m.Groups[1].Value;
        string hash = m.Groups[2].Value;
        string poster = "";
        var pm = Regex.Match(embed, @"[?&]poster=([^&""]+)");
        if (pm.Success)
            poster = pm.Groups[1].Value;

        return (hash, poster);
    }

    public static string StreamJsonUrl(string hash, string poster)
    {
        string url = "https://javplayer.cc/stream?id=" + hash;
        if (!string.IsNullOrEmpty(poster))
            url += "&poster=" + poster;
        return url;
    }

    public static (string master, string vtt) ParseStreamJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return (null, null);
        try
        {
            using (var doc = System.Text.Json.JsonDocument.Parse(json))
            {
                if (!doc.RootElement.TryGetProperty("media", out var media))
                    return (null, null);
                string master = media.TryGetProperty("stream", out var s) ? s.GetString() : null;
                string vtt = media.TryGetProperty("vtt", out var v) ? v.GetString() : null;
                if (string.IsNullOrEmpty(master))
                    return (null, null);
                return (master, vtt);
            }
        }
        catch { return (null, null); }
    }

    // (cu) trang video: <video src="...mp4"> direct tren CDN nha (bkcdn.net)
    public static async Task<string> CurlGet(string url, string referer, int retries = 2)
    {
        for (int attempt = 0; ; attempt++)
        {
            string s = await CurlGetOnceAsync(url, referer);
            if (!string.IsNullOrEmpty(s))
                return s;
            if (attempt >= retries)
                return null;
            try { await Task.Delay(500 * (attempt + 1)); } catch { }
        }
    }

    static async Task<string> CurlGetOnceAsync(string url, string referer)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/curl")
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
            psi.ArgumentList.Add("12");
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add(ChromeUA);
            if (!string.IsNullOrEmpty(referer))
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(referer);
            }
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using (var p = System.Diagnostics.Process.Start(psi))
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

    public static async Task<(byte[] data, int status, string contentRange)> CurlGetRangeAsync(string url, string referer, string range, int retries = 2)
    {
        for (int attempt = 0; ; attempt++)
        {
            var r = await CurlGetRangeOnceAsync(url, referer, range);
            // retry khi that bai tam thoi (null/rong/timeout); 416 la that (range sai) -> khong retry
            if (r.data != null && r.data.Length > 0)
                return r;
            if (r.status == 416 || attempt >= retries)
                return r;
            try { await Task.Delay(500 * (attempt + 1)); } catch { }
        }
    }

    static async Task<(byte[] data, int status, string contentRange)> CurlGetRangeOnceAsync(string url, string referer, string range)
    {
        string hdrFile = System.IO.Path.Combine("/tmp", "av123h" + Guid.NewGuid().ToString("N"));
        string binFile = System.IO.Path.Combine("/tmp", "av123b" + Guid.NewGuid().ToString("N"));
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/curl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add("-D");
            psi.ArgumentList.Add(hdrFile);
            psi.ArgumentList.Add("--http2");
            psi.ArgumentList.Add("--compressed");
            psi.ArgumentList.Add("--connect-timeout");
            psi.ArgumentList.Add("8");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add("15");
            psi.ArgumentList.Add("-A");
            psi.ArgumentList.Add(ChromeUA);
            if (!string.IsNullOrEmpty(referer))
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(referer);
            }
            if (!string.IsNullOrEmpty(range))
            {
                var m = System.Text.RegularExpressions.Regex.Match(range, @"bytes\s*=\s*(\d*-\d*)");
                if (m.Success)
                {
                    psi.ArgumentList.Add("-r");
                    psi.ArgumentList.Add(m.Groups[1].Value);
                }
            }
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(binFile);
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(url);

            using (var p = System.Diagnostics.Process.Start(psi))
            {
                if (p == null)
                    return (null, 0, null);
                await p.WaitForExitAsync();
                if (p.ExitCode != 0)
                    return (null, 0, null);
                byte[] data = System.IO.File.ReadAllBytes(binFile);
                if (data.Length == 0)
                    return (null, 0, null);
                int status = 200;
                string contentRange = null;
                try
                {
                    foreach (string line in System.IO.File.ReadAllLines(hdrFile))
                    {
                        if (line.StartsWith("HTTP/") && line.Contains(" 206"))
                            status = 206;
                        else if (line.StartsWith("HTTP/") && line.Contains(" 416"))
                            status = 416;
                        if (line.StartsWith("Content-Range:", StringComparison.OrdinalIgnoreCase))
                            contentRange = line.Substring(14).Trim();
                    }
                }
                catch { }
                return (data, status, contentRange);
            }
        }
        catch { return (null, 0, null); }
        finally
        {
            try { System.IO.File.Delete(hdrFile); } catch { }
            try { System.IO.File.Delete(binFile); } catch { }
        }
    }

    public static async Task<byte[]> CurlGetBytes(string url, string referer)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/curl")
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

            using (var p = System.Diagnostics.Process.Start(psi))
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

    // (du phong) trang video: <video src> direct
    public static List<string> StreamUrls(string html)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(html))
            return urls;

        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, @"<video[^>]*src=""(https?://[^""]+)"""))
        {
            if (seen.Add(m.Groups[1].Value))
                urls.Add(m.Groups[1].Value);
        }
        foreach (Match m in Regex.Matches(html, @"<source[^>]*src=""(https?://[^""]+)"""))
        {
            if (seen.Add(m.Groups[1].Value))
                urls.Add(m.Groups[1].Value);
        }

        return urls;
    }

    public static bool IsStreamHost(string url)
    {
        return !string.IsNullOrEmpty(url) && (url.Contains("bkcdn.net") || url.Contains(".mp4"));
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(string host)
    {
        // chi slug da verify tren site (site dung ca hash ID, khong doan bua)
        var genres = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new("VR", host + "/123av?c=genres/VR"),
            new("Eros", host + "/123av?c=genres/eros"),
            new("Gonzo", host + "/123av?c=genres/Gonzo"),
            new("Nampa", host + "/123av?c=genres/Nampa"),
            new("4K", host + "/123av?c=genres/4K"),
            new("Hi-vision", host + "/123av?c=genres/hi-vision"),
            new("Paizuri", host + "/123av?c=genres/paizuri"),
            new("Đồng tính nữ", host + "/123av?c=genres/lesbian"),
            new("Thổi kèn", host + "/123av?c=genres/e22f5b4c15"),
            new("Đồng tính nữ 2", host + "/123av?c=genres/f0f0e4571c"),
            new("Sáu chín", host + "/123av?c=genres/14bfa6bb14"),
            new("Chippai", host + "/123av?c=genres/0262798b1a"),
            new("Mông đẹp", host + "/123av?c=genres/7520ed52ed"),
            new("Ngực nhỏ", host + "/123av?c=genres/70d2b9a4d3"),
        };

        return new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/123av"
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Mới / Hot",
                playlist_url = "submenu",
                submenu = new List<Shared.Models.SISI.Base.MenuItem>()
                {
                    new("Mới nhất", host + "/123av"),
                    new("Mới cập nhật", host + "/123av?c=new"),
                    new("Đang hot", host + "/123av?c=hot"),
                    new("Xem gần đây", host + "/123av?c=recent"),
                    new("Có che", host + "/123av?c=censored"),
                    new("Không che", host + "/123av?c=uncensored"),
                    new("Rò rỉ không che", host + "/123av?c=uncensored-leaked"),
                }
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genres
            }
        };
    }
}
