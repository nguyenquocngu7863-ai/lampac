using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services.HTML;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace PornHub;

public static class PornHubTo
{
    #region Uri
    public static string Uri(string host, string plugin, string search, string model, string sort, int c, string hd, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        char splitkey = '?';

        url.Append(host);
        url.Append("/");

        if (!string.IsNullOrEmpty(search))
        {
            url.Append("video/search?search=");
            url.Append(HttpUtility.UrlEncode(search));
            splitkey = '&';

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append("&o=");
                url.Append(sort);
            }
        }
        else if (!string.IsNullOrEmpty(model))
        {
            if (model.StartsWith("pornstar/"))
            {
                url.Append(model);
                url.Append("/videos/upload");
            }
            else
            {
                url.Append("model/");
                url.Append(model);
                url.Append("/videos");
            }
        }
        else
        {
            switch (plugin ?? "")
            {
                case "phubgay":
                    url.Append("gay/video");
                    break;
                case "phubsml":
                    url.Append("transgender");
                    break;
                default:
                    url.Append("video");
                    break;
            }

            if (!string.IsNullOrEmpty(sort))
            {
                url.Append($"{splitkey}o={sort}");
                splitkey = '&';
            }

            if (!string.IsNullOrEmpty(hd))
            {
                url.Append($"{splitkey}hd={hd}");
                splitkey = '&';
            }

            if (c > 0)
            {
                url.Append($"{splitkey}c={c}");
                splitkey = '&';
            }
        }

        if (pg > 1)
        {
            url.Append(splitkey);
            url.Append("page=");
            url.Append(pg);
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string video_uri, string list_uri, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null, bool related = false, bool prem = false, bool IsModel_page = false)
    {
        if (html.IsEmpty)
            return null;

        var videoCategory = ReadOnlySpan<char>.Empty;

        if (related)
        {
            // список "Связанные", "Рекомендуется" в самом видео
            videoCategory = Rx.Slice(html, "relatedVideosListing", "loadMoreRelatedVideosCenter");
        }
        else if (html.Contains("id=\"videoCategory\"", StringComparison.Ordinal))
        {
            // навигация по категориям https://rt.pornhub.com/video?c=1
            videoCategory = HtmlSpan.Node(html, "*", "id", "videoCategory", HtmlSpanTargetType.Exact);
            if (videoCategory.IsEmpty)
                videoCategory = Rx.Slice(html, "id=\"videoCategory\"", "class=\"reset\"");
        }
        else if (html.Contains("videoList clearfix browseVideo-tabSplit", StringComparison.Ordinal))
        {
            // мобильный интерфейс (нужен для rhub)
            videoCategory = Rx.Slice(html, "videoList clearfix browseVideo-tabSplit", "pageHeader");
        }
        else if (html.Contains("id=\"profileContent\"", StringComparison.Ordinal))
        {
            // видео лысого https://rt.pornhub.com/pornstar/johnny-sins/videos/upload
            videoCategory = Rx.Slice(html, "id=\"profileContent\"", "</section>");
        }
        else
        {
            // поиск ясен хер
            if (html.Contains("id=\"videoSearchResult\"", StringComparison.Ordinal))
            {
                videoCategory = HtmlSpan.Node(html, "*", "id", "videoSearchResult", HtmlSpanTargetType.Exact);
                if (videoCategory.IsEmpty)
                    videoCategory = Rx.Slice(html, "id=\"videoSearchResult\"", "class=\"reset\"");
            }
            else
            {
                // всякая хуйня на smart-tv при включённом rhub
                if (videoCategory.IsEmpty)
                    videoCategory = HtmlSpan.Node(html, "*", "id", "mostRecentVideosSection", HtmlSpanTargetType.Exact);

                if (videoCategory.IsEmpty)
                    videoCategory = HtmlSpan.Node(html, "*", "id", "moreData", HtmlSpanTargetType.Exact);

                if (videoCategory.IsEmpty)
                    videoCategory = HtmlSpan.Node(html, "*", "id", "content-tv-container", HtmlSpanTargetType.Exact);

                if (videoCategory.IsEmpty)
                    videoCategory = HtmlSpan.Node(html, "*", "id", "lazyVids", HtmlSpanTargetType.Exact);
            }
        }

        if (videoCategory.IsEmpty)
            videoCategory = html;

        ModelItem model = null;

        if (IsModel_page)
        {
            string name = Rx.Match(html, "itemprop=\"name\">([\r\n\t ]+)?([^<]+)</h1>", 2);
            string href = Rx.Match(html, "rel=\"canonical\" href=\"(https?://[^/]+)?/model/([^/]+)/", 2);

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(href))
            {
                model = new ModelItem()
                {
                    name = name.Trim(),
                    uri = list_uri + (list_uri.Contains("?") ? "&" : "?") + $"model={href}",
                };
            }
        }

        string splitkey = videoCategory.Contains("pcVideoListItem ", StringComparison.Ordinal)
            ? "pcVideoListItem " : videoCategory.Contains("data-video-segment", StringComparison.Ordinal)
            ? "data-video-segment" : videoCategory.Contains("<li data-id=", StringComparison.Ordinal)
            ? "<li data-id=" : "<li id=";

        var rx = Rx.Split(splitkey, videoCategory, 1);
        if (rx.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(rx.Count);

        foreach (var row in rx.Rows())
        {
            if (row.Contains("brand__badge") || row.Contains("private-vid-title"))
                continue;

            string vkey = row.Match("(-|_)vkey=\"([^\"]+)\"", 2) ?? row.Match("viewkey=([^\"]+)\"");
            if (vkey == null)
                continue;

            string title = row.Match("href=\"/[^\"]+\" title=\"([^\"]+)\"") ?? row.Match("class=\"videoTitle\">([^<]+)<") ?? row.Match("href=\"/view_[^\"]+\" onclick=[^>]+>([^<]+)<");
            if (title == null)
                continue;

            string img = row.Match("data-mediumthumb=\"(https?://[^\"]+)\"") ?? row.Match("<img( [^>]+)? src=\"([^\"]+)\"", 2);
            if (img == null)
                continue;

            if (!IsModel_page)
            {
                model = null;
                var gmodel = row.Groups("href=\"/model/([^\"]+)\"[^>]+>([^<]+)<");
                if (string.IsNullOrEmpty(gmodel[1].Value))
                    gmodel = row.Groups("href=\"/(pornstar/[^\"]+)\"[^>]+>([^<]+)<");

                if (!string.IsNullOrEmpty(gmodel[1].Value))
                {
                    model = new ModelItem()
                    {
                        name = gmodel[2].Value,
                        uri = list_uri + (list_uri.Contains("?") ? "&" : "?") + $"model={gmodel[1].Value}",
                    };
                }
            }

            var pl = new PlaylistItem()
            {
                name = title,
                video = $"{video_uri}?vkey={vkey}",
                model = model,
                picture = img,
                preview = row.Match("data-mediabook=\"(https?://[^\"]+)\"") ?? row.Match("data-webm=\"(https?://[^\"]+)\""),
                time = row.Match("<var class=\"duration\">([^<]+)</var>") ?? row.Match("class=\"time\">([^<]+)<") ?? row.Match("class=\"videoDuration floatLeft\">([^<]+)<") ?? row.Match("time\">([^<]+)<"),
                json = true,
                related = true,
                bookmark = new Bookmark()
                {
                    site = prem ? "phubprem" : "phub",
                    href = vkey,
                    image = img
                }
            };

            if (onplaylist != null)
                pl = onplaylist.Invoke(pl);

            playlists.Add(pl);
        }

        return playlists;
    }
    #endregion

    #region Menu
    public static List<MenuItem> Menu(string host, string plugin, string search, string sort, int c, string hd = null)
    {
        #region getSortName
        string getSortName(string sort, string emptyName)
        {
            if (string.IsNullOrWhiteSpace(sort))
                return emptyName;

            switch (sort)
            {
                case "mr":
                case "cm":
                    return "Mới nhất";

                case "ht":
                    return "Nóng nhất";

                case "vi":
                case "mv":
                    return "Xem nhiều nhất";

                case "ra":
                case "tr":
                    return "Hay nhất";

                default:
                    return emptyName;
            }
        }
        #endregion

        string url = $"{host}/{plugin}";

        #region search menu
        if (!string.IsNullOrEmpty(search))
        {
            string encodesearch = HttpUtility.UrlEncode(search);

            return new List<MenuItem>()
            {
                new MenuItem()
                {
                    title = "Tìm kiếm",
                    search_on = "search_on",
                    playlist_url = url,
                },
                new MenuItem()
                {
                    title = $"Sắp xếp: {getSortName(sort, "Phù hợp nhất")}",
                    playlist_url = "submenu",
                    submenu = new List<MenuItem>(4)
                    {
                        new("Phù hợp nhất", $"{url}?search={encodesearch}"),
                        new("Mới nhất", $"{url}?search={encodesearch}&sort=mr"),
                        new("Tốt nhất", $"{url}?search={encodesearch}&sort=tr"),
                        new("Nhiều lượt xem",$"{url}?search={encodesearch}&sort=mv")
                    }
                }
            };
        }
        #endregion

        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"phub_menu_{host}_{plugin}_{sort}_{c}_{hd}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        menu = new List<MenuItem>(4)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = url,
            },
            new MenuItem()
            {
                title = $"Sắp xếp: {getSortName(sort, "Mới thêm vào yêu thích")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(4)
                {
                    new("Mới thêm vào yêu thích", $"{url}?hd={hd}&c={c}"),
                    new("Mới nhất", $"{url}?hd={hd}&c={c}&sort=cm"),
                    new("Nóng nhất", $"{url}?hd={hd}&c={c}&sort=ht"),
                    new("Tốt nhất", $"{url}?hd={hd}&c={c}&sort=tr")
                }
            }
        };

        if (plugin == "pornhubpremium" || plugin == "phubprem")
        {
            menu.Insert(1, new MenuItem()
            {
                title = $"Chất lượng: {(hd == "2" ? "1080p" : hd == "3" ? "1440p" : hd == "4" ? "2160p" : "tất cả")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(4)
                {
                    new("Tất cả", $"{url}?sort={sort}&c={c}"),
                    new("2160p", $"{url}?sort={sort}&c={c}&hd=4"),
                    new("1440p", $"{url}?sort={sort}&c={c}&hd=3"),
                    new("1080p", $"{url}?sort={sort}&c={c}&hd=2")
                }
            });
        }
        else
        {
            menu.Add(new MenuItem()
            {
                title = $"Xu hướng: {(plugin == "phubgay" ? "Đồng tính nam" : plugin == "phubsml" ? "Chuyển giới" : "Dị tính")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(3)
                {
                    new("Dị tính", $"{host}/phub"),
                    new("Đồng tính nam", $"{host}/phubgay"),
                    new("Chuyển giới", $"{host}/phubsml")
                }
            });
        }

        if (plugin == "phubgay")
        {
            var submenu = new List<MenuItem>(35)
            {
                new("Tất cả", $"{url}?hd={hd}&sort={sort}"),
                new("Châu Á", $"{url}?hd={hd}&sort={sort}&c=48"),
                new("Không bao", $"{url}?hd={hd}&sort={sort}&c=40"),
                new("Dương vật lớn", $"{url}?hd={hd}&sort={sort}&c=58"),
                new("Webcam", $"{url}?hd={hd}&sort={sort}&c=342"),
                new("Gonzo", $"{url}?hd={hd}&sort={sort}&c=372"),
                new("Quan hệ mạnh", $"{url}?hd={hd}&sort={sort}&c=312"),
                new("Thủ dâm", $"{url}?hd={hd}&sort={sort}&c=262"),
                new("Trai đẹp", $"{url}?hd={hd}&sort={sort}&c=70"),
                new("Trưởng thành", $"{url}?hd={hd}&sort={sort}&c=332"),
                new("Thử vai", $"{url}?hd={hd}&sort={sort}&c=362"),
                new("Cơ bắp", $"{url}?hd={hd}&sort={sort}&c=322"),
                new("Sinh viên", $"{url}?hd={hd}&sort={sort}&c=68"),
                new("Xuất tinh", $"{url}?hd={hd}&sort={sort}&c=352"),
                new("Xuất tinh bên trong", $"{url}?hd={hd}&sort={sort}&c=71"),
                new("Mỹ Latin", $"{url}?hd={hd}&sort={sort}&c=50"),
                new("Nghiệp dư", $"{url}?hd={hd}&sort={sort}&c=252"),
                new("Mát-xa", $"{url}?hd={hd}&sort={sort}&c=45"),
                new("Gấu", $"{url}?hd={hd}&sort={sort}&c=66"),
                new("Khác chủng tộc", $"{url}?hd={hd}&sort={sort}&c=64"),
                new("Khẩu giao", $"{url}?hd={hd}&sort={sort}&c=56"),
                new("Trẻ", $"{url}?hd={hd}&sort={sort}&c=49"),
                new("Hoạt hình", $"{url}?hd={hd}&sort={sort}&c=422"),
                new("Cơ bắp", $"{url}?hd={hd}&sort={sort}&c=51"),
                new("Nơi công cộng", $"{url}?hd={hd}&sort={sort}&c=84"),
                new("Không cắt bao", $"{url}?hd={hd}&sort={sort}&c=272"),
                new("Da đen", $"{url}?hd={hd}&sort={sort}&c=44"),
                new("Chân", $"{url}?hd={hd}&sort={sort}&c=412"),
                new("Sugar daddy", $"{url}?hd={hd}&sort={sort}&c=47"),
                new("Đơn", $"{url}?hd={hd}&sort={sort}&c=54"),
                new("Đầy đặn", $"{url}?hd={hd}&sort={sort}&c=392"),
                new("Cổ điển", $"{url}?hd={hd}&sort={sort}&c=77"),
                new("Hình xăm", $"{url}?hd={hd}&sort={sort}&c=552"),
                new("Fetish", $"{url}?hd={hd}&sort={sort}&c=52")
            };

            menu.Add(new MenuItem()
            {
                title = $"Danh mục: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "tất cả"}",
                playlist_url = "submenu",
                submenu = submenu
            });
        }
        else if (plugin == "phub" || plugin == "phubprem")
        {
            var submenu = new List<MenuItem>(90)
            {
                new("Tất cả", $"{url}?hd={hd}&sort={sort}"),
                new("Tuyển chọn nữ", $"{url}?hd={hd}&sort={sort}&c=73"),
                new("Nga", $"{url}?hd={hd}&sort={sort}&c=99"),
                new("Đức", $"{url}?hd={hd}&sort={sort}&c=95"),
                new("60FPS", $"{url}?hd={hd}&sort={sort}&c=105"),
                new("Châu Á", $"{url}?hd={hd}&sort={sort}&c=1"),
                new("Hậu môn", $"{url}?hd={hd}&sort={sort}&c=35"),
                new("Ả Rập", $"{url}?hd={hd}&sort={sort}&c=98"),
                new("BDSM", $"{url}?hd={hd}&sort={sort}&c=10"),
                new("Nhẹ nhàng", $"{url}?hd={hd}&sort={sort}&c=221"),
                new("Lưỡng tính", $"{url}?hd={hd}&sort={sort}&c=76"),
                new("Tóc vàng", $"{url}?hd={hd}&sort={sort}&c=9"),
                new("Ngực lớn", $"{url}?hd={hd}&sort={sort}&c=8"),
                new("Dương vật lớn", $"{url}?hd={hd}&sort={sort}&c=7"),
                new("Brazil", $"{url}?hd={hd}&sort={sort}&c=102"),
                new("Anh", $"{url}?hd={hd}&sort={sort}&c=96"),
                new("Squirt", $"{url}?hd={hd}&sort={sort}&c=69"),
                new("Tóc nâu", $"{url}?hd={hd}&sort={sort}&c=11"),
                new("Bukkake", $"{url}?hd={hd}&sort={sort}&c=14"),
                new("Trường học", $"{url}?hd={hd}&sort={sort}&c=88"),
                new("Webcam", $"{url}?hd={hd}&sort={sort}&c=61"),
                new("Tiệc", $"{url}?hd={hd}&sort={sort}&c=53"),
                new("Gonzo", $"{url}?hd={hd}&sort={sort}&c=41"),
                new("Quan hệ mạnh", $"{url}?hd={hd}&sort={sort}&c=67"),
                new("Nhóm", $"{url}?hd={hd}&sort={sort}&c=80"),
                new("Thâm nhập kép", $"{url}?hd={hd}&sort={sort}&c=72"),
                new("Nữ đơn", $"{url}?hd={hd}&sort={sort}&c=492"),
                new("Thủ dâm", $"{url}?hd={hd}&sort={sort}&c=20"),
                new("Châu Âu", $"{url}?hd={hd}&sort={sort}&c=55"),
                new("Cực khoái nữ", $"{url}?hd={hd}&sort={sort}&c=502"),
                new("Mạnh", $"{url}?hd={hd}&sort={sort}&c=21"),
                new("Hậu trường", $"{url}?hd={hd}&sort={sort}&c=141"),
                new("Ngôi sao", $"{url}?hd={hd}&sort={sort}&c=12"),
                new("Pissing", $"{url}?hd={hd}&sort={sort}&c=211"),
                new("Trưởng thành", $"{url}?hd={hd}&sort={sort}&c=28"),
                new("Đồ chơi", $"{url}?hd={hd}&sort={sort}&c=23"),
                new("Ấn Độ", $"{url}?hd={hd}&sort={sort}&c=101"),
                new("Ý", $"{url}?hd={hd}&sort={sort}&c=97"),
                new("Thử vai", $"{url}?hd={hd}&sort={sort}&c=90"),
                new("Sinh viên", $"{url}?hd={hd}&sort={sort}&c=79"),
                new("Xuất tinh", $"{url}?hd={hd}&sort={sort}&c=16"),
                new("Hàn Quốc", $"{url}?hd={hd}&sort={sort}&c=103"),
                new("Cosplay", $"{url}?hd={hd}&sort={sort}&c=241"),
                new("Người đẹp", $"{url}?hd={hd}&sort={sort}&c=5"),
                new("Xuất tinh bên trong", $"{url}?hd={hd}&sort={sort}&c=15"),
                new("Khẩu giao nữ", $"{url}?hd={hd}&sort={sort}&c=131"),
                new("Hút thuốc", $"{url}?hd={hd}&sort={sort}&c=91"),
                new("Mỹ Latin", $"{url}?hd={hd}&sort={sort}&c=26"),
                new("Nghiệp dư", $"{url}?hd={hd}&sort={sort}&c=3"),
                new("Ngực nhỏ", $"{url}?hd={hd}&sort={sort}&c=59"),
                new("Mẹ", $"{url}?hd={hd}&sort={sort}&c=29"),
                new("Mát-xa", $"{url}?hd={hd}&sort={sort}&c=78"),
                new("Thủ dâm", $"{url}?hd={hd}&sort={sort}&c=22"),
                new("Khác chủng tộc", $"{url}?hd={hd}&sort={sort}&c=25"),
                new("Khẩu giao", $"{url}?hd={hd}&sort={sort}&c=13"),
                new("Lai", $"{url}?hd={hd}&sort={sort}&c=17"),
                new("Hoạt hình", $"{url}?hd={hd}&sort={sort}&c=86"),
                new("Cơ bắp", $"{url}?hd={hd}&sort={sort}&c=512"),
                new("Nơi công cộng", $"{url}?hd={hd}&sort={sort}&c=24"),
                new("Chân", $"{url}?hd={hd}&sort={sort}&c=93"),
                new("Bảo mẫu", $"{url}?hd={hd}&sort={sort}&c=89"),
                new("Nhái", $"{url}?hd={hd}&sort={sort}&c=201"),
                new("Lệch tuổi", $"{url}?hd={hd}&sort={sort}&c=181"),
                new("Tuổi teen", $"{url}?hd={hd}&sort={sort}&c=37"),
                new("Mông", $"{url}?hd={hd}&sort={sort}&c=4"),
                new("Hài", $"{url}?hd={hd}&sort={sort}&c=32"),
                new("Cổ điển", $"{url}?hd={hd}&sort={sort}&c=43"),
                new("Cuckold", $"{url}?hd={hd}&sort={sort}&c=242"),
                new("Nhập vai", $"{url}?hd={hd}&sort={sort}&c=81"),
                new("Lãng mạn", $"{url}?hd={hd}&sort={sort}&c=522"),
                new("Tóc đỏ", $"{url}?hd={hd}&sort={sort}&c=42"),
                new("Ba người", $"{url}?hd={hd}&sort={sort}&c=65"),
                new("Orgy", $"{url}?hd={hd}&sort={sort}&c=2"),
                new("Gia đình giả tưởng", $"{url}?hd={hd}&sort={sort}&c=444"),
                new("Strap-on", $"{url}?hd={hd}&sort={sort}&c=542"),
                new("Thoát y", $"{url}?hd={hd}&sort={sort}&c=33"),
                new("Hình xăm", $"{url}?hd={hd}&sort={sort}&c=562"),
                new("Đầy đặn", $"{url}?hd={hd}&sort={sort}&c=6"),
                new("Chuyển giới", $"{url}?hd={hd}&sort={sort}&c=83"),
                new("Kích thích bằng tay", $"{url}?hd={hd}&sort={sort}&c=592"),
                new("Fetish", $"{url}?hd={hd}&sort={sort}&c=18"),
                new("Fisting", $"{url}?hd={hd}&sort={sort}&c=19"),
                new("Pháp", $"{url}?hd={hd}&sort={sort}&c=94"),
                new("Hentai", $"{url}?hd={hd}&sort={sort}&c=36"),
                new("Séc", $"{url}?hd={hd}&sort={sort}&c=100"),
                new("Nhật Bản", $"{url}?hd={hd}&sort={sort}&c=111")
            };

            menu.Add(new MenuItem()
            {
                title = $"Danh mục: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "tất cả"}",
                playlist_url = "submenu",
                submenu = submenu
            });
        }

        if (CoreInit.conf.lowMemoryMode == false)
            memoryCache.Set(menuKey, menu, TimeSpan.FromDays(1));

        return menu;
    }
    #endregion

    #region StreamLinks
    public static string StreamLinksUri(string host, string vkey)
    {
        if (string.IsNullOrEmpty(vkey))
            return null;

        return $"{host}/view_video.php?viewkey={vkey}";
    }

    public static StreamItem StreamLinks(ReadOnlySpan<char> html, string video_uri, string list_uri)
    {
        if (html.IsEmpty)
            return null;

        var qualitys = new Dictionary<string, string>(4);

        foreach (string q in new string[] { "1080", "720", "480", "240" })
        {
            string video = Rx.Match(html, $"\"videoUrl\":\"([^\"]+)\",\"quality\":\"{q}\"");

            if (!string.IsNullOrEmpty(video) || !video.Contains("validfrom"))
                qualitys.TryAdd($"{q}p", video.Replace("\\", "").Replace("///", "//"));
        }

        if (qualitys.Count == 0)
            return null;

        return new StreamItem()
        {
            qualitys = qualitys,
            recomends = Playlist(video_uri, list_uri, html, related: true)
        };
    }
    #endregion

    #region Pages
    public static int Pages(ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return 0;

        if (!html.Contains("class=\"page_number\"", StringComparison.Ordinal))
            return 1;

        var rx = Rx.Matches("class=\"page_number\"><a [^>]+>([0-9]+)<", html);
        if (rx.Count == 0)
            return 1;

        int maxpage = 0;
        foreach (var row in rx.Rows())
        {
            string page = row.Match("class=\"page_number\"><a [^>]+>([0-9]+)<");
            if (page != null && int.TryParse(page, out int pg) && pg > maxpage)
                maxpage = pg;
        }

        // модель 6, навигация 5
        if (4 >= maxpage)
            return maxpage;

        return 0;
    }
    #endregion

    #region getDirectLinks
    static string getDirectLinks(string pageCode)
    {
        var vars = new List<(string name, string param)>();

        string mainParamBody = Regex.Match(pageCode, "var player_mp4_seek = \"[^\"]+\";[\n\r\t ]+(// var[^\n\r]+[\n\r\t ]+)?([^\n\r]+)").Groups[2].Value;
        mainParamBody = Regex.Replace(mainParamBody, "/\\*.*?\\*/", "");
        mainParamBody = mainParamBody.Replace("\" + \"", "");

        foreach (Match currVar in Regex.Matches(mainParamBody, "var ([^=]+)=([^;]+);"))
            vars.Add((currVar.Groups[1].Value, currVar.Groups[2].Value.Replace("\"", "").Replace(" + ", "")));

        string mediapattern = /*mainParamBody.Contains("var media_4=") && mainParamBody.Contains("var media_5=") ? "var media_(4)=(.*?);" : */"var media_([0-9]+)=(.*?);";
        foreach (Match m in Regex.Matches(mainParamBody, mediapattern, RegexOptions.Singleline))
        {
            string link = "";
            foreach (string curr in m.Groups[2].Value.Replace(" ", "").Split('+'))
            {
                string param = vars.Find(x => x.name == curr).param;
                if (param == null)
                    continue;

                link += param;
            }

            if (link.Contains("urlset/master.m3u8"))
                return link;
        }

        return null;
    }
    #endregion
}
