using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web;

namespace Tizam;

public static class TizamTo
{
    public static string Uri(string host, int pg)
        => Uri(host, null, pg);

    public static string Uri(string host, string c, int pg)
    {
        string uri;

        if (string.IsNullOrWhiteSpace(c))
        {
            uri = $"{host}/fil_my_dlya_vzroslyh/s_russkim_perevodom/";
        }
        else
        {
            // chip popup: "cat/<slug>" | "tag/<slug>" | "actress/<slug>"
            string slug = c.Trim('/');

            if (slug.StartsWith("cat/", StringComparison.OrdinalIgnoreCase))
                uri = $"{host}/fil_my_dlya_vzroslyh/{slug.Substring(4)}/";
            else if (slug.StartsWith("tag/", StringComparison.OrdinalIgnoreCase))
                uri = $"{host}/podborki/{slug.Substring(4)}/";
            else if (slug.StartsWith("actress/", StringComparison.OrdinalIgnoreCase))
                uri = $"{host}/aktrisy/{slug.Substring(8)}/";
            else
                uri = $"{host}/fil_my_dlya_vzroslyh/{slug}/";
        }

        int page = pg - 1;
        if (page > 0)
            uri += $"?p={page}";

        return uri;
    }

    public static List<PlaylistItem> Playlist(string host, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        var pagination = Rx.Split("id=\"pagination\"", html);
        if (pagination.Count == 0)
            return null;

        var rx = Rx.Split("video-item", pagination[0].Span, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            if (row.Contains("pin--premium"))
                continue;

            string title = row.Match("-name=\"name\">([^<]+)<");
            // Trang moi them `class="item__cover"` GIUA href va itemprop,
            // nen cho phep bat ky thuoc tinh nao xen vao: `[^>]*`.
            string href = row.Match("href=\"/([^\"]+)\"[^>]*itemprop=\"url\"");

            if (string.IsNullOrEmpty(href) || string.IsNullOrWhiteSpace(title))
                continue;

            string img = row.Match("class=\"item__img\" src=\"/([^\"]+)\"");
            if (img == null)
                continue;

            string imageUrl = $"{host}/{img}";

            playlists.Add(new PlaylistItem()
            {
                name = title,
                video = $"tizam/vidosik?uri={HttpUtility.UrlEncode(href)}",
                picture = imageUrl,
                time = row.Match("itemprop=\"duration\" content=\"([^<]+)\"", trim: true),
                json = true,
                bookmark = new Bookmark()
                {
                    site = "tizam",
                    href = href,
                    image = imageUrl
                }
            });
        }

        return playlists;
    }

    #region Popup (chips -> /tizam/genres?uri= + "Похожие фильмы")
    // Detail (UMI CMS theme tizam):
    //   <li><span>Категория:</span><a href="/fil_my_dlya_vzroslyh/<slug>/">Tên</a></li>
    //   <div class="film__tags"> <a href="/podborki/<slug>/">Tên</a>
    //   <a class="actor__cover" href="/aktrisy/<slug>/"><h3 class="actor__title">Tên</h3></a>
    //   "Похожие фильмы" -> <div class="row same_video"> card <div class="item item--big">
    static readonly Dictionary<string, string> CatVi = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all_sex"] = "Tất cả",
        ["anal_seks_bol_shie_popki"] = "Hậu môn / Mông lớn",
        ["aziatki"] = "Châu Á",
        ["bol_shaya_grud"] = "Ngực lớn",
        ["dominirovanie"] = "Thống trị",
        ["groupvideo"] = "Quan hệ nhóm",
        ["incest"] = "Loạn luân",
        ["italyan_porn"] = "Ý",
        ["klassika"] = "Kinh điển",
        ["minet"] = "Khẩu giao",
        ["nemeckie_pornofil_my"] = "Đức",
        ["novinki"] = "Mới nhất",
        ["podrostki_18"] = "Trẻ 18+",
        ["polnometrazhnye"] = "Phim dài",
        ["pyshechki"] = "Đầy đặn",
        ["russkoe_porno"] = "Nga",
        ["s_russkim_perevodom"] = "Thuyết minh Nga",
        ["svingery"] = "Swinger",
        ["temnokozhie"] = "Da màu",
        ["zhenskaya_masturbaciya"] = "Nữ thủ dâm",
        ["zrelye"] = "Trưởng thành"
    };

    static readonly Dictionary<string, string> TagVi = new(StringComparer.OrdinalIgnoreCase)
    {
        ["macheha"] = "Mẹ kế",
        ["porno_hd"] = "Porno HD",
        ["bolshie_chleny"] = "Dương vật lớn",
        ["bryunetki"] = "Tóc nâu",
        ["milfy"] = "Milf",
        ["natural_nye_sis_ki"] = "Ngực tự nhiên",
        ["izmena"] = "Ngoại tình",
        ["ot_pervogo_lica"] = "Góc nhìn thứ nhất",
        ["wupat_ili_trogat"] = "Sờ mó",
        ["premium_vip"] = "Premium",
        ["pervmom"] = "PervMom",
        ["blondinki"] = "Tóc vàng",
        ["anal"] = "Hậu môn",
        ["lesbi"] = "Đồng tính nữ",
        ["kasting"] = "Casting",
        ["molodye"] = "Trẻ",
        ["krupnye_sis_ki"] = "Ngực khủng",
        ["minet_deep"] = "Khẩu giao sâu",
        ["chuvstvitel_nye"] = "Nhẹ nhàng",
        ["orgiya"] = "Thác loạn",
        ["chuzhie_zheny"] = "Vợ người khác",
        ["zhmzh"] = "Hai nữ một nam",
        ["mzhm"] = "Hai nam một nữ",
        ["russkie"] = "Nga",
        ["realnyj_seks"] = "Sex thật",
        ["domashnee"] = "Tự quay",
        ["lubiteli"] = "Amateur",
        ["studijnye"] = "Studio",
        ["chuvstva"] = "Cảm xúc",
        ["chuzhie-zheny"] = "Vợ người khác",
        ["bol_shie_dojki"] = "Ngực khủng",
        ["para"] = "Cặp đôi",
        ["zrelye_i_molodye_zhenwiny"] = "Trưởng thành và trẻ",
        ["pocelui"] = "Hôn nhau",
        ["populyarnoe_u_zhenwin"] = "Phổ biến với phụ nữ"
    };

    static string PrettySlug(string slug)
    {
        string s = slug.Replace('_', ' ').Replace('-', ' ');
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }

    public static string DetailUrl(string host, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        string baseHost = (host ?? "https://amu.tizam.org").TrimEnd('/');

        if (!uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return baseHost + "/" + uri.Trim('/') + "/";

        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return null;

        string h = u.Host.ToLowerInvariant();
        if (h == "amu.tizam.org" || h.EndsWith(".tizam.org") || h == "tizam.pw" || h.EndsWith(".tizam.pw"))
            return baseHost + u.AbsolutePath;

        return null;
    }

    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Thể loại: <li><span>Категория:</span><a href="/fil_my_dlya_vzroslyh/<slug>/">
        int i = html.IndexOf("Категория:", StringComparison.OrdinalIgnoreCase);
        if (i > 0)
        {
            string region = html.Substring(i, Math.Min(800, html.Length - i));
            foreach (Match m in Regex.Matches(region, "<a[^>]*href=\"(?:https?://[^\"]+)?/fil_my_dlya_vzroslyh/([^\"/]+)/\"[^>]*>[\\s\\S]{0,200}?</a>", RegexOptions.IgnoreCase))
            {
                string slug = m.Groups[1].Value.Trim();
                if (slug.Length == 0 || !seen.Add("cat/" + slug))
                    continue;

                string name = Regex.Replace(m.Value, "<[^>]+>", "");
                name = HttpUtility.HtmlDecode(name).Trim();
                if (name.Length == 0)
                    name = slug.Replace('_', ' ');

                if (CatVi.TryGetValue(slug, out string vi))
                    name = vi;
                else
                    name = PrettySlug(slug);

                list.Add(("cat", slug, name));
            }
        }

        // Thẻ: <div class="film__tags"> ... /podborki/<slug>/
        i = html.IndexOf("class=\"film__tags\"", StringComparison.OrdinalIgnoreCase);
        if (i > 0)
        {
            int end = html.IndexOf("</ul>", i, StringComparison.OrdinalIgnoreCase);
            string region = end > i ? html.Substring(i, end - i) : html.Substring(i, Math.Min(3000, html.Length - i));

            foreach (Match m in Regex.Matches(region, "<a[^>]*href=\"(?:https?://[^\"]+)?/podborki/([^\"/]+)/\"[^>]*>([^<]{1,120})</a>", RegexOptions.IgnoreCase))
            {
                string slug = m.Groups[1].Value.Trim();
                if (slug.Length == 0 || !seen.Add("tag/" + slug))
                    continue;

                string name = TagVi.TryGetValue(slug, out string vi) ? vi : PrettySlug(slug);
                list.Add(("tag", slug, name));
            }
        }

        // Diễn viên: actor__cover -> /aktrisy/<slug>/ (tên Latin, giữ nguyên)
        foreach (Match m in Regex.Matches(html, "<a[^>]*class=\"actor__cover\"[^>]*href=\"(?:https?://[^\"]+)?/aktrisy/([^\"/]+)/\"[^>]*>[\\s\\S]{0,600}?<h3 class=\"actor__title\">([^<]+)</h3>", RegexOptions.IgnoreCase))
        {
            string slug = m.Groups[1].Value.Trim();
            if (slug.Length == 0 || !seen.Add("actress/" + slug))
                continue;

            list.Add(("actress", slug, HttpUtility.HtmlDecode(m.Groups[2].Value).Trim()));
        }

        return list;
    }

    public static List<PlaylistItem> PlaylistRelated(string host, string html, string self)
    {
        if (string.IsNullOrEmpty(html))
            return new List<PlaylistItem>();

        int i = html.IndexOf("row same_video", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<PlaylistItem>();

        var list = new List<PlaylistItem>();

        foreach (var row in Rx.Split("item item--big", html.Substring(i)).Rows())
        {
            var g = row.Groups("href=\"(?:https?://[^\"]+)?/((?:fil_my_dlya_vzroslyh|cinema)/[^\"]+)/\"[^>]*itemprop=\"url\"");
            string link = g[1].Value.Trim();

            if (link.Length == 0 || link.Equals(self.Trim('/'), StringComparison.OrdinalIgnoreCase))
                continue;

            string name = row.Match("<span class=\"title\" itemprop=\"name\"[^>]*>([^<]+)</span>", trim: true);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string img = row.Match("class=\"item__img\" src=\"/([^\"]+)\"", trim: true);

            list.Add(new PlaylistItem()
            {
                name = HttpUtility.HtmlDecode(name),
                video = $"tizam/vidosik?uri={HttpUtility.UrlEncode(link)}",
                picture = img == null ? null : $"{host}/{img}",
                time = row.Match("itemprop=\"duration\" content=\"([^\"]+)\"", trim: true),
                json = true,
                bookmark = new Bookmark()
                {
                    site = "tizam",
                    href = link,
                    image = img == null ? null : $"{host}/{img}"
                }
            });
        }

        return list;
    }
    #endregion

    async public static Task<StreamItem> Stream(HttpHydra httpHydra, string host, string uri)
    {
        string location = null;

        await httpHydra.GetSpan($"{host}/{uri}", span =>
        {
            location = Rx.Match(span, "src=\"(https?://[^\"]+\\.mp4)\" type=\"video/mp4\"");
        });

        if (string.IsNullOrEmpty(location))
            return null;

        return new StreamItem()
        {
            qualitys = new Dictionary<string, string>()
            {
                ["auto"] = location
            }
        };
    }
}
