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
using System.Web;

namespace Ebalovo;

public static class EbalovoTo
{
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
                url.Append("porno/");
                url.Append(c);

                if (sort is "porno-online" or "xxx-top")
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

        var catmenu = new List<MenuItem>(140)
        {
            new("Tất cả", $"{url}?sort={sort}"),
            new("CFNM", $"{url}?sort={sort}&c=cfnm"),
            new("pov", $"{url}?sort={sort}&c=pov"),
            new("Hậu môn", $"{url}?sort={sort}&c=anal-videos"),
            new("Nới rộng hậu môn", $"{url}?sort={sort}&c=gape"),
            new("Nút hậu môn", $"{url}?sort={sort}&c=butt-plug-porn"),
            new("BDSM", $"{url}?sort={sort}&c=bdsm-porn"),
            new("Tóc vàng", $"{url}?sort={sort}&c=blonde"),
            new("Mông lớn", $"{url}?sort={sort}&c=big-ass"),
            new("Ngực lớn", $"{url}?sort={sort}&c=big-tits"),
            new("Dương vật lớn", $"{url}?sort={sort}&c=big-cock"),
            new("Dương vật đen lớn", $"{url}?sort={sort}&c=bbc"),
            new("Trói buộc", $"{url}?sort={sort}&c=bondage"),
            new("Sếp", $"{url}?sort={sort}&c=boss"),
            new("Đã cạo", $"{url}?sort={sort}&c=shaved-pussy"),
            new("Tóc nâu", $"{url}?sort={sort}&c=a1-brunette"),
            new("Bukkake", $"{url}?sort={sort}&c=bukkake"),
            new("Vớ cao", $"{url}?sort={sort}&c=knee-socks"),
            new("Trong club", $"{url}?sort={sort}&c=club"),
            new("Đồ lót đẹp", $"{url}?sort={sort}&c=lingerie"),
            new("Áo thun", $"{url}?sort={sort}&c=shirt"),
            new("Bôi dầu", $"{url}?sort={sort}&c=oiled"),
            new("Trong xe", $"{url}?sort={sort}&c=car-porn"),
            new("Đeo kính", $"{url}?sort={sort}&c=glasses"),
            new("Bao cao su", $"{url}?sort={sort}&c=condom"),
            new("Phòng ngủ", $"{url}?sort={sort}&c=bedroom"),
            new("Phòng tập", $"{url}?sort={sort}&c=gym-porn"),
            new("Vớ dài", $"{url}?sort={sort}&c=stockings"),
            new("Webcam", $"{url}?sort={sort}&c=webcam"),
            new("Không cạo", $"{url}?sort={sort}&c=hairy"),
            new("Dẻo dai", $"{url}?sort={sort}&c=flexible"),
            new("Nuốt tinh", $"{url}?sort={sort}&c=cum-swallow"),
            new("Người hầu", $"{url}?sort={sort}&c=maid"),
            new("Nữ thống trị", $"{url}?sort={sort}&c=mistress"),
            new("Quan hệ nhóm", $"{url}?sort={sort}&c=group-porno"),
            new("Dildo", $"{url}?sort={sort}&c=dildo"),
            new("Tóc dài", $"{url}?sort={sort}&c=long-hair"),
            new("Bác sĩ", $"{url}?sort={sort}&c=doctor"),
            new("Tự quay", $"{url}?sort={sort}&c=amateur"),
            new("Kích thích bằng tay", $"{url}?sort={sort}&c=handjob"),
            new("Châu Âu", $"{url}?sort={sort}&c=a1-europe"),
            new("Mạnh bạo", $"{url}?sort={sort}&c=fun"),
            new("Hai nữ một nam", $"{url}?sort={sort}&c=a1-threesome"),
            new("Ngoại tình", $"{url}?sort={sort}&c=cheating"),
            new("Tạo hình vùng kín", $"{url}?sort={sort}&c=intimate-haircut"),
            new("Bịt miệng", $"{url}?sort={sort}&c=gag"),
            new("Tóc ngắn", $"{url}?sort={sort}&c=short-hair"),
            new("Tóc tết", $"{url}?sort={sort}&c=braids"),
            new("Ngực đẹp", $"{url}?sort={sort}&c=nice-tits-porn"),
            new("Người đẹp", $"{url}?sort={sort}&c=a1-babe"),
            new("Mông đẹp", $"{url}?sort={sort}&c=ass"),
            new("Quan hệ đẹp", $"{url}?sort={sort}&c=beautiful"),
            new("Cận cảnh", $"{url}?sort={sort}&c=closeup"),
            new("Cuckold", $"{url}?sort={sort}&c=cuckold"),
            new("Khẩu giao nữ", $"{url}?sort={sort}&c=cunni"),
            new("Đồng tính nữ", $"{url}?sort={sort}&c=lesbi-porno"),
            new("Liếm hậu môn", $"{url}?sort={sort}&c=ass-licking-porn"),
            new("Mát-xa", $"{url}?sort={sort}&c=massage"),
            new("Thủ dâm", $"{url}?sort={sort}&c=a1-masturbation"),
            new("Mẹ kế", $"{url}?sort={sort}&c=a1-stepmom"),
            new("Y tá", $"{url}?sort={sort}&c=nurse"),
            new("Giữa ngực", $"{url}?sort={sort}&c=tits-fuck"),
            new("Khác chủng tộc", $"{url}?sort={sort}&c=interracial"),
            new("Hai nam một nữ", $"{url}?sort={sort}&c=2man-woman"),
            new("Khẩu giao", $"{url}?sort={sort}&c=blowjob"),
            new("Người trẻ 18+", $"{url}?sort={sort}&c=teen"),
            new("Giày cao gót", $"{url}?sort={sort}&c=heels"),
            new("Bãi biển", $"{url}?sort={sort}&c=beach"),
            new("Ngoài trời", $"{url}?sort={sort}&c=outdoor-sex"),
            new("Nơi công cộng", $"{url}?sort={sort}&c=a1-public"),
            new("Trên bàn", $"{url}?sort={sort}&c=table"),
            new("Tư thế cưỡi", $"{url}?sort={sort}&c=cowgirl"),
            new("Còng tay", $"{url}?sort={sort}&c=handcuffs"),
            new("Ngực tự nhiên", $"{url}?sort={sort}&c=a1-natural-tits"),
            new("Phụ nữ da đen", $"{url}?sort={sort}&c=black-girl"),
            new("Da đen", $"{url}?sort={sort}&c=black"),
            new("Da đen với tóc vàng", $"{url}?sort={sort}&c=blacks-on-blondes"),
            new("Ngực nhỏ", $"{url}?sort={sort}&c=ugly-tits"),
            new("Bảo mẫu", $"{url}?sort={sort}&c=babysitter"),
            new("Pissing", $"{url}?sort={sort}&c=pissing"),
            new("Roi da", $"{url}?sort={sort}&c=whip"),
            new("Dưới nước", $"{url}?sort={sort}&c=underwater"),
            new("Phục tùng", $"{url}?sort={sort}&c=submission"),
            new("Tư thế 69", $"{url}?sort={sort}&c=69"),
            new("Phụ nữ trưởng thành", $"{url}?sort={sort}&c=milfs"),
            new("Vật lộn", $"{url}?sort={sort}&c=wrestling"),
            new("Nga tự quay", $"{url}?sort={sort}&c=russian-amateur"),
            new("Nga", $"{url}?sort={sort}&c=ruporn"),
            new("Tóc đỏ", $"{url}?sort={sort}&c=redhead"),
            new("Mỹ Latin", $"{url}?sort={sort}&c=latina-sex"),
            new("Cô dâu", $"{url}?sort={sort}&c=bride"),
            new("Huấn luyện viên", $"{url}?sort={sort}&c=couch-porn"),
            new("Swinger", $"{url}?sort={sort}&c=swingers"),
            new("Thư ký", $"{url}?sort={sort}&c=secretary-porn"),
            new("Ký túc xá", $"{url}?sort={sort}&c=dorm-porn"),
            new("Văn phòng", $"{url}?sort={sort}&c=office-sex"),
            new("Nhà bếp", $"{url}?sort={sort}&c=kitchen"),
            new("Người yêu cũ", $"{url}?sort={sort}&c=exgfs"),
            new("Đồ chơi", $"{url}?sort={sort}&c=sex-toys"),
            new("Máy hỗ trợ", $"{url}?sort={sort}&c=sex-machines"),
            new("Nô lệ", $"{url}?sort={sort}&c=slave"),
            new("Ngực thẩm mỹ", $"{url}?sort={sort}&c=silicone-tits"),
            new("Squirt", $"{url}?sort={sort}&c=squirting"),
            new("Đơn", $"{url}?sort={sort}&c=a1-solo"),
            new("Xuất tinh bên trong", $"{url}?sort={sort}&c=creampie"),
            new("Xuất tinh lên ngực", $"{url}?sort={sort}&c=cum-on-tits"),
            new("Xuất tinh lên mặt", $"{url}?sort={sort}&c=facial"),
            new("Xuất tinh lên chân", $"{url}?sort={sort}&c=sperma-na-nogah"),
            new("Xuất tinh lên vùng kín", $"{url}?sort={sort}&c=cum-on-pussy"),
            new("Xuất tinh lên mông", $"{url}?sort={sort}&c=cum-on-ass"),
            new("Già và trẻ", $"{url}?sort={sort}&c=old-and-young"),
            new("Strap-on", $"{url}?sort={sort}&c=strapon"),
            new("Thoát y", $"{url}?sort={sort}&c=strip"),
            new("Nữ sinh viên", $"{url}?sort={sort}&c=schoolgirls"),
            new("Sinh viên", $"{url}?sort={sort}&c=students"),
            new("Tiếp viên", $"{url}?sort={sort}&c=styuardessa"),
            new("Quan hệ", $"{url}?sort={sort}&c=trah"),
            new("Hướng dẫn", $"{url}?sort={sort}&c=teaching"),
            new("Giáo viên", $"{url}?sort={sort}&c=teacher"),
            new("Cô giáo", $"{url}?sort={sort}&c=teacher-milf"),
            new("Tôn sùng bàn chân", $"{url}?sort={sort}&c=foot-fetish"),
            new("Mảnh mai", $"{url}?sort={sort}&c=skinny-porn"),
            new("Séc", $"{url}?sort={sort}&c=czech-porn"),
            new("Gloryhole", $"{url}?sort={sort}&c=gloryhole-porn"),
            new("Gợi cảm", $"{url}?sort={sort}&c=erotic")
        };

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
