using Microsoft.Extensions.Caching.Memory;
using Shared;
using Shared.Models.SISI.Base;
using Shared.Models.SISI.OnResult;
using Shared.Services.Hybrid;
using Shared.Services.Pools;
using Shared.Services.RxEnumerate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Xhamster;

public static class XhamsterTo
{
    #region Uri
    public static string Uri(string host, string plugin, string search, string c, string q, string sort, int pg)
    {
        var url = StringBuilderPool.ThreadInstance;

        url.Append(host);

        if (!string.IsNullOrWhiteSpace(search))
        {
            url.Append("/search/");
            url.Append(HttpUtility.UrlEncode(search));
            url.Append("?page=");
            url.Append(pg);
        }
        else
        {
            switch (plugin ?? "")
            {
                case "xmrsml":
                    url.Append("/shemale");
                    break;
                case "xmrgay":
                    url.Append("/gay");
                    break;
                default:
                    break;
            }

            if (!string.IsNullOrEmpty(c))
            {
                url.Append("/categories/");
                url.Append(c);
            }

            if (!string.IsNullOrEmpty(q))
            {
                url.Append("/");
                url.Append(q);
            }

            switch (sort ?? "")
            {
                case "newest":
                    url.Append("/newest");
                    break;
                case "best":
                    url.Append("/best");
                    break;
                default:
                    break;
            }

            if (pg > 0)
            {
                url.Append("/");
                url.Append(pg);
            }
        }

        return url.ToString();
    }
    #endregion

    #region Playlist
    public static List<PlaylistItem> Playlist(string route, ReadOnlySpan<char> html, Func<PlaylistItem, PlaylistItem> onplaylist = null)
    {
        if (html.IsEmpty)
            return null;

        ReadOnlySpan<char> json = Rx.Slice(html, "window.initials=", ";</script>");
        if (json.IsEmpty)
            return null;

        Root root = null;

        try
        {
            root = JsonSerializer.Deserialize<Root>(json, new JsonSerializerOptions
            {
                AllowTrailingCommas = true
            });
        }
        catch { }

        var videos = root?.layoutPage?.videoListProps?.videoThumbProps
            ?? root?.searchResult?.videoThumbProps
            ?? root?.pagesCategoryComponent?.trendingVideoListProps?.videoThumbProps;

        if (videos == null || videos.Count == 0)
            return null;

        var playlists = new List<PlaylistItem>(videos.Count);

        foreach (var video in videos)
        {
            if (!string.IsNullOrEmpty(video.title) && !string.IsNullOrWhiteSpace(video.pageURL))
            {
                int duration = video.duration > 60 ? (video.duration / 60) : 0;

                var pl = new PlaylistItem()
                {
                    name = video.title,
                    video = $"{route}?uri={HttpUtility.UrlEncode(Regex.Replace(video.pageURL, "^https?://[^/]+/", ""))}",
                    picture = video.thumbURL,
                    quality = video.isUHD ? "HD" : null,
                    preview = video.trailerURL ?? video.trailerFallbackUrl,
                    time = duration > 0 ? $"{duration.ToString()}m" : null,
                    json = true,
                    related = true,
                    bookmark = new Bookmark()
                    {
                        site = "xmr",
                        href = video.pageURL,
                        image = video.thumbURL
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
    public static List<MenuItem> Menu(string host, string plugin, string c, string q, string sort)
    {
        var memoryCache = HybridCache.GetMemory();
        string menuKey = $"Xhamster_menu_{host}_{plugin}_{sort}_{c}_{q}";

        if (memoryCache.TryGetValue(menuKey, out List<MenuItem> menu))
            return menu;

        menu = new List<MenuItem>(5)
        {
            new MenuItem()
            {
                title = "Tìm kiếm",
                search_on = "search_on",
                playlist_url = $"{host}/{plugin}"
            },
            new MenuItem()
            {
                title = $"Chất lượng: {(q == "4k" ? "2160p" : "Bất kỳ")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(2)
                {
                    new("Bất kỳ", $"{host}/{plugin}?c={c}&sort={sort}"),
                    new("2160p", $"{host}/{plugin}?c={c}&sort={sort}&q=4k")
                }
            },
            new MenuItem()
            {
                title = $"Sắp xếp: {(sort == "newest" ? "Mới nhất" : sort == "best" ? "Tốt nhất" :"Thịnh hành")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(3)
                {
                    new("Thịnh hành", $"{host}/{plugin}?c={c}&q={q}&sort=trend"),
                    new("Newest", $"{host}/{plugin}?c={c}&q={q}&sort=newest"),
                    new("Best videos", $"{host}/{plugin}?c={c}&q={q}&sort=best")
                }
            },
            new MenuItem()
            {
                title = $"Xu hướng: {(plugin == "xmrgay" ? "Đồng tính nam" : plugin == "xmrsml" ? "Chuyển giới" :"Dị tính")}",
                playlist_url = "submenu",
                submenu = new List<MenuItem>(3)
                {
                    new("Dị tính", $"{host}/xmr"),
                    new("Đồng tính nam", $"{host}/xmrgay"),
                    new("Chuyển giới", $"{host}/xmrsml")
                }
            }
        };

        if (plugin == "xmr")
        {
            var submenu = new List<MenuItem>(75)
            {
                new("Tất cả", $"{host}/{plugin}?sort={sort}&q={q}"),
                new("Russian", $"{host}/{plugin}?sort={sort}&q={q}&c=russian"),
                new("Threesome", $"{host}/{plugin}?sort={sort}&q={q}&c=threesome"),
                new("Asian", $"{host}/{plugin}?sort={sort}&q={q}&c=asian"),
                new("Anal", $"{host}/{plugin}?sort={sort}&q={q}&c=anal"),
                new("Arab", $"{host}/{plugin}?sort={sort}&q={q}&c=arab"),
                new("ASMR", $"{host}/{plugin}?sort={sort}&q={q}&c=asmr"),
                new("Granny", $"{host}/{plugin}?sort={sort}&q={q}&c=granny"),
                new("BDSM", $"{host}/{plugin}?sort={sort}&q={q}&c=bdsm"),
                new("Bisexual", $"{host}/{plugin}?sort={sort}&q={q}&c=bisexual"),
                new("Big Ass", $"{host}/{plugin}?sort={sort}&q={q}&c=big-ass"),
                new("Pawg", $"{host}/{plugin}?sort={sort}&q={q}&c=pawg"),
                new("Big Tits", $"{host}/{plugin}?sort={sort}&q={q}&c=big-tits"),
                new("Big Cock", $"{host}/{plugin}?sort={sort}&q={q}&c=big-cock"),
                new("British", $"{host}/{plugin}?sort={sort}&q={q}&c=british"),
                new("Mature", $"{host}/{plugin}?sort={sort}&q={q}&c=mature"),
                new("Webcam", $"{host}/{plugin}?sort={sort}&q={q}&c=webcam"),
                new("Vintage", $"{host}/{plugin}?sort={sort}&q={q}&c=vintage"),
                new("Hairy", $"{host}/{plugin}?sort={sort}&q={q}&c=hairy"),
                new("CFNM", $"{host}/{plugin}?sort={sort}&q={q}&c=cfnm"),
                new("Group Sex", $"{host}/{plugin}?sort={sort}&q={q}&c=group-sex"),
                new("Gangbang", $"{host}/{plugin}?sort={sort}&q={q}&c=gangbang"),
                new("Dildo", $"{host}/{plugin}?sort={sort}&q={q}&c=dildo"),
                new("Homemade", $"{host}/{plugin}?sort={sort}&q={q}&c=homemade"),
                new("Footjob", $"{host}/{plugin}?sort={sort}&q={q}&c=footjob"),
                new("Femdom", $"{host}/{plugin}?sort={sort}&q={q}&c=femdom"),
                new("SSBBW", $"{host}/{plugin}?sort={sort}&q={q}&c=ssbbw"),
                new("Ass", $"{host}/{plugin}?sort={sort}&q={q}&c=ass"),
                new("Stuck", $"{host}/{plugin}?sort={sort}&q={q}&c=stuck"),
                new("Celebrity", $"{host}/{plugin}?sort={sort}&q={q}&c=celebrity"),
                new("Game", $"{host}/{plugin}?sort={sort}&q={q}&c=game"),
                new("Story", $"{host}/{plugin}?sort={sort}&q={q}&c=story"),
                new("Casting", $"{host}/{plugin}?sort={sort}&q={q}&c=casting"),
                new("Comic", $"{host}/{plugin}?sort={sort}&q={q}&c=comic"),
                new("Cumshot", $"{host}/{plugin}?sort={sort}&q={q}&c=cumshot"),
                new("Creampie", $"{host}/{plugin}?sort={sort}&q={q}&c=creampie"),
                new("Latina", $"{host}/{plugin}?sort={sort}&q={q}&c=latina"),
                new("Lesbian", $"{host}/{plugin}?sort={sort}&q={q}&c=lesbian"),
                new("Eating Pussy", $"{host}/{plugin}?sort={sort}&q={q}&c=eating-pussy"),
                new("Amateur", $"{host}/{plugin}?sort={sort}&q={q}&c=amateur"),
                new("Massage", $"{host}/{plugin}?sort={sort}&q={q}&c=massage"),
                new("Nurse", $"{host}/{plugin}?sort={sort}&q={q}&c=nurse"),
                new("Interracial", $"{host}/{plugin}?sort={sort}&q={q}&c=interracial"),
                new("MILF", $"{host}/{plugin}?sort={sort}&q={q}&c=milf"),
                new("Cute", $"{host}/{plugin}?sort={sort}&q={q}&c=cute"),
                new("Blowjob", $"{host}/{plugin}?sort={sort}&q={q}&c=blowjob"),
                new("Petite", $"{host}/{plugin}?sort={sort}&q={q}&c=petite"),
                new("Missionary", $"{host}/{plugin}?sort={sort}&q={q}&c=missionary"),
                new("Nun", $"{host}/{plugin}?sort={sort}&q={q}&c=nun"),
                new("Cartoon", $"{host}/{plugin}?sort={sort}&q={q}&c=cartoon"),
                new("Black", $"{host}/{plugin}?sort={sort}&q={q}&c=black"),
                new("German", $"{host}/{plugin}?sort={sort}&q={q}&c=german"),
                new("Office", $"{host}/{plugin}?sort={sort}&q={q}&c=office"),
                new("First Time", $"{host}/{plugin}?sort={sort}&q={q}&c=first-time"),
                new("Beach", $"{host}/{plugin}?sort={sort}&q={q}&c=beach"),
                new("Porn For Women", $"{host}/{plugin}?sort={sort}&q={q}&c=porn-for-women"),
                new("Wrestling", $"{host}/{plugin}?sort={sort}&q={q}&c=wrestling"),
                new("Cuckold", $"{host}/{plugin}?sort={sort}&q={q}&c=cuckold"),
                new("Romantic", $"{host}/{plugin}?sort={sort}&q={q}&c=romantic"),
                new("Swingers", $"{host}/{plugin}?sort={sort}&q={q}&c=swingers"),
                new("Squirting", $"{host}/{plugin}?sort={sort}&q={q}&c=squirting"),
                new("Old Man", $"{host}/{plugin}?sort={sort}&q={q}&c=old-man"),
                new("Old Young", $"{host}/{plugin}?sort={sort}&q={q}&c=old-young"),
                new("Teen", $"{host}/{plugin}?sort={sort}&q={q}&c=teen"),
                new("BBW", $"{host}/{plugin}?sort={sort}&q={q}&c=bbw"),
                new("Gym", $"{host}/{plugin}?sort={sort}&q={q}&c=gym"),
                new("Tight Pussy", $"{host}/{plugin}?sort={sort}&q={q}&c=tight-pussy"),
                new("French", $"{host}/{plugin}?sort={sort}&q={q}&c=french"),
                new("Futanari", $"{host}/{plugin}?sort={sort}&q={q}&c=futanari"),
                new("Hardcore", $"{host}/{plugin}?sort={sort}&q={q}&c=hardcore"),
                new("Handjob", $"{host}/{plugin}?sort={sort}&q={q}&c=handjob"),
                new("Hentai", $"{host}/{plugin}?sort={sort}&q={q}&c=hentai"),
                new("Japanese", $"{host}/{plugin}?sort={sort}&q={q}&c=japanese")
            };

            menu.Add(new MenuItem()
            {
                title = $"Danh mục: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "tất cả"}",
                playlist_url = "submenu",
                submenu = submenu
            });
        }
        else if (plugin == "xmrgay")
        {
            var submenu = new List<MenuItem>(50)
            {
                new("Tất cả", $"{host}/{plugin}?sort={sort}&q={q}"),
                new("Russian", $"{host}/{plugin}?sort={sort}&q={q}&c=russian"),
                new("Threesome", $"{host}/{plugin}?sort={sort}&q={q}&c=threesome"),
                new("Asian", $"{host}/{plugin}?sort={sort}&q={q}&c=asian"),
                new("BDSM", $"{host}/{plugin}?sort={sort}&q={q}&c=bdsm"),
                new("Bareback", $"{host}/{plugin}?sort={sort}&q={q}&c=bareback"),
                new("Gaping", $"{host}/{plugin}?sort={sort}&q={q}&c=gaping"),
                new("Big Cock", $"{host}/{plugin}?sort={sort}&q={q}&c=big-cock"),
                new("Bukkake", $"{host}/{plugin}?sort={sort}&q={q}&c=bukkake"),
                new("Webcam", $"{host}/{plugin}?sort={sort}&q={q}&c=webcam"),
                new("Vintage", $"{host}/{plugin}?sort={sort}&q={q}&c=vintage"),
                new("Glory Hole", $"{host}/{plugin}?sort={sort}&q={q}&c=glory-hole"),
                new("Group Sex", $"{host}/{plugin}?sort={sort}&q={q}&c=group-sex"),
                new("Gangbang", $"{host}/{plugin}?sort={sort}&q={q}&c=gangbang"),
                new("Grandpa", $"{host}/{plugin}?sort={sort}&q={q}&c=grandpa"),
                new("Dildo", $"{host}/{plugin}?sort={sort}&q={q}&c=dildo"),
                new("Cum Tribute", $"{host}/{plugin}?sort={sort}&q={q}&c=cum-tribute"),
                new("Cumshot", $"{host}/{plugin}?sort={sort}&q={q}&c=cumshot"),
                new("Hunk", $"{host}/{plugin}?sort={sort}&q={q}&c=hunk"),
                new("Creampie", $"{host}/{plugin}?sort={sort}&q={q}&c=creampie"),
                new("Small Cock", $"{host}/{plugin}?sort={sort}&q={q}&c=small-cock"),
                new("Massage", $"{host}/{plugin}?sort={sort}&q={q}&c=massage"),
                new("Masturbation", $"{host}/{plugin}?sort={sort}&q={q}&c=masturbation"),
                new("Bear", $"{host}/{plugin}?sort={sort}&q={q}&c=bear"),
                new("Interracial", $"{host}/{plugin}?sort={sort}&q={q}&c=interracial"),
                new("Blowjob", $"{host}/{plugin}?sort={sort}&q={q}&c=blowjob"),
                new("Young", $"{host}/{plugin}?sort={sort}&q={q}&c=young"),
                new("Outdoor", $"{host}/{plugin}?sort={sort}&q={q}&c=outdoor"),
                new("Black", $"{host}/{plugin}?sort={sort}&q={q}&c=black"),
                new("Daddy", $"{host}/{plugin}?sort={sort}&q={q}&c=daddy"),
                new("Beach", $"{host}/{plugin}?sort={sort}&q={q}&c=beach"),
                new("Chubby", $"{host}/{plugin}?sort={sort}&q={q}&c=chubby"),
                new("Locker Room", $"{host}/{plugin}?sort={sort}&q={q}&c=locker-room"),
                new("Wrestling", $"{host}/{plugin}?sort={sort}&q={q}&c=wrestling"),
                new("Sex Toy", $"{host}/{plugin}?sort={sort}&q={q}&c=sex-toy"),
                new("Twink", $"{host}/{plugin}?sort={sort}&q={q}&c=twink"),
                new("Solo", $"{host}/{plugin}?sort={sort}&q={q}&c=solo"),
                new("Spanking", $"{host}/{plugin}?sort={sort}&q={q}&c=spanking"),
                new("Old Young", $"{host}/{plugin}?sort={sort}&q={q}&c=old-young"),
                new("Striptease", $"{host}/{plugin}?sort={sort}&q={q}&c=striptease"),
                new("Fat", $"{host}/{plugin}?sort={sort}&q={q}&c=fat"),
                new("Crossdresser", $"{host}/{plugin}?sort={sort}&q={q}&c=crossdresser"),
                new("Fisting", $"{host}/{plugin}?sort={sort}&q={q}&c=fisting"),
                new("Handjob", $"{host}/{plugin}?sort={sort}&q={q}&c=handjob"),
                new("Hentai", $"{host}/{plugin}?sort={sort}&q={q}&c=hentai"),
                new("Emo", $"{host}/{plugin}?sort={sort}&q={q}&c=emo")
            };

            menu.Add(new MenuItem()
            {
                title = $"Danh mục: {submenu.FirstOrDefault(i => i.playlist_url.EndsWith($"&c={c}"))?.title ?? "tất cả"}",
                playlist_url = "submenu",
                submenu = submenu
            });
        }
        else if (plugin == "xmrsml")
        {
            var submenu = new List<MenuItem>(50)
            {
                new("Tất cả", $"{host}/{plugin}?sort={sort}&q={q}"),
                new("Russian", $"{host}/{plugin}?sort={sort}&q={q}&c=russian"),
                new("Cuckold", $"{host}/{plugin}?sort={sort}&q={q}&c=cuckold"),
                new("Asian", $"{host}/{plugin}?sort={sort}&q={q}&c=asian"),
                new("BDSM", $"{host}/{plugin}?sort={sort}&q={q}&c=bdsm"),
                new("Bareback", $"{host}/{plugin}?sort={sort}&q={q}&c=bareback"),
                new("Blonde", $"{host}/{plugin}?sort={sort}&q={q}&c=blonde"),
                new("Big Ass", $"{host}/{plugin}?sort={sort}&q={q}&c=big-ass"),
                new("Big Cock", $"{host}/{plugin}?sort={sort}&q={q}&c=big-cock"),
                new("Webcam", $"{host}/{plugin}?sort={sort}&q={q}&c=webcam"),
                new("Vintage", $"{host}/{plugin}?sort={sort}&q={q}&c=vintage"),
                new("Group Sex", $"{host}/{plugin}?sort={sort}&q={q}&c=group-sex"),
                new("Gangbang", $"{host}/{plugin}?sort={sort}&q={q}&c=gangbang"),
                new("Homemade", $"{host}/{plugin}?sort={sort}&q={q}&c=homemade"),
                new("Pissing", $"{host}/{plugin}?sort={sort}&q={q}&c=pissing"),
                new("Creampie", $"{host}/{plugin}?sort={sort}&q={q}&c=creampie"),
                new("Latex", $"{host}/{plugin}?sort={sort}&q={q}&c=latex"),
                new("Latina", $"{host}/{plugin}?sort={sort}&q={q}&c=latina"),
                new("Ladyboy", $"{host}/{plugin}?sort={sort}&q={q}&c=ladyboy"),
                new("Trap", $"{host}/{plugin}?sort={sort}&q={q}&c=trap"),
                new("Amateur", $"{host}/{plugin}?sort={sort}&q={q}&c=amateur"),
                new("Small Tits", $"{host}/{plugin}?sort={sort}&q={q}&c=small-tits"),
                new("Masturbation", $"{host}/{plugin}?sort={sort}&q={q}&c=masturbation"),
                new("Interracial", $"{host}/{plugin}?sort={sort}&q={q}&c=interracial"),
                new("Blowjob", $"{host}/{plugin}?sort={sort}&q={q}&c=blowjob"),
                new("Petite", $"{host}/{plugin}?sort={sort}&q={q}&c=petite"),
                new("Outdoor", $"{host}/{plugin}?sort={sort}&q={q}&c=outdoor"),
                new("Lingerie", $"{host}/{plugin}?sort={sort}&q={q}&c=lingerie"),
                new("POV", $"{host}/{plugin}?sort={sort}&q={q}&c=pov"),
                new("Guy Fucks Shemale", $"{host}/{plugin}?sort={sort}&q={q}&c=guy-fucks-shemale"),
                new("Teen", $"{host}/{plugin}?sort={sort}&q={q}&c=teen"),
                new("Redhead", $"{host}/{plugin}?sort={sort}&q={q}&c=redhead"),
                new("Threesome", $"{host}/{plugin}?sort={sort}&q={q}&c=threesome"),
                new("Sex Toy", $"{host}/{plugin}?sort={sort}&q={q}&c=sex-toy"),
                new("Solo", $"{host}/{plugin}?sort={sort}&q={q}&c=solo"),
                new("Tattoo", $"{host}/{plugin}?sort={sort}&q={q}&c=tattoo"),
                new("BBW", $"{host}/{plugin}?sort={sort}&q={q}&c=bbw"),
                new("Shemale Fucks Girl", $"{host}/{plugin}?sort={sort}&q={q}&c=shemale-fucks-girl"),
                new("Shemale Fucks Guy", $"{host}/{plugin}?sort={sort}&q={q}&c=shemale-fucks-guy"),
                new("Shemale Fucks Shemale", $"{host}/{plugin}?sort={sort}&q={q}&c=shemale-fucks-shemale"),
                new("Transgender", $"{host}/{plugin}?sort={sort}&q={q}&c=transgender"),
                new("Fetish", $"{host}/{plugin}?sort={sort}&q={q}&c=fetish"),
                new("Hardcore", $"{host}/{plugin}?sort={sort}&q={q}&c=hardcore"),
                new("Handjob", $"{host}/{plugin}?sort={sort}&q={q}&c=handjob"),
                new("Hentai", $"{host}/{plugin}?sort={sort}&q={q}&c=hentai"),
                new("Pretty", $"{host}/{plugin}?sort={sort}&q={q}&c=pretty"),
                new("Black", $"{host}/{plugin}?sort={sort}&q={q}&c=black"),
                new("Stockings", $"{host}/{plugin}?sort={sort}&q={q}&c=stockings"),
                new("Japanese", $"{host}/{plugin}?sort={sort}&q={q}&c=japanese")
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
    public static string StreamLinksUri(string host, string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        return $"{host}/{url}";
    }

    public static StreamItem StreamLinks(string host, string route, ReadOnlySpan<char> html)
    {
        if (html.IsEmpty)
            return null;

        string stream_link = Rx.Match(html, "rel=\"preload\" href=\"([^\"]+)\"");
        if (stream_link == null || !stream_link.Contains(".m3u"))
            return null;

        return new StreamItem()
        {
            qualitys = new Dictionary<string, string>()
            {
                ["auto"] = stream_link.StartsWith("/")
                    ? host + stream_link.Replace("\\", "")
                    : stream_link.Replace("\\", "")
            },
            recomends = Playlist(route, html)
        };
    }
    #endregion
}
