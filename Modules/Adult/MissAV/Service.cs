using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    public static string Uri(string host, string search, string c, int pg, string sort = null)
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
            string q = pg > 1 ? (url.Contains("?") ? "&" : "?") + "page=" + pg : "";

            // Sort cua site (dropdown "Sap xep theo") la QUERY `?sort=`,
            // dat sau duong dan co phan trang.
            if (!string.IsNullOrWhiteSpace(sort))
                q += (q.Length == 0 ? "?" : "&") + "sort=" + sort.Trim();

            return url + q;
        }

        // Trang chu mac dinh KHONG lay feed "/vi": do la Recombee goi y, khong
        // phan trang duoc, moi trang tra ve trung nhau. Thay bang row
        // "Ban phat hanh moi" — co ?page=N that.
        return ReleaseUrl + page;
    }

    public static string ReleaseUrl = "https://missav.live/dm635/vi/release";

    // Feed goi y (Recombee) — khong phan trang, chi dung lam 1 muc menu.
    public static string RecommendUrl = "https://missav.live/vi";

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
                    // `a` = alt cua <img> (ten day du), `c` = alt cua <a> (ma phim)
                    string alt = el.TryGetProperty("a", out var pa) ? pa.GetString() : "";
                    string code = el.TryGetProperty("c", out var pc) ? pc.GetString() : "";
                    AddTile(playlists, seen, uri, href, name, poster, alt, code);
                }
            }
        }
        catch { }

        return playlists;
    }

    static void AddTile(List<Shared.Models.SISI.Base.PlaylistItem> playlists, HashSet<string> seen, string uri, string href, string name, string poster, string alt, string code)
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
            string display = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();

            // innerText cua tile nhieu dong: dong dau/cuoi la THOI LUONG
            // ("2:39:35\nSNOS-334 ..." hoac "... \n2:39:35"). Phai tach
            // ra, neu khong ten phim se hien kem gio phim.
            string time = "";
            string[] lines = display.Split('\n')
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (lines.Length > 1)
            {
                var reDur = new Regex(@"^\d{1,3}:\d{2}(:\d{2})?$");
                if (reDur.IsMatch(lines[0]))
                {
                    time = lines[0];
                    lines = lines.Skip(1).ToArray();
                }
                else if (reDur.IsMatch(lines[lines.Length - 1]))
                {
                    time = lines[lines.Length - 1];
                    lines = lines.Take(lines.Length - 1).ToArray();
                }
                display = string.Join(" ", lines);
            }

            // innerText chi ra MA PHIM (vd "fays-017") khi Alpine chua hydrate
            // tieu de; luc do phai dung `alt` cua <img> (ten day du). `code`
            // (alt cua <a>) chi la ma phim -> chi de so sanh, khong hien thi.
            bool onlyCode = string.IsNullOrEmpty(display)
                || Regex.IsMatch(display, @"^\d{1,3}:\d{2}(:\d{2})?$")
                || (!string.IsNullOrWhiteSpace(code) && display == code.Trim());

            if (onlyCode && !string.IsNullOrWhiteSpace(alt))
                display = alt.Trim();
            if (string.IsNullOrEmpty(display))
                return;

            // Ma phim lam doi dau cho doi nhat: "FNS-258 <ten day du>".
            if (!string.IsNullOrWhiteSpace(code))
            {
                code = code.Trim();
                if (!display.StartsWith(code, StringComparison.OrdinalIgnoreCase))
                    display = code + " " + display;
            }
            if (!string.IsNullOrEmpty(poster) && poster.StartsWith("//"))
                poster = "https:" + poster;

            playlists.Add(new Shared.Models.SISI.Base.PlaylistItem()
            {
                video = uri + "?uri=" + HttpUtility.UrlEncode(href),
                name = System.Net.WebUtility.HtmlDecode(display),
                time = time,
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
    // Lampac native set LD_PRELOAD troi sang libseccomp-shim.so (ELF glibc) va
    // LD_LIBRARY_PATH troi sang thu muc glibc. Tien trinh curl cua Termux la
    // bionic nen khong "noi" duoc: Android linker bao
    //   CANNOT LINK EXECUTABLE "curl": .../glibc/lib/libc.so has bad ELF magic
    // -> CurlGet tra null -> ProxyOverride roi ve pipeline mac dinh cua Lampac
    // (HttpClient) -> Cloudflare 403 -> player bao loi manifest.
    // Phai goi bang duong dan tuyet doi va bo LD_PRELOAD cho process con.
    static void PrepCurlEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("LD_PRELOAD");
        psi.Environment.Remove("LD_LIBRARY_PATH");

        string curl = "/data/data/com.termux/files/usr/bin/curl";
        psi.FileName = System.IO.File.Exists(curl) ? curl : "curl";
    }

    // surrit.com Cloudflare chan lai xen ke; thu lai vai lan truoc khi bo cua.
    public static async Task<string> CurlGetRetry(string url, string referer, int attempts = 3)
    {
        for (int i = 0; i < attempts; i++)
        {
            string body = await CurlGet(url, referer);
            if (!string.IsNullOrWhiteSpace(body) && body.Contains("#EXTM3U"))
                return body;
            if (i < attempts - 1)
                await Task.Delay(500 * (i + 1));
        }
        return null;
    }

    public static async Task<string> CurlGet(string url, string referer)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            PrepCurlEnv(psi);
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
        catch
        {
            return null;
        }
    }

    public static async Task<byte[]> CurlGetBytes(string url, string referer)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            PrepCurlEnv(psi);
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
        => Menu(host, null, null);

    static readonly char[] TaxHidden =
        { '\uFEFF', '\u200B', '\u200C', '\u200D', '\u200E', '\u200F' };

    static string CleanTax(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Trim(TaxHidden).Trim();
    }

    /// <summary>
    /// Parse card taxonomy MissAV: anchor co class `text-nord13` VA href chua
    /// `/vi/genres|makers|actors|actresses/`. Class nay con dung cho link
    /// login nen bat buoc loc theo href. GiU NGUYEN full dm-URL (dm-ID la
    /// nhom noi dung, bo di la sai trang). Card dien vien de ten trong
    /// `img alt` chu khong phai text nen phai fallback alt.
    /// </summary>
    public static List<(string name, string url)> Taxonomies(string html)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(html, @"<a\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string tag = m.Value;
            if (tag.IndexOf("text-nord13", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var href = Regex.Match(tag, @"href\s*=\s*""([^""]+)""",
                RegexOptions.IgnoreCase);
            if (!href.Success)
                continue;

            string url = System.Net.WebUtility.HtmlDecode(href.Groups[1].Value.Trim());
            if (url.IndexOf("/vi/genres/", StringComparison.OrdinalIgnoreCase) < 0
                && url.IndexOf("/vi/makers/", StringComparison.OrdinalIgnoreCase) < 0
                && url.IndexOf("/vi/actors/", StringComparison.OrdinalIgnoreCase) < 0
                && url.IndexOf("/vi/actresses/", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            int end = html.IndexOf("</a>", m.Index, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
                continue;

            string inner = html.Substring(m.Index + tag.Length, end - m.Index - tag.Length);

            string name = CleanTax(System.Net.WebUtility.HtmlDecode(
                Regex.Replace(inner, "<[^>]+>", " ")));
            name = Regex.Replace(name, @"\s+", " ").Trim();

            // Card dien vien: ten nam trong img alt.
            if (string.IsNullOrEmpty(name))
            {
                var alt = Regex.Match(inner, @"alt\s*=\s*""([^""]{1,60})""",
                    RegexOptions.IgnoreCase);
                if (alt.Success)
                    name = CleanTax(System.Net.WebUtility.HtmlDecode(alt.Groups[1].Value));
            }

            if (string.IsNullOrEmpty(name) || name.Length > 60)
                continue;

            // Client SISI cat title bang `:` — ten co dau do bi cut.
            name = name.Replace(':', '-').Replace('|', '-');

            if (!seen.Add(url))
                continue;

            res.Add((name, url));
        }

        return res;
    }

    public static int TaxPages(string html, string page)
    {
        if (string.IsNullOrEmpty(html))
            return 1;

        int max = 1;
        foreach (Match m in Regex.Matches(html,
            Regex.Escape(page) + @"\?page=(\d+)", RegexOptions.IgnoreCase))
        {
            if (int.TryParse(m.Groups[1].Value, out int p) && p > max)
                max = p;
        }

        return max;
    }

    public static List<Shared.Models.SISI.Base.MenuItem> Menu(
        string host, List<(string name, string url)> genres,
        List<(string name, string url)> makers)
    {
        string cat(string missavUrl) => host + "/missav?c=" + HttpUtility.UrlEncode(missavUrl);

        List<Shared.Models.SISI.Base.MenuItem> genreMenu;
        if (genres != null && genres.Count > 0)
        {
            genreMenu = new List<Shared.Models.SISI.Base.MenuItem>(genres.Count);
            foreach (var (name, url) in genres)
                genreMenu.Add(new(name, cat(url)));
        }
        else
        {
            genreMenu = new List<Shared.Models.SISI.Base.MenuItem>()
            {
                new("VR", cat("https://missav.live/vi/genres/VR")),
                new("Nghiệp dư", cat("https://missav.live/vi/genres/nghi%E1%BB%87p%20d%C6%B0")),
                new("Nữ sinh", cat("https://missav.live/vi/genres/n%E1%BB%AF%20sinh")),
                new("Gonzo", cat("https://missav.live/vi/genres/Gonzo")),
                new("4K", cat("https://missav.live/vi/genres/4K")),
            };
        }

        List<Shared.Models.SISI.Base.MenuItem> makerMenu;
        if (makers != null && makers.Count > 0)
        {
            makerMenu = new List<Shared.Models.SISI.Base.MenuItem>(makers.Count);
            foreach (var (name, url) in makers)
                makerMenu.Add(new(name, cat(url)));
        }
        else
        {
            makerMenu = new List<Shared.Models.SISI.Base.MenuItem>()
            {
                new("S1", cat("https://missav.live/dm191/vi/makers/S1")),
                new("Madonna", cat("https://missav.live/dm350/vi/makers/Madonna")),
            };
        }

        // Nav goc cua site (mobile Alpine `showCollapse`, lay 2026-09-30).
        // Ten giu nguyen tieng Viet cua site. Bo 2 muc index `/genres` va
        // `/makers` (da co full list rieng), bo nhom login (vip/saved/
        // playlists/history) vi can tai khoan.
        const string H = "https://missav.live";

        List<Shared.Models.SISI.Base.MenuItem> G(
            params (string name, string url)[] items)
        {
            var list = new List<Shared.Models.SISI.Base.MenuItem>(items.Length);
            foreach (var (name, url) in items)
                list.Add(new(name, cat(url)));
            return list;
        }

        var root = new List<Shared.Models.SISI.Base.MenuItem>()
        {
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = host + "/missav"
            },
            // DÒNG 2 — Sắp xếp (công thức SISI 9g). `?sort=` là query
            // dropdown "Sắp xếp theo" của site, áp trên row "Xem nhiều
            // nhất hôm nay" (row này có đủ mọi kiểu sort trong dropdown).
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Sắp xếp",
                playlist_url = "submenu",
                submenu = G(
                    ("Mới nhất", H + "/dm539/vi/new"),
                    ("Bản phát hành mới", H + "/dm635/vi/release"),
                    ("Xem nhiều hôm nay", H + "/dm301/vi/today-hot"),
                    ("Xem nhiều tuần này", H + "/dm170/vi/weekly-hot"),
                    ("Xem nhiều tháng này", H + "/dm273/vi/monthly-hot"),
                    ("Xem nhiều tất cả", H + "/dm301/vi/today-hot?sort=views"),
                    ("Xếp theo ngày phát hành", H + "/dm301/vi/today-hot?sort=released_at"))
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "JAV",
                playlist_url = "submenu",
                submenu = G(
                    ("Rò rỉ không kiểm duyệt", H + "/dm817/vi/uncensored-leak"),
                    ("Danh sách nữ diễn viên", H + "/vi/actresses"),
                    ("BXH nữ diễn viên", H + "/vi/actresses/ranking"),
                    ("VR", H + "/vi/genres/VR"))
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Nghiệp dư",
                playlist_url = "submenu",
                submenu = G(
                    ("SIRO", H + "/dm36/vi/siro"),
                    ("LUXU", H + "/dm34/vi/luxu"),
                    ("GANA", H + "/dm34/vi/gana"),
                    ("MAAN", H + "/dm1004/vi/maan"),
                    ("S-CUTE", H + "/dm38/vi/scute"),
                    ("ARA", H + "/dm34/vi/ara"))
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Không kiểm duyệt",
                playlist_url = "submenu",
                submenu = G(
                    ("Rò rỉ không kiểm duyệt", H + "/dm817/vi/uncensored-leak"),
                    ("FC2", H + "/dm597/vi/fc2"),
                    ("HEYZO", H + "/dm2208642/vi/heyzo"),
                    ("Tokyo Hot", H + "/dm42/vi/tokyohot"),
                    ("1pondo", H + "/dm5199603/vi/1pondo"),
                    ("Caribbeancom", H + "/dm7704788/vi/caribbeancom"),
                    ("Caribbeancompr", H + "/dm91887/vi/caribbeancompr"),
                    ("10musume", H + "/dm7208981/vi/10musume"),
                    ("Pacopacomama", H + "/dm3600557/vi/pacopacomama"),
                    ("Gachinco", H + "/dm150/vi/gachinco"),
                    ("XXX-AV", H + "/dm42/vi/xxxav"),
                    ("Marriedslash", H + "/dm37/vi/marriedslash"),
                    ("Naughty 4610", H + "/dm33/vi/naughty4610"),
                    ("Naughty 0930", H + "/dm37/vi/naughty0930"))
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Châu Á AV",
                playlist_url = "submenu",
                submenu = G(
                    ("Madou", H + "/dm63/vi/madou"),
                    ("TWAV", H + "/dm31/vi/twav"),
                    ("Furuke", H + "/dm15/vi/furuke"),
                    ("Hàn Quốc trực tiếp", H + "/vi/klive"),
                    ("Trung Quốc trực tiếp", H + "/vi/clive"))
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Thể loại",
                playlist_url = "submenu",
                submenu = genreMenu
            },
            new Shared.Models.SISI.Base.MenuItem()
            {
                title = "Hãng phim",
                playlist_url = "submenu",
                submenu = makerMenu
            }
        };

        return root;
    }
}
