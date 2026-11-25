using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.Base;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Web;

namespace Ebalovo;

public static class EbalovoTo
{
    // slug danh mục -> tên tiếng Việt (dùng chung cho menu + popup)
    public static readonly (string slug, string title)[] CatSlugs = new (string, string)[]
    {
        ("cfnm", "CFNM"),
        ("pov", "pov"),
        ("anal-videos", "Hậu môn"),
        ("gape", "Nới rộng hậu môn"),
        ("butt-plug-porn", "Nút hậu môn"),
        ("bdsm-porn", "BDSM"),
        ("blonde", "Tóc vàng"),
        ("big-ass", "Mông lớn"),
        ("big-tits", "Ngực lớn"),
        ("big-cock", "Dương vật lớn"),
        ("bbc", "Dương vật đen lớn"),
        ("bondage", "Trói buộc"),
        ("boss", "Sếp"),
        ("shaved-pussy", "Đã cạo"),
        ("a1-brunette", "Tóc nâu"),
        ("bukkake", "Bukkake"),
        ("knee-socks", "Vớ cao"),
        ("club", "Trong club"),
        ("lingerie", "Đồ lót đẹp"),
        ("shirt", "Áo thun"),
        ("oiled", "Bôi dầu"),
        ("car-porn", "Trong xe"),
        ("glasses", "Đeo kính"),
        ("condom", "Bao cao su"),
        ("bedroom", "Phòng ngủ"),
        ("gym-porn", "Phòng tập"),
        ("stockings", "Vớ dài"),
        ("webcam", "Webcam"),
        ("hairy", "Không cạo"),
        ("flexible", "Dẻo dai"),
        ("cum-swallow", "Nuốt tinh"),
        ("maid", "Người hầu"),
        ("mistress", "Nữ thống trị"),
        ("group-porno", "Quan hệ nhóm"),
        ("dildo", "Dildo"),
        ("long-hair", "Tóc dài"),
        ("doctor", "Bác sĩ"),
        ("amateur", "Tự quay"),
        ("handjob", "Kích thích bằng tay"),
        ("a1-europe", "Châu Âu"),
        ("fun", "Mạnh bạo"),
        ("a1-threesome", "Hai nữ một nam"),
        ("cheating", "Ngoại tình"),
        ("intimate-haircut", "Tạo hình vùng kín"),
        ("gag", "Bịt miệng"),
        ("short-hair", "Tóc ngắn"),
        ("braids", "Tóc tết"),
        ("nice-tits-porn", "Ngực đẹp"),
        ("a1-babe", "Người đẹp"),
        ("ass", "Mông đẹp"),
        ("beautiful", "Quan hệ đẹp"),
        ("closeup", "Cận cảnh"),
        ("cuckold", "Cuckold"),
        ("cunni", "Khẩu giao nữ"),
        ("lesbi-porno", "Đồng tính nữ"),
        ("ass-licking-porn", "Liếm hậu môn"),
        ("massage", "Mát-xa"),
        ("a1-masturbation", "Thủ dâm"),
        ("a1-stepmom", "Mẹ kế"),
        ("nurse", "Y tá"),
        ("tits-fuck", "Giữa ngực"),
        ("interracial", "Khác chủng tộc"),
        ("2man-woman", "Hai nam một nữ"),
        ("blowjob", "Khẩu giao"),
        ("teen", "Người trẻ 18+"),
        ("heels", "Giày cao gót"),
        ("beach", "Bãi biển"),
        ("outdoor-sex", "Ngoài trời"),
        ("a1-public", "Nơi công cộng"),
        ("table", "Trên bàn"),
        ("cowgirl", "Tư thế cưỡi"),
        ("handcuffs", "Còng tay"),
        ("a1-natural-tits", "Ngực tự nhiên"),
        ("black-girl", "Phụ nữ da đen"),
        ("black", "Da đen"),
        ("blacks-on-blondes", "Da đen với tóc vàng"),
        ("ugly-tits", "Ngực nhỏ"),
        ("babysitter", "Bảo mẫu"),
        ("pissing", "Pissing"),
        ("whip", "Roi da"),
        ("underwater", "Dưới nước"),
        ("submission", "Phục tùng"),
        ("69", "Tư thế 69"),
        ("milfs", "Phụ nữ trưởng thành"),
        ("wrestling", "Vật lộn"),
        ("russian-amateur", "Nga tự quay"),
        ("ruporn", "Nga"),
        ("redhead", "Tóc đỏ"),
        ("latina-sex", "Mỹ Latin"),
        ("bride", "Cô dâu"),
        ("couch-porn", "Huấn luyện viên"),
        ("swingers", "Swinger"),
        ("secretary-porn", "Thư ký"),
        ("dorm-porn", "Ký túc xá"),
        ("office-sex", "Văn phòng"),
        ("kitchen", "Nhà bếp"),
        ("exgfs", "Người yêu cũ"),
        ("sex-toys", "Đồ chơi"),
        ("sex-machines", "Máy hỗ trợ"),
        ("slave", "Nô lệ"),
        ("silicone-tits", "Ngực thẩm mỹ"),
        ("squirting", "Squirt"),
        ("a1-solo", "Đơn"),
        ("creampie", "Xuất tinh bên trong"),
        ("cum-on-tits", "Xuất tinh lên ngực"),
        ("facial", "Xuất tinh lên mặt"),
        ("sperma-na-nogah", "Xuất tinh lên chân"),
        ("cum-on-pussy", "Xuất tinh lên vùng kín"),
        ("cum-on-ass", "Xuất tinh lên mông"),
        ("old-and-young", "Già và trẻ"),
        ("strapon", "Strap-on"),
        ("strip", "Thoát y"),
        ("schoolgirls", "Nữ sinh viên"),
        ("students", "Sinh viên"),
        ("styuardessa", "Tiếp viên"),
        ("trah", "Quan hệ"),
        ("teaching", "Hướng dẫn"),
        ("teacher", "Giáo viên"),
        ("teacher-milf", "Cô giáo"),
        ("foot-fetish", "Tôn sùng bàn chân"),
        ("skinny-porn", "Mảnh mai"),
        ("czech-porn", "Séc"),
        ("gloryhole-porn", "Gloryhole"),
        ("erotic", "Gợi cảm"),
    };

    static readonly Dictionary<string, string> CatVi = CatSlugs.ToDictionary(x => x.slug, x => x.title, StringComparer.OrdinalIgnoreCase);

    // slug site in khác slug menu (a1-/b1-) -> vẫn phải ra tiếng Việt, không để tiếng Nga
    static readonly Dictionary<string, string> CatAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a1-russian"] = "Nga",
        ["b1-masturbation"] = "Thủ dâm",
        ["b1-solo"] = "Đơn",
        ["b1-blowjob"] = "Khẩu giao",
        ["b1-teen"] = "Người trẻ 18+"
    };

    #region Uri
    public static string Uri(string host, string search, string sort, string c, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("search/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("/");
        }
        else
        {
            if (!string.IsNullOrEmpty(c))
            {
                // chip popup: "cat/<slug>" | "model/<slug>" | "site|sites/<slug>"
                string kind = "porno";
                string slug = c.Trim('/');

                if (slug.StartsWith("cat/", StringComparison.OrdinalIgnoreCase))
                    slug = slug.Substring(4);
                else if (slug.StartsWith("model/", StringComparison.OrdinalIgnoreCase))
                {
                    kind = "models";
                    slug = slug.Substring(6);
                }
                else if (slug.StartsWith("sites/", StringComparison.OrdinalIgnoreCase))
                {
                    kind = "sites";
                    slug = slug.Substring(6);
                }
                else if (slug.StartsWith("site/", StringComparison.OrdinalIgnoreCase))
                {
                    kind = "site";
                    slug = slug.Substring(5);
                }

                url.Append(kind);
                url.Append("/");
                url.Append(slug.Trim('/'));

                if (kind == "porno" && sort is "porno-online" or "xxx-top")
                    url.Append("-rating");

                url.Append("/");
            }
            else
            {
                if (!string.IsNullOrEmpty(sort))
                {
                    url.Append(sort);
                    url.Append("/");
                }
            }
        }

        if (pg > 1)
        {
            url.Append(pg);
            url.Append("/");
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        var rx = Rx.Split("<div class=\"item\">", html);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            if (!row.Contains("<div class=\"item-info\">"))
                continue;

            string link = row.Match("<a href=\"https?://[^/]+/(video/[^\"]+)\"");
            string title = row.Match("<div class=\"item-title\">([^<]+)</div>");

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(link))
            {
                var img = row.Groups("( )src=\"(([^\"]+)/[0-9]+.jpg)\"");
                if (string.IsNullOrWhiteSpace(img[3].Value) || img[2].Value.Contains("load.png"))
                    img = row.Groups("(data-srcset|data-src|srcset)=\"([^\"]+/[0-9]+.jpg)\"");

                var pl = new PlaylistItem()
                {
                    name = title.Trim(),
                    video = $"{uri}?uri={link}",
                    picture = img[2].Value,
                    time = row.Match(" data-eb=\"([^;\"]+);", trim: true),
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "elo",
                        href = link,
                        image = img[2].Value
                    }
                };

                if (onplaylist != null)
                    pl = onplaylist.Invoke(pl);

                playlists.Add(pl);
            }
        }

        return playlists;
    }
    #endregion

    #region Popup (chips -> /elo/genres?uri= + "Ещё порно видео")
    // Detail (theme ebalovo):
    //   <strong>В этом порно:</strong> ... <a href="/porno/<slug>/">Tên</a>
    //   <span class="video-link link-models"> <strong>Модели:</strong> ... <a href="/models/<slug>/">
    //   <strong>Порно студия:</strong> ... <a href="/site/<slug>/">, <a href="/sites/<slug>/">
    //   "Ещё порно видео:" -> <div class="video-related"> <div id="list_videos_related_videos_items">
    static string PrettySlug(string slug)
    {
        string s = Regex.Replace(slug, "^(a|b)[0-9]+-", "", RegexOptions.IgnoreCase).Replace('-', ' ');
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }

    public static List<(string kind, string slug, string name)> Chips(string html)
    {
        var list = new List<(string, string, string)>();
        if (string.IsNullOrEmpty(html))
            return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void grab(string startMarker, string endMarker, string kind, string pattern)
        {
            int i = html.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                return;

            int j = endMarker == null ? -1 : html.IndexOf(endMarker, i + startMarker.Length, StringComparison.OrdinalIgnoreCase);
            string region = j > i ? html.Substring(i, j - i) : html.Substring(i, Math.Min(4000, html.Length - i));

            foreach (Match m in Regex.Matches(region, pattern, RegexOptions.IgnoreCase))
            {
                string slug = m.Groups[1].Value.Trim().Trim('/');
                if (slug.Length == 0 || slug.Contains("search-categories") || !seen.Add(kind + "/" + slug))
                    continue;

                string name = HttpUtility.HtmlDecode(m.Groups[2].Value).Trim();
                if (name.Length == 0)
                    name = slug.Replace('-', ' ');

                if (kind == "cat")
                {
                    if (CatVi.TryGetValue(slug, out string vi) || CatAlias.TryGetValue(slug, out vi))
                        name = vi;
                    else
                        name = PrettySlug(slug);
                }

                list.Add((kind, slug, name));
            }
        }

        grab("В этом порно:", "</p>", "cat",
            "<a[^>]*href=\"(?:https?://[^\"]+)?/porno/([^\"/]+)/?\"[^>]*>([^<]{1,120})</a>");

        grab("link-models", "Порно студия", "model",
            "<a[^>]*href=\"(?:https?://[^\"]+)?/models/([^\"/]+)/?\"[^>]*>([^<]{1,120})</a\\s*>");

        grab("Порно студия:", "</p>", "site",
            "<a[^>]*href=\"(?:https?://[^\"]+)?/(sites?/[^\"/]+)/?\"[^>]*>([^<]{1,120})</a\\s*>");

        return list;
    }

    public static List<PlaylistItem> PlaylistRelated(string route, string html, string self)
    {
        if (string.IsNullOrEmpty(html))
            return new List<PlaylistItem>();

        int i = html.IndexOf("list_videos_related_videos_items", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            i = html.IndexOf("video-related", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
            return new List<PlaylistItem>();

        var rel = Playlist(route, html.AsSpan(i)) ?? new List<PlaylistItem>();

        if (!string.IsNullOrEmpty(self))
        {
            string selfPath = self.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? (System.Uri.TryCreate(self, UriKind.Absolute, out var u) ? u.AbsolutePath.Trim('/') : self.Trim('/'))
                : self.Trim('/');

            rel = rel.Where(x => x.bookmark?.href == null ||
                !x.bookmark.href.Trim('/').Equals(selfPath, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return rel;
    }

    // Chặn SSRF: chỉ host *.ebalovo.porn|click|pro; uri tương đối "video/<slug>/" ghép host.
    public static string DetailUrl(string host, string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        if (!uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return (host ?? "https://www.ebalovo.porn").TrimEnd('/') + "/" + uri.Trim('/') + "/";

        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return null;

        string h = u.Host.ToLowerInvariant();
        foreach (string d in new[] { "ebalovo.porn", "ebalovo.click", "ebalovo.pro" })
        {
            if (h == d || h.EndsWith("." + d))
                return uri;
        }

        return null;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string sort, string c)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Ebalovo_menu_{host}_{sort}_{c}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        string url = $"{host}/elo";

        menu = new List<MenuItem>(3)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = url,
            },
            new MenuItem()
            {
                title = $"Sắp xếp: {(string.IsNullOrEmpty(sort) ? "Mới nhất" : sort == "porno-online" ? "Hay nhất" : sort == "xxx-top" ? "Phổ biến" : sort)}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(3)
                {
                    new("Mới nhất", $"{url}?c={c}"),
                    new("Hay nhất", $"{url}?c={c}&sort=porno-online"),
                    new("Phổ biến", $"{url}?c={c}&sort=xxx-top")
                }
            }
        };

        var catmenu = new List<MenuItem>(CatSlugs.Length + 1)
        {
            new("Tất cả", $"{url}?sort={sort}")
        };

        foreach (var (slug, title) in CatSlugs)
            catmenu.Add(new(title, $"{url}?sort={sort}&c={slug}"));

        menu.Add(new MenuItem()
        {
            title = $"Danh mục: {catmenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "tất cả"}",
            playlist_url = "submenu",
            submenu = catmenu
        });

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    async public static Task<StreamItem> StreamLinks(HttpHydra http, string uri, string host, string url, Func<string, Task<string>> onlocation = null)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        string stream_link = null;
        List<PlaylistItem> recomends = null;

        await http.GetSpan($"{host}/{url}", html =>
        {
            foreach (string q in new string[] { "video_alt_url", "video_url" })
            {
                stream_link = Rx.Groups(html, $"{q}:([\t ]+)?('|\")(?<link>[^\"']+)")["link"].Value;
                if (!string.IsNullOrEmpty(stream_link))
                    break;
            }

            if (!string.IsNullOrEmpty(stream_link))
                recomends = Playlist(uri, html);
        },
        addheaders: HeadersModel.Init(
            ("sec-fetch-dest", "document"),
            ("sec-fetch-mode", "navigate"),
            ("sec-fetch-site", "same-origin"),
            ("sec-fetch-user", "?1"),
            ("upgrade-insecure-requests", "1")
        ));

        if (string.IsNullOrEmpty(stream_link))
            return null;

        if (onlocation != null)
        {
            string location = await onlocation.Invoke(stream_link);
            if (location == null || stream_link == location || location.Contains("_file/"))
                return null;

            stream_link = location;
        }

        return new StreamItem()
        {
            qualitys = new Dictionary<string, string>()
            {
                ["auto"] = stream_link
            },
            recomends = recomends
        };
    }
    #endregion
}
