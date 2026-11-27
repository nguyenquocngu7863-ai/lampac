using Shared.Models.SISI.Base;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Sxyprn;

public static class SxyprnTo
{
    public const string SiteHost = "https://www.sxyprn.com";
    public const int TagPerPage = 30;

    public const string ChromeUA =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public const string TopUrl = SiteHost + "/popular/top-pop.html";
    public const string ViewedUrl = SiteHost + "/popular/top-viewed.html";

    public static string TagUrl(string name, string sort, int pg)
        => $"{SiteHost}/{Uri.EscapeDataString(name)}.html?page={Math.Max(0, pg - 1) * TagPerPage}&sm={Sort(sort)}";

    public static string Sort(string s) => s switch
    {
        "trending" => "trending",
        "views" => "views",
        "orgasmic" => "orgasmic",
        _ => "latest"
    };

    public static string SortLabel(string s) => s switch
    {
        "trending" => "Thịnh hành",
        "views" => "Xem nhiều",
        "orgasmic" => "Cực khoái",
        _ => "Mới nhất"
    };

    public static string VideoId(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        var m = Regex.Match(uri, @"/post/([0-9a-z]+)\.html");
        if (m.Success)
            return m.Groups[1].Value;

        string t = uri.Trim();
        return t.Length >= 8 && t.All(char.IsLetterOrDigit) ? t : null;
    }

    public static string DetailUrl(string id) => SiteHost + "/post/" + id + ".html";

    static string Pic(string p)
    {
        if (string.IsNullOrEmpty(p))
            return null;
        return p.StartsWith("//") ? "https:" + p : p;
    }

    static string Clean(string t)
    {
        if (string.IsNullOrEmpty(t))
            return "";

        t = HttpUtility.HtmlDecode(t);
        t = Regex.Replace(t, @"https?://\S+", "");
        t = Regex.Replace(t, @"\{[^}]*\}", "");
        t = Regex.Replace(t, @"#\S+", "");
        t = Regex.Replace(t, @"\s+", " ").Trim(' ', '-', '|');
        return t;
    }

    // <span class='duration_small' style='...' title='s10->c7'>02:01:48</span>
    // attribute co the chua '>' nen cat theo dau '>' cuoi cung truoc </span>
    static string Duration(string part)
    {
        int i = part.IndexOf("duration_small", StringComparison.Ordinal);
        if (i < 0)
            return null;

        int j = part.IndexOf("</span>", i, StringComparison.Ordinal);
        if (j < 0)
            return null;

        string chunk = part.Substring(i, j - i);
        string txt = chunk.Substring(chunk.LastIndexOf('>') + 1).Trim();
        txt = Regex.Replace(txt, "<[^>]+>", "").Trim();

        return Regex.IsMatch(txt, @"^\d{1,3}:\d{2}(:\d{2})?$") ? txt : null;
    }

    public static List<PlaylistItem> Playlist(string html, string route)
    {
        var list = new List<PlaylistItem>();
        if (string.IsNullOrEmpty(html))
            return list;

        var hits = Regex.Matches(html, "<a href='/post/");
        var seen = new HashSet<string>();

        for (int n = 0; n < hits.Count; n++)
        {
            int a = hits[n].Index;
            int b = n + 1 < hits.Count ? hits[n + 1].Index : html.Length;
            string part = html.Substring(a, b - a);

            var idm = Regex.Match(part, @"^<a href='/post/([0-9a-z]+)\.html");
            if (!idm.Success)
                continue;

            string id = idm.Groups[1].Value;
            if (!seen.Add(id))
                continue;

            string time = Duration(part);
            if (time == null)
                continue;

            string name = null;
            var tm = Regex.Match(part, @"post_time[^>]*title='([^']*)'");
            if (tm.Success)
                name = Clean(tm.Groups[1].Value);

            if (string.IsNullOrEmpty(name))
            {
                var am = Regex.Match(part, @"aria-label='([^']*)'");
                if (am.Success)
                    name = Clean(am.Groups[1].Value);
            }

            if (string.IsNullOrEmpty(name))
                name = id;

            var pm = Regex.Match(part, @"data-src='([^']+)'");
            string pic = pm.Success ? Pic(pm.Groups[1].Value) : null;

            list.Add(new PlaylistItem
            {
                video = route + "?uri=" + HttpUtility.UrlEncode(DetailUrl(id)),
                name = name,
                picture = pic,
                time = time,
                bookmark = new Bookmark
                {
                    site = "sxyprn",
                    href = DetailUrl(id),
                    image = pic
                }
            });
        }

        return list;
    }

    public static int Pages(string html)
    {
        var m = Regex.Match(html ?? "", @"- (\d+) videos on");
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out int total) || total <= 0)
            return 1;

        return Math.Max(1, (total + TagPerPage - 1) / TagPerPage);
    }

    // ---- chips (popup) ----

    public static List<(string kind, string name, string slug)> Chips(string html)
    {
        var chips = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return chips;

        int cut = html.IndexOf("Related by:", StringComparison.Ordinal);
        string head = cut > 0 ? html.Substring(0, cut) : html;

        var seen = new HashSet<string>();

        foreach (Match m in Regex.Matches(head, @"data-subkey='([^']+)' href='/([^']+?)\.html'"))
        {
            string name = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
            string slug = m.Groups[2].Value;
            if (name.Length == 0 || slug.Contains('/') || !seen.Add("a:" + slug))
                continue;
            chips.Add(("actor", name, slug));
        }

        foreach (Match m in Regex.Matches(head, "<a class=\"hash_link[^\"]*\" label=\"([^\"]+)\" href=\"/([^\"]+?)\\.html\\?sm="))
        {
            string name = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
            string slug = m.Groups[2].Value;
            if (name.Length == 0 || slug.Contains('/') || !seen.Add("t:" + slug))
                continue;
            chips.Add(("tag", name, slug));
        }

        return chips;
    }

    public static List<(string name, string slug)> Tags(string html, int take)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrEmpty(html))
            return res;

        var seen = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, "<a class=\"hash_link[^\"]*\" label=\"([^\"]+)\" href=\"/([^\"]+?)\\.html\\?sm="))
        {
            string name = HttpUtility.HtmlDecode(m.Groups[1].Value).Trim();
            string slug = m.Groups[2].Value;
            if (name.Length == 0 || slug.Contains('/') || !seen.Add(slug))
                continue;

            res.Add((name, slug));
            if (res.Count >= take)
                break;
        }

        return res;
    }

    // ---- stream: /cdn/<...> -> /cdn8/<b64>/... (JS getvsrc trong main2.js) ----

    static int DigitSum(string s)
    {
        int sum = 0;
        foreach (char c in s)
            if (c >= '0' && c <= '9')
                sum += c - '0';
        return sum;
    }

    static string Boo(int a, int b)
    {
        string raw = a.ToString(CultureInfo.InvariantCulture) + "-www.sxyprn.com-"
                   + b.ToString(CultureInfo.InvariantCulture);
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return b64.Replace('+', '-').Replace('/', '_').Replace('=', '.');
    }

    public static string StreamUrl(string html, string id)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        var m = Regex.Match(html, @"data-vnfo='(\{.*?\})'", RegexOptions.Singleline);
        if (!m.Success)
            return null;

        string src = null;
        try
        {
            using var doc = JsonDocument.Parse(m.Groups[1].Value);
            if (!string.IsNullOrEmpty(id) && doc.RootElement.TryGetProperty(id, out var own))
                src = own.GetString();
            else
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    src = p.Value.GetString();
                    break;
                }
            }
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(src) || !src.StartsWith("/cdn/", StringComparison.Ordinal))
            return null;

        var t = src.Split('/');
        if (t.Length < 8)
            return null;

        int a = DigitSum(t[6]);
        int b = DigitSum(t[7]);

        t[1] = t[1] + "8/" + Boo(a, b);
        if (!int.TryParse(t[5], out int exp))
            return null;
        t[5] = (exp - a - b).ToString(CultureInfo.InvariantCulture);

        return SiteHost + string.Join("/", t);
    }
}
