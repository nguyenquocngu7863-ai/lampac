using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AdminPanel;

public static class ConfigSectionGroups
{
    public sealed record GroupSpec(string Id, string Title, string Hint, string[] Keys);

    public static readonly GroupSpec[] Catalog =
    {
        new("runtime", "Hệ thống", "Các trường hệ thống; thường lấy từ current, không bắt buộc trong init.",
            new[] { "guid", "freeDiskSpace" }),
        new("listen", "Máy chủ HTTP (listen)", "Địa chỉ, cổng, giao thức, timeout, compression.",
            new[] { "listen" }),
        new("security", "Bảo mật và quyền truy cập", "WAF, accsdb, danh sách module và middleware lõi.",
            new[] { "WAF", "accsdb", "BaseModule" }),
        new("network", "Mạng và proxy", "Proxy cho request đi ra, CORS, mạng tin cậy.",
            new[] { "serverproxy", "proxy", "globalproxy", "corsehost", "KnownProxies" }),

        new("pools", "Pool và hệ thống", "Buffer, APN, kit.",
            new[] { "pool", "apn", "kit" }),
        new("cache-gc", "Cache và bộ nhớ", "Hybrid cache, Staticache, cấu hình GC.",
            new[] { "cache", "Staticache", "GC" }),
        new("media", "Ảnh và poster", "Engine hình ảnh, Poster API.",
            new[] { "imagelibrary", "posterApi" }),

        new("realtime", "WebSocket và RCH", "Socket native và hub từ xa.",
            new[] { "WebSocket", "rch" }),
        new("browser", "Nguồn cần trình duyệt", "Mirage/Phantom cần Chromium; HydraFlix/TwoEmbed cần Firefox. Các nguồn ENG còn lại xem nhóm ENG.", new[] { "chromium", "firefox", "Mirage", "Phantom" }),
        new("diagnostics", "Log và chẩn đoán", "Serilog, xử lý exception, openstat.",
            new[] { "serilog", "useDeveloperExceptionPage", "exceptionHandlerLogTarget", "exceptionHandlerLogFile", "watcherInit", "openstat" }),

        new("app", "Ứng dụng và giao diện", "online, cub, sisi, quảng cáo, mặc định, omdb.",
            new[] { "online", "cub", "sisi", "vast", "disableEng", "defaultOn", "omdbapi_key", "overrideResponse" }),
        new("client", "Client Lampa và API", "Giao diện Lampa, cookie, PidTor, TMDB, phụ đề tự động.", new[] { "tmdb", "LampaWeb", "SubFinder", "Cookie", "PidTor", "gst" }),
        new("local-services", "Dịch vụ cục bộ", "Kết nối Lampac/Lampa với dịch vụ chạy cùng Ubuntu proot.",
            new[] { "Jackett" }),
        new("modules", "Module mở rộng", "Các section module ở cấp cao nhất của config.",
            new[] { "Catalog", "DLNA", "JacRed", "Sync", "TimeCode", "TorrServer", "Tracks", "transcoding", "TmdbProxy", "CubProxy", "WebLog" }),

        new("src-vn", "Nguồn · Việt Nam", "Các nguồn phim Việt tùy biến, không cần Playwright.",
            new[] { "KKPhim", "K20", "VsMov" }),

        new("src-eng", "Nguồn · ENG (10 nguồn gốc)", "Nhóm embed tiếng Anh theo bản gốc: đủ 10 nguồn (Videasy/VidSrc là bản fix riêng). Phần lớn chạy bằng embed/Playwright; cần disableEng: false để hiện trong Lampa.",
            new[] { "Autoembed", "Hydraflix", "MovPI", "Playembed", "Rgshows", "Smashystream", "Twoembed", "VidLink", "Videasy", "Vidsrc" }),

        new("src-http-bridge", "Nguồn · HTTP / Stremio (tùy biến)", "Các nguồn tự viết, chạy không cần Playwright: Stremio bridge (AIOStreams, Sootio, WebStreamr), VidCore (embed 4K, giải mã qua apihost) và MoviesDrive/Movies4U (link HubCloud/Google Drive — chung một resolver, riêng một cấu hình).",
            new[] { "AIOStreams", "Sootio", "WebStreamr", "VidCore", "MoviesDrive", "Movies4U" }),

        new("src-rus", "Nguồn · Nga và CIS", "Nguồn VOD/CDN Nga; Mirage và Phantom nằm ở nhóm cần trình duyệt.",
            new[]
            {
                "CDNvideohub", "Collaps", "FanCDN", "FlixCDN", "HDVB", "Kinobase", "Kinogo", "Kinotochka", "LeProduction",
                "PizdatoeHD", "RutubeMovie", "Spectre", "VeoVeo", "Vibix", "VideoDB", "Videoseed", "VkMovie", "Zetflix", "ZetflixDB"
            }),

        new("src-paid", "Nguồn · cần tài khoản hoặc token", "Các nguồn thường cần token, cookie hoặc tài khoản riêng.",
            new[] { "Alloha", "Filmix", "FilmixPartner", "FilmixTV", "GetsTV", "IptvOnline", "iRemux", "KinoPub", "Rezka", "RezkaPrem", "SakhTV", "VoKino" }),

        new("src-ukr", "Nguồn · Ukraine", "Nguồn phim Ukraine và mirror liên quan.",
            new[] { "Ashdi", "BamBoo", "Eneyida", "HdvbUA", "Kinoukr", "Tortuga", "UAFilm", "UaKino" }),

        new("src-geo", "Nguồn · Georgia và châu Á", "Nguồn theo khu vực Georgia/Asia.",
            new[] { "AsiaGe", "Geosaitebi", "Kinoflix" }),

        new("src-anime", "Nguồn · anime", "Nguồn anime online và liên quan, bao gồm Kodik.",
            new[] { "AiLiberty", "AniLiberty", "AniLibria", "AniMedia", "Animebesst", "AnimeGo", "AnimeLib", "AnimeON", "Animevost", "Dreamerscast", "Kodik", "Mikai", "MoonAnime" }),

        new("src-adult", "Nguồn · SISI / 18+", "Module Adult viết bằng C# và engine NextHUB YAML.",
            new[]
            {
                "NextHUB", "BongaCams", "Chaturbate", "Ebalovo", "Eporner", "HQporner", "PornHub", "PornHubPremium", "Porntrex", "Runetki", "Spankbang", "Tizam",
                "Xhamster", "Xnxx", "Xvideos", "XvideosRED"
            }),
    };

    public static List<GroupDto> Build(JObject currentRoot, IEnumerable<string> nextHubSiteKeys = null)
    {
        if (currentRoot == null)
            currentRoot = new JObject();

        var inFile = new HashSet<string>(
            currentRoot.Properties().Select(p => p.Name),
            StringComparer.Ordinal);

        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GroupDto>();

        foreach (var g in Catalog)
        {
            var keys = g.Keys.Where(inFile.Contains).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            foreach (var k in keys)
                assigned.Add(k);

            if (keys.Length == 0)
                continue;

            result.Add(new GroupDto(g.Id, g.Title, g.Hint, keys));
        }

        var nextHubKeys = (nextHubSiteKeys ?? Array.Empty<string>())
            .Where(inFile.Contains)
            .Where(k => !assigned.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        if (nextHubKeys.Length > 0)
        {
            foreach (var k in nextHubKeys)
                assigned.Add(k);

            result.Add(new GroupDto(
                "src-adult-nexthub",
                "Nguồn · NextHUB / 18+",
                "Thông thường chỉ cần đổi enable. streamproxy đi qua Lampac nhưng không đổi IP; useproxy/useproxystream cần proxy ngoài; rhub/rch và các trường còn lại nên giữ nguyên.",
                nextHubKeys));
        }

        var orphans = inFile.Where(k => !assigned.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (orphans.Length > 0)
            result.Add(new GroupDto("other", "Khác", "Các khóa từ current.conf chưa có trong danh mục (module mới).", orphans));

        return result;
    }

    public static List<GroupDto> BuildCatalog(IEnumerable<string> nextHubSiteKeys = null)
    {
        var list = new List<GroupDto>(Catalog.Length + 1);
        foreach (var g in Catalog)
            list.Add(new GroupDto(g.Id, g.Title, g.Hint, g.Keys.ToArray()));

        var nextHubKeys = (nextHubSiteKeys ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        if (nextHubKeys.Length > 0)
        {
            list.Add(new GroupDto(
                "src-adult-nexthub",
                "Nguồn · NextHUB / 18+",
                "Thông thường chỉ cần đổi enable. streamproxy đi qua Lampac nhưng không đổi IP; useproxy/useproxystream cần proxy ngoài; rhub/rch và các trường còn lại nên giữ nguyên.",
                nextHubKeys));
        }

        return list;
    }

    public static HashSet<string> CatalogRootKeys { get; } = new(
        Catalog.SelectMany(g => g.Keys),
        StringComparer.Ordinal);
}

public sealed record GroupDto(string Id, string Title, string Hint, string[] Keys);
